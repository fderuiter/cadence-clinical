"""
HTTP client adapter for dispatching notification events from the Knowledge microservice.

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
    Publishes a notification event targeting apps/notifications/.

    Uses HMAC-SHA256 Gateway signature for secure internal service authentication.
    Logs and swallows transport/HTTP errors to prevent blocking article operations.

    Args:
        payload: NotificationEvent model or payload dictionary.

    Returns:
        True if notification was accepted, False otherwise.
    """
    return await _publish_notification(
        payload,
        service_name="knowledge-service",
    )


__all__ = ["publish_notification"]
