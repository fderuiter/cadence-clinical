import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import (
    JSON,
    DateTime,
    Float,
    ForeignKey,
    Integer,
    String,
    event,
    func,
)
from sqlalchemy.ext.asyncio import AsyncSession
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
    case_data: Mapped[dict[str, Any]] = mapped_column(JSON, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


SafetyCase = SafetyCaseICSR
ICSR = SafetyCaseICSR


class ExportJob(Base):
    """
    Represents a persisted export job record for ICSR XML exports, mirroring TranslationJob.
    """

    __tablename__ = "safety_export_jobs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    job_name: Mapped[str] = mapped_column(String(255), nullable=False)
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    output: Mapped[str | None] = mapped_column(String, nullable=True)
    error: Mapped[str | None] = mapped_column(String, nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    @property
    def error_message(self) -> str | None:
        return self.error

    @error_message.setter
    def error_message(self, val: str | None) -> None:
        self.error = val


SafetyExportJob = ExportJob


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

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    discrepancies: Mapped[list[SAEDiscrepancy]] = relationship(
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
    source: Mapped[str] = mapped_column(String(100), nullable=False)
    case_event_key: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    field_name: Mapped[str] = mapped_column(String(100), nullable=False)
    expected_value: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    actual_value: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    meddra_version: Mapped[str | None] = mapped_column(String(50), nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    run: Mapped[SAEReconciliationRun] = relationship(
        "SAEReconciliationRun", back_populates="discrepancies"
    )


class SAEReconciliationJob(Base):
    """
    Represents a tracked background job for SAE reconciliation.
    """

    __tablename__ = "sae_reconciliation_jobs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    status: Mapped[str] = mapped_column(String(50), default="PENDING", nullable=False)
    run_id: Mapped[str | None] = mapped_column(
        String(36), ForeignKey("sae_reconciliation_runs.id"), nullable=True, index=True
    )
    error: Mapped[str | None] = mapped_column(String, nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    run: Mapped[SAEReconciliationRun | None] = relationship("SAEReconciliationRun")

    @property
    def error_message(self) -> str | None:
        return self.error

    @error_message.setter
    def error_message(self, val: str | None) -> None:
        self.error = val


class SafetyNarrative(Base):
    """
    Represents an AI-generated or human-approved Serious Adverse Event (SAE) safety narrative
    with 21 CFR Part 11 dual-attribution audit and electronic signature gating.

    Requirements: PRD-SYS-052
    """

    __tablename__ = "safety_narratives"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    case_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    sae_event_key: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    title: Mapped[str] = mapped_column(String(500), nullable=False)
    sections: Mapped[list[dict[str, Any]]] = mapped_column(JSON, nullable=False)
    raw_narrative_text: Mapped[str] = mapped_column(String, nullable=False)
    timeline_events: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )
    grounded_claims: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )

    # 21 CFR Part 11 Dual-Attribution AI fields (AIAssistedRecordMixin)
    model_identifier: Mapped[str] = mapped_column(String(255), nullable=False)
    prompt_hash: Mapped[str] = mapped_column(String(64), nullable=False)
    confidence_score: Mapped[float] = mapped_column(Float, nullable=False, default=1.0)
    review_status: Mapped[str] = mapped_column(
        String(50), nullable=False, default="DRAFT_AI", index=True
    )
    approved_by_user_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    esignature_manifest_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    # Standard Part 11 Audit fields (Part11AuditMixin)
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


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
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    action: Mapped[str] = mapped_column(String(100), nullable=False)
    details: Mapped[str] = mapped_column(String(1000), nullable=False)
    record_id: Mapped[str | None] = mapped_column(String(255), nullable=True)


@event.listens_for(Session, "before_flush")
def prevent_audit_log_modification(session: Session, flush_context, instances) -> None:
    for obj in session.dirty:
        if isinstance(obj, SafetyAuditLog):
            raise ValueError(
                "Updates to SafetyAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )

    for obj in session.deleted:
        if isinstance(obj, SafetyAuditLog):
            raise ValueError(
                "Deletions from SafetyAuditLog are strictly forbidden to comply with 21 CFR Part 11."
            )


async def write_audit_log(
    session: AsyncSession,
    created_by: str,
    action: str,
    details: str,
    reason_for_change: str | None = None,
    version_index: int = 1,
    record_id: str | None = None,
) -> None:
    log_entry = SafetyAuditLog(
        created_by=created_by,
        action=action,
        details=details,
        record_id=record_id,
        reason_for_change=reason_for_change,
        version_index=version_index,
    )
    session.add(log_entry)
    await session.flush()
