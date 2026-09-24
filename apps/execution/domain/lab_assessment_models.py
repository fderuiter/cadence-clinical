"""Domain models for unified laboratory clinical assessment engine.

Requirements:
- ADR-2199 / PRD-SYS-123 (Laboratory Normal Range Governance & Interoperability)
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-PVG-001 (Automated Pharmacovigilance & Safety Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field

from .lab_models import GradingScaleEnum


class LabAssessmentContext(BaseModel):
    """Execution context and subject parameters for laboratory clinical assessment."""

    session: Any | None = Field(
        default=None,
        description="Optional SQLAlchemy async database session for lookups & persistence",
    )
    study_id: str | None = Field(
        default=None, description="Unique clinical trial study identifier"
    )
    subject_id: str | None = Field(
        default=None, description="Unique subject participant identifier"
    )
    site_id: str | None = Field(
        default=None, description="Clinical investigator site identifier"
    )
    visit_id: str | None = Field(
        default=None, description="Study encounter / visit identifier"
    )
    subject: Any | None = Field(
        default=None, description="Optional ClinicalSubject ORM entity"
    )
    demographics: dict[str, Any] | None = Field(
        default=None, description="Demographics dictionary (gender, age, ethnicity)"
    )
    age: float | None = Field(
        default=None, description="Subject age in completed years at observation"
    )
    sex: str | None = Field(
        default=None, description="Subject biological sex ('M', 'F', 'U', etc.)"
    )
    ethnicity: str | None = Field(
        default=None,
        description="Subject ethnicity or race ('HISPANIC', 'ASIAN', etc.)",
    )
    baseline_value: float | None = Field(
        default=None,
        description="Baseline laboratory measurement value for delta check",
    )
    previous_value: float | None = Field(
        default=None, description="Prior laboratory observation measurement"
    )
    grading_scale: GradingScaleEnum | str = Field(
        default=GradingScaleEnum.CTCAE_V5,
        description="Protocol-pinned toxicity grading scale standard",
    )
    co_analytes: dict[str, float] = Field(
        default_factory=dict,
        description="Co-reported laboratory analyte values for compound evaluations (e.g. ALT, AST, BILI, ALKP)",
    )
    reference_ranges: list[Any] | None = Field(
        default=None,
        description="Optional pre-fetched reference range records or candidate dictionary",
    )
    protocol_version_tag: str | None = Field(
        default=None, description="Active protocol version tag string"
    )
    protocol_version_index: int | None = Field(
        default=None, description="Active protocol version sequence counter"
    )
    user_id: str = Field(
        default="system_lab_assessment",
        description="User identifier performing or triggering the assessment",
    )
    change_reason: str = Field(
        default="Laboratory Clinical Assessment",
        description="Mandatory 21 CFR Part 11 GxP justification reason",
    )
    auto_stage_ae: bool = Field(
        default=True,
        description="Automatically construct staged Grade 3/4 AE FormSubmission and review records",
    )
    auto_create_queries: bool = Field(
        default=True,
        description="Automatically construct ClinicalQuery discrepancy items for alerts",
    )
    background_tasks: Any | None = Field(
        default=None,
        description="Starlette BackgroundTasks instance for async safety notifications",
    )
    persist_records: bool = Field(
        default=False,
        description="If True and session is supplied, automatically add generated records to session",
    )

    model_config = {"arbitrary_types_allowed": True}


class LabAssessmentResult(BaseModel):
    """Evaluated clinical laboratory assessment result encapsulating reference ranges,
    mathematical unit conversions, toxicity grading, Hy's Law, delta checks, and staged AEs."""

    test_code: str = Field(..., description="Standardized laboratory test code")
    test_name: str = Field(..., description="Descriptive laboratory test name")
    original_value: float | None = Field(
        default=None, description="Raw captured measurement value"
    )
    original_value_string: str | None = Field(
        default=None, description="Raw textual observation value string"
    )
    original_unit: str | None = Field(
        default=None, description="Raw captured unit of measurement"
    )
    normalized_value: float | None = Field(
        default=None, description="Standardized / converted numeric measurement value"
    )
    normalized_unit: str | None = Field(
        default=None, description="Target normalized unit of measurement"
    )
    matched_reference_range: Any | None = Field(
        default=None, description="Matched LabReferenceRange entity or dictionary"
    )
    matched_normal_bounds: str | None = Field(
        default=None,
        description='JSON-serialized normal bounds, e.g. \'{"low": 10, "high": 40}\'',
    )
    ref_low: float | None = Field(
        default=None, description="Lower limit of demographic normal range (LLN)"
    )
    ref_high: float | None = Field(
        default=None, description="Upper limit of demographic normal range (ULN)"
    )
    critical_low: float | None = Field(
        default=None, description="Lower limit of critical/panic alert range"
    )
    critical_high: float | None = Field(
        default=None, description="Upper limit of critical/panic alert range"
    )
    indicator: str | None = Field(
        default=None,
        description="Range indicator ('NORMAL', 'LOW', 'HIGH', 'LOW LOW', 'HIGH HIGH', 'ABNORMAL')",
    )
    out_of_range: bool = Field(
        default=False,
        description="True if value deviates outside matched demographic normal bounds",
    )
    is_critical: bool = Field(
        default=False,
        description="True if value breaches critical/panic limits or is Grade >= 3 / Hy's Law",
    )
    is_outlier: bool = Field(
        default=False,
        description="True if out of range or Grade >= 3 toxicity",
    )
    ctcae_grade: int = Field(
        default=0,
        description="Computed NCI-CTCAE / DAIDS toxicity grade integer (0 to 4)",
    )
    grading_scale: str = Field(
        default="CTCAE_V5",
        description="Toxicity scale applied ('CTCAE_V5' or 'DAIDS_V2_1')",
    )
    toxicity_description: str = Field(
        default="Within normal limits",
        description="Detailed clinical severity criterion description",
    )
    is_critical_ae_trigger: bool = Field(
        default=False,
        description="True if Grade >= 3 or Hy's Law triggering automatic AE drafting",
    )
    is_hys_law_warning: bool = Field(
        default=False,
        description="True if Hy's Law compound criteria for severe DILI are fulfilled",
    )
    is_delta_check_alert: bool = Field(
        default=False,
        description="True if acute delta deviation >= 50% from baseline is detected",
    )
    delta_percentage: float | None = Field(
        default=None, description="Percentage deviation calculated relative to baseline"
    )
    uncalibrated_safeguard_triggered: bool = Field(
        default=False,
        description="True if local laboratory certification was missing or expired",
    )
    safeguard_message: str | None = Field(
        default=None, description="Explanatory clinical safeguard message"
    )
    staged_ae_payload: dict[str, Any] | None = Field(
        default=None, description="Drafted Adverse Event eCRF form submission payload"
    )
    observation_record: Any | None = Field(
        default=None,
        description="Constructed or updated ClinicalObservation ORM entity",
    )
    form_submission_record: Any | None = Field(
        default=None, description="Constructed draft AE FormSubmission ORM entity"
    )
    staged_review_record: Any | None = Field(
        default=None, description="Constructed StagedAdverseEventReview ORM entity"
    )
    queries_raised: list[Any] = Field(
        default_factory=list, description="Constructed ClinicalQuery ORM entities"
    )
    dispatched_alert_type: str | None = Field(
        default=None, description="Alert category dispatched to notification system"
    )
    eval_timestamp: str = Field(
        default_factory=lambda: datetime.now(UTC).isoformat(),
        description="ISO 8601 evaluation timestamp",
    )

    model_config = {"arbitrary_types_allowed": True}
