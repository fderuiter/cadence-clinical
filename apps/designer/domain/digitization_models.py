from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field


class ExtractedArm(BaseModel):
    """Represents a clinical study arm extracted from protocol documentation."""

    name: str = Field(..., description="Arm name e.g., 'Dose Escalation Cohort A'")
    arm_type: Literal[
        "EXPERIMENTAL",
        "ACTIVE_COMPARATOR",
        "PLACEBO_COMPARATOR",
        "SHAM_COMPARATOR",
        "NO_INTERVENTION",
    ] = Field(..., description="Arm classification type")
    description: str | None = Field(None, description="Clinical description of the arm")
    target_sample_size: int | None = Field(
        None, description="Target planned sample size"
    )


class ExtractedEpoch(BaseModel):
    """Represents a trial epoch extracted from protocol documentation."""

    name: str = Field(
        ...,
        description="Epoch name e.g., 'Screening', 'Treatment', 'Follow-up'",
    )
    epoch_type: Literal["SCREENING", "TREATMENT", "WASHOUT", "FOLLOW_UP", "RUN_IN"] = (
        Field(..., description="Epoch categorization type")
    )
    sequence_index: int = Field(..., description="Chronological sequence order index")


class ExtractedVisit(BaseModel):
    """Represents a study encounter or visit extracted from the protocol."""

    visit_name: str = Field(
        ..., description="Visit name e.g., 'Visit 1 / Day 1 (Baseline)'"
    )
    epoch_name: str = Field(..., description="Associated parent epoch identifier name")
    target_day: int = Field(..., description="Target protocol timeline day")
    window_lower_days: int = Field(0, description="Visit window lower margin in days")
    window_upper_days: int = Field(0, description="Visit window upper margin in days")
    is_mandatory: bool = Field(
        True, description="Whether visit is required for protocol compliance"
    )


class ExtractedActivity(BaseModel):
    """Represents a clinical procedure or assessment extracted from the protocol."""

    activity_name: str = Field(
        ..., description="Procedure e.g., '12-Lead ECG', 'Vital Signs'"
    )
    cdash_domain: str = Field(
        ..., description="CDASH domain code e.g., 'EG', 'VS', 'LB'"
    )
    biomedical_concept_code: str | None = Field(
        None, description="NCI Thesaurus or SNOMED CT concept code"
    )
    assigned_visit_names: list[str] = Field(
        default_factory=list,
        description="List of visit names where this procedure is scheduled",
    )


class ExtractedCriterion(BaseModel):
    """Represents an eligibility criterion extracted from the protocol."""

    criterion_type: Literal["INCLUSION", "EXCLUSION"] = Field(
        ..., description="Criterion classification"
    )
    identifier: str = Field(..., description="e.g. 'INC-01', 'EXC-04'")
    text_expression: str = Field(..., description="Original protocol text requirement")
    logical_expression: str | None = Field(
        None,
        description="Compiled logical check e.g., 'DM.AGE >= 18'",
    )


class USDMProtocolExtractionResponse(BaseModel):
    """Structured response representing complete USDM v4.0 protocol parameters."""

    study_title: str = Field(..., description="Clinical study title")
    protocol_id: str = Field(..., description="Protocol identification code")
    phase: Literal["PHASE_I", "PHASE_I_II", "PHASE_II", "PHASE_III", "PHASE_IV"] = (
        Field(..., description="Clinical development phase")
    )
    therapeutic_area: str = Field(
        ..., description="Therapeutic area e.g., 'Oncology', 'Cardiology'"
    )
    arms: list[ExtractedArm] = Field(
        default_factory=list, description="Extracted study arms"
    )
    epochs: list[ExtractedEpoch] = Field(
        default_factory=list, description="Extracted trial epochs"
    )
    visits: list[ExtractedVisit] = Field(
        default_factory=list, description="Extracted encounters / visits"
    )
    activities: list[ExtractedActivity] = Field(
        default_factory=list,
        description="Extracted Schedule of Activities procedures",
    )
    criteria: list[ExtractedCriterion] = Field(
        default_factory=list,
        description="Extracted inclusion/exclusion criteria",
    )
    confidence_score: float = Field(
        ..., ge=0.0, le=1.0, description="Overall LLM extraction confidence"
    )


class SynthesizedECRFForm(BaseModel):
    """Synthesized CDASH eCRF form definition compiled from extracted activities."""

    form_id: str = Field(..., description="Unique form identifier")
    form_name: str = Field(..., description="Human-readable form title")
    cdash_domain: str = Field(..., description="CDASH domain code")
    items: list[dict[str, Any]] = Field(
        default_factory=list, description="eCRF form item definitions"
    )
    rules: list[dict[str, Any]] = Field(
        default_factory=list, description="Compiled validation and skip rules"
    )


class CommitUSDMRequest(BaseModel):
    """Request payload for committing extracted USDM protocol data into Neo4j."""

    study_id: str = Field(..., description="Unique study identifier")
    data: USDMProtocolExtractionResponse = Field(
        ..., description="USDM extraction data"
    )
    change_reason: str = Field(..., description="GxP change justification reason")


class CommitUSDMResponse(BaseModel):
    """Response payload following USDM graph population and eCRF synthesis."""

    study_id: str
    version_id: str
    status: str
    nodes_created: int
    relationships_created: int
    synthesized_forms: list[SynthesizedECRFForm] = Field(default_factory=list)
    message: str
