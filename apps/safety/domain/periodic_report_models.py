"""Domain models, enums, and DTOs for Periodic Aggregate Safety Reporting.

Requirements: PRD-PVG-004, PRD-PVG-008, ADR-2199
(ICH E2F DSUR, ICH E2C(R2) PSUR / PBRER)
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, ConfigDict


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


class DSURLineListingItem(BaseModel):
    """ICH E2F Standard Line Listing item for Serious Adverse Events."""

    case_id: str
    subject_id: str
    study_id: str
    treatment_arm: str = "Investigational Drug"
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
    action_taken: str = "DRUG_WITHDRAWN"


class DSURSummaryTabulationRow(BaseModel):
    """Interval summary tabulation row aggregated by SOC and PT."""

    meddra_soc: str
    meddra_pt: str
    active_arm_count: int = 0
    active_arm_subjects: int = 0
    comparator_arm_count: int = 0
    comparator_arm_subjects: int = 0
    placebo_arm_count: int = 0
    placebo_arm_subjects: int = 0
    total_event_count: int = 0
    total_subjects_count: int = 0


class PeriodicReportCompilationRequest(BaseModel):
    """Request payload to trigger automated periodic safety report compilation."""

    report_type: PeriodicSafetyReportType = PeriodicSafetyReportType.DSUR
    study_id: str
    compound_name: str
    reporting_period_start: str  # ISO date YYYY-MM-DD
    reporting_period_end: str  # ISO date YYYY-MM-DD
    sponsor_name: str = "Cadence Clinical Sponsor Inc."
    include_line_listings: bool = True
    include_summary_tabulations: bool = True
    include_signal_analyses: bool = True
    benefit_risk_narrative: str | None = None
    cumulative_exposure_subjects: int = 0
    cases: list[dict[str, Any]] | None = None


class PeriodicReportPayload(BaseModel):
    """Full structured JSON payload of a periodic regulatory safety report."""

    report_id: str
    report_type: PeriodicSafetyReportType
    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    sponsor_name: str
    total_subject_exposure: int
    total_saes: int
    total_susars: int
    line_listings: list[DSURLineListingItem] = []
    summary_tabulations: list[DSURSummaryTabulationRow] = []
    signal_summary: list[dict[str, Any]] = []
    benefit_risk_assessment: str
    conclusions: str
    regulatory_guidelines: list[str] = []


class PeriodicReportResponseDTO(BaseModel):
    """DTO for a persisted Periodic Safety Report record."""

    model_config = ConfigDict(from_attributes=True)

    id: str
    report_type: str
    study_id: str
    compound_name: str
    reporting_period_start: str
    reporting_period_end: str
    status: str
    total_subject_exposure: int
    total_saes: int
    line_listings_count: int
    summary_tabulations_count: int
    signals_included_count: int
    payload: PeriodicReportPayload
    approved_by_user_id: str | None = None
    approved_at: str | None = None
    esignature_manifest_id: str | None = None
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


class PeriodicReportSignResponse(BaseModel):
    """Response returned after electronic signature on a periodic report."""

    report_id: str
    status: PeriodicReportStatus
    approved_by_user_id: str
    approved_at: str
    esignature_manifest_id: str
    message: str
