"""FastAPI sub-router for subject consent revocation / withdrawal."""

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException, Request
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.models import ConsentWithdrawal
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentWithdrawalRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import WithdrawalService
from apps.econsent.presentation.dtos import (
    ConsentWithdrawalRequest,
    ConsentWithdrawalResponse,
)
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/econsent/withdrawal", tags=["Withdrawal"])
get_db_session = DatabaseSessionDependency(db_manager)


@router.post(
    "",
    response_model=ConsentWithdrawalResponse,
    status_code=201,
)
async def withdraw_subject_consent(
    request: Request,
    payload: ConsentWithdrawalRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentWithdrawalResponse:
    """Formally revokes/withdraws a subject's consent and records 21 CFR Part 11 audit records."""
    user_id = getattr(request.state, "user_id", "investigator")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    withdrawal_repo = SQLConsentWithdrawalRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = WithdrawalService(withdrawal_repo, consent_repo, audit_repo)

    withdrawal = await svc.withdraw_consent(
        study_id=payload.study_id,
        site_id=payload.site_id,
        subject_pseudonym=payload.subject_pseudonym,
        template_id=payload.template_id,
        withdrawal_date=payload.withdrawal_date or datetime.now(UTC),
        reason_category=payload.reason_category,
        reason_detail=payload.reason_detail,
        scope=payload.scope,
        investigator_id=payload.investigator_id or user_id,
        created_by=user_id,
        reason_for_change=change_reason,
    )

    return ConsentWithdrawalResponse(
        id=withdrawal.id,
        study_id=withdrawal.study_id,
        site_id=withdrawal.site_id,
        subject_pseudonym=withdrawal.subject_pseudonym,
        template_id=withdrawal.template_id,
        withdrawal_date=withdrawal.withdrawal_date,
        reason_category=withdrawal.reason_category,
        reason_detail=withdrawal.reason_detail,
        scope=withdrawal.scope,
        acknowledged_by_investigator=withdrawal.acknowledged_by_investigator,
        investigator_id=withdrawal.investigator_id,
        created_at=withdrawal.created_at,
        created_by=withdrawal.created_by,
        reason_for_change=withdrawal.reason_for_change,
    )


@router.get(
    "/{study_id}/{subject_pseudonym}",
    response_model=ConsentWithdrawalResponse,
)
async def get_subject_withdrawal_status(
    study_id: str,
    subject_pseudonym: str,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentWithdrawalResponse:
    """Retrieves the withdrawal record for a subject if one exists."""
    stmt = (
        select(ConsentWithdrawal)
        .where(
            ConsentWithdrawal.study_id == study_id,
            ConsentWithdrawal.subject_pseudonym == subject_pseudonym,
        )
        .order_by(ConsentWithdrawal.withdrawal_date.desc())
    )
    res = await session.execute(stmt)
    w = res.scalars().first()
    if not w:
        raise HTTPException(
            status_code=404,
            detail=f"No withdrawal record found for subject '{subject_pseudonym}' in study '{study_id}'.",
        )

    return ConsentWithdrawalResponse(
        id=w.id,
        study_id=w.study_id,
        site_id=w.site_id,
        subject_pseudonym=w.subject_pseudonym,
        template_id=w.template_id,
        withdrawal_date=w.withdrawal_date,
        reason_category=w.reason_category,
        reason_detail=w.reason_detail,
        scope=w.scope,
        acknowledged_by_investigator=w.acknowledged_by_investigator,
        investigator_id=w.investigator_id,
        created_at=w.created_at,
        created_by=w.created_by,
        reason_for_change=w.reason_for_change,
    )
