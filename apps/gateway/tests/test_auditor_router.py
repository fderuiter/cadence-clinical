"""Integration test suite for temporary auditor access token generation and audit trail inspection API.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.tests.test_lock_router import _make_auth_headers
from apps.execution.main import app

client = TestClient(app)


def test_generate_auditor_token_post_endpoint() -> None:
    """Validate POST /api/v1/execution/auditor/token/generate provisions auditor access token.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="qa_lead_01",
        roles="qa_manager",
        change_reason="Provision token for FDA inspection",
    )

    response = client.post(
        "/api/v1/execution/auditor/token/generate",
        json={
            "auditor_email": "fda_inspector_01@fda.gov",
            "study_id": "study_audit_api_01",
            "duration_hours": 48,
            "reason_for_access": "FDA Routine BIMO Inspection",
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert "access_token" in data
    assert data["auditor_email"] == "fda_inspector_01@fda.gov"
    assert data["study_id"] == "study_audit_api_01"


def test_inspect_study_audit_trail_endpoint() -> None:
    """Validate GET /api/v1/execution/auditor/inspect/audit-trail/{study_id} exports audit log records.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="auditor_user_99",
        roles="auditor",
        change_reason="Read-only audit inspection",
    )

    response = client.get(
        "/api/v1/execution/auditor/inspect/audit-trail/study_audit_api_01",
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study_audit_api_01"
    assert "audit_logs" in data
    assert isinstance(data["audit_logs"], list)
    assert len(data["audit_logs"]) >= 1
