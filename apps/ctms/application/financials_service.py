import uuid
from datetime import UTC, datetime

from apps.ctms.domain.models import (
    CTMSAuditLogEntity,
    FinancialInvoiceEntity,
    ProcedurePaymentGridEntity,
)
from apps.ctms.domain.ports import ICTMSDelegationRepository, IFinancialsRepository


class FinancialsService:
    """Application service for Procedure-Based Financials, EDC Auto-Payables, and Invoices."""

    def __init__(
        self,
        financials_repo: IFinancialsRepository,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.financials_repo = financials_repo
        self.doa_repo = doa_repo

    async def add_procedure_to_grid(
        self,
        grant_id: str,
        visit_name: str,
        procedure_code: str,
        procedure_name: str,
        base_amount: float,
        overhead_percentage: float,
        withholding_percentage: float,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> ProcedurePaymentGridEntity:
        entity = ProcedurePaymentGridEntity(
            grant_id=grant_id,
            visit_name=visit_name.upper(),
            procedure_code=procedure_code.upper(),
            procedure_name=procedure_name,
            base_amount=base_amount,
            overhead_percentage=overhead_percentage,
            withholding_percentage=withholding_percentage,
            is_active=True,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.financials_repo.save_procedure_grid(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="PROCEDURE_GRID_ADDED",
                details=f"Added procedure {procedure_code} ({procedure_name}) to grant {grant_id} for visit {visit_name} (${base_amount}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_procedure_grids(
        self, grant_id: str
    ) -> list[ProcedurePaymentGridEntity]:
        return await self.financials_repo.list_procedure_grids(grant_id)

    async def calculate_visit_payable(
        self, grant_id: str, visit_name: str
    ) -> dict[str, float]:
        grids = await self.financials_repo.list_procedure_grids(grant_id)
        matching = [
            g for g in grids if g.visit_name == visit_name.upper() and g.is_active
        ]

        gross = sum(
            g.base_amount * (1 + g.overhead_percentage / 100.0) for g in matching
        )
        withholding = sum(
            (g.base_amount * (1 + g.overhead_percentage / 100.0))
            * (g.withholding_percentage / 100.0)
            for g in matching
        )
        net = gross - withholding
        return {
            "gross_amount": round(gross, 2),
            "withholding_amount": round(withholding, 2),
            "net_amount": round(net, 2),
        }

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
