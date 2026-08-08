"""FastAPI application entrypoint for the Interop microservice.

FHIR / eSource & eCOA Sync Gateway providing REST APIs for FHIR prefill,
pre-screening, ePRO mobile sync, instrument authoring, compliance tracking, and quarantine triage.
"""

import os
import sys

from fastapi import FastAPI

from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.models import Base
from apps.interop.presentation.routers.interop import (
    router as interop_router,
)
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets
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


def validate_branding_and_domain() -> None:
    if os.getenv("SKIP_BRANDING_VALIDATION") in ("true", "1", "TRUE", "yes", "YES"):
        return
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")
    if is_prod_or_staging:
        invalid = []
        if not os.getenv("BRAND_NAME") or os.getenv("BRAND_NAME") == "Cadence Clinical":
            invalid.append("BRAND_NAME")
        if (
            not os.getenv("BRAND_DOMAIN")
            or os.getenv("BRAND_DOMAIN") == "cadenceclinical.com"
        ):
            invalid.append("BRAND_DOMAIN")
        if invalid:
            error_msg = f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations detected in environment '{app_env}' for variables: {', '.join(invalid)}. Halting boot sequence."
            print(error_msg, file=sys.stderr)
            sys.exit(1)


validate_branding_and_domain()

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


# Include Interop router
app.include_router(interop_router)

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
