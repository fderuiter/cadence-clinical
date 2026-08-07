"""Integration test suite for granular data locking and unlocking REST API.

Requirements: PRD-SYS-001
"""

import os
import time

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.main import app
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "datamanager_test_user",
    roles: str = "datamanager",
    change_reason: str = "Execute Data Locking Operation",
) -> dict:
    """Generate signed Gateway authentication headers for apps/execution endpoints."""
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id="tenant_default",
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }


def test_lock_data_post_endpoint() -> None:
    """Validate POST /api/v1/execution/locks/lock executes data lock.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    response = client.post(
        "/api/v1/execution/locks/lock",
        json={
            "study_id": "study_test_lock_01",
            "subject_id": "sub_101",
            "form_id": "form_vs_01",
            "scope": "FORM",
            "action": "LOCK",
            "reason_for_change": "Database freeze prior to interim analysis",
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "LOCKED"
    assert "lock_id" in data
    assert data["record"]["form_id"] == "form_vs_01"


def test_unlock_data_post_endpoint() -> None:
    """Validate POST /api/v1/execution/locks/unlock executes data unlock override.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(change_reason="Discrepancy resolution approved by CRA")
    response = client.post(
        "/api/v1/execution/locks/unlock",
        json={
            "study_id": "study_test_lock_01",
            "subject_id": "sub_101",
            "form_id": "form_vs_01",
            "scope": "FORM",
            "action": "UNLOCK",
            "reason_for_change": "Discrepancy resolution approved by CRA",
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "UNLOCKED"
    assert "Data lock successfully unlocked" in data["message"]


def test_get_form_lock_status_endpoint() -> None:
    """Validate GET /api/v1/execution/locks/status/{form_id} returns active locks.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()

    # Create lock record first
    client.post(
        "/api/v1/execution/locks/lock",
        json={
            "study_id": "study_test_lock_02",
            "subject_id": "sub_102",
            "form_id": "form_lb_02",
            "scope": "FIELD",
            "field_name": "LBORRES",
            "action": "LOCK",
            "reason_for_change": "Lock lab test result",
        },
        headers=headers,
    )

    response = client.get("/api/v1/execution/locks/status/form_lb_02", headers=headers)

    assert response.status_code == 200
    records = response.json()
    assert isinstance(records, list)
    assert len(records) >= 1
    assert records[0]["form_id"] == "form_lb_02"
    assert records[0]["field_name"] == "LBORRES"


def test_lock_data_missing_reason_returns_400() -> None:
    """Validate executing data lock without reason_for_change returns 400 Bad Request.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    response = client.post(
        "/api/v1/execution/locks/lock",
        json={
            "study_id": "study_test_lock_03",
            "subject_id": "sub_103",
            "form_id": "form_ae_03",
            "scope": "FORM",
            "action": "LOCK",
            "reason_for_change": "   ",
        },
        headers=headers,
    )

    assert response.status_code == 400
    assert "Reason for change is required" in response.json()["detail"]
