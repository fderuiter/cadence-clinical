"""
FastAPI application for the Tickets microservice.
"""

import os
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.database import db_manager
from apps.tickets.models import Base, Ticket, TicketAuditLog
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware


class TicketCreate(BaseModel):
    """
    Pydantic schema for creating a support ticket.
    """

    title: str = Field(..., description="Title of the support ticket")
    description: str = Field(..., description="Detailed description of the issue")
    priority: str = Field("LOW", description="Priority level of the ticket")


class TicketUpdate(BaseModel):
    """
    Pydantic schema for updating an existing support ticket.
    """

    title: Optional[str] = Field(None, description="Updated title")
    description: Optional[str] = Field(None, description="Updated description")
    status: Optional[str] = Field(None, description="Updated status")
    priority: Optional[str] = Field(None, description="Updated priority")
    is_deleted: Optional[bool] = Field(None, description="Soft delete state")


class TicketResponse(BaseModel):
    """
    Pydantic schema for returning support ticket details.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    title: str
    description: str
    status: str
    priority: str
    is_deleted: bool
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
        title=ticket.title,
        description=ticket.description,
        status=ticket.status,
        priority=ticket.priority,
        is_deleted=ticket.is_deleted,
        created_at=ticket.created_at.isoformat(),
        created_by=ticket.created_by,
        reason_for_change=ticket.reason_for_change,
        version_index=ticket.version_index,
    )


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

    ticket = Ticket(
        title=payload.title,
        description=payload.description,
        priority=payload.priority,
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
        details=f"Created ticket '{payload.title}' with priority '{payload.priority}'.",
        record_id=ticket.id,
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
        change_reason=change_reason,
        version_index=1,
    )

    stmt = select(TicketAuditLog).order_by(TicketAuditLog.created_at.desc())
    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
        TicketAuditLogResponse(
            id=log.id,
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
    if payload.status is not None:
        ticket.status = payload.status
    if payload.priority is not None:
        ticket.priority = payload.priority
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
        change_reason=change_reason,
        version_index=ticket.version_index,
    )

    return map_ticket_to_response(ticket)
