"""
FastAPI application entrypoint for the Organization Directory microservice.

Provides REST APIs for Organization, Site, and Personnel (SiteStaff) directory management,
with 21 CFR Part 11 and GxP compliant append-only version history and audit trails.
"""

import os
import sys

from fastapi import FastAPI

from apps.org.infrastructure.database import db_manager
from apps.org.infrastructure.models import Base
from apps.org.presentation.routers.org import router as org_router
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets
from packages.security.middleware import GatewayAuthMiddleware

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
).encode()  # pragma: allowlist secret

DATABASE_URL = os.getenv("ORG_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("org", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

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
    title=f"{BRAND_NAME} - Organization Directory",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Register internal gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include Organization Directory router
app.include_router(org_router)
