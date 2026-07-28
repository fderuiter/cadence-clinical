"""
Comprehensive compliance and integration tests for the digital Delegation of Authority (DOA) workflow,
including PI scope validation, Part 11 re-authentication, canonical signature verification, and full-history auditing.
"""

import time
import uuid
from datetime import datetime, timezone

import pytest
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select

from apps.org.database import db_manager
from apps.org.main import app, GATEWAY_SECRET
from apps.org.models import Base, Personnel, Site, OrgAuditLog, DelegationOfAuthority
from packages.security.signing import generate_gateway_signature, generate_canonical_signature


@pytest.fixture(name="db_session_fixture")
async def db_session_fixture():
    """
    Initializes a test in-memory SQLite database, creates all tables,
    and yields an active database session.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        yield session

    await db_manager.close()


def get_scope_auth_headers(
    user_id: str,
    roles: str,
    site_id: str = None,
    sponsor_id: str = None,
    change_reason: str = "Standard Access",
) -> dict:
    """
    Generates fully compliant, scope-aware gateway authentication headers.
    """
    timestamp = str(time.time())
    secret = b"internal-gateway-secret-12345"
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id=site_id,
        sponsor_id=sponsor_id,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sponsor_id:
        headers["X-Sponsor-Id"] = sponsor_id
    return headers


def generate_sig_token(user_id: str, action: str) -> str:
    """
    Generates a mock Part 11 step-up re-authentication token (X-Sig-Token).
    """
    secret = "internal-gateway-secret-12345"
    payload = {
        "sub": user_id,
        "action": action,
        "exp": time.time() + 3600,
        "jti": str(uuid.uuid4())
    }
    return jwt.encode(payload, secret, algorithm="HS256")


@pytest.mark.asyncio
async def test_complete_doa_workflow_lifecycle(db_session_fixture) -> None:
    """
    E2E integration test covering Delegation Grant, Sign-off, Revocation, History,
    and GxP Auditing with role enforcement and signature re-authentication.
    """
    with TestClient(app) as client:
        # Create Site
        site_payload = {
            "site_id": "site_100",
            "name": "Camelot Site",
            "organization_id": "org_1",
            "study_id": "study_alpha",
            "reason_for_change": "Initial site setup",
        }
        site_headers = get_scope_auth_headers("admin", "admin")
        site_resp = client.post("/api/v1/org/sites", json=site_payload, headers=site_headers)
        assert site_resp.status_code == 201

        # Create PI Personnel
        pi_payload = {
            "keycloak_user_id": "kc-pi-001",
            "first_name": "Arthur",
            "last_name": "Pendragon",
            "email": "pi.arthur@camelot.org",
            "role": "Principal Investigator",
            "site_id": "site_100",
            "study_id": "study_alpha",
            "reason_for_change": "Initial PI setup",
        }
        pi_resp = client.post("/api/v1/org/personnel", json=pi_payload, headers=site_headers)
        assert pi_resp.status_code == 201
        pi_id = pi_resp.json()["id"]

        # Create CRC Personnel
        crc_payload = {
            "keycloak_user_id": "kc-crc-001",
            "first_name": "Gwen",
            "last_name": "Guinevere",
            "email": "crc.gwen@camelot.org",
            "role": "CRC",
            "site_id": "site_100",
            "study_id": "study_alpha",
            "reason_for_change": "Initial CRC setup",
        }
        crc_resp = client.post("/api/v1/org/personnel", json=crc_payload, headers=site_headers)
        assert crc_resp.status_code == 201
        crc_id = crc_resp.json()["id"]

        # A. Grant Delegation (Succeeds)
        grant_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_100",
            change_reason="Granting data-entry rights to Gwen",
        )
        grant_payload = {
            "delegator_id": pi_id,
            "delegatee_id": crc_id,
            "site_id": "site_100",
            "study_id": "study_alpha",
            "duties": ["Informed Consent", "CRF Data Entry"],
            "start_date": datetime.now(timezone.utc).isoformat(),
            "reason_for_change": "Onboarding coordinator",
        }

        grant_resp = client.post("/api/v1/org/delegations", json=grant_payload, headers=grant_headers)
        assert grant_resp.status_code == 201
        doa_data = grant_resp.json()
        assert doa_data["is_active"] is True
        assert doa_data["version_index"] == 1
        doa_id = doa_data["id"]

        # B. Non-PI Delegation Attempt (Rejected with 403)
        bad_grant_headers = get_scope_auth_headers(
            user_id="kc-crc-001",
            roles="CRC",
            site_id="site_100",
            change_reason="Hack attempt",
        )
        bad_grant_resp = client.post("/api/v1/org/delegations", json=grant_payload, headers=bad_grant_headers)
        assert bad_grant_resp.status_code == 403

        # C. Invalid Scope Attempt (Site ID mismatch, rejected with 403)
        wrong_scope_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_200",  # Mismatching site
            change_reason="Grant with wrong scope",
        )
        wrong_scope_resp = client.post("/api/v1/org/delegations", json=grant_payload, headers=wrong_scope_headers)
        assert wrong_scope_resp.status_code == 403

        # D. Sign Delegation (Succeeds with correct re-auth & signature)
        # Build canonical payload
        canonical_payload = {
            "id": doa_id,
            "delegator_id": pi_id,
            "delegatee_id": crc_id,
            "site_id": "site_100",
            "study_id": "study_alpha",
            "duties": ["Informed Consent", "CRF Data Entry"],
            "start_date": doa_data["start_date"],
        }
        # Compute signature using GATEWAY_SECRET
        valid_sig = generate_canonical_signature(canonical_payload, GATEWAY_SECRET)

        sign_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_100",
            change_reason="Signing delegation of authority",
        )
        # Generate re-auth step-up token for /sign-off endpoint
        sig_token = generate_sig_token("kc-pi-001", f"/api/v1/org/delegations/{doa_id}/sign-off")
        sign_headers["X-Sig-Token"] = sig_token

        sign_payload = {
            "payload": canonical_payload,
            "signature": valid_sig,
            "reason_for_change": "I verify and execute this delegation",
        }

        sign_resp = client.post(f"/api/v1/org/delegations/{doa_id}/sign-off", json=sign_payload, headers=sign_headers)
        assert sign_resp.status_code == 200
        signed_doa = sign_resp.json()
        assert signed_doa["version_index"] == 2
        assert signed_doa["signature"] == valid_sig
        assert signed_doa["signed_by"] == "kc-pi-001"
        assert signed_doa["signed_payload"] == canonical_payload

        # E. Sign Delegation with Missing Re-Authentication Token (Rejected with 401)
        no_auth_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_100",
        )
        no_auth_resp = client.post(f"/api/v1/org/delegations/{doa_id}/sign-off", json=sign_payload, headers=no_auth_headers)
        assert no_auth_resp.status_code == 401

        # F. Sign Delegation with Tampered Payload (Rejected with 400)
        tampered_payload = canonical_payload.copy()
        tampered_payload["duties"] = ["Informed Consent", "CRF Data Entry", "Supervising Labs"] # Added extra duty!
        tampered_sign_payload = {
            "payload": tampered_payload,
            "signature": valid_sig, # Reusing original signature (mismatch)
            "reason_for_change": "I execute this delegation with added duties",
        }
        tampered_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_100",
        )
        tampered_headers["X-Sig-Token"] = generate_sig_token("kc-pi-001", f"/api/v1/org/delegations/{doa_id}/sign-off")

        tampered_resp = client.post(f"/api/v1/org/delegations/{doa_id}/sign-off", json=tampered_sign_payload, headers=tampered_headers)
        assert tampered_resp.status_code == 400

        # G. Revoke Delegation (Succeeds)
        revoke_headers = get_scope_auth_headers(
            user_id="kc-pi-001",
            roles="Principal Investigator",
            site_id="site_100",
            change_reason="Revoking Gwen's access",
        )
        revoke_payload = {
            "reason_for_change": "Gwen has transferred to another department",
        }

        revoke_resp = client.post(f"/api/v1/org/delegations/{doa_id}/revoke", json=revoke_payload, headers=revoke_headers)
        assert revoke_resp.status_code == 200
        revoked_doa = revoke_resp.json()
        assert revoked_doa["is_active"] is False
        assert revoked_doa["version_index"] == 3
        assert revoked_doa["revocation_reason"] == "Gwen has transferred to another department"
        assert revoked_doa["revoked_by"] == "kc-pi-001"

        # H. List and Filter (Returns only the latest unique version)
        list_headers = get_scope_auth_headers("kc-pi-001", "Principal Investigator", "site_100")
        list_resp = client.get("/api/v1/org/delegations?site_id=site_100", headers=list_headers)
        assert list_resp.status_code == 200
        list_data = list_resp.json()
        assert len(list_data) == 1
        assert list_data[0]["id"] == doa_id
        assert list_data[0]["is_active"] is False
        assert list_data[0]["version_index"] == 3

        # I. Retrieve Specific Versions (History)
        v1_headers = get_scope_auth_headers("kc-pi-001", "Principal Investigator", "site_100")
        v1_resp = client.get(f"/api/v1/org/delegations/{doa_id}?version_index=1", headers=v1_headers)
        assert v1_resp.status_code == 200
        assert v1_resp.json()["version_index"] == 1
        assert v1_resp.json()["signature"] is None

        v2_resp = client.get(f"/api/v1/org/delegations/{doa_id}?version_index=2", headers=v1_headers)
        assert v2_resp.status_code == 200
        assert v2_resp.json()["version_index"] == 2
        assert v2_resp.json()["signature"] == valid_sig

        # J. Full Version History
        history_resp = client.get(f"/api/v1/org/delegations/{doa_id}/history", headers=v1_headers)
        assert history_resp.status_code == 200
        history_data = history_resp.json()
        assert len(history_data) == 3
        assert history_data[0]["version_index"] == 3
        assert history_data[1]["version_index"] == 2
        assert history_data[2]["version_index"] == 1

        # K. GxP Audit Trail Verifications
        audit_headers = get_scope_auth_headers("kc-pi-001", "Principal Investigator", "site_100")
        audit_resp = client.get("/api/v1/org/audit-logs", headers=audit_headers)
        assert audit_resp.status_code == 200
        audit_logs = audit_resp.json()

        # Verify specific actions logged
        actions = [log["action"] for log in audit_logs]
        assert "DELEGATION_GRANT" in actions
        assert "DELEGATION_SIGN" in actions
        assert "DELEGATION_REVOKE" in actions
        assert "DELEGATION_LIST" in actions
        assert "DELEGATION_VIEW" in actions
        assert "DELEGATION_HISTORY" in actions
