"""Domain models, enums, and DTOs for Disproportionality Safety Signal Detection and Alerts.

Requirements: PRD-PVG-003, PRD-SYS-001, ADR-2199
(CIOMS VIII, FDA FAERS / EMA EudraVigilance Disproportionality Guidelines)
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict, Field


class StratificationLevel(StrEnum):
    """MedDRA hierarchical level for disproportionality analysis."""

    PT = "PT"  # Preferred Term
    SOC = "SOC"  # System Organ Class


class SignalStrength(StrEnum):
    """Classification of signal strength based on multi-metric consensus."""

    CRITICAL = "CRITICAL"
    STRONG = "STRONG"
    MODERATE = "MODERATE"
    WEAK = "WEAK"
    NONE = "NONE"


class SafetySignalAlertStatus(StrEnum):
    """Operational lifecycle status of a safety signal review alert."""

    TRIGGERED = "TRIGGERED"
    UNDER_REVIEW = "UNDER_REVIEW"
    CONFIRMED_SIGNAL = "CONFIRMED_SIGNAL"
    REFUTED = "REFUTED"
    CLOSED = "CLOSED"


class SafetySignalAlertSeverity(StrEnum):
    """Triage severity level for statistical threshold breaches."""

    CRITICAL = "CRITICAL"
    HIGH = "HIGH"
    MEDIUM = "MEDIUM"
    LOW = "LOW"


class SafetySignalReviewDecision(StrEnum):
    """Clinical safety review assessment decision."""

    CONFIRM_SIGNAL = "CONFIRM_SIGNAL"
    REFUTE_SIGNAL = "REFUTE_SIGNAL"
    REQUEST_SAFETY_INQUIRY = "REQUEST_SAFETY_INQUIRY"
    CLOSE_ALERT = "CLOSE_ALERT"


class BenchmarkReferenceDataset(StrEnum):
    """Reference background datasets for disproportionality comparisons."""

    INTERNAL_TRIAL_POOL = "INTERNAL_TRIAL_POOL"
    FAERS = "FAERS"
    EUDRAVIGILANCE = "EUDRAVIGILANCE"
    CUSTOM = "CUSTOM"


class DisproportionalityContingency(BaseModel):
    """2x2 contingency counts for a specific Drug-Event pair across PT or SOC."""

    drug_name: str
    meddra_pt: str  # MedDRA Preferred Term or SOC name
    meddra_soc: str | None = None
    term_level: StratificationLevel = StratificationLevel.PT
    a: int = Field(ge=0, description="Reports with target drug and target event")
    b: int = Field(ge=0, description="Reports with target drug and other events")
    c: int = Field(ge=0, description="Reports with other drugs and target event")
    d: int = Field(ge=0, description="Reports with other drugs and other events")
    stratum: str | None = None
    benchmark_dataset: str | None = None


class DisproportionalityMetricResult(BaseModel):
    """Calculated statistical signal detection metrics for a Drug-Event pair."""

    drug_name: str
    meddra_pt: str
    meddra_soc: str | None = None
    term_level: StratificationLevel = StratificationLevel.PT
    case_count_a: int
    case_count_b: int = 0
    case_count_c: int = 0
    case_count_d: int = 0
    total_n: int = 0
    expected_count_e: float
    prr: float
    prr_ci_lower: float
    prr_ci_upper: float
    chi_square_yates: float
    ror: float
    ror_ci_lower: float
    ror_ci_upper: float
    ebgm: float
    eb05: float  # 5th percentile lower bound of Empirical Bayes shrinkage
    eb95: float  # 95th percentile upper bound
    is_prr_signal: bool = False
    is_ror_signal: bool = False
    is_ebgm_signal: bool = False
    overall_signal_flag: bool = False
    signal_strength: SignalStrength = SignalStrength.NONE
    stratum: str | None = None
    benchmark_dataset: str | None = None
    benchmark_relative_risk: float | None = None


class MGPSMultiItemResult(BaseModel):
    """Multi-item Gamma Poisson Shrinker interaction score for drug/event combinations."""

    item_set: list[str] = Field(..., description="Combination of drug(s) and event(s)")
    observed_n: int
    expected_e: float
    ebgm: float
    eb05: float
    eb95: float
    interaction_score: float = Field(
        ..., description="Ratio of multi-item EBGM to expected marginal product"
    )
    is_interaction_signal: bool = False


class SignalDetectionScanRequest(BaseModel):
    """Request schema for executing a disproportionality safety signal detection scan."""

    target_drug: str
    study_id: str | None = None
    comparator_drugs: list[str] = []
    benchmark_dataset: BenchmarkReferenceDataset = (
        BenchmarkReferenceDataset.INTERNAL_TRIAL_POOL
    )
    stratification_levels: list[StratificationLevel] = [
        StratificationLevel.PT,
        StratificationLevel.SOC,
    ]
    min_case_count: int = Field(default=3, ge=1)
    prr_threshold: float = Field(default=2.0, ge=1.0)
    chi_square_threshold: float = Field(default=4.0, ge=0.0)
    ror_ci_lower_threshold: float = Field(default=1.0, ge=0.5)
    eb05_threshold: float = Field(default=2.0, ge=1.0)
    auto_trigger_alerts: bool = True
    cases: list[dict[str, Any]] | None = None
    benchmark_cases: list[dict[str, Any]] | None = None


class SignalDetectionScanResponse(BaseModel):
    """Response schema returned after executing a disproportionality scan."""

    run_id: str
    study_id: str | None = None
    target_drug: str
    benchmark_dataset: str
    total_cases_analyzed: int
    total_terms_evaluated: int
    signals_detected_count: int
    alerts_triggered_count: int
    results: list[DisproportionalityMetricResult]
    multi_item_results: list[MGPSMultiItemResult] = []
    created_at: str


class SafetySignalAlertDTO(BaseModel):
    """Data transfer object for a safety signal review alert."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    detection_run_id: str | None = None
    study_id: str | None = None
    target_drug: str
    meddra_term: str
    term_level: str
    meddra_soc: str | None = None
    case_count_a: int
    expected_count_e: float
    prr: float
    prr_ci_lower: float
    prr_ci_upper: float
    chi_square_yates: float
    ror: float
    ror_ci_lower: float
    ror_ci_upper: float
    ebgm: float
    eb05: float
    eb95: float
    severity: str
    status: str
    triage_summary: str
    assigned_safety_officer: str | None = None
    review_decision: str | None = None
    review_justification: str | None = None
    clinical_summary: str | None = None
    reviewed_by: str | None = None
    reviewed_at: str | None = None
    esignature_manifest_id: str | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SafetySignalAlertReviewRequest(BaseModel):
    """Request payload for reviewing and updating a signal alert."""

    decision: SafetySignalReviewDecision
    justification: str = Field(
        ..., min_length=5, description="Medical rationale for the review decision"
    )
    clinical_summary: str | None = None
    assigned_safety_officer: str | None = None
    recommended_action: str | None = None


class SafetySignalAlertSignRequest(BaseModel):
    """21 CFR Part 11 Electronic Signature request on a safety signal review disposition."""

    user_id: str
    meaning_of_signature: str = "Approval of Signal Review Assessment & Disposition"
    signature_secret: str
    decision: SafetySignalReviewDecision
    justification: str = Field(
        ..., min_length=5, description="Medical rationale for signature disposition"
    )
    clinical_summary: str | None = None


class SafetySignalAlertSignResponse(BaseModel):
    """Response returned after applying a Part 11 electronic signature to an alert."""

    alert_id: str
    status: SafetySignalAlertStatus
    reviewed_by: str
    reviewed_at: str
    esignature_manifest_id: str
    message: str
