import enum
from datetime import datetime

from sqlalchemy import JSON, Boolean, DateTime, Float, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from .audit import AuditedModel


class SDVStatus(enum.StrEnum):
    PENDING = "PENDING"
    VERIFIED = "VERIFIED"
    FLAGGED = "FLAGGED"
    RESOLVED = "RESOLVED"
    DROPPED = "DROPPED"


class SDVSignOff(AuditedModel):
    """Represents an aggregate sign-off record for SDV/TSDV verification.

    Maintains page-, visit- or field-level verification signatures and drop states
    for auditing and GxP traceability.
    """

    __tablename__ = "sdv_sign_offs"

    scope: Mapped[str] = mapped_column(
        String(50), nullable=False, comment="FIELD, PAGE, or VISIT"
    )  # FIELD, PAGE, or VISIT
    target_id: Mapped[str] = mapped_column(String(255), nullable=False)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    is_verified: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    verified_by: Mapped[str] = mapped_column(String(255), nullable=True)
    verified_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)
    dropped_reason: Mapped[str] = mapped_column(String(1000), nullable=True)
    dropped_at: Mapped[datetime] = mapped_column(DateTime, nullable=True)

    # Phase 25: state machine status column
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    # Phase 25: flag lifecycle columns
    flagged_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    flagged_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    flag_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    flag_severity: Mapped[str | None] = mapped_column(String(50), nullable=True)
    resolved_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # GxP 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime | None] = mapped_column(
        DateTime, server_default=func.now(), nullable=True
    )
    created_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class TSDVConfig(AuditedModel):
    """Represents the Targeted SDV (TSDV) sampling configuration for a study.

    Maintains study-specific parameters governing subject-based or field-based
    sampling models and domain-level SDV requirements.
    """

    __tablename__ = "tsdv_configs"

    study_id: Mapped[str] = mapped_column(String(255), unique=True, nullable=False)
    sampling_model: Mapped[str] = mapped_column(
        String(50), nullable=False
    )  # SUBJECT_BASED, FIELD_BASED, or COMBINED
    initial_full_sdv_subject_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    random_sample_percentage: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    full_sdv_domains: Mapped[list] = mapped_column(
        JSON, nullable=True
    )  # Wait, need JSON imported? Yes!
    safety_endpoints: Mapped[list] = mapped_column(JSON, nullable=True)
    zero_sdv_domains: Mapped[list] = mapped_column(JSON, nullable=True)
    trial_random_seed: Mapped[int] = mapped_column(Integer, nullable=True)
