"""FastAPI application entrypoint for the Interop microservice.

FHIR / eSource & eCOA Sync Gateway providing REST APIs for FHIR prefill,
pre-screening, ePRO mobile sync, instrument authoring, compliance tracking, and quarantine triage.
"""

from fastapi import FastAPI

from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.models import Base
from apps.interop.presentation.routers.cdr_pipeline import (
    router as cdr_pipeline_router,
)
from apps.interop.presentation.routers.dct import (
    router as dct_router,
)
from apps.interop.presentation.routers.devices import (
    router as devices_router,
)
from apps.interop.presentation.routers.esource import (
    router as esource_router,
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
from packages.config import get_interop_settings
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import setup_telemetry

settings = get_interop_settings()
DATABASE_URL = settings.interop_database_url

assert_secure_secrets(
    "interop",
    {
        "GATEWAY_SECRET": settings.gateway_secret,
        "PSEUDONYMIZATION_SALT": settings.pseudonymization_salt,
    },
)

BRAND_NAME = settings.brand_name


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

setup_telemetry("interop", app)
register_rfc7807_handlers(app)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "interop"}


# Include Interop, Lakehouse, CDR Pipeline, SCE, Sensors, DCT, Devices & eSource routers
app.include_router(interop_router)
app.include_router(lakehouse_router)
app.include_router(cdr_pipeline_router)
app.include_router(sce_router)
app.include_router(sensors_router)
app.include_router(dct_router)
app.include_router(esource_router)
app.include_router(devices_router)


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
