"""
Centralized notifications client wrapper for execution service.
"""

from packages.security.notifications_client import (
    publish_notification as _publish_notification,
)


async def publish_notification(payload: dict) -> bool:
    return await _publish_notification(
        payload,
        fallback_user_id="execution-service",
        fallback_change_reason="Clinical workflow event publication",
    )


__all__ = [
    "publish_notification",
]
