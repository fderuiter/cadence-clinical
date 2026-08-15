"""
FastAPI router for Tickets endpoints.
"""

import asyncio
import contextlib
import logging
from datetime import datetime

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Query, Request
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.adapters.database import get_db_session
from apps.tickets.adapters.models import Ticket, TicketAuditLog, TicketComment
from apps.tickets.application.notification_events import (
    generate_ticket_notification_payloads,
)
from apps.tickets.application.services import (
    TICKET_ESCALATE,  # noqa: F401
)
from apps.tickets.domain.models import (
    TERMINAL_STATES,
    TICKET_TRANSITIONS,
    TicketCategory,
    TicketPriority,
    TicketStatus,
)
from apps.tickets.domain.services import evaluate_setting_risk
from apps.tickets.presentation.dtos import (
    CommentCreate,
    CommentResponse,
    PaginatedTicketAuditLogResponse,
    RegulatoryRiskAssessment,
    SettingDiffEntry,
    TicketAssignPayload,
    TicketAuditLogResponse,
    TicketCreate,
    TicketResponse,
    TicketTransitionPayload,
    TicketUpdate,
)
from packages.security.context import audit_context
from packages.security.rbac import (
    Principal,
    can_access_site,
    can_access_study,
    get_principal,
    verify_not_auditor,
)

logger = logging.getLogger("tickets-router")
TICKET_CREATION_LOCK = asyncio.Lock()


async def write_ticket_audit_log(session, **kwargs):
    from apps.tickets.adapters.repositories import (
        write_ticket_audit_log as _write,
    )

    return await _write(session, **kwargs)


router = APIRouter()


async def dispatch_ticket_notifications(
    ticket_id: str,
    reference: str,
    assignee_user: str | None,
    assignee_role: str | None,
    reporter: str,
    version_index: int,
    event_type: str,
    actor_id: str,
    change_reason: str | None,
    old_status: str | None = None,
    new_status: str | None = None,
    comment_body: str | None = None,
) -> None:
    """
    Background task to generate and publish notifications for ticket events.
    Does not touch the active SQLAlchemy session, operating on captured committed values.
    Swallows and logs any exception.
    """
    try:

        class CommittedTicket:
            def __init__(
                self,
                id,
                reference,
                assignee_user,
                assignee_role,
                reporter,
                version_index,
            ):
                self.id = id
                self.reference = reference
                self.assignee_user = assignee_user
                self.assignee_role = assignee_role
                self.reporter = reporter
                self.version_index = version_index

        ticket = CommittedTicket(
            id=ticket_id,
            reference=reference,
            assignee_user=assignee_user,
            assignee_role=assignee_role,
            reporter=reporter,
            version_index=version_index,
        )

        with audit_context(user_id=actor_id, change_reason=change_reason):
            payloads = generate_ticket_notification_payloads(
                ticket,
                event_type,
                old_status=old_status,
                new_status=new_status,
                comment_body=comment_body,
            )
            for payload in payloads:
                try:
                    import apps.tickets.main as tickets_main

                    await tickets_main.publish_notification(payload)
                except Exception as e:
                    logger.error(
                        "Error publishing ticket notification payload for ticket %s: %s",
                        ticket_id,
                        e,
                        exc_info=True,
                    )
    except Exception as e:
        logger.error(
            "Error building/dispatching ticket notifications for ticket %s: %s",
            ticket_id,
            e,
            exc_info=True,
        )


def map_ticket_to_response(ticket: Ticket) -> TicketResponse:
    """
    Map a Ticket database model to a TicketResponse schema.
    """
    return TicketResponse(
        id=ticket.id,
        reference=ticket.reference,
        title=ticket.title,
        description=ticket.description,
        category=ticket.category.value
        if hasattr(ticket.category, "value")
        else str(ticket.category),
        priority=ticket.priority.value
        if hasattr(ticket.priority, "value")
        else str(ticket.priority),
        status=ticket.status.value
        if hasattr(ticket.status, "value")
        else str(ticket.status),
        reporter=ticket.reporter,
        assignee_user=ticket.assignee_user,
        assignee_role=ticket.assignee_role,
        org_id=ticket.org_id,
        site_id=ticket.site_id,
        study_id=ticket.study_id,
        related_entity_type=ticket.related_entity_type,
        related_entity_id=ticket.related_entity_id,
        due_date=ticket.due_date.isoformat() if ticket.due_date else None,
        is_deleted=ticket.is_deleted,
        created_at=ticket.created_at.isoformat(),
        created_by=ticket.created_by,
        reason_for_change=ticket.reason_for_change,
        version_index=ticket.version_index,
    )


async def get_next_ticket_reference(session: AsyncSession) -> str:
    """
    Generates a unique, human-readable ticket reference sequentially.
    """
    result = await session.execute(select(func.count(Ticket.id)))
    count = result.scalar() or 0
    idx = count + 1
    while True:
        candidate = f"TKT-{idx:05d}"
        exist_stmt = select(Ticket.id).where(Ticket.reference == candidate)
        exist_result = await session.execute(exist_stmt)
        if exist_result.scalar() is None:
            return candidate
        idx += 1


def check_optimistic_locking(
    ticket: Ticket, payload_version: int | None, request: Request
) -> None:
    """
    Verifies that the requested mutation specifies a matching expected version index.
    Raises HTTP 409 Conflict if missing or mismatched.
    """
    expected_version = payload_version
    if expected_version is None:
        q_val = request.query_params.get("version_index") or request.query_params.get(
            "expected_version"
        )
        if q_val:
            with contextlib.suppress(ValueError):
                expected_version = int(q_val)
    if expected_version is None:
        h_val = request.headers.get("If-Match") or request.headers.get(
            "X-Expected-Version"
        )
        if h_val:
            with contextlib.suppress(ValueError):
                expected_version = int(h_val)

    if expected_version is None:
        raise HTTPException(
            status_code=409,
            detail="Missing expected version index for optimistic locking.",
        )
    if ticket.version_index != expected_version:
        raise HTTPException(
            status_code=409,
            detail=f"Stale version index. Expected {expected_version}, but database version is {ticket.version_index}.",
        )


@router.post("/api/v1/tickets", response_model=TicketResponse, status_code=201)
async def create_ticket(
    request: Request,
    payload: TicketCreate,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """Create and persist a new Ticket record."""
    reporter = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    async with TICKET_CREATION_LOCK:
        reference = await get_next_ticket_reference(session)
        ticket = Ticket(
            reference=reference,
            title=payload.title,
            description=payload.description,
            category=payload.category,
            priority=payload.priority,
            status=TicketStatus.OPEN,
            reporter=reporter,
            assignee_user=payload.assignee_user,
            assignee_role=payload.assignee_role,
            org_id=payload.org_id,
            site_id=payload.site_id,
            study_id=payload.study_id,
            related_entity_type=payload.related_entity_type,
            related_entity_id=payload.related_entity_id,
            due_date=payload.due_date,
            created_by=reporter,
            reason_for_change=change_reason,
            version_index=1,
        )
        session.add(ticket)
        await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=reporter,
        action="TICKET_CREATE",
        details=f"Created ticket '{payload.title}' with priority '{payload.priority}'. Reference: '{ticket.reference}'",
        record_id=ticket.id,
        ticket_id=ticket.id,
        change_reason=change_reason,
        version_index=1,
    )

    return map_ticket_to_response(ticket)


@router.get("/api/v1/tickets", response_model=list[TicketResponse])
async def list_tickets(
    request: Request,
    status: TicketStatus | None = None,
    category: TicketCategory | None = None,
    priority: TicketPriority | None = None,
    reporter: str | None = None,
    assignee: str | None = None,
    org_id: str | None = None,
    site_id: str | None = None,
    study_id: str | None = None,
    include_deleted: bool = False,
    limit: int = Query(20, ge=1, le=100),
    offset: int = Query(0, ge=0),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> list[TicketResponse]:
    """List and filter tickets with pagination and scope-awareness."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    stmt = select(Ticket)
    if not include_deleted:
        stmt = stmt.where(Ticket.is_deleted.is_(False))

    site_scoped_roles = {"investigator", "crc", "cra", "external_monitor"}
    is_site_scoped = any(role in site_scoped_roles for role in principal.roles)

    if is_site_scoped or principal.assigned_sites or principal.assigned_studies:
        if is_site_scoped or principal.assigned_sites:
            if principal.assigned_sites:
                stmt = stmt.where(Ticket.site_id.in_(principal.assigned_sites))
            else:
                stmt = stmt.where(1 == 0)
        if "external_monitor" in principal.roles or principal.assigned_studies:
            if principal.assigned_studies:
                stmt = stmt.where(Ticket.study_id.in_(principal.assigned_studies))
            else:
                stmt = stmt.where(1 == 0)

    if site_id:
        if principal.assigned_sites and site_id not in principal.assigned_sites:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Insufficient scope access for this site.",
            )
        stmt = stmt.where(Ticket.site_id == site_id)

    if study_id:
        if principal.assigned_studies and study_id not in principal.assigned_studies:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Insufficient scope access for this study.",
            )
        stmt = stmt.where(Ticket.study_id == study_id)

    if status:
        stmt = stmt.where(Ticket.status == status)
    if category:
        stmt = stmt.where(Ticket.category == category)
    if priority:
        stmt = stmt.where(Ticket.priority == priority)
    if reporter:
        stmt = stmt.where(Ticket.reporter == reporter)
    if assignee:
        stmt = stmt.where(
            (Ticket.assignee_user == assignee) | (Ticket.assignee_role == assignee)
        )
    if org_id:
        stmt = stmt.where(Ticket.org_id == org_id)
    if study_id:
        stmt = stmt.where(Ticket.study_id == study_id)

    stmt = stmt.limit(limit).offset(offset)

    result = await session.execute(stmt)
    tickets = result.scalars().all()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_LIST",
        details=f"Listed tickets (status: {status}, category: {category}, priority: {priority}, limit: {limit}, offset: {offset}).",
        change_reason=change_reason,
        version_index=1,
    )

    return [map_ticket_to_response(t) for t in tickets]


@router.get(
    "/api/v1/tickets/audit-logs", response_model=PaginatedTicketAuditLogResponse
)
async def list_ticket_audit_logs(
    request: Request,
    ticket_id: str | None = None,
    limit: int = Query(50, ge=1, le=250),
    offset: int = Query(0, ge=0),
    start_time: datetime | None = Query(None),
    end_time: datetime | None = Query(None),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> PaginatedTicketAuditLogResponse:
    """Retrieve ticket audit logs in descending chronological order."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    actual_ticket_id = None
    if ticket_id:
        ticket_stmt = select(Ticket).where(
            (Ticket.id == ticket_id) | (Ticket.reference == ticket_id)
        )
        ticket_res = await session.execute(ticket_stmt)
        ticket = ticket_res.scalars().first()
        if not ticket:
            raise HTTPException(
                status_code=404,
                detail=f"Ticket with ID/reference '{ticket_id}' not found.",
            )
        actual_ticket_id = ticket.id
        if ticket.site_id and not can_access_site(principal, ticket.site_id):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Insufficient scope access for this site.",
            )

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_AUDIT_LOG_LIST",
        details="Listed ticket audit logs.",
        ticket_id=actual_ticket_id or ticket_id,
        change_reason=change_reason,
        version_index=1,
    )

    filters = []
    if actual_ticket_id:
        filters.append(TicketAuditLog.ticket_id == actual_ticket_id)
    else:
        site_scoped_roles = {"investigator", "crc", "cra", "external_monitor"}
        is_site_scoped = any(role in site_scoped_roles for role in principal.roles)

        if is_site_scoped or principal.assigned_sites:
            if principal.assigned_sites:
                subq = select(Ticket.id).where(
                    Ticket.site_id.in_(principal.assigned_sites)
                )
                filters.append(TicketAuditLog.ticket_id.in_(subq))
            else:
                filters.append(1 == 0)

    if start_time:
        filters.append(TicketAuditLog.created_at >= start_time)
    if end_time:
        filters.append(TicketAuditLog.created_at <= end_time)

    count_stmt = select(func.count(TicketAuditLog.id)).select_from(TicketAuditLog)
    for f in filters:
        count_stmt = count_stmt.where(f)
    count_res = await session.execute(count_stmt)
    total_count = count_res.scalar() or 0

    stmt = select(TicketAuditLog)
    for f in filters:
        stmt = stmt.where(f)
    stmt = stmt.order_by(TicketAuditLog.created_at.desc(), TicketAuditLog.id.desc())
    stmt = stmt.offset(offset).limit(limit)

    result = await session.execute(stmt)
    logs = result.scalars().all()

    items = [
        TicketAuditLogResponse(
            id=log.id,
            ticket_id=log.ticket_id,
            created_at=log.created_at.isoformat(),
            created_by=log.created_by,
            reason_for_change=log.reason_for_change,
            version_index=log.version_index,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
        )
        for log in logs
    ]

    has_more = (offset + limit) < total_count

    return PaginatedTicketAuditLogResponse(
        items=items,
        total_count=total_count,
        limit=limit,
        offset=offset,
        has_more=has_more,
    )


@router.get("/api/v1/tickets/{id}", response_model=TicketResponse)
async def get_ticket(
    request: Request,
    id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """Retrieve a specific ticket record by its ID or sequential reference."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    stmt = select(Ticket).where((Ticket.id == id) | (Ticket.reference == id))
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )
    if ticket.study_id and not can_access_study(principal, ticket.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this study.",
        )

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_VIEW",
        details=f"Viewed ticket reference/ID: {id}.",
        record_id=ticket.id,
        ticket_id=ticket.id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    return map_ticket_to_response(ticket)


@router.put("/api/v1/tickets/{id}", response_model=TicketResponse)
async def update_ticket(
    request: Request,
    id: str,
    payload: TicketUpdate,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """Update a specific ticket record by its ID or reference with optimistic locking and transitions."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = (
        select(Ticket)
        .where((Ticket.id == id) | (Ticket.reference == id))
        .with_for_update()
    )
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )
    if ticket.study_id and not can_access_study(principal, ticket.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this study.",
        )

    check_optimistic_locking(ticket, payload.version_index, request)

    is_reopening = payload.status == TicketStatus.REOPENED
    if ticket.status in TERMINAL_STATES and not is_reopening:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot update ticket because it is in terminal state '{ticket.status}'. Only reopening is allowed.",
        )

    current_status = ticket.status
    target_status = payload.status if payload.status is not None else ticket.status
    if current_status != target_status:
        if target_status not in TICKET_TRANSITIONS.get(current_status, set()):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid transition from {current_status} to {target_status}.",
            )

    has_assignment_diff = False
    if (
        payload.assignee_user is not None
        and payload.assignee_user != ticket.assignee_user
    ):
        has_assignment_diff = True
    if (
        payload.assignee_role is not None
        and payload.assignee_role != ticket.assignee_role
    ):
        has_assignment_diff = True

    has_status_change = False
    old_status_str = (
        current_status.value
        if hasattr(current_status, "value")
        else str(current_status)
    )
    if payload.status is not None and payload.status != ticket.status:
        has_status_change = True
        new_status_str = (
            payload.status.value
            if hasattr(payload.status, "value")
            else str(payload.status)
        )

    assignment_changes = []
    if (
        payload.assignee_user is not None
        and payload.assignee_user != ticket.assignee_user
    ):
        assignment_changes.append(
            f"assignee_user: '{ticket.assignee_user}' -> '{payload.assignee_user}'"
        )
    if (
        payload.assignee_role is not None
        and payload.assignee_role != ticket.assignee_role
    ):
        assignment_changes.append(
            f"assignee_role: '{ticket.assignee_role}' -> '{payload.assignee_role}'"
        )

    assignment_str = (
        "; ".join(assignment_changes) if assignment_changes else "No assignment changes"
    )
    actor_roles = ", ".join(principal.roles)
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{current_status.value if hasattr(current_status, 'value') else current_status}', "
        f"Target State: '{target_status.value if hasattr(target_status, 'value') else target_status}'. "
        f"Assignment Changes: '{assignment_str}'. "
        f"Reason: {change_reason}."
    )

    update_data = payload.model_dump(exclude_unset=True)
    if "version_index" in update_data:
        del update_data["version_index"]

    for key, val in update_data.items():
        setattr(ticket, key, val)

    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id

    await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_UPDATE",
        details=audit_details,
        record_id=ticket.id,
        ticket_id=ticket.id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    if has_assignment_diff:
        background_tasks.add_task(
            dispatch_ticket_notifications,
            ticket_id=ticket.id,
            reference=ticket.reference,
            assignee_user=ticket.assignee_user,
            assignee_role=ticket.assignee_role,
            reporter=ticket.reporter,
            version_index=ticket.version_index,
            event_type="assignment",
            actor_id=user_id,
            change_reason=change_reason,
        )

    if has_status_change:
        background_tasks.add_task(
            dispatch_ticket_notifications,
            ticket_id=ticket.id,
            reference=ticket.reference,
            assignee_user=ticket.assignee_user,
            assignee_role=ticket.assignee_role,
            reporter=ticket.reporter,
            version_index=ticket.version_index,
            event_type="transition",
            actor_id=user_id,
            change_reason=change_reason,
            old_status=old_status_str,
            new_status=new_status_str,
        )

    return map_ticket_to_response(ticket)


@router.post("/api/v1/tickets/{id}/transition", response_model=TicketResponse)
async def transition_ticket(
    request: Request,
    id: str,
    payload: TicketTransitionPayload,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """Transition a ticket's status explicitly with optimistic locking and transitions check."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = (
        select(Ticket)
        .where((Ticket.id == id) | (Ticket.reference == id))
        .with_for_update()
    )
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )
    if ticket.study_id and not can_access_study(principal, ticket.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this study.",
        )

    check_optimistic_locking(ticket, payload.version_index, request)

    current_status = ticket.status
    target_status = payload.status

    if current_status != target_status:
        if target_status not in TICKET_TRANSITIONS.get(current_status, set()):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid transition from {current_status} to {target_status}.",
            )

    old_status_str = (
        current_status.value
        if hasattr(current_status, "value")
        else str(current_status)
    )
    new_status_str = (
        target_status.value if hasattr(target_status, "value") else str(target_status)
    )

    actor_roles = ", ".join(principal.roles)
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{current_status.value if hasattr(current_status, 'value') else current_status}', "
        f"Target State: '{target_status.value if hasattr(target_status, 'value') else target_status}'. "
        f"Assignment Changes: 'No assignment changes'. "
        f"Reason: {change_reason}."
    )

    ticket.status = target_status
    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id

    await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_TRANSITION",
        details=audit_details,
        record_id=ticket.id,
        ticket_id=ticket.id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    background_tasks.add_task(
        dispatch_ticket_notifications,
        ticket_id=ticket.id,
        reference=ticket.reference,
        assignee_user=ticket.assignee_user,
        assignee_role=ticket.assignee_role,
        reporter=ticket.reporter,
        version_index=ticket.version_index,
        event_type="transition",
        actor_id=user_id,
        change_reason=change_reason,
        old_status=old_status_str,
        new_status=new_status_str,
    )

    return map_ticket_to_response(ticket)


@router.post("/api/v1/tickets/{id}/assign", response_model=TicketResponse)
async def assign_ticket(
    request: Request,
    id: str,
    payload: TicketAssignPayload,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """Assign a support ticket to an individual user and/or role-based routing target explicitly."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = (
        select(Ticket)
        .where((Ticket.id == id) | (Ticket.reference == id))
        .with_for_update()
    )
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )
    if ticket.study_id and not can_access_study(principal, ticket.study_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this study.",
        )

    check_optimistic_locking(ticket, payload.version_index, request)

    if ticket.status in TERMINAL_STATES:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot update ticket because it is in terminal state '{ticket.status}'.",
        )

    assignment_changes = []
    if (
        payload.assignee_user is not None
        and payload.assignee_user != ticket.assignee_user
    ):
        assignment_changes.append(
            f"assignee_user: '{ticket.assignee_user}' -> '{payload.assignee_user}'"
        )
    if (
        payload.assignee_role is not None
        and payload.assignee_role != ticket.assignee_role
    ):
        assignment_changes.append(
            f"assignee_role: '{ticket.assignee_role}' -> '{payload.assignee_role}'"
        )

    assignment_str = (
        "; ".join(assignment_changes) if assignment_changes else "No assignment changes"
    )
    actor_roles = ", ".join(principal.roles)
    status_val = (
        ticket.status.value if hasattr(ticket.status, "value") else ticket.status
    )
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{status_val}', Target State: '{status_val}'. "
        f"Assignment Changes: '{assignment_str}'. "
        f"Reason: {change_reason}."
    )

    if payload.assignee_user is not None:
        ticket.assignee_user = payload.assignee_user
    if payload.assignee_role is not None:
        ticket.assignee_role = payload.assignee_role

    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id

    await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_ASSIGN",
        details=audit_details,
        record_id=ticket.id,
        ticket_id=ticket.id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    background_tasks.add_task(
        dispatch_ticket_notifications,
        ticket_id=ticket.id,
        reference=ticket.reference,
        assignee_user=ticket.assignee_user,
        assignee_role=ticket.assignee_role,
        reporter=ticket.reporter,
        version_index=ticket.version_index,
        event_type="assignment",
        actor_id=user_id,
        change_reason=change_reason,
    )

    return map_ticket_to_response(ticket)


@router.post(
    "/api/v1/tickets/{id}/comments", response_model=CommentResponse, status_code=201
)
async def create_ticket_comment(
    request: Request,
    id: str,
    payload: CommentCreate,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> CommentResponse:
    """Append an auditable comment/note to a specific ticket."""
    user_id = principal.user_id
    change_reason = principal.change_reason
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    ticket_stmt = select(Ticket).where(Ticket.id == id)
    ticket_res = await session.execute(ticket_stmt)
    ticket = ticket_res.scalars().first()
    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )

    comment = TicketComment(
        ticket_id=id,
        body=payload.body,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(comment)
    await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_COMMENT_CREATE",
        details=f"Added comment to ticket ID: {id}.",
        record_id=comment.id,
        ticket_id=id,
        change_reason=change_reason,
        version_index=1,
    )

    background_tasks.add_task(
        dispatch_ticket_notifications,
        ticket_id=ticket.id,
        reference=ticket.reference,
        assignee_user=ticket.assignee_user,
        assignee_role=ticket.assignee_role,
        reporter=ticket.reporter,
        version_index=ticket.version_index,
        event_type="comment",
        actor_id=user_id,
        change_reason=change_reason,
        comment_body=comment.body,
    )

    return CommentResponse(
        id=comment.id,
        ticket_id=comment.ticket_id,
        body=comment.body,
        created_at=comment.created_at.isoformat(),
        created_by=comment.created_by,
        reason_for_change=comment.reason_for_change,
        version_index=comment.version_index,
    )


@router.get("/api/v1/tickets/{id}/comments", response_model=list[CommentResponse])
async def list_ticket_comments(
    request: Request,
    id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> list[CommentResponse]:
    """Retrieve all comments for a specific ticket in ascending chronological order."""
    user_id = principal.user_id
    change_reason = principal.change_reason

    ticket_stmt = select(Ticket).where(Ticket.id == id)
    ticket_res = await session.execute(ticket_stmt)
    ticket = ticket_res.scalars().first()
    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )

    stmt = (
        select(TicketComment)
        .where(TicketComment.ticket_id == id)
        .order_by(TicketComment.created_at.asc())
    )
    result = await session.execute(stmt)
    comments = result.scalars().all()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_COMMENTS_VIEW",
        details=f"Viewed comments for ticket ID: {id}.",
        record_id=id,
        ticket_id=id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    return [
        CommentResponse(
            id=c.id,
            ticket_id=c.ticket_id,
            body=c.body,
            created_at=c.created_at.isoformat(),
            created_by=c.created_by,
            reason_for_change=c.reason_for_change,
            version_index=c.version_index,
        )
        for c in comments
    ]


@router.post(
    "/api/v1/compliance/change-requests/analyze-diff",
    response_model=RegulatoryRiskAssessment,
)
async def analyze_diff_endpoint(
    payload: SettingDiffEntry,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
) -> RegulatoryRiskAssessment:
    """Analyze proposed setting change and evaluate GxP regulatory risk level.

    Requirements: PRD-SYS-001
    """
    try:
        metrics = evaluate_setting_risk(
            key=payload.setting_key,
            old_val=payload.old_value,
            new_val=payload.new_value,
        )
    except Exception as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    return RegulatoryRiskAssessment(
        risk_level=metrics["risk_level"],
        affected_gxp_clauses=metrics["affected_gxp_clauses"],
        requires_qa_signoff=metrics["requires_qa_signoff"],
        summary=metrics["summary"],
        risk_summary=metrics["risk_summary"],
    )
