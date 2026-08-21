"""FastAPI application entry point for the AI Gateway microservice."""

import os

from fastapi import FastAPI

from apps.ai_gateway.presentation.dtos import (
    ChatMessageDTO,
    EmbeddingRequestDTO,
    EmbeddingResponseDTO,
    GenerationRequestDTO,
    GenerationResponseDTO,
    TierInfoDTO,
    TokenUsageDTO,
)
from apps.ai_gateway.presentation.routers.inference import (
    get_ai_engine,
)
from apps.ai_gateway.presentation.routers.inference import (
    router as inference_router,
)
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

validate_branding("ai-gateway")

app = FastAPI(
    title=f"{BRAND_NAME} - AI Gateway Service",
    description="Centralized AI routing, tier management, structured output extraction, and embeddings.",
    version="0.1.0",
)

# Enforce secure gateway HMAC authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include inference routers
app.include_router(inference_router)


@app.get("/health")
@app.get("/healthz")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "ai-gateway"}


__all__ = [
    "ChatMessageDTO",
    "EmbeddingRequestDTO",
    "EmbeddingResponseDTO",
    "GenerationRequestDTO",
    "GenerationResponseDTO",
    "TierInfoDTO",
    "TokenUsageDTO",
    "app",
    "get_ai_engine",
    "health_check",
    "inference_router",
]
