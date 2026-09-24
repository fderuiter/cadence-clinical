"""
HTTP client adapter for dispatching document expiration notifications from the eTMF microservice.

Delegates to the unified NotificationDispatcher in packages/security/notifications.py.
"""

import logging
import os
from typing import Any

from packages.security import GatewayBaseClient

logger = logging.getLogger("etmf-notifications-client")


async def publish_expiration_notification(
    payload: dict[str, Any],
) -> tuple[bool, str | None, str | None]:
    """
    Publishes an eTMF document expiration notification event.

    Args:
        payload: Notification payload dictionary.

    Returns:
        Tuple of (success_bool, notification_id_or_none, error_message_or_none).
    """
    try:
        notifications_url = os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        timeout_env = os.getenv("ETMF_NOTIFICATIONS_CLIENT_TIMEOUT", "2.0")
        try:
            timeout = float(timeout_env)
        except ValueError:
            timeout = 2.0

        client = GatewayBaseClient(base_url=notifications_url, timeout=timeout)
        response = await client.request(
            method="POST",
            path="/api/v1/notifications",
            user_id="etmf-service",
            roles="admin",
            change_reason="System-initiated expiration alert generation",
            json=payload,
        )

        if response.status_code == 201:
            res_data = response.json()
            return True, res_data.get("id"), None

        error_msg = f"HTTP {response.status_code}: {response.text}"
        logger.error("Failed to publish notification: %s", error_msg)
        return False, None, error_msg

    except Exception as exc:
        error_msg = str(exc)
        logger.error(
            "Exception occurred during notification publication: %s",
            error_msg,
            exc_info=True,
        )
        return False, None, error_msg


__all__ = ["publish_expiration_notification"]
