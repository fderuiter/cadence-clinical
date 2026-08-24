"""FastAPI sub-router for Participant Compensation Service.

Owns: Participant financial wallets, payout methods, SoA stipend rules, visit verification triggers,
mobile travel expense claims with OCR, coordinator review, digital disbursements, ledger transactions,
and EU CTR 536/2014 Plain Language Lay Summaries.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-004, PRD-PAY-006
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.adapters.database import get_db_session
from apps.payments.infrastructure.models import (
    LaySummaryModel,
    LaySummaryReceiptModel,
    ParticipantDisbursementModel,
    ParticipantExpenseClaimModel,
    ParticipantStipendRuleModel,
    ParticipantTransactionModel,
    ParticipantWalletModel,
)
from apps.payments.presentation.dtos import (
    BalanceInquiryResponse,
    DisbursementRequest,
    DisbursementResponse,
    ExpenseClaimResponse,
    ExpenseClaimReviewRequest,
    ExpenseClaimSubmitRequest,
    IssueReloadableCardRequest,
    IssueReloadableCardResponse,
    LaySummaryCreate,
    LaySummaryPublishRequest,
    LaySummaryReceiptConfirmRequest,
    LaySummaryReceiptResponse,
    LaySummaryResponse,
    ParticipantStipendRuleCreate,
    ParticipantStipendRuleResponse,
    ParticipantTransactionResponse,
    ParticipantWalletCreate,
    ParticipantWalletPayoutMethodsUpdate,
    ParticipantWalletResponse,
    ReceiptOcrExtractRequest,
    ReceiptOcrExtractResponse,
    Tax1099ThresholdResponse,
    VisitVerificationEventDto,
    W9SubmitRequest,
    W9SubmitResponse,
    W9VerifyRequest,
)
from apps.payments.presentation.routers.site_ledger import (
    check_financial_write_access,
)
from apps.payments.services.participant_compensation_service import (
    ParticipantCompensationService,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(tags=["Participant Compensation"])


def get_participant_compensation_service(
    session: AsyncSession = Depends(get_db_session),
) -> ParticipantCompensationService:
    """Dependency provider for ParticipantCompensationService."""
    return ParticipantCompensationService(session=session)


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
        taxable_ytd=entity.taxable_ytd,
        is_1099_threshold_exceeded=entity.is_1099_threshold_exceeded,
        w9_status=entity.w9_status,
        w9_submitted_at=entity.w9_submitted_at,
        w9_verified_at=entity.w9_verified_at,
        tax_id_last_four=entity.tax_id_last_four,
        card_type=entity.card_type,
        card_status=entity.card_status,
        virtual_card_token=entity.virtual_card_token,
        virtual_card_mask=entity.virtual_card_mask,
        virtual_card_expiry=entity.virtual_card_expiry,
        anti_coercion_cap=entity.anti_coercion_cap,
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
        anti_coercion_cap=entity.anti_coercion_cap,
        is_milestone=entity.is_milestone,
        milestone_code=entity.milestone_code,
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
        is_meal_allowance=entity.is_meal_allowance,
        per_diem_cap=entity.per_diem_cap,
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
# 1. Participant Financial Wallets & Balances
# ==========================================


@router.post(
    "/participant/wallets",
    response_model=ParticipantWalletResponse,
    status_code=status.HTTP_201_CREATED,
)
async def get_or_create_participant_wallet(
    payload: ParticipantWalletCreate,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Updates payout credentials (virtual card / direct ACH) for a participant wallet."""
    try:
        wallet = await service.update_wallet_payout_methods(
            wallet_id=wallet_id,
            virtual_card_token=payload.virtual_card_token,
            virtual_card_mask=payload.virtual_card_mask,
            virtual_card_expiry=payload.virtual_card_expiry,
            card_status=payload.card_status,
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
    "/participant/wallets/{wallet_id}/issue-card",
    response_model=IssueReloadableCardResponse,
    status_code=status.HTTP_201_CREATED,
)
async def issue_participant_reloadable_card(
    wallet_id: str,
    payload: IssueReloadableCardRequest,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> IssueReloadableCardResponse:
    """Provisions an automated reloadable debit card linked to the participant wallet."""
    try:
        res = await service.issue_reloadable_debit_card(
            wallet_id=wallet_id,
            cardholder_name=payload.cardholder_name,
            card_type=payload.card_type,
            initial_load=payload.initial_load,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Automated Debit Card Issuing",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return IssueReloadableCardResponse(**res)


@router.get(
    "/participant/wallets/{wallet_id}/balance",
    response_model=BalanceInquiryResponse,
)
async def get_participant_balance_inquiry(
    wallet_id: str,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> BalanceInquiryResponse:
    """Performs real-time multi-channel balance inquiry."""
    try:
        res = await service.get_balance_inquiry(wallet_id=wallet_id)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e))
    return BalanceInquiryResponse(**res)


@router.get(
    "/participant/wallets/{wallet_id}/tax-1099-status",
    response_model=Tax1099ThresholdResponse,
)
async def get_participant_1099_tax_status(
    wallet_id: str,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> Tax1099ThresholdResponse:
    """Retrieves real-time IRS Form 1099 tax threshold analysis and W-9 validation state."""
    try:
        res = await service.get_1099_tax_status(wallet_id=wallet_id)
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(e))
    return Tax1099ThresholdResponse(**res)


@router.post(
    "/participant/wallets/{wallet_id}/submit-w9",
    response_model=W9SubmitResponse,
)
async def submit_participant_w9_form(
    wallet_id: str,
    payload: W9SubmitRequest,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> W9SubmitResponse:
    """Submits IRS Form W-9 certification for participant 1099 compliance."""
    try:
        res = await service.submit_w9_form(
            wallet_id=wallet_id,
            tax_id_last_four=payload.tax_id_last_four,
            full_legal_name=payload.full_legal_name,
            certify_accurate=payload.certify_accurate,
            address=payload.address,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Submitted Form W-9 Certification",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return W9SubmitResponse(**res)


def check_coordinator_or_financial_write_access(principal: Principal) -> None:
    """Validates that user has coordinator or financial authority to verify participant documents."""
    if not (
        has_permission(principal, "ctms_financial:write")
        or has_permission(principal, "payments:write")
        or has_permission(principal, "ctms_study:create")
        or has_permission(principal, "edc_data:write")
        or any(
            r in ["CRC", "Coordinator", "Grants Manager", "Admin", "Investigator"]
            for r in principal.raw_roles
        )
    ):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: User lacks coordinator/financial authority.",
        )


@router.post(
    "/participant/wallets/{wallet_id}/verify-w9",
    response_model=ParticipantWalletResponse,
)
async def verify_participant_w9_form(
    wallet_id: str,
    payload: W9VerifyRequest,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> ParticipantWalletResponse:
    """Performs coordinator verification on submitted Form W-9."""
    check_coordinator_or_financial_write_access(principal)
    try:
        wallet = await service.verify_w9_form(
            wallet_id=wallet_id,
            verified_by=principal.user_id,
            reviewer_roles=",".join(principal.raw_roles),
            notes=payload.notes,
            reason_for_change=principal.change_reason or "Verified Form W-9",
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> list[DisbursementResponse]:
    """Lists all digital disbursements dispatched across participant wallets."""
    disbs = await service.list_disbursements(
        wallet_id=wallet_id, study_id=study_id, subject_id=subject_id
    )
    return [map_disbursement_response(d) for d in disbs]


# ==========================================
# 2. SoA Protocol Stipend Rules & Visit Triggers
# ==========================================


@router.post(
    "/participant/stipend-rules",
    response_model=ParticipantStipendRuleResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_participant_stipend_rule(
    payload: ParticipantStipendRuleCreate,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> ParticipantStipendRuleResponse:
    """Configures participant visit compensation rules linked to the Schedule of Activities."""
    check_financial_write_access(principal)
    try:
        rule = await service.create_stipend_rule(
            study_id=payload.study_id,
            visit_code=payload.visit_code,
            visit_name=payload.visit_name,
            amount=payload.amount,
            currency=payload.currency,
            procedure_code=payload.procedure_code,
            requires_verification=payload.requires_verification,
            taxable=payload.taxable,
            anti_coercion_cap=payload.anti_coercion_cap,
            is_milestone=payload.is_milestone,
            milestone_code=payload.milestone_code,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Configured SoA Stipend Rule",
        )
    except ValueError as e:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(e))
    return map_stipend_rule_response(rule)


@router.get(
    "/participant/stipend-rules",
    response_model=list[ParticipantStipendRuleResponse],
)
async def list_participant_stipend_rules(
    study_id: str | None = Query(default=None),
    is_active_only: bool = Query(default=True),
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
# 3. Travel Expense Claims & Receipt OCR
# ==========================================


@router.post(
    "/participant/expenses/ocr-extract",
    response_model=ReceiptOcrExtractResponse,
)
async def extract_receipt_ocr(
    payload: ReceiptOcrExtractRequest,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
# 4. Plain Language Lay Summaries (EU CTR 536/2014)
# ==========================================


@router.post(
    "/lay-summaries",
    response_model=LaySummaryResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_lay_summary(
    payload: LaySummaryCreate,
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
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
    service: ParticipantCompensationService = Depends(
        get_participant_compensation_service
    ),
    principal: Principal = Depends(get_principal),
) -> list[LaySummaryReceiptResponse]:
    """Lists confirmation receipts for a lay summary for regulatory compliance audits."""
    receipts = await service.list_lay_summary_receipts(
        summary_id=summary_id, study_id=study_id, subject_id=subject_id
    )
    return [map_lay_summary_receipt_response(r) for r in receipts]


__all__ = [
    "get_participant_compensation_service",
    "map_disbursement_response",
    "map_expense_claim_response",
    "map_lay_summary_receipt_response",
    "map_lay_summary_response",
    "map_stipend_rule_response",
    "map_transaction_response",
    "map_wallet_response",
    "router",
]
