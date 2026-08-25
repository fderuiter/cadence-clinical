"""
Domain models for Hybrid Multi-Step Clinical Reasoning Core.

Enforces:
- Multi-step planning loops for protocol synthesis & patient record reasoning.
- In-flight PHI masking via temporary surrogate tokens.
- Grounded citation verification with 85% confidence threshold gate.
- Caller-managed stateless execution state context.
- Staging generated outputs into draft state requiring human e-signature.
"""

from __future__ import annotations

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field

from packages.compliance.services.protocol_rag import (
    ParsedCitation,
    ProtocolRAGContextChunk,
)


class ReasoningStepType(StrEnum):
    """Types of steps in a multi-step clinical reasoning planning loop."""

    PLANNING = "planning"
    EXTRACTION = "extraction"
    SYNTHESIS = "synthesis"
    VERIFICATION = "verification"
    DRAFT_STAGING = "draft_staging"


class ReasoningStepState(BaseModel):
    """Representation of an individual step in the multi-step planning loop."""

    step_index: int = Field(
        ..., ge=0, description="0-indexed step number in reasoning loop."
    )
    step_type: ReasoningStepType | str = Field(
        ..., description="Category/type of reasoning step."
    )
    title: str = Field(..., description="Human-readable title of step.")
    input_summary: str = Field(..., description="Summary of inputs passed into step.")
    output_text: str = Field(
        ..., description="Output generated during this reasoning step."
    )
    status: str = Field(
        default="COMPLETED",
        description="Execution status ('PENDING', 'IN_PROGRESS', 'COMPLETED', 'FAILED').",
    )
    has_phi_masked: bool = Field(
        default=False,
        description="True if patient identifiers were masked in this step.",
    )
    masked_tokens_count: int = Field(
        default=0,
        description="Count of surrogate tokens replaced during step execution.",
    )
    step_citations: list[ParsedCitation] = Field(
        default_factory=list,
        description="Protocol citations extracted/verified in this step.",
    )


class ExecutionStateContext(BaseModel):
    """Caller-managed execution state context enabling stateless microservice persistence."""

    session_id: str = Field(
        ..., description="Unique reasoning session correlation identifier."
    )
    current_step_index: int = Field(
        default=0, ge=0, description="Index of the active reasoning step."
    )
    total_steps: int = Field(
        default=5, ge=1, description="Total planned steps in the reasoning workflow."
    )
    completed_steps: list[ReasoningStepState] = Field(
        default_factory=list,
        description="List of previously completed reasoning steps.",
    )
    patient_context: list[dict[str, Any]] | dict[str, Any] | str | None = Field(
        default=None, description="Optional raw or masked patient record context."
    )
    protocol_chunks: list[ProtocolRAGContextChunk] = Field(
        default_factory=list, description="Target clinical protocol context chunks."
    )
    metadata: dict[str, Any] = Field(
        default_factory=dict,
        description="Arbitrary caller-supplied execution metadata.",
    )


class ClinicalReasoningRequest(BaseModel):
    """Inbound request for multi-step clinical trial reasoning."""

    query: str = Field(
        ..., min_length=3, description="Complex clinical trial reasoning query."
    )
    tenant_id: str = Field(..., description="Mandatory tenant isolation identifier.")
    study_id: str | None = Field(default=None, description="Target study identifier.")
    protocol_chunks: list[ProtocolRAGContextChunk] = Field(
        default_factory=list,
        description="Clinical trial protocol chunks for citation verification.",
    )
    patient_records: list[dict[str, Any]] | dict[str, Any] | str | None = Field(
        default=None,
        description="Patient records or clinical context containing sensitive identifiers.",
    )
    execution_state: ExecutionStateContext | None = Field(
        default=None,
        description="Caller-managed execution state context for stateless continuation.",
    )
    min_citation_confidence: float = Field(
        default=0.85,
        ge=0.0,
        le=1.0,
        description="Minimum required citation confidence score (default 0.85).",
    )
    compliance_profile: str = Field(
        default="HIPAA",
        description="Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR).",
    )
    esignature_context: dict[str, Any] | None = Field(
        default=None,
        description="Electronic signature & audit context passed across execution steps.",
    )


class DraftClinicalRecord(BaseModel):
    """Generated clinical trial record staged in draft state requiring human review."""

    record_id: str = Field(..., description="Unique draft record identifier.")
    title: str = Field(..., description="Draft clinical record title.")
    study_id: str | None = Field(default=None, description="Associated study ID.")
    status: str = Field(
        default="DRAFT_STAGED",
        description="Draft state status requiring explicit human review/signature before release.",
    )
    synthesized_guidelines: str = Field(
        ..., description="Synthesized clinical guidelines and protocol analysis."
    )
    eligibility_criteria: list[str] = Field(
        default_factory=list, description="Extracted eligibility criteria."
    )
    citations: list[ParsedCitation] = Field(
        default_factory=list, description="Grounded verbatim protocol citations."
    )
    requires_esignature: bool = Field(
        default=True,
        description="Mandates explicit human review and e-signature authorization.",
    )
    created_by_agent: str = Field(
        default="HybridClinicalReasoningAgent/v1",
        description="Agent version identifier.",
    )
    created_at: str = Field(..., description="ISO 8601 creation timestamp.")


class ClinicalReasoningResponse(BaseModel):
    """Outbound response containing staged draft record, reasoning chain, and telemetry."""

    status: str = Field(
        ...,
        description="Execution status ('DRAFT_STAGED', 'REJECTED_LOW_CONFIDENCE', 'COMPLETED', 'ERROR').",
    )
    draft_record: DraftClinicalRecord | None = Field(
        default=None,
        description="Staged draft clinical record (None if rejected or low confidence).",
    )
    reasoning_chain: list[ReasoningStepState] = Field(
        default_factory=list,
        description="Discrete multi-step planning loop execution history.",
    )
    citations: list[ParsedCitation] = Field(
        default_factory=list, description="Extracted & verified protocol citations."
    )
    overall_faithfulness_score: float = Field(
        ..., description="Overall citation confidence score (0.0 to 1.0)."
    )
    is_citation_verified: bool = Field(
        ..., description="True if overall faithfulness >= min_citation_confidence."
    )
    phi_masking_applied: bool = Field(
        ..., description="True if PHI masking was applied to intermediate steps."
    )
    phi_tokens_masked: int = Field(
        ..., description="Total surrogate PHI tokens masked during execution."
    )
    execution_state: ExecutionStateContext = Field(
        ..., description="Updated caller-managed execution state context."
    )
    model_used: str = Field(
        default="tier-3-frontier-reasoning", description="AI model used."
    )
    latency_ms: float = Field(default=0.0, description="Latency in milliseconds.")


__all__ = [
    "ClinicalReasoningRequest",
    "ClinicalReasoningResponse",
    "DraftClinicalRecord",
    "ExecutionStateContext",
    "ReasoningStepState",
    "ReasoningStepType",
]
