import datetime
import hashlib
import hmac
import json
import os
import time
from typing import Awaitable, Callable

from fastapi import Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware

from packages.security.context import (
    current_change_reason,
    current_ip_address,
    current_timestamp,
    current_user_id,
)


class GatewayAuthMiddleware(BaseHTTPMiddleware):
    """
    Middleware to verify internal gateway authentication.

    Extracts identity headers injected by the API gateway and cryptographic
    signatures. If missing or invalid, blocks the request to prevent
    unauthorized direct access to the microservice.
    """

    def __init__(self, app):
        """
        Initialize the GatewayAuthMiddleware.

        Args:
            app: The ASGI application to wrap.
        """
        super().__init__(app)
        self.gateway_secret = os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode()

    async def dispatch(
        self, request: Request, call_next: Callable[[Request], Awaitable[Response]]
    ) -> Response:
        """
        Process the incoming request and perform authentication.

        Args:
            request (Request): The incoming HTTP request.
            call_next (Callable): The next middleware or route handler in the chain.

        Returns:
            Response: The HTTP response from the downstream handler, or a 401/403/400
                      JSON response if validation fails.
        """
        if request.url.path == "/health":
            return await call_next(request)

        is_mutation = request.method in ("POST", "PUT", "DELETE", "PATCH")

        user_id = request.headers.get("X-User-Id")
        roles = request.headers.get("X-User-Roles")
        timestamp = request.headers.get("X-Gateway-Timestamp")
        signature = request.headers.get("X-Gateway-Signature")

        if not all([user_id, roles, timestamp, signature]):
            status_code = 403 if is_mutation else 401
            return JSONResponse(
                status_code=status_code,
                content={"detail": "Missing gateway authentication headers"},
            )

        try:
            ts = float(timestamp)
            if abs(time.time() - ts) > 300:
                status_code = 403 if is_mutation else 401
                return JSONResponse(
                    status_code=status_code,
                    content={"detail": "Gateway signature expired"},
                )
        except ValueError:
            status_code = 400 if is_mutation else 401
            return JSONResponse(
                status_code=status_code, content={"detail": "Invalid gateway timestamp"}
            )

        version = request.headers.get("X-Signature-Version")
        if not version or version not in ("2", "v2"):
            status_code = 403 if is_mutation else 401
            return JSONResponse(
                status_code=status_code,
                content={
                    "detail": "Missing or obsolete signature format. Version 2 canonical JSON signature is required."
                },
            )

        change_reason = request.headers.get("X-Change-Reason")
        if not change_reason:
            if request.method in ("GET", "HEAD", "OPTIONS"):
                change_reason = ""
            else:
                status_code = 403 if is_mutation else 401
                return JSONResponse(
                    status_code=status_code,
                    content={"detail": "Missing change justification reason"},
                )

        if change_reason and len(change_reason) > 255:
            status_code = 400 if is_mutation else 401
            return JSONResponse(
                status_code=status_code,
                content={"detail": "Change reason exceeds 255 characters"},
            )

        payload = {
            "change_reason": change_reason,
            "roles": roles,
            "timestamp": timestamp,
            "user_id": user_id,
        }
        serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
        expected_signature = hmac.new(
            self.gateway_secret, serialized.encode(), hashlib.sha256
        ).hexdigest()

        if not hmac.compare_digest(expected_signature, signature):
            status_code = 403 if is_mutation else 401
            return JSONResponse(
                status_code=status_code, content={"detail": "Invalid gateway signature"}
            )

        request.state.user_id = user_id
        request.state.roles = roles
        request.state.change_reason = change_reason

        # Extract IP address for context injection
        ip_address = request.headers.get(
            "x-forwarded-for", request.client.host if request.client else "127.0.0.1"
        )
        if "," in ip_address:
            ip_address = ip_address.split(",")[0].strip()

        # Set the thread-safe context variables
        user_token = current_user_id.set(user_id)
        reason_token = current_change_reason.set(change_reason or "system_operation")
        ip_token = current_ip_address.set(ip_address)
        ts_token = current_timestamp.set(
            datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
        )

        try:
            return await call_next(request)
        finally:
            # Clean up the context variables to prevent context leakage across tasks
            current_user_id.reset(user_token)
            current_change_reason.reset(reason_token)
            current_ip_address.reset(ip_token)
            current_timestamp.reset(ts_token)
