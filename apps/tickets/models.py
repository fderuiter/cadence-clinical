"""
SQLAlchemy models for the Tickets service.
"""

import uuid
from datetime import datetime
from enum import Enum
from typing import Optional

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, event, func
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, relationship


class Base(DeclarativeBase):
    """
    Base class for SQLAlchemy models in the Tickets service.
    """

    pass


class TicketCategory(str, Enum):
    """
    Enum representing categories of support tickets.
    """

    TECHNICAL = "TECHNICAL"
    CLINICAL = "CLINICAL"
    HARDWARE = "HARDWARE"
    ACCESS = "ACCESS"
    OTHER = "OTHER"


class TicketPriority(str, Enum):
    """
    Enum representing priority levels of support tickets.
    """

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class TicketStatus(str, Enum):
    """
    Enum representing statuses of support tickets.
    """

    OPEN = "OPEN"
    IN_PROGRESS = "IN_PROGRESS"
    RESOLVED = "RESOLVED"
    CLOSED = "CLOSED"
    REOPENED = "REOPENED"
    CANCELLED = "CANCELLED"


# Valid state transitions for TicketStatus
TICKET_TRANSITIONS = {
    TicketStatus.OPEN: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.CLOSED,
    },
    TicketStatus.IN_PROGRESS: {
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
        TicketStatus.OPEN,
    },
    TicketStatus.RESOLVED: {
        TicketStatus.CLOSED,
        TicketStatus.REOPENED,
        TicketStatus.IN_PROGRESS,
    },
    TicketStatus.CLOSED: {TicketStatus.REOPENED},
    TicketStatus.REOPENED: {
        TicketStatus.IN_PROGRESS,
        TicketStatus.RESOLVED,
        TicketStatus.CANCELLED,
    },
    TicketStatus.CANCELLED: {TicketStatus.REOPENED},
}

# Explicit lifecycle rule categories
TERMINAL_STATES = {TicketStatus.CLOSED, TicketStatus.CANCELLED}
CANCELLABLE_STATES = {
    TicketStatus.OPEN,
    TicketStatus.IN_PROGRESS,
    TicketStatus.RESOLVED,
    TicketStatus.REOPENED,
}
REOPENABLE_STATES = {TicketStatus.CLOSED, TicketStatus.CANCELLED}


class Ticket(Base):
    """
    Represents a ticket in the platform with 21 CFR Part 11 compliance.
    """

    __tablename__ = "tickets"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    reference: Mapped[str] = mapped_column(
        String(50), unique=True, index=True, nullable=False
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String, nullable=False)
    category: Mapped[TicketCategory] = mapped_column(
        String(50), default=TicketCategory.OTHER, nullable=False
    )
    priority: Mapped[TicketPriority] = mapped_column(
        String(50), default=TicketPriority.LOW, nullable=False
    )
    status: Mapped[TicketStatus] = mapped_column(
        String(50), default=TicketStatus.OPEN, nullable=False
    )
    reporter: Mapped[str] = mapped_column(String(255), nullable=False)
    assignee_user: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)
    assignee_role: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    # Organization / Site / Study scope
    org_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    site_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )
    study_id: Mapped[Optional[str]] = mapped_column(
        String(255), nullable=True, index=True
    )

    # Related entity link details
    related_entity_type: Mapped[Optional[str]] = mapped_column(
        String(100), nullable=True
    )
    related_entity_id: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    # Optional due date
    due_date: Mapped[Optional[datetime]] = mapped_column(DateTime, nullable=True)

    workflow_type: Mapped[Optional[str]] = mapped_column(String(100), nullable=True)
    action_type: Mapped[Optional[str]] = mapped_column(String(100), nullable=True)
    signature_action: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    # Soft delete
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    comments: Mapped[list["TicketComment"]] = relationship(
        back_populates="ticket", cascade="all, delete-orphan"
    )
    audit_logs: Mapped[list["TicketAuditLog"]] = relationship(back_populates="ticket")


class TicketComment(Base):
    """
    Represents an auditable comment or note appended to a ticket.
    """

    __tablename__ = "ticket_comments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    ticket_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("tickets.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    body: Mapped[str] = mapped_column(String, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata for Comments
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    ticket: Mapped["Ticket"] = relationship(back_populates="comments")


class TicketAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Ticket records.
    """

    __tablename__ = "ticket_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    ticket_id: Mapped[Optional[str]] = mapped_column(
        String(36),
        ForeignKey("tickets.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    reason_for_change: Mapped[Optional[str]] = mapped_column(
        String(1000), nullable=True
    )
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    action: Mapped[str] = mapped_column(String(100), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    record_id: Mapped[Optional[str]] = mapped_column(String(255), nullable=True)

    # Relationships
    ticket: Mapped[Optional["Ticket"]] = relationship(back_populates="audit_logs")


@event.listens_for(Session, "before_flush")
def prevent_audit_log_modification(session: Session, flush_context, instances) -> None:
    """
    Ensures that TicketAuditLog records can never be updated or deleted.
    """
    for obj in session.dirty:
        if isinstance(obj, TicketAuditLog):
            raise ValueError(
                "Updates to TicketAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )

    for obj in session.deleted:
        if isinstance(obj, TicketAuditLog):
            raise ValueError(
                "Deletions from TicketAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )
