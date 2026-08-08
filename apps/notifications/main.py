import os

from fastapi import FastAPI

from apps.notifications.infrastructure.database import db_manager
from apps.notifications.infrastructure.models import Base
from apps.notifications.presentation.dtos import (
    NotificationCreate,
    NotificationResponse,
)
from apps.notifications.presentation.routers.notifications import (
    active_deliveries,
    active_tasks,
    deliver_channel,
    deliver_channel_wrapper,
    dispatcher_lifecycle_worker,
    map_notification_to_response,
    poll_and_dispatch,
    start_dispatcher,
    stop_dispatcher,
    write_audit_log,
)
from apps.notifications.presentation.routers.notifications import (
    router as notifications_router,
)
from packages.database import get_relational_db_lifespan
from packages.security import assert_secure_secrets
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("NOTIFICATIONS_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets(
    "notifications", {"WEBHOOK_SIGNING_SECRET": os.getenv("WEBHOOK_SIGNING_SECRET")}
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
            import sys

            error_msg = f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations detected in environment '{app_env}' for variables: {', '.join(invalid)}. Halting boot sequence."
            print(error_msg, file=sys.stderr)
            sys.exit(1)


validate_branding_and_domain()


app = FastAPI(
    title=f"{BRAND_NAME} - Notifications Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[start_dispatcher],
        shutdown_hooks=[stop_dispatcher],
    ),
)

app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint."""
    return {"status": "ok", "service": "notifications"}


app.include_router(notifications_router)

__all__ = [
    "NotificationCreate",
    "NotificationResponse",
    "active_deliveries",
    "active_tasks",
    "app",
    "deliver_channel",
    "deliver_channel_wrapper",
    "dispatcher_lifecycle_worker",
    "map_notification_to_response",
    "poll_and_dispatch",
    "start_dispatcher",
    "stop_dispatcher",
    "write_audit_log",
]
