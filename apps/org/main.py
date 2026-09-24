"""
FastAPI application entrypoint for the Organization Directory microservice.

Provides REST APIs for Organization, Site, and Personnel (SiteStaff) directory management,
with 21 CFR Part 11 and GxP compliant append-only version history and audit trails.
"""

from fastapi import FastAPI

from apps.org.infrastructure.database import db_manager
from apps.org.infrastructure.models import Base
from apps.org.presentation.routers.org import router as org_router
from packages.config import get_org_settings
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import setup_telemetry

settings = get_org_settings()
GATEWAY_SECRET = settings.gateway_secret.encode()

DATABASE_URL = settings.org_database_url

assert_secure_secrets("org", {"GATEWAY_SECRET": settings.gateway_secret})

BRAND_NAME = settings.brand_name


validate_branding("org")
app = FastAPI(
    title=f"{BRAND_NAME} - Organization Directory",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

setup_telemetry("org", app)

register_rfc7807_handlers(app)

# Register internal gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include Organization Directory router
app.include_router(org_router)
