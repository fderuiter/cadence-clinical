"""
SQLAlchemy models for the Tickets service.
"""

from apps.tickets.infrastructure.models import (
    CANCELLABLE_STATES,
    REOPENABLE_STATES,
    TERMINAL_STATES,
    TICKET_TRANSITIONS,
    Base,
    Ticket,
    TicketAuditLog,
    TicketCategory,
    TicketComment,
    TicketPriority,
    TicketStatus,
    prevent_audit_log_modification,
)

__all__ = [
    "CANCELLABLE_STATES",
    "REOPENABLE_STATES",
    "TERMINAL_STATES",
    "TICKET_TRANSITIONS",
    "Base",
    "Ticket",
    "TicketAuditLog",
    "TicketCategory",
    "TicketComment",
    "TicketPriority",
    "TicketStatus",
    "prevent_audit_log_modification",
]
