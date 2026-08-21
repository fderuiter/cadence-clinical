"""
HTTP client adapter for dispatching notification events from the Knowledge microservice.

Mirrors the pattern in apps/tickets/adapters/notifications_client.py.
"""

import logging
import os

from packages.security import GatewayBaseClient
from packages.security.context import current_change_reason, current_user_id

logger = logging.getLogger("knowledge-notifications-client")


async def publish_notification(payload: dict) -> bool:
    """
    Sends a POST request to {NOTIFICATIONS_URL}/api/v1/notifications.

    Uses HMAC-SHA256 Gateway signature for secure internal service authentication.
    Logs and swallows all transport or non-2xx errors to avoid blocking article
    lifecycle operations on notification delivery failures.

    Args:
        payload: Notification payload dict to POST.

    Returns:
        True if the notification was accepted (HTTP 201), False otherwise.
    """
    try:
        notifications_url = os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        client = GatewayBaseClient(base_url=notifications_url, timeout=2.0)

        user_id = current_user_id.get()
        if not user_id or user_id == "system":
            user_id = "knowledge-service"

        change_reason = current_change_reason.get()
        if not change_reason or change_reason == "system_operation":
            change_reason = "Knowledge article event notification dispatch"

        response = await client.request(
            method="POST",
            path="/api/v1/notifications",
            user_id=user_id,
            roles="admin",
            change_reason=change_reason,
            json=payload,
        )
        if response.status_code != 201:
            logger.error(
                "Failed to publish notification, status=%s body=%s",
                response.status_code,
                response.text,
            )
            return False
        return True
    except Exception as exc:
        logger.error(
            "Exception during notification publication: %s", exc, exc_info=True
        )
        return False
