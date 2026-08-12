"""FastAPI router for clinical queries.

Requirements: PRD-SYS-007
"""

import json
import uuid
from contextlib import suppress
from datetime import datetime
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request
from sqlalchemy import select

from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    ClinicalCodingAssignment,
    ClinicalQuery,
    CodingState,
)
from apps.execution.presentation.routers.queries_schemas import (
    ClinicalQueryResponse,
    QueryCancel,
    QueryCreate,
    QueryHistoryItem,
    QueryReopen,
    QueryRespond,
    QueryUpdate,
    SyncRequest,
)
from apps.execution.query_service import QueryService, StateTransitionError
from apps.execution.rtsm_authz import redact_response, verify_site_access
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    ROLE_SITE_INVESTIGATOR,
    Principal,
    get_principal,
    require_roles,
)
from packages.security.rbac import SITE_SCOPED_ROLES, can_access_study

router = APIRouter(prefix="/api/v1/execution", tags=["Queries"])


async def fetch_history(session: Any, query_id: str) -> list[QueryHistoryItem]:
    """Fetch and parse audit logs for a specific query."""
    stmt_history = (
        select(AuditLog)
        .where(
            AuditLog.table_name == "clinical_queries",
            AuditLog.record_id == query_id,
        )
        .order_by(AuditLog.timestamp.asc())
    )
    res_history = await session.execute(stmt_history)
    logs = res_history.scalars().all()
    history = []
    for log in logs:
        old_val = log.old_values
        new_val = log.new_values
        if isinstance(old_val, str):
            with suppress(Exception):
                old_val = json.loads(old_val)
        if isinstance(new_val, str):
            with suppress(Exception):
                new_val = json.loads(new_val)
        history.append(
            QueryHistoryItem(
                action=log.action,
                user_id=log.user_id,
                timestamp=log.timestamp,
                old_values=old_val,
                new_values=new_val,
                change_reason=log.change_reason,
                version_index=log.version_index,
            )
        )
    return history


async def _revert_coding_assignment_if_system_query_resolved(
    session: Any, q: ClinicalQuery
) -> None:
    """Helper to revert a QUERY_PENDING coding assignment back to UNCODED when its system query is closed/cancelled."""
    if q.origin == "SYSTEM_CODING":
        stmt_assign = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.observation_id == q.observation_id,
            ClinicalCodingAssignment.status == CodingState.QUERY_PENDING,
            ClinicalCodingAssignment.is_deleted.is_(False),
        )
        res_assign = await session.execute(stmt_assign)
        assignment = res_assign.scalars().first()
        if assignment:
            assignment.status = CodingState.UNCODED
            session.add(assignment)


@router.get("/queries", response_model=list[ClinicalQueryResponse])
async def list_queries(
    study_id: str | None = None,
    subject_id: str | None = None,
    visit_id: str | None = None,
    status: str | None = None,
    principal: Principal = Depends(get_principal),
) -> list[ClinicalQueryResponse]:
    """Retrieve a list of clinical queries with optional filtering."""
    if study_id and not can_access_study(principal, study_id):
        return []

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(ClinicalQuery.is_deleted.is_(False))
        if study_id:
            stmt = stmt.where(ClinicalQuery.study_id == study_id)
        if subject_id:
            stmt = stmt.where(ClinicalQuery.subject_id == subject_id)
        if visit_id:
            stmt = stmt.where(ClinicalQuery.visit_id == visit_id)
        if status:
            stmt = stmt.where(ClinicalQuery.status == status)

        user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]
        if user_site_roles or principal.assigned_sites:
            stmt = stmt.where(ClinicalQuery.site_id.in_(principal.assigned_sites))

        if principal.assigned_studies:
            stmt = stmt.where(ClinicalQuery.study_id.in_(principal.assigned_studies))

        res = await session.execute(stmt)
        queries = res.scalars().all()

        responses = []
        for q in queries:
            history = await fetch_history(session, q.id)
            responses.append(
                redact_response(
                    ClinicalQueryResponse(
                        id=q.id,
                        study_id=q.study_id,
                        subject_id=q.subject_id,
                        visit_id=q.visit_id,
                        domain=q.domain,
                        test_code=q.test_code,
                        status=q.status,
                        explanation=q.explanation,
                        response=q.response,
                        created_at=q.created_at,
                        updated_at=q.updated_at,
                        history=history,
                        observation_id=q.observation_id,
                        field_link=q.field_link,
                        message=q.message,
                        origin=q.origin,
                        priority=q.priority,
                        rule_id=q.rule_id,
                        created_by=q.created_by,
                        responder=q.responder,
                        resolver=q.resolver,
                        resolved_at=q.resolved_at,
                        cancellation_reason=q.cancellation_reason,
                        escalated_at=q.escalated_at,
                        form_id=q.form_id,
                        field_id=q.field_id,
                        query_type=q.query_type,
                        action_required=q.action_required,
                    ),
                    principal,
                )
            )
        return responses


@router.get("/queries/{query_id}", response_model=ClinicalQueryResponse)
async def get_query(
    query_id: str,
    principal: Principal = Depends(get_principal),
) -> ClinicalQueryResponse:
    """Query a single clinical query by ID, returning its full audit history."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        verify_site_access(
            principal, q.site_id, study_id=q.study_id, subject_id=q.subject_id
        )

        history = await fetch_history(session, q.id)
        return redact_response(
            ClinicalQueryResponse(
                id=q.id,
                study_id=q.study_id,
                subject_id=q.subject_id,
                visit_id=q.visit_id,
                domain=q.domain,
                test_code=q.test_code,
                status=q.status,
                explanation=q.explanation,
                response=q.response,
                created_at=q.created_at,
                updated_at=q.updated_at,
                history=history,
                observation_id=q.observation_id,
                field_link=q.field_link,
                message=q.message,
                origin=q.origin,
                priority=q.priority,
                rule_id=q.rule_id,
                created_by=q.created_by,
                responder=q.responder,
                resolver=q.resolver,
                resolved_at=q.resolved_at,
                cancellation_reason=q.cancellation_reason,
                escalated_at=q.escalated_at,
                form_id=q.form_id,
                field_id=q.field_id,
                query_type=q.query_type,
                action_required=q.action_required,
            ),
            principal,
        )


@router.post(
    "/queries",
    response_model=ClinicalQueryResponse,
    status_code=201,
)
async def open_query(
    request: Request,
    payload: QueryCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Raise a new clinical query on a specific field coordinate."""
    target_status = (payload.status or "OPEN").upper()
    if target_status not in ("CANDIDATE", "OPEN"):
        raise HTTPException(
            status_code=400,
            detail=f"Initial status must be CANDIDATE or OPEN. Received: {target_status}",
        )

    async with db_manager.get_session_maker()() as session:
        # Check if active query already exists on this coordinate
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.study_id == payload.study_id,
            ClinicalQuery.subject_id == payload.subject_id,
            ClinicalQuery.visit_id == payload.visit_id,
            ClinicalQuery.domain == payload.domain,
            ClinicalQuery.test_code == payload.test_code,
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        if res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail="An active query already exists on this target field coordinates.",
            )

        q = ClinicalQuery(
            study_id=payload.study_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            domain=payload.domain,
            test_code=payload.test_code,
            status=target_status,
            explanation=payload.explanation,
            observation_id=payload.observation_id,
            field_link=payload.field_link,
            message=payload.message or payload.explanation,
            origin=payload.origin or "manual",
            priority=payload.priority,
            rule_id=payload.rule_id,
            created_by=payload.created_by or current_user_id.get(),
            form_id=payload.form_id,
            field_id=payload.field_id,
            query_type=payload.query_type,
            action_required=payload.action_required,
        )
        session.add(q)
        await session.commit()

        # Refresh to get timestamps and trigger-generated IDs
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.post(
    "/queries/{query_id}/respond",
    response_model=ClinicalQueryResponse,
)
async def respond_query(
    query_id: str,
    request: Request,
    payload: QueryRespond,
    roles: list[str] = Depends(require_roles(ROLE_SITE_INVESTIGATOR)),
) -> ClinicalQueryResponse:
    """Submit an investigator response/answer to an open or reopened clinical query."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "ANSWERED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "ANSWERED"
        q.response = payload.response
        q.responder = payload.responder or current_user_id.get()
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.post(
    "/queries/{query_id}/close",
    response_model=ClinicalQueryResponse,
)
async def close_query(
    query_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Close an answered query (resolving the discrepancy loop)."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "CLOSED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "CLOSED"
        q.resolver = current_user_id.get()
        q.resolved_at = datetime.now()
        await _revert_coding_assignment_if_system_query_resolved(session, q)
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.post(
    "/queries/{query_id}/reopen",
    response_model=ClinicalQueryResponse,
)
async def reopen_query(
    query_id: str,
    request: Request,
    payload: QueryReopen | None = None,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Reopen an answered or closed clinical query for further clarification."""
    if payload is not None:
        reason_str = payload.reason or ""
    else:
        reason_str = (
            request.headers.get("X-Change-Reason", "")
            or current_change_reason.get()
            or ""
        )
    has_reason = bool(reason_str and reason_str.strip())

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(
                q.status, "REOPENED", has_reason=has_reason
            )
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "REOPENED"
        if has_reason:
            q.explanation = reason_str.strip()
        q.resolver = None
        q.resolved_at = None
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.post(
    "/queries/{query_id}/cancel",
    response_model=ClinicalQueryResponse,
)
async def cancel_query(
    query_id: str,
    request: Request,
    payload: QueryCancel,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Cancel a clinical query raised in error."""
    if not payload.reason or not payload.reason.strip():
        raise HTTPException(
            status_code=400, detail="Cancellation requires a non-empty reason."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "CANCELLED", has_reason=True)
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "CANCELLED"
        q.cancellation_reason = payload.reason
        q.resolver = current_user_id.get()
        q.resolved_at = datetime.now()
        await _revert_coding_assignment_if_system_query_resolved(session, q)
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.patch(
    "/queries/{query_id}",
    response_model=ClinicalQueryResponse,
)
async def update_query_state(
    query_id: str,
    request: Request,
    payload: QueryUpdate,
    roles: list[str] = Depends(
        require_roles(ROLE_CRA, ROLE_DATA_MANAGER, ROLE_SITE_INVESTIGATOR)
    ),
) -> ClinicalQueryResponse:
    """Transition a query through the designated state sequence and perform role checks."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        target_status = payload.status.upper()

        # Validate transition
        reason_val = (
            payload.cancellation_reason
            or payload.explanation
            or request.headers.get("X-Change-Reason", "")
            or current_change_reason.get()
            or ""
        ).strip()
        has_reason = bool(reason_val)

        try:
            QueryService.validate_transition(
                q.status, target_status, has_reason=has_reason
            )
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        # Enforce role boundaries depending on target transition state
        user_roles = roles
        cra_dm_roles = {
            "cra",
            "data manager",
            "data_manager",
            "sponsor_dm",
            "dm",
            "admin",
        }
        inv_roles = {
            "site investigator",
            "site_investigator",
            "site-investigator",
            "investigator",
            "investigator_user",
        }

        if target_status in ("CANDIDATE", "OPEN", "CLOSED", "REOPENED", "CANCELLED"):
            if not any(r in cra_dm_roles for r in user_roles):
                raise HTTPException(
                    status_code=403,
                    detail="User role is not authorized for this action.",
                )
        elif target_status == "ANSWERED":
            if not any(r in inv_roles for r in user_roles):
                raise HTTPException(
                    status_code=403,
                    detail="User role is not authorized for this action.",
                )

        q.status = target_status
        if payload.explanation is not None:
            q.explanation = payload.explanation
        if payload.response is not None:
            q.response = payload.response
        if payload.observation_id is not None:
            q.observation_id = payload.observation_id
        if payload.field_link is not None:
            q.field_link = payload.field_link
        if payload.message is not None:
            q.message = payload.message
        if payload.origin is not None:
            q.origin = payload.origin
        if payload.priority is not None:
            q.priority = payload.priority
        if payload.rule_id is not None:
            q.rule_id = payload.rule_id
        if payload.created_by is not None:
            q.created_by = payload.created_by
        if payload.responder is not None:
            q.responder = payload.responder
        if payload.resolver is not None:
            q.resolver = payload.resolver
        if payload.resolved_at is not None:
            q.resolved_at = payload.resolved_at
        if payload.cancellation_reason is not None:
            q.cancellation_reason = payload.cancellation_reason
        if payload.escalated_at is not None:
            q.escalated_at = payload.escalated_at

        if payload.form_id is not None:
            q.form_id = payload.form_id
        if payload.field_id is not None:
            q.field_id = payload.field_id
        if payload.query_type is not None:
            q.query_type = payload.query_type
        if payload.action_required is not None:
            q.action_required = payload.action_required

        if target_status == "CLOSED":
            q.resolver = current_user_id.get()
            q.resolved_at = datetime.now()
        elif target_status == "REOPENED":
            if q.status == "ANSWERED" and payload.explanation:
                q.explanation = payload.explanation
            q.resolver = None
            q.resolved_at = None
        elif target_status == "ANSWERED":
            q.responder = current_user_id.get()
        elif target_status == "CANCELLED":
            if payload.cancellation_reason:
                q.cancellation_reason = payload.cancellation_reason
            elif payload.explanation:
                q.cancellation_reason = payload.explanation
            q.resolver = current_user_id.get()
            q.resolved_at = datetime.now()

        if target_status in ("CLOSED", "CANCELLED"):
            await _revert_coding_assignment_if_system_query_resolved(session, q)

        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@router.post(
    "/queries/sync",
    status_code=200,
)
async def sync_queries(
    request: Request,
    payload: SyncRequest,
    roles: list[str] = Depends(
        require_roles(ROLE_CRA, ROLE_DATA_MANAGER, ROLE_SITE_INVESTIGATOR)
    ),
) -> dict[str, Any]:
    """Synchronize clinical query local ledger blocks to the target database."""
    # We map fieldId to CDASH domain & test_code
    field_map = {
        "brthdt": ("DM", "BRTHDT"),
        "sex": ("DM", "SEX"),
        "vssbp": ("VS", "VSSBP"),
        "vsdpb": ("VS", "VSDPB"),
        "pulse": ("VS", "VSHR"),
    }

    # Normalize caller roles
    from packages.security.rbac import ROLE_EXPANSIONS, get_normalized_roles

    user_roles = get_normalized_roles(request)

    expanded_allowed_dm = set(["data manager", "cra"])
    for r in ["data manager", "cra"]:
        if r in ROLE_EXPANSIONS:
            expanded_allowed_dm.update(ROLE_EXPANSIONS[r])

    expanded_allowed_inv = set(["site investigator"])
    for r in ["site investigator"]:
        if r in ROLE_EXPANSIONS:
            expanded_allowed_inv.update(ROLE_EXPANSIONS[r])

    has_dm_role = any(r in expanded_allowed_dm for r in user_roles)
    has_inv_role = any(r in expanded_allowed_inv for r in user_roles)

    # 21 CFR Part 11 compliant offline transaction sync block validation loop
    processed_count = 0
    async with db_manager.get_session_maker()() as session:
        for block in payload.blocks:
            action = block.action.upper()
            details = block.details

            # Validate role for this specific action
            if action in ("QUERY_CREATE", "QUERY_CLOSE", "QUERY_REOPEN"):
                if not has_dm_role:
                    raise HTTPException(
                        status_code=403,
                        detail=f"User role is not authorized for {action} action.",
                    )
            elif action == "QUERY_RESPOND" and not has_inv_role:
                raise HTTPException(
                    status_code=403,
                    detail=f"User role is not authorized for {action} action.",
                )

            # Extract/determine query coordinates
            study_id = details.study_id or "STUDY-USDM-001"
            subject_id = details.subject_id or "SUBJ-001"
            visit_id = details.visit_id or "Screening"

            # Map domain & test_code from field_id
            mapped_domain, mapped_test = field_map.get(
                details.field_id.lower(), ("VS", details.field_id.upper())
            )
            domain = details.domain or mapped_domain
            test_code = details.test_code or mapped_test

            # Find existing active query
            stmt = select(ClinicalQuery).where(
                ClinicalQuery.study_id == study_id,
                ClinicalQuery.subject_id == subject_id,
                ClinicalQuery.visit_id == visit_id,
                ClinicalQuery.domain == domain,
                ClinicalQuery.test_code == test_code,
                ClinicalQuery.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            q = res.scalars().first()

            if action == "QUERY_CREATE":
                if not q:
                    # Create a new query
                    q = ClinicalQuery(
                        id=str(uuid.uuid4()),
                        study_id=study_id,
                        subject_id=subject_id,
                        visit_id=visit_id,
                        domain=domain,
                        test_code=test_code,
                        status="OPEN",
                        explanation=details.query.message
                        if details.query
                        else "Offline raised discrepancy",
                        message=details.query.message
                        if details.query
                        else "Offline raised discrepancy",
                        created_by=request.state.user_id,
                    )
                    session.add(q)
                    processed_count += 1

            elif action == "QUERY_RESPOND":
                if q:
                    with suppress(StateTransitionError):
                        QueryService.validate_transition(q.status, "ANSWERED")
                        q.status = "ANSWERED"
                        if details.query and details.query.response:
                            q.response = details.query.response
                        q.responder = request.state.user_id
                        session.add(q)
                        processed_count += 1

            elif action == "QUERY_CLOSE":
                if q:
                    with suppress(StateTransitionError):
                        QueryService.validate_transition(q.status, "CLOSED")
                        q.status = "CLOSED"
                        q.resolver = request.state.user_id
                        q.resolved_at = datetime.utcnow()
                        session.add(q)
                        processed_count += 1

            elif action == "QUERY_REOPEN" and q:
                with suppress(StateTransitionError):
                    QueryService.validate_transition(
                        q.status, "REOPENED", has_reason=True
                    )
                    q.status = "REOPENED"
                    q.resolver = None
                    q.resolved_at = None
                    session.add(q)
                    processed_count += 1

        await session.commit()

    return {
        "status": "success",
        "processed_blocks": processed_count,
    }
