import os
import time

from packages.security.signing import generate_gateway_signature


def build_gateway_headers(
    user_id: str,
    roles: str,
    change_reason: str = "Authorized change",
    site_id: str | None = None,
    sponsor_id: str | None = None,
    tenant_id: str | None = "tenant_default",
    unblinded_access: bool = False,
    sig_token: str | None = None,
) -> dict:
    """
    Build valid Gateway V2 signed headers with full scope.
    Directly uses packages.security.signing.generate_gateway_signature.
    """
    timestamp = str(time.time())

    # Secrets Management: Safe retrieval of GATEWAY_SECRET
    secret_str = os.getenv("GATEWAY_SECRET")
    if not secret_str:
        secret_str = "internal-gateway-secret-12345"
    secret = secret_str.encode()

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason is not None:
        headers["X-Change-Reason"] = change_reason
    if site_id is not None:
        headers["X-Site-Id"] = site_id
        headers["X-Assigned-Sites"] = site_id
    if sponsor_id is not None:
        headers["X-Sponsor-Id"] = sponsor_id
        headers["X-Assigned-Studies"] = sponsor_id
    if tenant_id is not None:
        headers["X-Tenant-Id"] = tenant_id
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"
    if sig_token is not None:
        headers["X-Sig-Token"] = sig_token
    return headers


def sponsor_admin(user_id: str = "test_sponsor_admin", **kwargs) -> dict:
    """Persona builder for sponsor_admin role."""
    return build_gateway_headers(user_id=user_id, roles="sponsor_admin", **kwargs)


def sponsor_designer(user_id: str = "test_sponsor_designer", **kwargs) -> dict:
    """Persona builder for sponsor_designer role."""
    return build_gateway_headers(user_id=user_id, roles="sponsor_designer", **kwargs)


def data_manager(user_id: str = "test_data_manager", **kwargs) -> dict:
    """Persona builder for data_manager role."""
    return build_gateway_headers(user_id=user_id, roles="sponsor_dm", **kwargs)


def cra(user_id: str = "test_cra", **kwargs) -> dict:
    """Persona builder for cra role."""
    return build_gateway_headers(user_id=user_id, roles="cra", **kwargs)


def crc(user_id: str = "test_crc", **kwargs) -> dict:
    """Persona builder for crc role."""
    return build_gateway_headers(user_id=user_id, roles="crc", **kwargs)


def investigator(user_id: str = "test_investigator", **kwargs) -> dict:
    """Persona builder for investigator role."""
    return build_gateway_headers(user_id=user_id, roles="investigator", **kwargs)


def auditor(user_id: str = "test_auditor", **kwargs) -> dict:
    """Persona builder for auditor role."""
    return build_gateway_headers(user_id=user_id, roles="auditor", **kwargs)


def external_monitor(user_id: str = "test_external_monitor", **kwargs) -> dict:
    """Persona builder for external_monitor role."""
    return build_gateway_headers(user_id=user_id, roles="external_monitor", **kwargs)
