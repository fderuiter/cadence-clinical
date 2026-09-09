"""FastAPI sub-router for eConsent re-consent triggers, cohort tracking, and minor age-of-majority milestones."""

from datetime import date

from fastapi import APIRouter, Depends, Request
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.notifications_client import publish_notification
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLReconsentRepository,
    SQLSubjectConsentRepository,
)
from apps.econsent.application.use_cases import ReconsentService
from apps.econsent.presentation.dtos import (
    MinorMilestoneEvaluationRequest,
    MinorMilestoneEvaluationResponse,
    MinorMilestoneResultDTO,
    ReconsentRequirementResponse,
    ReconsentTriggerRequest,
    TriggerMajorityReconsentRequest,
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
    svc = ReconsentService(
        reconsent_repo,
        consent_repo,
        audit_repo,
        notification_dispatcher=publish_notification,
    )

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


@router.post(
    "/complete/{requirement_id}",
    response_model=ReconsentRequirementResponse,
)
async def complete_reconsent(
    request: Request,
    requirement_id: str,
    completed_consent_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ReconsentRequirementResponse:
    """Marks a re-consent requirement as completed following subject e-signature."""
    user_id = getattr(request.state, "user_id", "subject")
    change_reason = getattr(request.state, "change_reason", "Re-consent completed")

    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(reconsent_repo, consent_repo, audit_repo)

    r = await svc.complete_reconsent_requirement(
        requirement_id=requirement_id,
        completed_consent_id=completed_consent_id,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    return ReconsentRequirementResponse(
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


@router.post(
    "/minor-milestones/evaluate",
    response_model=MinorMilestoneEvaluationResponse,
)
async def evaluate_minor_milestones(
    payload: MinorMilestoneEvaluationRequest,
    session: AsyncSession = Depends(get_db_session),
) -> MinorMilestoneEvaluationResponse:
    """Evaluates minor subjects against jurisdiction legal age of majority rules."""
    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(reconsent_repo, consent_repo, audit_repo)

    raw_subjects = [s.model_dump() for s in payload.subjects]
    results = await svc.evaluate_minor_milestones(
        subjects=raw_subjects,
        as_of_date=payload.as_of_date,
        lead_time_days=payload.lead_time_days,
    )

    dto_results = [
        MinorMilestoneResultDTO(
            subject_pseudonym=r.subject_pseudonym,
            date_of_birth=r.date_of_birth,
            jurisdiction=r.jurisdiction,
            current_age_years=r.current_age_years,
            exact_age=r.exact_age,
            legal_age_threshold=r.legal_age_threshold,
            majority_reached_date=r.majority_reached_date,
            has_reached_majority=r.has_reached_majority,
            is_transition_due=r.is_transition_due,
            days_until_majority=r.days_until_majority,
            requires_adult_reconsent=r.requires_adult_reconsent,
            milestone_status=r.milestone_status,
        )
        for r in results
    ]

    majority_count = sum(1 for r in results if r.has_reached_majority)
    approaching_count = sum(
        1 for r in results if r.milestone_status == "APPROACHING_MAJORITY"
    )
    eval_date = payload.as_of_date or date.today().isoformat()

    return MinorMilestoneEvaluationResponse(
        study_id=payload.study_id,
        evaluation_date=eval_date,
        total_evaluated=len(results),
        majority_reached_count=majority_count,
        approaching_count=approaching_count,
        results=dto_results,
    )


@router.post(
    "/minor-milestones/trigger",
    response_model=list[ReconsentRequirementResponse],
    status_code=201,
)
async def trigger_majority_reconsent(
    request: Request,
    payload: TriggerMajorityReconsentRequest,
    session: AsyncSession = Depends(get_db_session),
) -> list[ReconsentRequirementResponse]:
    """Generates mandatory adult re-consent tasks for minor subjects who turned legal age."""
    user_id = getattr(request.state, "user_id", "site_investigator")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(
        reconsent_repo,
        consent_repo,
        audit_repo,
        notification_dispatcher=publish_notification,
    )

    raw_subjects = [s.model_dump() for s in payload.subjects]
    requirements = await svc.trigger_majority_reconsent_for_subjects(
        study_id=payload.study_id,
        site_id=payload.site_id,
        template_id=payload.template_id,
        adult_version_index=payload.adult_version_index,
        subjects=raw_subjects,
        lead_time_days=payload.lead_time_days,
        as_of_date=payload.as_of_date,
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
    "/minor-milestones/pending/{study_id}",
    response_model=list[ReconsentRequirementResponse],
)
async def get_pending_majority_reconsents(
    study_id: str,
    subject_pseudonym: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ReconsentRequirementResponse]:
    """Retrieves pending adult re-consent requirements resulting from age-of-majority transitions."""
    reconsent_repo = SQLReconsentRepository(session)
    consent_repo = SQLSubjectConsentRepository(session)
    audit_repo = SQLConsentAuditRepository(session)
    svc = ReconsentService(reconsent_repo, consent_repo, audit_repo)

    requirements = await svc.get_pending_reconsents(
        study_id=study_id, subject_pseudonym=subject_pseudonym
    )
    # Filter for majority milestone reasons
    majority_reqs = [
        r
        for r in requirements
        if "majority" in (r.reason_for_change or "").lower()
        or "majority" in (r.change_summary or "").lower()
    ]
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
        for r in majority_reqs
    ]
