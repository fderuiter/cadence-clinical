import asyncio
import concurrent.futures
import logging
import os
import time

import httpx

from packages.security.context import current_change_reason, current_user_id
from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("execution-notifications-client")


async def publish_notification(payload: dict) -> bool:
    """
    Sends a POST request to {NOTIFICATIONS_URL}/api/v1/notifications.
    Uses HMAC-SHA256 Gateway signature V2 for secure internal service authentication.
    Logs and swallows all transport or non-2xx errors.
    """
    try:
        notifications_url = os.getenv(
            "NOTIFICATIONS_URL", "http://localhost:8006"
        ).rstrip("/")
        url = f"{notifications_url}/api/v1/notifications"

        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        )
        gateway_secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

        # Retrieve ContextVars or fallback
        user_id = current_user_id.get()
        if not user_id or user_id == "system":
            user_id = "execution-service"

        change_reason = current_change_reason.get()
        if not change_reason or change_reason == "system_operation":
            change_reason = "Clinical workflow event publication"

        # Role must be authorized
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

        async with httpx.AsyncClient(timeout=2.0) as client:
            response = await client.post(url, json=payload, headers=headers)
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


def run_async(coro):
    """
    Runs an async coroutine synchronously.
    Ports loop-detection + ThreadPoolExecutor pattern from apps/designer/db.py::run_async.
    """
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        # No running event loop in this thread, safely use asyncio.run
        return asyncio.run(coro)
    else:
        # Event loop is running (e.g. FastAPI / ASGI context). Run in separate thread.
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as executor:
            return executor.submit(asyncio.run, coro).result()
