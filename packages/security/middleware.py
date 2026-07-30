import datetime
import hashlib
import hmac
import os
import time
from typing import Any, Awaitable, Callable, Optional, Union

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


def verify_sig_token(
    sig_token: Optional[str],
    user_id: str,
    request_path: str,
    secret: bytes,
    replay_cache: Any,
    expected_semantic_action: Optional[str] = None,
    check_replay: bool = True,
) -> tuple[bool, Union[dict, str]]:
    """
    Standalone function to verify a signature token (JWT).
    Validates presence, signature, expiration, identity binding, action/semantic_action binding,
    and single-use replay protection.

    Returns:
        tuple[bool, Union[dict, str]]: (True, sig_payload) on success, or (False, error_message) on failure.
    """
    print(
        f"VERIFY_SIG_TOKEN: sig_token={sig_token[:20] if sig_token else None}, user_id={user_id}, request_path={request_path}, expected_semantic={expected_semantic_action}"
    )
    if not sig_token:
        print("VERIFY_SIG_TOKEN: Failed on missing token")
        return False, "21 CFR Part 11 mandate: Re-authentication is required."

    try:
        sig_payload = jwt.decode(sig_token, secret, algorithms=["HS256"])
    except JWTError as e:
        print(f"VERIFY_SIG_TOKEN: Failed to decode: {e}")
        return False, "Invalid signature token."

    # Check expiration
    if sig_payload.get("exp", 0) < time.time():
        print("VERIFY_SIG_TOKEN: Failed on expiration")
        return False, "Signature token has expired."

    # Check user binding
    if sig_payload.get("sub") != user_id:
        print(
            f"VERIFY_SIG_TOKEN: Failed on user mismatch: sub={sig_payload.get('sub')} vs {user_id}"
        )
        return False, "Signature token user mismatch."

    # Validate semantic action binding if expected & present in token
    token_semantic = sig_payload.get("semantic_action")
    if expected_semantic_action and token_semantic:
        if token_semantic != expected_semantic_action:
            print(
                f"VERIFY_SIG_TOKEN: Failed on semantic mismatch: {token_semantic} vs {expected_semantic_action}"
            )
            return False, "Signature token semantic action mismatch."

    # Check loose path binding (always checked for baseline safety)
    bound_action = sig_payload.get("action", "")
    if (
        request_path != bound_action
        and bound_action not in request_path
        and request_path not in bound_action
    ):
        print(
            f"VERIFY_SIG_TOKEN: Failed on path mismatch: bound_action={bound_action} vs request_path={request_path}"
        )
        return False, "Signature token action mismatch."

    # Check replay attack
    if check_replay:
        jti = sig_payload.get("jti")
        if replay_cache.is_replayed(sig_token, sig_payload.get("exp", 0), jti):
            print("VERIFY_SIG_TOKEN: Failed on replay check")
            return False, "Signature token has already been used."

    print("VERIFY_SIG_TOKEN: SUCCESS")
    return True, sig_payload


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

    @property
    def gateway_secret(self) -> bytes:
        """Dynamically resolve gateway secret from environment to support runtime configuration and test overrides."""
        return os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()

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
        if request.url.path in ("/health", "/api/v1/etmf/inbound-email"):
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

        # Retrieve optional scope headers from API gateway and normalize them using the shared helper
        from packages.security.signing import normalize_scope_values

        raw_site_id = request.headers.get("X-Site-Id")
        raw_sponsor_id = request.headers.get("X-Sponsor-Id")
        raw_unblinded = request.headers.get("X-Unblinded-Access")

        site_id, sponsor_id, unblinded_access = normalize_scope_values(
            raw_site_id, raw_sponsor_id, raw_unblinded
        )

        # Extract tenant identity and apply least-privilege migration policy (default to tenant_default)
        tenant_id = request.headers.get("X-Tenant-Id")
        if tenant_id is None or not str(tenant_id).strip():
            tenant_id = "tenant_default"
        else:
            tenant_id = str(tenant_id).strip()

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
                tenant_id=tenant_id,
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

        body_json = None
        if is_mutation:
            body_bytes = await request.body()
            if body_bytes:
                try:
                    import json

                    body_json = json.loads(body_bytes)
                except Exception:
                    pass

                # Restore body receive for Starlette downstream
                async def receive():
                    return {
                        "type": "http.request",
                        "body": body_bytes,
                        "more_body": False,
                    }

                request._receive = receive

        from packages.security.gating import is_path_signature_gated
        from packages.security.regulated_actions import resolve_regulated_action

        expected_semantic = resolve_regulated_action(
            request.method, request.url.path, body_json
        )
        path_lower = request.url.path.lower()
        is_signature_gated = (expected_semantic is not None) or is_path_signature_gated(
            path_lower
        )

        if is_signature_gated and is_mutation:
            sig_token = request.headers.get("X-Sig-Token")
            success, result = verify_sig_token(
                sig_token=sig_token,
                user_id=user_id,
                request_path=request.url.path,
                secret=self.gateway_secret,
                replay_cache=downstream_replay_cache,
                expected_semantic_action=expected_semantic.value
                if expected_semantic
                else None,
            )
            if not success:
                return JSONResponse(
                    status_code=401,
                    content={
                        "detail": "REAUTHENTICATION_REQUIRED",
                        "error": "REAUTHENTICATION_REQUIRED",
                        "message": str(result),
                    },
                )

            sig_payload = result

            # Check batch binding if path is batch-sign-off or if token contains batch_id
            token_batch_id = sig_payload.get("batch_id")
            if "batch-sign-off" in path_lower:
                if not token_batch_id:
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token is not bound to a batch.",
                        },
                    )

                req_study_id = body_json.get("study_id")
                req_target_type = body_json.get("target_type")
                req_target_ids = body_json.get("target_ids")
                req_signing_reason = body_json.get("signing_reason")

                if not all(
                    [
                        req_study_id,
                        req_target_type,
                        req_target_ids is not None,
                        req_signing_reason,
                    ]
                ):
                    return JSONResponse(
                        status_code=400,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Missing batch sign-off fields for validation.",
                        },
                    )

                # Compute canonical batch binding
                norm_study = str(req_study_id).strip()
                norm_type = str(req_target_type).strip().upper()
                sorted_ids = sorted([str(tid).strip() for tid in req_target_ids])
                norm_ids = ",".join(sorted_ids)
                norm_reason = str(req_signing_reason).strip()

                binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
                computed_batch_id = hashlib.sha256(
                    binding_str.encode("utf-8")
                ).hexdigest()

                if token_batch_id != computed_batch_id:
                    return JSONResponse(
                        status_code=401,
                        content={
                            "detail": "REAUTHENTICATION_REQUIRED",
                            "error": "REAUTHENTICATION_REQUIRED",
                            "message": "Signature token batch binding mismatch.",
                        },
                    )

        request.state.user_id = user_id
        request.state.roles = roles
        request.state.change_reason = change_reason
        request.state.site_id = site_id
        request.state.sponsor_id = sponsor_id
        request.state.unblinded_access = unblinded_access
        request.state.tenant_id = tenant_id

        # Extract IP address for context injection
        ip_address = request.headers.get(
            "x-forwarded-for", request.client.host if request.client else "127.0.0.1"
        )
        if "," in ip_address:
            ip_address = ip_address.split(",")[0].strip()

        from packages.security.context import (
            current_site_id,
            current_sponsor_id,
            current_tenant_id,
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
        tenant_token = current_tenant_id.set(tenant_id)

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
            current_tenant_id.reset(tenant_token)
