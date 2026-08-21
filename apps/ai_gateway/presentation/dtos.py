"""Data Transfer Objects (DTOs) for AI Gateway presentation endpoints."""

from typing import Any

from pydantic import BaseModel, Field

from apps.ai_gateway.domain.models import MessageRole, ModelTier


class ChatMessageDTO(BaseModel):
    """Chat message transfer object."""

    role: MessageRole | str = Field(
        default=MessageRole.USER,
        description="Role of the message author.",
    )
    content: str = Field(
        ...,
        description="Text content of the message.",
    )


class GenerationRequestDTO(BaseModel):
    """Inbound DTO for AI text generation and structured completions."""

    prompt: str | None = Field(
        default=None,
        description="Single-turn prompt string.",
    )
    messages: list[ChatMessageDTO] | None = Field(
        default=None,
        description="Multi-turn conversation history.",
    )
    tier: ModelTier = Field(
        default=ModelTier.TIER_2_FAST,
        description="Execution tier (tier_1_local, tier_2_fast, tier_3_frontier).",
    )
    model_override: str | None = Field(
        default=None,
        description="Explicit model name override.",
    )
    temperature: float = Field(
        default=0.0,
        ge=0.0,
        le=2.0,
        description="Sampling temperature.",
    )
    max_tokens: int | None = Field(
        default=None,
        ge=1,
        description="Maximum tokens to generate.",
    )
    response_schema: dict[str, Any] | None = Field(
        default=None,
        description="JSON Schema for Pydantic v2 structured outputs.",
    )
    tenant_id: str | None = Field(
        default=None,
        description="Tenant identifier for isolation and auditing.",
    )
    study_id: str | None = Field(
        default=None,
        description="Study protocol scope identifier.",
    )


class TokenUsageDTO(BaseModel):
    """Token consumption accounting transfer object."""

    prompt_tokens: int = Field(default=0, description="Input prompt tokens.")
    completion_tokens: int = Field(default=0, description="Generated output tokens.")
    total_tokens: int = Field(default=0, description="Total tokens consumed.")


class GenerationResponseDTO(BaseModel):
    """Outbound DTO containing completion results and execution telemetry."""

    content: str = Field(..., description="Generated text or serialized JSON.")
    structured_data: dict[str, Any] | None = Field(
        default=None,
        description="Structured JSON object if response_schema was requested.",
    )
    model: str = Field(..., description="Active model identifier.")
    tier: ModelTier = Field(..., description="Resolved model tier.")
    usage: TokenUsageDTO = Field(
        default_factory=TokenUsageDTO,
        description="Token usage metrics.",
    )
    latency_ms: float = Field(
        default=0.0,
        description="Request latency in milliseconds.",
    )


class EmbeddingRequestDTO(BaseModel):
    """Inbound DTO for vector embedding generation."""

    input_texts: list[str] = Field(
        ...,
        min_length=1,
        description="Batch of text strings to embed.",
    )
    tier: ModelTier = Field(
        default=ModelTier.TIER_1_LOCAL,
        description="Embedding model tier (defaults to Tier 1 local).",
    )
    model_override: str | None = Field(
        default=None,
        description="Optional model override.",
    )
    tenant_id: str | None = Field(
        default=None,
        description="Tenant identifier.",
    )


class EmbeddingResponseDTO(BaseModel):
    """Outbound DTO containing generated float vector embeddings."""

    embeddings: list[list[float]] = Field(
        ...,
        description="List of dense float vectors.",
    )
    model: str = Field(..., description="Active embedding model.")
    tier: ModelTier = Field(..., description="Resolved model tier.")
    usage: TokenUsageDTO = Field(
        default_factory=TokenUsageDTO,
        description="Token usage metrics.",
    )
    latency_ms: float = Field(
        default=0.0,
        description="Request latency in milliseconds.",
    )


class TierInfoDTO(BaseModel):
    """Outbound DTO describing active model tier configurations."""

    tier: ModelTier = Field(..., description="Tier identifier.")
    default_model: str = Field(..., description="Default model string.")
    description: str = Field(..., description="Clinical purpose.")
    capabilities: list[str] = Field(
        default_factory=list,
        description="Supported capabilities.",
    )
