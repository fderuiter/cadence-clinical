"""Integration test suite for protocol synopsis export API endpoints.

Requirements: PRD-SYS-001
"""

import os
import time

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.main import app
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "designer_test_user",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Export Protocol Synopsis",
) -> dict:
    """Generate signed Gateway authentication headers for testing apps/designer endpoints."""
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


def test_synopsis_export_post_html() -> None:
    """Validate POST /api/v1/synopsis/export returns Base64 encoded HTML content.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    response = client.post(
        "/api/v1/synopsis/export",
        json={
            "study_id": "study-test-101",
            "format": "html",
            "creator": "Lead Author",
            "change_reason": "Baseline Draft",
        },
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "study-test-101"
    assert data["format"] == "html"
    assert "content_base64" in data
    assert data["filename"] == "synopsis_study-test-101.html"


def test_synopsis_export_post_docx() -> None:
    """Validate POST /api/v1/synopsis/export returns Base64 encoded Word content.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    response = client.post(
        "/api/v1/synopsis/export",
        json={
            "study_id": "study-test-102",
            "format": "docx",
        },
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()
    assert data["format"] == "docx"
    assert data["filename"] == "synopsis_study-test-102.docx"


def test_synopsis_render_get_download() -> None:
    """Validate GET /api/v1/synopsis/render/{study_id} file download.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(change_reason="Download synopsis PDF")
    response = client.get(
        "/api/v1/synopsis/render/study-test-103?format=pdf",
        headers=headers,
    )
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/pdf"
    assert (
        "attachment; filename=synopsis_study-test-103.pdf"
        in response.headers["content-disposition"]
    )
    assert response.content.startswith(b"%PDF-")


def test_synopsis_export_invalid_format_returns_400() -> None:
    """Validate requesting invalid export format returns 400 Bad Request.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    response = client.post(
        "/api/v1/synopsis/export",
        json={
            "study_id": "study-test-104",
            "format": "invalid_format",
        },
        headers=headers,
    )
    assert response.status_code == 400
    assert "Unsupported export format" in response.json()["detail"]
