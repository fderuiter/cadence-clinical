import asyncio
import concurrent.futures
import logging
import os
import time
from typing import Dict, Optional

import httpx
from tenacity import (
    AsyncRetrying,
    stop_after_attempt,
    stop_after_delay,
    wait_random_exponential,
    retry_if_exception_type,
)

from packages.security.signing import generate_gateway_signature

logger = logging.getLogger("packages.security.gateway_client")


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


class GatewayRetryableError(Exception):
    """Exception raised internally to trigger tenacity retry for 5xx status codes or connection errors."""
    def __init__(self, response: httpx.Response):
        super().__init__(f"HTTP {response.status_code}")
        self.response = response


class GatewayBaseClient:
    """
    Standardized client wrapper for service-to-service communication.
    Handles automated signature generation, gateway secret resolution,
    header formatting, and consistent logging of failed requests.
    """

    def __init__(self, base_url: str = "", timeout: float = 5.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        # Resolved gateway secret
        gateway_secret_env = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
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
        site_id: Optional[str] = None,
        sponsor_id: Optional[str] = None,
        unblinded_access: bool = False,
        tenant_id: Optional[str] = None,
    ) -> Dict[str, str]:
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
        site_id: Optional[str] = None,
        sponsor_id: Optional[str] = None,
        unblinded_access: bool = False,
        tenant_id: Optional[str] = None,
        headers: Optional[Dict[str, str]] = None,
        client: Optional[httpx.AsyncClient] = None,
        **kwargs,
    ) -> httpx.Response:
        """
        Sends an HTTP request with auto-generated gateway security headers.
        Logs all failures (transport or non-2xx responses) at the error level.
        Uses tenacity to retry on 5xx or connection timeouts/errors up to 3 times,
        regenerating headers on each try, with exponential backoff and randomized jitter.
        """
        # Resolve full URL
        url = (
            f"{self.base_url}{path}"
            if path.startswith("/")
            else f"{self.base_url}/{path}"
        )
        if not self.base_url:
            url = path

        # Outgoing request timeout limits must stay at 5.0 seconds per try
        req_timeout = min(float(kwargs.get("timeout") or self.timeout or 5.0), 5.0)
        kwargs["timeout"] = req_timeout

        async def make_attempt() -> httpx.Response:
            # Build gateway headers dynamically inside the retry block so they are regenerated on each retry!
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

            # If client is passed, use it directly (do not close/exit context on it)
            if client is not None:
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
            else:
                async with httpx.AsyncClient() as cli:
                    method_lower = method.lower()
                    if method_lower == "get":
                        response = await cli.get(url, headers=gw_headers, **kwargs)
                    elif method_lower == "post":
                        response = await cli.post(url, headers=gw_headers, **kwargs)
                    elif method_lower == "put":
                        response = await cli.put(url, headers=gw_headers, **kwargs)
                    elif method_lower == "delete":
                        response = await cli.delete(url, headers=gw_headers, **kwargs)
                    elif method_lower == "patch":
                        response = await cli.patch(url, headers=gw_headers, **kwargs)
                    else:
                        response = await cli.request(
                            method, url, headers=gw_headers, **kwargs
                        )

            # Check if the response is a 5xx failure
            if response.status_code >= 500:
                logger.error(
                    "Failed request to %s: HTTP status code %s. Response content: %s",
                    url,
                    response.status_code,
                    response.text,
                )
                raise GatewayRetryableError(response)

            if response.status_code < 200 or response.status_code >= 300:
                logger.error(
                    "Failed request to %s: HTTP status code %s. Response content: %s",
                    url,
                    response.status_code,
                    response.text,
                )
            return response

        try:
            # stop=stop_after_delay(15.0) | stop_after_attempt(4) ensures that cumulative time of all retries does not exceed 15s,
            # and total attempts do not exceed 4 (initial + up to 3 retries).
            async for attempt in AsyncRetrying(
                stop=(stop_after_delay(15.0) | stop_after_attempt(4)),
                wait=wait_random_exponential(multiplier=1.0, min=1.0, max=4.0),
                retry=retry_if_exception_type((httpx.RequestError, GatewayRetryableError)),
                reraise=True,
            ):
                with attempt:
                    return await make_attempt()
        except GatewayRetryableError as e:
            # All retries failed with 5xx, return the last 5xx response
            return e.response
        except Exception as e:
            logger.error(
                "Exception occurred during request to %s: %s",
                url,
                str(e),
                exc_info=True,
            )
            raise e

    def request_sync(
        self,
        method: str,
        path: str,
        user_id: str,
        roles: str,
        change_reason: str,
        site_id: Optional[str] = None,
        sponsor_id: Optional[str] = None,
        unblinded_access: bool = False,
        tenant_id: Optional[str] = None,
        headers: Optional[Dict[str, str]] = None,
        client: Optional[httpx.AsyncClient] = None,
        **kwargs,
    ) -> httpx.Response:
        """
        Sends an HTTP request with auto-generated gateway security headers synchronously.
        """
        coro = self.request(
            method=method,
            path=path,
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
            headers=headers,
            client=client,
            **kwargs,
        )
        return run_async(coro)
