"""Mock AI engine adapter for deterministic zero-IO testing."""

import json
from typing import Any

from apps.ai_gateway.domain.models import (
    EmbeddingRequest,
    EmbeddingResponse,
    GenerationRequest,
    GenerationResponse,
    ModelTier,
    TierInfo,
    TokenUsage,
    ToolCall,
    ToolCallFunction,
)
from apps.ai_gateway.domain.ports import AIEnginePort


class MockAIEngineAdapter(AIEnginePort):
    """Deterministic mock adapter for unit tests and local development."""

    def __init__(self) -> None:
        self.generation_calls: list[GenerationRequest] = []
        self.embedding_calls: list[EmbeddingRequest] = []
        self.custom_responses: dict[str, str] = {}
        self.custom_tool_calls: dict[str, list[ToolCall]] = {}

    def set_mock_response(self, prompt_substring: str, response: str) -> None:
        """Register a canned response triggered by a prompt substring."""
        self.custom_responses[prompt_substring] = response

    def set_mock_tool_calls(
        self, prompt_substring: str, tool_calls: list[ToolCall]
    ) -> None:
        """Register canned tool call selections triggered by a prompt substring."""
        self.custom_tool_calls[prompt_substring] = tool_calls

    async def generate(self, request: GenerationRequest) -> GenerationResponse:
        """Return a simulated generation response."""
        self.generation_calls.append(request)

        # Check for custom matching response
        content = "Simulated clinical AI completion."
        prompt_text = (
            request.prompt
            if request.prompt is not None
            else (
                " ".join(m.content for m in request.messages if m.content)
                if request.messages
                else ""
            )
        )

        for key, value in self.custom_responses.items():
            if key in prompt_text:
                content = value
                break

        # Check for custom or synthesized tool calls
        tool_calls: list[ToolCall] | None = None
        for key, tc_list in self.custom_tool_calls.items():
            if key in prompt_text:
                tool_calls = tc_list
                break

        if tool_calls is None and request.tools:
            first_tool = request.tools[0]
            synth_args: dict[str, Any] = {}
            if (
                first_tool.function.parameters
                and "properties" in first_tool.function.parameters
            ):
                for (
                    p_name,
                    p_def,
                ) in first_tool.function.parameters["properties"].items():
                    p_type = p_def.get("type", "string")
                    if p_type == "string":
                        synth_args[p_name] = f"mock_{p_name}"
                    elif p_type in ("integer", "number"):
                        synth_args[p_name] = 101
                    elif p_type == "boolean":
                        synth_args[p_name] = True
                    else:
                        synth_args[p_name] = "mock_val"
            else:
                synth_args = {"query": "mock_query"}

            tool_calls = [
                ToolCall(
                    id="call_mock_12345",
                    type="function",
                    function=ToolCallFunction(
                        name=first_tool.function.name,
                        arguments=synth_args,
                    ),
                )
            ]

        # If a structured schema is requested and no custom content matched, synthesize JSON
        structured_data: dict[str, Any] | None = None
        if request.response_schema is not None:
            if content == "Simulated clinical AI completion.":
                # Synthesize dummy dictionary based on schema
                properties = request.response_schema.get("properties", {})
                synth: dict[str, Any] = {}
                for prop_name, prop_def in properties.items():
                    prop_type = prop_def.get("type", "string")
                    if prop_type == "string":
                        synth[prop_name] = f"mock_{prop_name}"
                    elif prop_type in ("integer", "number"):
                        synth[prop_name] = 42
                    elif prop_type == "boolean":
                        synth[prop_name] = True
                    elif prop_type == "array":
                        synth[prop_name] = ["item_1", "item_2"]
                    else:
                        synth[prop_name] = {}
                structured_data = synth
                content = json.dumps(synth)
            else:
                try:
                    structured_data = json.loads(content)
                except json.JSONDecodeError:
                    structured_data = None

        model_name = request.model_override or f"mock-{request.tier.value}"

        return GenerationResponse(
            content=content,
            structured_data=structured_data,
            tool_calls=tool_calls,
            model=model_name,
            tier=request.tier,
            usage=TokenUsage(
                prompt_tokens=25,
                completion_tokens=50,
                total_tokens=75,
            ),
            latency_ms=12.5,
        )

    async def embed(self, request: EmbeddingRequest) -> EmbeddingResponse:
        """Return simulated dense vectors."""
        self.embedding_calls.append(request)

        embeddings: list[list[float]] = []
        for idx, _ in enumerate(request.input_texts):
            # Deterministic 384-dimension pseudo-vector
            val = (idx + 1.0) / 100.0
            vector = [val] * 384
            embeddings.append(vector)

        model_name = request.model_override or f"mock-embed-{request.tier.value}"

        return EmbeddingResponse(
            embeddings=embeddings,
            model=model_name,
            tier=request.tier,
            usage=TokenUsage(
                prompt_tokens=len(request.input_texts) * 8,
                completion_tokens=0,
                total_tokens=len(request.input_texts) * 8,
            ),
            latency_ms=5.0,
        )

    def get_tier_info(self) -> list[TierInfo]:
        """Return mock tier descriptors."""
        return [
            TierInfo(
                tier=ModelTier.TIER_1_LOCAL,
                default_model="mock-tier-1-local",
                description="Mock Local SLM Tier.",
                capabilities=["embeddings", "classification"],
            ),
            TierInfo(
                tier=ModelTier.TIER_2_FAST,
                default_model="mock-tier-2-fast",
                description="Mock Fast Tier.",
                capabilities=["chat", "structured_outputs"],
            ),
            TierInfo(
                tier=ModelTier.TIER_3_FRONTIER,
                default_model="mock-tier-3-frontier",
                description="Mock Frontier Tier.",
                capabilities=["reasoning", "protocol_synthesis"],
            ),
        ]
