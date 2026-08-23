import uuid
from datetime import UTC, datetime
from typing import Any

from apps.ctms.domain.models import (
    BudgetAdjustmentEntity,
    CTMSAuditLogEntity,
    CurrencyExchangeRateEntity,
    FinancialInvoiceEntity,
    GrantContractAmendmentEntity,
    ProcedurePaymentGridEntity,
    SiteBudgetTemplateEntity,
)
from apps.ctms.domain.ports import ICTMSDelegationRepository, IFinancialsRepository


class FinancialsService:
    """Application service for Procedure-Based Fee Matrices, Investigator Grant Contracts, and Invoicing."""

    def __init__(
        self,
        financials_repo: IFinancialsRepository,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.financials_repo = financials_repo
        self.doa_repo = doa_repo

    # ==========================================
    # 1. Site Budget Templates (Multi-Tier)
    # ==========================================

    async def create_budget_template(
        self,
        template_name: str,
        study_id: str | None,
        tier_level: str,
        currency: str,
        overhead_percentage: float,
        withholding_percentage: float,
        fixed_startup_fee: float,
        fixed_closeout_fee: float,
        irb_fee: float,
        patient_stipend_amount: float,
        travel_reimbursement_rate: float,
        fixed_fees: list[dict[str, Any]],
        procedure_items: list[dict[str, Any]],
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> SiteBudgetTemplateEntity:
        entity = SiteBudgetTemplateEntity(
            template_name=template_name,
            study_id=study_id,
            tier_level=tier_level.upper(),
            currency=currency.upper(),
            overhead_percentage=overhead_percentage,
            withholding_percentage=withholding_percentage,
            fixed_startup_fee=fixed_startup_fee,
            fixed_closeout_fee=fixed_closeout_fee,
            irb_fee=irb_fee,
            patient_stipend_amount=patient_stipend_amount,
            travel_reimbursement_rate=travel_reimbursement_rate,
            fixed_fees=fixed_fees,
            procedure_items=procedure_items,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.financials_repo.save_budget_template(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="SITE_BUDGET_TEMPLATE_CREATED",
                details=f"Created budget template '{template_name}' ({tier_level}, {currency}). Overhead: {overhead_percentage}%. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def get_budget_template(
        self, template_id: str
    ) -> SiteBudgetTemplateEntity | None:
        return await self.financials_repo.get_budget_template(template_id)

    async def list_budget_templates(
        self, study_id: str | None = None
    ) -> list[SiteBudgetTemplateEntity]:
        return await self.financials_repo.list_budget_templates(study_id)

    async def apply_template_to_grant(
        self,
        grant_id: str,
        template_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> list[ProcedurePaymentGridEntity]:
        template = await self.financials_repo.get_budget_template(template_id)
        if not template:
            raise ValueError(f"Budget template {template_id} not found")

        created_grids: list[ProcedurePaymentGridEntity] = []
        for item in template.procedure_items:
            grid_entity = ProcedurePaymentGridEntity(
                grant_id=grant_id,
                visit_name=item.get("visit_name", "UNSCHEDULED").upper(),
                activity_id=item.get("activity_id"),
                form_id=item.get("form_id"),
                procedure_code=item.get("procedure_code", "PROC-GEN").upper(),
                procedure_name=item.get("procedure_name", "Procedure"),
                base_amount=float(item.get("base_amount", 0.0)),
                overhead_percentage=float(
                    item.get("overhead_percentage", template.overhead_percentage)
                ),
                withholding_percentage=float(
                    item.get("withholding_percentage", template.withholding_percentage)
                ),
                patient_stipend=float(
                    item.get("patient_stipend", template.patient_stipend_amount)
                ),
                travel_reimbursement=float(
                    item.get("travel_reimbursement", template.travel_reimbursement_rate)
                ),
                is_active=True,
                created_by=user_id,
                reason_for_change=reason_for_change,
                version_index=1,
            )
            saved_grid = await self.financials_repo.save_procedure_grid(grid_entity)
            created_grids.append(saved_grid)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="TEMPLATE_APPLIED_TO_GRANT",
                details=f"Applied template {template.template_name} to grant {grant_id} ({len(created_grids)} procedure items). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return created_grids

    # ==========================================
    # 2. Procedure Payment Grid
    # ==========================================

    async def add_procedure_to_grid(
        self,
        grant_id: str,
        visit_name: str,
        procedure_code: str,
        procedure_name: str,
        base_amount: float,
        overhead_percentage: float = 0.0,
        withholding_percentage: float = 0.0,
        activity_id: str | None = None,
        form_id: str | None = None,
        patient_stipend: float = 0.0,
        travel_reimbursement: float = 0.0,
        user_id: str = "system",
        user_roles: str = "Grants Manager",
        reason_for_change: str = "Procedure grid entry",
        version_index: int = 1,
    ) -> ProcedurePaymentGridEntity:
        entity = ProcedurePaymentGridEntity(
            grant_id=grant_id,
            visit_name=visit_name.upper(),
            activity_id=activity_id,
            form_id=form_id,
            procedure_code=procedure_code.upper(),
            procedure_name=procedure_name,
            base_amount=base_amount,
            overhead_percentage=overhead_percentage,
            withholding_percentage=withholding_percentage,
            patient_stipend=patient_stipend,
            travel_reimbursement=travel_reimbursement,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=version_index,
        )
        saved = await self.financials_repo.save_procedure_grid(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="PROCEDURE_GRID_ADDED",
                details=f"Added procedure {procedure_code} ({procedure_name}) to grant {grant_id} for visit {visit_name} (${base_amount}, form={form_id}, v={version_index}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_procedure_grids(
        self,
        grant_id: str,
        is_active_only: bool = False,
        version_index: int | None = None,
    ) -> list[ProcedurePaymentGridEntity]:
        return await self.financials_repo.list_procedure_grids(
            grant_id, is_active_only=is_active_only, version_index=version_index
        )

    # ==========================================
    # 3. Visit Payable Calculation & Multi-Currency
    # ==========================================

    async def calculate_visit_payable(
        self,
        grant_id: str,
        visit_name: str,
        version_index: int | None = None,
        grant_currency: str = "USD",
        target_currency: str | None = None,
    ) -> dict[str, Any]:
        grids = await self.financials_repo.list_procedure_grids(
            grant_id, is_active_only=True, version_index=version_index
        )
        matching = [
            g for g in grids if g.visit_name == visit_name.upper() and g.is_active
        ]

        base_total = sum(g.base_amount for g in matching)
        overhead_total = sum(
            g.base_amount * (g.overhead_percentage / 100.0) for g in matching
        )
        stipend_total = sum(g.patient_stipend for g in matching)
        travel_total = sum(g.travel_reimbursement for g in matching)

        gross = base_total + overhead_total + stipend_total + travel_total
        withholding = sum(
            (g.base_amount * (1 + g.overhead_percentage / 100.0))
            * (g.withholding_percentage / 100.0)
            for g in matching
        )
        net = gross - withholding

        exchange_rate = 1.0
        converted_net = round(net, 2)
        target_curr = (
            target_currency.upper() if target_currency else grant_currency.upper()
        )

        if target_curr != grant_currency.upper():
            rate_entity = await self.financials_repo.get_exchange_rate(
                grant_currency.upper(), target_curr
            )
            if rate_entity:
                exchange_rate = rate_entity.rate
                converted_net = round(net * exchange_rate, 2)

        return {
            "visit_name": visit_name.upper(),
            "procedures_count": len(matching),
            "base_amount": round(base_total, 2),
            "overhead_amount": round(overhead_total, 2),
            "patient_stipend_total": round(stipend_total, 2),
            "travel_reimbursement_total": round(travel_total, 2),
            "gross_amount": round(gross, 2),
            "withholding_amount": round(withholding, 2),
            "net_amount": round(net, 2),
            "currency": grant_currency.upper(),
            "target_currency": target_curr,
            "exchange_rate": exchange_rate,
            "converted_net_amount": converted_net,
            "version_index": version_index,
        }

    # ==========================================
    # 4. Contract Amendments & Versioning
    # ==========================================

    async def execute_contract_amendment(
        self,
        grant_id: str,
        summary_of_changes: str,
        revised_budget: float,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        updated_procedures: list[dict[str, Any]] | None = None,
        signature_manifestation: dict[str, Any] | None = None,
    ) -> GrantContractAmendmentEntity:
        existing_amendments = await self.financials_repo.list_grant_amendments(grant_id)
        amendment_num = len(existing_amendments) + 1
        grant_entity = await self.financials_repo.get_investigator_grant(grant_id)

        if existing_amendments:
            previous_budget = existing_amendments[-1].revised_budget
            previous_version = existing_amendments[-1].new_version_index
        elif grant_entity:
            previous_budget = grant_entity.total_budget
            previous_version = grant_entity.version_index
        else:
            previous_budget = revised_budget
            previous_version = 1

        new_version = previous_version + 1

        amendment = GrantContractAmendmentEntity(
            grant_id=grant_id,
            amendment_number=amendment_num,
            effective_date=datetime.now(UTC).isoformat(),
            previous_budget=previous_budget,
            revised_budget=revised_budget,
            previous_version_index=previous_version,
            new_version_index=new_version,
            summary_of_changes=summary_of_changes,
            status="EXECUTED",
            approved_by=user_id,
            approved_at=datetime.now(UTC).isoformat(),
            signature_manifestation=signature_manifestation,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=new_version,
        )
        saved_amendment = await self.financials_repo.save_grant_amendment(amendment)

        # Update grant's total budget and version_index
        await self.financials_repo.update_grant_budget_and_version(
            grant_id, revised_budget, new_version
        )

        # Preserve historical fee schedule: add updated procedure records under new version_index
        if updated_procedures:
            for item in updated_procedures:
                grid_entity = ProcedurePaymentGridEntity(
                    grant_id=grant_id,
                    visit_name=item.get("visit_name", "UNSCHEDULED").upper(),
                    activity_id=item.get("activity_id"),
                    form_id=item.get("form_id"),
                    procedure_code=item.get("procedure_code", "PROC-GEN").upper(),
                    procedure_name=item.get("procedure_name", "Procedure"),
                    base_amount=float(item.get("base_amount", 0.0)),
                    overhead_percentage=float(item.get("overhead_percentage", 0.0)),
                    withholding_percentage=float(
                        item.get("withholding_percentage", 0.0)
                    ),
                    patient_stipend=float(item.get("patient_stipend", 0.0)),
                    travel_reimbursement=float(item.get("travel_reimbursement", 0.0)),
                    is_active=True,
                    created_by=user_id,
                    reason_for_change=f"Amendment #{amendment_num}: {reason_for_change}",
                    version_index=new_version,
                )
                await self.financials_repo.save_procedure_grid(grid_entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="GRANT_CONTRACT_AMENDMENT_EXECUTED",
                details=f"Executed Amendment #{amendment_num} on grant {grant_id} (Budget: ${previous_budget} -> ${revised_budget}, v{previous_version}->v{new_version}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved_amendment

    async def list_grant_amendments(
        self, grant_id: str
    ) -> list[GrantContractAmendmentEntity]:
        return await self.financials_repo.list_grant_amendments(grant_id)

    async def get_grant_amendment(
        self, amendment_id: str
    ) -> GrantContractAmendmentEntity | None:
        return await self.financials_repo.get_grant_amendment(amendment_id)

    # ==========================================
    # 5. Currency Exchange Rates
    # ==========================================

    async def set_exchange_rate(
        self,
        base_currency: str,
        target_currency: str,
        rate: float,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        expiry_date: str | None = None,
    ) -> CurrencyExchangeRateEntity:
        entity = CurrencyExchangeRateEntity(
            base_currency=base_currency.upper(),
            target_currency=target_currency.upper(),
            rate=rate,
            effective_date=datetime.now(UTC).isoformat(),
            expiry_date=expiry_date,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.financials_repo.save_exchange_rate(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="EXCHANGE_RATE_CONFIGURED",
                details=f"Configured FX rate: 1 {base_currency.upper()} = {rate} {target_currency.upper()}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def get_exchange_rate(
        self, base_currency: str, target_currency: str
    ) -> CurrencyExchangeRateEntity | None:
        return await self.financials_repo.get_exchange_rate(
            base_currency, target_currency
        )

    async def list_exchange_rates(self) -> list[CurrencyExchangeRateEntity]:
        return await self.financials_repo.list_exchange_rates()

    async def convert_currency(
        self, amount: float, from_currency: str, to_currency: str
    ) -> dict[str, Any]:
        if from_currency.upper() == to_currency.upper():
            return {
                "original_amount": round(amount, 2),
                "from_currency": from_currency.upper(),
                "to_currency": to_currency.upper(),
                "exchange_rate": 1.0,
                "converted_amount": round(amount, 2),
            }

        rate_entity = await self.financials_repo.get_exchange_rate(
            from_currency, to_currency
        )
        rate = rate_entity.rate if rate_entity else 1.0
        converted = round(amount * rate, 2)
        return {
            "original_amount": round(amount, 2),
            "from_currency": from_currency.upper(),
            "to_currency": to_currency.upper(),
            "exchange_rate": rate,
            "converted_amount": converted,
        }

    # ==========================================
    # 6. Budget Adjustments (Passthroughs & Penalties)
    # ==========================================

    async def create_budget_adjustment(
        self,
        grant_id: str,
        site_id: str,
        study_id: str,
        adjustment_type: str,
        description: str,
        amount: float,
        currency: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> BudgetAdjustmentEntity:
        entity = BudgetAdjustmentEntity(
            grant_id=grant_id,
            site_id=site_id,
            study_id=study_id,
            adjustment_type=adjustment_type.upper(),
            description=description,
            amount=amount,
            currency=currency.upper(),
            status="PENDING",
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.financials_repo.save_budget_adjustment(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="BUDGET_ADJUSTMENT_CREATED",
                details=f"Created {adjustment_type} adjustment ({amount} {currency}) for site {site_id}. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def approve_budget_adjustment(
        self,
        adjustment_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> BudgetAdjustmentEntity:
        adjustment = await self.financials_repo.get_budget_adjustment(adjustment_id)
        if not adjustment:
            raise ValueError(f"Budget adjustment {adjustment_id} not found")

        adjustment.status = "APPROVED"
        adjustment.approved_by = user_id
        adjustment.approved_at = datetime.now(UTC).isoformat()
        adjustment.version_index += 1
        adjustment.reason_for_change = reason_for_change

        saved = await self.financials_repo.save_budget_adjustment(adjustment)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="BUDGET_ADJUSTMENT_APPROVED",
                details=f"Approved adjustment {adjustment_id} ({adjustment.amount} {adjustment.currency}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_budget_adjustments(
        self,
        grant_id: str | None = None,
        study_id: str | None = None,
        site_id: str | None = None,
    ) -> list[BudgetAdjustmentEntity]:
        return await self.financials_repo.list_budget_adjustments(
            grant_id=grant_id, study_id=study_id, site_id=site_id
        )

    # ==========================================
    # 7. Invoices & Disbursements
    # ==========================================

    async def create_batch_invoice(
        self,
        study_id: str,
        site_id: str,
        grant_id: str,
        invoice_type: str,
        gross_amount: float,
        withholding_amount: float,
        currency: str,
        payable_ids: list[str],
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> FinancialInvoiceEntity:
        net_amount = gross_amount - withholding_amount
        invoice_num = f"INV-{site_id[:4]}-{datetime.now(UTC).strftime('%Y%m')}-{uuid.uuid4().hex[:4].upper()}"

        entity = FinancialInvoiceEntity(
            study_id=study_id,
            site_id=site_id,
            grant_id=grant_id,
            invoice_number=invoice_num,
            invoice_type=invoice_type.upper(),
            gross_amount=gross_amount,
            withholding_amount=withholding_amount,
            net_amount=net_amount,
            currency=currency.upper(),
            status="DRAFT",
            payable_ids=payable_ids,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.financials_repo.save_invoice(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="FINANCIAL_INVOICE_CREATED",
                details=f"Created invoice {invoice_num} for site {site_id} (Net: {net_amount} {currency}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def approve_and_disburse_invoice(
        self,
        invoice_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> FinancialInvoiceEntity:
        invoice = await self.financials_repo.get_invoice(invoice_id)
        if not invoice:
            raise ValueError(f"Invoice {invoice_id} not found")

        invoice.status = "DISBURSED"
        invoice.approved_by = user_id
        invoice.approved_at = datetime.now(UTC).isoformat()
        invoice.disbursed_at = datetime.now(UTC).isoformat()
        invoice.version_index += 1
        invoice.reason_for_change = reason_for_change

        saved = await self.financials_repo.save_invoice(invoice)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="FINANCIAL_INVOICE_DISBURSED",
                details=f"Approved and disbursed invoice {invoice.invoice_number} ({invoice.net_amount} {invoice.currency}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_invoices(
        self, study_id: str, site_id: str | None = None
    ) -> list[FinancialInvoiceEntity]:
        return await self.financials_repo.list_invoices(study_id, site_id)
