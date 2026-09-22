"""
HTTP client adapter for dispatching notification events from the Execution microservice.

Delegates to the unified NotificationDispatcher in packages/security/notifications.py.
"""

from typing import Any

from packages.security.notifications import (
    NotificationEvent,
)
from packages.security.notifications import (
    publish_notification as _publish_notification,
)


async def publish_notification(payload: NotificationEvent | dict[str, Any]) -> bool:
    """
    Sends a POST request to {NOTIFICATIONS_URL}/api/v1/notifications.
    Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
    Logs and swallows all transport or non-2xx errors.
    """
    return await _publish_notification(
        payload,
        service_name="execution-service",
    )


__all__ = ["publish_notification"]
