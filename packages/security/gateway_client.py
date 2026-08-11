import asyncio
import concurrent.futures
import logging
import os
import time

import httpx

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("packages.security.gateway_client")


def create_service_auth_headers(
    user_id: str, roles: str = "system", change_reason: str = "system_operation"
) -> dict[str, str]:
    gateway_secret_env = os.getenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    )  # pragma: allowlist secret
    secret = (
        gateway_secret_env.encode("utf-8")
        if isinstance(gateway_secret_env, str)
        else gateway_secret_env
    )
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def run_async(coro):
    """
    Runs an async coroutine synchronously.
    Handles loop-detection + ThreadPoolExecutor pattern to run async tasks in non-async contexts.
    Ensures that active thread pools or running event loops are not blocked.
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


class GatewayBaseClient:
    """
    Standardized client wrapper for service-to-service communication.
    Handles automated signature generation, gateway secret resolution,
    header formatting, and consistent logging of failed requests.
    """

    _shared_client: httpx.AsyncClient | None = None

    @classmethod
    def get_shared_client(cls) -> httpx.AsyncClient:
        if cls._shared_client is None:
            limits = httpx.Limits(
                max_connections=100,
                max_keepalive_connections=20,
                keepalive_expiry=30.0,
            )
            cls._shared_client = httpx.AsyncClient(limits=limits)
        return cls._shared_client

    def __init__(self, base_url: str = "", timeout: float = 5.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        # Resolved gateway secret
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET",
            "internal-gateway-secret-12345",  # pragma: allowlist secret
        )
        self.secret = (
            gateway_secret_env.encode("utf-8")
            if isinstance(gateway_secret_env, str)
            else gateway_secret_env
        )

    def build_headers(
        self,
        user_id: str,
        roles: str,
        change_reason: str,
        site_id: str | None = None,
        sponsor_id: str | None = None,
        unblinded_access: bool = False,
        tenant_id: str | None = None,
    ) -> dict[str, str]:
        """
        Builds standard gateway headers with an HMAC-SHA256 signature V2.
        """
        timestamp = str(time.time())
        signature = generate_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            secret=self.secret,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
        )

        return {
            "X-User-Id": user_id,
            "X-User-Roles": roles,
            "X-Gateway-Timestamp": timestamp,
            "X-Gateway-Signature": signature,
            "X-Signature-Version": "2",
            "X-Change-Reason": change_reason,
        }

    async def request(
        self,
        method: str,
        path: str,
        user_id: str,
        roles: str,
        change_reason: str,
        site_id: str | None = None,
        sponsor_id: str | None = None,
        unblinded_access: bool = False,
        tenant_id: str | None = None,
        headers: dict[str, str] | None = None,
        **kwargs,
    ) -> httpx.Response:
        """
        Sends an HTTP request with auto-generated gateway security headers.
        Logs all failures (transport or non-2xx responses) at the error level.
        """
        # Resolve full URL
        url = (
            f"{self.base_url}{path}"
            if path.startswith("/")
            else f"{self.base_url}/{path}"
        )
        if not self.base_url:
            url = path

        # Build gateway headers
        gw_headers = self.build_headers(
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
        )

        if headers:
            gw_headers.update(headers)

        # Ensure timeout is set
        if "timeout" not in kwargs:
            kwargs["timeout"] = self.timeout

        try:
            client = self.get_shared_client()
            method_lower = method.lower()
            if method_lower == "get":
                response = await client.get(url, headers=gw_headers, **kwargs)
            elif method_lower == "post":
                response = await client.post(url, headers=gw_headers, **kwargs)
            elif method_lower == "put":
                response = await client.put(url, headers=gw_headers, **kwargs)
            elif method_lower == "delete":
                response = await client.delete(url, headers=gw_headers, **kwargs)
            elif method_lower == "patch":
                response = await client.patch(url, headers=gw_headers, **kwargs)
            else:
                response = await client.request(
                    method, url, headers=gw_headers, **kwargs
                )

            # Check if the response is a failure (not 2xx)
            if response.status_code < 200 or response.status_code >= 300:
                logger.error(
                    "Failed request to %s: HTTP status code %s. Response content: %s",
                    url,
                    response.status_code,
                    response.text,
                )
            return response
        except Exception as e:
            logger.error(
                "Exception occurred during request to %s: %s",
                url,
                str(e),
                exc_info=True,
            )
            raise e
