"""Site Payment Ledger Engine for Clinical Investigator Grants and Real-Time Event Triggers.

Owns: Investigator grant budget contract lifecycle, fee schedule procedure rate calculations,
multi-party split payee allocations, real-time EDC/SDV event ingestion, payable state transitions,
and 10% holdback retention reserve escrow and release governance.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from __future__ import annotations

import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.payments.domain.exceptions import (
    ContractNotFoundError,
    PayableNotFoundError,
    PaymentsValidationError,
)
from apps.payments.domain.models import (
    AuditAction,
    ContractStatus,
    HoldbackStatus,
    PayableStatus,
    SplitAllocationCalculation,
    TriggerType,
    VisitCalculationBreakdown,
)
from apps.payments.infrastructure.models import (
    BudgetContractModel,
    FeeScheduleModel,
    HoldbackReserveModel,
    PayableAllocationModel,
    PayableModel,
    PaymentAuditLogModel,
    SplitPayeeConfigModel,
)
from apps.payments.presentation.dtos import SplitPayeeConfigItem
from apps.payments.services.audit import (
    list_payment_audit_logs,
    record_payment_audit_log,
)


class SitePaymentLedgerEngine:
    """Deep domain engine for investigator site grant contracts, event triggers, and payable ledgers."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    # ==========================================
    # 1. Budget Contracts & Fee Schedules
    # ==========================================

    async def create_budget_contract(
        self,
        study_id: str,
        site_id: str,
        contract_name: str,
        tier_level: str = "TIER_1_ACADEMIC",
        currency: str = "USD",
        total_budget: float = 0.0,
        overhead_percentage: float = 0.0,
        withholding_percentage: float = 10.0,
        effective_date: str | None = None,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> BudgetContractModel:
        """Creates or versions an active investigator grant contract for a study site."""
        existing = await self.get_active_contract(study_id, site_id)
        version_index = 1
        if existing:
            version_index = existing.version_index + 1
            existing.status = ContractStatus.SUPERSEDED.value
            existing.is_active = False
            self.session.add(existing)
            await self.session.commit()
            await self.session.refresh(existing)

        contract = BudgetContractModel(
            id=str(uuid.uuid4()),
            study_id=study_id,
            site_id=site_id,
            contract_name=contract_name,
            tier_level=tier_level,
            currency=currency,
            total_budget=total_budget,
            overhead_percentage=overhead_percentage,
            withholding_percentage=withholding_percentage,
            effective_date=effective_date or datetime.now(UTC).strftime("%Y-%m-%d"),
            status=ContractStatus.ACTIVE.value,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change or "Executed site budget contract",
            version_index=version_index,
        )
        self.session.add(contract)
        await self.session.commit()
        await self.session.refresh(contract)

        await record_payment_audit_log(
            session=self.session,
            entity_type="CONTRACT",
            entity_id=contract.id,
            action=AuditAction.CONTRACT_CREATED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "id": contract.id,
                "study_id": contract.study_id,
                "site_id": contract.site_id,
                "contract_name": contract.contract_name,
                "tier_level": contract.tier_level,
                "total_budget": contract.total_budget,
                "overhead_percentage": contract.overhead_percentage,
                "withholding_percentage": contract.withholding_percentage,
                "version_index": contract.version_index,
            },
            details=f"Created budget contract '{contract.contract_name}' for site '{site_id}' in study '{study_id}'.",
            reason_for_change=reason_for_change or "Initial contract execution",
            version_index=contract.version_index,
        )
        return contract

    async def get_budget_contract(self, contract_id: str) -> BudgetContractModel | None:
        """Retrieves a budget contract by primary key with relations eagerly loaded."""
        stmt = (
            select(BudgetContractModel)
            .where(BudgetContractModel.id == contract_id)
            .options(
                selectinload(BudgetContractModel.fee_schedules),
                selectinload(BudgetContractModel.split_payees),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_active_contract(
        self, study_id: str, site_id: str
    ) -> BudgetContractModel | None:
        """Retrieves active budget contract for a study and site."""
        stmt = (
            select(BudgetContractModel)
            .where(
                BudgetContractModel.study_id == study_id,
                BudgetContractModel.site_id == site_id,
                BudgetContractModel.is_active.is_(True),
                BudgetContractModel.status == "ACTIVE",
            )
            .options(
                selectinload(BudgetContractModel.fee_schedules),
                selectinload(BudgetContractModel.split_payees),
            )
            .order_by(desc(BudgetContractModel.version_index))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_budget_contracts(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[BudgetContractModel]:
        """Lists budget contracts with optional filtering."""
        stmt = select(BudgetContractModel).options(
            selectinload(BudgetContractModel.fee_schedules),
            selectinload(BudgetContractModel.split_payees),
        )
        if study_id:
            stmt = stmt.where(BudgetContractModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(BudgetContractModel.site_id == site_id)
        if is_active_only:
            stmt = stmt.where(BudgetContractModel.is_active.is_(True))
        stmt = stmt.order_by(desc(BudgetContractModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def add_fee_schedule(
        self,
        contract_id: str,
        visit_code: str,
        procedure_code: str,
        procedure_name: str,
        base_amount: float,
        activity_id: str | None = None,
        form_id: str | None = None,
        overhead_percentage: float | None = None,
        withholding_percentage: float | None = None,
        patient_stipend: float = 0.0,
        travel_reimbursement: float = 0.0,
        requires_sdv: bool = False,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> FeeScheduleModel:
        """Adds a procedure fee schedule item to an active budget contract."""
        contract = await self.get_budget_contract(contract_id)
        if not contract:
            raise ContractNotFoundError(f"Contract with ID '{contract_id}' not found.")

        final_overhead = (
            overhead_percentage
            if overhead_percentage is not None
            else contract.overhead_percentage
        )
        final_withholding = (
            withholding_percentage
            if withholding_percentage is not None
            else contract.withholding_percentage
        )

        fee_schedule = FeeScheduleModel(
            id=str(uuid.uuid4()),
            contract_id=contract_id,
            visit_code=visit_code,
            activity_id=activity_id,
            form_id=form_id,
            procedure_code=procedure_code,
            procedure_name=procedure_name,
            base_amount=base_amount,
            overhead_percentage=final_overhead,
            withholding_percentage=final_withholding,
            patient_stipend=patient_stipend,
            travel_reimbursement=travel_reimbursement,
            requires_sdv=requires_sdv,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change or "Added procedure fee schedule item",
            version_index=contract.version_index,
        )
        self.session.add(fee_schedule)
        await self.session.commit()
        await self.session.refresh(fee_schedule)

        await record_payment_audit_log(
            session=self.session,
            entity_type="FEE_SCHEDULE",
            entity_id=fee_schedule.id,
            action=AuditAction.FEE_SCHEDULE_ADDED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "id": fee_schedule.id,
                "contract_id": fee_schedule.contract_id,
                "visit_code": fee_schedule.visit_code,
                "procedure_code": fee_schedule.procedure_code,
                "base_amount": fee_schedule.base_amount,
                "requires_sdv": fee_schedule.requires_sdv,
            },
            details=f"Added fee schedule for procedure '{procedure_code}' ({procedure_name}) on visit '{visit_code}'.",
            reason_for_change=reason_for_change or "Fee schedule setup",
            version_index=contract.version_index,
        )
        return fee_schedule

    async def list_fee_schedules(
        self,
        contract_id: str,
        visit_code: str | None = None,
        form_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[FeeScheduleModel]:
        """Lists procedure fee schedules for a contract."""
        stmt = select(FeeScheduleModel).where(
            FeeScheduleModel.contract_id == contract_id
        )
        if visit_code:
            stmt = stmt.where(FeeScheduleModel.visit_code == visit_code)
        if form_id:
            stmt = stmt.where(FeeScheduleModel.form_id == form_id)
        if is_active_only:
            stmt = stmt.where(FeeScheduleModel.is_active.is_(True))
        stmt = stmt.order_by(FeeScheduleModel.procedure_code)
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # 2. Split Payee Configuration
    # ==========================================

    async def configure_split_payees(
        self,
        contract_id: str,
        payees: list[SplitPayeeConfigItem],
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> list[SplitPayeeConfigModel]:
        """Configures multi-party split payee disbursement shares (must sum to 100%)."""
        contract = await self.get_budget_contract(contract_id)
        if not contract:
            raise ContractNotFoundError(f"Contract with ID '{contract_id}' not found.")

        total_percentage = sum(p.allocation_percentage for p in payees)
        if abs(total_percentage - 100.0) > 0.01:
            raise PaymentsValidationError(
                f"Split payee allocations must sum exactly to 100.0% (current sum: {total_percentage:.2f}%)."
            )

        existing_payees = await self.list_split_payees(
            contract_id=contract_id, is_active_only=True
        )
        for ep in existing_payees:
            ep.is_active = False
            self.session.add(ep)
        if existing_payees:
            await self.session.commit()

        new_entities: list[SplitPayeeConfigModel] = []
        for p in payees:
            entity = SplitPayeeConfigModel(
                id=str(uuid.uuid4()),
                contract_id=contract_id,
                payee_id=p.payee_id,
                payee_name=p.payee_name,
                payee_role=p.payee_role,
                allocation_percentage=p.allocation_percentage,
                bank_account_reference=p.bank_account_reference,
                tax_identifier=p.tax_identifier,
                is_active=True,
                created_by=user_id,
                reason_for_change=reason_for_change or "Configured split payee shares",
                version_index=contract.version_index,
            )
            self.session.add(entity)
            new_entities.append(entity)

        await self.session.commit()
        for ne in new_entities:
            await self.session.refresh(ne)

        await record_payment_audit_log(
            session=self.session,
            entity_type="SPLIT_PAYEE",
            entity_id=contract_id,
            action=AuditAction.SPLIT_PAYEE_CONFIGURED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "contract_id": contract_id,
                "payees": [
                    {
                        "payee_id": sp.payee_id,
                        "payee_name": sp.payee_name,
                        "payee_role": sp.payee_role,
                        "percentage": sp.allocation_percentage,
                    }
                    for sp in new_entities
                ],
            },
            details=f"Configured {len(new_entities)} split payees for contract '{contract_id}'.",
            reason_for_change=reason_for_change or "Split payee governance setup",
            version_index=contract.version_index,
        )
        return new_entities

    async def list_split_payees(
        self, contract_id: str, is_active_only: bool = True
    ) -> list[SplitPayeeConfigModel]:
        """Lists configured split payees for a contract."""
        stmt = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == contract_id
        )
        if is_active_only:
            stmt = stmt.where(SplitPayeeConfigModel.is_active.is_(True))
        stmt = stmt.order_by(desc(SplitPayeeConfigModel.allocation_percentage))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # 3. Calculation Breakdown Helper
    # ==========================================

    def calculate_procedure_breakdown(
        self,
        fee_schedule: FeeScheduleModel,
        contract: BudgetContractModel,
        split_payees: list[SplitPayeeConfigModel],
    ) -> VisitCalculationBreakdown:
        """Performs deterministic financial calculation for a fee schedule procedure."""
        base_amt = fee_schedule.base_amount
        overhead_pct = fee_schedule.overhead_percentage
        overhead_amt = round(base_amt * (overhead_pct / 100.0), 2)
        patient_stipend = fee_schedule.patient_stipend
        travel_reimb = fee_schedule.travel_reimbursement

        gross_amt = round(base_amt + overhead_amt + patient_stipend + travel_reimb, 2)
        withholding_rate = fee_schedule.withholding_percentage
        withholding_amt = round(gross_amt * (withholding_rate / 100.0), 2)
        net_amt = round(gross_amt - withholding_amt, 2)

        allocations: list[SplitAllocationCalculation] = []
        if split_payees:
            for sp in split_payees:
                pct = sp.allocation_percentage
                g_share = round(gross_amt * (pct / 100.0), 2)
                w_share = round(withholding_amt * (pct / 100.0), 2)
                n_share = round(net_amt * (pct / 100.0), 2)
                allocations.append(
                    SplitAllocationCalculation(
                        payee_id=sp.payee_id,
                        payee_name=sp.payee_name,
                        payee_role=sp.payee_role,
                        allocation_percentage=pct,
                        gross_share=g_share,
                        withholding_share=w_share,
                        net_share=n_share,
                        currency=contract.currency,
                    )
                )
        else:
            allocations.append(
                SplitAllocationCalculation(
                    payee_id=f"PAYEE-{contract.site_id}",
                    payee_name=f"Site {contract.site_id} Research Account",
                    payee_role="INSTITUTION",
                    allocation_percentage=100.0,
                    gross_share=gross_amt,
                    withholding_share=withholding_amt,
                    net_share=net_amt,
                    currency=contract.currency,
                )
            )

        return VisitCalculationBreakdown(
            visit_code=fee_schedule.visit_code,
            procedure_code=fee_schedule.procedure_code,
            procedure_name=fee_schedule.procedure_name,
            base_amount=base_amt,
            overhead_percentage=overhead_pct,
            overhead_amount=overhead_amt,
            patient_stipend=patient_stipend,
            travel_reimbursement=travel_reimb,
            gross_amount=gross_amt,
            withholding_rate=withholding_rate,
            withholding_amount=withholding_amt,
            net_amount=net_amt,
            currency=contract.currency,
            requires_sdv=fee_schedule.requires_sdv,
            is_sdv_gated=fee_schedule.requires_sdv,
            allocations=allocations,
            contract_id=contract.id,
            version_index=contract.version_index,
        )

    # ==========================================
    # 4. Real-Time Event Processing Engine
    # ==========================================

    async def _get_payable_by_event_id(self, event_id: str) -> PayableModel | None:
        stmt = (
            select(PayableModel)
            .where(PayableModel.trigger_event_id == event_id)
            .options(
                selectinload(PayableModel.allocations),
                selectinload(PayableModel.holdback_reserve),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def process_ecrf_submission_event(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        visit_code: str,
        form_id: str,
        event_id: str,
        timestamp: datetime | None = None,
        actor_id: str = "edc_system",
        user_id: str = "system",
        user_roles: str = "EDC System",
        reason_for_change: str | None = None,
    ) -> list[PayableModel]:
        """Processes real-time eCRF completion events into payable ledger items."""
        existing_payable = await self._get_payable_by_event_id(event_id)
        if existing_payable:
            return [existing_payable]

        contract = await self.get_active_contract(study_id, site_id)
        if not contract:
            raise ContractNotFoundError(
                f"No active budget contract found for study '{study_id}' and site '{site_id}'."
            )

        fee_schedules = await self.list_fee_schedules(
            contract_id=contract.id,
            visit_code=visit_code,
            form_id=form_id,
            is_active_only=True,
        )
        if not fee_schedules:
            fee_schedules = await self.list_fee_schedules(
                contract_id=contract.id,
                visit_code=visit_code,
                is_active_only=True,
            )

        if not fee_schedules:
            raise PaymentsValidationError(
                f"No active fee schedule matching visit '{visit_code}' and form '{form_id}' for contract '{contract.id}'."
            )

        split_payees = await self.list_split_payees(
            contract_id=contract.id, is_active_only=True
        )

        generated_payables: list[PayableModel] = []

        for idx, fs in enumerate(fee_schedules):
            sub_event_id = (
                event_id if len(fee_schedules) == 1 else f"{event_id}-{idx + 1}"
            )

            existing_sub = await self._get_payable_by_event_id(sub_event_id)
            if existing_sub:
                generated_payables.append(existing_sub)
                continue

            breakdown = self.calculate_procedure_breakdown(
                fee_schedule=fs, contract=contract, split_payees=split_payees
            )

            is_gated = fs.requires_sdv
            initial_status = (
                PayableStatus.PENDING_SDV.value
                if is_gated
                else PayableStatus.TRIGGERED.value
            )

            payable_id = str(uuid.uuid4())
            payable = PayableModel(
                id=payable_id,
                study_id=study_id,
                site_id=site_id,
                contract_id=contract.id,
                subject_id=subject_id,
                visit_code=visit_code,
                form_id=form_id,
                procedure_code=fs.procedure_code,
                procedure_name=fs.procedure_name,
                trigger_type=TriggerType.ECRF_SUBMISSION.value,
                trigger_event_id=sub_event_id,
                trigger_source="EDC_SERVICE",
                base_amount=breakdown.base_amount,
                overhead_amount=breakdown.overhead_amount,
                patient_stipend_amount=breakdown.patient_stipend,
                travel_reimbursement_amount=breakdown.travel_reimbursement,
                gross_amount=breakdown.gross_amount,
                withholding_rate=breakdown.withholding_rate,
                withholding_amount=breakdown.withholding_amount,
                net_amount=breakdown.net_amount,
                currency=breakdown.currency,
                status=initial_status,
                is_sdv_gated=is_gated,
                created_by=actor_id,
                reason_for_change=reason_for_change
                or f"Triggered from eCRF form submission '{form_id}' on visit '{visit_code}'",
                version_index=contract.version_index,
            )

            alloc_models: list[PayableAllocationModel] = []
            for alloc_calc in breakdown.allocations:
                alloc_model = PayableAllocationModel(
                    id=str(uuid.uuid4()),
                    payable_id=payable_id,
                    payee_id=alloc_calc.payee_id,
                    payee_name=alloc_calc.payee_name,
                    payee_role=alloc_calc.payee_role,
                    allocation_percentage=alloc_calc.allocation_percentage,
                    gross_share=alloc_calc.gross_share,
                    withholding_share=alloc_calc.withholding_share,
                    net_share=alloc_calc.net_share,
                    currency=alloc_calc.currency,
                    status="ALLOCATED",
                    created_by=actor_id,
                    reason_for_change="Split payee allocation",
                    version_index=contract.version_index,
                )
                alloc_models.append(alloc_model)

            holdback = HoldbackReserveModel(
                id=str(uuid.uuid4()),
                study_id=study_id,
                site_id=site_id,
                contract_id=contract.id,
                payable_id=payable_id,
                holdback_amount=breakdown.withholding_amount,
                currency=breakdown.currency,
                status=HoldbackStatus.HELD.value,
                created_by=actor_id,
                reason_for_change=f"10% holdback reserve retained for payable {payable_id}",
                version_index=contract.version_index,
            )

            payable.allocations = alloc_models
            payable.holdback_reserve = holdback
            self.session.add(payable)
            await self.session.commit()
            await self.session.refresh(payable)

            audit_action = (
                AuditAction.PAYABLE_GATED_PENDING_SDV.value
                if is_gated
                else AuditAction.PAYABLE_TRIGGERED_ECRF.value
            )

            await record_payment_audit_log(
                session=self.session,
                entity_type="PAYABLE",
                entity_id=payable.id,
                action=audit_action,
                user_id=user_id,
                user_roles=user_roles,
                new_state={
                    "payable_id": payable.id,
                    "subject_id": subject_id,
                    "visit_code": visit_code,
                    "form_id": form_id,
                    "gross_amount": payable.gross_amount,
                    "withholding_amount": payable.withholding_amount,
                    "net_amount": payable.net_amount,
                    "status": payable.status,
                    "is_sdv_gated": payable.is_sdv_gated,
                },
                details=(
                    f"Payable {payable.id} created in '{initial_status}' status "
                    f"from EDC trigger '{event_id}' (Gross: ${payable.gross_amount:.2f}, "
                    f"Holdback: ${payable.withholding_amount:.2f}, Net: ${payable.net_amount:.2f})."
                ),
                reason_for_change=reason_for_change
                or "Automated EDC Form Payable Trigger",
                version_index=contract.version_index,
            )

            generated_payables.append(payable)

        return generated_payables

    async def process_cra_sdv_verification_event(
        self,
        study_id: str,
        site_id: str,
        subject_id: str,
        visit_code: str,
        verified_by: str,
        event_id: str,
        form_id: str | None = None,
        timestamp: datetime | None = None,
        user_id: str = "system",
        user_roles: str = "CRA Monitor",
        reason_for_change: str | None = None,
    ) -> PayableModel | None:
        """Processes CRA SDV verification events, unlocking gated payables."""
        payables = await self.list_payables(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_code=visit_code,
            form_id=form_id,
            status=PayableStatus.PENDING_SDV.value,
        )

        dt_verified = timestamp or datetime.now(UTC)

        if payables:
            target_payable = payables[0]
            prev_state = {
                "status": target_payable.status,
                "is_sdv_gated": target_payable.is_sdv_gated,
            }
            target_payable.status = PayableStatus.TRIGGERED.value
            target_payable.is_sdv_gated = False
            target_payable.sdv_verified_at = dt_verified
            target_payable.sdv_verified_by = verified_by
            target_payable.reason_for_change = (
                reason_for_change
                or f"Unlocked payable via CRA SDV verification by {verified_by}"
            )
            self.session.add(target_payable)
            await self.session.commit()
            await self.session.refresh(target_payable)

            await record_payment_audit_log(
                session=self.session,
                entity_type="PAYABLE",
                entity_id=target_payable.id,
                action=AuditAction.SDV_VERIFIED_UNLOCKED.value,
                user_id=user_id,
                user_roles=user_roles,
                previous_state=prev_state,
                new_state={
                    "status": target_payable.status,
                    "is_sdv_gated": target_payable.is_sdv_gated,
                    "sdv_verified_at": str(dt_verified),
                    "sdv_verified_by": verified_by,
                },
                details=f"Payable {target_payable.id} unlocked from PENDING_SDV to TRIGGERED following CRA SDV sign-off by '{verified_by}'.",
                reason_for_change=reason_for_change
                or "CRA SDV Sign-off Verification Gate",
                version_index=target_payable.version_index,
            )
            return await self.get_payable(target_payable.id)

        contract = await self.get_active_contract(study_id, site_id)
        if not contract:
            return None

        fee_schedules = await self.list_fee_schedules(
            contract_id=contract.id,
            visit_code=visit_code,
            form_id=form_id,
            is_active_only=True,
        )
        if not fee_schedules:
            fee_schedules = await self.list_fee_schedules(
                contract_id=contract.id,
                visit_code=visit_code,
                is_active_only=True,
            )
        if not fee_schedules:
            return None

        fs = fee_schedules[0]
        split_payees = await self.list_split_payees(
            contract_id=contract.id, is_active_only=True
        )
        breakdown = self.calculate_procedure_breakdown(
            fee_schedule=fs, contract=contract, split_payees=split_payees
        )

        payable_id = str(uuid.uuid4())
        payable = PayableModel(
            id=payable_id,
            study_id=study_id,
            site_id=site_id,
            contract_id=contract.id,
            subject_id=subject_id,
            visit_code=visit_code,
            form_id=form_id,
            procedure_code=fs.procedure_code,
            procedure_name=fs.procedure_name,
            trigger_type=TriggerType.CRA_SDV_VERIFIED.value,
            trigger_event_id=event_id,
            trigger_source="MONITORING_CRA",
            base_amount=breakdown.base_amount,
            overhead_amount=breakdown.overhead_amount,
            patient_stipend_amount=breakdown.patient_stipend,
            travel_reimbursement_amount=breakdown.travel_reimbursement,
            gross_amount=breakdown.gross_amount,
            withholding_rate=breakdown.withholding_rate,
            withholding_amount=breakdown.withholding_amount,
            net_amount=breakdown.net_amount,
            currency=breakdown.currency,
            status=PayableStatus.TRIGGERED.value,
            is_sdv_gated=False,
            sdv_verified_at=dt_verified,
            sdv_verified_by=verified_by,
            created_by=verified_by,
            reason_for_change=reason_for_change
            or f"Triggered from CRA SDV verification on visit '{visit_code}'",
            version_index=contract.version_index,
        )

        alloc_models = []
        for alloc_calc in breakdown.allocations:
            alloc_model = PayableAllocationModel(
                id=str(uuid.uuid4()),
                payable_id=payable_id,
                payee_id=alloc_calc.payee_id,
                payee_name=alloc_calc.payee_name,
                payee_role=alloc_calc.payee_role,
                allocation_percentage=alloc_calc.allocation_percentage,
                gross_share=alloc_calc.gross_share,
                withholding_share=alloc_calc.withholding_share,
                net_share=alloc_calc.net_share,
                currency=alloc_calc.currency,
                status="ALLOCATED",
                created_by=verified_by,
                reason_for_change="Split payee allocation from CRA SDV",
                version_index=contract.version_index,
            )
            alloc_models.append(alloc_model)

        holdback = HoldbackReserveModel(
            id=str(uuid.uuid4()),
            study_id=study_id,
            site_id=site_id,
            contract_id=contract.id,
            payable_id=payable_id,
            holdback_amount=breakdown.withholding_amount,
            currency=breakdown.currency,
            status=HoldbackStatus.HELD.value,
            created_by=verified_by,
            reason_for_change=f"10% holdback reserve retained for payable {payable_id}",
            version_index=contract.version_index,
        )

        payable.allocations = alloc_models
        payable.holdback_reserve = holdback
        self.session.add(payable)
        await self.session.commit()
        await self.session.refresh(payable)

        await record_payment_audit_log(
            session=self.session,
            entity_type="PAYABLE",
            entity_id=payable.id,
            action=AuditAction.SDV_VERIFIED_UNLOCKED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "payable_id": payable.id,
                "gross_amount": payable.gross_amount,
                "withholding_amount": payable.withholding_amount,
                "net_amount": payable.net_amount,
                "status": payable.status,
                "sdv_verified_by": verified_by,
            },
            details=f"Payable {payable.id} created from direct CRA SDV verification '{event_id}'.",
            reason_for_change=reason_for_change or "Direct CRA SDV Trigger",
            version_index=contract.version_index,
        )
        return payable

    async def process_milestone_completion_event(
        self,
        study_id: str,
        site_id: str,
        milestone_code: str,
        milestone_name: str,
        amount: float,
        verified_by: str,
        event_id: str,
        timestamp: datetime | None = None,
        user_id: str = "system",
        user_roles: str = "CTMS Milestone Service",
        reason_for_change: str | None = None,
    ) -> PayableModel:
        """Processes site/study milestone completion events into payables."""
        existing = await self._get_payable_by_event_id(event_id)
        if existing:
            return existing

        contract = await self.get_active_contract(study_id, site_id)
        if not contract:
            raise ContractNotFoundError(
                f"No active budget contract found for study '{study_id}' and site '{site_id}'."
            )

        withholding_rate = contract.withholding_percentage
        withholding_amt = round(amount * (withholding_rate / 100.0), 2)
        net_amt = round(amount - withholding_amt, 2)

        payable_id = str(uuid.uuid4())
        payable = PayableModel(
            id=payable_id,
            study_id=study_id,
            site_id=site_id,
            contract_id=contract.id,
            subject_id="SITE_LEVEL",
            visit_code="MILESTONE",
            form_id=None,
            procedure_code=milestone_code,
            procedure_name=milestone_name,
            trigger_type=TriggerType.MILESTONE_COMPLETED.value,
            trigger_event_id=event_id,
            trigger_source="CTMS_MILESTONE",
            base_amount=amount,
            overhead_amount=0.0,
            patient_stipend_amount=0.0,
            travel_reimbursement_amount=0.0,
            gross_amount=amount,
            withholding_rate=withholding_rate,
            withholding_amount=withholding_amt,
            net_amount=net_amt,
            currency=contract.currency,
            status=PayableStatus.TRIGGERED.value,
            is_sdv_gated=False,
            created_by=verified_by,
            reason_for_change=reason_for_change
            or f"Milestone achieved: {milestone_name} ({milestone_code})",
            version_index=contract.version_index,
        )

        split_payees = await self.list_split_payees(
            contract_id=contract.id, is_active_only=True
        )
        alloc_models: list[PayableAllocationModel] = []
        if split_payees:
            for sp in split_payees:
                pct = sp.allocation_percentage
                g_share = round(amount * (pct / 100.0), 2)
                w_share = round(withholding_amt * (pct / 100.0), 2)
                n_share = round(net_amt * (pct / 100.0), 2)
                alloc_model = PayableAllocationModel(
                    id=str(uuid.uuid4()),
                    payable_id=payable_id,
                    payee_id=sp.payee_id,
                    payee_name=sp.payee_name,
                    payee_role=sp.payee_role,
                    allocation_percentage=pct,
                    gross_share=g_share,
                    withholding_share=w_share,
                    net_share=n_share,
                    currency=contract.currency,
                    status="ALLOCATED",
                    created_by=verified_by,
                    reason_for_change="Milestone split payee share",
                    version_index=contract.version_index,
                )
                alloc_models.append(alloc_model)
        else:
            alloc_models.append(
                PayableAllocationModel(
                    id=str(uuid.uuid4()),
                    payable_id=payable_id,
                    payee_id=f"PAYEE-{contract.site_id}",
                    payee_name=f"Site {contract.site_id} Research Account",
                    payee_role="INSTITUTION",
                    allocation_percentage=100.0,
                    gross_share=amount,
                    withholding_share=withholding_amt,
                    net_share=net_amt,
                    currency=contract.currency,
                    status="ALLOCATED",
                    created_by=verified_by,
                    reason_for_change="Milestone primary payee share",
                    version_index=contract.version_index,
                )
            )

        holdback = HoldbackReserveModel(
            id=str(uuid.uuid4()),
            study_id=study_id,
            site_id=site_id,
            contract_id=contract.id,
            payable_id=payable_id,
            holdback_amount=withholding_amt,
            currency=contract.currency,
            status=HoldbackStatus.HELD.value,
            created_by=verified_by,
            reason_for_change=f"Holdback for milestone {milestone_code}",
            version_index=contract.version_index,
        )

        payable.allocations = alloc_models
        payable.holdback_reserve = holdback
        self.session.add(payable)
        await self.session.commit()
        await self.session.refresh(payable)

        await record_payment_audit_log(
            session=self.session,
            entity_type="PAYABLE",
            entity_id=payable.id,
            action="PAYABLE_TRIGGERED_MILESTONE",
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "payable_id": payable.id,
                "milestone_code": milestone_code,
                "gross_amount": amount,
                "withholding_amount": withholding_amt,
                "net_amount": net_amt,
            },
            details=f"Payable {payable.id} created for milestone '{milestone_name}' ({milestone_code}).",
            reason_for_change=reason_for_change or "Milestone Achievement Trigger",
            version_index=contract.version_index,
        )
        return payable

    # ==========================================
    # 5. Holdback Retention & Release Governance
    # ==========================================

    async def release_holdback_reserves(
        self,
        study_id: str,
        site_id: str,
        release_reason: str,
        dm_signoff_by: str,
        quality_signoff_by: str,
        holdback_ids: list[str] | None = None,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> dict[str, Any]:
        """Formally releases withholding holdback retention reserves with dual DM and Quality sign-off."""
        if not dm_signoff_by or not dm_signoff_by.strip():
            raise PaymentsValidationError(
                "Formal Data Management sign-off is required to release holdback retention reserves."
            )
        if not quality_signoff_by or not quality_signoff_by.strip():
            raise PaymentsValidationError(
                "Formal Quality Oversight sign-off is required to release holdback retention reserves."
            )

        all_held = await self.list_holdback_reserves(
            study_id=study_id, site_id=site_id, status=HoldbackStatus.HELD.value
        )

        if holdback_ids:
            target_reserves = [hr for hr in all_held if hr.id in holdback_ids]
        else:
            target_reserves = all_held

        if not target_reserves:
            raise PaymentsValidationError(
                f"No active HELD holdback reserves found for study '{study_id}' and site '{site_id}'."
            )

        now = datetime.now(UTC)
        total_released = 0.0
        currency = "USD"
        released_items: list[HoldbackReserveModel] = []

        for hr in target_reserves:
            hr.status = HoldbackStatus.RELEASED.value
            hr.release_authorized_by = user_id
            hr.release_authorized_at = now
            hr.dm_signoff_by = dm_signoff_by
            hr.dm_signoff_at = now
            hr.quality_signoff_by = quality_signoff_by
            hr.quality_signoff_at = now
            hr.release_notes = release_reason
            hr.reason_for_change = (
                reason_for_change
                or f"Released holdback with DM ({dm_signoff_by}) and Quality ({quality_signoff_by}) sign-offs"
            )
            self.session.add(hr)
            total_released += hr.holdback_amount
            currency = hr.currency
            released_items.append(hr)

        await self.session.commit()
        for hr in released_items:
            await self.session.refresh(hr)

            await record_payment_audit_log(
                session=self.session,
                entity_type="HOLDBACK_RESERVE",
                entity_id=hr.id,
                action=AuditAction.HOLDBACK_RELEASED.value,
                user_id=user_id,
                user_roles=user_roles,
                previous_state={"status": "HELD"},
                new_state={
                    "status": "RELEASED",
                    "amount": hr.holdback_amount,
                    "dm_signoff_by": dm_signoff_by,
                    "quality_signoff_by": quality_signoff_by,
                },
                details=f"Holdback reserve {hr.id} (${hr.holdback_amount:.2f}) released with dual DM and Quality approval.",
                reason_for_change=release_reason,
                version_index=hr.version_index,
            )

        return {
            "study_id": study_id,
            "site_id": site_id,
            "total_released_amount": round(total_released, 2),
            "currency": currency,
            "released_count": len(released_items),
            "dm_signoff_by": dm_signoff_by,
            "quality_signoff_by": quality_signoff_by,
            "status": "RELEASED",
            "released_reserves": released_items,
        }

    # ==========================================
    # 6. Payable Management & Queries
    # ==========================================

    async def get_payable(self, payable_id: str) -> PayableModel | None:
        """Retrieves payable by ID with allocations and holdback reserve eagerly loaded."""
        stmt = (
            select(PayableModel)
            .where(PayableModel.id == payable_id)
            .options(
                selectinload(PayableModel.allocations),
                selectinload(PayableModel.holdback_reserve),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_payables(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_id: str | None = None,
        visit_code: str | None = None,
        form_id: str | None = None,
        status: str | None = None,
        is_sdv_gated: bool | None = None,
    ) -> list[PayableModel]:
        """Lists payables with optional filtering."""
        stmt = select(PayableModel).options(
            selectinload(PayableModel.allocations),
            selectinload(PayableModel.holdback_reserve),
        )
        if study_id:
            stmt = stmt.where(PayableModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(PayableModel.site_id == site_id)
        if subject_id:
            stmt = stmt.where(PayableModel.subject_id == subject_id)
        if visit_code:
            stmt = stmt.where(PayableModel.visit_code == visit_code)
        if form_id:
            stmt = stmt.where(PayableModel.form_id == form_id)
        if status:
            stmt = stmt.where(PayableModel.status == status)
        if is_sdv_gated is not None:
            stmt = stmt.where(PayableModel.is_sdv_gated.is_(is_sdv_gated))
        stmt = stmt.order_by(desc(PayableModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def approve_payable(
        self,
        payable_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str | None = None,
    ) -> PayableModel:
        """Approves a payable for invoicing and disbursement."""
        payable = await self.get_payable(payable_id)
        if not payable:
            raise PayableNotFoundError(f"Payable with ID '{payable_id}' not found.")

        if payable.is_sdv_gated or payable.status == PayableStatus.PENDING_SDV.value:
            raise PaymentsValidationError(
                f"Cannot approve payable '{payable_id}' while gated pending CRA SDV verification."
            )

        prev_status = payable.status
        payable.status = PayableStatus.APPROVED.value
        payable.reason_for_change = (
            reason_for_change or f"Payable approved by {user_id}"
        )
        self.session.add(payable)
        await self.session.commit()
        await self.session.refresh(payable)

        await record_payment_audit_log(
            session=self.session,
            entity_type="PAYABLE",
            entity_id=payable.id,
            action=AuditAction.PAYABLE_APPROVED.value,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={"status": payable.status},
            details=f"Payable {payable.id} approved for disbursement.",
            reason_for_change=reason_for_change or "Payment Approval",
            version_index=payable.version_index,
        )
        return payable

    async def list_holdback_reserves(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        status: str | None = None,
    ) -> list[HoldbackReserveModel]:
        """Lists holdback retention reserves."""
        stmt = select(HoldbackReserveModel)
        if study_id:
            stmt = stmt.where(HoldbackReserveModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(HoldbackReserveModel.site_id == site_id)
        if status:
            stmt = stmt.where(HoldbackReserveModel.status == status)
        stmt = stmt.order_by(desc(HoldbackReserveModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def list_audit_logs(
        self,
        entity_type: str | None = None,
        entity_id: str | None = None,
        user_id: str | None = None,
    ) -> list[PaymentAuditLogModel]:
        """Lists immutable 21 CFR Part 11 payment audit logs."""
        return await list_payment_audit_logs(
            session=self.session,
            entity_type=entity_type,
            entity_id=entity_id,
            user_id=user_id,
        )


__all__ = ["SitePaymentLedgerEngine"]
