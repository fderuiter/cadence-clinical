"""
Compliance tests for the eISF service.
"""

import time

import pytest
from fastapi.testclient import TestClient

from apps.eisf.database import db_manager as eisf_db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base as EisfBase


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
) -> str:
    from packages.security.signing import generate_gateway_signature

    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    return generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret.encode(),
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
        unblinded_access=unblinded_access,
        tenant_id=tenant_id,
        sig_token=sig_token,
    )


@pytest.mark.asyncio
async def test_site_level_data_isolation():
    """
    Validation Suite - Site-level Data Isolation & Cross-site document access restrictions
    @req:PRD-SYS-004
    """
    # Initialize in-memory database for EISF
    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EisfBase.metadata.create_all)

    try:
        client = TestClient(eisf_app)

        # Helper to generate headers
        def get_auth_headers(user_id, roles, site_id):
            timestamp = str(time.time())
            sig = generate_signature(
                user_id,
                roles,
                timestamp,
                version="2",
                change_reason="Isolation Test",
                site_id=site_id,
            )
            return {
                "X-User-Id": user_id,
                "X-User-Roles": roles,
                "X-Gateway-Timestamp": timestamp,
                "X-Gateway-Signature": sig,
                "X-Signature-Version": "2",
                "X-Change-Reason": "Isolation Test",
                "X-Site-Id": site_id,
            }

        # Create a document for london site using admin role
        london_headers = get_auth_headers("admin-london", "admin", "site-london-02")
        payload = {
            "study_id": "study-100",
            "site_id": "site-london-02",
            "binder_classification": "Investigator CVs",
            "filename": "london_cv.pdf",
            "content": "London investigator CV content",
            "mime_type": "application/pdf",
            "reason_for_change": "Admin pre-population",
        }
        create_resp = client.post(
            "/api/v1/eisf/documents", json=payload, headers=london_headers
        )
        assert create_resp.status_code == 201
        london_doc_id = create_resp.json()["id"]

        # Attempt to access London document using Boston investigator headers
        boston_headers = get_auth_headers(
            "pi-boston", "site investigator", "site-boston-01"
        )
        get_resp = client.get(
            f"/api/v1/eisf/documents/{london_doc_id}", headers=boston_headers
        )
        assert get_resp.status_code == 403, "Cross-site access was not forbidden!"

    finally:
        async with eisf_db_manager.engine.begin() as conn:
            await conn.run_sync(EisfBase.metadata.drop_all)
        await eisf_db_manager.close()
