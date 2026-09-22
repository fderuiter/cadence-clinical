from sqlalchemy import JSON, Boolean, Index, String
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class TranslationJob(AuditedModel):
    """Represents an asynchronous study translation job.

    Inherits from AuditedModel to maintain an immutable audit log of status changes and generated payloads.
    """

    __tablename__ = "translation_jobs"

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # PENDING, COMPLETED, FAILED
    odm_payload: Mapped[str] = mapped_column(String, nullable=True)
    openrosa_payload: Mapped[str] = mapped_column(String, nullable=True)
    error_message: Mapped[str] = mapped_column(String, nullable=True)
    warnings: Mapped[list | dict] = mapped_column(JSON, nullable=True, default=list)


class StudyAuthoredRule(AuditedModel):
    """Represents a study-level authored cross-form check rule from the published study payload."""

    __tablename__ = "study_authored_rules"
    __table_args__ = (
        Index("idx_authored_rules_study_active", "study_id", "is_active"),
        Index("idx_authored_rules_study_rule", "study_id", "rule_id"),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    rule_id: Mapped[str] = mapped_column(String(255), nullable=False)
    rule_type: Mapped[str] = mapped_column(
        String(50), default="cross_form_check", nullable=False
    )
    condition: Mapped[dict] = mapped_column(JSON, nullable=False)
    query_message: Mapped[str] = mapped_column(String(1000), nullable=False)
    message: Mapped[str] = mapped_column(String(1000), nullable=False)
    publication_version: Mapped[str] = mapped_column(String(50), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
