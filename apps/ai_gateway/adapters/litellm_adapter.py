"""LiteLLM adapter implementing the AIEnginePort for the AI Gateway."""

import json
import logging
import os
import time
from typing import Any

import litellm

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

logger = logging.getLogger(__name__)


class LiteLLMAdapter(AIEnginePort):
    """Hexagonal adapter wrapping LiteLLM for multi-tier model routing and execution."""

    def __init__(
        self,
        tier_1_model: str | None = None,
        tier_2_model: str | None = None,
        tier_3_model: str | None = None,
        tier_1_embed_model: str | None = None,
    ) -> None:
        self.tier_1_model: str = (
            tier_1_model or os.getenv("AI_TIER_1_MODEL") or "ollama/llama3"
        )
        self.tier_2_model: str = (
            tier_2_model or os.getenv("AI_TIER_2_MODEL") or "gpt-4o-mini"
        )
        self.tier_3_model: str = (
            tier_3_model or os.getenv("AI_TIER_3_MODEL") or "gpt-4o"
        )
        self.tier_1_embed_model: str = (
            tier_1_embed_model
            or os.getenv("AI_TIER_1_EMBED_MODEL")
            or "text-embedding-3-small"
        )

        # Suppress verbose external telemetry logs
        litellm.telemetry = False

    def resolve_model(
        self,
        tier: ModelTier,
        override: str | None = None,
        is_embedding: bool = False,
    ) -> str:
        """Resolve the exact model string based on tier and optional override."""
        if override:
            return override

        if is_embedding:
            return self.tier_1_embed_model

        match tier:
            case ModelTier.TIER_1_LOCAL:
                return self.tier_1_model
            case ModelTier.TIER_2_FAST:
                return self.tier_2_model
            case ModelTier.TIER_3_FRONTIER:
                return self.tier_3_model
            case _:
                return self.tier_2_model

    async def generate(self, request: GenerationRequest) -> GenerationResponse:
        """Execute a text generation or structured completion via LiteLLM."""
        model = self.resolve_model(request.tier, request.model_override)
        start_time = time.perf_counter()

        # Build messages payload
        messages: list[dict[str, str]] = []
        if request.messages:
            messages = [
                {"role": str(m.role), "content": m.content} for m in request.messages
            ]
        elif request.prompt:
            messages = [{"role": "user", "content": request.prompt}]
        else:
            raise ValueError("Either 'prompt' or 'messages' must be provided.")

        kwargs: dict[str, Any] = {
            "model": model,
            "messages": messages,
            "temperature": request.temperature,
        }

        if request.max_tokens is not None:
            kwargs["max_tokens"] = request.max_tokens

        # Support JSON mode / Structured Outputs
        if request.response_schema is not None:
            kwargs["response_format"] = {
                "type": "json_schema",
                "json_schema": {
                    "name": "response_schema",
                    "schema": request.response_schema,
                },
            }

        try:
            response = await litellm.acompletion(**kwargs)
        except Exception as err:
            logger.error("LiteLLM generation failed for model %s: %s", model, err)
            raise RuntimeError(f"AI Generation failed: {err}") from err

        latency_ms = (time.perf_counter() - start_time) * 1000.0

        # Extract content
        choice = response.choices[0]
        content = choice.message.content or ""

        # Parse structured output if schema was supplied
        structured_data: dict[str, Any] | None = None
        if request.response_schema is not None and content:
            try:
                structured_data = json.loads(content)
            except json.JSONDecodeError:
                logger.warning("Failed to decode JSON from model response: %s", content)

        # Extract usage
        usage_data = getattr(response, "usage", None)
        usage = TokenUsage(
            prompt_tokens=getattr(usage_data, "prompt_tokens", 0) if usage_data else 0,
            completion_tokens=getattr(usage_data, "completion_tokens", 0)
            if usage_data
            else 0,
            total_tokens=getattr(usage_data, "total_tokens", 0) if usage_data else 0,
        )

        return GenerationResponse(
            content=content,
            structured_data=structured_data,
            model=model,
            tier=request.tier,
            usage=usage,
            latency_ms=round(latency_ms, 2),
        )

    async def embed(self, request: EmbeddingRequest) -> EmbeddingResponse:
        """Execute vector embeddings via LiteLLM."""
        model = self.resolve_model(
            request.tier, request.model_override, is_embedding=True
        )
        start_time = time.perf_counter()

        try:
            response = await litellm.aembedding(
                model=model,
                input=request.input_texts,
            )
        except Exception as err:
            logger.error("LiteLLM embedding failed for model %s: %s", model, err)
            raise RuntimeError(f"AI Embedding generation failed: {err}") from err

        latency_ms = (time.perf_counter() - start_time) * 1000.0

        embeddings = [item["embedding"] for item in response.data]

        usage_data = getattr(response, "usage", None)
        usage = TokenUsage(
            prompt_tokens=getattr(usage_data, "prompt_tokens", 0) if usage_data else 0,
            completion_tokens=0,
            total_tokens=getattr(usage_data, "total_tokens", 0) if usage_data else 0,
        )

        return EmbeddingResponse(
            embeddings=embeddings,
            model=model,
            tier=request.tier,
            usage=usage,
            latency_ms=round(latency_ms, 2),
        )

    def get_tier_info(self) -> list[TierInfo]:
        """Return active model tier definitions."""
        return [
            TierInfo(
                tier=ModelTier.TIER_1_LOCAL,
                default_model=self.tier_1_model,
                description="Local SLMs and local embedding models. Compute-only cost for high-volume tasks.",
                capabilities=["embeddings", "classification", "offline_inference"],
            ),
            TierInfo(
                tier=ModelTier.TIER_2_FAST,
                default_model=self.tier_2_model,
                description="Fast, cost-effective cloud LLMs for RAG, ticket triage, and OCR mapping.",
                capabilities=["chat", "structured_outputs", "vision", "rag"],
            ),
            TierInfo(
                tier=ModelTier.TIER_3_FRONTIER,
                default_model=self.tier_3_model,
                description="Frontier reasoning models for complex multi-stage protocol synthesis and safety narratives.",
                capabilities=[
                    "reasoning",
                    "multi_modal",
                    "protocol_synthesis",
                    "safety_narratives",
                ],
            ),
        ]
