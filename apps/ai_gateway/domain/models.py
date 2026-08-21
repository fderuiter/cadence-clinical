"""Domain models for AI Gateway routing, requests, and responses."""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class ModelTier(StrEnum):
    """Execution tiers for routing AI workloads based on cost and capability."""

    TIER_1_LOCAL = "tier_1_local"
    TIER_2_FAST = "tier_2_fast"
    TIER_3_FRONTIER = "tier_3_frontier"


class MessageRole(StrEnum):
    """Standard message roles for conversational or structured prompts."""

    SYSTEM = "system"
    USER = "user"
    ASSISTANT = "assistant"


class ChatMessage(BaseModel):
    """Individual prompt message in a conversation or completion context."""

    role: MessageRole | str = Field(
        default=MessageRole.USER,
        description="Role of the message author.",
    )
    content: str = Field(
        ...,
        description="Text content of the message.",
    )


class GenerationRequest(BaseModel):
    """Request payload for structured or unstructured text generation."""

    prompt: str | None = Field(
        default=None,
        description="Raw prompt string for single-turn completions.",
    )
    messages: list[ChatMessage] | None = Field(
        default=None,
        description="Structured list of conversational messages.",
    )
    tier: ModelTier = Field(
        default=ModelTier.TIER_2_FAST,
        description="Desired model routing tier.",
    )
    model_override: str | None = Field(
        default=None,
        description="Optional explicit model name overriding tier defaults.",
    )
    temperature: float = Field(
        default=0.0,
        ge=0.0,
        le=2.0,
        description="Sampling temperature. Defaults to 0.0 for deterministic outputs.",
    )
    max_tokens: int | None = Field(
        default=None,
        ge=1,
        description="Maximum tokens to generate in completion.",
    )
    response_schema: dict[str, Any] | None = Field(
        default=None,
        description="JSON Schema for Pydantic v2 Structured Outputs mode.",
    )
    tenant_id: str | None = Field(
        default=None,
        description="Tenant identifier for multi-tenant isolation and cost accounting.",
    )
    study_id: str | None = Field(
        default=None,
        description="Clinical study scope identifier.",
    )


class TokenUsage(BaseModel):
    """Token consumption accounting for an inference request."""

    prompt_tokens: int = Field(default=0, description="Tokens in input prompt.")
    completion_tokens: int = Field(default=0, description="Tokens in output.")
    total_tokens: int = Field(default=0, description="Total tokens consumed.")


class GenerationResponse(BaseModel):
    """Response payload containing generated content and execution telemetry."""

    content: str = Field(
        ...,
        description="Raw generated text or serialized JSON string.",
    )
    structured_data: dict[str, Any] | None = Field(
        default=None,
        description="Parsed JSON payload if response_schema was enforced.",
    )
    model: str = Field(
        ...,
        description="Exact model identifier that executed the inference.",
    )
    tier: ModelTier = Field(
        ...,
        description="Resolved model tier.",
    )
    usage: TokenUsage = Field(
        default_factory=TokenUsage,
        description="Token consumption metrics.",
    )
    latency_ms: float = Field(
        default=0.0,
        description="Execution latency in milliseconds.",
    )


class EmbeddingRequest(BaseModel):
    """Request payload for vector embedding generation."""

    input_texts: list[str] = Field(
        ...,
        min_length=1,
        description="List of strings to embed into dense vectors.",
    )
    tier: ModelTier = Field(
        default=ModelTier.TIER_1_LOCAL,
        description="Embedding model tier. Defaults to Tier 1 local embeddings.",
    )
    model_override: str | None = Field(
        default=None,
        description="Optional explicit embedding model name.",
    )
    tenant_id: str | None = Field(
        default=None,
        description="Tenant identifier.",
    )


class EmbeddingResponse(BaseModel):
    """Response payload containing generated dense vector embeddings."""

    embeddings: list[list[float]] = Field(
        ...,
        description="List of float vector embeddings matching input text order.",
    )
    model: str = Field(
        ...,
        description="Exact embedding model identifier used.",
    )
    tier: ModelTier = Field(
        ...,
        description="Resolved model tier.",
    )
    usage: TokenUsage = Field(
        default_factory=TokenUsage,
        description="Token consumption metrics.",
    )
    latency_ms: float = Field(
        default=0.0,
        description="Execution latency in milliseconds.",
    )


class TierInfo(BaseModel):
    """Descriptive metadata and active model mapping for an AI execution tier."""

    tier: ModelTier = Field(..., description="Tier identifier.")
    default_model: str = Field(..., description="Default model string.")
    description: str = Field(..., description="Clinical purpose and SLA.")
    capabilities: list[str] = Field(
        default_factory=list,
        description="Supported capabilities (e.g. structured_outputs, vision, embeddings).",
    )
