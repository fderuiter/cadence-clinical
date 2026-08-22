"""
FastAPI application for the Knowledge & Support Hub microservice.

This service owns: knowledge article controlled documents, categories,
contextual help mappings, and article lifecycle. Support ticket creation
is delegated to apps/tickets/ via the internal gateway signing pattern.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002
"""

import os

from fastapi import FastAPI

from apps.knowledge.adapters.database import db_manager, get_db_session
from apps.knowledge.infrastructure.models import Base
from apps.knowledge.presentation.routers.articles import (
    router as articles_router,
)
from apps.knowledge.presentation.routers.protocols import (
    router as protocols_router,
)
from packages.database import get_relational_db_lifespan
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("KNOWLEDGE_DATABASE_URL", "sqlite+aiosqlite:///./knowledge.db")
BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

validate_branding("knowledge")

app = FastAPI(
    title=f"{BRAND_NAME} - Knowledge & Support Hub",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication on every request
app.add_middleware(GatewayAuthMiddleware)

# Mount the articles/categories/contextual-help router
app.include_router(articles_router)
app.include_router(protocols_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "knowledge"}


__all__ = [
    "app",
    "get_db_session",
]
