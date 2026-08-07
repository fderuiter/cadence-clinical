"""Unit and integration test suite for the form designer comments REST API.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
import time

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.db import MOCK_DESIGNER_AUDIT_LOGS
from apps.designer.main import app
from apps.designer.routers.comments import MOCK_FORM_COMMENTS
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "designer_test_user",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Review form comment modification",
) -> dict:
    """Generate signed Gateway authentication headers for testing comments router."""
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


def test_create_and_retrieve_form_comments() -> None:
    """Validate posting and getting review comments for a form.

    Requirements: PRD-SYS-001
    """
    # Clear prior mock state
    MOCK_FORM_COMMENTS.clear()
    form_id = "test_form_age"
    headers = _make_auth_headers()

    # 1. Fetch initial comments (should be empty)
    response = client.get(
        f"/api/v1/designer/forms/{form_id}/comments",
        headers=headers,
    )
    assert response.status_code == 200
    assert response.json() == []

    # 2. Post a comment
    post_payload = {
        "field_id": "field_age_input",
        "comment_text": "Please double-check age min/max boundary constraints.",
    }
    post_response = client.post(
        f"/api/v1/designer/forms/{form_id}/comments",
        json=post_payload,
        headers=headers,
    )
    assert post_response.status_code == 201
    comment_data = post_response.json()
    assert "id" in comment_data
    assert comment_data["form_id"] == form_id
    assert comment_data["field_id"] == "field_age_input"
    assert (
        comment_data["comment_text"]
        == "Please double-check age min/max boundary constraints."
    )
    assert comment_data["status"] == "Open"
    assert comment_data["isResolved"] is False
    assert comment_data["authorName"] == "designer_test_user"

    # 3. Retrieve form comments and verify list has the comment
    get_response = client.get(
        f"/api/v1/designer/forms/{form_id}/comments",
        headers=headers,
    )
    assert get_response.status_code == 200
    comments_list = get_response.json()
    assert len(comments_list) == 1
    assert comments_list[0]["id"] == comment_data["id"]
    assert comments_list[0]["comment_text"] == comment_data["comment_text"]


def test_resolve_form_comment_logs_gxp_audit() -> None:
    """Validate that patching a comment to resolve transitions status and logs a GxP audit event.

    Requirements: PRD-SYS-001 | GxP 21 CFR Part 11
    """
    # Clear prior mock state
    MOCK_FORM_COMMENTS.clear()
    initial_audit_log_count = len(MOCK_DESIGNER_AUDIT_LOGS)

    form_id = "test_form_vitals"
    headers = _make_auth_headers()

    # 1. Create comment
    post_response = client.post(
        f"/api/v1/designer/forms/{form_id}/comments",
        json={"field_id": "vitals_sbp", "comment_text": "Check standard unit."},
        headers=headers,
    )
    comment_id = post_response.json()["id"]

    # 2. Resolve comment
    patch_response = client.patch(
        f"/api/v1/designer/comments/{comment_id}/resolve",
        headers=headers,
    )
    assert patch_response.status_code == 200
    updated_comment = patch_response.json()
    assert updated_comment["status"] == "Resolved"
    assert updated_comment["isResolved"] is True

    # 3. Check that GxP Audit Log entry was added to MOCK_DESIGNER_AUDIT_LOGS
    assert len(MOCK_DESIGNER_AUDIT_LOGS) == initial_audit_log_count + 1
    latest_audit = MOCK_DESIGNER_AUDIT_LOGS[-1]
    assert latest_audit["type"] == "FORM_COMMENT_RESOLVE"
    assert latest_audit["comment_id"] == comment_id
    assert latest_audit["actor"] == "designer_test_user"
    assert "timestamp" in latest_audit
    assert "Resolved review comment thread" in latest_audit["change_reason"]
