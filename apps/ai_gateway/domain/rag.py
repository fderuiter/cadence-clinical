"""
Domain models, Tier 2 prompt templates, and faithfulness scoring for Grounded Protocol RAG.

Enforces verbatim citation markers: `[Protocol v2.1, Section 7.3, Page 42]` and mathematical
faithfulness evaluation per PRD-TCK-005, PRD-SYS-051, and ADR-2192.
"""

from __future__ import annotations

from pydantic import BaseModel, Field

from packages.compliance.services.protocol_rag import (
    CITATION_REGEX,
    CONFIDENCE_THRESHOLD,
    TIER_2_GROUNDED_RAG_SYSTEM_PROMPT,
    ParsedCitation,
    ProtocolRAGContextChunk,
    evaluate_rag_faithfulness,
    format_citation_marker,
    format_grounded_rag_prompt,
)


class ProtocolRAGRequest(BaseModel):
    """Inbound request to synthesize a citation-grounded response for a clinical inquiry."""

    query: str = Field(
        ..., min_length=3, description="Clinical inquiry text from user/ticket."
    )
    study_id: str = Field(..., description="Target study identifier.")
    protocol_version: str | None = Field(
        default=None, description="Optional target protocol version."
    )
    context_chunks: list[ProtocolRAGContextChunk] = Field(
        default_factory=list,
        description="Retrieved protocol chunks from Knowledge Hub.",
    )
    top_k: int = Field(default=5, ge=1, le=20)


class ProtocolRAGResponse(BaseModel):
    """Outbound response containing grounded answer, citations, and faithfulness telemetry."""

    answer: str = Field(
        ..., description="Generated clinical draft with citation markers."
    )
    citations: list[ParsedCitation] = Field(
        default_factory=list, description="Extracted citation references."
    )
    faithfulness_score: float = Field(
        ...,
        ge=0.0,
        le=1.0,
        description="Mathematical faithfulness score (0.0 - 1.0).",
    )
    is_grounded: bool = Field(..., description="True if faithfulness score >= 0.85.")
    confidence_tier: str = Field(
        ..., description="'HIGH' (>= 0.85) or 'LOW_FAIL_CLOSED' (< 0.85)."
    )
    model: str = Field(default="tier-2-clinical-rag", description="Model identifier.")
    latency_ms: float = Field(
        default=0.0, description="Execution latency in milliseconds."
    )


__all__ = [
    "CITATION_REGEX",
    "CONFIDENCE_THRESHOLD",
    "ParsedCitation",
    "ProtocolRAGContextChunk",
    "ProtocolRAGRequest",
    "ProtocolRAGResponse",
    "TIER_2_GROUNDED_RAG_SYSTEM_PROMPT",
    "evaluate_rag_faithfulness",
    "format_citation_marker",
    "format_grounded_rag_prompt",
]
