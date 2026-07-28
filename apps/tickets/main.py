"""
FastAPI application for the Tickets microservice.
"""

import asyncio
import os
from datetime import datetime
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.database import db_manager
from apps.tickets.models import (
    Base,
    Ticket,
    TicketAuditLog,
    TicketCategory,
    TicketComment,
    TicketPriority,
    TicketStatus,
    TICKET_TRANSITIONS,
    TERMINAL_STATES,
    CANCELLABLE_STATES,
    REOPENABLE_STATES,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import (
    Principal,
    can_access_site,
    get_principal,
    verify_not_auditor,
)


class TicketCreate(BaseModel):
    """
    Pydantic schema for creating a support ticket.
    """

    title: str = Field(..., description="Title of the support ticket")
    description: str = Field(..., description="Detailed description of the issue")
    category: TicketCategory = Field(
        TicketCategory.OTHER, description="Category of the ticket"
    )
    priority: TicketPriority = Field(
        TicketPriority.LOW, description="Priority level of the ticket"
    )
    reporter: Optional[str] = Field(None, description="Reporter of the ticket")
    assignee_user: Optional[str] = Field(None, description="Assigned user")
    assignee_role: Optional[str] = Field(None, description="Assigned role")
    org_id: Optional[str] = Field(None, description="Scope organization ID")
    site_id: Optional[str] = Field(None, description="Scope site ID")
    study_id: Optional[str] = Field(None, description="Scope study ID")
    related_entity_type: Optional[str] = Field(None, description="Related entity type")
    related_entity_id: Optional[str] = Field(None, description="Related entity ID")
    due_date: Optional[datetime] = Field(None, description="Optional due date")


class TicketUpdate(BaseModel):
    """
    Pydantic schema for updating an existing support ticket.
    """

    title: Optional[str] = Field(None, description="Updated title")
    description: Optional[str] = Field(None, description="Updated description")
    category: Optional[TicketCategory] = Field(None, description="Updated category")
    priority: Optional[TicketPriority] = Field(None, description="Updated priority")
    status: Optional[TicketStatus] = Field(None, description="Updated status")
    assignee_user: Optional[str] = Field(None, description="Updated assigned user")
    assignee_role: Optional[str] = Field(None, description="Updated assigned role")
    org_id: Optional[str] = Field(None, description="Updated organization scope")
    site_id: Optional[str] = Field(None, description="Updated site scope")
    study_id: Optional[str] = Field(None, description="Updated study scope")
    related_entity_type: Optional[str] = Field(
        None, description="Updated related entity type"
    )
    related_entity_id: Optional[str] = Field(
        None, description="Updated related entity ID"
    )
    due_date: Optional[datetime] = Field(None, description="Updated due date")
    is_deleted: Optional[bool] = Field(None, description="Soft delete state")
    version_index: Optional[int] = Field(None, description="Expected version index for optimistic locking")


class TicketAssignPayload(BaseModel):
    """
    Pydantic schema for assigning a support ticket.
    """

    assignee_user: Optional[str] = Field(None, description="Username of the assignee")
    assignee_role: Optional[str] = Field(None, description="Role-based routing target")
    version_index: int = Field(..., description="Expected version index for optimistic locking")


class TicketTransitionPayload(BaseModel):
    """
    Pydantic schema for transitioning support ticket lifecycle status.
    """

    status: TicketStatus = Field(..., description="Target status for the lifecycle transition")
    version_index: int = Field(..., description="Expected version index for optimistic locking")


class TicketResponse(BaseModel):
    """
    Pydantic schema for returning support ticket details.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    reference: str
    title: str
    description: str
    category: str
    priority: str
    status: str
    reporter: str
    assignee_user: Optional[str] = None
    assignee_role: Optional[str] = None
    org_id: Optional[str] = None
    site_id: Optional[str] = None
    study_id: Optional[str] = None
    related_entity_type: Optional[str] = None
    related_entity_id: Optional[str] = None
    due_date: Optional[str] = None
    is_deleted: bool
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class CommentCreate(BaseModel):
    """
    Pydantic schema for creating a ticket comment.
    """

    body: str = Field(..., description="The comment body text")


class CommentResponse(BaseModel):
    """
    Pydantic schema for returning ticket comment details.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: str
    body: str
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class TicketAuditLogResponse(BaseModel):
    """
    Pydantic schema for returning ticket audit logs.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    ticket_id: Optional[str] = None
    created_at: str
    created_by: str
    reason_for_change: Optional[str] = None
    version_index: int
    action: str
    details: str
    record_id: Optional[str] = None


DATABASE_URL = os.getenv("TICKETS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - Tickets Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Dependable to obtain database session
get_db_session = DatabaseSessionDependency(db_manager)


def get_user_context(request: Request):
    """
    Helper to extract user identity headers parsed by GatewayAuthMiddleware.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = request.headers.get("X-User-Roles", "system")
    change_reason = getattr(
        request.state, "change_reason", None
    ) or request.headers.get("X-Change-Reason")
    return user_id, user_role, change_reason


async def write_ticket_audit_log(
    session: AsyncSession,
    user_id: str,
    action: str,
    details: str,
    record_id: Optional[str] = None,
    ticket_id: Optional[str] = None,
    change_reason: Optional[str] = None,
    version_index: int = 1,
) -> None:
    """
    Utility function to write to the immutable Ticket audit ledger.
    """
    log_entry = TicketAuditLog(
        created_by=user_id,
        action=action,
        details=details,
        record_id=record_id,
        ticket_id=ticket_id,
        reason_for_change=change_reason,
        version_index=version_index,
    )
    session.add(log_entry)
    await session.flush()


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "tickets"}


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


# Global lock to serialize reference generation and ticket insertion safely under concurrent creates.
TICKET_CREATION_LOCK = asyncio.Lock()


def check_optimistic_locking(ticket: Ticket, payload_version: Optional[int], request: Request) -> None:
    """
    Verifies that the requested mutation specifies a matching expected version index.
    Raises HTTP 409 Conflict if missing or mismatched.
    """
    expected_version = payload_version
    if expected_version is None:
        q_val = request.query_params.get("version_index") or request.query_params.get("expected_version")
        if q_val:
            try:
                expected_version = int(q_val)
            except ValueError:
                pass
    if expected_version is None:
        h_val = request.headers.get("If-Match") or request.headers.get("X-Expected-Version")
        if h_val:
            try:
                expected_version = int(h_val)
            except ValueError:
                pass

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


# Tickets Endpoints
@app.post("/api/v1/tickets", response_model=TicketResponse, status_code=201)
async def create_ticket(
    request: Request,
    payload: TicketCreate,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Create and persist a new Ticket record.
    """
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
            status=TicketStatus.OPEN,  # Always default to OPEN
            reporter=reporter,  # Obtained from authenticated principal; never trust request-supplied payload.reporter
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


@app.get("/api/v1/tickets", response_model=List[TicketResponse])
async def list_tickets(
    request: Request,
    status: Optional[TicketStatus] = None,
    category: Optional[TicketCategory] = None,
    priority: Optional[TicketPriority] = None,
    reporter: Optional[str] = None,
    assignee: Optional[str] = None,
    org_id: Optional[str] = None,
    site_id: Optional[str] = None,
    study_id: Optional[str] = None,
    include_deleted: bool = False,
    limit: int = Query(20, ge=1, le=100),
    offset: int = Query(0, ge=0),
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> List[TicketResponse]:
    """
    List and filter tickets with pagination and scope-awareness.
    """
    user_id = principal.user_id
    change_reason = principal.change_reason

    stmt = select(Ticket)
    if not include_deleted:
        stmt = stmt.where(Ticket.is_deleted.is_(False))

    # Scope-awareness
    site_scoped_roles = {"investigator", "crc", "cra"}
    is_site_scoped = any(role in site_scoped_roles for role in principal.roles)

    if is_site_scoped or principal.assigned_sites:
        if principal.assigned_sites:
            stmt = stmt.where(Ticket.site_id.in_(principal.assigned_sites))
        else:
            stmt = stmt.where(1 == 0)

    # For other/any users, if site_id filter is supplied, verify access against assigned_sites if restricted
    if site_id:
        if principal.assigned_sites and site_id not in principal.assigned_sites:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Insufficient scope access for this site.",
            )
        stmt = stmt.where(Ticket.site_id == site_id)

    # Composable filters
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

    # Pagination
    stmt = stmt.limit(limit).offset(offset)

    result = await session.execute(stmt)
    tickets = result.scalars().all()

    # Log listing action
    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_LIST",
        details=f"Listed tickets (status: {status}, category: {category}, priority: {priority}, limit: {limit}, offset: {offset}).",
        change_reason=change_reason,
        version_index=1,
    )

    return [map_ticket_to_response(t) for t in tickets]


# Audit Logs Retrieval Endpoint
@app.get("/api/v1/tickets/audit-logs", response_model=List[TicketAuditLogResponse])
async def list_ticket_audit_logs(
    request: Request,
    ticket_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[TicketAuditLogResponse]:
    """
    Retrieve ticket audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Note: Recording self-auditing list action first so it is included in the query result.
    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_AUDIT_LOG_LIST",
        details="Listed ticket audit logs.",
        ticket_id=ticket_id,
        change_reason=change_reason,
        version_index=1,
    )

    stmt = select(TicketAuditLog)
    if ticket_id:
        stmt = stmt.where(TicketAuditLog.ticket_id == ticket_id)
    stmt = stmt.order_by(TicketAuditLog.created_at.desc())

    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
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


@app.get("/api/v1/tickets/{id}", response_model=TicketResponse)
async def get_ticket(
    request: Request,
    id: str,
    principal: Principal = Depends(get_principal),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Retrieve a specific ticket record by its ID or sequential reference.
    """
    user_id = principal.user_id
    change_reason = principal.change_reason

    stmt = select(Ticket).where((Ticket.id == id) | (Ticket.reference == id))
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    # Validate scope access
    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
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


@app.put("/api/v1/tickets/{id}", response_model=TicketResponse)
async def update_ticket(
    request: Request,
    id: str,
    payload: TicketUpdate,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Update a specific ticket record by its ID or reference with optimistic locking and transitions.
    """
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = select(Ticket).where((Ticket.id == id) | (Ticket.reference == id)).with_for_update()
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    # Validate scope access before making edits
    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )

    # Check optimistic locking
    check_optimistic_locking(ticket, payload.version_index, request)

    # Reject updates to terminal tickets unless explicitly transitioning to REOPENED
    is_reopening = (payload.status == TicketStatus.REOPENED)
    if ticket.status in TERMINAL_STATES and not is_reopening:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot update ticket because it is in terminal state '{ticket.status}'. Only reopening is allowed.",
        )

    # Validate status transition if provided
    current_status = ticket.status
    target_status = payload.status if payload.status is not None else ticket.status
    if current_status != target_status:
        if target_status not in TICKET_TRANSITIONS.get(current_status, set()):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid transition from {current_status} to {target_status}.",
            )

    # Track audit details
    assignment_changes = []
    if payload.assignee_user is not None and payload.assignee_user != ticket.assignee_user:
        assignment_changes.append(f"assignee_user: '{ticket.assignee_user}' -> '{payload.assignee_user}'")
    if payload.assignee_role is not None and payload.assignee_role != ticket.assignee_role:
        assignment_changes.append(f"assignee_role: '{ticket.assignee_role}' -> '{payload.assignee_role}'")

    assignment_str = "; ".join(assignment_changes) if assignment_changes else "No assignment changes"
    actor_roles = ", ".join(principal.roles)
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{current_status.value if hasattr(current_status, 'value') else current_status}', "
        f"Target State: '{target_status.value if hasattr(target_status, 'value') else target_status}'. "
        f"Assignment Changes: '{assignment_str}'. "
        f"Reason: {change_reason}."
    )

    # Apply updates for explicitly defined fields in TicketUpdate payload (excluding version_index)
    update_data = payload.model_dump(exclude_unset=True)
    if "version_index" in update_data:
        del update_data["version_index"]

    for key, val in update_data.items():
        setattr(ticket, key, val)

    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id  # update editor context for current version

    await session.flush()

    # Log update in TicketAuditLog
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

    return map_ticket_to_response(ticket)


@app.post("/api/v1/tickets/{id}/transition", response_model=TicketResponse)
async def transition_ticket(
    request: Request,
    id: str,
    payload: TicketTransitionPayload,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Transition a ticket's status explicitly with optimistic locking and transitions check.
    """
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = select(Ticket).where((Ticket.id == id) | (Ticket.reference == id)).with_for_update()
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    # Validate scope access before making edits
    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )

    # Check optimistic locking
    check_optimistic_locking(ticket, payload.version_index, request)

    # Check transition validity
    current_status = ticket.status
    target_status = payload.status

    if current_status != target_status:
        if target_status not in TICKET_TRANSITIONS.get(current_status, set()):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid transition from {current_status} to {target_status}.",
            )

    # Record details for auditing before we modify the model
    actor_roles = ", ".join(principal.roles)
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{current_status.value if hasattr(current_status, 'value') else current_status}', "
        f"Target State: '{target_status.value if hasattr(target_status, 'value') else target_status}'. "
        f"Assignment Changes: 'No assignment changes'. "
        f"Reason: {change_reason}."
    )

    # Apply transition
    ticket.status = target_status
    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id

    await session.flush()

    # Log transition in TicketAuditLog
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

    return map_ticket_to_response(ticket)


@app.post("/api/v1/tickets/{id}/assign", response_model=TicketResponse)
async def assign_ticket(
    request: Request,
    id: str,
    payload: TicketAssignPayload,
    principal: Principal = Depends(get_principal),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Assign a support ticket to an individual user and/or role-based routing target explicitly.
    """
    user_id = principal.user_id
    change_reason = principal.change_reason

    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = select(Ticket).where((Ticket.id == id) | (Ticket.reference == id)).with_for_update()
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(
            status_code=404, detail=f"Ticket with ID/reference '{id}' not found."
        )

    # Validate scope access before making edits
    if ticket.site_id and not can_access_site(principal, ticket.site_id):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Insufficient scope access for this site.",
        )

    # Check optimistic locking
    check_optimistic_locking(ticket, payload.version_index, request)

    # Reject updates to terminal tickets unless reopening
    if ticket.status in TERMINAL_STATES:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot update ticket because it is in terminal state '{ticket.status}'.",
        )

    # Record details for auditing before we modify the model
    assignment_changes = []
    if payload.assignee_user is not None and payload.assignee_user != ticket.assignee_user:
        assignment_changes.append(f"assignee_user: '{ticket.assignee_user}' -> '{payload.assignee_user}'")
    if payload.assignee_role is not None and payload.assignee_role != ticket.assignee_role:
        assignment_changes.append(f"assignee_role: '{ticket.assignee_role}' -> '{payload.assignee_role}'")

    assignment_str = "; ".join(assignment_changes) if assignment_changes else "No assignment changes"
    actor_roles = ", ".join(principal.roles)
    status_val = ticket.status.value if hasattr(ticket.status, "value") else ticket.status
    audit_details = (
        f"Actor: {user_id}, Roles: [{actor_roles}]. "
        f"Source State: '{status_val}', Target State: '{status_val}'. "
        f"Assignment Changes: '{assignment_str}'. "
        f"Reason: {change_reason}."
    )

    # Apply assignment
    if payload.assignee_user is not None:
        ticket.assignee_user = payload.assignee_user
    if payload.assignee_role is not None:
        ticket.assignee_role = payload.assignee_role

    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id

    await session.flush()

    # Log assignment in TicketAuditLog
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

    return map_ticket_to_response(ticket)


# Ticket Comment Endpoints
@app.post(
    "/api/v1/tickets/{id}/comments", response_model=CommentResponse, status_code=201
)
async def create_ticket_comment(
    request: Request,
    id: str,
    payload: CommentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CommentResponse:
    """
    Append an auditable comment/note to a specific ticket.
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    # Verify ticket exists
    ticket_stmt = select(Ticket).where(Ticket.id == id)
    ticket_res = await session.execute(ticket_stmt)
    ticket = ticket_res.scalars().first()
    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    comment = TicketComment(
        ticket_id=id,
        body=payload.body,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(comment)
    await session.flush()

    # Log comment creation in TicketAuditLog
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

    return CommentResponse(
        id=comment.id,
        ticket_id=comment.ticket_id,
        body=comment.body,
        created_at=comment.created_at.isoformat(),
        created_by=comment.created_by,
        reason_for_change=comment.reason_for_change,
        version_index=comment.version_index,
    )


@app.get("/api/v1/tickets/{id}/comments", response_model=List[CommentResponse])
async def list_ticket_comments(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[CommentResponse]:
    """
    Retrieve all comments for a specific ticket in ascending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Verify ticket exists
    ticket_stmt = select(Ticket).where(Ticket.id == id)
    ticket_res = await session.execute(ticket_stmt)
    ticket = ticket_res.scalars().first()
    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    stmt = (
        select(TicketComment)
        .where(TicketComment.ticket_id == id)
        .order_by(TicketComment.created_at.asc())
    )
    result = await session.execute(stmt)
    comments = result.scalars().all()

    # Log view action
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
