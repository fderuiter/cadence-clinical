"""Pydantic data models for Protocol Quality Sentinel and site feasibility analyzer.

Requirements: PRD-SYS-001
"""

from typing import List, Optional

from pydantic import BaseModel, Field


class QualityRuleFinding(BaseModel):
    """Specific protocol quality rule finding.

    Requirements: PRD-SYS-001
    """

    rule_id: str = Field(
        ..., description="Unique quality rule ID (e.g. SENTINEL_REQ_01)"
    )
    severity: str = Field(..., description="Severity level: ERROR, WARNING, INFO")
    category: str = Field(
        ..., description="Category: Structural, Regulatory, Burden, Inconsistency"
    )
    message: str = Field(..., description="Human-readable rule finding message")
    target_node_id: Optional[str] = Field(None, description="Target USDM graph node ID")


class ReadabilityReport(BaseModel):
    """Deterministic readability metrics of block texts."""

    flesch_reading_ease: float = Field(..., description="Flesch Reading Ease score")
    flesch_kincaid_grade_level: float = Field(
        ..., description="Flesch-Kincaid Grade Level"
    )
    word_count: int = Field(..., description="Total words counted")
    sentence_count: int = Field(..., description="Total sentences counted")
    syllable_count: int = Field(..., description="Total syllables counted")
    interpretation: str = Field(
        ..., description="Human-readable readability description"
    )


class BurdenTraceItem(BaseModel):
    """An itemized breakdown of clinical operational burden."""

    component: str = Field(
        ..., description="The name of the component, e.g. visits, procedures, forms"
    )
    count: int = Field(..., description="Occurrences of the component")
    weight: float = Field(..., description="Weight multiplier per occurrence")
    subtotal: float = Field(..., description="Subtotal burden (count * weight)")
    explanation: str = Field(..., description="Trace explanation of this component")


class BurdenTraceReport(BaseModel):
    """Patient operational burden trace score."""

    visit_burden: float = Field(..., description="Aggregated burden of patient visits")
    procedure_burden: float = Field(
        ..., description="Aggregated burden of clinical procedures"
    )
    activity_burden: float = Field(
        ..., description="Aggregated burden of CRFs and forms"
    )
    total_burden: float = Field(..., description="Total clinical burden score")
    trace: List[BurdenTraceItem] = Field(
        default_factory=list, description="Trace details explaining the sum"
    )


class AmendmentImpactReport(BaseModel):
    """Analysis of changes and cost estimates of a study amendment."""

    base_version: Optional[str] = Field(None, description="Parent/base version tag")
    amended_version: Optional[str] = Field(
        None, description="Current amended version tag"
    )
    added_forms_count: int = Field(0, description="Count of added forms")
    modified_forms_count: int = Field(0, description="Count of modified forms")
    deleted_forms_count: int = Field(0, description="Count of deleted forms")
    estimated_cost_usd: float = Field(
        0.0, description="Estimated total cost in USD for this amendment"
    )
    burden_change: float = Field(
        0.0, description="Burden index difference from base version"
    )
    explanation: str = Field(
        ..., description="Detailed text explanation of impact and costs"
    )


class AttritionStep(BaseModel):
    """Step in the patient population attrition funnel."""

    criterion_id: str = Field(..., description="The ID of the eligibility criterion")
    type: str = Field(..., description="inclusion or exclusion")
    description: str = Field(..., description="Description of the criterion")
    passed_count: int = Field(
        ..., description="Number of patients passing this criterion"
    )
    failed_count: int = Field(
        ..., description="Number of patients failing this criterion"
    )
    remaining_count: int = Field(
        ..., description="Number of patients continuing to the next step"
    )
    attrition_rate: float = Field(
        ..., description="Percentage of current cohort lost at this step"
    )


class FeasibilityReport(BaseModel):
    """Cohort-backed patient population feasibility and attrition rates."""

    starting_cohort_size: int = Field(..., description="Initial patient pool size")
    final_eligible_count: int = Field(
        ..., description="Number of fully eligible patients"
    )
    overall_eligibility_rate: float = Field(
        ..., description="Percentage of cohort that is eligible"
    )
    attrition_steps: List[AttritionStep] = Field(
        default_factory=list, description="Step-by-step funnel of attrition"
    )


class ProtocolQualityScore(BaseModel):
    """Protocol Quality Sentinel evaluation summary report.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    quality_score: float = Field(
        ..., description="Overall protocol quality score (0.0 to 100.0)"
    )
    patient_burden_index: float = Field(
        ..., description="Calculated patient operational burden score"
    )
    findings: List[QualityRuleFinding] = Field(
        default_factory=list, description="Quality findings"
    )
    passed: bool = Field(..., description="True if no ERROR severity findings exist")

    # Expanded sub-reports (Optional for backward compatibility)
    readability: Optional[ReadabilityReport] = Field(
        None, description="Readability metrics of narrative text blocks"
    )
    burden_details: Optional[BurdenTraceReport] = Field(
        None, description="Traceable operational burden details"
    )
    amendment_impact: Optional[AmendmentImpactReport] = Field(
        None, description="Amendment impact and cost estimation"
    )
    feasibility: Optional[FeasibilityReport] = Field(
        None, description="Pluggable patient population feasibility metrics"
    )
