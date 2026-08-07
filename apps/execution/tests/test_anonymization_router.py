"""Integration test suite for PHI detection and document redaction REST API endpoints.

Requirements: PRD-SYS-001
"""

import base64

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.tests.test_lock_router import _make_auth_headers
from apps.execution.main import app

client = TestClient(app)


def test_scan_phi_post_endpoint() -> None:
    """Validate POST /api/v1/execution/anonymization/scan-phi detects PHI entities.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    sample_text = "Subject Phone: 555-987-6543, SSN: 999-88-7777"

    response = client.post(
        "/api/v1/execution/anonymization/scan-phi",
        json={"text": sample_text},
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["phi_detected_count"] >= 2
    assert "[REDACTED_PHONE]" in data["scrubbed_text_preview"]
    assert "[REDACTED_SSN]" in data["scrubbed_text_preview"]


def test_redact_pdf_post_endpoint() -> None:
    """Validate POST /api/v1/execution/anonymization/redact-pdf applies redactions to PDF.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    doc_bytes = b"Subject Name: Alice Smith, SSN: 123-45-6789."
    doc_b64 = base64.b64encode(doc_bytes).decode("utf-8")

    response = client.post(
        "/api/v1/execution/anonymization/redact-pdf",
        json={
            "pdf_base64": doc_b64,
            "target_snippets": ["Alice Smith"],
        },
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["is_clean"] is True
    assert data["redacted_entities_count"] >= 2

    redacted_bytes = base64.b64decode(data["redacted_pdf_base64"])
    assert b"Alice Smith" not in redacted_bytes
    assert b"123-45-6789" not in redacted_bytes
