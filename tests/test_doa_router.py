"""Integration test suite for CTMS Delegation of Authority (DOA) log REST API endpoints.

Requirements: PRD-SYS-001
"""

import os
import time

import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt

import packages  # noqa: F401
from apps.ctms.database import db_manager
from apps.ctms.main import app
from apps.ctms.models import Base
from apps.gateway.main import generate_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory CTMS database for unit and integration testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_ctms_auth_headers(
    roles: str = "CRA",
    change_reason: str = "DOA Configuration",
    action: str = None,
    user_id: str = "cra_user_01",
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing."""
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    if action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, "internal-gateway-secret-12345", algorithm="HS256"
        )
    return headers


def test_ctms_doa_lifecycle_flow():
    """Validate delegation creation, PI sign-off, log retrieval, PDF export, and revocation.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    site_id = "site_doa_101"
    staff_user_id = "kc-staff-001"

    # Step 1: Create a delegation assignment -> PENDING_PI_APPROVAL
    headers = get_ctms_auth_headers(roles="CRA", change_reason="Assign nurse duties")
    delegate_payload = {
        "site_id": site_id,
        "staff_user_id": staff_user_id,
        "task_codes": ["SUBJECT_INFORMED_CONSENT", "CRF_DATA_ENTRY"],
        "start_date": "2026-07-01",
        "reason_for_change": "Assigning trial nurse Jacqueline Thorne",
    }

    resp_delegate = client.post(
        "/api/v1/ctms/doa/delegate",
        json=delegate_payload,
        headers=headers,
    )

    assert resp_delegate.status_code == 201
    data_delegate = resp_delegate.json()
    assert data_delegate["status"] == "PENDING_PI_APPROVAL"
    assert data_delegate["site_id"] == site_id
    record_id = data_delegate["record_id"]

    # Step 2: Retrieve site DOA log and check states (record should be inactive and unsigned)
    headers_read = get_ctms_auth_headers(roles="Monitor")
    resp_log = client.get(
        f"/api/v1/ctms/doa/sites/{site_id}/log",
        headers=headers_read,
    )

    assert resp_log.status_code == 200
    data_log = resp_log.json()
    assert data_log["site_id"] == site_id
    assert len(data_log["delegated_staff"]) == 1
    staff_record = data_log["delegated_staff"][0]
    assert staff_record["record_id"] == record_id
    assert staff_record["is_active"] is False
    assert staff_record["signed_off"] is False

    # Check that audit log has DOA_LOG_MODIFIED event
    assert len(data_log["audit_history"]) >= 1
    assert data_log["audit_history"][0]["action"] == "DOA_LOG_MODIFIED"

    # Step 3: PI eSignature Sign-Off and activation
    sign_off_action = "/api/v1/ctms/doa/sign-off"
    headers_pi = get_ctms_auth_headers(
        roles="Principal Investigator",
        change_reason="PI Endorsement of nurse duties",
        action=sign_off_action,
        user_id="pi_user_301",
    )
    sign_payload = {
        "record_id": record_id,
        "reason_for_change": "Delegation approved with PI eSignature",
    }

    resp_sign = client.post(
        sign_off_action,
        json=sign_payload,
        headers=headers_pi,
    )

    assert resp_sign.status_code == 200
    data_sign = resp_sign.json()
    assert data_sign["status"] == "ACTIVE"
    assert data_sign["record_id"] == record_id
    assert data_sign["signed_off"] is True

    # Check updated site log (should be ACTIVE now)
    resp_log_2 = client.get(
        f"/api/v1/ctms/doa/sites/{site_id}/log",
        headers=headers_read,
    )
    assert resp_log_2.status_code == 200
    data_log_2 = resp_log_2.json()
    assert data_log_2["delegated_staff"][0]["is_active"] is True
    assert data_log_2["delegated_staff"][0]["signed_off"] is True
    # Audit trail should have multiple DOA_LOG_MODIFIED records
    assert len(data_log_2["audit_history"]) == 2

    # Step 4: Export signed PDF DOA log
    resp_pdf = client.get(
        f"/api/v1/ctms/doa/sites/{site_id}/export-pdf",
        headers=headers_read,
    )
    assert resp_pdf.status_code == 200
    assert resp_pdf.headers["content-type"] == "application/pdf"
    assert len(resp_pdf.content) > 0

    # Step 5: Revoke/end the task delegation
    revoke_payload = {
        "record_id": record_id,
        "reason_for_change": "Nurse Jacqueline Thorne resigned",
    }

    resp_revoke = client.post(
        "/api/v1/ctms/doa/revoke",
        json=revoke_payload,
        headers=headers,
    )

    assert resp_revoke.status_code == 200
    data_revoke = resp_revoke.json()
    assert data_revoke["status"] == "REVOKED"
    assert data_revoke["record_id"] == record_id

    # Verify log state is updated to inactive
    resp_log_3 = client.get(
        f"/api/v1/ctms/doa/sites/{site_id}/log",
        headers=headers_read,
    )
    assert resp_log_3.status_code == 200
    assert resp_log_3.json()["delegated_staff"][0]["is_active"] is False


def test_ctms_doa_rbac_violations():
    """Verify that unauthorized roles get rejected with 403 Forbidden.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    headers_unauth = get_ctms_auth_headers(
        roles="Site Investigator", change_reason="Attempting DOA modification"
    )

    delegate_payload = {
        "site_id": "site_doa_101",
        "staff_user_id": "kc-staff-001",
        "task_codes": ["SUBJECT_INFORMED_CONSENT"],
        "start_date": "2026-07-01",
        "reason_for_change": "Unauthorized assignment",
    }

    resp_delegate = client.post(
        "/api/v1/ctms/doa/delegate",
        json=delegate_payload,
        headers=headers_unauth,
    )
    assert resp_delegate.status_code == 403
