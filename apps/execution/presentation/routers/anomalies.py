"""FastAPI router for cross-domain eCRF anomaly detection and candidate query adjudication.

Requirements: PRD-QRY-008, PRD-SYS-001
"""

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import audit_context
from apps.execution.database.models import AuditLog, ClinicalQuery
from apps.execution.presentation.routers.anomalies_schemas import (
    AdjudicationActionEnum,
    AdjudicationRequest,
    AdjudicationResponse,
    AnomalyCandidateItem,
    AnomalyEvaluateRequest,
    AnomalyEvaluateResponse,
)
from apps.execution.services.cross_domain_anomaly_service import (
    CrossDomainAnomalyService,
)
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    ROLE_SPONSOR_ADMIN,
    Principal,
    get_principal,
    require_roles,
)
from packages.security.rbac import SITE_SCOPED_ROLES, can_access_study

router = APIRouter(prefix="/api/v1/execution/anomalies", tags=["Anomalies"])


@router.post(
    "/evaluate",
    response_model=AnomalyEvaluateResponse,
    status_code=200,
)
async def evaluate_anomalies(
    payload: AnomalyEvaluateRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(ROLE_DATA_MANAGER, ROLE_CRA, ROLE_SPONSOR_ADMIN)
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> AnomalyEvaluateResponse:
    """Evaluates cross-domain clinical data for a subject and stages detected candidate queries."""
    if not can_access_study(principal, payload.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks access to study {payload.study_id}",
        )

    service = CrossDomainAnomalyService()
    with audit_context(
        user_id=principal.user_id,
        change_reason="On-demand cross-domain anomaly evaluation request",
    ):
        result = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id=payload.subject_id,
            study_id=payload.study_id,
            enable_ai=payload.enable_ai,
            auto_stage_queries=payload.auto_stage_queries,
        )
        await session.commit()

    return AnomalyEvaluateResponse(
        subject_id=result.subject_id,
        study_id=result.study_id,
        anomalies=result.anomalies,
        evaluated_at=result.evaluated_at,
        queries_staged_count=result.queries_staged_count,
    )


@router.get(
    "/candidates",
    response_model=list[AnomalyCandidateItem],
)
async def list_candidate_queries(
    study_id: str | None = Query(None, description="Filter by study ID"),
    subject_id: str | None = Query(None, description="Filter by subject ID"),
    domain: str | None = Query(None, description="Filter by primary domain"),
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(
        require_roles(ROLE_DATA_MANAGER, ROLE_CRA, ROLE_SPONSOR_ADMIN)
    ),
    session: AsyncSession = Depends(get_execution_db_session),
) -> list[AnomalyCandidateItem]:
    """Lists staged CANDIDATE clinical queries flagged by the anomaly worker for review."""
    if study_id and not can_access_study(principal, study_id):
        return []

    stmt = select(ClinicalQuery).where(
        ClinicalQuery.status == "CANDIDATE",
        ClinicalQuery.is_deleted.is_(False),
    )

    if study_id:
        stmt = stmt.where(ClinicalQuery.study_id == study_id)
    if subject_id:
        stmt = stmt.where(ClinicalQuery.subject_id == subject_id)
    if domain:
        stmt = stmt.where(ClinicalQuery.domain == domain)

    user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]
    if user_site_roles or principal.assigned_sites:
        stmt = stmt.where(ClinicalQuery.site_id.in_(principal.assigned_sites))

    if principal.assigned_studies:
        stmt = stmt.where(ClinicalQuery.study_id.in_(principal.assigned_studies))

    stmt = stmt.order_by(ClinicalQuery.created_at.desc())
    res = await session.execute(stmt)
    queries = res.scalars().all()

    return [
        AnomalyCandidateItem(
            query_id=q.id,
            study_id=q.study_id,
            subject_id=q.subject_id,
            site_id=q.site_id,
            visit_id=q.visit_id,
            domain=q.domain,
            test_code=q.test_code,
            observation_id=q.observation_id,
            field_link=q.field_link,
            rule_id=q.rule_id,
            message=q.message,
            explanation=q.explanation,
            origin=q.origin,
            priority=q.priority,
            status=q.status,
            created_at=q.created_at,
            created_by=q.created_by,
        )
        for q in queries
    ]


@router.post(
    "/candidates/{query_id}/adjudicate",
    response_model=AdjudicationResponse,
)
async def adjudicate_candidate_query(
    query_id: str,
    payload: AdjudicationRequest,
    principal: Principal = Depends(get_principal),
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
    session: AsyncSession = Depends(get_execution_db_session),
) -> AdjudicationResponse:
    """Adjudicates a staged CANDIDATE query: promotes to OPEN or dismisses to CANCELLED."""
    stmt = select(ClinicalQuery).where(
        ClinicalQuery.id == query_id,
        ClinicalQuery.is_deleted.is_(False),
    )
    res = await session.execute(stmt)
    query_record = res.scalars().first()

    if not query_record:
        raise HTTPException(
            status_code=404,
            detail=f"Candidate clinical query {query_id} not found.",
        )

    if query_record.status != "CANDIDATE":
        raise HTTPException(
            status_code=400,
            detail=f"Query {query_id} is in status '{query_record.status}', not 'CANDIDATE'.",
        )

    if not can_access_study(principal, query_record.study_id):
        raise HTTPException(
            status_code=403,
            detail=f"User lacks permission to adjudicate queries in study {query_record.study_id}",
        )

    now = datetime.now(UTC)
    with audit_context(
        user_id=principal.user_id,
        change_reason=payload.reason,
    ):
        if payload.action == AdjudicationActionEnum.APPROVE:
            query_record.status = "OPEN"
            query_record.created_by = principal.user_id
            if payload.updated_message:
                query_record.message = payload.updated_message
                query_record.explanation = payload.updated_message

            audit_entry = AuditLog(
                table_name="clinical_queries",
                record_id=query_record.id,
                action="UPDATE",
                user_id=principal.user_id,
                change_reason=f"Data Manager approved candidate query into active OPEN status: {payload.reason}",
            )
            session.add(audit_entry)
            action_desc = "promoted to OPEN"

        else:  # REJECT
            query_record.status = "CANCELLED"
            query_record.cancellation_reason = payload.reason
            query_record.resolver = principal.user_id
            query_record.resolved_at = now

            audit_entry = AuditLog(
                table_name="clinical_queries",
                record_id=query_record.id,
                action="UPDATE",
                user_id=principal.user_id,
                change_reason=f"Data Manager dismissed candidate query: {payload.reason}",
            )
            session.add(audit_entry)
            action_desc = "dismissed as CANCELLED"

        await session.commit()

    return AdjudicationResponse(
        query_id=query_record.id,
        new_status=query_record.status,
        action=payload.action.value,
        message=f"Query {query_id} successfully {action_desc}.",
        adjudicated_by=principal.user_id,
        adjudicated_at=now,
    )
