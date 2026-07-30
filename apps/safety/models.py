import uuid
from datetime import datetime
from typing import Any, Dict, Optional

from sqlalchemy import JSON, DateTime, ForeignKey, Integer, String, event, func
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class SafetyCaseICSR(Base):
    """
    Represents a persisted safety case / ICSR (Individual Case Safety Report)
    with 21 CFR Part 11 compliant audit and versioning fields.
    """

    __tablename__ = "safety_cases"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    worldwide_unique_case_id: Mapped[str] = mapped_column(
        String(255), nullable=False, index=True
    )
    patient_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    case_data: Mapped[Dict[str, Any]] = mapped_column(JSON, nullable=False)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SafetyExportJob(Base):
    """
    Represents a persisted export job record for ICSR XML exports.
    """

    __tablename__ = "safety_export_jobs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    job_name: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False
    )  # PENDING, COMPLETED, FAILED
    error_message: Mapped[Optional[str]] = mapped_column(String, nullable=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class SAEReconciliationRun(Base):
    """
    Represents an auditable safety/SAE reconciliation execution run.
    """

    __tablename__ = "sae_reconciliation_runs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    run_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    discrepancies: Mapped[list["SAEDiscrepancy"]] = relationship(
        "SAEDiscrepancy", back_populates="run", cascade="all, delete-orphan"
    )


class SAEDiscrepancy(Base):
    """
    Represents a specific discrepancy identified during an SAE reconciliation run.
    Carries source, stable key, field name, expected and actual values, and MedDRA details.
    """

    __tablename__ = "sae_discrepancies"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    run_id: Mapped[str] = mapped_column(
        String(36), ForeignKey("sae_reconciliation_runs.id"), nullable=False, index=True
    )
    source: Mapped[str] = mapped_column(
        String(100), nullable=False
    )  # EDC, SAFETY, or RECONCILIATION
    case_event_key: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    field_name: Mapped[str] = mapped_column(String(100), nullable=False)
    expected_value: Mapped[Optional[str]] = mapped_column(String(1000), nullable=True)
    actual_value: Mapped[Optional[str]] = mapped_column(String(1000), nullable=True)
    meddra_version: Mapped[Optional[str]] = mapped_column(String(50), nullable=True)

    # 21 CFR Part 11 Compliance Auditing Metadata
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    run: Mapped["SAEReconciliationRun"] = relationship(
        "SAEReconciliationRun", back_populates="discrepancies"
    )


class SafetyAuditLog(Base):
    """
    Represents an immutable, chronological append-only audit ledger of actions performed on Safety records.
    """

    __tablename__ = "safety_audit_logs"

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
    Ensures that SafetyAuditLog records can never be updated or deleted.
    """
    # Check session.dirty for any updates to SafetyAuditLog
    for obj in session.dirty:
        if isinstance(obj, SafetyAuditLog):
            raise ValueError(
                "Updates to SafetyAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )

    # Check session.deleted for any deletions of SafetyAuditLog
    for obj in session.deleted:
        if isinstance(obj, SafetyAuditLog):
            raise ValueError(
                "Deletions from SafetyAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )
