"""FastAPI application for the Fileshare Microservice.

Owns: Clinical file records, object storage presigned transfers,
internal share grants, and guest links.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import os

from fastapi import FastAPI

from apps.fileshare.adapters.database import db_manager, get_db_session
from apps.fileshare.infrastructure.models import Base
from apps.fileshare.presentation.routers.files import router as files_router
from packages.database import get_relational_db_lifespan
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv(
    "FILESHARE_DATABASE_URL", "sqlite+aiosqlite:///./fileshare.db"
)
BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")

validate_branding("fileshare")

app = FastAPI(
    title=f"{BRAND_NAME} - Fileshare & Media Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication on every request
app.add_middleware(GatewayAuthMiddleware)

# Mount files router
app.include_router(files_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "fileshare"}


__all__ = [
    "app",
    "get_db_session",
]

