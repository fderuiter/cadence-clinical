import re
import pytest
from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.designer.rendering import sanitize_filename, get_safe_filename, ensure_docx_template_exists
from tests.test_designer_differences import get_auth_headers


@pytest.fixture
def client():
    return TestClient(designer_app)


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
    import os
    assert os.path.exists(path)
    assert path.endswith(".docx")


def test_export_protocol_as_pdf_success(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with format=pdf returns a structurally
    valid PDF with correct headers and MIME types.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?format=pdf",
        headers=get_auth_headers(),
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
        headers=get_auth_headers(),
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
        headers=get_auth_headers(),
    )
    assert response.status_code == 404
    assert "Study not found" in response.json()["detail"]


def test_export_protocol_unsupported_format(client):
    """
    Verify GET /api/v1/studies/{study_id}/export with unsupported format returns HTTP 400.
    """
    response = client.get(
        "/api/v1/studies/study_1/export?format=txt",
        headers=get_auth_headers(),
    )
    assert response.status_code == 400
    assert "Unsupported format" in response.json()["detail"] or "validation-failed" in response.text
