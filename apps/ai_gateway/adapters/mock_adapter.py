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
)
from apps.ai_gateway.domain.ports import AIEnginePort


class MockAIEngineAdapter(AIEnginePort):
    """Deterministic mock adapter for unit tests and local development."""

    def __init__(self) -> None:
        self.generation_calls: list[GenerationRequest] = []
        self.embedding_calls: list[EmbeddingRequest] = []
        self.custom_responses: dict[str, str] = {}

    def set_mock_response(self, prompt_substring: str, response: str) -> None:
        """Register a canned response triggered by a prompt substring."""
        self.custom_responses[prompt_substring] = response

    async def generate(self, request: GenerationRequest) -> GenerationResponse:
        """Return a simulated generation response."""
        self.generation_calls.append(request)

        # Check for custom matching response
        content = "Simulated clinical AI completion."
        prompt_text = request.prompt or (
            request.messages[-1].content if request.messages else ""
        )

        for key, value in self.custom_responses.items():
            if key in prompt_text:
                content = value
                break

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
