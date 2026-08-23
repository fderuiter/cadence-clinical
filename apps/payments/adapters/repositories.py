"""SQLAlchemy Repository implementation for Site Payments microservice.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

import uuid
from typing import Any

from fastapi import Depends
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.payments.adapters.database import get_db_session
from apps.payments.domain.ports import IPaymentsRepository
from apps.payments.infrastructure.models import (
    BudgetContractModel,
    ErpSyncLogModel,
    FeeScheduleModel,
    HoldbackReserveModel,
    InvoiceLineItemModel,
    LaySummaryModel,
    LaySummaryReceiptModel,
    ParticipantDisbursementModel,
    ParticipantExpenseClaimModel,
    ParticipantStipendRuleModel,
    ParticipantTransactionModel,
    ParticipantWalletModel,
    PayableAllocationModel,
    PayableModel,
    PaymentAuditLogModel,
    RemittanceAdviceModel,
    SelfBillingInvoiceModel,
    SplitPayeeConfigModel,
)


class SQLAlchemyPaymentsRepository(IPaymentsRepository):
    """SQLAlchemy implementation of the Site Payments repository."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_active_contract(
        self, study_id: str, site_id: str
    ) -> BudgetContractModel | None:
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

    async def get_contract_by_id(self, contract_id: str) -> BudgetContractModel | None:
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

    async def save_contract(self, contract: BudgetContractModel) -> BudgetContractModel:
        if not contract.id:
            contract.id = str(uuid.uuid4())
        self.session.add(contract)
        await self.session.commit()
        await self.session.refresh(contract)
        return contract

    async def list_contracts(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[BudgetContractModel]:
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

    async def save_fee_schedule(
        self, fee_schedule: FeeScheduleModel
    ) -> FeeScheduleModel:
        if not fee_schedule.id:
            fee_schedule.id = str(uuid.uuid4())
        self.session.add(fee_schedule)
        await self.session.commit()
        await self.session.refresh(fee_schedule)
        return fee_schedule

    async def list_fee_schedules(
        self,
        contract_id: str,
        visit_code: str | None = None,
        form_id: str | None = None,
        is_active_only: bool = True,
    ) -> list[FeeScheduleModel]:
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

    async def save_split_payees(
        self, split_payees: list[SplitPayeeConfigModel]
    ) -> list[SplitPayeeConfigModel]:
        for payee in split_payees:
            if not payee.id:
                payee.id = str(uuid.uuid4())
            self.session.add(payee)
        await self.session.commit()
        for payee in split_payees:
            await self.session.refresh(payee)
        return split_payees

    async def list_split_payees(
        self, contract_id: str, is_active_only: bool = True
    ) -> list[SplitPayeeConfigModel]:
        stmt = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == contract_id
        )
        if is_active_only:
            stmt = stmt.where(SplitPayeeConfigModel.is_active.is_(True))
        stmt = stmt.order_by(desc(SplitPayeeConfigModel.allocation_percentage))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_payable(self, payable: PayableModel) -> PayableModel:
        if not payable.id:
            payable.id = str(uuid.uuid4())
        self.session.add(payable)
        await self.session.commit()
        await self.session.refresh(payable)
        return payable

    async def get_payable_by_id(self, payable_id: str) -> PayableModel | None:
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

    async def get_payable_by_event_id(self, event_id: str) -> PayableModel | None:
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
            stmt = (
                stmt.where(PayableModel.is_sdv_gated.is_(True))
                if is_sdv_gated
                else stmt.where(PayableModel.is_sdv_gated.is_(False))
            )
        stmt = stmt.order_by(desc(PayableModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_payable_allocations(
        self, allocations: list[PayableAllocationModel]
    ) -> list[PayableAllocationModel]:
        for alloc in allocations:
            if not alloc.id:
                alloc.id = str(uuid.uuid4())
            self.session.add(alloc)
        await self.session.commit()
        for alloc in allocations:
            await self.session.refresh(alloc)
        return allocations

    async def save_holdback_reserve(
        self, holdback: HoldbackReserveModel
    ) -> HoldbackReserveModel:
        if not holdback.id:
            holdback.id = str(uuid.uuid4())
        self.session.add(holdback)
        await self.session.commit()
        await self.session.refresh(holdback)
        return holdback

    async def get_holdback_reserve(
        self, holdback_id: str
    ) -> HoldbackReserveModel | None:
        stmt = select(HoldbackReserveModel).where(
            HoldbackReserveModel.id == holdback_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_holdback_reserves(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        status: str | None = None,
    ) -> list[HoldbackReserveModel]:
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

    # ==========================================
    # Self-Billing Invoices & Line Items
    # ==========================================

    async def save_invoice(
        self, invoice: SelfBillingInvoiceModel
    ) -> SelfBillingInvoiceModel:
        if not invoice.id:
            invoice.id = str(uuid.uuid4())
        self.session.add(invoice)
        await self.session.commit()
        await self.session.refresh(invoice)
        return invoice

    async def get_invoice_by_id(
        self, invoice_id: str
    ) -> SelfBillingInvoiceModel | None:
        stmt = (
            select(SelfBillingInvoiceModel)
            .where(SelfBillingInvoiceModel.id == invoice_id)
            .options(
                selectinload(SelfBillingInvoiceModel.line_items),
                selectinload(SelfBillingInvoiceModel.remittance_advice),
                selectinload(SelfBillingInvoiceModel.contract),
                selectinload(SelfBillingInvoiceModel.erp_logs),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_invoice_by_number(
        self, invoice_number: str
    ) -> SelfBillingInvoiceModel | None:
        stmt = (
            select(SelfBillingInvoiceModel)
            .where(SelfBillingInvoiceModel.invoice_number == invoice_number)
            .options(
                selectinload(SelfBillingInvoiceModel.line_items),
                selectinload(SelfBillingInvoiceModel.remittance_advice),
                selectinload(SelfBillingInvoiceModel.contract),
                selectinload(SelfBillingInvoiceModel.erp_logs),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_invoices(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        billing_cycle: str | None = None,
        status: str | None = None,
        erp_sync_status: str | None = None,
    ) -> list[SelfBillingInvoiceModel]:
        stmt = select(SelfBillingInvoiceModel).options(
            selectinload(SelfBillingInvoiceModel.line_items),
            selectinload(SelfBillingInvoiceModel.remittance_advice),
            selectinload(SelfBillingInvoiceModel.contract),
            selectinload(SelfBillingInvoiceModel.erp_logs),
        )
        if study_id:
            stmt = stmt.where(SelfBillingInvoiceModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(SelfBillingInvoiceModel.site_id == site_id)
        if billing_cycle:
            stmt = stmt.where(SelfBillingInvoiceModel.billing_cycle == billing_cycle)
        if status:
            stmt = stmt.where(SelfBillingInvoiceModel.status == status)
        if erp_sync_status:
            stmt = stmt.where(
                SelfBillingInvoiceModel.erp_sync_status == erp_sync_status
            )
        stmt = stmt.order_by(desc(SelfBillingInvoiceModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def save_invoice_line_items(
        self, line_items: list[InvoiceLineItemModel]
    ) -> list[InvoiceLineItemModel]:
        for item in line_items:
            if not item.id:
                item.id = str(uuid.uuid4())
            self.session.add(item)
        await self.session.commit()
        for item in line_items:
            await self.session.refresh(item)
        return line_items

    # ==========================================
    # Remittance Advice
    # ==========================================

    async def save_remittance_advice(
        self, remittance: RemittanceAdviceModel
    ) -> RemittanceAdviceModel:
        if not remittance.id:
            remittance.id = str(uuid.uuid4())
        self.session.add(remittance)
        await self.session.commit()
        await self.session.refresh(remittance)
        return remittance

    async def get_remittance_advice_by_id(
        self, remittance_id: str
    ) -> RemittanceAdviceModel | None:
        stmt = (
            select(RemittanceAdviceModel)
            .where(RemittanceAdviceModel.id == remittance_id)
            .options(selectinload(RemittanceAdviceModel.invoice))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_remittance_advice_by_invoice_id(
        self, invoice_id: str
    ) -> RemittanceAdviceModel | None:
        stmt = (
            select(RemittanceAdviceModel)
            .where(RemittanceAdviceModel.invoice_id == invoice_id)
            .options(selectinload(RemittanceAdviceModel.invoice))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_remittance_advices(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        dispatch_status: str | None = None,
    ) -> list[RemittanceAdviceModel]:
        stmt = select(RemittanceAdviceModel).options(
            selectinload(RemittanceAdviceModel.invoice)
        )
        if study_id:
            stmt = stmt.where(RemittanceAdviceModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(RemittanceAdviceModel.site_id == site_id)
        if dispatch_status:
            stmt = stmt.where(RemittanceAdviceModel.dispatch_status == dispatch_status)
        stmt = stmt.order_by(desc(RemittanceAdviceModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Enterprise ERP Synchronization
    # ==========================================

    async def save_erp_sync_log(self, sync_log: ErpSyncLogModel) -> ErpSyncLogModel:
        if not sync_log.id:
            sync_log.id = str(uuid.uuid4())
        self.session.add(sync_log)
        await self.session.commit()
        await self.session.refresh(sync_log)
        return sync_log

    async def list_erp_sync_logs(
        self,
        invoice_id: str | None = None,
        erp_system: str | None = None,
        status: str | None = None,
    ) -> list[ErpSyncLogModel]:
        stmt = select(ErpSyncLogModel)
        if invoice_id:
            stmt = stmt.where(ErpSyncLogModel.invoice_id == invoice_id)
        if erp_system:
            stmt = stmt.where(ErpSyncLogModel.erp_system == erp_system)
        if status:
            stmt = stmt.where(ErpSyncLogModel.status == status)
        stmt = stmt.order_by(desc(ErpSyncLogModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Audit Logs
    # ==========================================

    async def record_audit_log(
        self,
        entity_type: str,
        entity_id: str,
        action: str,
        user_id: str,
        user_roles: str,
        previous_state: dict[str, Any] | None = None,
        new_state: dict[str, Any] | None = None,
        details: str | None = None,
        reason_for_change: str | None = None,
        version_index: int = 1,
    ) -> PaymentAuditLogModel:
        log_entry = PaymentAuditLogModel(
            id=str(uuid.uuid4()),
            entity_type=entity_type,
            entity_id=entity_id,
            action=action,
            previous_state=previous_state,
            new_state=new_state,
            details=details,
            user_id=user_id,
            user_roles=user_roles,
            created_by=user_id,
            reason_for_change=reason_for_change
            or f"Logged financial transaction {action}",
            version_index=version_index,
        )
        self.session.add(log_entry)
        await self.session.commit()
        await self.session.refresh(log_entry)
        return log_entry

    async def list_audit_logs(
        self,
        entity_type: str | None = None,
        entity_id: str | None = None,
        user_id: str | None = None,
    ) -> list[PaymentAuditLogModel]:
        stmt = select(PaymentAuditLogModel)
        if entity_type:
            stmt = stmt.where(PaymentAuditLogModel.entity_type == entity_type)
        if entity_id:
            stmt = stmt.where(PaymentAuditLogModel.entity_id == entity_id)
        if user_id:
            stmt = stmt.where(PaymentAuditLogModel.user_id == user_id)
        stmt = stmt.order_by(desc(PaymentAuditLogModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Participant Financial Wallet Methods
    # ==========================================

    async def get_wallet_by_id(self, wallet_id: str) -> ParticipantWalletModel | None:
        stmt = (
            select(ParticipantWalletModel)
            .where(ParticipantWalletModel.id == wallet_id)
            .options(
                selectinload(ParticipantWalletModel.expense_claims),
                selectinload(ParticipantWalletModel.disbursements),
                selectinload(ParticipantWalletModel.transactions),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_wallet_by_subject(
        self, study_id: str, subject_id: str
    ) -> ParticipantWalletModel | None:
        stmt = (
            select(ParticipantWalletModel)
            .where(
                ParticipantWalletModel.study_id == study_id,
                ParticipantWalletModel.subject_id == subject_id,
            )
            .options(
                selectinload(ParticipantWalletModel.expense_claims),
                selectinload(ParticipantWalletModel.disbursements),
                selectinload(ParticipantWalletModel.transactions),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def save_wallet(
        self, wallet: ParticipantWalletModel
    ) -> ParticipantWalletModel:
        if not wallet.id:
            wallet.id = str(uuid.uuid4())
        self.session.add(wallet)
        await self.session.commit()
        await self.session.refresh(wallet)
        return wallet

    async def list_wallets(
        self, study_id: str | None = None, site_id: str | None = None
    ) -> list[ParticipantWalletModel]:
        stmt = select(ParticipantWalletModel).options(
            selectinload(ParticipantWalletModel.expense_claims),
            selectinload(ParticipantWalletModel.disbursements),
            selectinload(ParticipantWalletModel.transactions),
        )
        if study_id:
            stmt = stmt.where(ParticipantWalletModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(ParticipantWalletModel.site_id == site_id)
        stmt = stmt.order_by(desc(ParticipantWalletModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # SoA Protocol Stipend Rules
    # ==========================================

    async def save_stipend_rule(
        self, rule: ParticipantStipendRuleModel
    ) -> ParticipantStipendRuleModel:
        if not rule.id:
            rule.id = str(uuid.uuid4())
        self.session.add(rule)
        await self.session.commit()
        await self.session.refresh(rule)
        return rule

    async def get_stipend_rule_by_visit(
        self, study_id: str, visit_code: str
    ) -> ParticipantStipendRuleModel | None:
        stmt = select(ParticipantStipendRuleModel).where(
            ParticipantStipendRuleModel.study_id == study_id,
            ParticipantStipendRuleModel.visit_code == visit_code,
            ParticipantStipendRuleModel.is_active.is_(True),
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_stipend_rules(
        self, study_id: str | None = None, is_active_only: bool = True
    ) -> list[ParticipantStipendRuleModel]:
        stmt = select(ParticipantStipendRuleModel)
        if study_id:
            stmt = stmt.where(ParticipantStipendRuleModel.study_id == study_id)
        if is_active_only:
            stmt = stmt.where(ParticipantStipendRuleModel.is_active.is_(True))
        stmt = stmt.order_by(ParticipantStipendRuleModel.visit_code)
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Mobile Travel Expenses & Receipts
    # ==========================================

    async def save_expense_claim(
        self, claim: ParticipantExpenseClaimModel
    ) -> ParticipantExpenseClaimModel:
        if not claim.id:
            claim.id = str(uuid.uuid4())
        self.session.add(claim)
        await self.session.commit()
        await self.session.refresh(claim)
        return claim

    async def get_expense_claim_by_id(
        self, claim_id: str
    ) -> ParticipantExpenseClaimModel | None:
        stmt = select(ParticipantExpenseClaimModel).where(
            ParticipantExpenseClaimModel.id == claim_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_expense_claims(
        self,
        study_id: str | None = None,
        site_id: str | None = None,
        subject_id: str | None = None,
        status: str | None = None,
    ) -> list[ParticipantExpenseClaimModel]:
        stmt = select(ParticipantExpenseClaimModel)
        if study_id:
            stmt = stmt.where(ParticipantExpenseClaimModel.study_id == study_id)
        if site_id:
            stmt = stmt.where(ParticipantExpenseClaimModel.site_id == site_id)
        if subject_id:
            stmt = stmt.where(ParticipantExpenseClaimModel.subject_id == subject_id)
        if status:
            stmt = stmt.where(ParticipantExpenseClaimModel.status == status)
        stmt = stmt.order_by(desc(ParticipantExpenseClaimModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Digital Disbursements
    # ==========================================

    async def save_disbursement(
        self, disbursement: ParticipantDisbursementModel
    ) -> ParticipantDisbursementModel:
        if not disbursement.id:
            disbursement.id = str(uuid.uuid4())
        self.session.add(disbursement)
        await self.session.commit()
        await self.session.refresh(disbursement)
        return disbursement

    async def get_disbursement_by_id(
        self, disbursement_id: str
    ) -> ParticipantDisbursementModel | None:
        stmt = select(ParticipantDisbursementModel).where(
            ParticipantDisbursementModel.id == disbursement_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_disbursements(
        self,
        wallet_id: str | None = None,
        study_id: str | None = None,
        subject_id: str | None = None,
    ) -> list[ParticipantDisbursementModel]:
        stmt = select(ParticipantDisbursementModel)
        if wallet_id:
            stmt = stmt.where(ParticipantDisbursementModel.wallet_id == wallet_id)
        if study_id:
            stmt = stmt.where(ParticipantDisbursementModel.study_id == study_id)
        if subject_id:
            stmt = stmt.where(ParticipantDisbursementModel.subject_id == subject_id)
        stmt = stmt.order_by(desc(ParticipantDisbursementModel.dispatched_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Wallet Ledger Transactions
    # ==========================================

    async def save_transaction(
        self, transaction: ParticipantTransactionModel
    ) -> ParticipantTransactionModel:
        if not transaction.id:
            transaction.id = str(uuid.uuid4())
        self.session.add(transaction)
        await self.session.commit()
        await self.session.refresh(transaction)
        return transaction

    async def get_transaction_by_reference(
        self, reference_id: str
    ) -> ParticipantTransactionModel | None:
        stmt = select(ParticipantTransactionModel).where(
            ParticipantTransactionModel.reference_id == reference_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_transactions(
        self,
        wallet_id: str | None = None,
        subject_id: str | None = None,
        study_id: str | None = None,
    ) -> list[ParticipantTransactionModel]:
        stmt = select(ParticipantTransactionModel)
        if wallet_id:
            stmt = stmt.where(ParticipantTransactionModel.wallet_id == wallet_id)
        if subject_id:
            stmt = stmt.where(ParticipantTransactionModel.subject_id == subject_id)
        if study_id:
            stmt = stmt.where(ParticipantTransactionModel.study_id == study_id)
        stmt = stmt.order_by(desc(ParticipantTransactionModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # EU CTR 536/2014 Plain Language Lay Summaries
    # ==========================================

    async def save_lay_summary(self, summary: LaySummaryModel) -> LaySummaryModel:
        if not summary.id:
            summary.id = str(uuid.uuid4())
        self.session.add(summary)
        await self.session.commit()
        await self.session.refresh(summary)
        return summary

    async def get_lay_summary_by_id(self, summary_id: str) -> LaySummaryModel | None:
        stmt = (
            select(LaySummaryModel)
            .where(LaySummaryModel.id == summary_id)
            .options(selectinload(LaySummaryModel.receipts))
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def list_lay_summaries(
        self,
        study_id: str | None = None,
        language: str | None = None,
        status: str | None = None,
    ) -> list[LaySummaryModel]:
        stmt = select(LaySummaryModel).options(selectinload(LaySummaryModel.receipts))
        if study_id:
            stmt = stmt.where(LaySummaryModel.study_id == study_id)
        if language:
            stmt = stmt.where(LaySummaryModel.language == language)
        if status:
            stmt = stmt.where(LaySummaryModel.status == status)
        stmt = stmt.order_by(desc(LaySummaryModel.created_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    # ==========================================
    # Lay Summary Confirmation Receipts
    # ==========================================

    async def save_lay_summary_receipt(
        self, receipt: LaySummaryReceiptModel
    ) -> LaySummaryReceiptModel:
        if not receipt.id:
            receipt.id = str(uuid.uuid4())
        self.session.add(receipt)
        await self.session.commit()
        await self.session.refresh(receipt)
        return receipt

    async def list_lay_summary_receipts(
        self,
        summary_id: str | None = None,
        study_id: str | None = None,
        subject_id: str | None = None,
    ) -> list[LaySummaryReceiptModel]:
        stmt = select(LaySummaryReceiptModel)
        if summary_id:
            stmt = stmt.where(LaySummaryReceiptModel.summary_id == summary_id)
        if study_id:
            stmt = stmt.where(LaySummaryReceiptModel.study_id == study_id)
        if subject_id:
            stmt = stmt.where(LaySummaryReceiptModel.subject_id == subject_id)
        stmt = stmt.order_by(desc(LaySummaryReceiptModel.confirmed_at))
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def get_by_id(self, id: str) -> Any | None:
        """Generic get_by_id implementing RepositoryPort."""
        return await self.get_contract_by_id(id)

    async def save(self, entity: Any) -> Any:
        """Generic save implementing RepositoryPort."""
        self.session.add(entity)
        await self.session.flush()
        return entity


async def get_payments_repository(
    session: AsyncSession = Depends(get_db_session),
) -> SQLAlchemyPaymentsRepository:
    """FastAPI dependency yielding a SQLAlchemyPaymentsRepository instance."""
    return SQLAlchemyPaymentsRepository(session)
