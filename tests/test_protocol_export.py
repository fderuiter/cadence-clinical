import hashlib
import hmac
import json
import os
import re
import time

import pytest
from fastapi.testclient import TestClient

from apps.designer.db import MOCK_DESIGNER_AUDIT_LOGS
from apps.designer.main import app as designer_app
from apps.designer.rendering import (
    ensure_docx_template_exists,
    get_safe_filename,
    sanitize_filename,
)


def get_custom_auth_headers(change_reason="system_operation"):
    timestamp = str(time.time())
    user_id = "123"
    roles = "admin"
    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        secret.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.fixture
def client():
    return TestClient(designer_app)


@pytest.fixture(autouse=True)
def clear_audit_logs():
    MOCK_DESIGNER_AUDIT_LOGS.clear()
    yield
    MOCK_DESIGNER_AUDIT_LOGS.clear()


def test_sanitize_filename():
    """
    Verify that sanitize_filename filters out dangerous or unsupported characters.
    """
    assert sanitize_filename("study_123") == "study_123"
    assert sanitize_filename("study/123/../../etc") == "study_123_.._.._etc"
    assert sanitize_filename("My Protocol #1!") == "My_Protocol_1"
    assert sanitize_filename("space test   and symbols @#$") == "space_test_and_symbols"


def test_get_safe_filename():
    """
    Verify safe and deterministic suggested filename derivation.
    """
    filename = get_safe_filename("study/123", 4, "pdf")
    assert filename == "protocol_study_123_v4.pdf"

    filename_docx = get_safe_filename(" oncology-trial ", 1, "docx")
    assert filename_docx == "protocol_oncology-trial_v1.docx"


def test_ensure_docx_template_exists():
    """
    Ensure the version-controlled docxtpl base template exists or is generated correctly.
    """
    path = ensure_docx_template_exists()
    assert os.path.exists(path)
    assert path.endswith(".docx")


def test_export_protocol_as_pdf_success(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with format=pdf returns a structurally
    valid PDF with correct headers and MIME types.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?format=pdf",
        headers=get_custom_auth_headers(),
    )
    if response.status_code != 200:
        print("ERROR RESPONSE DETAIL:", response.text)
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/pdf"

    # Verify Content-Disposition header with safe, suggested filename
    disp = response.headers["content-disposition"]
    assert "attachment; filename=" in disp
    assert re.search(r'filename="protocol_.*_v\d+\.pdf"', disp)

    # Verify structural PDF signature (%PDF)
    content = response.content
    assert len(content) > 0
    assert content.startswith(b"%PDF")


def test_export_protocol_as_docx_success(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with format=docx returns a valid
    Office Open XML document with correct headers and MIME types.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?format=docx",
        headers=get_custom_auth_headers(),
    )
    if response.status_code != 200:
        print("ERROR RESPONSE DETAIL:", response.text)
    assert response.status_code == 200
    assert (
        response.headers["content-type"]
        == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    )

    # Verify Content-Disposition header with safe, suggested filename
    disp = response.headers["content-disposition"]
    assert "attachment; filename=" in disp
    assert re.search(r'filename="protocol_.*_v\d+\.docx"', disp)

    # Verify OpenXML (ZIP) zipfile structure prefix (PK\x03\x04)
    content = response.content
    assert len(content) > 0
    assert content.startswith(b"PK\x03\x04")


def test_export_protocol_not_found(client):
    """
    Verify GET /api/v1/studies/{study_id}/export for non-existent study returns HTTP 404.
    """
    response = client.get(
        "/api/v1/studies/invalid_study_id_999/export?format=pdf",
        headers=get_custom_auth_headers(),
    )
    assert response.status_code == 404
    assert "Study not found" in response.json()["detail"]


def test_export_protocol_unsupported_format(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with unsupported format returns HTTP 422.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?format=txt",
        headers=get_custom_auth_headers(),
    )
    assert response.status_code == 422
    assert "Invalid format" in response.json()["detail"]


def test_export_protocol_invalid_output(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with unsupported output returns HTTP 422.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?output=invalid_section",
        headers=get_custom_auth_headers(),
    )
    assert response.status_code == 422
    assert "Invalid output" in response.json()["detail"]


def test_export_protocol_outputs_rendering(client):
    """
    Verify that all supported output sections (narrative, synopsis, soa) render successfully in PDF and DOCX.
    """
    for out in ("narrative", "synopsis", "soa", "combined"):
        for fmt in ("pdf", "docx"):
            response = client.get(
                f"/api/v1/studies/study_1/export?format={fmt}&output={out}",
                headers=get_custom_auth_headers(),
            )
            assert response.status_code == 200
            expected_mime = (
                "application/pdf"
                if fmt == "pdf"
                else "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            )
            assert response.headers["content-type"] == expected_mime


def test_export_protocol_generation_auditing(client):
    """
    Verify that successful exports are tracked in the Part 11 compliant audit trail,
    capturing caller identity, change reason, and output selection.
    """
    headers = get_custom_auth_headers(change_reason="Regulatory submission export")

    response = client.get(
        "/api/v1/studies/study_1/export?format=pdf&output=synopsis",
        headers=headers,
    )
    assert response.status_code == 200

    # Assert that one audit log event was successfully appended
    assert len(MOCK_DESIGNER_AUDIT_LOGS) == 1
    event = MOCK_DESIGNER_AUDIT_LOGS[0]
    assert event["actor"] == "123"  # matches get_custom_auth_headers user_id
    assert event["change_reason"] == "Regulatory submission export"
    assert event["study_id"] == "study_1"
    assert event["format"] == "pdf"
    assert event["output"] == "synopsis"
    assert event["type"] == "PROTOCOL_EXPORT"
    assert "timestamp" in event
    assert "id" in event


def test_export_protocol_etmf_forwarding_best_effort(client, monkeypatch):
    """
    Verify that when ETMF_STRICT_ARCHIVAL is false (default), forwarding failures do NOT block
    or invalidate a successful real-time document export.
    """
    # Force best-effort archival configuration
    monkeypatch.setenv("ETMF_FORWARDING_ENABLED", "true")
    monkeypatch.setenv("ETMF_STRICT_ARCHIVAL", "false")
    monkeypatch.setenv("ETMF_URL", "http://invalid-non-existent-etmf-url:9999")

    # The export should succeed normally even with non-existent ETMF URL (warnings logged)
    response = client.get(
        "/api/v1/studies/study_1/export?format=pdf",
        headers=get_custom_auth_headers(),
    )
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/pdf"


def test_export_protocol_etmf_forwarding_strict_failure(client, monkeypatch):
    """
    Verify that when ETMF_STRICT_ARCHIVAL is true, forwarding failures explicitly
    propagate and invalidate the export transaction.
    """
    # Force strict archival configuration
    monkeypatch.setenv("ETMF_FORWARDING_ENABLED", "true")
    monkeypatch.setenv("ETMF_STRICT_ARCHIVAL", "true")
    monkeypatch.setenv("ETMF_URL", "http://invalid-non-existent-etmf-url:9999")

    # The export should raise HTTP 500 on strict archival failure
    response = client.get(
        "/api/v1/studies/study_1/export?format=pdf",
        headers=get_custom_auth_headers(),
    )
    assert response.status_code == 500
    assert "Strict Archival Failure" in response.json()["detail"]
