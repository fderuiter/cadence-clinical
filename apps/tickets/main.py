"""
FastAPI application for the Tickets microservice.
"""

import asyncio
import os
from datetime import datetime
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Request
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
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware


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


# Tickets Endpoints
@app.post("/api/v1/tickets", response_model=TicketResponse, status_code=201)
async def create_ticket(
    request: Request,
    payload: TicketCreate,
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Create and persist a new Ticket record.
    """
    user_id, user_role, change_reason = get_user_context(request)
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
            reporter=payload.reporter or user_id,
            assignee_user=payload.assignee_user,
            assignee_role=payload.assignee_role,
            org_id=payload.org_id,
            site_id=payload.site_id,
            study_id=payload.study_id,
            related_entity_type=payload.related_entity_type,
            related_entity_id=payload.related_entity_id,
            due_date=payload.due_date,
            created_by=user_id,
            reason_for_change=change_reason,
            version_index=1,
        )
        session.add(ticket)
        await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
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
    status: Optional[str] = None,
    include_deleted: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> List[TicketResponse]:
    """
    List all tickets, optionally filtered by status.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Ticket)
    if not include_deleted:
        stmt = stmt.where(Ticket.is_deleted.is_(False))
    if status:
        stmt = stmt.where(Ticket.status == status)

    result = await session.execute(stmt)
    tickets = result.scalars().all()

    # Log listing action
    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_LIST",
        details=f"Listed tickets (status filter: {status}, include_deleted: {include_deleted}).",
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
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Retrieve a specific ticket record by its ID.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Ticket).where(Ticket.id == id)
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_VIEW",
        details=f"Viewed ticket ID: {id}.",
        record_id=id,
        ticket_id=id,
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    return map_ticket_to_response(ticket)


@app.put("/api/v1/tickets/{id}", response_model=TicketResponse)
async def update_ticket(
    request: Request,
    id: str,
    payload: TicketUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> TicketResponse:
    """
    Update a specific ticket record by its ID.
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt = select(Ticket).where(Ticket.id == id)
    result = await session.execute(stmt)
    ticket = result.scalars().first()

    if not ticket:
        raise HTTPException(status_code=404, detail=f"Ticket with ID '{id}' not found.")

    # Apply updates
    if payload.title is not None:
        ticket.title = payload.title
    if payload.description is not None:
        ticket.description = payload.description
    if payload.category is not None:
        ticket.category = payload.category
    if payload.priority is not None:
        ticket.priority = payload.priority
    if payload.status is not None:
        ticket.status = payload.status
    if payload.assignee_user is not None:
        ticket.assignee_user = payload.assignee_user
    if payload.assignee_role is not None:
        ticket.assignee_role = payload.assignee_role
    if payload.org_id is not None:
        ticket.org_id = payload.org_id
    if payload.site_id is not None:
        ticket.site_id = payload.site_id
    if payload.study_id is not None:
        ticket.study_id = payload.study_id
    if payload.related_entity_type is not None:
        ticket.related_entity_type = payload.related_entity_type
    if payload.related_entity_id is not None:
        ticket.related_entity_id = payload.related_entity_id
    if payload.due_date is not None:
        ticket.due_date = payload.due_date
    if payload.is_deleted is not None:
        ticket.is_deleted = payload.is_deleted

    ticket.version_index += 1
    ticket.reason_for_change = change_reason
    ticket.created_by = user_id  # update editor context for current version

    await session.flush()

    await write_ticket_audit_log(
        session=session,
        user_id=user_id,
        action="TICKET_UPDATE",
        details=f"Updated ticket ID: {id}. Version index incremented to {ticket.version_index}.",
        record_id=id,
        ticket_id=id,
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
