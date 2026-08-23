"""FastAPI sub-router for Billing and ERP Gateway.

Owns: Self-billing invoice compilation cycles, approvals, cryptographic PDF remittance advice
generation, digital signing and verification, dispatch, ERP export (SAP/NetSuite), webhook receiver,
payment reconciliation, and ERP audit logs.

Requirements: ADR-2199, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

import base64

from fastapi import APIRouter, Depends, HTTPException, Query, Response, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.adapters.database import get_db_session
from apps.payments.infrastructure.models import (
    ErpSyncLogModel,
    InvoiceLineItemModel,
    RemittanceAdviceModel,
    SelfBillingInvoiceModel,
)
from apps.payments.presentation.dtos import (
    BillingCycleCompilationRequest,
    ErpExportResponse,
    ErpSyncLogResponse,
    ErpWebhookPayload,
    ErpWebhookResponse,
    InvoiceLineItemResponse,
    PaymentReconciliationRequest,
    PaymentReconciliationResponse,
    RemittanceAdviceDispatchRequest,
    RemittanceAdviceResponse,
    RemittanceAdviceSignRequest,
    RemittanceSignatureVerificationResponse,
    SelfBillingInvoiceResponse,
)
from apps.payments.presentation.routers.site_ledger import (
    check_financial_read_access,
    check_financial_write_access,
)
from apps.payments.services.billing_and_erp_gateway import (
    BillingAndErpGateway,
)
from packages.security.rbac import Principal, get_principal

router = APIRouter(tags=["Billing & ERP Gateway"])


def get_billing_erp_gateway(
    session: AsyncSession = Depends(get_db_session),
) -> BillingAndErpGateway:
    """Dependency provider for BillingAndErpGateway."""
    return BillingAndErpGateway(session=session)


def map_line_item_response(entity: InvoiceLineItemModel) -> InvoiceLineItemResponse:
    return InvoiceLineItemResponse(
        id=entity.id,
        invoice_id=entity.invoice_id,
        payable_id=entity.payable_id,
        subject_id=entity.subject_id,
        visit_code=entity.visit_code,
        procedure_code=entity.procedure_code,
        procedure_name=entity.procedure_name,
        base_amount=entity.base_amount,
        overhead_amount=entity.overhead_amount,
        patient_stipend=entity.patient_stipend,
        travel_reimbursement=entity.travel_reimbursement,
        gross_amount=entity.gross_amount,
        withholding_rate=entity.withholding_rate,
        withholding_amount=entity.withholding_amount,
        net_amount=entity.net_amount,
        currency=entity.currency,
        created_at=entity.created_at,
        version_index=entity.version_index,
    )


def map_remittance_response(
    entity: RemittanceAdviceModel,
) -> RemittanceAdviceResponse:
    return RemittanceAdviceResponse(
        id=entity.id,
        remittance_number=entity.remittance_number,
        invoice_id=entity.invoice_id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        currency=entity.currency,
        gross_amount=entity.gross_amount,
        overhead_amount=entity.overhead_amount,
        holdback_amount=entity.holdback_amount,
        net_amount=entity.net_amount,
        pdf_sha256_hash=entity.pdf_sha256_hash,
        is_sealed=entity.is_sealed,
        is_signed=entity.is_signed,
        signed_by=entity.signed_by,
        signed_at=entity.signed_at,
        signer_role=entity.signer_role,
        signature_manifest=entity.signature_manifest,
        dispatched_to=entity.dispatched_to,
        dispatched_at=entity.dispatched_at,
        dispatch_status=entity.dispatch_status,
        created_at=entity.created_at,
        version_index=entity.version_index,
    )


def map_invoice_response(
    entity: SelfBillingInvoiceModel,
) -> SelfBillingInvoiceResponse:
    line_item_resps = (
        [map_line_item_response(li) for li in entity.line_items]
        if entity.line_items
        else []
    )
    remittance_resp = (
        map_remittance_response(entity.remittance_advice)
        if entity.remittance_advice
        else None
    )

    return SelfBillingInvoiceResponse(
        id=entity.id,
        invoice_number=entity.invoice_number,
        study_id=entity.study_id,
        site_id=entity.site_id,
        contract_id=entity.contract_id,
        billing_cycle=entity.billing_cycle,
        period_start=entity.period_start,
        period_end=entity.period_end,
        currency=entity.currency,
        total_base_amount=entity.total_base_amount,
        total_overhead_amount=entity.total_overhead_amount,
        total_patient_stipend=entity.total_patient_stipend,
        total_travel_reimbursement=entity.total_travel_reimbursement,
        total_gross_amount=entity.total_gross_amount,
        total_holdback_amount=entity.total_holdback_amount,
        total_net_amount=entity.total_net_amount,
        payable_count=entity.payable_count,
        status=entity.status,
        approved_by=entity.approved_by,
        approved_at=entity.approved_at,
        erp_system=entity.erp_system,
        erp_reference_id=entity.erp_reference_id,
        erp_sync_status=entity.erp_sync_status,
        erp_synced_at=entity.erp_synced_at,
        reconciled_by=entity.reconciled_by,
        reconciled_at=entity.reconciled_at,
        reconciliation_reference=entity.reconciliation_reference,
        reconciliation_notes=entity.reconciliation_notes,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
        line_items=line_item_resps,
        remittance_advice=remittance_resp,
    )


def map_erp_log_response(entity: ErpSyncLogModel) -> ErpSyncLogResponse:
    return ErpSyncLogResponse(
        id=entity.id,
        invoice_id=entity.invoice_id,
        erp_system=entity.erp_system,
        sync_direction=entity.sync_direction,
        status=entity.status,
        payload=entity.payload,
        response_data=entity.response_data,
        external_reference=entity.external_reference,
        reconciled=entity.reconciled,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
    )


# ==========================================
# 1. Self-Billing Invoicing
# ==========================================


@router.post(
    "/invoices/compile",
    response_model=SelfBillingInvoiceResponse,
    status_code=status.HTTP_201_CREATED,
)
async def compile_billing_cycle(
    payload: BillingCycleCompilationRequest,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Automates monthly or quarterly billing cycle compilation into a self-billing invoice."""
    check_financial_write_access(principal)
    try:
        invoice = await gateway.compile_billing_cycle(
            study_id=payload.study_id,
            site_id=payload.site_id,
            billing_cycle=payload.billing_cycle,
            period_start=payload.period_start,
            period_end=payload.period_end,
            auto_approve=payload.auto_approve,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or f"Compiled {payload.billing_cycle} billing cycle",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_invoice_response(invoice)


@router.get("/invoices", response_model=list[SelfBillingInvoiceResponse])
async def list_invoices(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    billing_cycle: str | None = Query(default=None),
    status: str | None = Query(default=None),
    erp_sync_status: str | None = Query(default=None),
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> list[SelfBillingInvoiceResponse]:
    """Lists compiled self-billing invoices matching filter criteria."""
    check_financial_read_access(principal)
    invoices = await gateway.list_invoices(
        study_id=study_id,
        site_id=site_id,
        billing_cycle=billing_cycle,
        status=status,
        erp_sync_status=erp_sync_status,
    )
    return [map_invoice_response(inv) for inv in invoices]


@router.get("/invoices/{invoice_id}", response_model=SelfBillingInvoiceResponse)
async def get_invoice(
    invoice_id: str,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Retrieves full self-billing invoice details with line items and remittance advice."""
    check_financial_read_access(principal)
    invoice = await gateway.get_invoice(invoice_id)
    if not invoice:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Self-billing invoice '{invoice_id}' not found.",
        )
    return map_invoice_response(invoice)


@router.post(
    "/invoices/{invoice_id}/approve",
    response_model=SelfBillingInvoiceResponse,
)
async def approve_self_billing_invoice(
    invoice_id: str,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Approves a draft self-billing invoice for ERP synchronization and disbursement."""
    check_financial_write_access(principal)
    try:
        invoice = await gateway.approve_self_billing_invoice(
            invoice_id=invoice_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Approved invoice",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_invoice_response(invoice)


# ==========================================
# 2. Cryptographically Signed Remittance Advice
# ==========================================


@router.get(
    "/invoices/{invoice_id}/remittance-advice",
    response_model=RemittanceAdviceResponse,
)
async def get_remittance_advice(
    invoice_id: str,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Retrieves remittance advice statement record for an invoice."""
    check_financial_read_access(principal)
    remittance = await gateway.get_remittance_advice(invoice_id)
    if not remittance:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Remittance advice for invoice '{invoice_id}' not found.",
        )
    return map_remittance_response(remittance)


@router.get("/invoices/{invoice_id}/remittance-advice/pdf")
async def download_remittance_advice_pdf(
    invoice_id: str,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> Response:
    """Downloads binary PDF remittance advice statement."""
    check_financial_read_access(principal)
    remittance = await gateway.get_remittance_advice(invoice_id)
    if not remittance or not remittance.pdf_content:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Remittance advice PDF for invoice '{invoice_id}' not found.",
        )

    pdf_bytes = base64.b64decode(remittance.pdf_content)
    return Response(
        content=pdf_bytes,
        media_type="application/pdf",
        headers={
            "Content-Disposition": f"attachment; filename=Remittance_{remittance.remittance_number}.pdf",
            "X-Document-SHA256": remittance.pdf_sha256_hash,
        },
    )


@router.post(
    "/invoices/{invoice_id}/remittance-advice/sign",
    response_model=RemittanceAdviceResponse,
)
async def sign_remittance_advice(
    invoice_id: str,
    payload: RemittanceAdviceSignRequest,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Applies 21 CFR Part 11 cryptographic digital signature to remittance advice statement."""
    check_financial_write_access(principal)
    try:
        remittance = await gateway.sign_and_seal_remittance_advice(
            invoice_id=invoice_id,
            signer_name=payload.signer_name,
            signer_role=payload.signer_role,
            signature_reason=payload.signature_reason,
            cert_pem=payload.cert_pem,
            private_key_pem=payload.private_key_pem,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or payload.signature_reason,
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_remittance_response(remittance)


@router.post(
    "/invoices/{invoice_id}/remittance-advice/verify-signature",
    response_model=RemittanceSignatureVerificationResponse,
)
async def verify_remittance_advice_signature(
    invoice_id: str,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> RemittanceSignatureVerificationResponse:
    """Verifies cryptographic signature validity and tamper resistance of remittance advice statement."""
    check_financial_read_access(principal)
    try:
        res = await gateway.verify_remittance_advice_signature(invoice_id)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e))

    return RemittanceSignatureVerificationResponse(**res)


@router.post(
    "/invoices/{invoice_id}/remittance-advice/dispatch",
    response_model=RemittanceAdviceResponse,
)
async def dispatch_remittance_advice(
    invoice_id: str,
    payload: RemittanceAdviceDispatchRequest,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Dispatches signed remittance advice statement via automated email notification."""
    check_financial_write_access(principal)
    try:
        remittance = await gateway.dispatch_remittance_advice(
            invoice_id=invoice_id,
            recipient_email=payload.recipient_email,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or f"Dispatched remittance advice to {payload.recipient_email}",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_remittance_response(remittance)


# ==========================================
# 3. Enterprise ERP Synchronization (SAP/NetSuite) & Reconciliation
# ==========================================


@router.post(
    "/invoices/{invoice_id}/erp-export",
    response_model=ErpExportResponse,
)
async def export_invoice_to_erp(
    invoice_id: str,
    erp_system: str = Query(default="SAP", description="Target ERP (SAP, NETSUITE)"),
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> ErpExportResponse:
    """Exports invoice into standardized ERP transmission format (SAP/NetSuite)."""
    check_financial_write_access(principal)
    try:
        export_envelope = await gateway.export_invoice_to_erp(
            invoice_id=invoice_id,
            erp_system=erp_system,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return ErpExportResponse(
        erp_system=export_envelope.erp_system,
        invoice_number=export_envelope.invoice_number,
        document_type=export_envelope.document_type,
        payload=export_envelope.model_dump(),
        export_timestamp=export_envelope.export_timestamp,
    )


@router.post("/erp/webhook", response_model=ErpWebhookResponse)
async def process_erp_webhook(
    payload: ErpWebhookPayload,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
) -> ErpWebhookResponse:
    """Inbound enterprise ERP webhook receiver for payment execution and reconciliation status updates."""
    try:
        res = await gateway.process_erp_webhook(
            payload=payload,
            user_id=f"ERP_{payload.erp_system}",
            user_roles="ERP Integration Connector",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return ErpWebhookResponse(**res)


@router.post(
    "/invoices/{invoice_id}/reconcile",
    response_model=PaymentReconciliationResponse,
)
async def reconcile_invoice_payment(
    invoice_id: str,
    payload: PaymentReconciliationRequest,
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> PaymentReconciliationResponse:
    """Reconciles payment execution against self-billing invoice and underlying payables."""
    check_financial_write_access(principal)
    try:
        res = await gateway.reconcile_invoice_payment(
            invoice_id=invoice_id,
            payment_reference=payload.payment_reference,
            erp_system=payload.erp_system,
            payment_date=payload.payment_date,
            amount_paid=payload.amount_paid,
            notes=payload.notes,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Financial Payment Reconciliation",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return PaymentReconciliationResponse(**res)


@router.get(
    "/invoices/{invoice_id}/erp-logs",
    response_model=list[ErpSyncLogResponse],
)
async def list_erp_sync_logs(
    invoice_id: str,
    erp_system: str | None = Query(default=None),
    status: str | None = Query(default=None),
    gateway: BillingAndErpGateway = Depends(get_billing_erp_gateway),
    principal: Principal = Depends(get_principal),
) -> list[ErpSyncLogResponse]:
    """Lists ERP synchronization history and webhook audit records for an invoice."""
    check_financial_read_access(principal)
    logs = await gateway.list_erp_sync_logs(
        invoice_id=invoice_id, erp_system=erp_system, status=status
    )
    return [map_erp_log_response(log) for log in logs]


__all__ = [
    "get_billing_erp_gateway",
    "map_erp_log_response",
    "map_invoice_response",
    "map_line_item_response",
    "map_remittance_response",
    "router",
]
