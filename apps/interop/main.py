"""FastAPI application entrypoint for the Interop microservice.

FHIR / eSource & eCOA Sync Gateway providing REST APIs for FHIR prefill,
pre-screening, ePRO mobile sync, instrument authoring, compliance tracking, and quarantine triage.
"""

import os

from fastapi import FastAPI

from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.models import Base
from apps.interop.presentation.routers.cdr_pipeline import (
    router as cdr_pipeline_router,
)
from apps.interop.presentation.routers.interop import (
    router as interop_router,
)
from apps.interop.presentation.routers.lakehouse import (
    router as lakehouse_router,
)
from apps.interop.presentation.routers.sensors import (
    router as sensors_router,
)
from apps.interop.presentation.routers.statistical_computing import (
    router as sce_router,
)
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("INTEROP_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets(
    "interop",
    {
        "GATEWAY_SECRET": os.getenv("GATEWAY_SECRET"),
        "PSEUDONYMIZATION_SALT": os.getenv("PSEUDONYMIZATION_SALT"),
    },
)

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("interop")
app = FastAPI(
    title=f"{BRAND_NAME} - FHIR / eSource & eCOA Sync Gateway",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "interop"}


# Include Interop, Lakehouse, CDR Pipeline, SCE & Sensors routers
app.include_router(interop_router)
app.include_router(lakehouse_router)
app.include_router(cdr_pipeline_router)
app.include_router(sce_router)
app.include_router(sensors_router)

from apps.interop.infrastructure.designer_client import (  # noqa: E402
    fetch_eligibility_criteria,
)
from apps.interop.presentation.routers.interop import (  # noqa: E402
    deliver_notification_task,
)

__all__ = [
    "app",
    "fetch_eligibility_criteria",
    "deliver_notification_task",
]
