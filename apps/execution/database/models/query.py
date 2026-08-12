import enum
from datetime import datetime

from sqlalchemy import DateTime, Index, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class QueryStatus(enum.StrEnum):
    NONE = "NONE"
    CANDIDATE = "CANDIDATE"
    OPEN = "OPEN"
    ANSWERED = "ANSWERED"
    CLOSED = "CLOSED"
    REOPENED = "REOPENED"
    CANCELLED = "CANCELLED"


class ClinicalQuery(AuditedModel):
    """Represents a clinical query state record for GxP data discrepancy tracking.

    Inherits from AuditedModel to maintain an immutable audit log of status changes and history,
    and prevent hard deletions through automatic trigger-based protection.
    """

    __tablename__ = "clinical_queries"
    __table_args__ = (
        Index(
            "idx_query_target",
            "study_id",
            "subject_id",
            "visit_id",
            "domain",
            "test_code",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), index=True, nullable=False)
    visit_id: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    domain: Mapped[str] = mapped_column(String(50), index=True, nullable=True)
    test_code: Mapped[str] = mapped_column(String(100), index=True, nullable=False)

    status: Mapped[str] = mapped_column(String(50), default="NONE", nullable=False)
    explanation: Mapped[str] = mapped_column(String(255), nullable=True)
    response: Mapped[str] = mapped_column(String(255), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=func.now())
    updated_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), onupdate=func.now()
    )

    observation_id: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    field_link: Mapped[str] = mapped_column(String(255), index=True, nullable=True)
    message: Mapped[str] = mapped_column(String(1000), nullable=True)
    origin: Mapped[str] = mapped_column(String(50), nullable=True)
    priority: Mapped[str] = mapped_column(String(50), nullable=True)
    rule_id: Mapped[str] = mapped_column(String(255), nullable=True)
    created_by: Mapped[str] = mapped_column(String(255), nullable=True)
    responder: Mapped[str] = mapped_column(String(255), nullable=True)
    resolver: Mapped[str] = mapped_column(String(255), nullable=True)
    resolved_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    cancellation_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    escalated_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)

    form_id: Mapped[str] = mapped_column(String(255), nullable=True)
    field_id: Mapped[str] = mapped_column(String(255), nullable=True)
    query_type: Mapped[str] = mapped_column(String(255), nullable=True)
    action_required: Mapped[str] = mapped_column(String(255), nullable=True)
