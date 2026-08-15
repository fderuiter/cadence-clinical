"""FastAPI sub-router for eConsent re-consent triggers and cohort tracking."""

from fastapi import APIRouter, Depends, Request
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLReconsentRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import ReconsentService
from apps.econsent.presentation.dtos import (
    ReconsentRequirementResponse,
    ReconsentTriggerRequest,
)
from packages.database import DatabaseSessionDependency

router = APIRouter(prefix="/api/v1/econsent/reconsent", tags=["Re-Consent"])
get_db_session = DatabaseSessionDependency(db_manager)


@router.post(
    "/trigger/{template_id}",
    response_model=list[ReconsentRequirementResponse],
    status_code=201,
)
async def trigger_reconsent(
    request: Request,
    template_id: str,
    payload: ReconsentTriggerRequest,
    session: AsyncSession = Depends(get_db_session),
) -> list[ReconsentRequirementResponse]:
    """Triggers re-consent requirements for all active enrolled subjects under an amended ICF."""
    user_id = getattr(request.state, "user_id", "study_designer")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(reconsent_repo, consent_repo, audit_repo)

    requirements = await svc.trigger_reconsent_for_active_cohort(
        study_id=payload.study_id,
        site_id=payload.site_id,
        template_id=template_id,
        prior_version_index=payload.prior_version_index,
        new_version_index=payload.new_version_index,
        change_summary=payload.change_summary,
        substantive_changes=payload.substantive_changes,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    return [
        ReconsentRequirementResponse(
            id=r.id,
            study_id=r.study_id,
            site_id=r.site_id,
            template_id=r.template_id,
            prior_version_index=r.prior_version_index,
            new_version_index=r.new_version_index,
            subject_pseudonym=r.subject_pseudonym,
            status=r.status,
            change_summary=r.change_summary,
            substantive_changes=r.substantive_changes,
            deadline_at=r.deadline_at,
            completed_consent_id=r.completed_consent_id,
            created_at=r.created_at,
            created_by=r.created_by,
            reason_for_change=r.reason_for_change,
        )
        for r in requirements
    ]


@router.get(
    "/pending/{study_id}",
    response_model=list[ReconsentRequirementResponse],
)
async def get_pending_reconsents(
    study_id: str,
    subject_pseudonym: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ReconsentRequirementResponse]:
    """Retrieves all pending re-consent requirements for a study or subject."""
    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(reconsent_repo, consent_repo, audit_repo)

    requirements = await svc.get_pending_reconsents(
        study_id=study_id, subject_pseudonym=subject_pseudonym
    )
    return [
        ReconsentRequirementResponse(
            id=r.id,
            study_id=r.study_id,
            site_id=r.site_id,
            template_id=r.template_id,
            prior_version_index=r.prior_version_index,
            new_version_index=r.new_version_index,
            subject_pseudonym=r.subject_pseudonym,
            status=r.status,
            change_summary=r.change_summary,
            substantive_changes=r.substantive_changes,
            deadline_at=r.deadline_at,
            completed_consent_id=r.completed_consent_id,
            created_at=r.created_at,
            created_by=r.created_by,
            reason_for_change=r.reason_for_change,
        )
        for r in requirements
    ]
