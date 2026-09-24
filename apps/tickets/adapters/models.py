"""
SQLAlchemy models for the Tickets service.
"""

from __future__ import annotations

import uuid
from datetime import datetime

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, event, func
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, relationship

from apps.tickets.domain.models import (
    CANCELLABLE_STATES,
    REOPENABLE_STATES,
    SLA_PAUSED_STATES,
    TERMINAL_STATES,
    TICKET_TRANSITIONS,
    CommentVisibility,
    GxPSeverity,
    ResolutionCode,
    RootCauseCategory,
    TicketCategory,
    TicketPriority,
    TicketStatus,
)


class Base(DeclarativeBase):
    """
    Base class for SQLAlchemy models in the Tickets service.
    """

    pass


class Ticket(Base):
    """
    Represents a support or clinical operations ticket with 21 CFR Part 11 compliance.
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
    assignee_user: Mapped[str | None] = mapped_column(String(255), nullable=True)
    assignee_role: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Organization / Site / Study scope
    org_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)

    # Related entity link details
    related_entity_type: Mapped[str | None] = mapped_column(String(100), nullable=True)
    related_entity_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    context_payload: Mapped[str | None] = mapped_column(String, nullable=True)

    # Clinical GxP & RCA Classification
    gxp_severity: Mapped[str] = mapped_column(
        String(50), default=GxPSeverity.NOT_APPLICABLE.value, nullable=False
    )
    root_cause_category: Mapped[str | None] = mapped_column(String(50), nullable=True)
    root_cause_summary: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    resolution_code: Mapped[str | None] = mapped_column(String(50), nullable=True)

    # Optional due date & Clinical SLA Tracking
    due_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    sla_target_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    sla_paused_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    sla_total_paused_seconds: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    sla_breached: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    sla_amber_warned: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )

    # Escalation tracking
    last_escalated_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    last_escalation_notified_at: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    escalation_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    workflow_type: Mapped[str | None] = mapped_column(String(100), nullable=True)
    action_type: Mapped[str | None] = mapped_column(String(100), nullable=True)
    signature_action: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # 21 CFR Part 11 Cryptographic eSignature Sign-Off
    signature_token: Mapped[str | None] = mapped_column(String(512), nullable=True)
    signature_meaning: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signature_timestamp: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    signature_user: Mapped[str | None] = mapped_column(String(255), nullable=True)

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
    comments: Mapped[list[TicketComment]] = relationship(
        back_populates="ticket", cascade="all, delete-orphan"
    )
    audit_logs: Mapped[list[TicketAuditLog]] = relationship(back_populates="ticket")
    attachments: Mapped[list[TicketAttachment]] = relationship(
        back_populates="ticket", cascade="all, delete-orphan"
    )


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
    visibility: Mapped[str] = mapped_column(
        String(50), default=CommentVisibility.PUBLIC.value, nullable=False
    )

    # 21 CFR Part 11 Compliance Auditing Metadata for Comments
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    ticket: Mapped[Ticket] = relationship(back_populates="comments")


class TicketAttachment(Base):
    """
    Represents an audited file attachment or evidence document uploaded to a ticket.
    """

    __tablename__ = "ticket_attachments"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    ticket_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("tickets.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    filename: Mapped[str] = mapped_column(String(255), nullable=False)
    file_size_bytes: Mapped[int] = mapped_column(Integer, nullable=False)
    mime_type: Mapped[str] = mapped_column(
        String(100), default="application/octet-stream", nullable=False
    )
    storage_uri: Mapped[str] = mapped_column(String(500), nullable=False)
    sha256_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    uploaded_by: Mapped[str] = mapped_column(String(255), nullable=False)
    uploaded_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    deid_scrubbed: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    # Relationships
    ticket: Mapped[Ticket] = relationship(back_populates="attachments")


class TicketAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Ticket records.
    """

    __tablename__ = "ticket_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    ticket_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("tickets.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    action: Mapped[str] = mapped_column(String(100), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    record_id: Mapped[str | None] = mapped_column(String(255), nullable=True)

    # Relationships
    ticket: Mapped[Ticket | None] = relationship(back_populates="audit_logs")


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


__all__ = [
    "CANCELLABLE_STATES",
    "CommentVisibility",
    "GxPSeverity",
    "REOPENABLE_STATES",
    "ResolutionCode",
    "RootCauseCategory",
    "SLA_PAUSED_STATES",
    "TERMINAL_STATES",
    "TICKET_TRANSITIONS",
    "Base",
    "Ticket",
    "TicketAttachment",
    "TicketAuditLog",
    "TicketCategory",
    "TicketComment",
    "TicketPriority",
    "TicketStatus",
]
