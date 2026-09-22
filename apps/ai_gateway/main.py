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
from apps.ai_gateway.presentation.routers.rag import (
    router as rag_router,
)
from apps.ai_gateway.presentation.routers.reasoning import (
    router as reasoning_router,
)
from packages.hexagonal import register_rfc7807_handlers
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import setup_telemetry

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

validate_branding("ai-gateway")

app = FastAPI(
    title=f"{BRAND_NAME} - AI Gateway Service",
    description="Centralized AI routing, tier management, structured output extraction, and embeddings.",
    version="0.1.0",
)

setup_telemetry("ai_gateway", app)

register_rfc7807_handlers(app)

# Enforce secure gateway HMAC authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include inference & reasoning routers
app.include_router(inference_router)
app.include_router(rag_router)
app.include_router(reasoning_router)


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
    "rag_router",
    "reasoning_router",
]
