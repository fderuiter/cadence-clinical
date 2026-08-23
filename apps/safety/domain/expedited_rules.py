"""Domain models, enums, and DTOs for deterministic expedited safety reporting rules."""

from enum import StrEnum

from pydantic import BaseModel, Field, field_validator


class ReportingAuthority(StrEnum):
    """Supported global health authorities for expedited safety reporting."""

    US_FDA = "US_FDA"
    EMA = "EMA"
    PMDA = "PMDA"
    MHRA = "MHRA"
    HEALTH_CANADA = "HEALTH_CANADA"


class ClockAlertLevel(StrEnum):
    """Countdown clock threshold alert levels."""

    NORMAL = "NORMAL"
    WARNING = "WARNING"  # >= 75% elapsed
    CRITICAL = "CRITICAL"  # >= 90% elapsed
    OVERDUE = "OVERDUE"  # > 100% elapsed / past due date


class ExpeditedClassification(StrEnum):
    """Regulatory classification category for adverse event reporting."""

    SUSAR_FATAL_LIFE_THREATENING_7_DAY = "SUSAR_FATAL_LIFE_THREATENING_7_DAY"
    SUSAR_SERIOUS_UNEXPECTED_15_DAY = "SUSAR_SERIOUS_UNEXPECTED_15_DAY"
    NON_EXPEDITED_PERIODIC = "NON_EXPEDITED_PERIODIC"
    EXEMPT = "EXEMPT"


class ICSRApprovalStatus(StrEnum):
    """Part 11 approval and review status for ICSR reporting determinations."""

    DRAFT = "DRAFT"
    PHYSICIAN_APPROVED = "PHYSICIAN_APPROVED"
    TRANSMITTED = "TRANSMITTED"
    REJECTED = "REJECTED"


class CausalityCategory(StrEnum):
    """Causality assessment classification."""

    RELATED = "RELATED"
    SUSPECTED = "SUSPECTED"
    POSSIBLY_RELATED = "POSSIBLY_RELATED"
    PROBABLY_RELATED = "PROBABLY_RELATED"
    DEFINITELY_RELATED = "DEFINITELY_RELATED"
    NOT_RELATED = "NOT_RELATED"
    UNLIKELY = "UNLIKELY"


class ExpectednessCategory(StrEnum):
    """Expectedness determination against Reference Safety Information (RSI)."""

    EXPECTED = "EXPECTED"
    UNEXPECTED = "UNEXPECTED"


class CountdownClockStatus(BaseModel):
    """Real-time statutory countdown clock status."""

    clock_start_date: str = Field(
        ..., description="Day 0 initial receipt date in ISO 8601 format"
    )
    clock_due_date: str = Field(
        ..., description="Calculated due date in ISO 8601 format"
    )
    total_hours: float = Field(..., description="Total statutory duration in hours")
    elapsed_hours: float = Field(
        ..., description="Hours elapsed since Day 0 receipt date"
    )
    elapsed_percentage: float = Field(
        ..., description="Percentage of statutory clock elapsed (0.0 - 100.0+)"
    )
    remaining_hours: float = Field(..., description="Remaining hours before deadline")
    is_overdue: bool = Field(
        False, description="Whether statutory reporting deadline has expired"
    )
    alert_level: ClockAlertLevel = Field(
        ClockAlertLevel.NORMAL, description="Current alert threshold level"
    )


class AuthorityRuleEvaluationResult(BaseModel):
    """Deterministic evaluation outcome for a specific regulatory health authority."""

    authority: ReportingAuthority = Field(
        ..., description="Health authority identifier"
    )
    country_code: str = Field(..., description="ISO 3166-1 alpha-2 country code")
    is_expedited: bool = Field(
        ..., description="Whether expedited reporting is statutorily mandated"
    )
    timeframe_days: int | None = Field(
        None, description="Statutory timeframe in calendar days (e.g. 7 or 15)"
    )
    due_date: str | None = Field(
        None, description="Due date/time in ISO 8601 format if expedited"
    )
    rule_name: str = Field(..., description="Statutory rule title")
    statutory_citation: str = Field(
        ..., description="Regulatory citation (e.g. 21 CFR 312.32(c)(1)(i))"
    )
    classification: ExpeditedClassification = Field(
        ..., description="Expedited reporting classification category"
    )
    rationale: str = Field(..., description="Deterministic decision rationale")
    countdown: CountdownClockStatus | None = Field(
        None, description="Real-time countdown clock if expedited"
    )


class ICSRRuleEvaluationInput(BaseModel):
    """Input payload for deterministic multi-country safety reporting evaluation."""

    case_id: str = Field(..., description="Worldwide unique case ID or ICSR ID")
    study_id: str = Field(..., description="Protocol / Study identifier")
    subject_id: str = Field(..., description="Subject / USUBJID identifier")
    receipt_date: str = Field(
        ..., description="Day 0 receipt date/time in ISO 8601 or CDISC DTC format"
    )
    seriousness_death: bool = Field(False, description="Results in death")
    seriousness_life_threatening: bool = Field(False, description="Is life-threatening")
    seriousness_hospitalization: bool = Field(
        False, description="Requires or prolongs hospitalization"
    )
    seriousness_disability: bool = Field(
        False, description="Results in persistent or significant disability"
    )
    seriousness_congenital_anomaly: bool = Field(
        False, description="Results in congenital anomaly or birth defect"
    )
    seriousness_other: bool = Field(
        False, description="Other medically important serious event"
    )
    causality: CausalityCategory = Field(
        CausalityCategory.SUSPECTED, description="Medical causality / relatedness"
    )
    expectedness: ExpectednessCategory = Field(
        ExpectednessCategory.UNEXPECTED,
        description="Expectedness per Reference Safety Information (RSI)",
    )
    target_authorities: list[ReportingAuthority] | None = Field(
        None,
        description="List of target authorities to evaluate (evaluates all if omitted)",
    )
    reason_for_change: str | None = Field(
        None, description="GxP reason for evaluation / change"
    )


class ICSRRuleEvaluationSummary(BaseModel):
    """Comprehensive summary of multi-country ICSR reporting evaluation."""

    id: str = Field(..., description="Unique evaluation record ID")
    case_id: str = Field(..., description="Case identifier")
    study_id: str = Field(..., description="Study identifier")
    subject_id: str = Field(..., description="Subject identifier")
    receipt_date: str = Field(..., description="Day 0 receipt timestamp")
    is_serious: bool = Field(
        ..., description="Whether any seriousness criterion is met"
    )
    is_unexpected: bool = Field(..., description="Whether event is unexpected per RSI")
    is_causal: bool = Field(
        ..., description="Whether event is suspected/causally related"
    )
    is_susar: bool = Field(
        ...,
        description="Whether event qualifies as SUSAR (Serious + Unexpected + Related)",
    )
    any_expedited: bool = Field(
        ..., description="Whether any authority requires expedited reporting"
    )
    shortest_timeframe_days: int | None = Field(
        None, description="Shortest applicable statutory timeframe in days"
    )
    overall_alert_level: ClockAlertLevel = Field(
        ClockAlertLevel.NORMAL,
        description="Highest active alert level across authorities",
    )
    evaluations: list[AuthorityRuleEvaluationResult] = Field(
        default_factory=list, description="Per-authority evaluation outcomes"
    )
    approval_status: ICSRApprovalStatus = Field(
        ICSRApprovalStatus.DRAFT, description="Part 11 review / approval status"
    )
    approved_by_user_id: str | None = Field(
        None, description="User ID of approving Safety Physician"
    )
    approved_at: str | None = Field(
        None, description="Timestamp of Part 11 approval signature"
    )
    esignature_manifest_id: str | None = Field(
        None, description="Cryptographic electronic signature manifest identifier"
    )
    created_at: str = Field(..., description="Creation timestamp")
    created_by: str = Field(..., description="Creator user ID")
    reason_for_change: str = Field(..., description="GxP reason for change")
    version_index: int = Field(1, description="Audit version index")


class SignICSRRuleEvaluationRequest(BaseModel):
    """Request payload for 21 CFR Part 11 Safety Physician electronic signature."""

    reason_for_change: str = Field(
        ...,
        min_length=3,
        description="Mandatory 21 CFR Part 11 reason for electronic signature",
    )
    signature_secret: str | None = Field(
        None, description="Physician re-authentication signing secret / credential"
    )
    meaning_of_signature: str = Field(
        "Approval of Expedited ICSR Regulatory Submission and Causality Assessment",
        description="Part 11 declaration of signature intent",
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v or not v.strip():
            raise ValueError("Reason for change is mandatory and cannot be empty.")
        return v.strip()


class SignICSRRuleEvaluationResponse(BaseModel):
    """Response payload for 21 CFR Part 11 Safety Physician electronic signature."""

    evaluation_id: str = Field(..., description="Evaluation record ID")
    approval_status: ICSRApprovalStatus = Field(
        ..., description="Updated approval status"
    )
    approved_by_user_id: str = Field(
        ..., description="User ID of signing Safety Physician"
    )
    approved_at: str = Field(..., description="Timestamp of signature approval")
    esignature_manifest_id: str = Field(
        ..., description="Cryptographic electronic signature manifest ID"
    )
    message: str = Field(..., description="Confirmation message")
