import logging
import os

from packages.security import GatewayBaseClient
from packages.security.context import current_change_reason, current_user_id

logger = logging.getLogger("execution-notifications-client")


async def publish_notification(payload: dict) -> bool:
    """
    Sends a POST request to {NOTIFICATIONS_URL}/api/v1/notifications.
    Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
    Logs and swallows all transport or non-2xx errors.
    """
    try:
        notifications_url = os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        client = GatewayBaseClient(base_url=notifications_url, timeout=2.0)

        # Retrieve ContextVars or fallback
        user_id = current_user_id.get()
        if not user_id or user_id == "system":
            user_id = "execution-service"

        change_reason = current_change_reason.get()
        if not change_reason or change_reason == "system_operation":
            change_reason = "Clinical workflow event publication"

        # Role must be authorized
        roles = "admin"

        response = await client.request(
            method="POST",
            path="/api/v1/notifications",
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
            json=payload,
        )
        if response.status_code != 201:
            logger.error(
                "Failed to publish notification, status code: %s, response: %s",
                response.status_code,
                response.text,
            )
            return False
        return True
    except Exception as e:
        logger.error(
            "Exception occurred during notification publication: %s",
            e,
            exc_info=True,
        )
        return False
