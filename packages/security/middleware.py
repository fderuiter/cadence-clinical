import datetime
import hashlib
import hmac
import os
import time
from typing import Awaitable, Callable

from fastapi import Request, Response
from fastapi.responses import JSONResponse
from jose import JWTError, jwt
from starlette.middleware.base import BaseHTTPMiddleware

from packages.security.context import (
    current_change_reason,
    current_ip_address,
    current_timestamp,
    current_user_id,
)


class DownstreamReplayCache:
    def __init__(self) -> None:
        self.used_tokens: dict[str, float] = {}

    def is_replayed(self, token: str, exp: float, jti: str | None = None) -> bool:
        now = time.time()
        # Prune expired tokens
        self.used_tokens = {t: e for t, e in self.used_tokens.items() if e > now}
        key = jti if jti else token
        if key in self.used_tokens:
            return True
        self.used_tokens[key] = exp
        return False


downstream_replay_cache = DownstreamReplayCache()


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
        if not version or version not in ("1", "v1", "2", "v2"):
            status_code = 403 if is_mutation else 401
            return JSONResponse(
                status_code=status_code,
                content={
                    "detail": "Missing or obsolete signature format. Version 1 or Version 2 signature is required."
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

        # Retrieve optional scope headers from API gateway
        site_id = request.headers.get("X-Site-Id")
        sponsor_id = request.headers.get("X-Sponsor-Id")
        unblinded_header = request.headers.get("X-Unblinded-Access", "")
        unblinded_access = False
        if unblinded_header.lower() in ("true", "1", "yes"):
            unblinded_access = True

        if version in ("2", "v2"):
            from packages.security.signing import verify_gateway_signature

            is_valid_sig = verify_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                signature=signature,
                secret=self.gateway_secret,
                change_reason=change_reason,
                site_id=site_id,
                sponsor_id=sponsor_id,
                unblinded_access=unblinded_access,
            )
        else:
            # Version 1/v1 (legacy colon concatenated format) - doesn't support scope
            serialized = f"{user_id}:{roles}:{timestamp}"
            expected_signature = hmac.new(
                self.gateway_secret, serialized.encode(), hashlib.sha256
            ).hexdigest()
            is_valid_sig = hmac.compare_digest(expected_signature, signature)

        if not is_valid_sig:
            status_code = 403 if is_mutation else 401
            return JSONResponse(
                status_code=status_code, content={"detail": "Invalid gateway signature"}
            )

        # Check if request is signature-gated
        is_signature_gated = False
        path_lower = request.url.path.lower()
        for pattern in [
            "approve",
            "sign-off",
            "unblind",
            "randomize",
            "queries/sync",
            "close",
        ]:
            if pattern in path_lower:
                is_signature_gated = True
                break

        if is_signature_gated and is_mutation:
            sig_token = request.headers.get("X-Sig-Token")
            if not sig_token:
                return JSONResponse(
                    status_code=401,
                    content={
                        "detail": "REAUTHENTICATION_REQUIRED",
                        "error": "REAUTHENTICATION_REQUIRED",
                        "message": "21 CFR Part 11 mandate: Re-authentication is required.",
                    },
                )
            try:
                sig_payload = jwt.decode(
                    sig_token, self.gateway_secret, algorithms=["HS256"]
                )

                # Check expiration
                if sig_payload.get("exp", 0) < time.time():
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token has expired.",
                        },
                    )

                # Check user binding
                if sig_payload.get("sub") != user_id:
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token user mismatch.",
                        },
                    )

                # Check action binding
                bound_action = sig_payload.get("action", "")
                request_path = request.url.path
                if (
                    request_path != bound_action
                    and bound_action not in request_path
                    and request_path not in bound_action
                ):
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token action mismatch.",
                        },
                    )

                # Check replay attack
                jti = sig_payload.get("jti")
                if downstream_replay_cache.is_replayed(
                    sig_token, sig_payload.get("exp", 0), jti
                ):
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token has already been used.",
                        },
                    )
            except JWTError:
                return JSONResponse(
                    status_code=401,
                    content={
                        "detail": "REAUTHENTICATION_REQUIRED",
                        "error": "REAUTHENTICATION_REQUIRED",
                        "message": "Invalid signature token.",
                    },
                )

        request.state.user_id = user_id
        request.state.roles = roles
        request.state.change_reason = change_reason
        request.state.site_id = site_id
        request.state.sponsor_id = sponsor_id
        request.state.unblinded_access = unblinded_access

        # Extract IP address for context injection
        ip_address = request.headers.get(
            "x-forwarded-for", request.client.host if request.client else "127.0.0.1"
        )
        if "," in ip_address:
            ip_address = ip_address.split(",")[0].strip()

        from packages.security.context import (
            current_site_id,
            current_sponsor_id,
            current_unblinded_access,
        )

        # Set the thread-safe context variables
        user_token = current_user_id.set(user_id)
        reason_token = current_change_reason.set(change_reason or "system_operation")
        ip_token = current_ip_address.set(ip_address)
        ts_token = current_timestamp.set(
            datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None)
        )
        site_token = current_site_id.set(site_id)
        sponsor_token = current_sponsor_id.set(sponsor_id)
        unblinded_token = current_unblinded_access.set(unblinded_access)

        try:
            return await call_next(request)
        finally:
            # Clean up the context variables to prevent context leakage across tasks
            current_user_id.reset(user_token)
            current_change_reason.reset(reason_token)
            current_ip_address.reset(ip_token)
            current_timestamp.reset(ts_token)
            current_site_id.reset(site_token)
            current_sponsor_id.reset(sponsor_token)
            current_unblinded_access.reset(unblinded_token)
