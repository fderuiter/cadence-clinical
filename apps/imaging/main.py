"""FastAPI application for the Medical Imaging Microservice.

Owns: In-flight DICOM PHI de-identification, burned-in pixel masking,
tenant-isolated S3/MinIO envelope storage, and independent review charter workflows.

Requirements: ADR-2199, PRD-IMG-001, PRD-IMG-002
"""

from fastapi import FastAPI

from apps.imaging.adapters.database import db_manager, get_db_session
from apps.imaging.infrastructure.models import Base
from apps.imaging.presentation.routers.imaging import router as imaging_router
from packages.config import get_imaging_settings
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import setup_telemetry

settings = get_imaging_settings()
DATABASE_URL = settings.imaging_database_url
BRAND_NAME = settings.brand_name

validate_branding("imaging")

app = FastAPI(
    title=f"{BRAND_NAME} - Medical Imaging & Adjudication Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

setup_telemetry("imaging", app)

register_rfc7807_handlers(app)

# Enforce secure gateway authentication on every request
app.add_middleware(GatewayAuthMiddleware)

# Mount imaging router
app.include_router(imaging_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "imaging"}


__all__ = [
    "app",
    "get_db_session",
]
