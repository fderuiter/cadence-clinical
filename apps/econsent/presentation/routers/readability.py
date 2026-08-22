"""FastAPI Router for eConsent Readability Harmonization Engine."""

import uuid
from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException, Request, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.adapters.database import db_manager
from apps.econsent.adapters.repositories import (
    SQLConsentAuditRepository,
    SQLConsentClauseRepository,
)
from apps.econsent.domain.entities import (
    ConsentAuditLogEntity,
    ConsentClauseEntity,
)
from apps.econsent.presentation.dtos import (
    ClauseHarmonizationApplyRequest,
    ClauseHarmonizationApplyResponse,
    JargonSubstitutionDTO,
    ReadabilityAnalysisRequest,
    ReadabilityAnalysisResponse,
    ReadabilityHarmonizationRequest,
    ReadabilityHarmonizationResponse,
    ReadabilityMetricsDTO,
)
from apps.econsent.services.readability import (
    ReadabilityHarmonizerService,
    ReadabilityMetricsService,
)
from packages.database import DatabaseSessionDependency
from packages.security.rbac import verify_not_auditor

router = APIRouter(prefix="/api/v1/econsent", tags=["Readability Harmonization"])
get_db_session = DatabaseSessionDependency(db_manager)

metrics_service = ReadabilityMetricsService()
harmonizer_service = ReadabilityHarmonizerService(metrics_service=metrics_service)


@router.post(
    "/readability/analyze",
    response_model=ReadabilityAnalysisResponse,
    status_code=status.HTTP_200_OK,
    summary="Compute real-time readability metrics for consent text",
)
async def analyze_readability(
    payload: ReadabilityAnalysisRequest,
) -> ReadabilityAnalysisResponse:
    """Computes Flesch-Kincaid Grade Level, Flesch Reading Ease, and Dale-Chall readability indices.

    Args:
        payload: ReadabilityAnalysisRequest containing narrative or clause text.

    Returns:
        ReadabilityAnalysisResponse with calculated grade levels, word counts, and target indicators.
    """
    res = metrics_service.compute_metrics(payload.text)
    return ReadabilityAnalysisResponse(metrics=ReadabilityMetricsDTO.from_domain(res))


@router.post(
    "/readability/harmonize",
    response_model=ReadabilityHarmonizationResponse,
    status_code=status.HTTP_200_OK,
    summary="Generate patient-friendly medical jargon substitutions via AI Gateway Tier 2",
)
async def harmonize_readability(
    payload: ReadabilityHarmonizationRequest,
) -> ReadabilityHarmonizationResponse:
    """Analyzes text for complex clinical jargon and suggests plain-language substitutions.

    Args:
        payload: ReadabilityHarmonizationRequest containing original text and target reading level.

    Returns:
        ReadabilityHarmonizationResponse with candidate substitutions, harmonized text, and delta scores.
    """
    result = await harmonizer_service.harmonize_text(
        text=payload.text,
        target_grade_level=payload.target_grade_level,
        study_id=payload.study_id,
    )

    orig_dto = ReadabilityMetricsDTO.from_domain(result.original_metrics)
    harm_dto = ReadabilityMetricsDTO.from_domain(result.harmonized_metrics)

    subs_dto = [
        JargonSubstitutionDTO(
            original_term=s.original_term,
            suggested_term=s.suggested_term,
            rationale=s.rationale,
            category=s.category,
            confidence_score=s.confidence_score,
            start_offset=s.start_offset,
            end_offset=s.end_offset,
        )
        for s in result.substitutions
    ]

    return ReadabilityHarmonizationResponse(
        original_metrics=orig_dto,
        harmonized_metrics=harm_dto,
        substitutions=subs_dto,
        harmonized_text=result.harmonized_text,
        grade_level_delta=result.grade_level_delta,
        model_identifier=result.model_identifier,
    )


@router.post(
    "/clauses/{clause_id}/harmonize",
    response_model=ClauseHarmonizationApplyResponse,
    status_code=status.HTTP_200_OK,
    summary="Apply plain-language readability harmonization to a clause with IRB amendment linking",
)
async def apply_clause_harmonization(
    request: Request,
    clause_id: str,
    payload: ClauseHarmonizationApplyRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ClauseHarmonizationApplyResponse:
    """Applies harmonized text to an ICF clause, incrementing version index and logging 21 CFR Part 11 audit trail.

    Args:
        request: Request object for extracting user_id and change_reason.
        clause_id: Identifier of the consent clause being harmonized.
        payload: ClauseHarmonizationApplyRequest with harmonized text and audit reason.
        session: Active asynchronous database session.

    Returns:
        ClauseHarmonizationApplyResponse with updated version index and new readability metrics.

    Raises:
        HTTPException: If clause_id is not found or auditor role attempts mutation.
    """
    verify_not_auditor(request)

    clause_repo = SQLConsentClauseRepository(session)
    audit_repo = SQLConsentAuditRepository(session)

    latest_clause = await clause_repo.get_latest_by_clause_id(clause_id)
    if not latest_clause:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Clause with ID '{clause_id}' not found.",
        )

    user_id = getattr(request.state, "user_id", "designer.user")
    change_reason = payload.reason_for_change

    # Calculate post-harmonization metrics
    new_metrics = metrics_service.compute_metrics(payload.harmonized_text)

    # Save new version of the clause
    updated_clause = ConsentClauseEntity(
        id=str(uuid.uuid4()),
        clause_id=clause_id,
        study_id=latest_clause.study_id,
        title=latest_clause.title,
        text=payload.harmonized_text,
        version_index=latest_clause.version_index + 1,
        created_at=datetime.now(UTC),
        created_by=user_id,
        reason_for_change=change_reason,
    )
    saved = await clause_repo.save(updated_clause)

    # Write 21 CFR Part 11 Audit Trail
    proto_tag = payload.protocol_version or "N/A"
    audit_log = ConsentAuditLogEntity(
        id=str(uuid.uuid4()),
        timestamp=datetime.now(UTC),
        actor_id=user_id,
        actor_role="designer",
        action="HARMONIZE_READABILITY_CLAUSE",
        document_id=saved.id,
        details=(
            f"Harmonized clause '{clause_id}' from v{latest_clause.version_index} to v{saved.version_index}. "
            f"Protocol Amendment: {proto_tag}. FKGL: {new_metrics.flesch_kincaid_grade_level}, "
            f"Dale-Chall: {new_metrics.dale_chall_score} ({new_metrics.dale_chall_grade_level})."
        ),
        reason_for_change=change_reason,
    )
    await audit_repo.save(audit_log)

    return ClauseHarmonizationApplyResponse(
        clause_id=saved.clause_id,
        version_index=saved.version_index,
        title=saved.title,
        text=saved.text,
        metrics=ReadabilityMetricsDTO.from_domain(new_metrics),
        protocol_version=payload.protocol_version,
        created_at=saved.created_at,
        created_by=saved.created_by,
        reason_for_change=saved.reason_for_change,
    )
