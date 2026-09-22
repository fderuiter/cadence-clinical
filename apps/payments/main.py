"""FastAPI application for the Site Payments Microservice.

Owns: Real-time EDC/SDV trigger-to-payable automation, multi-party split payee allocation,
10% holdback retention governance, and 21 CFR Part 11 financial audit logs.

Requirements: ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006
"""

import os

from fastapi import FastAPI

from apps.payments.adapters.database import db_manager, get_db_session
from apps.payments.infrastructure.models import Base
from apps.payments.presentation.routers import payments_router
from packages.database import get_relational_db_lifespan
from packages.hexagonal import register_rfc7807_handlers
from packages.security import validate_branding
from packages.security.middleware import GatewayAuthMiddleware
from packages.telemetry import setup_telemetry

from packages.config import get_payments_settings

settings = get_payments_settings()
DATABASE_URL = settings.payments_database_url
BRAND_NAME = settings.brand_name

validate_branding("payments")

app = FastAPI(
    title=f"{BRAND_NAME} - Site Payments & Split-Payee Engine",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

setup_telemetry("payments", app)

register_rfc7807_handlers(app)

# Enforce secure gateway authentication on every request
app.add_middleware(GatewayAuthMiddleware)

# Mount payments router
app.include_router(payments_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "payments"}


__all__ = [
    "app",
    "get_db_session",
]
