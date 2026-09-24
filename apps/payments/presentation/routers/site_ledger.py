"""FastAPI sub-router for Site Payment Ledger Engine.

Owns: Budget contracts, fee schedule matrices, split payee configs, real-time event ingestion,
payable ledgers, holdback retention releases, and GxP financial audit trails.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

from __future__ import annotations

from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.payments.adapters.database import get_db_session
from apps.payments.adapters.models import (
    BudgetContractModel,
    FeeScheduleModel,
    HoldbackReserveModel,
    PayableAllocationModel,
    PayableModel,
    SplitPayeeConfigModel,
)
from apps.payments.presentation.dtos import (
    BatchEventIngestDto,
    BudgetContractCreate,
    BudgetContractResponse,
    CraSdvVerificationEventDto,
    EcrfSubmissionEventDto,
    FeeScheduleCreate,
    FeeScheduleResponse,
    HoldbackReleaseRequest,
    HoldbackReleaseResponse,
    HoldbackReserveResponse,
    MilestoneCompletionEventDto,
    PayableAllocationResponse,
    PayableResponse,
    PaymentAuditLogResponse,
    SplitPayeeConfigBulkUpdate,
    SplitPayeeConfigResponse,
)
from apps.payments.services.site_payment_ledger_engine import (
    SitePaymentLedgerEngine,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter(tags=["Site Payment Ledger"])


def get_site_ledger_engine(
    session: AsyncSession = Depends(get_db_session),
) -> SitePaymentLedgerEngine:
    """Dependency provider for SitePaymentLedgerEngine."""
    return SitePaymentLedgerEngine(session=session)


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
        or has_permission(principal, "ctms_study:view")
        or has_permission(principal, "ctms_study:read")
    ):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: User lacks financial read authority.",
        )


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
        fee_schedules=[map_fee_schedule_response(fs) for fs in entity.fee_schedules]
        if entity.fee_schedules
        else [],
        split_payees=[map_split_payee_response(sp) for sp in entity.split_payees]
        if entity.split_payees
        else [],
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> BudgetContractResponse:
    """Creates or versions an active investigator grant contract schedule."""
    check_financial_write_access(principal)

    contract = await engine.create_budget_contract(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[BudgetContractResponse]:
    """Lists budget contracts matching filters."""
    check_financial_read_access(principal)
    contracts = await engine.list_budget_contracts(
        study_id=study_id, site_id=site_id, is_active_only=is_active_only
    )
    return [map_contract_response(c) for c in contracts]


@router.get("/contracts/{contract_id}", response_model=BudgetContractResponse)
async def get_budget_contract(
    contract_id: str,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> BudgetContractResponse:
    """Retrieves a single budget contract by ID."""
    check_financial_read_access(principal)
    contract = await engine.get_budget_contract(contract_id)
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> FeeScheduleResponse:
    """Adds a procedure reimbursement rate item to the contract fee schedule."""
    check_financial_write_access(principal)
    schedule = await engine.add_fee_schedule(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[FeeScheduleResponse]:
    """Lists procedure fee schedules configured for a contract."""
    check_financial_read_access(principal)
    schedules = await engine.list_fee_schedules(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[SplitPayeeConfigResponse]:
    """Configures multi-party split payee allocation percentages (must sum to 100%)."""
    check_financial_write_access(principal)
    payees = await engine.configure_split_payees(
        contract_id=contract_id,
        payees=payload.payees,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Configured split payees",
    )

    return [map_split_payee_response(p) for p in payees]


@router.get(
    "/contracts/{contract_id}/split-payees",
    response_model=list[SplitPayeeConfigResponse],
)
async def list_split_payees(
    contract_id: str,
    is_active_only: bool = Query(default=True),
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[SplitPayeeConfigResponse]:
    """Lists configured split payee shares for a contract."""
    check_financial_read_access(principal)
    payees = await engine.list_split_payees(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[PayableResponse]:
    """Real-time event listener ingesting eCRF submission to trigger payables."""
    payables = await engine.process_ecrf_submission_event(
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

    return [map_payable_response(p) for p in payables]


@router.post(
    "/events/cra-sdv-verification",
    response_model=PayableResponse | None,
    status_code=status.HTTP_200_OK,
)
async def ingest_cra_sdv_verification_event(
    event: CraSdvVerificationEventDto,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> PayableResponse | None:
    """Real-time event listener ingesting CRA SDV verification to unlock payables."""
    payable = await engine.process_cra_sdv_verification_event(
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

    return map_payable_response(payable) if payable else None


@router.post(
    "/events/milestone-completion",
    response_model=PayableResponse,
    status_code=status.HTTP_201_CREATED,
)
async def ingest_milestone_completion_event(
    event: MilestoneCompletionEventDto,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Real-time event listener ingesting CTMS milestone completion."""
    payable = await engine.process_milestone_completion_event(
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

    return map_payable_response(payable)


@router.post(
    "/events/batch",
    response_model=dict[str, Any],
    status_code=status.HTTP_200_OK,
)
async def ingest_batch_events(
    payload: BatchEventIngestDto,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    """Ingests batch of mixed clinical financial events."""
    ecrf_processed = 0
    sdv_processed = 0
    milestone_processed = 0

    for e in payload.ecrf_submissions:
        await engine.process_ecrf_submission_event(
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
        await engine.process_cra_sdv_verification_event(
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
        await engine.process_milestone_completion_event(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[PayableResponse]:
    """Lists payables with split allocations and holdback reserve status."""
    check_financial_read_access(principal)
    payables = await engine.list_payables(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Retrieves payable details by ID."""
    check_financial_read_access(principal)
    payable = await engine.get_payable(payable_id)
    if not payable:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Payable '{payable_id}' not found.",
        )
    return map_payable_response(payable)


@router.post("/payables/{payable_id}/approve", response_model=PayableResponse)
async def approve_payable(
    payable_id: str,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> PayableResponse:
    """Approves a payable for disbursement."""
    check_financial_write_access(principal)
    payable = await engine.approve_payable(
        payable_id=payable_id,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Approved payable",
    )

    return map_payable_response(payable)


# ==========================================
# 5. Holdback Retention Reserves & Governance
# ==========================================


@router.get("/holdbacks", response_model=list[HoldbackReserveResponse])
async def list_holdback_reserves(
    study_id: str | None = Query(default=None),
    site_id: str | None = Query(default=None),
    status: str | None = Query(default=None),
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[HoldbackReserveResponse]:
    """Lists 10% holdback retention reserves."""
    check_financial_read_access(principal)
    reserves = await engine.list_holdback_reserves(
        study_id=study_id, site_id=site_id, status=status
    )
    return [map_holdback_response(hr) for hr in reserves]


@router.post("/holdbacks/release", response_model=HoldbackReleaseResponse)
async def release_holdback_reserves(
    payload: HoldbackReleaseRequest,
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> HoldbackReleaseResponse:
    """Releases holdback retention reserves with dual DM and Quality oversight sign-offs."""
    check_financial_write_access(principal)
    res = await engine.release_holdback_reserves(
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
    engine: SitePaymentLedgerEngine = Depends(get_site_ledger_engine),
    principal: Principal = Depends(get_principal),
) -> list[PaymentAuditLogResponse]:
    """Queries immutable 21 CFR Part 11 payment transaction audit logs."""
    check_financial_read_access(principal)
    logs = await engine.list_audit_logs(
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


__all__ = [
    "check_financial_read_access",
    "check_financial_write_access",
    "get_site_ledger_engine",
    "map_contract_response",
    "map_fee_schedule_response",
    "map_holdback_response",
    "map_payable_response",
    "map_split_payee_response",
    "router",
]
