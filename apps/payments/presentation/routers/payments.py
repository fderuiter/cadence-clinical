"""FastAPI router endpoints for Site Payments, Invoicing, Remittance Advice & ERP Sync.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-004, PRD-PAY-006
"""

import base64
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, Response, status

from apps.payments.adapters.repositories import (
    SQLAlchemyPaymentsRepository,
    get_payments_repository,
)
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
    RemittanceAdviceModel,
    SelfBillingInvoiceModel,
    SplitPayeeConfigModel,
)
from apps.payments.presentation.dtos import (
    BatchEventIngestDto,
    BillingCycleCompilationRequest,
    BudgetContractCreate,
    BudgetContractResponse,
    CraSdvVerificationEventDto,
    DisbursementRequest,
    DisbursementResponse,
    EcrfSubmissionEventDto,
    ErpExportResponse,
    ErpSyncLogResponse,
    ErpWebhookPayload,
    ErpWebhookResponse,
    ExpenseClaimResponse,
    ExpenseClaimReviewRequest,
    ExpenseClaimSubmitRequest,
    FeeScheduleCreate,
    FeeScheduleResponse,
    HoldbackReleaseRequest,
    HoldbackReleaseResponse,
    HoldbackReserveResponse,
    InvoiceLineItemResponse,
    LaySummaryCreate,
    LaySummaryPublishRequest,
    LaySummaryReceiptConfirmRequest,
    LaySummaryReceiptResponse,
    LaySummaryResponse,
    MilestoneCompletionEventDto,
    ParticipantStipendRuleCreate,
    ParticipantStipendRuleResponse,
    ParticipantTransactionResponse,
    ParticipantWalletCreate,
    ParticipantWalletPayoutMethodsUpdate,
    ParticipantWalletResponse,
    PayableAllocationResponse,
    PayableResponse,
    PaymentAuditLogResponse,
    PaymentReconciliationRequest,
    PaymentReconciliationResponse,
    ReceiptOcrExtractRequest,
    ReceiptOcrExtractResponse,
    RemittanceAdviceDispatchRequest,
    RemittanceAdviceResponse,
    RemittanceAdviceSignRequest,
    RemittanceSignatureVerificationResponse,
    SelfBillingInvoiceResponse,
    SplitPayeeConfigBulkUpdate,
    SplitPayeeConfigResponse,
    VisitVerificationEventDto,
)
from apps.payments.services.payments_service import PaymentsService
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(prefix="/api/v1/payments", tags=["Site Payments"])


def get_payments_service(
    repo: SQLAlchemyPaymentsRepository = Depends(get_payments_repository),
) -> PaymentsService:
    """Dependency provider for PaymentsService."""
    return PaymentsService(repo=repo)


def check_financial_write_access(principal: Principal) -> None:
    """Validates that user has financial write authority."""
    if not (
        has_permission(principal, "ctms_financial:write")
        or has_permission(principal, "payments:write")
        or has_permission(principal, "ctms_study:create")
    ):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: User lacks financial write authority.",
        )


def check_financial_read_access(principal: Principal) -> None:
    """Validates that user has financial read authority."""
    if not (
        has_permission(principal, "ctms_financial:read")
        or has_permission(principal, "payments:read")
        or has_permission(principal, "ctms_study:read")
        or has_permission(principal, "system_audit_logs:read")
    ):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: User lacks financial read authority.",
        )


# ==========================================
# Helpers / Response Mappers
# ==========================================


def map_contract_response(entity: BudgetContractModel) -> BudgetContractResponse:
    return BudgetContractResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        contract_name=entity.contract_name,
        tier_level=entity.tier_level,
        currency=entity.currency,
        total_budget=entity.total_budget,
        overhead_percentage=entity.overhead_percentage,
        withholding_percentage=entity.withholding_percentage,
        effective_date=entity.effective_date,
        status=entity.status,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
        fee_schedules_count=len(entity.fee_schedules) if entity.fee_schedules else 0,
        split_payees_count=len(entity.split_payees) if entity.split_payees else 0,
    )


def map_fee_schedule_response(entity: FeeScheduleModel) -> FeeScheduleResponse:
    return FeeScheduleResponse(
        id=entity.id,
        contract_id=entity.contract_id,
        visit_code=entity.visit_code,
        activity_id=entity.activity_id,
        form_id=entity.form_id,
        procedure_code=entity.procedure_code,
        procedure_name=entity.procedure_name,
        base_amount=entity.base_amount,
        overhead_percentage=entity.overhead_percentage,
        withholding_percentage=entity.withholding_percentage,
        patient_stipend=entity.patient_stipend,
        travel_reimbursement=entity.travel_reimbursement,
        requires_sdv=entity.requires_sdv,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


def map_split_payee_response(entity: SplitPayeeConfigModel) -> SplitPayeeConfigResponse:
    return SplitPayeeConfigResponse(
        id=entity.id,
        contract_id=entity.contract_id,
        payee_id=entity.payee_id,
        payee_name=entity.payee_name,
        payee_role=entity.payee_role,
        allocation_percentage=entity.allocation_percentage,
        bank_account_reference=entity.bank_account_reference,
        tax_identifier=entity.tax_identifier,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


def map_allocation_response(
    entity: PayableAllocationModel,
) -> PayableAllocationResponse:
    return PayableAllocationResponse(
        id=entity.id,
        payable_id=entity.payable_id,
        payee_id=entity.payee_id,
        payee_name=entity.payee_name,
        payee_role=entity.payee_role,
        allocation_percentage=entity.allocation_percentage,
        gross_share=entity.gross_share,
        withholding_share=entity.withholding_share,
        net_share=entity.net_share,
        currency=entity.currency,
        status=entity.status,
        created_at=entity.created_at,
        version_index=entity.version_index,
    )


def map_holdback_response(entity: HoldbackReserveModel) -> HoldbackReserveResponse:
    return HoldbackReserveResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        contract_id=entity.contract_id,
        payable_id=entity.payable_id,
        holdback_amount=entity.holdback_amount,
        currency=entity.currency,
        status=entity.status,
        release_authorized_by=entity.release_authorized_by,
        release_authorized_at=entity.release_authorized_at,
        dm_signoff_by=entity.dm_signoff_by,
        dm_signoff_at=entity.dm_signoff_at,
        quality_signoff_by=entity.quality_signoff_by,
        quality_signoff_at=entity.quality_signoff_at,
        release_notes=entity.release_notes,
        created_at=entity.created_at,
        version_index=entity.version_index,
    )


def map_payable_response(entity: PayableModel) -> PayableResponse:
    alloc_responses = (
        [map_allocation_response(a) for a in entity.allocations]
        if entity.allocations
        else []
    )
    holdback_resp = (
        map_holdback_response(entity.holdback_reserve)
        if entity.holdback_reserve
        else None
    )

    return PayableResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        contract_id=entity.contract_id,
        subject_id=entity.subject_id,
        visit_code=entity.visit_code,
        form_id=entity.form_id,
        procedure_code=entity.procedure_code,
        procedure_name=entity.procedure_name,
        trigger_type=entity.trigger_type,
        trigger_event_id=entity.trigger_event_id,
        trigger_source=entity.trigger_source,
        base_amount=entity.base_amount,
        overhead_amount=entity.overhead_amount,
        patient_stipend_amount=entity.patient_stipend_amount,
        travel_reimbursement_amount=entity.travel_reimbursement_amount,
        gross_amount=entity.gross_amount,
        withholding_rate=entity.withholding_rate,
        withholding_amount=entity.withholding_amount,
        net_amount=entity.net_amount,
        currency=entity.currency,
        status=entity.status,
        is_sdv_gated=entity.is_sdv_gated,
        sdv_verified_at=entity.sdv_verified_at,
        sdv_verified_by=entity.sdv_verified_by,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
        allocations=alloc_responses,
        holdback_reserve=holdback_resp,
    )


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


def map_wallet_response(
    entity: ParticipantWalletModel,
) -> ParticipantWalletResponse:
    pending_count = len(
        [c for c in (entity.expense_claims or []) if c.status == "PENDING_REVIEW"]
    )
    total_claims = len(entity.expense_claims or [])
    disb_count = len(entity.disbursements or [])
    return ParticipantWalletResponse(
        id=entity.id,
        subject_id=entity.subject_id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        current_balance=entity.current_balance,
        lifetime_stipends=entity.lifetime_stipends,
        lifetime_reimbursements=entity.lifetime_reimbursements,
        lifetime_disbursements=entity.lifetime_disbursements,
        currency=entity.currency,
        status=entity.status,
        virtual_card_token=entity.virtual_card_token,
        virtual_card_mask=entity.virtual_card_mask,
        virtual_card_expiry=entity.virtual_card_expiry,
        ach_routing_number=entity.ach_routing_number,
        ach_account_mask=entity.ach_account_mask,
        ach_account_holder=entity.ach_account_holder,
        created_at=entity.created_at,
        created_by=entity.created_by,
        updated_at=entity.updated_at,
        version_index=entity.version_index,
        pending_claims_count=pending_count,
        total_claims_count=total_claims,
        disbursements_count=disb_count,
    )


def map_stipend_rule_response(
    entity: ParticipantStipendRuleModel,
) -> ParticipantStipendRuleResponse:
    return ParticipantStipendRuleResponse(
        id=entity.id,
        study_id=entity.study_id,
        visit_code=entity.visit_code,
        visit_name=entity.visit_name,
        procedure_code=entity.procedure_code,
        amount=entity.amount,
        currency=entity.currency,
        requires_verification=entity.requires_verification,
        taxable=entity.taxable,
        is_active=entity.is_active,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
    )


def map_expense_claim_response(
    entity: ParticipantExpenseClaimModel,
) -> ExpenseClaimResponse:
    return ExpenseClaimResponse(
        id=entity.id,
        wallet_id=entity.wallet_id,
        subject_id=entity.subject_id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        expense_date=entity.expense_date,
        category=entity.category,
        merchant_name=entity.merchant_name,
        gross_amount=entity.gross_amount,
        tax_deduction=entity.tax_deduction,
        net_amount=entity.net_amount,
        currency=entity.currency,
        receipt_image_name=entity.receipt_image_name,
        receipt_ocr_data=entity.receipt_ocr_data,
        status=entity.status,
        reviewed_by=entity.reviewed_by,
        reviewed_at=entity.reviewed_at,
        review_notes=entity.review_notes,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
    )


def map_disbursement_response(
    entity: ParticipantDisbursementModel,
) -> DisbursementResponse:
    return DisbursementResponse(
        id=entity.id,
        wallet_id=entity.wallet_id,
        subject_id=entity.subject_id,
        study_id=entity.study_id,
        disbursement_type=entity.disbursement_type,
        amount=entity.amount,
        currency=entity.currency,
        status=entity.status,
        reference_number=entity.reference_number,
        destination_mask=entity.destination_mask,
        dispatched_at=entity.dispatched_at,
        settled_at=entity.settled_at,
        error_message=entity.error_message,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
    )


def map_transaction_response(
    entity: ParticipantTransactionModel,
) -> ParticipantTransactionResponse:
    return ParticipantTransactionResponse(
        id=entity.id,
        wallet_id=entity.wallet_id,
        subject_id=entity.subject_id,
        study_id=entity.study_id,
        transaction_type=entity.transaction_type,
        amount=entity.amount,
        balance_after=entity.balance_after,
        reference_id=entity.reference_id,
        description=entity.description,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
    )


def map_lay_summary_response(entity: LaySummaryModel) -> LaySummaryResponse:
    return LaySummaryResponse(
        id=entity.id,
        study_id=entity.study_id,
        protocol_id=entity.protocol_id,
        title=entity.title,
        trial_phase=entity.trial_phase,
        therapeutic_area=entity.therapeutic_area,
        sponsor_name=entity.sponsor_name,
        summary_text=entity.summary_text,
        plain_language_sections=entity.plain_language_sections,
        irb_approval_number=entity.irb_approval_number,
        irb_approval_date=entity.irb_approval_date,
        language=entity.language,
        status=entity.status,
        published_at=entity.published_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        version_index=entity.version_index,
        receipts_count=len(entity.receipts or []),
    )


def map_lay_summary_receipt_response(
    entity: LaySummaryReceiptModel,
) -> LaySummaryReceiptResponse:
    return LaySummaryReceiptResponse(
        id=entity.id,
        summary_id=entity.summary_id,
        subject_id=entity.subject_id,
        study_id=entity.study_id,
        comprehension_score=entity.comprehension_score,
        comprehension_feedback=entity.comprehension_feedback,
        confirmation_statement=entity.confirmation_statement,
        receipt_hash=entity.receipt_hash,
        signer_identity=entity.signer_identity,
        confirmed_at=entity.confirmed_at,
        status="CONFIRMED",
    )


# ==========================================
# 1. Budget Contracts & Fee Matrices
# ==========================================


@router.post(
    "/contracts",
    response_model=BudgetContractResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_budget_contract(
    payload: BudgetContractCreate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> BudgetContractResponse:
    """Creates or versions an active investigator grant contract schedule."""
    check_financial_write_access(principal)

    contract = await service.create_budget_contract(
        study_id=payload.study_id,
        site_id=payload.site_id,
        contract_name=payload.contract_name,
        tier_level=payload.tier_level,
        currency=payload.currency,
        total_budget=payload.total_budget,
        overhead_percentage=payload.overhead_percentage,
        withholding_percentage=payload.withholding_percentage,
        effective_date=payload.effective_date,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Created budget contract",
    )
    return map_contract_response(contract)


@router.get("/contracts", response_model=list[BudgetContractResponse])
async def list_budget_contracts(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    is_active_only: bool = Query(default=True),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[BudgetContractResponse]:
    """Lists budget contracts matching filters."""
    check_financial_read_access(principal)
    contracts = await service.list_budget_contracts(
        study_id=study_id, site_id=site_id, is_active_only=is_active_only
    )
    return [map_contract_response(c) for c in contracts]


@router.get("/contracts/{contract_id}", response_model=BudgetContractResponse)
async def get_budget_contract(
    contract_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> BudgetContractResponse:
    """Retrieves a single budget contract by ID."""
    check_financial_read_access(principal)
    contract = await service.get_budget_contract(contract_id)
    if not contract:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Budget contract '{contract_id}' not found.",
        )
    return map_contract_response(contract)


@router.post(
    "/contracts/{contract_id}/fee-schedules",
    response_model=FeeScheduleResponse,
    status_code=status.HTTP_201_CREATED,
)
async def add_fee_schedule(
    contract_id: str,
    payload: FeeScheduleCreate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> FeeScheduleResponse:
    """Adds a procedure reimbursement rate item to the contract fee schedule."""
    check_financial_write_access(principal)
    try:
        schedule = await service.add_fee_schedule(
            contract_id=contract_id,
            visit_code=payload.visit_code,
            procedure_code=payload.procedure_code,
            procedure_name=payload.procedure_name,
            base_amount=payload.base_amount,
            activity_id=payload.activity_id,
            form_id=payload.form_id,
            overhead_percentage=payload.overhead_percentage,
            withholding_percentage=payload.withholding_percentage,
            patient_stipend=payload.patient_stipend,
            travel_reimbursement=payload.travel_reimbursement,
            requires_sdv=payload.requires_sdv,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Fee schedule addition",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e))

    return map_fee_schedule_response(schedule)


@router.get(
    "/contracts/{contract_id}/fee-schedules",
    response_model=list[FeeScheduleResponse],
)
async def list_fee_schedules(
    contract_id: str,
    visit_code: str | None = Query(default=None),
    form_id: str | None = Query(default=None),
    is_active_only: bool = Query(default=True),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[FeeScheduleResponse]:
    """Lists procedure fee schedules configured for a contract."""
    check_financial_read_access(principal)
    schedules = await service.list_fee_schedules(
        contract_id=contract_id,
        visit_code=visit_code,
        form_id=form_id,
        is_active_only=is_active_only,
    )
    return [map_fee_schedule_response(s) for s in schedules]


# ==========================================
# 2. Split Payees
# ==========================================


@router.post(
    "/contracts/{contract_id}/split-payees",
    response_model=list[SplitPayeeConfigResponse],
    status_code=status.HTTP_201_CREATED,
)
async def configure_split_payees(
    contract_id: str,
    payload: SplitPayeeConfigBulkUpdate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[SplitPayeeConfigResponse]:
    """Configures multi-party split payee allocation percentages (must sum to 100%)."""
    check_financial_write_access(principal)
    try:
        payees = await service.configure_split_payees(
            contract_id=contract_id,
            payees=payload.payees,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Configured split payees",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return [map_split_payee_response(p) for p in payees]


@router.get(
    "/contracts/{contract_id}/split-payees",
    response_model=list[SplitPayeeConfigResponse],
)
async def list_split_payees(
    contract_id: str,
    is_active_only: bool = Query(default=True),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[SplitPayeeConfigResponse]:
    """Lists configured split payee shares for a contract."""
    check_financial_read_access(principal)
    payees = await service.list_split_payees(
        contract_id=contract_id, is_active_only=is_active_only
    )
    return [map_split_payee_response(p) for p in payees]


# ==========================================
# 3. Real-Time Clinical Event Ingestion
# ==========================================


@router.post(
    "/events/ecrf-submission",
    response_model=list[PayableResponse],
    status_code=status.HTTP_201_CREATED,
)
async def ingest_ecrf_submission_event(
    event: EcrfSubmissionEventDto,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[PayableResponse]:
    """Real-time event listener ingesting eCRF submission to trigger payables."""
    try:
        payables = await service.process_ecrf_submission_event(
            study_id=event.study_id,
            site_id=event.site_id,
            subject_id=event.subject_id,
            visit_code=event.visit_code,
            form_id=event.form_id,
            event_id=event.event_id,
            timestamp=event.timestamp,
            actor_id=event.actor_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "EDC eCRF Submission Trigger",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return [map_payable_response(p) for p in payables]


@router.post(
    "/events/cra-sdv-verification",
    response_model=PayableResponse | None,
    status_code=status.HTTP_200_OK,
)
async def ingest_cra_sdv_verification_event(
    event: CraSdvVerificationEventDto,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> PayableResponse | None:
    """Real-time event listener ingesting CRA SDV verification to unlock payables."""
    try:
        payable = await service.process_cra_sdv_verification_event(
            study_id=event.study_id,
            site_id=event.site_id,
            subject_id=event.subject_id,
            visit_code=event.visit_code,
            verified_by=event.verified_by,
            event_id=event.event_id,
            form_id=event.form_id,
            timestamp=event.timestamp,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "CRA SDV Verification Trigger",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_payable_response(payable) if payable else None


@router.post(
    "/events/milestone-completion",
    response_model=PayableResponse,
    status_code=status.HTTP_201_CREATED,
)
async def ingest_milestone_completion_event(
    event: MilestoneCompletionEventDto,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Real-time event listener ingesting CTMS milestone completion."""
    try:
        payable = await service.process_milestone_completion_event(
            study_id=event.study_id,
            site_id=event.site_id,
            milestone_code=event.milestone_code,
            milestone_name=event.milestone_name,
            amount=event.amount,
            verified_by=event.verified_by,
            event_id=event.event_id,
            timestamp=event.timestamp,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Milestone Trigger",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_payable_response(payable)


@router.post(
    "/events/batch",
    response_model=dict[str, Any],
    status_code=status.HTTP_200_OK,
)
async def ingest_batch_events(
    payload: BatchEventIngestDto,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    """Ingests batch of mixed clinical financial events."""
    ecrf_processed = 0
    sdv_processed = 0
    milestone_processed = 0

    for e in payload.ecrf_submissions:
        await service.process_ecrf_submission_event(
            study_id=e.study_id,
            site_id=e.site_id,
            subject_id=e.subject_id,
            visit_code=e.visit_code,
            form_id=e.form_id,
            event_id=e.event_id,
            timestamp=e.timestamp,
            actor_id=e.actor_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
        )
        ecrf_processed += 1

    for s in payload.sdv_verifications:
        await service.process_cra_sdv_verification_event(
            study_id=s.study_id,
            site_id=s.site_id,
            subject_id=s.subject_id,
            visit_code=s.visit_code,
            verified_by=s.verified_by,
            event_id=s.event_id,
            form_id=s.form_id,
            timestamp=s.timestamp,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
        )
        sdv_processed += 1

    for m in payload.milestones:
        await service.process_milestone_completion_event(
            study_id=m.study_id,
            site_id=m.site_id,
            milestone_code=m.milestone_code,
            milestone_name=m.milestone_name,
            amount=m.amount,
            verified_by=m.verified_by,
            event_id=m.event_id,
            timestamp=m.timestamp,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
        )
        milestone_processed += 1

    return {
        "status": "success",
        "ecrf_submissions_processed": ecrf_processed,
        "sdv_verifications_processed": sdv_processed,
        "milestones_processed": milestone_processed,
        "total_processed": ecrf_processed + sdv_processed + milestone_processed,
    }


# ==========================================
# 4. Payables & Allocations
# ==========================================


@router.get("/payables", response_model=list[PayableResponse])
async def list_payables(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    visit_code: str | None = Query(default=None),
    form_id: str | None = Query(default=None),
    status: str | None = Query(default=None),
    is_sdv_gated: bool | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[PayableResponse]:
    """Lists payables with split allocations and holdback reserve status."""
    check_financial_read_access(principal)
    payables = await service.list_payables(
        study_id=study_id,
        site_id=site_id,
        subject_id=subject_id,
        visit_code=visit_code,
        form_id=form_id,
        status=status,
        is_sdv_gated=is_sdv_gated,
    )
    return [map_payable_response(p) for p in payables]


@router.get("/payables/{payable_id}", response_model=PayableResponse)
async def get_payable(
    payable_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Retrieves payable details by ID."""
    check_financial_read_access(principal)
    payable = await service.get_payable(payable_id)
    if not payable:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Payable '{payable_id}' not found.",
        )
    return map_payable_response(payable)


@router.post("/payables/{payable_id}/approve", response_model=PayableResponse)
async def approve_payable(
    payable_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Approves a payable for disbursement."""
    check_financial_write_access(principal)
    try:
        payable = await service.approve_payable(
            payable_id=payable_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Approved payable",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_payable_response(payable)


# ==========================================
# 5. Holdback Retention Reserves & Governance
# ==========================================


@router.get("/holdbacks", response_model=list[HoldbackReserveResponse])
async def list_holdback_reserves(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    status: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[HoldbackReserveResponse]:
    """Lists 10% holdback retention reserves."""
    check_financial_read_access(principal)
    reserves = await service.list_holdback_reserves(
        study_id=study_id, site_id=site_id, status=status
    )
    return [map_holdback_response(hr) for hr in reserves]


@router.post("/holdbacks/release", response_model=HoldbackReleaseResponse)
async def release_holdback_reserves(
    payload: HoldbackReleaseRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> HoldbackReleaseResponse:
    """Releases holdback retention reserves with dual DM and Quality oversight sign-offs."""
    check_financial_write_access(principal)
    try:
        res = await service.release_holdback_reserves(
            study_id=payload.study_id,
            site_id=payload.site_id,
            release_reason=payload.release_reason,
            dm_signoff_by=payload.dm_signoff_by,
            quality_signoff_by=payload.quality_signoff_by,
            holdback_ids=payload.holdback_ids,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or payload.release_reason,
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return HoldbackReleaseResponse(
        study_id=res["study_id"],
        site_id=res["site_id"],
        total_released_amount=res["total_released_amount"],
        currency=res["currency"],
        released_count=res["released_count"],
        dm_signoff_by=res["dm_signoff_by"],
        quality_signoff_by=res["quality_signoff_by"],
        status=res["status"],
        released_reserves=[
            map_holdback_response(hr) for hr in res["released_reserves"]
        ],
    )


# ==========================================
# 6. Immutable Audit Trail
# ==========================================


@router.get("/audit-logs", response_model=list[PaymentAuditLogResponse])
async def list_payment_audit_logs(
    entity_type: str | None = Query(default=None),
    entity_id: str | None = Query(default=None),
    user_id: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[PaymentAuditLogResponse]:
    """Queries immutable 21 CFR Part 11 payment transaction audit logs."""
    check_financial_read_access(principal)
    logs = await service.list_audit_logs(
        entity_type=entity_type, entity_id=entity_id, user_id=user_id
    )
    return [
        PaymentAuditLogResponse(
            id=entry.id,
            entity_type=entry.entity_type,
            entity_id=entry.entity_id,
            action=entry.action,
            previous_state=entry.previous_state,
            new_state=entry.new_state,
            details=entry.details,
            user_id=entry.user_id,
            user_roles=entry.user_roles,
            created_at=entry.created_at,
            created_by=entry.created_by,
            reason_for_change=entry.reason_for_change,
            version_index=entry.version_index,
        )
        for entry in logs
    ]


# ==========================================
# 7. Self-Billing Invoicing
# ==========================================


@router.post(
    "/invoices/compile",
    response_model=SelfBillingInvoiceResponse,
    status_code=status.HTTP_201_CREATED,
)
async def compile_billing_cycle(
    payload: BillingCycleCompilationRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Automates monthly or quarterly billing cycle compilation into a self-billing invoice."""
    check_financial_write_access(principal)
    try:
        invoice = await service.compile_billing_cycle(
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[SelfBillingInvoiceResponse]:
    """Lists compiled self-billing invoices matching filter criteria."""
    check_financial_read_access(principal)
    invoices = await service.list_invoices(
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Retrieves full self-billing invoice details with line items and remittance advice."""
    check_financial_read_access(principal)
    invoice = await service.get_invoice(invoice_id)
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> SelfBillingInvoiceResponse:
    """Approves a draft self-billing invoice for ERP synchronization and disbursement."""
    check_financial_write_access(principal)
    try:
        invoice = await service.approve_self_billing_invoice(
            invoice_id=invoice_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Approved invoice",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))

    return map_invoice_response(invoice)


# ==========================================
# 8. Cryptographically Signed Remittance Advice
# ==========================================


@router.get(
    "/invoices/{invoice_id}/remittance-advice",
    response_model=RemittanceAdviceResponse,
)
async def get_remittance_advice(
    invoice_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Retrieves remittance advice statement record for an invoice."""
    check_financial_read_access(principal)
    remittance = await service.get_remittance_advice(invoice_id)
    if not remittance:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Remittance advice for invoice '{invoice_id}' not found.",
        )
    return map_remittance_response(remittance)


@router.get("/invoices/{invoice_id}/remittance-advice/pdf")
async def download_remittance_advice_pdf(
    invoice_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> Response:
    """Downloads binary PDF remittance advice statement."""
    check_financial_read_access(principal)
    remittance = await service.get_remittance_advice(invoice_id)
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Applies 21 CFR Part 11 cryptographic digital signature to remittance advice statement."""
    check_financial_write_access(principal)
    try:
        remittance = await service.sign_and_seal_remittance_advice(
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> RemittanceSignatureVerificationResponse:
    """Verifies cryptographic signature validity and tamper resistance of remittance advice statement."""
    check_financial_read_access(principal)
    try:
        res = await service.verify_remittance_advice_signature(invoice_id)
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> RemittanceAdviceResponse:
    """Dispatches signed remittance advice statement via automated email notification."""
    check_financial_write_access(principal)
    try:
        remittance = await service.dispatch_remittance_advice(
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
# 9. Enterprise ERP Synchronization (SAP/NetSuite) & Reconciliation
# ==========================================


@router.post(
    "/invoices/{invoice_id}/erp-export",
    response_model=ErpExportResponse,
)
async def export_invoice_to_erp(
    invoice_id: str,
    erp_system: str = Query(default="SAP", description="Target ERP (SAP, NETSUITE)"),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ErpExportResponse:
    """Exports invoice into standardized ERP transmission format (SAP/NetSuite)."""
    check_financial_write_access(principal)
    try:
        export_envelope = await service.export_invoice_to_erp(
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
    service: PaymentsService = Depends(get_payments_service),
) -> ErpWebhookResponse:
    """Inbound enterprise ERP webhook receiver for payment execution and reconciliation status updates."""
    try:
        res = await service.process_erp_webhook(
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> PaymentReconciliationResponse:
    """Reconciles payment execution against self-billing invoice and underlying payables."""
    check_financial_write_access(principal)
    try:
        res = await service.reconcile_invoice_payment(
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
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[ErpSyncLogResponse]:
    """Lists ERP synchronization history and webhook audit records for an invoice."""
    check_financial_read_access(principal)
    logs = await service.list_erp_sync_logs(
        invoice_id=invoice_id, erp_system=erp_system, status=status
    )
    return [map_erp_log_response(log) for log in logs]


# ==========================================
# 7. Participant Financial Wallets & Balances
# ==========================================


@router.post(
    "/participant/wallets",
    response_model=ParticipantWalletResponse,
    status_code=status.HTTP_201_CREATED,
)
async def get_or_create_participant_wallet(
    payload: ParticipantWalletCreate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Provisions or retrieves a participant financial wallet."""
    wallet = await service.get_or_create_wallet(
        subject_id=payload.subject_id,
        study_id=payload.study_id,
        site_id=payload.site_id,
        currency=payload.currency,
        virtual_card_token=payload.virtual_card_token,
        virtual_card_mask=payload.virtual_card_mask,
        virtual_card_expiry=payload.virtual_card_expiry,
        ach_routing_number=payload.ach_routing_number,
        ach_account_mask=payload.ach_account_mask,
        ach_account_holder=payload.ach_account_holder,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
    )
    return map_wallet_response(wallet)


@router.get(
    "/participant/wallets",
    response_model=list[ParticipantWalletResponse],
)
async def list_participant_wallets(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[ParticipantWalletResponse]:
    """Lists participant financial wallets filtered by study and/or site."""
    wallets = await service.list_wallets(study_id=study_id, site_id=site_id)
    return [map_wallet_response(w) for w in wallets]


@router.get(
    "/participant/wallets/{wallet_id}",
    response_model=ParticipantWalletResponse,
)
async def get_participant_wallet_by_id(
    wallet_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Retrieves participant wallet details and current balance."""
    wallet = await service.get_wallet_by_id(wallet_id)
    if not wallet:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Participant wallet '{wallet_id}' not found.",
        )
    return map_wallet_response(wallet)


@router.get(
    "/participant/wallets/subject/{study_id}/{subject_id}",
    response_model=ParticipantWalletResponse,
)
async def get_participant_wallet_by_subject(
    study_id: str,
    subject_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Retrieves participant wallet by study ID and subject ID."""
    wallet = await service.get_wallet_by_subject(
        study_id=study_id, subject_id=subject_id
    )
    if not wallet:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Wallet for subject '{subject_id}' in study '{study_id}' not found.",
        )
    return map_wallet_response(wallet)


@router.put(
    "/participant/wallets/{wallet_id}/payout-methods",
    response_model=ParticipantWalletResponse,
)
async def update_participant_wallet_payout_methods(
    wallet_id: str,
    payload: ParticipantWalletPayoutMethodsUpdate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Updates payout credentials (virtual card / direct ACH) for a participant wallet."""
    try:
        wallet = await service.update_wallet_payout_methods(
            wallet_id=wallet_id,
            virtual_card_token=payload.virtual_card_token,
            virtual_card_mask=payload.virtual_card_mask,
            virtual_card_expiry=payload.virtual_card_expiry,
            ach_routing_number=payload.ach_routing_number,
            ach_account_mask=payload.ach_account_mask,
            ach_account_holder=payload.ach_account_holder,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Updated Payout Methods",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_wallet_response(wallet)


@router.post(
    "/participant/wallets/{wallet_id}/disburse",
    response_model=DisbursementResponse,
    status_code=status.HTTP_201_CREATED,
)
async def disburse_participant_funds(
    wallet_id: str,
    payload: DisbursementRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> DisbursementResponse:
    """Dispatches digital funds payout (Virtual Card / Direct ACH) with 21 CFR Part 11 ledger recording."""
    try:
        disbursement = await service.disburse_funds(
            wallet_id=wallet_id,
            disbursement_type=payload.disbursement_type,
            amount=payload.amount,
            destination_mask=payload.destination_mask,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Digital Payout Request",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_disbursement_response(disbursement)


@router.get(
    "/participant/wallets/{wallet_id}/transactions",
    response_model=list[ParticipantTransactionResponse],
)
async def list_wallet_transactions(
    wallet_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[ParticipantTransactionResponse]:
    """Retrieves immutable financial transaction history for a participant wallet."""
    txs = await service.list_transactions(wallet_id=wallet_id)
    return [map_transaction_response(tx) for tx in txs]


@router.get(
    "/participant/disbursements",
    response_model=list[DisbursementResponse],
)
async def list_participant_disbursements(
    wallet_id: str | None = Query(default=None),
    study_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[DisbursementResponse]:
    """Lists all digital disbursements dispatched across participant wallets."""
    disbs = await service.list_disbursements(
        wallet_id=wallet_id, study_id=study_id, subject_id=subject_id
    )
    return [map_disbursement_response(d) for d in disbs]


# ==========================================
# 8. SoA Protocol Stipend Rules & Visit Triggers
# ==========================================


@router.post(
    "/participant/stipend-rules",
    response_model=ParticipantStipendRuleResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_participant_stipend_rule(
    payload: ParticipantStipendRuleCreate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ParticipantStipendRuleResponse:
    """Configures participant visit compensation rules linked to the Schedule of Activities."""
    check_financial_write_access(principal)
    rule = await service.create_stipend_rule(
        study_id=payload.study_id,
        visit_code=payload.visit_code,
        visit_name=payload.visit_name,
        amount=payload.amount,
        currency=payload.currency,
        procedure_code=payload.procedure_code,
        requires_verification=payload.requires_verification,
        taxable=payload.taxable,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Configured SoA Stipend Rule",
    )
    return map_stipend_rule_response(rule)


@router.get(
    "/participant/stipend-rules",
    response_model=list[ParticipantStipendRuleResponse],
)
async def list_participant_stipend_rules(
    study_id: str | None = Query(default=None),
    is_active_only: bool = Query(default=True),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[ParticipantStipendRuleResponse]:
    """Lists SoA protocol stipend compensation rules for a study."""
    rules = await service.list_stipend_rules(
        study_id=study_id, is_active_only=is_active_only
    )
    return [map_stipend_rule_response(r) for r in rules]


@router.post(
    "/participant/events/visit-verified",
    status_code=status.HTTP_200_OK,
)
async def ingest_visit_verified_event(
    payload: VisitVerificationEventDto,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    """Receives verified clinic visit completion from EDC and credits stipend to participant wallet."""
    return await service.process_visit_verified_stipend(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_id=payload.subject_id,
        visit_code=payload.visit_code,
        event_id=payload.event_id,
        verified_by=payload.verified_by or principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Verified Clinic Visit Completion",
    )


# ==========================================
# 9. Travel Expense Claims & Receipt OCR
# ==========================================


@router.post(
    "/participant/expenses/ocr-extract",
    response_model=ReceiptOcrExtractResponse,
)
async def extract_receipt_ocr(
    payload: ReceiptOcrExtractRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ReceiptOcrExtractResponse:
    """Performs OCR metadata extraction on a travel/meal expense receipt."""
    res = service.extract_receipt_ocr(
        image_base64=payload.image_base64,
        raw_text=payload.raw_text,
        category_hint=payload.category_hint,
        filename=payload.filename,
    )
    return ReceiptOcrExtractResponse(
        merchant_name=res.merchant_name,
        expense_date=res.expense_date,
        gross_amount=res.gross_amount,
        tax_deduction=res.tax_deduction,
        net_amount=res.net_amount,
        category=res.category,
        confidence_score=res.confidence_score,
        detected_keywords=res.detected_keywords,
        raw_text=res.raw_text,
    )


@router.post(
    "/participant/expenses",
    response_model=ExpenseClaimResponse,
    status_code=status.HTTP_201_CREATED,
)
async def submit_participant_expense_claim(
    payload: ExpenseClaimSubmitRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ExpenseClaimResponse:
    """Submits a mobile travel/meal expense reimbursement claim for coordinator review."""
    claim = await service.submit_expense_claim(
        subject_id=payload.subject_id,
        study_id=payload.study_id,
        site_id=payload.site_id,
        expense_date=payload.expense_date,
        category=payload.category,
        merchant_name=payload.merchant_name,
        gross_amount=payload.gross_amount,
        tax_deduction=payload.tax_deduction,
        net_amount=payload.net_amount,
        currency=payload.currency,
        wallet_id=payload.wallet_id,
        receipt_image_name=payload.receipt_image_name,
        receipt_image_base64=payload.receipt_image_base64,
        receipt_ocr_data=payload.receipt_ocr_data,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason
        or "Mobile Travel Reimbursement Submission",
    )
    return map_expense_claim_response(claim)


@router.get(
    "/participant/expenses",
    response_model=list[ExpenseClaimResponse],
)
async def list_participant_expense_claims(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    status: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[ExpenseClaimResponse]:
    """Lists submitted expense claims filtered by study, site, subject, or review status."""
    claims = await service.list_expense_claims(
        study_id=study_id, site_id=site_id, subject_id=subject_id, status=status
    )
    return [map_expense_claim_response(c) for c in claims]


@router.post(
    "/participant/expenses/{claim_id}/review",
    response_model=ExpenseClaimResponse,
)
async def review_participant_expense_claim(
    claim_id: str,
    payload: ExpenseClaimReviewRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> ExpenseClaimResponse:
    """Processes 1-click coordinator review and approval/rejection of a travel expense claim."""
    try:
        claim = await service.review_expense_claim(
            claim_id=claim_id,
            review_status=payload.status,
            reviewer_id=principal.user_id,
            reviewer_roles=",".join(principal.raw_roles),
            review_notes=payload.review_notes,
            adjusted_amount=payload.adjusted_amount,
            adjusted_category=payload.adjusted_category,
            adjusted_merchant=payload.adjusted_merchant,
            reason_for_change=principal.change_reason
            or f"Coordinator {payload.status.lower()} claim",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_expense_claim_response(claim)


# ==========================================
# 10. Plain Language Lay Summaries (EU CTR 536/2014)
# ==========================================


@router.post(
    "/lay-summaries",
    response_model=LaySummaryResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_lay_summary(
    payload: LaySummaryCreate,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> LaySummaryResponse:
    """Drafts or creates an EU CTR Regulation No 536/2014 Article 37 Plain Language Lay Summary."""
    summary = await service.create_or_update_lay_summary(
        study_id=payload.study_id,
        protocol_id=payload.protocol_id,
        title=payload.title,
        trial_phase=payload.trial_phase,
        therapeutic_area=payload.therapeutic_area,
        sponsor_name=payload.sponsor_name,
        summary_text=payload.summary_text,
        plain_language_sections=payload.plain_language_sections,
        irb_approval_number=payload.irb_approval_number,
        irb_approval_date=payload.irb_approval_date,
        language=payload.language,
        status=payload.status,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Created Lay Summary",
    )
    return map_lay_summary_response(summary)


@router.get(
    "/lay-summaries",
    response_model=list[LaySummaryResponse],
)
async def list_lay_summaries(
    study_id: str | None = Query(default=None),
    language: str | None = Query(default=None),
    status: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[LaySummaryResponse]:
    """Lists published and drafted plain-language trial summaries."""
    summaries = await service.list_lay_summaries(
        study_id=study_id, language=language, status=status
    )
    return [map_lay_summary_response(s) for s in summaries]


@router.get(
    "/lay-summaries/{summary_id}",
    response_model=LaySummaryResponse,
)
async def get_lay_summary(
    summary_id: str,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> LaySummaryResponse:
    """Retrieves details of a plain-language trial summary."""
    summary = await service.get_lay_summary_by_id(summary_id)
    if not summary:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Lay summary '{summary_id}' not found.",
        )
    return map_lay_summary_response(summary)


@router.post(
    "/lay-summaries/{summary_id}/publish",
    response_model=LaySummaryResponse,
)
async def publish_lay_summary(
    summary_id: str,
    payload: LaySummaryPublishRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> LaySummaryResponse:
    """Formally publishes an IRB-approved lay summary for participant access."""
    try:
        summary = await service.publish_lay_summary(
            summary_id=summary_id,
            irb_approval_number=payload.irb_approval_number,
            irb_approval_date=payload.irb_approval_date,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Published Lay Summary",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_lay_summary_response(summary)


@router.post(
    "/lay-summaries/{summary_id}/confirm-receipt",
    response_model=LaySummaryReceiptResponse,
    status_code=status.HTTP_201_CREATED,
)
async def confirm_lay_summary_receipt(
    summary_id: str,
    payload: LaySummaryReceiptConfirmRequest,
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> LaySummaryReceiptResponse:
    """Records participant confirmation of lay summary receipt with comprehension score and SHA-256 seal."""
    try:
        receipt = await service.confirm_lay_summary_receipt(
            summary_id=summary_id,
            subject_id=payload.subject_id,
            study_id=payload.study_id,
            comprehension_score=payload.comprehension_score,
            comprehension_feedback=payload.comprehension_feedback,
            confirmation_statement=payload.confirmation_statement,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_lay_summary_receipt_response(receipt)


@router.get(
    "/lay-summaries/{summary_id}/receipts",
    response_model=list[LaySummaryReceiptResponse],
)
async def list_lay_summary_receipts(
    summary_id: str,
    study_id: str | None = Query(default=None),
    subject_id: str | None = Query(default=None),
    service: PaymentsService = Depends(get_payments_service),
    principal: Principal = Depends(get_principal),
) -> list[LaySummaryReceiptResponse]:
    """Lists confirmation receipts for a lay summary for regulatory compliance audits."""
    receipts = await service.list_lay_summary_receipts(
        summary_id=summary_id, study_id=study_id, subject_id=subject_id
    )
    return [map_lay_summary_receipt_response(r) for r in receipts]
