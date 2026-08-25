"""Billing and ERP Gateway Service for Clinical Trial Site Payments.

Owns: Monthly/quarterly self-billing invoice compilation cycles, line item aggregation,
cryptographic SHA-256 PDF remittance advice generation and sealing, 21 CFR Part 11 electronic
signatures, ERP synchronization (SAP S/4HANA / NetSuite SuiteTalk), inbound webhook handling,
and automated payment reconciliation.

Requirements: ADR-2199, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

import base64
import hashlib
import uuid
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.payments.domain.exceptions import (
    ContractNotFoundError,
    InvoiceNotFoundError,
    PaymentsValidationError,
)
from apps.payments.domain.models import (
    AuditAction,
    ErpExportEnvelope,
    ErpSyncStatus,
    ErpSystem,
    InvoiceStatus,
    PayableStatus,
    RemittanceDispatchStatus,
)
from apps.payments.infrastructure.models import (
    BudgetContractModel,
    ErpSyncLogModel,
    InvoiceLineItemModel,
    PayableModel,
    RemittanceAdviceModel,
    SelfBillingInvoiceModel,
    SplitPayeeConfigModel,
)
from apps.payments.presentation.dtos import ErpWebhookPayload
from apps.payments.services.audit import record_payment_audit_log
from apps.payments.services.pdf_remittance_generator import (
    RemittanceAdvicePdfGenerator,
)


class BillingAndErpGateway:
    """Deep domain engine for self-billing invoicing, remittance statements, and enterprise ERP integration."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    # ==========================================
    # 1. Self-Billing Invoicing Engine
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
        """Automates monthly and quarterly billing cycle compilation into self-billing invoices."""
        # Find active contract
        stmt_contract = (
            select(BudgetContractModel)
            .where(
                BudgetContractModel.study_id == study_id,
                BudgetContractModel.site_id == site_id,
                BudgetContractModel.is_active.is_(True),
                BudgetContractModel.status == "ACTIVE",
            )
            .order_by(desc(BudgetContractModel.version_index))
        )
        res_contract = await self.session.execute(stmt_contract)
        contract = res_contract.scalars().first()
        if not contract:
            raise ContractNotFoundError(
                f"No active budget contract found for study '{study_id}' and site '{site_id}'."
            )

        # Retrieve all approved (or payable) items ready for billing compilation
        stmt_payables = (
            select(PayableModel)
            .where(
                PayableModel.study_id == study_id,
                PayableModel.site_id == site_id,
                PayableModel.status == PayableStatus.APPROVED.value,
            )
            .options(
                selectinload(PayableModel.allocations),
                selectinload(PayableModel.holdback_reserve),
            )
        )
        res_payables = await self.session.execute(stmt_payables)
        payables = list(res_payables.scalars().all())

        if not payables:
            stmt_payables_trig = (
                select(PayableModel)
                .where(
                    PayableModel.study_id == study_id,
                    PayableModel.site_id == site_id,
                    PayableModel.status == PayableStatus.TRIGGERED.value,
                    PayableModel.is_sdv_gated.is_(False),
                )
                .options(
                    selectinload(PayableModel.allocations),
                    selectinload(PayableModel.holdback_reserve),
                )
            )
            res_payables_trig = await self.session.execute(stmt_payables_trig)
            payables = list(res_payables_trig.scalars().all())

        if not payables:
            raise PaymentsValidationError(
                f"No approved payable line items available for billing compilation in study '{study_id}', site '{site_id}'."
            )

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

        stmt_split = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == contract.id,
            SplitPayeeConfigModel.is_active.is_(True),
        )
        res_split = await self.session.execute(stmt_split)
        split_payees = list(res_split.scalars().all())

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

        self.session.add(invoice)

        for p in payables:
            p.status = PayableStatus.INVOICED.value
            p.reason_for_change = f"Compiled into invoice {invoice_number}"
            self.session.add(p)

        await self.session.commit()
        await self.session.refresh(invoice)

        await record_payment_audit_log(
            session=self.session,
            entity_type="INVOICE",
            entity_id=invoice.id,
            action=AuditAction.INVOICE_COMPILED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "invoice_number": invoice.invoice_number,
                "billing_cycle": invoice.billing_cycle,
                "period_start": invoice.period_start,
                "period_end": invoice.period_end,
                "total_gross_amount": invoice.total_gross_amount,
                "total_holdback_amount": invoice.total_holdback_amount,
                "total_net_amount": invoice.total_net_amount,
                "status": invoice.status,
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

        return invoice

    async def approve_self_billing_invoice(
        self,
        invoice_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str | None = None,
    ) -> SelfBillingInvoiceModel:
        """Approves a draft self-billing invoice for ERP synchronization and disbursement."""
        invoice = await self.get_invoice(invoice_id)
        if not invoice:
            raise InvoiceNotFoundError(f"Self-billing invoice '{invoice_id}' not found.")

        prev_status = invoice.status
        now = datetime.now(UTC)
        invoice.status = InvoiceStatus.APPROVED.value
        invoice.approved_by = user_id
        invoice.approved_at = now
        invoice.reason_for_change = (
            reason_for_change or f"Invoice approved by {user_id}"
        )
        self.session.add(invoice)
        await self.session.commit()
        await self.session.refresh(invoice)

        await record_payment_audit_log(
            session=self.session,
            entity_type="INVOICE",
            entity_id=invoice.id,
            action=AuditAction.INVOICE_APPROVED.value,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={"status": invoice.status, "approved_by": user_id},
            details=f"Invoice {invoice.invoice_number} approved for disbursement.",
            reason_for_change=reason_for_change or "Invoice Approval",
            version_index=invoice.version_index,
        )
        return invoice

    async def get_invoice(self, invoice_id: str) -> SelfBillingInvoiceModel | None:
        """Retrieves self-billing invoice by ID with line items and remittance advice eagerly loaded."""
        stmt = (
            select(SelfBillingInvoiceModel)
            .where(SelfBillingInvoiceModel.id == invoice_id)
            .options(
                selectinload(SelfBillingInvoiceModel.line_items),
                selectinload(SelfBillingInvoiceModel.remittance_advice),
            )
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    async def get_invoice_by_number(
        self, invoice_number: str
    ) -> SelfBillingInvoiceModel | None:
        """Retrieves self-billing invoice by invoice number."""
        stmt = (
            select(SelfBillingInvoiceModel)
            .where(SelfBillingInvoiceModel.invoice_number == invoice_number)
            .options(
                selectinload(SelfBillingInvoiceModel.line_items),
                selectinload(SelfBillingInvoiceModel.remittance_advice),
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
        """Lists self-billing invoices matching filter criteria."""
        stmt = select(SelfBillingInvoiceModel).options(
            selectinload(SelfBillingInvoiceModel.line_items),
            selectinload(SelfBillingInvoiceModel.remittance_advice),
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

    # ==========================================
    # 2. Cryptographic Remittance Advice Signing & Verification
    # ==========================================

    async def get_remittance_advice(
        self, invoice_id: str
    ) -> RemittanceAdviceModel | None:
        """Retrieves remittance advice record for an invoice."""
        stmt = select(RemittanceAdviceModel).where(
            RemittanceAdviceModel.invoice_id == invoice_id
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

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
        """Cryptographically signs and seals a PDF remittance advice statement with 21 CFR Part 11 validation."""
        invoice = await self.get_invoice(invoice_id)
        if not invoice:
            raise InvoiceNotFoundError(f"Invoice with ID '{invoice_id}' not found.")

        remittance = invoice.remittance_advice
        if not remittance:
            raise PaymentsValidationError(
                f"No remittance advice document found for invoice '{invoice_id}'."
            )

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

        stmt_split = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == invoice.contract_id,
            SplitPayeeConfigModel.is_active.is_(True),
        )
        res_split = await self.session.execute(stmt_split)
        split_payees = list(res_split.scalars().all())

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

        self.session.add(remittance)
        await self.session.commit()
        await self.session.refresh(remittance)

        await record_payment_audit_log(
            session=self.session,
            entity_type="REMITTANCE_ADVICE",
            entity_id=remittance.id,
            action=AuditAction.REMITTANCE_ADVICE_SIGNED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "remittance_number": remittance.remittance_number,
                "is_signed": True,
                "signed_by": signer_name,
                "signed_at": str(now),
                "pdf_sha256_hash": signed_hash,
                "manifest": manifest,
            },
            details=f"Remittance advice {remittance.remittance_number} cryptographically signed by {signer_name} (SHA-256: {signed_hash[:16]}...).",
            reason_for_change=signature_reason,
            version_index=remittance.version_index,
        )

        return remittance

    async def verify_remittance_advice_signature(
        self, invoice_id: str
    ) -> dict[str, Any]:
        """Cryptographically verifies electronic signature and tamper resistance of remittance advice."""
        remittance = await self.get_remittance_advice(invoice_id)
        if not remittance:
            raise PaymentsValidationError(
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
        """Dispatches signed remittance advice statement to site financial contact."""
        remittance = await self.get_remittance_advice(invoice_id)
        if not remittance:
            raise PaymentsValidationError(
                f"No remittance advice document found for invoice '{invoice_id}'."
            )

        if not remittance.is_signed:
            raise PaymentsValidationError(
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

        self.session.add(remittance)
        await self.session.commit()
        await self.session.refresh(remittance)

        await record_payment_audit_log(
            session=self.session,
            entity_type="REMITTANCE_ADVICE",
            entity_id=remittance.id,
            action=AuditAction.REMITTANCE_ADVICE_DISPATCHED.value,
            user_id=user_id,
            user_roles=user_roles,
            new_state={
                "dispatch_status": "DISPATCHED",
                "dispatched_to": recipient_email,
                "dispatched_at": str(now),
            },
            details=f"Remittance advice {remittance.remittance_number} dispatched to '{recipient_email}'.",
            reason_for_change=reason_for_change or "Site Remittance Email Notification",
            version_index=remittance.version_index,
        )
        return remittance

    # ==========================================
    # 3. Enterprise ERP Synchronization (SAP / NetSuite) & Webhooks
    # ==========================================

    async def export_invoice_to_erp(
        self,
        invoice_id: str,
        erp_system: str = "SAP",
        user_id: str = "system",
        user_roles: str = "Grants Manager",
    ) -> ErpExportEnvelope:
        """Generates ERP export payload and marks invoice for enterprise ERP sync."""
        invoice = await self.get_invoice(invoice_id)
        if not invoice:
            raise InvoiceNotFoundError(f"Invoice with ID '{invoice_id}' not found.")

        now = datetime.now(UTC)
        export_timestamp = now.isoformat()

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

        stmt_split = select(SplitPayeeConfigModel).where(
            SplitPayeeConfigModel.contract_id == invoice.contract_id,
            SplitPayeeConfigModel.is_active.is_(True),
        )
        res_split = await self.session.execute(stmt_split)
        split_payees = list(res_split.scalars().all())

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

        invoice.erp_system = erp_system
        invoice.erp_sync_status = ErpSyncStatus.SYNCED.value
        invoice.erp_synced_at = now
        self.session.add(invoice)

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
        self.session.add(sync_log)
        await self.session.commit()

        await record_payment_audit_log(
            session=self.session,
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
        """Processes inbound ERP webhook status updates (SAP/NetSuite) with payment reconciliation."""
        invoice: SelfBillingInvoiceModel | None = None
        if payload.invoice_id:
            invoice = await self.get_invoice(payload.invoice_id)
        elif payload.invoice_number:
            invoice = await self.get_invoice_by_number(payload.invoice_number)

        if not invoice:
            raise InvoiceNotFoundError(
                f"Target invoice not found for webhook reference '{payload.invoice_id or payload.invoice_number}'."
            )

        prev_status = invoice.status
        now = datetime.now(UTC)
        is_reconciled = False

        normalized_status = payload.status.upper()
        if normalized_status in ["PAID"]:
            invoice.status = InvoiceStatus.PAID.value
            invoice.erp_sync_status = ErpSyncStatus.SYNCED.value
            for item in invoice.line_items:
                if item.payable:
                    item.payable.status = PayableStatus.PAID.value
                    self.session.add(item.payable)

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
            for item in invoice.line_items:
                if item.payable:
                    item.payable.status = PayableStatus.PAID.value
                    self.session.add(item.payable)

        elif normalized_status in ["APPROVED"]:
            invoice.status = InvoiceStatus.APPROVED.value

        invoice.erp_system = payload.erp_system
        invoice.erp_reference_id = payload.erp_reference_id
        invoice.reason_for_change = f"ERP webhook update from {payload.erp_system} to status '{normalized_status}'"
        self.session.add(invoice)

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
        self.session.add(sync_log)
        await self.session.commit()
        await self.session.refresh(invoice)

        action_name = (
            AuditAction.INVOICE_RECONCILED.value
            if is_reconciled
            else AuditAction.ERP_WEBHOOK_PROCESSED.value
        )

        await record_payment_audit_log(
            session=self.session,
            entity_type="INVOICE",
            entity_id=invoice.id,
            action=action_name,
            user_id=user_id,
            user_roles=user_roles,
            previous_state={"status": prev_status},
            new_state={
                "status": invoice.status,
                "erp_system": payload.erp_system,
                "erp_reference_id": payload.erp_reference_id,
                "erp_sync_status": invoice.erp_sync_status,
            },
            details=(
                f"Processed ERP webhook from {payload.erp_system} for invoice {invoice.invoice_number} "
                f"transitioning from '{prev_status}' to '{invoice.status}' (Ref: {payload.erp_reference_id})."
            ),
            reason_for_change=f"ERP Synchronization ({payload.erp_system})",
            version_index=invoice.version_index,
        )

        return {
            "status": "success",
            "invoice_id": invoice.id,
            "invoice_number": invoice.invoice_number,
            "erp_system": payload.erp_system,
            "erp_reference_id": payload.erp_reference_id,
            "previous_status": prev_status,
            "new_status": invoice.status,
            "reconciled": is_reconciled,
            "message": f"Successfully processed {payload.erp_system} webhook for invoice {invoice.invoice_number}.",
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
        """Formally reconciles bank/ERP disbursement with self-billing invoice and underlying payables."""
        invoice = await self.get_invoice(invoice_id)
        if not invoice:
            raise InvoiceNotFoundError(f"Invoice with ID '{invoice_id}' not found.")

        if amount_paid < 0:
            raise PaymentsValidationError("Reconciliation amount paid cannot be negative.")

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

        reconciled_count = 0
        for item in invoice.line_items:
            if item.payable_id:
                stmt_p = select(PayableModel).where(PayableModel.id == item.payable_id)
                res_p = await self.session.execute(stmt_p)
                p = res_p.scalars().first()
                if p:
                    p.status = PayableStatus.PAID.value
                    p.reason_for_change = (
                        f"Payment reconciled under invoice {invoice.invoice_number}"
                    )
                    self.session.add(p)
                    reconciled_count += 1

        self.session.add(invoice)

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
        self.session.add(sync_log)
        await self.session.commit()
        await self.session.refresh(invoice)

        await record_payment_audit_log(
            session=self.session,
            entity_type="INVOICE",
            entity_id=invoice.id,
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
                f"Reconciled invoice {invoice.invoice_number} (${amount_paid:.2f} paid) "
                f"against reference '{payment_reference}' across {reconciled_count} payables."
            ),
            reason_for_change=reason_for_change
            or "Financial Payment Settlement Reconciliation",
            version_index=invoice.version_index,
        )

        return {
            "invoice_id": invoice.id,
            "invoice_number": invoice.invoice_number,
            "status": invoice.status,
            "erp_sync_status": invoice.erp_sync_status,
            "reconciled_by": user_id,
            "reconciled_at": now,
            "reconciliation_reference": payment_reference,
            "total_net_amount": invoice.total_net_amount,
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


__all__ = ["BillingAndErpGateway"]
