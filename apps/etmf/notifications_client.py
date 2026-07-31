import logging
import os
import time
from typing import Optional, Tuple

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("etmf-notifications-client")


async def publish_expiration_notification(
    payload: dict,
) -> Tuple[bool, Optional[str], Optional[str]]:
    """
    Sends a POST request to {NOTIFICATIONS_URL}/api/v1/notifications.
    Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
    Returns (success, notification_id, error_message).
    """
    try:
        notifications_url = os.getenv(
            "NOTIFICATIONS_URL", "http://localhost:8006"
        ).rstrip("/")
        url = f"{notifications_url}/api/v1/notifications"

        timeout_env = os.getenv("ETMF_NOTIFICATIONS_CLIENT_TIMEOUT", "2.0")
        try:
            timeout = float(timeout_env)
        except ValueError:
            timeout = 2.0

        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        user_id = "etmf-service"
        change_reason = "System-initiated expiration alert generation"
        roles = "admin"
        timestamp = str(time.time())

        # Generate gateway signature covering parameters
        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=gateway_secret,
            change_reason=change_reason,
        )

        headers = {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
        }

        async with httpx.AsyncClient(timeout=timeout) as client:
            response = await client.post(url, json=payload, headers=headers)
            if response.status_code == 201:
                res_data = response.json()
                return True, res_data.get("id"), None
            else:
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
