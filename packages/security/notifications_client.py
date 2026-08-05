import logging
import os

from packages.security.context import current_change_reason, current_user_id
from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("packages.security.notifications_client")


class NotificationClient(GatewayBaseClient):
    """
    Centralized client to publish notifications.
    Subclasses GatewayBaseClient for secure gateways and signature handling.
    """

    def __init__(self, base_url: str | None = None, timeout: float = 2.0) -> None:
        url = (
            base_url or os.getenv("NOTIFICATIONS_URL") or "http://localhost:8006"
        ).rstrip("/")
        super().__init__(base_url=url, timeout=timeout)

    async def send_notification(
        self,
        payload: dict,
        user_id: str,
        roles: str = "admin",
        change_reason: str = "Notification publication",
        **kwargs,
    ):
        """
        Sends a POST request to /api/v1/notifications to publish a notification.
        """
        return await self.request(
            method="POST",
            path="/api/v1/notifications",
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
            json=payload,
            **kwargs,
        )


async def publish_notification(
    payload: dict,
    fallback_user_id: str = "system-service",
    fallback_change_reason: str = "System operation",
) -> bool:
    """
    Centralized helper to publish a notification.
    """
    try:
        notifications_url = os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        client = NotificationClient(base_url=notifications_url, timeout=2.0)

        user_id = current_user_id.get()
        if not user_id or user_id == "system":
            user_id = fallback_user_id

        change_reason = current_change_reason.get()
        if not change_reason or change_reason == "system_operation":
            change_reason = fallback_change_reason

        roles = "admin"

        response = await client.send_notification(
            payload=payload,
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
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


async def publish_expiration_notification(
    payload: dict,
) -> tuple[bool, str | None, str | None]:
    """
    Centralized helper to publish an expiration notification.
    """
    try:
        notifications_url = os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        timeout_env = os.getenv("ETMF_NOTIFICATIONS_CLIENT_TIMEOUT", "2.0")
        try:
            timeout = float(timeout_env)
        except ValueError:
            timeout = 2.0

        client = NotificationClient(base_url=notifications_url, timeout=timeout)

        user_id = "etmf-service"
        change_reason = "System-initiated expiration alert generation"
        roles = "admin"

        response = await client.send_notification(
            payload=payload,
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
        )
        if response.status_code == 201:
            res_data = response.json()
            return True, res_data.get("id"), None
        error_msg = f"HTTP {response.status_code}: {response.text}"
        logger.error("Failed to publish notification: %s", error_msg)
        return False, None, error_msg
    except Exception as e:
        error_msg = str(e)
        logger.error(
            "Exception occurred during notification publication: %s",
            error_msg,
            exc_info=True,
        )
        return False, None, error_msg
