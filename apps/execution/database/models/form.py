import enum

from sqlalchemy import JSON, Boolean, Index, String
from sqlalchemy.orm import Mapped, mapped_column, synonym

from .audit import AuditedModel


class FormSubmissionStatus(enum.StrEnum):
    DRAFT = "DRAFT"
    COMPLETED = "COMPLETED"
    APPROVED = "APPROVED"


class FormSubmission(AuditedModel):
    """Represents a CRF form submission as the auditable unit of PI sign-off.

    Inherits from AuditedModel to maintain an immutable audit log and participate in
    existing versioning, audit-ledger, Merkle-sealing, and locking conventions.
    """

    __tablename__ = "form_submissions"
    __table_args__ = (
        Index(
            "idx_form_submissions_coords",
            "study_id",
            "subject_id",
            "visit_id",
            "form_id",
        ),
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    visit_id: Mapped[str] = mapped_column(String(255), nullable=True)
    form_id: Mapped[str] = mapped_column(String(255), nullable=False)
    form_key = synonym("form_id")
    status: Mapped[str] = mapped_column(
        String(50),
        default="DRAFT",
        nullable=False,
        comment="DRAFT, COMPLETED, APPROVED",
    )  # DRAFT, COMPLETED, APPROVED
    signature_manifest: Mapped[dict] = mapped_column(JSON, nullable=True)

    protocol_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    payload: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    is_readonly: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    cloned_from_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
