import os

from fastapi import FastAPI

from apps.ctms.adapters.database import db_manager
from apps.ctms.adapters.models import Base
from apps.ctms.presentation.routers import (
    ctms_router,
    deviations_router,
    doa_router,
    etmf_sync_router,
    financials_router,
    ip_accountability_router,
    rbqm_router,
    site_startup_router,
)
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import assert_secure_secrets, validate_branding
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("CTMS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets("ctms", {"GATEWAY_SECRET": os.getenv("GATEWAY_SECRET")})

BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("ctms")
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
register_rfc7807_handlers(app)

# Include routers
app.include_router(doa_router)
app.include_router(site_startup_router)
app.include_router(deviations_router)
app.include_router(rbqm_router)
app.include_router(financials_router)
app.include_router(ip_accountability_router)
app.include_router(etmf_sync_router)
app.include_router(ctms_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "ctms"}
