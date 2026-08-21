"""Inference router handling generation, structured outputs, and embeddings."""

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, status

from apps.ai_gateway.adapters.litellm_adapter import LiteLLMAdapter
from apps.ai_gateway.domain.models import (
    ChatMessage,
    EmbeddingRequest,
    GenerationRequest,
)
from apps.ai_gateway.domain.ports import AIEnginePort
from apps.ai_gateway.presentation.dtos import (
    EmbeddingRequestDTO,
    EmbeddingResponseDTO,
    GenerationRequestDTO,
    GenerationResponseDTO,
    TierInfoDTO,
    TokenUsageDTO,
)

router = APIRouter(prefix="/api/v1/ai", tags=["Inference"])

_default_ai_engine: AIEnginePort | None = None


def get_ai_engine() -> AIEnginePort:
    """Dependency provider for the AI engine adapter."""
    global _default_ai_engine
    if _default_ai_engine is None:
        _default_ai_engine = LiteLLMAdapter()
    return _default_ai_engine


def set_ai_engine_override(engine: AIEnginePort | None) -> None:
    """Set or clear a global AI engine override for testing environments."""
    global _default_ai_engine
    _default_ai_engine = engine


AIEngineDep = Annotated[AIEnginePort, Depends(get_ai_engine)]


@router.post(
    "/generate",
    response_model=GenerationResponseDTO,
    status_code=status.HTTP_200_OK,
    summary="Execute AI text generation or structured extraction",
)
async def generate_completion(
    payload: GenerationRequestDTO,
    engine: AIEngineDep,
) -> GenerationResponseDTO:
    """Generate completion text or structured JSON data conforming to a requested schema."""
    if not payload.prompt and not payload.messages:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Either 'prompt' or 'messages' must be provided.",
        )

    messages = (
        [ChatMessage(role=m.role, content=m.content) for m in payload.messages]
        if payload.messages
        else None
    )

    domain_request = GenerationRequest(
        prompt=payload.prompt,
        messages=messages,
        tier=payload.tier,
        model_override=payload.model_override,
        temperature=payload.temperature,
        max_tokens=payload.max_tokens,
        response_schema=payload.response_schema,
        tenant_id=payload.tenant_id,
        study_id=payload.study_id,
    )

    try:
        response = await engine.generate(domain_request)
    except ValueError as val_err:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=str(val_err),
        ) from val_err
    except Exception as err:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"AI generation failed: {err}",
        ) from err

    return GenerationResponseDTO(
        content=response.content,
        structured_data=response.structured_data,
        model=response.model,
        tier=response.tier,
        usage=TokenUsageDTO(
            prompt_tokens=response.usage.prompt_tokens,
            completion_tokens=response.usage.completion_tokens,
            total_tokens=response.usage.total_tokens,
        ),
        latency_ms=response.latency_ms,
    )


@router.post(
    "/embed",
    response_model=EmbeddingResponseDTO,
    status_code=status.HTTP_200_OK,
    summary="Generate dense vector embeddings for input texts",
)
async def generate_embeddings(
    payload: EmbeddingRequestDTO,
    engine: AIEngineDep,
) -> EmbeddingResponseDTO:
    """Generate float vector embeddings for input text strings."""
    domain_request = EmbeddingRequest(
        input_texts=payload.input_texts,
        tier=payload.tier,
        model_override=payload.model_override,
        tenant_id=payload.tenant_id,
    )

    try:
        response = await engine.embed(domain_request)
    except Exception as err:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"AI embedding generation failed: {err}",
        ) from err

    return EmbeddingResponseDTO(
        embeddings=response.embeddings,
        model=response.model,
        tier=response.tier,
        usage=TokenUsageDTO(
            prompt_tokens=response.usage.prompt_tokens,
            completion_tokens=response.usage.completion_tokens,
            total_tokens=response.usage.total_tokens,
        ),
        latency_ms=response.latency_ms,
    )


@router.get(
    "/tiers",
    response_model=list[TierInfoDTO],
    status_code=status.HTTP_200_OK,
    summary="Retrieve active configuration and model mapping across execution tiers",
)
async def list_tiers(
    engine: AIEngineDep,
) -> list[TierInfoDTO]:
    """List all available execution tiers and active model configurations."""
    tiers = engine.get_tier_info()
    return [
        TierInfoDTO(
            tier=t.tier,
            default_model=t.default_model,
            description=t.description,
            capabilities=t.capabilities,
        )
        for t in tiers
    ]
