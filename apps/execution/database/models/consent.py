from datetime import datetime

from sqlalchemy import (
    Boolean,
    DateTime,
    Float,
    String,
    event,
    func,
    inspect,
)
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class ConsentFormRecord(AuditedModel):
    """Represents an eConsent form record bound to a specific ICF version.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "consent_form_records"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    icf_version_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    printed_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    relationship_to_subject: Mapped[str | None] = mapped_column(
        String(50), nullable=True
    )
    signature_svg: Mapped[str | None] = mapped_column(String, nullable=True)
    otp_auth_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    signed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    is_verified: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)


class ConsentSignature(AuditedModel):
    """Represents a GxP 21 CFR Part 11 compliant consent signature.

    Requirements: PRD-SYS-001
    """

    __tablename__ = "consent_signatures"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    icf_version_id: Mapped[str] = mapped_column(String(100), nullable=False)
    printed_name: Mapped[str] = mapped_column(String(255), nullable=False)
    signature_svg_data: Mapped[str | None] = mapped_column(String, nullable=True)
    signature_svg: Mapped[str | None] = mapped_column(String, nullable=True)
    otp_auth_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    meaning: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        default="I agree to participate in this research study",
    )
    cryptographic_token: Mapped[str | None] = mapped_column(String(255), nullable=True)
    verification_hash: Mapped[str | None] = mapped_column(String(255), nullable=True)
    signed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, server_default=func.now(), nullable=False
    )
    status: Mapped[str] = mapped_column(String(50), default="SIGNED", nullable=False)
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)


class ComprehensionQuizResult(AuditedModel):
    """Represents a subject's comprehension evaluation result."""

    __tablename__ = "comprehension_quiz_results"

    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    icf_version_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    score: Mapped[float] = mapped_column(
        Float, nullable=False
    )  # Wait, need Float imported? Yes! Let's check imports
    passed: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )


@event.listens_for(ConsentSignature, "before_update")
def lock_consent_signature_update(mapper, connection, target):
    raise ValueError("Cannot modify signed consent records")


@event.listens_for(ConsentSignature, "before_delete")
def lock_consent_signature_delete(mapper, connection, target):
    raise ValueError("Cannot delete consent records")


@event.listens_for(ConsentFormRecord, "before_update")
def lock_consent_form_record_update(mapper, connection, target):
    from sqlalchemy.orm.attributes import get_history

    inspect(target)
    status_history = get_history(target, "status")
    was_signed = "SIGNED" in status_history.deleted

    is_currently_signed = getattr(target, "status") == "SIGNED"
    is_transitioning_to_signed = (
        is_currently_signed and "PENDING" in status_history.deleted
    )

    if was_signed or (is_currently_signed and not is_transitioning_to_signed):
        new_status = getattr(target, "status")
        if new_status != "RECONSENT_REQUIRED":
            raise ValueError("Cannot modify signed consent records")
        # Ensure immutable fields are not modified
        for field in ("subject_id", "icf_version_id"):
            if get_history(target, field).has_changes():
                raise ValueError("Cannot modify signed consent records")


@event.listens_for(ConsentFormRecord, "before_delete")
def lock_consent_form_record_delete(mapper, connection, target):
    raise ValueError("Cannot delete consent records")
