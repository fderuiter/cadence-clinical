"""
Infrastructure repository implementations for Tickets service.
"""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.tickets.domain.ports import TicketRepositoryPort
from apps.tickets.infrastructure.models import Ticket, TicketAuditLog
from packages.database import map_database_exceptions

TICKET_ESCALATE = "TICKET_ESCALATE"


async def write_ticket_audit_log(
    session: AsyncSession,
    user_id: str,
    action: str,
    details: str,
    record_id: str | None = None,
    ticket_id: str | None = None,
    change_reason: str | None = None,
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


class TicketRepository(TicketRepositoryPort):
    """
    SQLAlchemy repository implementation for Ticket persistence.
    Subclasses apps.tickets.domain.ports.TicketRepositoryPort.
    """

    def __init__(self, session: AsyncSession):
        self.session = session

    @map_database_exceptions
    async def get_by_id(self, entity_id: str) -> Ticket | None:
        """Fetch ticket by ID."""
        stmt = select(Ticket).where(Ticket.id == entity_id)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_by_reference(self, reference: str) -> Ticket | None:
        """Fetch ticket by reference."""
        stmt = select(Ticket).where(Ticket.reference == reference)
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def get_by_id_or_ref(self, ticket_id_or_ref: str) -> Ticket | None:
        """Fetch ticket by ID or reference."""
        stmt = select(Ticket).where(
            (Ticket.id == ticket_id_or_ref) | (Ticket.reference == ticket_id_or_ref)
        )
        result = await self.session.execute(stmt)
        return result.scalars().first()

    @map_database_exceptions
    async def save(self, entity: Ticket) -> Ticket:
        """Save or update ticket entity."""
        self.session.add(entity)
        await self.session.flush()
        return entity
