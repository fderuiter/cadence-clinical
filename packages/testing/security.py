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
    change_reason: str | None = None,
    secret: bytes = b"default_test_secret_32_bytes_long_key!!",
) -> dict[str, str]:
    """Generates authentic GatewayAuthMiddleware-compatible HTTP headers."""
    assigned_roles = roles or ["Site Investigator / CRC"]
    roles_str = ",".join(assigned_roles)
    ts = str(int(time.time()))
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles_str,
        timestamp=ts,
        secret=secret,
        change_reason=change_reason,
        tenant_id=tenant_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles_str,
        "X-Timestamp": ts,
        "X-Tenant-Id": tenant_id,
        "X-Gateway-Signature": signature,
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers
