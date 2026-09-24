from __future__ import annotations

import uuid
from datetime import datetime
from typing import Any

from sqlalchemy import (
    JSON,
    Boolean,
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


class ICSRReportingEvaluation(Base):
    """
    Represents a deterministic multi-country expedited safety reporting evaluation record
    with real-time countdown clocks, threshold alert tracking, and 21 CFR Part 11
    Safety Physician electronic signature approval gating.

    Requirements: PRD-PVG-001, PRD-PVG-008
    """

    __tablename__ = "safety_reporting_evaluations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    case_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    receipt_date: Mapped[datetime] = mapped_column(DateTime, nullable=False, index=True)

    # Seriousness criteria
    seriousness_death: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    seriousness_life_threatening: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    seriousness_hospitalization: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    seriousness_disability: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    seriousness_congenital_anomaly: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    seriousness_other: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )

    # Causality and Expectedness
    causality: Mapped[str] = mapped_column(String(50), nullable=False)
    expectedness: Mapped[str] = mapped_column(String(50), nullable=False)

    # Deterministic conclusions
    is_serious: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_unexpected: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_causal: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    is_susar: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    any_expedited: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    shortest_timeframe_days: Mapped[int | None] = mapped_column(Integer, nullable=True)

    # Evaluated authority outcomes (JSON)
    evaluations: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )

    # 21 CFR Part 11 Electronic Signature Gating
    approval_status: Mapped[str] = mapped_column(
        String(50), nullable=False, default="DRAFT", index=True
    )
    approved_by_user_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    esignature_manifest_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    # Part 11 Audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


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


class AS2Transmission(Base):
    """
    Represents an outbound AS2 B2B Transmission record of ICH E2B(R3) XML payloads
    to regulatory health authorities with S/MIME encryption, digital signatures,
    and multi-tier ACK tracking (ACK A, ACK B, ACK C).

    Requirements: PRD-PVG-002, ADR-2199
    """

    __tablename__ = "safety_as2_transmissions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    transmission_id: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    case_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    worldwide_unique_case_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    message_id: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    as2_from: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    as2_to: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    target_authority: Mapped[str] = mapped_column(
        String(100), nullable=False, index=True
    )
    environment: Mapped[str] = mapped_column(
        String(50), default="PRODUCTION", nullable=False
    )
    status: Mapped[str] = mapped_column(
        String(50), default="PENDING", nullable=False, index=True
    )

    # Cryptographic & payload integrity verification
    payload_digest_sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    mic: Mapped[str] = mapped_column(String(255), nullable=False)
    encryption_algorithm: Mapped[str] = mapped_column(
        String(50), default="AES-256-GCM", nullable=False
    )
    signature_algorithm: Mapped[str] = mapped_column(
        String(50), default="SHA-256-RSA", nullable=False
    )
    outbound_payload_size: Mapped[int] = mapped_column(Integer, nullable=False)
    outbound_xml: Mapped[str | None] = mapped_column(String, nullable=True)

    # ACK A: Perimeter Transport / MDN Receipt
    mdn_message_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    mdn_disposition: Mapped[str | None] = mapped_column(String(255), nullable=True)
    mdn_mic: Mapped[str | None] = mapped_column(String(255), nullable=True)
    mdn_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    mdn_received_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    ack_a_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    ack_a_received_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)

    # ACK B: Syntactic / Technical Schema Validation Receipt
    ack_b_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    ack_b_received_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    ack_b_errors: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )

    # ACK C: Regulatory Health Authority Business Acceptance / DB Ingestion Receipt
    ack_c_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    ack_c_received_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    ack_c_details: Mapped[dict[str, Any]] = mapped_column(
        JSON, nullable=False, default=dict
    )

    error_message: Mapped[str | None] = mapped_column(String(2000), nullable=True)
    retry_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    # 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    audit_events: Mapped[list[AS2TransmissionAuditLog]] = relationship(
        "AS2TransmissionAuditLog",
        back_populates="transmission",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class AS2TransmissionAuditLog(Base):
    """
    Represents an immutable, append-only B2B transmission ledger event log.

    Requirements: PRD-PVG-002, 21 CFR Part 11
    """

    __tablename__ = "safety_as2_transmission_audit_logs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    transmission_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("safety_as2_transmissions.id"),
        nullable=False,
        index=True,
    )
    action: Mapped[str] = mapped_column(String(100), nullable=False, index=True)
    from_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    to_status: Mapped[str | None] = mapped_column(String(50), nullable=True)
    details: Mapped[str] = mapped_column(String(2000), nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False, index=True
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    reason_for_change: Mapped[str | None] = mapped_column(String(1000), nullable=True)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    transmission: Mapped[AS2Transmission] = relationship(
        "AS2Transmission", back_populates="audit_events"
    )


class SafetySignalDetectionRun(Base):
    """
    Represents an automated statistical safety signal detection scan run.

    Requirements: PRD-PVG-003, ADR-2199
    """

    __tablename__ = "safety_signal_detection_runs"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    target_drug: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    benchmark_dataset: Mapped[str] = mapped_column(
        String(100), nullable=False, default="INTERNAL_TRIAL_POOL"
    )
    total_cases_analyzed: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    signals_detected_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    summary_metrics: Mapped[list[dict[str, Any]]] = mapped_column(
        JSON, nullable=False, default=list
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    alerts: Mapped[list[SafetySignalAlert]] = relationship(
        "SafetySignalAlert",
        back_populates="detection_run",
        cascade="all, delete-orphan",
        lazy="selectin",
    )


class SafetySignalAlert(Base):
    """
    Represents an automated or triaged Signal Review alert triggered on statistical threshold breaches.

    Requirements: PRD-PVG-003, PRD-PVG-008, ADR-2199
    """

    __tablename__ = "safety_signal_alerts"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    detection_run_id: Mapped[str | None] = mapped_column(
        String(36),
        ForeignKey("safety_signal_detection_runs.id"),
        nullable=True,
        index=True,
    )
    study_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    target_drug: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    meddra_term: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    term_level: Mapped[str] = mapped_column(String(50), nullable=False, default="PT")
    meddra_soc: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )

    # Statistical indices
    case_count_a: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    expected_count_e: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    prr: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    prr_ci_lower: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    prr_ci_upper: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    chi_square_yates: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    ror: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    ror_ci_lower: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    ror_ci_upper: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    ebgm: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    eb05: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)
    eb95: Mapped[float] = mapped_column(Float, default=0.0, nullable=False)

    # Operational review fields
    severity: Mapped[str] = mapped_column(
        String(50), default="HIGH", nullable=False, index=True
    )
    status: Mapped[str] = mapped_column(
        String(50), default="TRIGGERED", nullable=False, index=True
    )
    triage_summary: Mapped[str] = mapped_column(String(2000), nullable=False)
    assigned_safety_officer: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    review_decision: Mapped[str | None] = mapped_column(String(100), nullable=True)
    review_justification: Mapped[str | None] = mapped_column(
        String(2000), nullable=True
    )
    clinical_summary: Mapped[str | None] = mapped_column(String(4000), nullable=True)
    reviewed_by: Mapped[str | None] = mapped_column(String(255), nullable=True)
    reviewed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    esignature_manifest_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    # 21 CFR Part 11 Audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)

    detection_run: Mapped[SafetySignalDetectionRun | None] = relationship(
        "SafetySignalDetectionRun", back_populates="alerts"
    )


class PeriodicSafetyReportRecord(Base):
    """
    Represents an aggregate periodic safety report record (ICH E2F DSUR, ICH E2C(R2) PSUR / PBRER)
    with electronic signature approval and audit trail.

    Requirements: PRD-PVG-004, PRD-PVG-008, ADR-2199
    """

    __tablename__ = "periodic_safety_reports"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    report_type: Mapped[str] = mapped_column(
        String(50), nullable=False, default="DSUR", index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    compound_name: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    period_start: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    period_end: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    status: Mapped[str] = mapped_column(
        String(50), default="DRAFT", nullable=False, index=True
    )
    total_subject_exposure: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )
    total_saes: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    report_payload: Mapped[dict[str, Any]] = mapped_column(
        JSON, nullable=False, default=dict
    )

    approved_by_user_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    approved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    esignature_manifest_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


class DeviceSafetyInvestigation(Base):
    """
    Represents an Adverse Device Effect (ADE / SADE / USADE) safety investigation
    with vigilance reporting countdown tracking, ISO 14155 / EU MDR / IVDR alignment,
    and 21 CFR Part 11 Safety Physician electronic signature adjudication.

    Requirements: PRD-SYS-126, PRD-DEV-003, PRD-DEV-004, ISO 14155:2020, ADR-2209
    """

    __tablename__ = "device_safety_investigations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    case_number: Mapped[str] = mapped_column(
        String(255), nullable=False, unique=True, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    subject_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )
    deficiency_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    device_name: Mapped[str] = mapped_column(String(255), nullable=False)
    device_udi_di: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    lot_number: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    serial_number: Mapped[str | None] = mapped_column(
        String(255), nullable=True, index=True
    )

    # Classification & Causality
    classification: Mapped[str] = mapped_column(
        String(50), default="ADE", nullable=False, index=True
    )  # ADE, SADE, USADE, DEVICE_DEFICIENCY_INCIDENT
    causality: Mapped[str] = mapped_column(
        String(50), default="POSSIBLE", nullable=False
    )
    is_usade: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # Regulatory Vigilance & Deadlines
    vigilance_reporting_required: Mapped[bool] = mapped_column(
        Boolean, default=True, nullable=False
    )
    vigilance_deadline_days: Mapped[int] = mapped_column(
        Integer, default=15, nullable=False
    )
    vigilance_due_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    notified_body_notified: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    competent_authority_notified: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )

    # Lifecycle & Corrective Actions
    status: Mapped[str] = mapped_column(
        String(50), default="OPEN", nullable=False, index=True
    )
    investigation_notes: Mapped[str | None] = mapped_column(String(4000), nullable=True)
    root_cause_summary: Mapped[str | None] = mapped_column(String(4000), nullable=True)
    capa_required: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    capa_reference: Mapped[str | None] = mapped_column(String(255), nullable=True)
    fsca_required: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)

    # 21 CFR Part 11 Electronic Signature Gating
    safety_physician_user_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )
    adjudicated_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    esignature_manifest_id: Mapped[str | None] = mapped_column(
        String(255), nullable=True
    )

    # Part 11 Audit fields
    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)


@event.listens_for(Session, "before_flush")
def prevent_audit_log_modification(session: Session, flush_context, instances) -> None:
    for obj in session.dirty:
        if isinstance(obj, (SafetyAuditLog, AS2TransmissionAuditLog)):
            raise ValueError(
                f"Updates to {type(obj).__name__} are strictly forbidden to comply with 21 CFR Part 11."
            )

    for obj in session.deleted:
        if isinstance(obj, (SafetyAuditLog, AS2TransmissionAuditLog)):
            raise ValueError(
                f"Deletions from {type(obj).__name__} are strictly forbidden to comply with 21 CFR Part 11."
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


async def write_as2_audit_log(
    session: AsyncSession,
    transmission_id: str,
    action: str,
    details: str,
    created_by: str,
    from_status: str | None = None,
    to_status: str | None = None,
    reason_for_change: str | None = None,
    version_index: int = 1,
) -> AS2TransmissionAuditLog:
    """Record an immutable append-only event into the B2B transmission ledger.

    Requirements: PRD-PVG-002, 21 CFR Part 11
    """
    event_entry = AS2TransmissionAuditLog(
        transmission_id=transmission_id,
        action=action,
        details=details,
        from_status=from_status,
        to_status=to_status,
        created_by=created_by,
        reason_for_change=reason_for_change,
        version_index=version_index,
    )
    session.add(event_entry)
    await session.flush()
    return event_entry
