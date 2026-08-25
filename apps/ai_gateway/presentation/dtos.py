"""Data Transfer Objects (DTOs) for AI Gateway presentation endpoints."""

from typing import Any

from pydantic import BaseModel, Field

from apps.ai_gateway.domain.models import MessageRole, ModelTier
from apps.ai_gateway.domain.reasoning import (
    ClinicalReasoningRequest,
    ClinicalReasoningResponse,
    DraftClinicalRecord,
    ExecutionStateContext,
    ReasoningStepState,
    ReasoningStepType,
)


class FunctionDefinitionDTO(BaseModel):
    """Function specification transfer object."""

    name: str = Field(..., description="Name of the function to be called.")
    description: str | None = Field(
        default=None,
        description="Description of function capability for the model.",
    )
    parameters: dict[str, Any] | None = Field(
        default=None,
        description="JSON Schema for function arguments.",
    )


class ToolDefinitionDTO(BaseModel):
    """Tool definition transfer object."""

    type: str = Field(
        default="function",
        description="Type of tool. Defaults to 'function'.",
    )
    function: FunctionDefinitionDTO = Field(
        ...,
        description="Function declaration details.",
    )


class ToolCallFunctionDTO(BaseModel):
    """Function call details transfer object."""

    name: str = Field(..., description="Name of the requested function.")
    arguments: str | dict[str, Any] = Field(
        ...,
        description="Raw JSON string or parsed dictionary of arguments.",
    )


class ToolCallDTO(BaseModel):
    """Structured tool call transfer object."""

    id: str = Field(..., description="Unique tool call invocation identifier.")
    type: str = Field(
        default="function",
        description="Tool type.",
    )
    function: ToolCallFunctionDTO = Field(
        ...,
        description="Function call payload.",
    )


class ToolResponseDTO(BaseModel):
    """Structured result transfer object for tool call execution."""

    tool_call_id: str = Field(..., description="Matching ID of the tool call request.")
    content: str | dict[str, Any] = Field(
        ...,
        description="Result content string or structured dictionary from tool execution.",
    )
    name: str | None = Field(
        default=None, description="Optional name of the tool executed."
    )


class ChatMessageDTO(BaseModel):
    """Chat message transfer object."""

    role: MessageRole | str = Field(
        default=MessageRole.USER,
        description="Role of the message author.",
    )
    content: str | None = Field(
        default=None,
        description="Text content of the message.",
    )
    tool_calls: list[ToolCallDTO] | None = Field(
        default=None,
        description="Optional list of tool calls emitted by assistant.",
    )
    tool_call_id: str | None = Field(
        default=None,
        description="ID of tool call this message responds to (for tool role).",
    )
    name: str | None = Field(
        default=None,
        description="Optional author or tool name.",
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
    tools: list[ToolDefinitionDTO] | None = Field(
        default=None,
        description="List of tool definitions available for model tool selection.",
    )
    tool_choice: str | dict[str, Any] | None = Field(
        default=None,
        description="Tool choice configuration (e.g. 'auto', 'required', or explicit selection).",
    )
    tool_responses: list[ToolResponseDTO] | None = Field(
        default=None,
        description="List of execution responses for previously requested tool calls.",
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
    custom_terms: list[str] | None = Field(
        default=None,
        description="Optional custom literal terms or patient names to redact.",
    )
    enable_deid: bool = Field(
        default=True,
        description="Whether to apply in-flight de-identification air-gap.",
    )
    compliance_profile: str = Field(
        default="HIPAA",
        description="Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR).",
    )


class TokenUsageDTO(BaseModel):
    """Token consumption accounting transfer object."""

    prompt_tokens: int = Field(default=0, description="Input prompt tokens.")
    completion_tokens: int = Field(default=0, description="Generated output tokens.")
    total_tokens: int = Field(default=0, description="Total tokens consumed.")


class GenerationResponseDTO(BaseModel):
    """Outbound DTO containing completion results and execution telemetry."""

    content: str | None = Field(
        default="", description="Generated text or serialized JSON."
    )
    structured_data: dict[str, Any] | None = Field(
        default=None,
        description="Structured JSON object if response_schema was requested.",
    )
    tool_calls: list[ToolCallDTO] | None = Field(
        default=None,
        description="Structured tool call requests emitted by the model.",
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
    deid_applied: bool = Field(
        default=False,
        description="Whether in-flight de-identification was applied.",
    )
    deid_tokens_count: int = Field(
        default=0,
        description="Count of surrogate tokens replaced and re-hydrated.",
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
    custom_terms: list[str] | None = Field(
        default=None,
        description="Optional custom literal terms or patient names to redact.",
    )
    enable_deid: bool = Field(
        default=True,
        description="Whether to apply in-flight de-identification air-gap.",
    )
    compliance_profile: str = Field(
        default="HIPAA",
        description="Compliance profile for PHI detection (HIPAA, GDPR, EU_CTR).",
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
    deid_applied: bool = Field(
        default=False,
        description="Whether in-flight de-identification was applied to input texts.",
    )
    deid_tokens_count: int = Field(
        default=0,
        description="Count of surrogate tokens replaced in input texts.",
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


__all__ = [
    "ChatMessageDTO",
    "ClinicalReasoningRequest",
    "ClinicalReasoningResponse",
    "DraftClinicalRecord",
    "EmbeddingRequestDTO",
    "EmbeddingResponseDTO",
    "ExecutionStateContext",
    "GenerationRequestDTO",
    "GenerationResponseDTO",
    "ReasoningStepState",
    "ReasoningStepType",
    "TierInfoDTO",
    "TokenUsageDTO",
]
