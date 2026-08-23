"""Pydantic schemas for Quality service presentation layer."""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from apps.quality.domain.models import (
    ActionItemStatus,
    AuditStatus,
    AuditType,
    BenfordConformity,
    BreachStatus,
    CAPAStatus,
    CAPAType,
    DataIntegrityFindingStatus,
    DataIntegritySentinelType,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    EffectivenessOutcome,
    FindingSeverity,
    ForensicsAnomalyFlag,
    ImageTransformationType,
    RACTCategory,
    RCAMethodology,
    RiskCategory,
    RiskTier,
    RoundingBiasTier,
)


class DeviationCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    title: str = Field(
        ..., max_length=255, description="A short summary of the deviation"
    )
    description: str = Field(..., description="Detailed explanation of the deviation")
    severity: DeviationSeverity = Field(
        ..., description="Severity level: MINOR, MAJOR, CRITICAL, SYSTEMATIC_TREND"
    )
    type: DeviationType = Field(
        ..., description="Type of deviation, e.g., INFORMED_CONSENT"
    )
    category: str | None = Field(None, description="Optional granular category")
    is_protocol_violation: bool = Field(
        False, description="Whether this constitutes a protocol violation"
    )
    impact_safety: bool = Field(
        False, description="Potential or actual impact on subject safety"
    )
    impact_data: bool = Field(
        False, description="Potential or actual impact on study data reliability"
    )
    impact_compliance: bool = Field(
        False, description="Potential or actual impact on GCP regulatory compliance"
    )
    source_system: str = Field(
        "MANUAL", description="Source of deviation (MANUAL, EDC, CTMS, eTMF)"
    )
    source_reference_id: str | None = Field(
        None, description="External reference ID from originating system"
    )


class DeviationIngestRequest(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    title: str = Field(..., description="Summary of the quality event")
    description: str = Field(..., description="Detailed event log")
    severity: DeviationSeverity = Field(
        DeviationSeverity.MINOR, description="Severity grade"
    )
    type: DeviationType = Field(
        DeviationType.PROTOCOL_PROCEDURE, description="Deviation classification"
    )
    category: str | None = Field(None, description="Sub-category")
    is_protocol_violation: bool = Field(False, description="Violation flag")
    impact_safety: bool = Field(False, description="Safety impact")
    impact_data: bool = Field(False, description="Data impact")
    impact_compliance: bool = Field(False, description="Compliance impact")
    source_system: str = Field(..., description="Source system: EDC, CTMS, eTMF")
    source_reference_id: str | None = Field(
        None, description="Idempotency key / foreign entity reference"
    )


class DeviationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str | None = None
    title: str
    description: str
    severity: DeviationSeverity
    status: DeviationStatus
    type: DeviationType
    category: str | None = None
    is_protocol_violation: bool
    impact_safety: bool = False
    impact_data: bool = False
    impact_compliance: bool = False
    source_system: str = "MANUAL"
    source_reference_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RCACreateOrUpdate(BaseModel):
    methodology: str = Field(
        RCAMethodology.FIVE_WHYS.value,
        max_length=255,
        description="RCA methodology used (FIVE_WHYS, ISHIKAWA_FISHBONE, HUMAN_FACTORS)",
    )
    investigation_details: str = Field(
        ..., description="Full details of the investigation"
    )
    root_cause_summary: str = Field(
        ..., description="Summary of the determined root cause"
    )
    five_whys_chain: dict | list | None = Field(
        None, description="Structured 5-Whys causal hierarchy (why_1 through why_5)"
    )
    fishbone_categories: dict | None = Field(
        None,
        description="Ishikawa 6M categories (Man, Machine, Material, Method, Measurement, Milieu)",
    )
    contributing_factors: list | None = Field(
        None, description="Contributing factor tags and classifications"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class RCAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    methodology: str
    investigation_details: str
    root_cause_summary: str
    five_whys_chain: dict | list | None = None
    fishbone_categories: dict | None = None
    contributing_factors: list | None = None
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPAActionItemCreate(BaseModel):
    title: str = Field(..., description="Action item title")
    description: str = Field(..., description="Detailed instructions for the action")
    action_type: str = Field(
        "CORRECTIVE", description="Action type: CORRECTIVE or PREVENTIVE"
    )
    assigned_to: str = Field(
        ..., description="User ID assigned to complete this action"
    )
    due_date: datetime | None = Field(None, description="Target completion timestamp")


class CAPAActionItemUpdate(BaseModel):
    status: ActionItemStatus = Field(
        ..., description="Target status: OPEN, IN_PROGRESS, COMPLETED, CANCELLED"
    )
    evidence_url: str | None = Field(
        None, description="URL or reference to completion verification evidence"
    )


class CAPAActionItemResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    capa_id: str
    title: str
    description: str
    action_type: str
    assigned_to: str
    due_date: str | None = None
    status: str
    completed_at: str | None = None
    evidence_url: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPAEffectivenessCheckCreate(BaseModel):
    planned_date: datetime = Field(..., description="Scheduled verification date")
    metric_evaluated: str = Field(..., description="Metric or condition being verified")
    baseline_value: str = Field(
        ..., description="Baseline measurement before CAPA implementation"
    )
    target_value: str = Field(..., description="Target success threshold")
    actual_value: str = Field(
        ..., description="Observed measurement during effectiveness check"
    )
    outcome: EffectivenessOutcome = Field(
        ...,
        description="Evaluation outcome: EFFECTIVE, INEFFECTIVE, PARTIALLY_EFFECTIVE",
    )
    comments: str | None = Field(None, description="Evaluator rationale and notes")


class CAPAEffectivenessCheckResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    capa_id: str
    planned_date: str
    executed_date: str | None = None
    metric_evaluated: str
    baseline_value: str
    target_value: str
    actual_value: str | None = None
    outcome: str
    evaluator_id: str | None = None
    comments: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPACreate(BaseModel):
    deviation_id: str = Field(..., description="Reference to the parent deviation ID")
    rca_id: str | None = Field(
        None, description="Optional reference to the Root Cause Analysis ID"
    )
    capa_type: str = Field(
        CAPAType.BOTH.value, description="Type of CAPA: CORRECTIVE, PREVENTIVE, BOTH"
    )
    action_plan: str = Field(
        ..., description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    risk_level: str = Field(
        "MEDIUM", description="Risk level: LOW, MEDIUM, HIGH, CRITICAL"
    )
    lead_investigator_id: str | None = Field(
        None, description="Lead investigator user ID"
    )
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )
    effectiveness_interval_days: int = Field(
        30,
        description="Scheduled interval in days before post-closure effectiveness check",
    )
    audit_finding_id: str | None = Field(
        None, description="Optional linked audit finding ID"
    )


class CAPATransitionRequest(BaseModel):
    to_status: CAPAStatus = Field(
        ..., description="Target CAPA Status to transition to"
    )
    version_index: int | None = Field(
        None, description="Expected version index for optimistic locking"
    )


class CAPAUpdate(BaseModel):
    action_plan: str | None = Field(
        None, description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    risk_level: str | None = Field(None, description="Risk level")
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )
    effectiveness_interval_days: int | None = Field(
        None, description="Effectiveness interval in days"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class CAPAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    rca_id: str | None = None
    capa_type: str
    action_plan: str
    status: CAPAStatus
    preventive_measures: str | None = None
    risk_level: str = "MEDIUM"
    lead_investigator_id: str | None = None
    qa_approver_id: str | None = None
    target_completion_date: str | None = None
    actual_completion_date: str | None = None
    effectiveness_interval_days: int = 30
    effectiveness_review_date: str | None = None
    effectiveness_outcome: str = "PENDING"
    recurrence_detected: bool = False
    audit_finding_id: str | None = None
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str
    action_items: list[CAPAActionItemResponse] = []
    effectiveness_checks: list[CAPAEffectivenessCheckResponse] = []


# --- RBQM / KRI / QTL DTOs ---


class CtQFactorCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    category: str = Field(
        RiskCategory.PATIENT_SAFETY.value, description="Risk category"
    )
    critical_aspect: str = Field(..., description="Critical process or data parameter")
    risk_description: str = Field(..., description="Potential threat to quality")
    impact_area: str = Field(..., description="Primary endpoint, patient safety, etc.")
    mitigation_strategy: str = Field(..., description="Operational control measures")


class CtQFactorResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    category: str
    critical_aspect: str
    risk_description: str
    impact_area: str
    mitigation_strategy: str
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class KRIDefinitionCreate(BaseModel):
    code: str = Field(..., description="Unique KRI identifier code (e.g. KRI_AE_RATE)")
    name: str = Field(..., description="Human-readable KRI title")
    category: str = Field(
        RiskCategory.DATA_INTEGRITY.value, description="Risk category"
    )
    description: str = Field(..., description="Metric description")
    calculation_formula: str = Field(..., description="Formula definition")
    green_threshold: float = Field(1.0, description="Green / Low risk boundary")
    amber_threshold: float = Field(2.0, description="Amber / Moderate risk boundary")
    red_threshold: float = Field(3.0, description="Red / High risk boundary")
    weight: float = Field(1.0, description="Weight factor for Site Risk Index")


class KRIDefinitionResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    code: str
    name: str
    category: str
    description: str
    calculation_formula: str
    green_threshold: float
    amber_threshold: float
    red_threshold: float
    weight: float
    is_active: bool
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class KRIBatchEvaluationRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    kri_code: str = Field(..., description="KRI Code to evaluate across sites")
    site_raw_values: dict[str, float] = Field(
        ..., description="Dictionary mapping site_id -> raw metric value"
    )


class KRIMetricEvaluationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    kri_code: str
    evaluation_date: str
    raw_value: float
    standardized_z_score: float
    risk_tier: str
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class SiteRiskProfileResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    evaluation_date: str
    composite_risk_score: float
    risk_rank: int
    percentile_rank: float = 0.0
    risk_tier: str = "LOW"
    high_risk_kri_count: int
    active_deviations_count: int
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


# --- TransCelerate RACT DTOs (PRD-CSM-005) ---


class RACTRiskFactorCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    category: RACTCategory = Field(
        ...,
        description="Protocol risk category: SAFETY, CONSENT, STUDY_CONDUCT, DATA_INTEGRITY, IP_MANAGEMENT, TECHNOLOGY",
    )
    risk_topic: str = Field(
        ...,
        max_length=255,
        description="Concise risk factor title / topic (e.g. Complex Dosing)",
    )
    risk_description: str = Field(
        ..., description="Detailed description of the potential risk"
    )
    probability_score: int = Field(
        ..., ge=1, le=3, description="Likelihood score: 1=Low, 2=Medium, 3=High"
    )
    impact_score: int = Field(
        ..., ge=1, le=3, description="Impact severity score: 1=Low, 2=Medium, 3=High"
    )
    detectability_score: int = Field(
        ...,
        ge=1,
        le=3,
        description="Detectability score: 1=High (Easy), 2=Medium, 3=Low (Hard)",
    )
    mitigation_strategy: str = Field(
        ..., description="Operational risk mitigation and control plan"
    )
    linked_kri_code: str | None = Field(
        None, description="Optional code of linked automated Key Risk Indicator"
    )


class RACTRiskFactorUpdate(BaseModel):
    risk_topic: str | None = Field(
        None, max_length=255, description="Updated risk topic"
    )
    risk_description: str | None = Field(None, description="Updated risk description")
    probability_score: int | None = Field(
        None, ge=1, le=3, description="Updated probability score"
    )
    impact_score: int | None = Field(
        None, ge=1, le=3, description="Updated impact score"
    )
    detectability_score: int | None = Field(
        None, ge=1, le=3, description="Updated detectability score"
    )
    mitigation_strategy: str | None = Field(
        None, description="Updated mitigation strategy"
    )
    linked_kri_code: str | None = Field(None, description="Updated linked KRI code")
    is_active: bool | None = Field(None, description="Active status flag")


class RACTRiskFactorResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    category: str
    risk_topic: str
    risk_description: str
    probability_score: int
    impact_score: int
    detectability_score: int
    total_risk_score: int
    risk_tier: str
    mitigation_strategy: str
    linked_kri_code: str | None = None
    is_active: bool = True
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RACTCategorySummary(BaseModel):
    category: str
    total_factors: int
    high_count: int
    medium_count: int
    low_count: int
    average_score: float
    linked_kri_count: int


class RACTMatrixSummaryResponse(BaseModel):
    study_id: str
    total_risk_factors: int
    high_risk_count: int
    medium_risk_count: int
    low_risk_count: int
    overall_average_score: float
    linked_kri_coverage_percentage: float
    category_summaries: list[RACTCategorySummary]
    risk_factors: list[RACTRiskFactorResponse]


# --- Centralized Monitoring Inspection Dossier DTOs (PRD-CSM-006) ---


class CompileCSMDossierRequest(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    dossier_name: str | None = Field(
        None, description="Custom name for the compiled inspection dossier"
    )
    signer_name: str = Field(..., description="Full legal name of the reviewing signer")
    signer_role: str = Field(
        ..., description="Role of the signer (e.g. Lead Central Monitor / QA Lead)"
    )
    signature_meaning: str = Field(
        "I approve this Centralized Monitoring Inspection Dossier in accordance with 21 CFR Part 11 and ICH E6(R3).",
        description="21 CFR Part 11 controlled signing statement",
    )


class CSMDossierResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    dossier_name: str
    generated_at: str
    generated_by: str
    status: str
    ract_summary: dict[str, Any]
    kri_surveillance_summary: dict[str, Any]
    site_risk_rankings: list[dict[str, Any]]
    anomaly_forensics_summary: dict[str, Any]
    electronic_signature: dict[str, Any]
    audit_trail_snapshot: list[dict[str, Any]]
    merkle_seal: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class QTLCreate(BaseModel):
    study_id: str = Field(..., description="Study ID")
    parameter_name: str = Field(
        ..., description="Parameter name (e.g. Lost to Follow-up Rate)"
    )
    target_value: float = Field(..., description="Expected target value")
    tolerance_limit: float = Field(
        ..., description="Maximum allowable threshold before breach"
    )
    unit: str = Field("%", description="Measurement unit (%, count, days)")


class QTLResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    parameter_name: str
    target_value: float
    tolerance_limit: float
    unit: str
    is_breached: bool
    breach_count: int
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class QTLEvaluateBreachRequest(BaseModel):
    observed_value: float = Field(..., description="Current observed metric value")
    root_cause: str = Field(
        ..., description="Investigation of why the QTL was breached"
    )
    corrective_action_summary: str = Field(
        ..., description="Mitigation and corrective actions planned"
    )


class QTLBreachEventResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    qtl_id: str
    study_id: str
    breach_date: str
    observed_value: float
    threshold_value: float
    root_cause: str
    corrective_action_summary: str
    csr_narrative: str
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


# --- Clinical Audits & Findings DTOs ---


class AuditCreate(BaseModel):
    audit_number: str = Field(
        ..., description="Unique audit engagement identifier (e.g. AUD-2026-001)"
    )
    study_id: str = Field(..., description="Clinical study ID")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    vendor_name: str | None = Field(None, description="Optional CRO/Vendor name")
    audit_type: str = Field(
        AuditType.SITE_AUDIT.value,
        description="Audit type: SITE_AUDIT, VENDOR_QUALIFICATION, PROCESS_AUDIT, TMF_AUDIT",
    )
    lead_auditor: str = Field(..., description="Lead auditor name / ID")
    planned_start_date: datetime = Field(..., description="Scheduled audit start")
    planned_end_date: datetime = Field(..., description="Scheduled audit end")
    scope_summary: str = Field(..., description="Scope and objectives of the audit")


class AuditStatusUpdate(BaseModel):
    status: AuditStatus = Field(..., description="Updated audit status")
    actual_start_date: datetime | None = Field(
        None, description="Actual start timestamp"
    )
    actual_end_date: datetime | None = Field(
        None, description="Actual completion timestamp"
    )


class AuditFindingCreate(BaseModel):
    finding_number: str = Field(..., description="Finding sequence (e.g. F-01)")
    severity: str = Field(
        FindingSeverity.MAJOR.value,
        description="Severity: CRITICAL, MAJOR, MINOR, OBSERVATION",
    )
    category: str = Field(
        ...,
        description="Finding category (Informed Consent, Investigational Product, Data Integrity)",
    )
    condition: str = Field(..., description="Observed non-compliance or defect")
    criteria: str = Field(..., description="Applicable GCP or protocol requirement")
    cause: str = Field(..., description="Why the condition occurred")
    effect: str = Field(
        ..., description="Impact on subject safety, rights, or trial reliability"
    )


class AuditFindingResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    audit_id: str
    finding_number: str
    severity: str
    category: str
    condition: str
    criteria: str
    cause: str
    effect: str
    capa_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class AuditResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    audit_number: str
    study_id: str
    site_id: str | None = None
    vendor_name: str | None = None
    audit_type: str
    lead_auditor: str
    planned_start_date: str
    planned_end_date: str
    actual_start_date: str | None = None
    actual_end_date: str | None = None
    status: str
    scope_summary: str
    findings: list[AuditFindingResponse] = []
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class PromoteFindingToCAPARequest(BaseModel):
    action_plan: str = Field(
        ..., description="Corrective action plan to remediate the finding"
    )
    preventive_measures: str | None = Field(
        None, description="Preventive measures to prevent recurrence"
    )
    target_completion_date: datetime | None = Field(
        None, description="Target completion date"
    )


# --- Serious Breach DTOs ---


class SeriousBreachReportRequest(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    title: str = Field(..., description="Title of serious breach event")
    summary: str = Field(
        ..., description="Comprehensive description of the breach and suspected impact"
    )
    event_date: datetime = Field(..., description="Date breach occurred")
    discovery_date: datetime = Field(..., description="Date breach was discovered")
    affected_authorities: list[str] = Field(
        default_factory=lambda: ["MHRA"],
        description="List of regulatory authorities to notify",
    )


class SeriousBreachConfirmRequest(BaseModel):
    affected_authorities: list[str] = Field(
        ..., description="Confirmed list of health authorities to notify"
    )


class SeriousBreachStatusUpdate(BaseModel):
    status: BreachStatus = Field(
        ...,
        description="Target status: CONFIRMED_BREACH, AUTHORITY_NOTIFIED, RESOLVED, DISMISSED",
    )


class SeriousBreachResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str | None = None
    title: str
    summary: str
    event_date: str
    discovery_date: str
    confirmation_date: str | None = None
    reporting_deadline: str | None = None
    affected_authorities: list | None = None
    status: str
    regulatory_clock_hours_remaining: float = 168.0
    lead_qa_id: str
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RegulatoryClockStatusResponse(BaseModel):
    breach_id: str
    study_id: str
    status: str
    reporting_deadline: str | None = None
    regulatory_clock_hours_remaining: float
    is_approaching_deadline: bool
    is_overdue: bool
    affected_authorities: list | None = None


# --- Audit Ledger DTO ---


class AuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str
    entity_type: str | None = None
    record_id: str | None = None
    old_value: Any | None = None
    new_value: Any | None = None
    change_reason: str | None = None
    merkle_hash: str | None = None
    sha256_hash: str | None = None
    signature_hash: str | None = None


# --- Centralized Statistical Monitoring (CSM) & Forensics DTOs ---


class SiteObservationPayload(BaseModel):
    site_id: str = Field(..., description="Site identifier")
    observations: list[dict[str, float]] = Field(
        ..., description="List of multi-variable continuous observation dictionaries"
    )


class StatisticalForensicsAnalyzeRequest(BaseModel):
    study_id: str = Field(..., description="Clinical study ID")
    target_variables: list[str] = Field(
        default=["systolic_bp", "diastolic_bp", "heart_rate", "lab_creatinine"],
        description="Continuous clinical variables for multivariate surveillance",
    )
    site_observations: dict[str, list[dict[str, float]]] | None = Field(
        None, description="Mapping of site_id to list of observation dictionaries"
    )
    site_data: list[SiteObservationPayload] | None = Field(
        None, description="Alternative list format of site observation payloads"
    )
    run_name: str | None = Field(
        None, description="Custom name for the surveillance run"
    )
    auto_trigger_kri: bool = Field(
        True,
        description="Whether to automatically dispatch KRI breach alerts for flagged sites",
    )


class BenfordDigitDistributionResponse(BaseModel):
    sample_size: int
    chi_square: float
    p_value: float
    mad: float
    conformity: BenfordConformity
    observed_frequencies: dict[str, int]
    expected_frequencies: dict[str, float]


class TerminalDigitDistributionResponse(BaseModel):
    sample_size: int
    chi_square: float
    p_value: float
    zero_five_index: float
    rounding_bias_tier: RoundingBiasTier
    observed_frequencies: dict[str, int]
    expected_frequencies: dict[str, float]


class MahalanobisMetricsResponse(BaseModel):
    mahalanobis_distance: float
    mahalanobis_p_value: float
    correlation_matrix_distance: float
    site_mean_vector: dict[str, float]
    site_covariance_matrix: dict[str, Any]


class VarianceShrinkageMetricsResponse(BaseModel):
    variance_ratio: float
    variance_shrinkage_p_value: float
    kl_divergence: float
    is_variance_shrunk: bool


class SiteForensicsResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    run_id: str
    study_id: str
    site_id: str
    sample_size: int
    benford: BenfordDigitDistributionResponse
    terminal_digit: TerminalDigitDistributionResponse
    multivariate: MahalanobisMetricsResponse
    variance_shrinkage: VarianceShrinkageMetricsResponse
    composite_risk_score: float
    risk_tier: RiskTier
    is_anomalous_outlier: bool
    flagged_anomaly_reasons: list[str]
    kri_evaluation_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class StatisticalForensicsRunResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    run_name: str
    analysis_timestamp: str
    target_variables: list[str]
    total_sites: int
    total_observations: int
    anomalous_sites_count: int
    status: str
    site_results: list[SiteForensicsResponse]
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class ForensicsSimulationRequest(BaseModel):
    study_id: str = Field("STUDY-ONC-2026", description="Study ID for simulation")
    num_normal_sites: int = Field(
        4, ge=1, le=50, description="Count of compliant benchmark sites"
    )
    include_benford_anomaly: bool = Field(
        True, description="Include site violating Benford's Law"
    )
    include_rounding_bias: bool = Field(
        True, description="Include site with excessive terminal digit rounding"
    )
    include_variance_shrinkage: bool = Field(
        True, description="Include site with fabricated variance shrinkage"
    )
    include_multivariate_outlier: bool = Field(
        True, description="Include site with shifted multivariate correlation centroid"
    )
    samples_per_site: int = Field(
        60, ge=10, le=500, description="Observations per site"
    )


class ForensicsSimulationResponse(BaseModel):
    study_id: str
    target_variables: list[str]
    site_data: dict[str, list[dict[str, float]]]
    total_sites: int
    total_observations: int
    simulated_anomalies: list[str]


# --- Data Integrity Sentinel DTOs (PRD-CSM-003, PRD-CSM-004) ---


class SubjectClinicalVectorPayload(BaseModel):
    subject_id: str = Field(..., description="Subject unique identifier")
    site_id: str = Field(..., description="Clinical site ID")
    study_id: str = Field(..., description="Study ID")
    demographics: dict[str, Any] = Field(
        default_factory=dict,
        description="Demographics: age, sex, race, ethnicity, height_cm, weight_kg, bmi, blood_type",
    )
    vital_signs: dict[str, float] = Field(
        default_factory=dict,
        description="Baseline vital signs: systolic_bp, diastolic_bp, heart_rate, respiratory_rate, temp",
    )
    ecg_intervals: dict[str, float] = Field(
        default_factory=dict,
        description="12-Lead ECG intervals: pr_interval_ms, qrs_duration_ms, qt_interval_ms, qtc_ms, rr_interval_ms",
    )
    lab_panel: dict[str, float] = Field(
        default_factory=dict,
        description="Baseline laboratory panel: creatinine, alt, ast, fasting_glucose, hemoglobin, etc.",
    )
    medical_history: list[str] = Field(
        default_factory=list,
        description="Baseline medical history ICD-10 codes or disease strings",
    )


class DuplicatePatientScanRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    subjects: list[SubjectClinicalVectorPayload] | None = Field(
        None, description="List of baseline subject clinical vector payloads"
    )
    raw_subjects: list[dict[str, Any]] | None = Field(
        None, description="Optional raw dictionary format for subject vectors"
    )
    similarity_threshold: float = Field(
        0.82,
        ge=0.50,
        le=1.0,
        description="Minimum composite similarity threshold to flag duplicate",
    )
    auto_dispatch_investigation: bool = Field(
        True,
        description="Automatically dispatch Part 11 investigation deviation if critical match",
    )


class AuditEventPayload(BaseModel):
    entry_id: str = Field(..., description="Audit event identifier")
    subject_id: str = Field(..., description="Subject identifier")
    site_id: str = Field(..., description="Site identifier")
    user_id: str = Field(..., description="User identifier")
    timestamp: str = Field(..., description="ISO datetime timestamp of event")
    assessment_type: str = Field(..., description="Assessment or form type")
    procedure_pair: str | None = Field(
        None, description="Procedure pair identifier for elapsed interval evaluation"
    )
    elapsed_time_seconds: float | None = Field(
        None, description="Inter-procedure elapsed interval in seconds"
    )


class TemporalAnomalyScanRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    audit_events: list[AuditEventPayload] | None = Field(
        None, description="List of data entry audit events"
    )
    raw_events: list[dict[str, Any]] | None = Field(
        None, description="Optional raw dictionary format of audit events"
    )
    work_hour_start: int = Field(
        7, ge=0, le=23, description="Clinic opening hour (0-23)"
    )
    work_hour_end: int = Field(
        19, ge=0, le=23, description="Clinic closing hour (0-23)"
    )
    auto_dispatch_investigation: bool = Field(
        True,
        description="Automatically dispatch Part 11 investigation deviation if critical anomaly",
    )


class ImageAttachmentPayload(BaseModel):
    image_id: str = Field(..., description="Image attachment identifier")
    subject_id: str = Field(..., description="Subject identifier")
    site_id: str = Field(..., description="Site identifier")
    study_id: str = Field(..., description="Study identifier")
    visit_name: str = Field("Screening", description="Visit / milestone name")
    modality: str = Field(
        "RADIOLOGY", description="Imaging modality (e.g. CHEST_CT, XRAY, MRI, DERM)"
    )
    pixel_matrix: list[list[float]] | None = Field(
        None, description="2D grayscale pixel array (e.g. 32x32)"
    )
    base64_data: str | None = Field(
        None, description="Optional base64 encoded image string"
    )
    phash: str | None = Field(None, description="Precomputed 16-hex pHash string")


class ImageHashScanRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    image_attachments: list[ImageAttachmentPayload] | None = Field(
        None, description="List of medical image attachments"
    )
    raw_images: list[dict[str, Any]] | None = Field(
        None, description="Optional raw dictionary format for image attachments"
    )
    hamming_threshold: int = Field(
        10,
        ge=0,
        le=32,
        description="Maximum Hamming distance threshold to classify duplicate (<=10)",
    )
    auto_dispatch_investigation: bool = Field(
        True,
        description="Automatically dispatch Part 11 investigation deviation if duplicate found",
    )


class DataIntegrityComprehensiveScanRequest(BaseModel):
    study_id: str = Field(..., description="Study ID")
    subjects: list[SubjectClinicalVectorPayload] | None = Field(
        None, description="Baseline clinical subject vectors"
    )
    audit_events: list[AuditEventPayload] | None = Field(
        None, description="Data entry audit events"
    )
    image_attachments: list[ImageAttachmentPayload] | None = Field(
        None, description="Medical image attachments"
    )
    auto_dispatch_investigation: bool = Field(
        True,
        description="Automatically dispatch Part 11 investigation deviation for critical findings",
    )


class DataIntegrityFindingResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    run_id: str
    study_id: str
    sentinel_type: DataIntegritySentinelType
    severity: DeviationSeverity
    anomaly_flag: ForensicsAnomalyFlag
    primary_subject_id: str
    primary_site_id: str
    matched_subject_id: str | None = None
    matched_site_id: str | None = None
    confidence_score: float
    similarity_score: float
    hamming_distance: int | None = None
    transformation_type: ImageTransformationType | None = None
    evidence_summary: str
    forensic_metadata: dict[str, Any]
    investigation_deviation_id: str | None = None
    kri_evaluation_id: str | None = None
    status: DataIntegrityFindingStatus
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class DataIntegritySentinelRunResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    run_name: str
    scan_timestamp: str
    sentinel_types: list[str]
    total_subjects_scanned: int
    total_images_scanned: int
    total_events_scanned: int
    anomalies_detected_count: int
    risk_tier: RiskTier
    status: str
    summary_findings: dict[str, Any]
    findings: list[DataIntegrityFindingResponse]
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class DataIntegrityInvestigationDispatchRequest(BaseModel):
    finding_id: str = Field(
        ..., description="Data integrity finding ID to dispatch investigation for"
    )
    rationale: str = Field(
        "Manual investigation ticket dispatch from Data Integrity Sentinel Console",
        description="Reason for change / investigation rationale",
    )


class DataIntegritySimulationRequest(BaseModel):
    study_id: str = Field("STUDY-ONC-2026", description="Study ID for simulation")
    num_subjects_per_site: int = Field(
        15, ge=5, le=100, description="Subjects per benchmark site"
    )
    include_duplicate_patient: bool = Field(
        True, description="Inject cross-site duplicate subject vector"
    )
    include_temporal_burst: bool = Field(
        True, description="Inject Sunday off-hours rapid burst cluster"
    )
    include_identical_intervals: bool = Field(
        True, description="Inject identical procedure elapsed intervals"
    )
    include_image_duplication: bool = Field(
        True, description="Inject rotated 90 deg duplicate image scan"
    )


class DataIntegritySimulationResponse(BaseModel):
    study_id: str
    subjects_count: int
    audit_events_count: int
    image_attachments_count: int
    simulated_scenarios: list[str]
    sample_subjects: list[dict[str, Any]]
    sample_audit_events: list[dict[str, Any]]
    sample_image_attachments: list[dict[str, Any]]
