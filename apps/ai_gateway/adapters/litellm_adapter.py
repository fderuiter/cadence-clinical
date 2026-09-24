"""LiteLLM adapter implementing the AIEnginePort for the AI Gateway."""

import json
import logging
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
    ToolCall,
    ToolCallFunction,
)
from apps.ai_gateway.domain.ports import AIEnginePort
from packages.config import get_ai_gateway_settings

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
        settings = get_ai_gateway_settings()
        self.tier_1_model: str = (
            tier_1_model or settings.ai_tier_1_model or "ollama/llama3"
        )
        self.tier_2_model: str = (
            tier_2_model or settings.ai_tier_2_model or "gpt-4o-mini"
        )
        self.tier_3_model: str = tier_3_model or settings.ai_tier_3_model or "gpt-4o"
        self.tier_1_embed_model: str = (
            tier_1_embed_model
            or settings.ai_tier_1_embed_model
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
        messages: list[dict[str, Any]] = []
        if request.messages:
            for m in request.messages:
                m_dict: dict[str, Any] = {"role": str(m.role)}
                if m.content is not None:
                    m_dict["content"] = m.content
                else:
                    m_dict["content"] = ""
                if m.name is not None:
                    m_dict["name"] = m.name
                if m.tool_call_id is not None:
                    m_dict["tool_call_id"] = m.tool_call_id
                if m.tool_calls:
                    m_dict["tool_calls"] = [
                        {
                            "id": tc.id,
                            "type": tc.type,
                            "function": {
                                "name": tc.function.name,
                                "arguments": tc.function.arguments
                                if isinstance(tc.function.arguments, str)
                                else json.dumps(tc.function.arguments),
                            },
                        }
                        for tc in m.tool_calls
                    ]
                messages.append(m_dict)
        elif request.prompt:
            messages = [{"role": "user", "content": request.prompt}]
        elif request.tool_responses:
            messages = []
        else:
            raise ValueError(
                "Either 'prompt', 'messages', or 'tool_responses' must be provided."
            )

        if request.tool_responses:
            for tr in request.tool_responses:
                tr_content = (
                    tr.content
                    if isinstance(tr.content, str)
                    else json.dumps(tr.content)
                )
                tr_dict: dict[str, Any] = {
                    "role": "tool",
                    "tool_call_id": tr.tool_call_id,
                    "content": tr_content,
                }
                if tr.name:
                    tr_dict["name"] = tr.name
                messages.append(tr_dict)

        kwargs: dict[str, Any] = {
            "model": model,
            "messages": messages,
            "temperature": request.temperature,
        }

        if request.max_tokens is not None:
            kwargs["max_tokens"] = request.max_tokens

        if request.tools:
            kwargs["tools"] = [t.model_dump(exclude_none=True) for t in request.tools]

        if request.tool_choice is not None:
            kwargs["tool_choice"] = request.tool_choice

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
        content = getattr(choice.message, "content", None) or ""

        tool_calls: list[ToolCall] | None = None
        raw_tool_calls = getattr(choice.message, "tool_calls", None)
        if raw_tool_calls:
            tool_calls = []
            for tc in raw_tool_calls:
                if isinstance(tc, dict):
                    tc_id = str(tc.get("id", ""))
                    tc_type = str(tc.get("type", "function"))
                    tc_func = tc.get("function", {})
                else:
                    tc_id = str(getattr(tc, "id", ""))
                    tc_type = str(getattr(tc, "type", "function"))
                    tc_func = getattr(tc, "function", {})

                if isinstance(tc_func, dict):
                    fn_name = str(tc_func.get("name", ""))
                    fn_args = tc_func.get("arguments", {})
                else:
                    fn_name = str(getattr(tc_func, "name", ""))
                    fn_args = getattr(tc_func, "arguments", {})

                if isinstance(fn_args, str):
                    try:
                        parsed_args = json.loads(fn_args)
                        fn_args = parsed_args
                    except Exception:
                        pass
                tool_calls.append(
                    ToolCall(
                        id=tc_id,
                        type=tc_type,
                        function=ToolCallFunction(
                            name=fn_name,
                            arguments=fn_args,
                        ),
                    )
                )

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
            tool_calls=tool_calls,
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
