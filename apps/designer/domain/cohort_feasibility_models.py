"""
Domain contracts and DTO models for Autonomous Agentic Multi-Criteria Cohort Reasoning & Synthetic Feasibility Engine.

Conforms to FDA 21 CFR Part 11 and CDISC GxP requirements.
"""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field

from apps.designer.domain.eligibility.models import (
    EligibilityCriterion,
    NodeEvaluation,
)
from packages.compliance.services.protocol_rag import ProtocolRAGContextChunk


class CriteriaBreakdownSummary(BaseModel):
    """Summary of criteria evaluation results for a sub-cohort or participant group."""

    criterion_id: str = Field(
        ..., description="Unique criterion identifier, e.g., 'INC_01'"
    )
    identifier: str = Field(..., description="Business identifier, e.g., 'INC-001'")
    human_readable_text: str = Field(..., description="Human readable description")
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ..., description="Type of criterion"
    )
    passed_count: int = Field(0, description="Number of subjects satisfying criterion")
    failed_count: int = Field(0, description="Number of subjects failing criterion")
    indeterminate_count: int = Field(
        0,
        description="Number of subjects with indeterminate status due to missing data",
    )
    pass_rate_percentage: float = Field(
        0.0, description="Percentage of evaluated subjects meeting criterion"
    )


class SubCohortRecommendation(BaseModel):
    """Multi-criteria sub-cohort recommendation with specific criteria breakdown and fit percentage."""

    sub_cohort_id: str = Field(
        ...,
        description="Unique identifier for sub-cohort, e.g., 'subcohort_renal_adjusted'",
    )
    name: str = Field(..., description="Human-readable sub-cohort name")
    description: str = Field(
        ..., description="Clinical justification and definition summary"
    )
    estimated_fit_percentage: float = Field(
        ...,
        description="Estimated percentage of target patient pool fitting this sub-cohort",
    )
    criteria_breakdown: list[CriteriaBreakdownSummary] = Field(
        default_factory=list, description="Specific breakdown summary across criteria"
    )
    sample_patient_ids: list[str] = Field(
        default_factory=list,
        description="Representative sample patient identifiers (or synthetic profile IDs)",
    )


class NodeFitExplanation(BaseModel):
    """Granular node-level trace explanation for AST criteria evaluation."""

    subject_id: str = Field(..., description="Subject or synthetic profile ID")
    criterion_id: str = Field(..., description="Evaluated criterion identifier")
    criterion_name: str = Field(..., description="Name or description of criterion")
    result_status: Literal["TRUE", "FALSE", "INDETERMINATE"] = Field(
        ...,
        description="Kleene 3VL outcome: TRUE (met), FALSE (failed), or INDETERMINATE (missing data)",
    )
    node_trace: NodeEvaluation = Field(
        ..., description="Granular recursive AST node evaluation trace"
    )
    missing_fields: list[str] = Field(
        default_factory=list,
        description="Captured fields that were missing or null during evaluation",
    )
    explanation: str = Field(..., description="Detailed human-readable fit explanation")


class SyntheticSimulationSummary(BaseModel):
    """Summary of synthetic patient profile feasibility simulation under missing data scenarios."""

    simulated_subject_count: int = Field(
        ..., description="Total synthetic subject profiles generated and evaluated"
    )
    complete_data_fit_rate: float = Field(
        ...,
        description="Eligibility percentage under complete observation data (0.0 - 100.0)",
    )
    partial_data_fit_rate: float = Field(
        ...,
        description="Eligibility percentage under partial observation data scenarios",
    )
    missing_data_fit_rate: float = Field(
        ..., description="Eligibility percentage when key fields are missing"
    )
    indeterminate_rate: float = Field(
        ...,
        description="Percentage of simulated evaluations resulting in INDETERMINATE state",
    )
    high_risk_missing_fields: list[str] = Field(
        default_factory=list,
        description="Fields whose absence causes eligibility to shift from TRUE to INDETERMINATE/UNKNOWN",
    )


class CohortFeasibilityRequest(BaseModel):
    """Inbound request payload for multi-criteria cohort reasoning and synthetic feasibility simulation."""

    analysis_id: str | None = Field(
        default=None, description="Optional caller-supplied job/analysis ID"
    )
    study_id: str = Field(
        ..., description="Target protocol study ID, e.g., 'PROTOCOL-2026-T2D'"
    )
    cohort_id: str | None = Field(
        default="MAIN_COHORT", description="Target cohort or arm identifier"
    )
    unstructured_protocol_text: str | list[ProtocolRAGContextChunk] | None = Field(
        default=None,
        description="Unstructured protocol excerpts or RAG chunks for semantic vector matching",
    )
    structured_criteria: list[EligibilityCriterion] = Field(
        default_factory=list,
        description="Structured inclusion and exclusion criteria AST definitions",
    )
    patient_profiles: list[dict[str, Any]] | None = Field(
        default=None,
        description="Optional patient observation data contexts. If omitted, synthetic profiles are generated.",
    )
    synthetic_simulation_count: int = Field(
        default=20,
        ge=1,
        le=500,
        description="Number of GxP synthetic subject profiles to generate for feasibility simulation",
    )
    missing_data_scenarios: list[str] | None = Field(
        default_factory=lambda: ["baseline_labs", "biomarker_status"],
        description="Optional specific field groups to simulate missing data conditions",
    )
    is_async: bool = Field(
        default=True,
        description="Whether to execute asynchronously in a background worker",
    )
    compliance_profile: str = Field(
        default="HIPAA",
        description="De-identification compliance profile (HIPAA or GXP)",
    )


class CohortFeasibilityReport(BaseModel):
    """Comprehensive multi-criteria cohort reasoning and synthetic feasibility report."""

    job_id: str = Field(..., description="Unique analysis job identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    cohort_id: str | None = Field(default=None, description="Target cohort identifier")
    status: Literal["PENDING", "RUNNING", "COMPLETED", "FAILED", "DUPLICATE_LOCKED"] = (
        Field(..., description="Execution status of the cohort feasibility job")
    )
    starting_cohort_size: int = Field(
        ..., description="Total patient profiles evaluated"
    )
    eligible_count: int = Field(
        ...,
        description="Number of subjects fully eligible (all inclusion TRUE, exclusion FALSE)",
    )
    ineligible_count: int = Field(
        ...,
        description="Number of subjects ineligible (any inclusion FALSE or exclusion TRUE)",
    )
    indeterminate_count: int = Field(
        ..., description="Number of subjects with INDETERMINATE overall outcome"
    )
    overall_eligibility_rate: float = Field(
        ..., description="Overall eligibility percentage (0.0 - 100.0)"
    )
    estimated_fit_percentage: float = Field(
        ..., description="Estimated population fit percentage"
    )
    sub_cohort_recommendations: list[SubCohortRecommendation] = Field(
        default_factory=list,
        description="Multi-criteria sub-cohort recommendations with breakdown summaries",
    )
    node_explanations: list[NodeFitExplanation] = Field(
        default_factory=list,
        description="Granular node-level fit trace explanations for AST evaluations",
    )
    synthetic_simulation_summary: SyntheticSimulationSummary | None = Field(
        default=None,
        description="Feasibility simulation summary across complete, partial, and missing data scenarios",
    )
    deid_applied: bool = Field(
        default=True,
        description="True if de-identification scrubbing was applied to AI prompts",
    )
    deid_tokens_count: int = Field(
        default=0, description="Count of PII/PHI tokens scrubbed"
    )
    advisory_lock_acquired: bool = Field(
        default=True,
        description="True if background PostgreSQL advisory lock was successfully acquired",
    )
    execution_time_seconds: float = Field(
        default=0.0, description="Total execution time in seconds"
    )
    error_message: str | None = Field(
        default=None, description="Error message if job status is FAILED"
    )


class CohortFeasibilityJob(BaseModel):
    """Asynchronous background job status wrapper."""

    job_id: str = Field(..., description="Job identifier")
    study_id: str = Field(..., description="Study ID")
    cohort_id: str | None = Field(..., description="Cohort ID")
    status: Literal["PENDING", "RUNNING", "COMPLETED", "FAILED", "DUPLICATE_LOCKED"] = (
        Field(..., description="Job status")
    )
    created_at: str = Field(..., description="ISO timestamp when job was enqueued")
    report: CohortFeasibilityReport | None = Field(
        default=None, description="Feasibility report when status is COMPLETED"
    )


__all__ = [
    "CohortFeasibilityJob",
    "CohortFeasibilityReport",
    "CohortFeasibilityRequest",
    "CriteriaBreakdownSummary",
    "NodeFitExplanation",
    "SubCohortRecommendation",
    "SyntheticSimulationSummary",
]
