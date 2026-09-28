"""Mock authentication headers, signature tokens, and security context helpers for testing."""

import time
from typing import Any

from pydantic import BaseModel, Field

from packages.security.signing import generate_gateway_signature


class SecurityContext(BaseModel):
    """Test representation of an authenticated security context."""

    user_id: str = "test-user-001"
    roles: list[str] = Field(default_factory=lambda: ["Site Investigator / CRC"])
    tenant_id: str = "tenant_default"
    auth_level: str = "authenticated"


def create_test_token(
    user_id: str = "test-user-001",
    roles: list[str] | None = None,
    tenant_id: str = "tenant_default",
    expires_in: int = 3600,
) -> dict[str, Any]:
    """Generates a mock JWT payload representation for unit tests."""
    return {
        "sub": user_id,
        "roles": roles or ["Site Investigator / CRC"],
        "tenant_id": tenant_id,
        "iat": int(time.time()),
        "exp": int(time.time()) + expires_in,
    }


def create_test_security_context(
    user_id: str = "test-user-001",
    roles: list[str] | None = None,
    tenant_id: str = "tenant_default",
) -> SecurityContext:
    """Instantiates a valid SecurityContext object for direct service invocations."""
    return SecurityContext(
        user_id=user_id,
        roles=roles or ["Site Investigator / CRC"],
        tenant_id=tenant_id,
        auth_level="authenticated",
    )


def create_test_auth_headers(
    user_id: str = "test-user-001",
    roles: list[str] | None = None,
    tenant_id: str = "tenant_default",
    change_reason: str | None = "Automated test change justification reason",
    site_id: str | None = None,
    sponsor_id: str | None = None,
    unblinded_access: bool = False,
    sig_token: str | None = None,
    secret: bytes | None = None,
) -> dict[str, str]:
    """Generates authentic GatewayAuthMiddleware-compatible HTTP headers."""
    import os

    secret_bytes = (
        secret
        or os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode()  # pragma: allowlist secret
    )
    assigned_roles = roles or ["Site Investigator / CRC"]
    roles_str = ",".join(assigned_roles)
    ts = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles_str,
        timestamp=ts,
        secret=secret_bytes,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles_str,
        "X-Gateway-Timestamp": ts,
        "X-Timestamp": ts,
        "X-Tenant-Id": tenant_id,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    if site_id:
        headers["X-Site-Id"] = site_id
    if sponsor_id:
        headers["X-Sponsor-Id"] = sponsor_id
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


def generate_signature(
    user_id: str,
    roles: str,
    timestamp: str,
    version: str = "2",
    change_reason: str | None = None,
    site_id: str | None = None,
    sponsor_id: str | None = None,
    unblinded_access: bool = False,
    tenant_id: str | None = None,
    sig_token: str | None = None,
    secret: bytes | None = None,
) -> str:
    """Generates an HMAC-SHA256 signature for test gateway authentication headers."""
    import os

    secret_bytes = (
        secret
        or os.getenv(
            "GATEWAY_SECRET", "internal-gateway-secret-12345"
        ).encode()  # pragma: allowlist secret
    )
    return generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret_bytes,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
        sig_token=sig_token,
    )
