"""
Centralized notifications client wrapper for tickets service.
"""

from packages.security.notifications_client import (
    publish_notification as _publish_notification,
)


async def publish_notification(payload: dict) -> bool:
    return await _publish_notification(
        payload,
        fallback_user_id="tickets-service",
        fallback_change_reason="Ticket event notification dispatch",
    )


__all__ = [
    "publish_notification",
]
