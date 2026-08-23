"""Domain models, enums, and DTOs for Periodic Aggregate Safety Reporting.

Requirements: PRD-PVG-004, PRD-PVG-008, PRD-AGG-001, PRD-SYS-125, ADR-2199
(ICH E2F DSUR, ICH E2C(R2) PSUR / PBRER, 21 CFR Part 11)
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class PeriodicSafetyReportType(StrEnum):
    """Supported periodic aggregate regulatory safety report formats."""

    DSUR = "DSUR"  # Development Safety Update Report (ICH E2F)
    PSUR = "PSUR"  # Periodic Safety Update Report (ICH E2C(R2))
    PBRER = "PBRER"  # Periodic Benefit-Risk Evaluation Report (ICH E2C(R2))


class PeriodicReportStatus(StrEnum):
    """Lifecycle state of a periodic regulatory safety report."""

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    APPROVED = "APPROVED"
    ARCHIVED = "ARCHIVED"


class ExposureStratificationCategory(StrEnum):
    """Categories for stratifying subject exposure metrics."""

    TREATMENT_ARM = "TREATMENT_ARM"
    DOSAGE_COHORT = "DOSAGE_COHORT"
    AGE_GROUP = "AGE_GROUP"
    SEX = "SEX"
    OVERALL = "OVERALL"


class RegulatoryExportFormat(StrEnum):
    """Supported export package formats for regulatory submissions."""

    HTML = "HTML"
    PDF = "PDF"
    XML = "XML"
    JSON = "JSON"


# =========================================================================
# Subject Exposure Domain Models (ICH E2F § 5, ICH E2C(R2) § 5)
# =========================================================================


class SubjectExposureRecord(BaseModel):
    """Raw or parsed individual subject exposure timeline record."""

    subject_id: str
    treatment_arm: str = "Investigational Drug"
    dosage_cohort: str = "Standard Dose"
    age: int | None = None
    age_group: str = "Adult (18-64)"
    sex: str = "UNKNOWN"
    first_dose_date: str  # ISO date YYYY-MM-DD
    last_dose_date: str  # ISO date YYYY-MM-DD
    cumulative_exposure_days: float = 0.0
    interval_exposure_days: float = 0.0
    cumulative_patient_years: float = 0.0
    interval_patient_years: float = 0.0


class ExposureStratificationRow(BaseModel):
    """Aggregated exposure metrics for a specific stratum."""

    category: ExposureStratificationCategory
    group_name: str
    subject_count: int = 0
    interval_patient_years: float = 0.0
    cumulative_patient_years: float = 0.0
    mean_duration_days: float = 0.0
    median_duration_days: float = 0.0


class SubjectExposureMetrics(BaseModel):
    """Comprehensive patient-years of exposure calculations."""

    total_cumulative_subjects: int = 0
    total_interval_subjects: int = 0
    total_cumulative_patient_years: float = 0.0
    total_interval_patient_years: float = 0.0
    by_treatment_arm: list[ExposureStratificationRow] = []
    by_dosage_cohort: list[ExposureStratificationRow] = []
    by_age_group: list[ExposureStratificationRow] = []
    by_sex: list[ExposureStratificationRow] = []


# =========================================================================
# Reference Safety Information (RSI) & Special Interest Models
# =========================================================================


class ReferenceSafetyInformationTerm(BaseModel):
    """Listed term in Reference Safety Information (e.g. IB / SmPC)."""

    meddra_pt: str
    meddra_soc: str
    is_listed: bool = True
    expected_frequency_rate: float = 0.05  # e.g., 5% expected rate
    severity_grade_threshold: int = 3
    rsi_version: str = "IB Ed. 1.0"


class ReferenceSafetyInformationConfig(BaseModel):
    """Configuration and baseline terms for Reference Safety Information comparison."""

    rsi_document_id: str = "RSI-DOC-001"
    rsi_version: str = "1.0"
    effective_date: str = "2026-01-01"
    listed_terms: list[ReferenceSafetyInformationTerm] = []


# =========================================================================
# Cryptographically Sealed Regulatory Snapshot (PRD-AGG-001)
# =========================================================================


class RegulatorySnapshot(BaseModel):
    """Immutable point-in-time regulatory dataset snapshot."""

    snapshot_id: str
    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    dibd_anniversary: str | None = None
    snapshot_hash: str  # SHA-256 Merkle root / content digest
    total_cases_frozen: int = 0
    total_subjects_frozen: int = 0
    is_locked: bool = True
    locked_at: str | None = None
    locked_by: str | None = None
    lock_reason: str | None = None
    dataset_manifest: dict[str, Any] = Field(default_factory=dict)


# =========================================================================
# Executive QPPV Sign-Off Manifest (PRD-AGG-001, 21 CFR Part 11)
# =========================================================================


class QPPVSignoffManifest(BaseModel):
    """Tamper-evident 21 CFR Part 11 electronic signature manifest for QPPV."""

    qppv_user_id: str
    qppv_name: str
    qppv_role: str = "Qualified Person for Pharmacovigilance (QPPV)"
    meaning_of_signature: str = (
        "Executive QPPV Approval of Aggregate Safety Report for Regulatory Submission"
    )
    signed_at: str
    manifest_id: str
    snapshot_hash: str
    report_hash: str
    signature_token: str
    comments: str | None = None
    verification_status: str = "VALID"


# =========================================================================
# Line Listings and Summary Tabulations Models (ICH E2F / E2C(R2))
# =========================================================================


class DSURLineListingItem(BaseModel):
    """ICH E2F Standard Line Listing item for Serious Adverse Events."""

    case_id: str
    subject_id: str
    study_id: str
    treatment_arm: str = "Investigational Drug"
    dosage_cohort: str | None = None
    age: int | None = None
    sex: str | None = None
    country: str = "US"
    meddra_pt: str
    meddra_soc: str
    ae_term: str
    onset_date: str
    resolution_date: str | None = None
    outcome: str = "RECOVERED"
    seriousness_death: bool = False
    seriousness_life_threatening: bool = False
    seriousness_hospitalization: bool = False
    seriousness_disability: bool = False
    seriousness_congenital_anomaly: bool = False
    seriousness_other: bool = False
    causality: str = "RELATED"
    expectedness: str = "UNEXPECTED"
    susar: bool = False
    is_aesi: bool = False
    aesi_category: str | None = None
    action_taken: str = "DRUG_WITHDRAWN"


class DSURSummaryTabulationRow(BaseModel):
    """Interval summary tabulation row aggregated by SOC and PT with RSI comparison."""

    meddra_soc: str
    meddra_pt: str
    is_aesi: bool = False
    aesi_category: str | None = None
    is_fatal: bool = False
    is_susar: bool = False
    active_arm_count: int = 0
    active_arm_subjects: int = 0
    comparator_arm_count: int = 0
    comparator_arm_subjects: int = 0
    placebo_arm_count: int = 0
    placebo_arm_subjects: int = 0
    total_event_count: int = 0
    total_subjects_count: int = 0
    cumulative_event_count: int = 0
    cumulative_subjects_count: int = 0
    rsi_listed: bool = False
    rsi_expected_rate: float | None = None
    observed_rate_pct: float = 0.0
    observed_incidence_rate_pyr: float = 0.0
    rsi_rate_ratio: float | None = None
    rsi_threshold_breached: bool = False


# =========================================================================
# Request and Full Report Payload Models
# =========================================================================


class PeriodicReportCompilationRequest(BaseModel):
    """Request payload to trigger automated periodic safety report compilation."""

    report_type: PeriodicSafetyReportType = PeriodicSafetyReportType.DSUR
    study_id: str
    compound_name: str
    reporting_period_start: str  # ISO date YYYY-MM-DD
    reporting_period_end: str  # ISO date YYYY-MM-DD
    dibd_anniversary: str | None = None  # DIBD anniversary date
    sponsor_name: str = "Cadence Clinical Sponsor Inc."
    include_line_listings: bool = True
    include_summary_tabulations: bool = True
    include_signal_analyses: bool = True
    benefit_risk_narrative: str | None = None
    cumulative_exposure_subjects: int = 0
    rsi_config: ReferenceSafetyInformationConfig | None = None
    aesi_terms: list[str] | None = None
    cases: list[dict[str, Any]] | None = None
    subjects_exposure_data: list[dict[str, Any]] | None = None
    auto_lock_snapshot: bool = True


class PeriodicReportPayload(BaseModel):
    """Full structured JSON payload of a periodic regulatory safety report."""

    report_id: str
    report_type: PeriodicSafetyReportType
    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    dibd_anniversary: str | None = None
    sponsor_name: str
    snapshot: RegulatorySnapshot | None = None
    exposure_metrics: SubjectExposureMetrics = Field(
        default_factory=SubjectExposureMetrics
    )
    total_subject_exposure: int
    total_saes: int
    total_fatal_cases: int = 0
    total_susars: int = 0
    total_aesi: int = 0
    line_listings: list[DSURLineListingItem] = []
    summary_tabulations: list[DSURSummaryTabulationRow] = []
    rsi_comparison_summary: list[dict[str, Any]] = []
    signal_summary: list[dict[str, Any]] = []
    benefit_risk_assessment: str
    conclusions: str
    regulatory_guidelines: list[str] = []
    qppv_signoff: QPPVSignoffManifest | None = None
    xml_submission_package: str | None = None


# =========================================================================
# Presentation DTOs & Sign-Off Schemas
# =========================================================================


class PeriodicReportResponseDTO(BaseModel):
    """DTO for a persisted Periodic Safety Report record."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    report_type: str
    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    dibd_anniversary: str | None = None
    status: str
    total_subject_exposure: int
    total_saes: int
    total_fatal_cases: int = 0
    total_susars: int = 0
    total_aesi: int = 0
    snapshot_hash: str | None = None
    line_listings_count: int
    summary_tabulations_count: int
    signals_included_count: int
    payload: PeriodicReportPayload
    approved_by_user_id: str | None = None
    approved_at: str | None = None
    esignature_manifest_id: str | None = None
    qppv_signoff: QPPVSignoffManifest | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class PeriodicReportSignRequest(BaseModel):
    """21 CFR Part 11 Electronic Signature request for approving a Periodic Safety Report."""

    user_id: str
    meaning_of_signature: str = (
        "Approval of ICH E2F DSUR / ICH E2C(R2) Periodic Safety Report"
    )
    signature_secret: str
    role: str | None = None
    comments: str | None = None


class PeriodicReportSignResponse(BaseModel):
    """Response returned after electronic signature on a periodic report."""

    report_id: str
    status: PeriodicReportStatus
    approved_by_user_id: str
    approved_at: str
    esignature_manifest_id: str
    message: str
    qppv_manifest: QPPVSignoffManifest | None = None


# =========================================================================
# Additional Endpoint Request/Response Models
# =========================================================================


class SubjectExposureCalculationRequest(BaseModel):
    """Request to compute patient-years of exposure."""

    study_id: str
    reporting_period_start: str
    reporting_period_end: str
    subjects_exposure_data: list[dict[str, Any]] | None = None
    cases: list[dict[str, Any]] | None = None


class SubjectExposureCalculationResponse(BaseModel):
    """Response payload containing calculated exposure metrics."""

    study_id: str
    reporting_period_start: str
    reporting_period_end: str
    metrics: SubjectExposureMetrics


class RegulatorySnapshotLockRequest(BaseModel):
    """Request to seal and lock a point-in-time regulatory dataset snapshot."""

    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    dibd_anniversary: str | None = None
    lock_reason: str = "Lock dataset snapshot for periodic regulatory report"
    cases: list[dict[str, Any]] | None = None
    subjects_data: list[dict[str, Any]] | None = None


class RegulatorySnapshotLockResponse(BaseModel):
    """Response returned when a regulatory snapshot is locked."""

    snapshot: RegulatorySnapshot


class RegulatorySnapshotVerifyRequest(BaseModel):
    """Request to cryptographically verify a regulatory snapshot dataset."""

    snapshot_id: str
    snapshot_hash: str
    dataset_manifest: dict[str, Any]


class RegulatorySnapshotVerifyResponse(BaseModel):
    """Verification result for a regulatory snapshot."""

    snapshot_id: str
    is_valid: bool
    computed_hash: str
    expected_hash: str
    match_status: str
    verified_at: str


class RegulatorySubmissionPackageExportResponse(BaseModel):
    """Export package payload with binary/text content metadata."""

    report_id: str
    format: RegulatoryExportFormat
    content_type: str
    file_name: str
    content: str
