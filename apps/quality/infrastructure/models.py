import uuid
from datetime import datetime

from sqlalchemy import (
    JSON,
    Boolean,
    DateTime,
    Float,
    ForeignKey,
    Integer,
    String,
    func,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship

from apps.quality.domain.models import (
    ActionItemStatus,
    AuditStatus,
    AuditType,
    BreachStatus,
    CAPAStatus,
    CAPAType,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    EffectivenessOutcome,
    FindingSeverity,
    RCAMethodology,
    RiskCategory,
    RiskTier,
)


class Base(DeclarativeBase):
    pass


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
    category: Mapped[str | None] = mapped_column(
        String(100), default=DeviationType.PROTOCOL_PROCEDURE, nullable=True
    )
    is_protocol_violation: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    impact_safety: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    impact_data: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    impact_compliance: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    source_system: Mapped[str] = mapped_column(
        String(50), default="MANUAL", nullable=False
    )
    source_reference_id: Mapped[str | None] = mapped_column(String(255), nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    root_cause_analysis: Mapped[RootCauseAnalysis | None] = relationship(
        back_populates="deviation",
        uselist=False,
        cascade="all, delete-orphan",
        lazy="selectin",
    )
    capa_records: Mapped[list[CAPARecord]] = relationship(
        back_populates="deviation", cascade="all, delete-orphan", lazy="selectin"
    )


class RootCauseAnalysis(Base):
    """
    Represents a Root Cause Analysis (RCA) linked to a specific deviation.
    Supports both 5-Whys causal hierarchy and 6M Fishbone structures.
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
    methodology: Mapped[str] = mapped_column(
        String(255), default=RCAMethodology.FIVE_WHYS, nullable=False
    )
    investigation_details: Mapped[str] = mapped_column(String, nullable=False)
    root_cause_summary: Mapped[str] = mapped_column(String, nullable=False)
    five_whys_chain: Mapped[dict | list | None] = mapped_column(JSON, nullable=True)
    fishbone_categories: Mapped[dict | None] = mapped_column(JSON, nullable=True)
    contributing_factors: Mapped[list | None] = mapped_column(JSON, nullable=True)

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
    Enforces a strict 6-stage gate lifecycle with sub-actions and scheduled effectiveness verification.
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
    capa_type: Mapped[str] = mapped_column(
        String(50), default=CAPAType.BOTH, nullable=False
    )
    action_plan: Mapped[str] = mapped_column(String, nullable=False)
    status: Mapped[CAPAStatus] = mapped_column(
        String(50), default=CAPAStatus.INITIATED, nullable=False
    )
    preventive_measures: Mapped[str | None] = mapped_column(String, nullable=True)
    risk_level: Mapped[str] = mapped_column(
        String(50), default="MEDIUM", nullable=False
    )
    lead_investigator_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    qa_approver_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    target_completion_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    actual_completion_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    effectiveness_interval_days: Mapped[int] = mapped_column(
        Integer, default=30, nullable=False
    )
    effectiveness_review_date: Mapped[datetime | None] = mapped_column(
        DateTime, nullable=True
    )
    effectiveness_outcome: Mapped[str] = mapped_column(
        String(50), default=EffectivenessOutcome.PENDING, nullable=False
    )
    recurrence_detected: Mapped[bool] = mapped_column(
        Boolean, default=False, nullable=False
    )
    audit_finding_id: Mapped[str | None] = mapped_column(
        String(36), nullable=True, index=True
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
    action_items: Mapped[list[CAPAActionItem]] = relationship(
        back_populates="capa", cascade="all, delete-orphan", lazy="selectin"
    )
    effectiveness_checks: Mapped[list[CAPAEffectivenessCheck]] = relationship(
        back_populates="capa", cascade="all, delete-orphan", lazy="selectin"
    )


class CAPAActionItem(Base):
    """
    Sub-task action item decomposed from a CAPA plan (Corrective or Preventive action).
    """

    __tablename__ = "quality_capa_action_items"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    capa_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_capa_records.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    description: Mapped[str] = mapped_column(String, nullable=False)
    action_type: Mapped[str] = mapped_column(
        String(50), default="CORRECTIVE", nullable=False
    )
    assigned_to: Mapped[str] = mapped_column(String(255), nullable=False)
    due_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default=ActionItemStatus.OPEN, nullable=False
    )
    completed_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    evidence_url: Mapped[str | None] = mapped_column(String(1000), nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    capa: Mapped[CAPARecord] = relationship(back_populates="action_items")


class CAPAEffectivenessCheck(Base):
    """
    Scheduled post-implementation effectiveness evaluation for a CAPA.
    """

    __tablename__ = "quality_capa_effectiveness_checks"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    capa_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_capa_records.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    planned_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    executed_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    metric_evaluated: Mapped[str] = mapped_column(String(255), nullable=False)
    baseline_value: Mapped[str] = mapped_column(String(255), nullable=False)
    target_value: Mapped[str] = mapped_column(String(255), nullable=False)
    actual_value: Mapped[str | None] = mapped_column(String(255), nullable=True)
    outcome: Mapped[str] = mapped_column(
        String(50), default=EffectivenessOutcome.PENDING, nullable=False
    )
    evaluator_id: Mapped[str | None] = mapped_column(String(255), nullable=True)
    comments: Mapped[str | None] = mapped_column(String, nullable=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    capa: Mapped[CAPARecord] = relationship(back_populates="effectiveness_checks")


# --- RBQM / Risk-Based Quality Management Models ---


class CtQFactor(Base):
    """
    Critical to Quality (CtQ) factor identifying critical processes & data under ICH E8(R1)/E6(R3).
    """

    __tablename__ = "quality_ctq_factors"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    category: Mapped[str] = mapped_column(
        String(100), default=RiskCategory.PATIENT_SAFETY, nullable=False
    )
    critical_aspect: Mapped[str] = mapped_column(String(255), nullable=False)
    risk_description: Mapped[str] = mapped_column(String, nullable=False)
    impact_area: Mapped[str] = mapped_column(String(255), nullable=False)
    mitigation_strategy: Mapped[str] = mapped_column(String, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class KRIDefinition(Base):
    """
    Key Risk Indicator (KRI) definition with dynamic threshold boundaries and weight.
    """

    __tablename__ = "quality_kri_definitions"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    code: Mapped[str] = mapped_column(
        String(50), unique=True, nullable=False, index=True
    )
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    category: Mapped[str] = mapped_column(
        String(100), default=RiskCategory.DATA_INTEGRITY, nullable=False
    )
    description: Mapped[str] = mapped_column(String, nullable=False)
    calculation_formula: Mapped[str] = mapped_column(String(255), nullable=False)
    green_threshold: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    amber_threshold: Mapped[float] = mapped_column(Float, default=2.0, nullable=False)
    red_threshold: Mapped[float] = mapped_column(Float, default=3.0, nullable=False)
    weight: Mapped[float] = mapped_column(Float, default=1.0, nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class KRIMetricEvaluation(Base):
    """
    Site-level metric measurement and statistical Z-score evaluation for a KRI.
    """

    __tablename__ = "quality_kri_evaluations"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    kri_code: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    evaluation_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    raw_value: Mapped[float] = mapped_column(Float, nullable=False)
    standardized_z_score: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    risk_tier: Mapped[str] = mapped_column(
        String(50), default=RiskTier.LOW, nullable=False
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class SiteRiskProfile(Base):
    """
    Composite weighted Site Risk Index (SRI) profile across all KRIs and deviations.
    """

    __tablename__ = "quality_site_risk_profiles"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    evaluation_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    composite_risk_score: Mapped[float] = mapped_column(
        Float, default=0.0, nullable=False
    )
    risk_rank: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    high_risk_kri_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)
    active_deviations_count: Mapped[int] = mapped_column(
        Integer, default=0, nullable=False
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class QualityToleranceLimit(Base):
    """
    Study-level Quality Tolerance Limit (QTL) defined in protocol / quality plan.
    """

    __tablename__ = "quality_qtl_limits"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    parameter_name: Mapped[str] = mapped_column(String(255), nullable=False)
    target_value: Mapped[float] = mapped_column(Float, nullable=False)
    tolerance_limit: Mapped[float] = mapped_column(Float, nullable=False)
    unit: Mapped[str] = mapped_column(String(50), default="%", nullable=False)
    is_breached: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    breach_count: Mapped[int] = mapped_column(Integer, default=0, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


class QTLBreachEvent(Base):
    """
    Recorded event when a study-level Quality Tolerance Limit (QTL) is breached.
    """

    __tablename__ = "quality_qtl_breach_events"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    qtl_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_qtl_limits.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    breach_date: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    observed_value: Mapped[float] = mapped_column(Float, nullable=False)
    threshold_value: Mapped[float] = mapped_column(Float, nullable=False)
    root_cause: Mapped[str] = mapped_column(String, nullable=False)
    corrective_action_summary: Mapped[str] = mapped_column(String, nullable=False)
    csr_narrative: Mapped[str] = mapped_column(String, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


# --- Clinical Audits & Findings Models ---


class QualityAudit(Base):
    """
    Clinical Quality Audit engagement (Site, Vendor, Process, TMF).
    """

    __tablename__ = "quality_audits"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    audit_number: Mapped[str] = mapped_column(
        String(100), unique=True, nullable=False, index=True
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    vendor_name: Mapped[str | None] = mapped_column(String(255), nullable=True)
    audit_type: Mapped[str] = mapped_column(
        String(50), default=AuditType.SITE_AUDIT, nullable=False
    )
    lead_auditor: Mapped[str] = mapped_column(String(255), nullable=False)
    planned_start_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    planned_end_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    actual_start_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    actual_end_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default=AuditStatus.PLANNED, nullable=False
    )
    scope_summary: Mapped[str] = mapped_column(String, nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    findings: Mapped[list[AuditFinding]] = relationship(
        back_populates="audit", cascade="all, delete-orphan", lazy="selectin"
    )


class AuditFinding(Base):
    """
    Specific finding recorded during a clinical quality audit.
    """

    __tablename__ = "quality_audit_findings"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    audit_id: Mapped[str] = mapped_column(
        String(36),
        ForeignKey("quality_audits.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
    )
    finding_number: Mapped[str] = mapped_column(String(50), nullable=False)
    severity: Mapped[str] = mapped_column(
        String(50), default=FindingSeverity.MAJOR, nullable=False
    )
    category: Mapped[str] = mapped_column(String(100), nullable=False)
    condition: Mapped[str] = mapped_column(String, nullable=False)
    criteria: Mapped[str] = mapped_column(String, nullable=False)
    cause: Mapped[str] = mapped_column(String, nullable=False)
    effect: Mapped[str] = mapped_column(String, nullable=False)
    capa_id: Mapped[str | None] = mapped_column(String(36), nullable=True, index=True)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)

    audit: Mapped[QualityAudit] = relationship(back_populates="findings")


# --- Serious Breach & Regulatory Reporting Models ---


class SeriousBreachRecord(Base):
    """
    Potential or confirmed Serious Breach under GCP / regulatory frameworks (e.g. MHRA / EMA / FDA).
    """

    __tablename__ = "quality_serious_breaches"

    id: Mapped[str] = mapped_column(
        String(36), primary_key=True, default=lambda: str(uuid.uuid4())
    )
    study_id: Mapped[str] = mapped_column(String(255), nullable=False, index=True)
    site_id: Mapped[str | None] = mapped_column(String(255), nullable=True, index=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    summary: Mapped[str] = mapped_column(String, nullable=False)
    event_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    discovery_date: Mapped[datetime] = mapped_column(DateTime, nullable=False)
    confirmation_date: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    reporting_deadline: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)
    affected_authorities: Mapped[list | None] = mapped_column(JSON, nullable=True)
    status: Mapped[str] = mapped_column(
        String(50), default=BreachStatus.UNDER_EVALUATION, nullable=False
    )
    regulatory_clock_hours_remaining: Mapped[float] = mapped_column(
        Float, default=168.0, nullable=False
    )  # 7 days = 168h
    lead_qa_id: Mapped[str] = mapped_column(String(255), nullable=False)

    created_at: Mapped[datetime] = mapped_column(
        DateTime, default=func.now(), nullable=False
    )
    created_by: Mapped[str] = mapped_column(String(255), nullable=False)
    version_index: Mapped[int] = mapped_column(Integer, default=1, nullable=False)
    reason_for_change: Mapped[str] = mapped_column(String(1000), nullable=False)


# --- Audit Ledger ---


from packages.database import IntegrationOutboxMixin


class IntegrationOutbox(Base, IntegrationOutboxMixin):
    """Concrete integration outbox table for Quality service."""

    __tablename__ = "integration_outbox"


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
    merkle_hash: Mapped[str | None] = mapped_column(String(64), nullable=True)
