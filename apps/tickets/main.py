"""
FastAPI application for the Tickets microservice.
"""

import os
import sys

from fastapi import FastAPI

from apps.tickets.escalation import (
    start_background_ticket_escalation,
    stop_background_ticket_escalation,
)
from apps.tickets.infrastructure.database import db_manager, get_db_session
from apps.tickets.infrastructure.models import Base
from apps.tickets.infrastructure.notifications_client import publish_notification
from apps.tickets.presentation.dtos import (
    CommentCreate,
    CommentResponse,
    PaginatedTicketAuditLogResponse,
    RegulatoryRiskAssessment,
    SettingDiffEntry,
    TicketAssignPayload,
    TicketAuditLogResponse,
    TicketCreate,
    TicketResponse,
    TicketTransitionPayload,
    TicketUpdate,
)
from apps.tickets.presentation.routers.tickets import (
    TICKET_ESCALATE,
    dispatch_ticket_notifications,
    map_ticket_to_response,
    write_ticket_audit_log,
)
from apps.tickets.presentation.routers.tickets import (
    router as tickets_router,
)
from packages.database import get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("TICKETS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
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
    title=f"{BRAND_NAME} - Tickets Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[start_background_ticket_escalation],
        shutdown_hooks=[stop_background_ticket_escalation],
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Include presentation routers
app.include_router(tickets_router)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "tickets"}


__all__ = [
    "TICKET_ESCALATE",
    "CommentCreate",
    "CommentResponse",
    "PaginatedTicketAuditLogResponse",
    "RegulatoryRiskAssessment",
    "SettingDiffEntry",
    "TicketAssignPayload",
    "TicketAuditLogResponse",
    "TicketCreate",
    "TicketResponse",
    "TicketTransitionPayload",
    "TicketUpdate",
    "app",
    "dispatch_ticket_notifications",
    "get_db_session",
    "map_ticket_to_response",
    "publish_notification",
    "write_ticket_audit_log",
]
