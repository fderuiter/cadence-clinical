"""Core Payments Application Service and Real-Time Trigger/Split Engine.

Owns: Real-time EDC/SDV event ingestion, procedure fee schedule evaluation,
multi-party split payee allocation, 10% holdback retention governance,
self-billing invoicing, cryptographically signed remittance advice,
enterprise ERP synchronization (SAP/NetSuite), and 21 CFR Part 11 immutable financial logging.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

import base64
import hashlib
import uuid
from datetime import UTC, datetime
from typing import Any

from apps.payments.adapters.repositories import SQLAlchemyPaymentsRepository
from apps.payments.domain.models import (
    AuditAction,
    ContractStatus,
    ErpExportEnvelope,
    ErpSyncStatus,
    ErpSystem,
    HoldbackStatus,
    InvoiceStatus,
    PayableStatus,
    RemittanceDispatchStatus,
    SplitAllocationCalculation,
    TriggerType,
    VisitCalculationBreakdown,
)
from apps.payments.infrastructure.models import (
    BudgetContractModel,
    ErpSyncLogModel,
    FeeScheduleModel,
    HoldbackReserveModel,
    InvoiceLineItemModel,
    PayableAllocationModel,
    PayableModel,
    PaymentAuditLogModel,
    RemittanceAdviceModel,
    SelfBillingInvoiceModel,
    SplitPayeeConfigModel,
)
from apps.payments.presentation.dtos import (
    ErpWebhookPayload,
    SplitPayeeConfigItem,
)
from apps.payments.services.pdf_remittance_generator import (
    RemittanceAdvicePdfGenerator,
)


class PaymentsService:
    """Application service for Site Payments, Real-Time Triggers, and Split-Payee Ledgers."""

    def __init__(self, repo: SQLAlchemyPaymentsRepository) -> None:
        self.repo = repo

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
        # Check for existing active contract
        existing = await self.repo.get_active_contract(study_id, site_id)
        version_index = 1
        if existing:
            version_index = existing.version_index + 1
            existing.status = ContractStatus.SUPERSEDED.value
            existing.is_active = False
            await self.repo.save_contract(existing)

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
        saved = await self.repo.save_contract(contract)

        await self.repo.record_audit_log(
            entity_type="CONTRACT",
            entity_id=saved.id,
            action=AuditAction.CONTRACT_CREATED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "id": saved.id,
                "study_id": saved.study_id,
                "site_id": saved.site_id,
                "contract_name": saved.contract_name,
                "tier_level": saved.tier_level,
                "total_budget": saved.total_budget,
                "overhead_percentage": saved.overhead_percentage,
                "withholding_percentage": saved.withholding_percentage,
                "version_index": saved.version_index,
            },
            details=f"Created budget contract '{saved.contract_name}' for site '{site_id}' in study '{study_id}'.",
            reason_for_change=reason_for_change or "Initial contract execution",
            version_index=saved.version_index,
        )
        return saved

    async def get_budget_contract(self, contract_id: str) -> BudgetContractModel | None:
        """Retrieves a budget contract by primary key."""
        return await self.repo.get_contract_by_id(contract_id)

    async def get_active_contract(
        self, study_id: str, site_id: str
    ) -> BudgetContractModel | None:
        """Retrieves active budget contract for a study and site."""
        return await self.repo.get_active_contract(study_id, site_id)

    async def list_budget_contracts(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[BudgetContractModel]:
        """Lists budget contracts with optional filtering."""
        return await self.repo.list_contracts(
            study_id=study_id, site_id=site_id, is_active_only=is_active_only
        )

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
        contract = await self.repo.get_contract_by_id(contract_id)
        if not contract:
            raise ValueError(f"Contract with ID '{contract_id}' not found.")

        # Inherit overhead and withholding percentages from contract if not specified
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
        saved = await self.repo.save_fee_schedule(fee_schedule)

        await self.repo.record_audit_log(
            entity_type="FEE_SCHEDULE",
            entity_id=saved.id,
            action=AuditAction.FEE_SCHEDULE_ADDED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "id": saved.id,
                "contract_id": saved.contract_id,
                "visit_code": saved.visit_code,
                "procedure_code": saved.procedure_code,
                "base_amount": saved.base_amount,
                "requires_sdv": saved.requires_sdv,
            },
            details=f"Added fee schedule for procedure '{procedure_code}' ({procedure_name}) on visit '{visit_code}'.",
            reason_for_change=reason_for_change or "Fee schedule setup",
            version_index=contract.version_index,
        )
        return saved

    async def list_fee_schedules(
        self,
        contract_id: str,
        visit_code: str | None = None,
        form_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[FeeScheduleModel]:
        """Lists procedure fee schedules for a contract."""
        return await self.repo.list_fee_schedules(
            contract_id=contract_id,
            visit_code=visit_code,
            form_id=form_id,
            is_active_only=is_active_only,
        )

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
        contract = await self.repo.get_contract_by_id(contract_id)
        if not contract:
            raise ValueError(f"Contract with ID '{contract_id}' not found.")

        # Validate that allocation percentages sum to 100.0%
        total_percentage = sum(p.allocation_percentage for p in payees)
        if abs(total_percentage - 100.0) > 0.01:
            raise ValueError(
                f"Split payee allocations must sum exactly to 100.0% (current sum: {total_percentage:.2f}%)."
            )

        # Deactivate existing active split payees for this contract
        existing_payees = await self.repo.list_split_payees(
            contract_id=contract_id, is_active_only=True
        )
        for ep in existing_payees:
            ep.is_active = False
        if existing_payees:
            await self.repo.save_split_payees(existing_payees)

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
            new_entities.append(entity)

        saved_payees = await self.repo.save_split_payees(new_entities)

        await self.repo.record_audit_log(
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
                    for sp in saved_payees
                ],
            },
            details=f"Configured {len(saved_payees)} split payees for contract '{contract_id}'.",
            reason_for_change=reason_for_change or "Split payee governance setup",
            version_index=contract.version_index,
        )
        return saved_payees

    async def list_split_payees(
        self, contract_id: str, is_active_only: bool = True
    ) -> list[SplitPayeeConfigModel]:
        """Lists configured split payees for a contract."""
        return await self.repo.list_split_payees(
            contract_id=contract_id, is_active_only=is_active_only
        )

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

        # Split payee allocations calculation
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
            # Default 100% to main site payee
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
        """Processes real-time eCRF completion events into payable ledger items.

        Requirements: PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
        """
        # Deterministic idempotency check
        existing_payable = await self.repo.get_payable_by_event_id(event_id)
        if existing_payable:
            return [existing_payable]

        # Retrieve active contract
        contract = await self.repo.get_active_contract(study_id, site_id)
        if not contract:
            raise ValueError(
                f"No active budget contract found for study '{study_id}' and site '{site_id}'."
            )

        # Match fee schedules by (visit_code, form_id) or visit_code
        fee_schedules = await self.repo.list_fee_schedules(
            contract_id=contract.id,
            visit_code=visit_code,
            form_id=form_id,
            is_active_only=True,
        )
        if not fee_schedules:
            # Try matching by visit_code if form-specific match wasn't found
            fee_schedules = await self.repo.list_fee_schedules(
                contract_id=contract.id,
                visit_code=visit_code,
                is_active_only=True,
            )

        if not fee_schedules:
            raise ValueError(
                f"No active fee schedule matching visit '{visit_code}' and form '{form_id}' for contract '{contract.id}'."
            )

        # Retrieve active split payees
        split_payees = await self.repo.list_split_payees(
            contract_id=contract.id, is_active_only=True
        )

        generated_payables: list[PayableModel] = []

        for idx, fs in enumerate(fee_schedules):
            # Form unique event id per procedure item if multiple items in schedule
            sub_event_id = (
                event_id if len(fee_schedules) == 1 else f"{event_id}-{idx + 1}"
            )

            # Check if this sub-event was already created
            existing_sub = await self.repo.get_payable_by_event_id(sub_event_id)
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

            # Split allocations
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

            # Holdback reserve record (10% retention)
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
            saved_payable = await self.repo.save_payable(payable)

            audit_action = (
                AuditAction.PAYABLE_GATED_PENDING_SDV.value
                if is_gated
                else AuditAction.PAYABLE_TRIGGERED_ECRF.value
            )

            await self.repo.record_audit_log(
                entity_type="PAYABLE",
                entity_id=saved_payable.id,
                action=audit_action,
                user_id=user_id,
                user_roles=user_roles,
                new_state={
                    "payable_id": saved_payable.id,
                    "subject_id": subject_id,
                    "visit_code": visit_code,
                    "form_id": form_id,
                    "gross_amount": saved_payable.gross_amount,
                    "withholding_amount": saved_payable.withholding_amount,
                    "net_amount": saved_payable.net_amount,
                    "status": saved_payable.status,
                    "is_sdv_gated": saved_payable.is_sdv_gated,
                },
                details=(
                    f"Payable {saved_payable.id} created in '{initial_status}' status "
                    f"from EDC trigger '{event_id}' (Gross: ${saved_payable.gross_amount:.2f}, "
                    f"Holdback: ${saved_payable.withholding_amount:.2f}, Net: ${saved_payable.net_amount:.2f})."
                ),
                reason_for_change=reason_for_change
                or "Automated EDC Form Payable Trigger",
                version_index=contract.version_index,
            )

            generated_payables.append(saved_payable)

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
        """Processes CRA SDV verification events, unlocking gated payables.

        Requirements: PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
        """
        # Deterministic check: Is there a gated payable in PENDING_SDV status?
        payables = await self.repo.list_payables(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_code=visit_code,
            form_id=form_id,
            status=PayableStatus.PENDING_SDV.value,
        )

        dt_verified = timestamp or datetime.now(UTC)

        if payables:
            # Unlock the gated payable
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
            saved = await self.repo.save_payable(target_payable)

            await self.repo.record_audit_log(
                entity_type="PAYABLE",
                entity_id=saved.id,
                action=AuditAction.SDV_VERIFIED_UNLOCKED.value,
                user_id=user_id,
                user_roles=user_roles,
                previous_state=prev_state,
                new_state={
                    "status": saved.status,
                    "is_sdv_gated": saved.is_sdv_gated,
                    "sdv_verified_at": str(dt_verified),
                    "sdv_verified_by": verified_by,
                },
                details=f"Payable {saved.id} unlocked from PENDING_SDV to TRIGGERED following CRA SDV sign-off by '{verified_by}'.",
                reason_for_change=reason_for_change
                or "CRA SDV Sign-off Verification Gate",
                version_index=saved.version_index,
            )
            return await self.repo.get_payable_by_id(saved.id)

        # If no pending gated payable exists, check if we need to trigger directly from SDV event
        contract = await self.repo.get_active_contract(study_id, site_id)
        if not contract:
            return None

        fee_schedules = await self.repo.list_fee_schedules(
            contract_id=contract.id,
            visit_code=visit_code,
            form_id=form_id,
            is_active_only=True,
        )
        if not fee_schedules:
            fee_schedules = await self.repo.list_fee_schedules(
                contract_id=contract.id,
                visit_code=visit_code,
                is_active_only=True,
            )
        if not fee_schedules:
            return None

        fs = fee_schedules[0]
        split_payees = await self.repo.list_split_payees(
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

        # Split allocations
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
                created_by=verified_by,
                reason_for_change="Split payee allocation from CRA SDV",
                version_index=contract.version_index,
            )
            alloc_models.append(alloc_model)

        # Holdback reserve
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
        saved_payable = await self.repo.save_payable(payable)

        await self.repo.record_audit_log(
            entity_type="PAYABLE",
            entity_id=saved_payable.id,
            action=AuditAction.SDV_VERIFIED_UNLOCKED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "payable_id": saved_payable.id,
                "gross_amount": saved_payable.gross_amount,
                "withholding_amount": saved_payable.withholding_amount,
                "net_amount": saved_payable.net_amount,
                "status": saved_payable.status,
                "sdv_verified_by": verified_by,
            },
            details=f"Payable {saved_payable.id} created from direct CRA SDV verification '{event_id}'.",
            reason_for_change=reason_for_change or "Direct CRA SDV Trigger",
            version_index=contract.version_index,
        )
        return saved_payable

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
        existing = await self.repo.get_payable_by_event_id(event_id)
        if existing:
            return existing

        contract = await self.repo.get_active_contract(study_id, site_id)
        if not contract:
            raise ValueError(
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

        # Split allocations
        split_payees = await self.repo.list_split_payees(
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

        # Holdback reserve
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
        saved_payable = await self.repo.save_payable(payable)

        await self.repo.record_audit_log(
            entity_type="PAYABLE",
            entity_id=saved_payable.id,
            action="PAYABLE_TRIGGERED_MILESTONE",
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "payable_id": saved_payable.id,
                "milestone_code": milestone_code,
                "gross_amount": amount,
                "withholding_amount": withholding_amt,
                "net_amount": net_amt,
            },
            details=f"Payable {saved_payable.id} created for milestone '{milestone_name}' ({milestone_code}).",
            reason_for_change=reason_for_change or "Milestone Achievement Trigger",
            version_index=contract.version_index,
        )
        return saved_payable

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
        """Formally releases withholding holdback retention reserves with dual DM and Quality sign-off.

        Requirements: PRD-PAY-003, PRD-PAY-006
        """
        # Strict dual-governance validation
        if not dm_signoff_by or not dm_signoff_by.strip():
            raise ValueError(
                "Formal Data Management sign-off is required to release holdback retention reserves."
            )
        if not quality_signoff_by or not quality_signoff_by.strip():
            raise ValueError(
                "Formal Quality Oversight sign-off is required to release holdback retention reserves."
            )

        all_held = await self.repo.list_holdback_reserves(
            study_id=study_id, site_id=site_id, status=HoldbackStatus.HELD.value
        )

        if holdback_ids:
            target_reserves = [hr for hr in all_held if hr.id in holdback_ids]
        else:
            target_reserves = all_held

        if not target_reserves:
            raise ValueError(
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
            saved_hr = await self.repo.save_holdback_reserve(hr)
            total_released += saved_hr.holdback_amount
            currency = saved_hr.currency
            released_items.append(saved_hr)

            await self.repo.record_audit_log(
                entity_type="HOLDBACK_RESERVE",
                entity_id=saved_hr.id,
                action=AuditAction.HOLDBACK_RELEASED.value,
                user_id=user_id,
                user_roles=user_roles,
                previous_state={"status": "HELD"},
                new_state={
                    "status": "RELEASED",
                    "amount": saved_hr.holdback_amount,
                    "dm_signoff_by": dm_signoff_by,
                    "quality_signoff_by": quality_signoff_by,
                },
                details=f"Holdback reserve {saved_hr.id} (${saved_hr.holdback_amount:.2f}) released with dual DM and Quality approval.",
                reason_for_change=release_reason,
                version_index=saved_hr.version_index,
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
        """Retrieves payable by ID with allocations and holdback reserve."""
        return await self.repo.get_payable_by_id(payable_id)

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
        return await self.repo.list_payables(
            study_id=study_id,
            site_id=site_id,
            subject_id=subject_id,
            visit_code=visit_code,
            form_id=form_id,
            status=status,
            is_sdv_gated=is_sdv_gated,
        )

    async def approve_payable(
        self,
        payable_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str | None = None,
    ) -> PayableModel:
        """Approves a payable for invoicing and disbursement."""
        payable = await self.repo.get_payable_by_id(payable_id)
        if not payable:
            raise ValueError(f"Payable with ID '{payable_id}' not found.")

        if payable.is_sdv_gated or payable.status == PayableStatus.PENDING_SDV.value:
            raise ValueError(
                f"Cannot approve payable '{payable_id}' while gated pending CRA SDV verification."
            )

        prev_status = payable.status
        payable.status = PayableStatus.APPROVED.value
        payable.reason_for_change = (
            reason_for_change or f"Payable approved by {user_id}"
        )
        saved = await self.repo.save_payable(payable)

        await self.repo.record_audit_log(
            entity_type="PAYABLE",
            entity_id=saved.id,
            action=AuditAction.PAYABLE_APPROVED.value,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={"status": saved.status},
            details=f"Payable {saved.id} approved for disbursement.",
            reason_for_change=reason_for_change or "Payment Approval",
            version_index=saved.version_index,
        )
        return saved

    async def list_holdback_reserves(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        status: str | None = None,
    ) -> list[HoldbackReserveModel]:
        """Lists holdback retention reserves."""
        return await self.repo.list_holdback_reserves(
            study_id=study_id, site_id=site_id, status=status
        )

    async def list_audit_logs(
        self,
        entity_type: str | None = None,
        entity_id: str | None = None,
        user_id: str | None = None,
    ) -> list[PaymentAuditLogModel]:
        """Lists immutable 21 CFR Part 11 payment audit logs."""
        return await self.repo.list_audit_logs(
            entity_type=entity_type, entity_id=entity_id, user_id=user_id
        )

    # ==========================================
    # 7. Self-Billing Invoicing Engine
    # ==========================================

    async def compile_billing_cycle(
        self,
        study_id: str,
        site_id: str,
        billing_cycle: str = "MONTHLY",
        period_start: str = "2026-08-01",
        period_end: str = "2026-08-31",
        auto_approve: bool = False,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> SelfBillingInvoiceModel:
        """Automates monthly and quarterly billing cycle compilation into self-billing invoices.

        Requirements: PRD-PAY-004, PRD-PAY-006, ADR-2199
        """
        contract = await self.repo.get_active_contract(study_id, site_id)
        if not contract:
            raise ValueError(
                f"No active budget contract found for study '{study_id}' and site '{site_id}'."
            )

        # Retrieve all approved (or payable) items ready for billing compilation
        payables = await self.repo.list_payables(
            study_id=study_id,
            site_id=site_id,
            status=PayableStatus.APPROVED.value,
        )

        # If no APPROVED payables found, also include un-gated TRIGGERED payables if available
        if not payables:
            payables = await self.repo.list_payables(
                study_id=study_id,
                site_id=site_id,
                status=PayableStatus.TRIGGERED.value,
                is_sdv_gated=False,
            )

        if not payables:
            raise ValueError(
                f"No approved payable line items available for billing compilation in study '{study_id}', site '{site_id}'."
            )

        # Aggregate financial totals
        total_base = round(sum(p.base_amount for p in payables), 2)
        total_overhead = round(sum(p.overhead_amount for p in payables), 2)
        total_stipend = round(sum(p.patient_stipend_amount for p in payables), 2)
        total_travel = round(sum(p.travel_reimbursement_amount for p in payables), 2)
        total_gross = round(sum(p.gross_amount for p in payables), 2)
        total_holdback = round(sum(p.withholding_amount for p in payables), 2)
        total_net = round(sum(p.net_amount for p in payables), 2)

        clean_period = period_end.replace("-", "")
        unique_suffix = uuid.uuid4().hex[:6].upper()
        invoice_number = (
            f"SBI-{study_id[:6]}-{site_id[:6]}-{clean_period}-{unique_suffix}"
        )
        remittance_number = f"REM-{clean_period}-{unique_suffix}"

        # Fetch split payees configured on the contract
        split_payees = await self.repo.list_split_payees(
            contract_id=contract.id, is_active_only=True
        )
        payee_data = []
        if split_payees:
            for sp in split_payees:
                pct = sp.allocation_percentage
                payee_data.append(
                    {
                        "payee_id": sp.payee_id,
                        "payee_name": sp.payee_name,
                        "payee_role": sp.payee_role,
                        "allocation_percentage": pct,
                        "gross_share": round(total_gross * (pct / 100.0), 2),
                        "withholding_share": round(total_holdback * (pct / 100.0), 2),
                        "net_share": round(total_net * (pct / 100.0), 2),
                    }
                )
        else:
            payee_data.append(
                {
                    "payee_id": f"PAYEE-{site_id}",
                    "payee_name": f"Site {site_id} Research Account",
                    "payee_role": "INSTITUTION",
                    "allocation_percentage": 100.0,
                    "gross_share": total_gross,
                    "withholding_share": total_holdback,
                    "net_share": total_net,
                }
            )

        invoice_id = str(uuid.uuid4())
        initial_status = (
            InvoiceStatus.APPROVED.value if auto_approve else InvoiceStatus.DRAFT.value
        )
        now = datetime.now(UTC)

        invoice = SelfBillingInvoiceModel(
            id=invoice_id,
            invoice_number=invoice_number,
            study_id=study_id,
            site_id=site_id,
            contract_id=contract.id,
            billing_cycle=billing_cycle,
            period_start=period_start,
            period_end=period_end,
            currency=contract.currency,
            total_base_amount=total_base,
            total_overhead_amount=total_overhead,
            total_patient_stipend=total_stipend,
            total_travel_reimbursement=total_travel,
            total_gross_amount=total_gross,
            total_holdback_amount=total_holdback,
            total_net_amount=total_net,
            payable_count=len(payables),
            status=initial_status,
            approved_by=user_id if auto_approve else None,
            approved_at=now if auto_approve else None,
            erp_system=ErpSystem.SAP.value,
            erp_sync_status=ErpSyncStatus.PENDING.value,
            created_by=user_id,
            reason_for_change=reason_for_change
            or f"Compiled {billing_cycle} self-billing invoice {invoice_number}",
            version_index=contract.version_index,
        )

        # Generate line item records
        line_items: list[InvoiceLineItemModel] = []
        raw_items_dict: list[dict[str, Any]] = []
        for p in payables:
            item = InvoiceLineItemModel(
                id=str(uuid.uuid4()),
                invoice_id=invoice_id,
                payable_id=p.id,
                subject_id=p.subject_id,
                visit_code=p.visit_code,
                procedure_code=p.procedure_code,
                procedure_name=p.procedure_name,
                base_amount=p.base_amount,
                overhead_amount=p.overhead_amount,
                patient_stipend=p.patient_stipend_amount,
                travel_reimbursement=p.travel_reimbursement_amount,
                gross_amount=p.gross_amount,
                withholding_rate=p.withholding_rate,
                withholding_amount=p.withholding_amount,
                net_amount=p.net_amount,
                currency=p.currency,
                created_by=user_id,
                reason_for_change=f"Aggregated payable {p.id}",
                version_index=contract.version_index,
            )
            line_items.append(item)
            raw_items_dict.append(
                {
                    "subject_id": p.subject_id,
                    "visit_code": p.visit_code,
                    "procedure_code": p.procedure_code,
                    "procedure_name": p.procedure_name,
                    "base_amount": p.base_amount,
                    "overhead_amount": p.overhead_amount,
                    "patient_stipend": p.patient_stipend_amount,
                    "travel_reimbursement": p.travel_reimbursement_amount,
                    "gross_amount": p.gross_amount,
                    "withholding_rate": p.withholding_rate,
                    "withholding_amount": p.withholding_amount,
                    "net_amount": p.net_amount,
                }
            )

        # Generate standard sealed PDF Remittance Advice
        html_markup = RemittanceAdvicePdfGenerator.generate_html_content(
            invoice_number=invoice_number,
            remittance_number=remittance_number,
            study_id=study_id,
            site_id=site_id,
            billing_cycle=billing_cycle,
            period_start=period_start,
            period_end=period_end,
            currency=contract.currency,
            total_base_amount=total_base,
            total_overhead_amount=total_overhead,
            total_patient_stipend=total_stipend,
            total_travel_reimbursement=total_travel,
            total_gross_amount=total_gross,
            total_holdback_amount=total_holdback,
            total_net_amount=total_net,
            line_items=raw_items_dict,
            split_payees=payee_data,
        )

        pdf_bytes = RemittanceAdvicePdfGenerator.render_pdf_bytes(
            html_markup, title=f"Remittance Advice {remittance_number}"
        )
        pdf_hash = RemittanceAdvicePdfGenerator.compute_sha256_hash(pdf_bytes)
        b64_pdf = base64.b64encode(pdf_bytes).decode("utf-8")

        remittance = RemittanceAdviceModel(
            id=str(uuid.uuid4()),
            remittance_number=remittance_number,
            invoice_id=invoice_id,
            study_id=study_id,
            site_id=site_id,
            currency=contract.currency,
            gross_amount=total_gross,
            overhead_amount=total_overhead,
            holdback_amount=total_holdback,
            net_amount=total_net,
            pdf_sha256_hash=pdf_hash,
            pdf_content=b64_pdf,
            is_sealed=True,
            is_signed=False,
            dispatch_status=RemittanceDispatchStatus.PENDING.value,
            created_by=user_id,
            reason_for_change="Generated Remittance Advice Statement",
            version_index=contract.version_index,
        )

        invoice.line_items = line_items
        invoice.remittance_advice = remittance

        saved_invoice = await self.repo.save_invoice(invoice)

        # Mark all compiled payables as INVOICED
        for p in payables:
            p.status = PayableStatus.INVOICED.value
            p.reason_for_change = f"Compiled into invoice {invoice_number}"
            await self.repo.save_payable(p)

        await self.repo.record_audit_log(
            entity_type="INVOICE",
            entity_id=saved_invoice.id,
            action=AuditAction.INVOICE_COMPILED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "invoice_number": saved_invoice.invoice_number,
                "billing_cycle": saved_invoice.billing_cycle,
                "period_start": saved_invoice.period_start,
                "period_end": saved_invoice.period_end,
                "total_gross_amount": saved_invoice.total_gross_amount,
                "total_holdback_amount": saved_invoice.total_holdback_amount,
                "total_net_amount": saved_invoice.total_net_amount,
                "status": saved_invoice.status,
                "remittance_number": remittance_number,
                "pdf_sha256_hash": pdf_hash,
            },
            details=(
                f"Compiled {billing_cycle} self-billing invoice {invoice_number} "
                f"with {len(payables)} payables (Gross: ${total_gross:.2f}, Holdback: ${total_holdback:.2f}, "
                f"Net: ${total_net:.2f}, SHA-256: {pdf_hash[:16]}...)."
            ),
            reason_for_change=reason_for_change
            or "Automated Billing Cycle Compilation",
            version_index=contract.version_index,
        )

        return saved_invoice

    async def approve_self_billing_invoice(
        self,
        invoice_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str | None = None,
    ) -> SelfBillingInvoiceModel:
        """Approves a draft self-billing invoice for ERP synchronization and disbursement."""
        invoice = await self.repo.get_invoice_by_id(invoice_id)
        if not invoice:
            raise ValueError(f"Self-billing invoice '{invoice_id}' not found.")

        prev_status = invoice.status
        now = datetime.now(UTC)
        invoice.status = InvoiceStatus.APPROVED.value
        invoice.approved_by = user_id
        invoice.approved_at = now
        invoice.reason_for_change = (
            reason_for_change or f"Invoice approved by {user_id}"
        )
        saved = await self.repo.save_invoice(invoice)

        await self.repo.record_audit_log(
            entity_type="INVOICE",
            entity_id=saved.id,
            action=AuditAction.INVOICE_APPROVED.value,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={"status": saved.status, "approved_by": user_id},
            details=f"Invoice {saved.invoice_number} approved for disbursement.",
            reason_for_change=reason_for_change or "Invoice Approval",
            version_index=saved.version_index,
        )
        return saved

    async def get_invoice(self, invoice_id: str) -> SelfBillingInvoiceModel | None:
        """Retrieves self-billing invoice by ID."""
        return await self.repo.get_invoice_by_id(invoice_id)

    async def get_invoice_by_number(
        self, invoice_number: str
    ) -> SelfBillingInvoiceModel | None:
        """Retrieves self-billing invoice by invoice number."""
        return await self.repo.get_invoice_by_number(invoice_number)

    async def list_invoices(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        billing_cycle: str | None = None,
        status: str | None = None,
        erp_sync_status: str | None = None,
    ) -> list[SelfBillingInvoiceModel]:
        """Lists self-billing invoices matching filter criteria."""
        return await self.repo.list_invoices(
            study_id=study_id,
            site_id=site_id,
            billing_cycle=billing_cycle,
            status=status,
            erp_sync_status=erp_sync_status,
        )

    # ==========================================
    # 8. Cryptographic Remittance Advice Signing & Verification
    # ==========================================

    async def get_remittance_advice(
        self, invoice_id: str
    ) -> RemittanceAdviceModel | None:
        """Retrieves remittance advice record for an invoice."""
        return await self.repo.get_remittance_advice_by_invoice_id(invoice_id)

    async def sign_and_seal_remittance_advice(
        self,
        invoice_id: str,
        signer_name: str,
        signer_role: str = "Grants Lead",
        signature_reason: str = "Authorized Self-Billing Remittance Statement",
        cert_pem: str | None = None,
        private_key_pem: str | None = None,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> RemittanceAdviceModel:
        """Cryptographically signs and seals a PDF remittance advice statement with 21 CFR Part 11 validation.

        Requirements: PRD-PAY-004, PRD-PAY-006, ADR-2199
        """
        invoice = await self.repo.get_invoice_by_id(invoice_id)
        if not invoice:
            raise ValueError(f"Invoice with ID '{invoice_id}' not found.")

        remittance = invoice.remittance_advice
        if not remittance:
            raise ValueError(
                f"No remittance advice document found for invoice '{invoice_id}'."
            )

        # Generate or utilize provided signing keys
        if not cert_pem or not private_key_pem:
            (
                priv_key_str,
                pub_cert_str,
                cert_fp,
            ) = RemittanceAdvicePdfGenerator.generate_ephemeral_key_pair(
                common_name=signer_name
            )
            signing_key = priv_key_str
            signing_cert = pub_cert_str
        else:
            signing_key = private_key_pem
            signing_cert = cert_pem
            cert_fp = hashlib.sha256(signing_cert.encode()).hexdigest()

        now = datetime.now(UTC)

        # Re-generate document HTML with full signer metadata manifest
        raw_items_dict = [
            {
                "subject_id": item.subject_id,
                "visit_code": item.visit_code,
                "procedure_code": item.procedure_code,
                "procedure_name": item.procedure_name,
                "base_amount": item.base_amount,
                "overhead_amount": item.overhead_amount,
                "patient_stipend": item.patient_stipend,
                "travel_reimbursement": item.travel_reimbursement,
                "gross_amount": item.gross_amount,
                "withholding_rate": item.withholding_rate,
                "withholding_amount": item.withholding_amount,
                "net_amount": item.net_amount,
            }
            for item in invoice.line_items
        ]

        split_payees = await self.repo.list_split_payees(
            contract_id=invoice.contract_id, is_active_only=True
        )
        payee_data = []
        if split_payees:
            for sp in split_payees:
                pct = sp.allocation_percentage
                payee_data.append(
                    {
                        "payee_id": sp.payee_id,
                        "payee_name": sp.payee_name,
                        "payee_role": sp.payee_role,
                        "allocation_percentage": pct,
                        "gross_share": round(
                            invoice.total_gross_amount * (pct / 100.0), 2
                        ),
                        "withholding_share": round(
                            invoice.total_holdback_amount * (pct / 100.0), 2
                        ),
                        "net_share": round(invoice.total_net_amount * (pct / 100.0), 2),
                    }
                )
        else:
            payee_data.append(
                {
                    "payee_id": f"PAYEE-{invoice.site_id}",
                    "payee_name": f"Site {invoice.site_id} Research Account",
                    "payee_role": "INSTITUTION",
                    "allocation_percentage": 100.0,
                    "gross_share": invoice.total_gross_amount,
                    "withholding_share": invoice.total_holdback_amount,
                    "net_share": invoice.total_net_amount,
                }
            )

        signed_html = RemittanceAdvicePdfGenerator.generate_html_content(
            invoice_number=invoice.invoice_number,
            remittance_number=remittance.remittance_number,
            study_id=invoice.study_id,
            site_id=invoice.site_id,
            billing_cycle=invoice.billing_cycle,
            period_start=invoice.period_start,
            period_end=invoice.period_end,
            currency=invoice.currency,
            total_base_amount=invoice.total_base_amount,
            total_overhead_amount=invoice.total_overhead_amount,
            total_patient_stipend=invoice.total_patient_stipend,
            total_travel_reimbursement=invoice.total_travel_reimbursement,
            total_gross_amount=invoice.total_gross_amount,
            total_holdback_amount=invoice.total_holdback_amount,
            total_net_amount=invoice.total_net_amount,
            line_items=raw_items_dict,
            split_payees=payee_data,
            signer_name=signer_name,
            signer_role=signer_role,
            signed_at=now,
            signature_reason=signature_reason,
            cert_fingerprint=cert_fp,
        )

        pdf_bytes = RemittanceAdvicePdfGenerator.render_pdf_bytes(
            signed_html,
            title=f"Signed Remittance Advice {remittance.remittance_number}",
        )

        signed_pdf, signed_hash = RemittanceAdvicePdfGenerator.sign_remittance_pdf(
            pdf_bytes=pdf_bytes,
            cert_pem=signing_cert,
            key_pem=signing_key,
        )

        # Verify signed PDF structure
        verify_result = RemittanceAdvicePdfGenerator.verify_remittance_pdf(signed_pdf)

        manifest = {
            "signer_name": signer_name,
            "signer_role": signer_role,
            "signature_reason": signature_reason,
            "signed_at": now.isoformat(),
            "pdf_sha256_hash": signed_hash,
            "cert_fingerprint": cert_fp,
            "verification_status": verify_result.status,
            "is_valid": verify_result.is_valid,
        }

        remittance.is_signed = True
        remittance.signed_by = signer_name
        remittance.signed_at = now
        remittance.signer_role = signer_role
        remittance.pdf_sha256_hash = signed_hash
        remittance.pdf_content = base64.b64encode(signed_pdf).decode("utf-8")
        remittance.signature_manifest = manifest
        remittance.reason_for_change = (
            reason_for_change
            or f"21 CFR Part 11 digital signature applied by {signer_name}"
        )

        saved_remittance = await self.repo.save_remittance_advice(remittance)

        await self.repo.record_audit_log(
            entity_type="REMITTANCE_ADVICE",
            entity_id=saved_remittance.id,
            action=AuditAction.REMITTANCE_ADVICE_SIGNED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "remittance_number": saved_remittance.remittance_number,
                "is_signed": True,
                "signed_by": signer_name,
                "signed_at": str(now),
                "pdf_sha256_hash": signed_hash,
                "manifest": manifest,
            },
            details=f"Remittance advice {saved_remittance.remittance_number} cryptographically signed by {signer_name} (SHA-256: {signed_hash[:16]}...).",
            reason_for_change=signature_reason,
            version_index=saved_remittance.version_index,
        )

        return saved_remittance

    async def verify_remittance_advice_signature(
        self, invoice_id: str
    ) -> dict[str, Any]:
        """Cryptographically verifies electronic signature and tamper resistance of remittance advice."""
        remittance = await self.repo.get_remittance_advice_by_invoice_id(invoice_id)
        if not remittance:
            raise ValueError(
                f"No remittance advice document found for invoice '{invoice_id}'."
            )

        if not remittance.is_signed or not remittance.pdf_content:
            return {
                "is_valid": False,
                "status": "UNSIGNED",
                "failure_reason": "Remittance advice document has not been electronically signed yet.",
                "pdf_sha256_hash": remittance.pdf_sha256_hash,
                "signed_by": None,
                "signed_at": None,
                "signer_role": None,
            }

        signed_bytes = base64.b64decode(remittance.pdf_content)
        res = RemittanceAdvicePdfGenerator.verify_remittance_pdf(signed_bytes)

        return {
            "is_valid": res.is_valid,
            "status": res.status,
            "failure_reason": res.failure_reason if not res.is_valid else None,
            "pdf_sha256_hash": remittance.pdf_sha256_hash,
            "signed_by": remittance.signed_by,
            "signed_at": remittance.signed_at,
            "signer_role": remittance.signer_role,
        }

    async def dispatch_remittance_advice(
        self,
        invoice_id: str,
        recipient_email: str,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> RemittanceAdviceModel:
        """Dispatches signed remittance advice statement to site financial contact.

        Requirements: PRD-PAY-004, PRD-PAY-006
        """
        remittance = await self.repo.get_remittance_advice_by_invoice_id(invoice_id)
        if not remittance:
            raise ValueError(
                f"No remittance advice document found for invoice '{invoice_id}'."
            )

        if not remittance.is_signed:
            raise ValueError(
                "Cannot dispatch unsigned remittance advice. Sign the document prior to dispatch."
            )

        now = datetime.now(UTC)
        remittance.dispatch_status = RemittanceDispatchStatus.DISPATCHED.value
        remittance.dispatched_to = recipient_email
        remittance.dispatched_at = now
        remittance.reason_for_change = (
            reason_for_change
            or f"Dispatched signed remittance advice to {recipient_email}"
        )

        saved = await self.repo.save_remittance_advice(remittance)

        await self.repo.record_audit_log(
            entity_type="REMITTANCE_ADVICE",
            entity_id=saved.id,
            action=AuditAction.REMITTANCE_ADVICE_DISPATCHED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "dispatch_status": "DISPATCHED",
                "dispatched_to": recipient_email,
                "dispatched_at": str(now),
            },
            details=f"Remittance advice {saved.remittance_number} dispatched to '{recipient_email}'.",
            reason_for_change=reason_for_change or "Site Remittance Email Notification",
            version_index=saved.version_index,
        )
        return saved

    # ==========================================
    # 9. Enterprise ERP Synchronization (SAP / NetSuite) & Webhooks
    # ==========================================

    async def export_invoice_to_erp(
        self,
        invoice_id: str,
        erp_system: str = "SAP",
        user_id: str = "system",
        user_roles: str = "Grants Manager",
    ) -> ErpExportEnvelope:
        """Generates ERP export payload and marks invoice for enterprise ERP sync.

        Requirements: PRD-PAY-004, PRD-PAY-006
        """
        invoice = await self.repo.get_invoice_by_id(invoice_id)
        if not invoice:
            raise ValueError(f"Invoice with ID '{invoice_id}' not found.")

        now = datetime.now(UTC)
        export_timestamp = now.isoformat()

        # Build standard ERP payload
        line_items_data = [
            {
                "line_item_id": item.id,
                "payable_id": item.payable_id,
                "subject_id": item.subject_id,
                "visit_code": item.visit_code,
                "procedure_code": item.procedure_code,
                "procedure_name": item.procedure_name,
                "base_amount": item.base_amount,
                "overhead_amount": item.overhead_amount,
                "gross_amount": item.gross_amount,
                "withholding_amount": item.withholding_amount,
                "net_amount": item.net_amount,
            }
            for item in invoice.line_items
        ]

        split_payees = await self.repo.list_split_payees(
            contract_id=invoice.contract_id, is_active_only=True
        )
        payee_data = [
            {
                "payee_id": sp.payee_id,
                "payee_name": sp.payee_name,
                "payee_role": sp.payee_role,
                "allocation_percentage": sp.allocation_percentage,
                "bank_account_reference": sp.bank_account_reference,
                "tax_identifier": sp.tax_identifier,
                "net_disbursement": round(
                    invoice.total_net_amount * (sp.allocation_percentage / 100.0),
                    2,
                ),
            }
            for sp in split_payees
        ]

        erp_payload = {
            "erp_system": erp_system,
            "document_type": "SELF_BILLING_INVOICE",
            "invoice_number": invoice.invoice_number,
            "study_id": invoice.study_id,
            "site_id": invoice.site_id,
            "billing_period": f"{invoice.period_start}..{invoice.period_end}",
            "currency": invoice.currency,
            "total_amount": invoice.total_gross_amount,
            "net_amount": invoice.total_net_amount,
            "holdback_amount": invoice.total_holdback_amount,
            "payables_count": invoice.payable_count,
            "line_items": line_items_data,
            "payee_distributions": payee_data,
            "export_timestamp": export_timestamp,
        }

        # Update invoice ERP status
        invoice.erp_system = erp_system
        invoice.erp_sync_status = ErpSyncStatus.SYNCED.value
        invoice.erp_synced_at = now
        await self.repo.save_invoice(invoice)

        # Record ERP Sync transaction log
        sync_log = ErpSyncLogModel(
            id=str(uuid.uuid4()),
            invoice_id=invoice.id,
            erp_system=erp_system,
            sync_direction="OUTBOUND_EXPORT",
            status="SYNCED",
            payload=erp_payload,
            external_reference=invoice.invoice_number,
            reconciled=False,
            created_by=user_id,
            reason_for_change=f"Outbound ERP export to {erp_system}",
            version_index=invoice.version_index,
        )
        await self.repo.save_erp_sync_log(sync_log)

        await self.repo.record_audit_log(
            entity_type="INVOICE",
            entity_id=invoice.id,
            action=AuditAction.ERP_PAYMENT_SYNCED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "erp_system": erp_system,
                "erp_sync_status": "SYNCED",
                "invoice_number": invoice.invoice_number,
                "export_timestamp": export_timestamp,
            },
            details=f"Exported invoice {invoice.invoice_number} to {erp_system} for financial execution.",
            reason_for_change="Enterprise ERP Synchronization",
            version_index=invoice.version_index,
        )

        return ErpExportEnvelope(**erp_payload)

    async def process_erp_webhook(
        self,
        payload: ErpWebhookPayload,
        user_id: str = "system",
        user_roles: str = "ERP Connector",
    ) -> dict[str, Any]:
        """Processes inbound ERP webhook status updates (SAP/NetSuite) with payment reconciliation.

        Requirements: PRD-PAY-004, PRD-PAY-006
        """
        # Identify target invoice
        invoice: SelfBillingInvoiceModel | None = None
        if payload.invoice_id:
            invoice = await self.repo.get_invoice_by_id(payload.invoice_id)
        elif payload.invoice_number:
            invoice = await self.repo.get_invoice_by_number(payload.invoice_number)

        if not invoice:
            raise ValueError(
                f"Target invoice not found for webhook reference '{payload.invoice_id or payload.invoice_number}'."
            )

        prev_status = invoice.status
        now = datetime.now(UTC)
        is_reconciled = False

        # Status transition mapping
        normalized_status = payload.status.upper()
        if normalized_status in ["PAID"]:
            invoice.status = InvoiceStatus.PAID.value
            invoice.erp_sync_status = ErpSyncStatus.SYNCED.value
            # Mark underlying payables as PAID
            for item in invoice.line_items:
                if item.payable:
                    item.payable.status = PayableStatus.PAID.value
                    await self.repo.save_payable(item.payable)

        elif normalized_status in ["RECONCILED"]:
            invoice.status = InvoiceStatus.RECONCILED.value
            invoice.erp_sync_status = ErpSyncStatus.RECONCILED.value
            invoice.reconciled_by = f"ERP_{payload.erp_system}"
            invoice.reconciled_at = now
            invoice.reconciliation_reference = (
                payload.bank_reference or payload.erp_reference_id
            )
            invoice.reconciliation_notes = (
                payload.notes
                or f"Automated ERP reconciliation from {payload.erp_system}"
            )
            is_reconciled = True
            # Mark underlying payables as PAID
            for item in invoice.line_items:
                if item.payable:
                    item.payable.status = PayableStatus.PAID.value
                    await self.repo.save_payable(item.payable)

        elif normalized_status in ["APPROVED"]:
            invoice.status = InvoiceStatus.APPROVED.value

        invoice.erp_system = payload.erp_system
        invoice.erp_reference_id = payload.erp_reference_id
        invoice.reason_for_change = f"ERP webhook update from {payload.erp_system} to status '{normalized_status}'"
        saved_invoice = await self.repo.save_invoice(invoice)

        # Log ERP webhook
        sync_log = ErpSyncLogModel(
            id=str(uuid.uuid4()),
            invoice_id=invoice.id,
            erp_system=payload.erp_system,
            sync_direction="INBOUND_WEBHOOK",
            status=normalized_status,
            payload=payload.model_dump(),
            external_reference=payload.erp_reference_id,
            reconciled=is_reconciled,
            created_by=user_id,
            reason_for_change=f"Inbound webhook from {payload.erp_system}",
            version_index=invoice.version_index,
        )
        await self.repo.save_erp_sync_log(sync_log)

        action_name = (
            AuditAction.INVOICE_RECONCILED.value
            if is_reconciled
            else AuditAction.ERP_WEBHOOK_PROCESSED.value
        )

        await self.repo.record_audit_log(
            entity_type="INVOICE",
            entity_id=saved_invoice.id,
            action=action_name,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={
                "status": saved_invoice.status,
                "erp_system": payload.erp_system,
                "erp_reference_id": payload.erp_reference_id,
                "erp_sync_status": saved_invoice.erp_sync_status,
            },
            details=(
                f"Processed ERP webhook from {payload.erp_system} for invoice {saved_invoice.invoice_number} "
                f"transitioning from '{prev_status}' to '{saved_invoice.status}' (Ref: {payload.erp_reference_id})."
            ),
            reason_for_change=f"ERP Synchronization ({payload.erp_system})",
            version_index=saved_invoice.version_index,
        )

        return {
            "status": "success",
            "invoice_id": saved_invoice.id,
            "invoice_number": saved_invoice.invoice_number,
            "erp_system": payload.erp_system,
            "erp_reference_id": payload.erp_reference_id,
            "previous_status": prev_status,
            "new_status": saved_invoice.status,
            "reconciled": is_reconciled,
            "message": f"Successfully processed {payload.erp_system} webhook for invoice {saved_invoice.invoice_number}.",
        }

    async def reconcile_invoice_payment(
        self,
        invoice_id: str,
        payment_reference: str,
        erp_system: str = "SAP",
        payment_date: str | None = None,
        amount_paid: float = 0.0,
        notes: str | None = None,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str | None = None,
    ) -> dict[str, Any]:
        """Formally reconciles bank/ERP disbursement with self-billing invoice and underlying payables.

        Requirements: PRD-PAY-004, PRD-PAY-006
        """
        invoice = await self.repo.get_invoice_by_id(invoice_id)
        if not invoice:
            raise ValueError(f"Invoice with ID '{invoice_id}' not found.")

        # Financial reconciliation validation
        if amount_paid < 0:
            raise ValueError("Reconciliation amount paid cannot be negative.")

        prev_status = invoice.status
        now = datetime.now(UTC)

        invoice.status = InvoiceStatus.RECONCILED.value
        invoice.erp_sync_status = ErpSyncStatus.RECONCILED.value
        invoice.erp_system = erp_system
        invoice.erp_reference_id = payment_reference
        invoice.reconciled_by = user_id
        invoice.reconciled_at = now
        invoice.reconciliation_reference = payment_reference
        invoice.reconciliation_notes = (
            notes
            or f"Reconciled disbursement against {erp_system} reference {payment_reference}"
        )
        invoice.reason_for_change = (
            reason_for_change or f"Payment reconciliation confirmed by {user_id}"
        )

        saved_invoice = await self.repo.save_invoice(invoice)

        # Mark all associated payables as PAID
        reconciled_count = 0
        for item in saved_invoice.line_items:
            if item.payable:
                item.payable.status = PayableStatus.PAID.value
                item.payable.reason_for_change = (
                    f"Payment reconciled under invoice {saved_invoice.invoice_number}"
                )
                await self.repo.save_payable(item.payable)
                reconciled_count += 1

        # Record ERP Sync Log
        sync_log = ErpSyncLogModel(
            id=str(uuid.uuid4()),
            invoice_id=invoice.id,
            erp_system=erp_system,
            sync_direction="RECONCILIATION",
            status="RECONCILED",
            payload={
                "payment_reference": payment_reference,
                "amount_paid": amount_paid,
                "payment_date": payment_date,
                "notes": notes,
            },
            external_reference=payment_reference,
            reconciled=True,
            created_by=user_id,
            reason_for_change="Bank Payment Reconciliation",
            version_index=invoice.version_index,
        )
        await self.repo.save_erp_sync_log(sync_log)

        await self.repo.record_audit_log(
            entity_type="INVOICE",
            entity_id=saved_invoice.id,
            action=AuditAction.INVOICE_RECONCILED.value,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={
                "status": "RECONCILED",
                "erp_sync_status": "RECONCILED",
                "reconciliation_reference": payment_reference,
                "amount_paid": amount_paid,
            },
            details=(
                f"Reconciled invoice {saved_invoice.invoice_number} (${amount_paid:.2f} paid) "
                f"against reference '{payment_reference}' across {reconciled_count} payables."
            ),
            reason_for_change=reason_for_change
            or "Financial Payment Settlement Reconciliation",
            version_index=saved_invoice.version_index,
        )

        return {
            "invoice_id": saved_invoice.id,
            "invoice_number": saved_invoice.invoice_number,
            "status": saved_invoice.status,
            "erp_sync_status": saved_invoice.erp_sync_status,
            "reconciled_by": user_id,
            "reconciled_at": now,
            "reconciliation_reference": payment_reference,
            "total_net_amount": saved_invoice.total_net_amount,
            "amount_paid": amount_paid,
            "reconciled_payables_count": reconciled_count,
        }

    async def list_erp_sync_logs(
        self,
        invoice_id: str | None = None,
        erp_system: str | None = None,
        status: str | None = None,
    ) -> list[ErpSyncLogModel]:
        """Lists ERP synchronization logs and webhooks."""
        return await self.repo.list_erp_sync_logs(
            invoice_id=invoice_id, erp_system=erp_system, status=status
        )
