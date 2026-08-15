import uuid
from datetime import datetime
from enum import StrEnum

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class DeviationStatus(StrEnum):
    REPORTED = "REPORTED"
    UNDER_INVESTIGATION = "UNDER_INVESTIGATION"
    RCA_COMPLETE = "RCA_COMPLETE"
    CAPA_INITIATED = "CAPA_INITIATED"
    RESOLVED = "RESOLVED"
    CLOSED = "CLOSED"


class CAPAStatus(StrEnum):
    INITIATED = "INITIATED"
    UNDER_REVIEW = "UNDER_REVIEW"
    IMPLEMENTATION = "IMPLEMENTATION"
    EFFECTIVENESS_CHECK = "EFFECTIVENESS_CHECK"
    CLOSED = "CLOSED"
    CANCELLED = "CANCELLED"


class DeviationSeverity(StrEnum):
    MINOR = "MINOR"
    MAJOR = "MAJOR"
    CRITICAL = "CRITICAL"


class DeviationType(StrEnum):
    INFORMED_CONSENT = "INFORMED_CONSENT"
    ELIGIBILITY = "ELIGIBILITY"
    PROTOCOL_PROCEDURE = "PROTOCOL_PROCEDURE"
    SAFETY_REPORTING = "SAFETY_REPORTING"
    IP_MANAGEMENT = "IP_MANAGEMENT"
    OTHER = "OTHER"


class Deviation(Base):
    """
    Represents a clinical protocol deviation or quality deviation event
    with mandatory Part 11 audit fields and lifecycle controls.
    """

    __tablename__ = "quality_deviations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String, nullable=False)
    severity: Mapped[DeviationSeverity] = mapped_column(String(50), nullable=False)
    status: Mapped[DeviationStatus] = mapped_column(
        String(50), default=DeviationStatus.REPORTED, nullable=False
    )
    type: Mapped[DeviationType] = mapped_column(String(100), nullable=False)
    is_protocol_violation: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    root_cause_analysis: Mapped[RootCauseAnalysis | None] = relationship(
        back_populates="deviation", uselist=False, cascade="all, delete-orphan"
    )
    capa_records: Mapped[list[CAPARecord]] = relationship(
        back_populates="deviation", cascade="all, delete-orphan"
    )


class RootCauseAnalysis(Base):
    """
    Represents a Root Cause Analysis (RCA) linked to a specific deviation.
    """

    __tablename__ = "quality_root_cause_analyses"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    deviation_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_deviations.id", ondelete="CASCADE"),
        nullable=False,
        unique=True,
        index=True,
    )
    methodology: Mapped[str] = mapped_column(String(255), nullable=False)
    investigation_details: Mapped[str] = mapped_column(String, nullable=False)
    root_cause_summary: Mapped[str] = mapped_column(String, nullable=False)

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    deviation: Mapped[Deviation] = relationship(back_populates="root_cause_analysis")
    capa_records: Mapped[list[CAPARecord]] = relationship(
        back_populates="rca", cascade="all, delete-orphan"
    )


class CAPARecord(Base):
    """
    Represents a Corrective and Preventive Action (CAPA) record linked to a deviation and an optional RCA.
    """

    __tablename__ = "quality_capa_records"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    deviation_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_deviations.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    rca_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("quality_root_cause_analyses.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
    )
    capa_type: Mapped[str] = mapped_column(String(50), nullable=False)
    action_plan: Mapped[str] = mapped_column(String, nullable=False)
    status: Mapped[CAPAStatus] = mapped_column(
        String(50), default=CAPAStatus.INITIATED, nullable=False
    )
    preventive_measures: Mapped[str | None] = mapped_column(String, nullable=True)
    target_completion_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )

    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    deviation: Mapped[Deviation] = relationship(back_populates="capa_records")
    rca: Mapped[RootCauseAnalysis | None] = relationship(back_populates="capa_records")


class QualityAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Quality records.
    """

    __tablename__ = "quality_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    timestamp: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    user_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    user_role: Mapped[str] = mapped_column(String(255), nullable=False)
    action: Mapped[str] = mapped_column(String(50), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    record_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    change_reason: Mapped[str | None] = mapped_column(String(1000), nullable=True)
