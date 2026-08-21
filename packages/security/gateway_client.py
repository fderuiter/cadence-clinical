import asyncio
import concurrent.futures
import logging
import os
import time

import httpx

from packages.security.asgi_registry import get_service_app, resolve_service_name
from packages.security.context import (
    current_change_reason,
    current_site_id,
    current_sponsor_id,
    current_tenant_id,
    current_unblinded_access,
    current_user_id,
    current_user_roles,
)
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
        "X-In-Process": "true",
    }


def run_async(coro):
    """Runs an async coroutine synchronously while preserving contextvars context.

    Handles loop-detection + ThreadPoolExecutor pattern to run async tasks in non-async contexts.
    Ensures that active thread pools or running event loops are not blocked and authorization
    context is maintained across thread pool boundaries.
    """
    ctx = concurrent.futures.ThreadPoolExecutor
    try:
        asyncio.get_running_loop()
    except RuntimeError:
        return asyncio.run(coro)
    else:
        with ctx(max_workers=1) as executor:
            import contextvars

            c_vars = contextvars.copy_context()
            return executor.submit(c_vars.run, asyncio.run, coro).result()


class GatewayBaseClient:
    """Standardized client wrapper for service-to-service communication.

    Routes internal inter-service requests in-process using ASGI transports
    without generating HMAC HTTP header signatures, propagating active contextvars.
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
        is_in_process: bool = False,
    ) -> dict[str, str]:
        """Builds standard gateway headers for internal routing.

        For in-process calls, HMAC header signatures are omitted.
        """
        headers = {
            "X-User-Id": user_id or current_user_id.get(),
            "X-User-Roles": roles or current_user_roles.get() or "system",
            "X-Change-Reason": change_reason or current_change_reason.get(),
            "X-Tenant-Id": tenant_id or current_tenant_id.get() or "tenant_default",
        }
        if is_in_process:
            headers["X-In-Process"] = "true"

        effective_site = site_id or current_site_id.get()
        effective_sponsor = sponsor_id or current_sponsor_id.get()
        effective_unblinded = unblinded_access or current_unblinded_access.get()

        if effective_site:
            headers["X-Site-Id"] = effective_site
        if effective_sponsor:
            headers["X-Sponsor-Id"] = effective_sponsor
        if effective_unblinded:
            headers["X-Unblinded-Access"] = "true"

        if not is_in_process:
            timestamp = str(time.time())
            signature = generate_gateway_signature(
                user_id=headers["X-User-Id"],
                roles=headers["X-User-Roles"],
                timestamp=timestamp,
                secret=self.secret,
                change_reason=headers["X-Change-Reason"],
                site_id=effective_site,
                sponsor_id=effective_sponsor,
                unblinded_access=effective_unblinded,
                tenant_id=headers["X-Tenant-Id"],
            )
            headers["X-Gateway-Timestamp"] = timestamp
            headers["X-Gateway-Signature"] = signature
            headers["X-Signature-Version"] = "2"

        return headers

    async def request(
        self,
        method: str,
        path: str,
        user_id: str = "",
        roles: str = "",
        change_reason: str = "",
        site_id: str | None = None,
        sponsor_id: str | None = None,
        unblinded_access: bool = False,
        tenant_id: str | None = None,
        headers: dict[str, str] | None = None,
        **kwargs,
    ) -> httpx.Response:
        """Sends an in-process or HTTP request with automated context propagation.

        Logs all failures (transport or non-2xx responses) at the error level.
        """
        url = (
            f"{self.base_url}{path}"
            if path.startswith("/")
            else f"{self.base_url}/{path}"
        )
        if not self.base_url:
            url = path

        service_name = resolve_service_name(self.base_url or path)
        target_app = get_service_app(service_name) if service_name else None

        gw_headers = self.build_headers(
            user_id=user_id,
            roles=roles,
            change_reason=change_reason,
            site_id=site_id,
            sponsor_id=sponsor_id,
            unblinded_access=unblinded_access,
            tenant_id=tenant_id,
            is_in_process=target_app is not None,
        )

        if headers:
            gw_headers.update(headers)

        if "timeout" not in kwargs:
            kwargs["timeout"] = self.timeout

        try:
            if target_app is not None:
                # In-process ASGI transport invocation
                transport = httpx.ASGITransport(app=target_app)
                clean_path = path if path.startswith("/") else f"/{path}"
                async with httpx.AsyncClient(
                    transport=transport, base_url="http://inprocess"
                ) as client:
                    req_fn = getattr(client, method.lower(), client.request)
                    response = await req_fn(clean_path, headers=gw_headers, **kwargs)
            else:
                import sys

                is_testing = (
                    "pytest" in sys.modules or "PYTEST_CURRENT_TEST" in os.environ
                )
                if is_testing:
                    async with httpx.AsyncClient() as client:
                        req_fn = getattr(client, method.lower(), client.request)
                        response = await req_fn(url, headers=gw_headers, **kwargs)
                else:
                    client = self.get_shared_client()
                    req_fn = getattr(client, method.lower(), client.request)
                    response = await req_fn(url, headers=gw_headers, **kwargs)

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
