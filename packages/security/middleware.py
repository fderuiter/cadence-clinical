import hashlib
import hmac
import os
import time
from typing import Awaitable, Callable

from fastapi import Request, Response
from fastapi.responses import JSONResponse
from starlette.middleware.base import BaseHTTPMiddleware


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
            Response: The HTTP response from the downstream handler, or a 401
                      unauthorized JSON response if validation fails.
        """
        if request.url.path == "/health":
            return await call_next(request)

        user_id = request.headers.get("X-User-Id")
        roles = request.headers.get("X-User-Roles")
        timestamp = request.headers.get("X-Gateway-Timestamp")
        signature = request.headers.get("X-Gateway-Signature")

        if not all([user_id, roles, timestamp, signature]):
            return JSONResponse(
                status_code=401,
                content={"detail": "Missing gateway authentication headers"},
            )

        try:
            ts = float(timestamp)
            if abs(time.time() - ts) > 300:
                return JSONResponse(
                    status_code=401, content={"detail": "Gateway signature expired"}
                )
        except ValueError:
            return JSONResponse(
                status_code=401, content={"detail": "Invalid gateway timestamp"}
            )

        version = request.headers.get("X-Signature-Version", "1")

        if version in ("2", "v2"):
            change_reason = request.headers.get("X-Change-Reason")
            if not change_reason:
                return JSONResponse(
                    status_code=401,
                    content={"detail": "Missing change justification reason"},
                )

            import json

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
        else:
            message = f"{user_id}:{roles}:{timestamp}"
            expected_signature = hmac.new(
                self.gateway_secret, message.encode(), hashlib.sha256
            ).hexdigest()

        if not hmac.compare_digest(expected_signature, signature):
            return JSONResponse(
                status_code=401, content={"detail": "Invalid gateway signature"}
            )

        request.state.user_id = user_id
        request.state.roles = roles
        if version in ("2", "v2"):
            request.state.change_reason = change_reason

        return await call_next(request)
