import hashlib
import hmac
import io
import json
import time

import docx
import pytest
from fastapi.testclient import TestClient

from apps.designer.db import MOCK_STUDY_VERSIONS
from apps.designer.main import MOCK_PROTOCOL_INGESTIONS
from apps.designer.main import app as designer_app


def get_designer_auth_headers(
    roles="sponsor_designer", change_reason="system_operation", user_id="123"
):
    """
    Generates v2 gateway HMAC signature headers for the given roles and change reason.
    """
    timestamp = str(time.time())
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
def clear_ingestions():
    MOCK_PROTOCOL_INGESTIONS.clear()
    yield
    MOCK_PROTOCOL_INGESTIONS.clear()


def test_pdf_ingestion_success(client):
    """
    Validate successful PDF protocol document ingestion, producing candidate draft.

    Requirements: PRD-SYS-001
    """
    pdf_bytes = (
        b"%PDF-1.4\n%...\nSample Protocol content for Visit 1 and Vitals form\n%%EOF"
    )
    file_payload = {"file": ("protocol.pdf", pdf_bytes, "application/pdf")}

    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files=file_payload,
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 201
    data = response.json()
    assert "id" in data
    assert data["filename"] == "protocol.pdf"
    assert data["status"] == "PENDING_REVIEW"
    assert "cand_visit_1" in data["items"]
    assert "cand_field_1" in data["items"]

    # Verify confidence and source citations
    v1 = data["items"]["cand_visit_1"]
    assert v1["confidence"] == 0.90
    assert v1["confidence_level"] == "auto"
    assert "source_citation" in v1


def test_docx_ingestion_success(client):
    """
    Validate successful DOCX protocol document ingestion.

    Requirements: PRD-SYS-001
    """
    doc = docx.Document()
    doc.add_paragraph(
        "This is a study protocol containing Visit 1 details and Blood pressure questions."
    )
    out = io.BytesIO()
    doc.save(out)
    docx_bytes = out.getvalue()

    file_payload = {
        "file": (
            "protocol.docx",
            docx_bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        )
    }

    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files=file_payload,
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 201
    data = response.json()
    assert "id" in data
    assert data["filename"] == "protocol.docx"
    assert data["status"] == "PENDING_REVIEW"


def test_low_confidence_classification(client):
    """
    Validate low-confidence threshold classification when protocol file triggers keyword.

    Requirements: PRD-SYS-001
    """
    pdf_bytes = (
        b"%PDF-1.4\n%...\nSample Protocol content containing low-confidence text\n%%EOF"
    )
    file_payload = {"file": ("protocol.pdf", pdf_bytes, "application/pdf")}

    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files=file_payload,
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 201
    data = response.json()
    v1 = data["items"]["cand_visit_1"]
    assert v1["confidence_level"] == "low-confidence"


def test_malformed_or_unsupported_document(client):
    """
    Validate malformed, empty, or unsupported file formats return clear error status or 422.

    Requirements: PRD-SYS-001
    """
    # 1. Empty file
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files={"file": ("protocol.pdf", b"", "application/pdf")},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 422

    # 2. Unsupported extension
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files={"file": ("protocol.txt", b"Text file", "text/plain")},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 422

    # 3. Malformed PDF (does not start with %PDF)
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files={"file": ("protocol.pdf", b"Malformed content", "application/pdf")},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 422


def test_unauthorized_upload(client):
    """
    Verify RBAC checks prevent unauthorized upload or transition.

    Requirements: PRD-SYS-001
    """
    pdf_bytes = b"%PDF-1.4\n%...\nSample Protocol\n%%EOF"
    file_payload = {"file": ("protocol.pdf", pdf_bytes, "application/pdf")}

    # Using Auditor role (which is read-only and lacks ingestion perms)
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files=file_payload,
        headers=get_designer_auth_headers(roles="Auditor"),
    )
    assert response.status_code == 403


def test_candidate_item_review_transitions(client):
    """
    Verify ACCEPTED, REJECTED, and EDITED transitions, and that edit/reject require a mandatory change reason.

    Requirements: PRD-SYS-001
    """
    # 1. Generate candidate first
    pdf_bytes = b"%PDF-1.4\n%...\nSample Protocol content\n%%EOF"
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files={"file": ("protocol.pdf", pdf_bytes, "application/pdf")},
        headers=get_designer_auth_headers(),
    )
    candidate_id = response.json()["id"]

    # 2. Transition item to ACCEPTED
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_1/transition",
        json={"status": "ACCEPTED", "reason": "Looks good"},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 200
    candidate = response.json()
    assert candidate["items"]["cand_visit_1"]["review_status"] == "ACCEPTED"
    assert len(candidate["review_history"]) == 1

    # 3. Rejecting/Editing without reason fails with 400 Bad Request
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_2/transition",
        json={"status": "REJECTED", "reason": ""},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 400

    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_2/transition",
        json={"status": "EDITED", "reason": ""},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 400

    # 4. Reject with mandatory reason
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_2/transition",
        json={"status": "REJECTED", "reason": "Not applicable to this study"},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 200
    candidate = response.json()
    assert candidate["items"]["cand_visit_2"]["review_status"] == "REJECTED"

    # 5. Edit with mandatory reason
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_field_1/transition",
        json={
            "status": "EDITED",
            "reason": "Correcting parsed spelling",
            "label": "Systolic Blood Pressure (mmHg)",
        },
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 200
    candidate = response.json()
    assert candidate["items"]["cand_field_1"]["review_status"] == "EDITED"
    assert (
        candidate["items"]["cand_field_1"]["label"] == "Systolic Blood Pressure (mmHg)"
    )


def test_promotion_gates_and_draft_creation(client):
    """
    Validate that promotion blocks if any items are unreviewed, promotes only reviewed items,
    and writes strictly to a DRAFT study/protocol version.

    Requirements: PRD-SYS-001
    """
    # 1. Generate candidate first
    pdf_bytes = b"%PDF-1.4\n%...\nSample Protocol content\n%%EOF"
    response = client.post(
        "/api/v1/designer/ingestion/upload",
        files={"file": ("protocol.pdf", pdf_bytes, "application/pdf")},
        headers=get_designer_auth_headers(),
    )
    candidate_id = response.json()["id"]

    # 2. Promoting with unreviewed (PENDING) items fails with 400
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/promote",
        json={"change_reason": "Promoting CRF draft"},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 400
    assert "All candidate items must be reviewed" in response.json()["detail"]

    # 3. Review all items: Accept visit 1, Reject visit 2, Edit field 1, Accept field 2
    client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_1/transition",
        json={"status": "ACCEPTED", "reason": "Approved"},
        headers=get_designer_auth_headers(),
    )
    client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_visit_2/transition",
        json={"status": "REJECTED", "reason": "No need"},
        headers=get_designer_auth_headers(),
    )
    client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_field_1/transition",
        json={"status": "EDITED", "reason": "Adjusted label", "label": "SBP"},
        headers=get_designer_auth_headers(),
    )
    client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/items/cand_field_2/transition",
        json={"status": "ACCEPTED", "reason": "Approved"},
        headers=get_designer_auth_headers(),
    )

    # 4. Promote with missing change reason fails
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/promote",
        json={"change_reason": ""},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 400

    # 5. Promoting succeeds and creates draft study version containing only accepted/edited items
    response = client.post(
        f"/api/v1/designer/ingestion/candidates/{candidate_id}/promote",
        json={"change_reason": "Promote fully reviewed CRF builder draft"},
        headers=get_designer_auth_headers(),
    )
    assert response.status_code == 200
    promo_data = response.json()
    assert promo_data["status"] == "PROMOTED"

    # Assert created version is DRAFT and contains only accepted/edited items
    versions = MOCK_STUDY_VERSIONS["study_1"]
    created_ver = next(v for v in versions if v["id"] == promo_data["version_id"])
    assert created_ver["status"] == "DRAFT"

    promoted = created_ver["promoted_items"]
    # Accepted visit 1 is present, rejected visit 2 is omitted
    assert len(promoted["visits"]) == 1
    assert promoted["visits"][0]["id"] == "cand_visit_1"

    # Edited field 1 and accepted field 2 are present
    assert len(promoted["fields"]) == 2
    assert promoted["fields"][0]["id"] == "cand_field_1"
    assert promoted["fields"][0]["label"] == "SBP"
