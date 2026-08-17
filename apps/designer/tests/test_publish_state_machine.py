import os
import time
from unittest.mock import patch

import httpx
import pytest
from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.db import (
    MOCK_DESIGNER_AUDIT_LOGS,
    MOCK_STUDY_PROJECTIONS_BY_VERSION,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", default="internal-gateway-secret-12345"
).encode("utf-8")


def _make_auth_headers(
    user_id: str = "designer_test_user",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Publish Study Version",
) -> dict:
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


@pytest.fixture(autouse=True)
def clean_stores():
    """Ensure mock stores are fresh for each test."""
    MOCK_STUDY_VERSIONS.clear()
    MOCK_STUDY_PROJECTIONS_BY_VERSION.clear()
    MOCK_DESIGNER_AUDIT_LOGS.clear()
    yield


@pytest.fixture
def mock_client():
    with patch("httpx.Client") as mock_class:
        client_instance = mock_class.return_value.__enter__.return_value
        yield client_instance


def test_publish_state_machine_success(mock_client) -> None:
    """Verify successful end-to-end publish flow:
    1. Status starts as DRAFT.
    2. Status moves to PENDING_PUBLISH, then to ACTIVE.
    3. Returns 200 OK.
    """
    study_id = "study_sm_1"
    version_id = "ver_sm_1"
    
    # Setup mock data
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "2.0",
            "parent_version": "1.0",
            "status": "DRAFT",
            "version_index": 2,
            "created_by": "designer_test_user",
        }
    ]
    
    # Mock successful downstream call
    mock_response = httpx.Response(
        status_code=200,
        json={
            "amendment_id": "amd_success_123",
            "summary_of_changes": "Protocol amendment summary"
        },
        request=httpx.Request("POST", "http://test")
    )
    mock_client.post.return_value = mock_response

    headers = _make_auth_headers()
    response = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/publish",
        json={},
        headers=headers,
    )

    assert response.status_code == 200, response.text
    data = response.json()
    assert data["status"] == "ACTIVE"
    assert data["version_id"] == version_id
    assert data["amendment_id"] == "amd_success_123"
    assert data["summary_of_changes"] == "Protocol amendment summary"

    # Verify state in database is finalized to ACTIVE
    assert MOCK_STUDY_VERSIONS[study_id][0]["status"] == "ACTIVE"


def test_publish_state_machine_rollback_on_downstream_error(mock_client) -> None:
    """Verify that a downstream failure triggers a compensating rollback to DRAFT
    and records a detailed audit trail entry.
    """
    study_id = "study_sm_2"
    version_id = "ver_sm_2"
    
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "2.0",
            "parent_version": "1.0",
            "status": "DRAFT",
            "version_index": 2,
            "created_by": "designer_test_user",
        }
    ]

    # Mock failing downstream response
    response_mock = httpx.Response(
        status_code=400,
        json={"detail": "Downstream migration failed due to clinical validation error"},
        request=httpx.Request("POST", "http://test")
    )
    mock_client.post.side_effect = httpx.HTTPStatusError("Bad Request", request=None, response=response_mock)

    headers = _make_auth_headers()
    response = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/publish",
        json={},
        headers=headers,
    )

    # Propagate exact execution error with 422 Unprocessable Entity
    assert response.status_code == 422
    assert "PUBLISH_FAILED" in response.json()["detail"]
    assert "Downstream migration failed" in response.json()["detail"]

    # Verify state has rolled back to DRAFT
    assert MOCK_STUDY_VERSIONS[study_id][0]["status"] == "DRAFT"

    # Verify audit trail contains corresponding entry
    rollback_audits = [
        log for log in MOCK_DESIGNER_AUDIT_LOGS
        if log.get("type") == "PUBLISH_ROLLBACK"
    ]
    assert len(rollback_audits) == 1
    audit = rollback_audits[0]
    assert audit["study_id"] == study_id
    assert audit["version_id"] == version_id
    assert "Downstream migration failed" in audit["error_message"]
    assert "designer_test_user" in audit["actor"]


def test_publish_state_machine_rollback_on_timeout(mock_client) -> None:
    """Verify 15-second timeout handling and rollback."""
    study_id = "study_sm_3"
    version_id = "ver_sm_3"
    
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "2.0",
            "parent_version": "1.0",
            "status": "DRAFT",
            "version_index": 2,
            "created_by": "designer_test_user",
        }
    ]

    # Mock timeout exception
    mock_client.post.side_effect = httpx.TimeoutException("Downstream server timed out.")

    headers = _make_auth_headers()
    response = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/publish",
        json={},
        headers=headers,
    )

    assert response.status_code == 422
    assert "PUBLISH_FAILED" in response.json()["detail"]
    assert "timed out" in response.json()["detail"]

    # Verify status reverts to DRAFT
    assert MOCK_STUDY_VERSIONS[study_id][0]["status"] == "DRAFT"

    # Verify audit log exists
    rollback_audits = [
        log for log in MOCK_DESIGNER_AUDIT_LOGS
        if log.get("type") == "PUBLISH_ROLLBACK"
    ]
    assert len(rollback_audits) == 1
    assert "timed out" in rollback_audits[0]["error_message"]


def test_publish_state_machine_lock_freed(mock_client) -> None:
    """Verify that lock resources are freed and allow subsequent calls."""
    study_id = "study_sm_4"
    version_id = "ver_sm_4"
    
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "2.0",
            "parent_version": "1.0",
            "status": "DRAFT",
            "version_index": 2,
            "created_by": "designer_test_user",
        }
    ]

    # 1. Trigger a failing publish (raises timeout) to verify the lock is released during rollback
    mock_client.post.side_effect = httpx.TimeoutException("Timeout")

    headers = _make_auth_headers()
    response = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/publish",
        json={},
        headers=headers,
    )
    assert response.status_code == 422

    # 2. Trigger again - should not hit a concurrent publishing conflict!
    # Let this second one succeed
    mock_client.post.side_effect = None
    mock_client.post.return_value = httpx.Response(
        status_code=200,
        json={
            "amendment_id": "amd_success_456",
            "summary_of_changes": "Protocol amendment summary"
        },
        request=httpx.Request("POST", "http://test")
    )

    headers = _make_auth_headers()
    response2 = client.post(
        f"/api/v1/studies/{study_id}/versions/{version_id}/publish",
        json={},
        headers=headers,
    )
    assert response2.status_code == 200
    assert response2.json()["status"] == "ACTIVE"
