"""
SQLAlchemy models for the Tickets service.
"""

import uuid
from datetime import datetime
from typing import Optional

from sqlalchemy import Boolean, DateTime, Integer, String, event, func
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column


class Base(DeclarativeBase):
    """
    Base class for SQLAlchemy models in the Tickets service.
    """

    pass


class Ticket(Base):
    """
    Represents a ticket in the platform with 21 CFR Part 11 compliance.
    """

    __tablename__ = "tickets"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="OPEN", nullable=False
    )  # OPEN, IN_PROGRESS, RESOLVED, CLOSED
    priority: Mapped[str] = mapped_column(
        String(50), default="LOW", nullable=False
    )  # LOW, MEDIUM, HIGH, CRITICAL
    is_deleted: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class TicketAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Ticket records.
    """

    __tablename__ = "ticket_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
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
