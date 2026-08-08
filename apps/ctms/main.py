import os
import sys

from fastapi import FastAPI

from apps.ctms.database import db_manager
from apps.ctms.models import Base
from apps.ctms.presentation.routers import ctms_router, doa_router
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("ctms", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

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
    title=f"{BRAND_NAME} - CTMS",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include routers
app.include_router(doa_router)
app.include_router(ctms_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "ctms"}
