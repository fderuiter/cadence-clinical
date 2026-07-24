import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "test_user"
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
    return headers


@pytest.mark.asyncio
async def test_new_document_defaults_to_draft():
    """
    Verify newly ingested documents start in the DRAFT status.
    """
    # @req:PRD-QC-001
    client = TestClient(app)
    headers = get_auth_headers(roles="admin", change_reason="Ingest initial document")

    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "This is the clinical trial protocol for study 001. Enforces double blind randomized controls.",
        "mime_type": "application/pdf",
        "metadata_json": {"sponsor": "Acme Corp"},
    }
    response = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert response.status_code == 201
    doc_id = response.json()["document_id"]

    # Verify that the retrieved document has a default status of "DRAFT"
    doc_resp = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers)
    assert doc_resp.status_code == 200
    assert doc_resp.json()["status"] == "DRAFT"


@pytest.mark.asyncio
async def test_invalid_status_transition_raises_error():
    """
    Verify that invalid status values and forbidden transitions cannot be processed.
    """
    # @req:PRD-QC-002
    client = TestClient(app)
    headers_admin = get_auth_headers(
        roles="admin", change_reason="Setup and test transitions"
    )

    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=headers_admin
    )
    doc_id = ingest_resp.json()["document_id"]

    # 1. Attempt to transition to an invalid status
    transition_payload = {
        "to_status": "SUPER_QC_STATUS",
        "reason_for_change": "Transitioning with a valid Part 11 reason of sufficient length.",
    }
    res_invalid_status = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=transition_payload,
        headers=headers_admin,
    )
    assert res_invalid_status.status_code == 422
    assert "Invalid status" in res_invalid_status.json()["detail"]

    # 2. Attempt to transition from DRAFT straight to APPROVED (skipping TECHNICAL/CLINICAL QC)
    transition_skip_qc = {
        "to_status": "APPROVED",
        "reason_for_change": "Bypassing intermediate review checks.",
    }
    res_skip = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=transition_skip_qc,
        headers=headers_admin,
    )
    assert res_skip.status_code == 422
    assert "Invalid transition" in res_skip.json()["detail"]


@pytest.mark.asyncio
async def test_role_based_access_controls_and_gates():
    """
    Verify that target-stage-to-required-role mappings are strictly enforced.
    """
    # @req:PRD-QC-003
    client = TestClient(app)
    headers_admin = get_auth_headers(roles="admin", change_reason="Ingest doc")

    # Ingest document
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=headers_admin
    )
    doc_id = ingest_resp.json()["document_id"]

    # 1. Try to transition from DRAFT to TECHNICAL_QC using a non-permitted role (e.g., monitor or clinical reviewer)
    headers_clinical = get_auth_headers(
        roles="sponsor_clinical", change_reason="Attempt transition"
    )
    payload_transition = {
        "to_status": "TECHNICAL_QC",
        "reason_for_change": "Attempting to transition status without proper role.",
    }
    res_forbidden = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=payload_transition,
        headers=headers_clinical,
    )
    assert res_forbidden.status_code == 403
    assert "not authorized" in res_forbidden.json()["detail"]

    # 2. Transition successfully using permitted role (sponsor_dm)
    headers_dm = get_auth_headers(
        roles="sponsor_dm", change_reason="Execute technical QC"
    )
    res_permitted = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=payload_transition,
        headers=headers_dm,
    )
    assert res_permitted.status_code == 200
    assert res_permitted.json()["new_status"] == "TECHNICAL_QC"


@pytest.mark.asyncio
async def test_part11_change_reason_enforcement():
    """
    Verify that change reasons must comply with length constraints for 21 CFR Part 11 auditing.
    """
    # @req:PRD-QC-004
    client = TestClient(app)
    headers_admin = get_auth_headers(roles="admin", change_reason="Ingest doc")

    # Ingest document
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=headers_admin
    )
    doc_id = ingest_resp.json()["document_id"]

    # Try transition with too short reason
    payload_short = {
        "to_status": "TECHNICAL_QC",
        "reason_for_change": "Short",
    }
    res_short = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json=payload_short,
        headers=headers_admin,
    )
    # Fastapi validation of min_length=10 returns 422
    assert res_short.status_code == 422


@pytest.mark.asyncio
async def test_append_only_transition_history():
    """
    Verify that every status change creates a separately persisted, immutable history record.
    """
    # @req:PRD-QC-005
    client = TestClient(app)
    headers_admin = get_auth_headers(roles="admin", change_reason="Ingest doc")

    # Ingest document
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=headers_admin
    )
    doc_id = ingest_resp.json()["document_id"]

    # Perform a sequence of valid transitions:
    # 1. DRAFT -> TECHNICAL_QC (by sponsor_dm)
    # 2. TECHNICAL_QC -> CLINICAL_QC (by sponsor_clinical)
    # 3. CLINICAL_QC -> REJECTED (by sponsor_clinical)
    # 4. REJECTED -> DRAFT (by sponsor_dm)

    headers_dm = get_auth_headers(
        roles="sponsor_dm", change_reason="Executing technical QC transition"
    )
    headers_clinical = get_auth_headers(
        roles="sponsor_clinical", change_reason="Executing clinical QC transition"
    )

    # Step 1
    res1 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Completed technical QC check",
        },
        headers=headers_dm,
    )
    assert res1.status_code == 200

    # Step 2
    res2 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "CLINICAL_QC",
            "reason_for_change": "Completed clinical QC check",
        },
        headers=headers_clinical,
    )
    assert res2.status_code == 200

    # Step 3
    res3 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "REJECTED",
            "reason_for_change": "Fails protocol alignment guidelines",
        },
        headers=headers_clinical,
    )
    assert res3.status_code == 200

    # Step 4
    res4 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "DRAFT",
            "reason_for_change": "Reverted back to draft for correction",
        },
        headers=headers_dm,
    )
    assert res4.status_code == 200

    # Retrieve history and verify details
    history_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}/transitions",
        headers=headers_admin,
    )
    assert history_resp.status_code == 200
    history = history_resp.json()
    assert len(history) == 4

    # Verify transitions are logged in exact chronological order
    assert history[0]["from_status"] == "DRAFT"
    assert history[0]["to_status"] == "TECHNICAL_QC"
    assert "sponsor_dm" in history[0]["actor_role"]
    assert history[0]["reason_for_change"] == "Completed technical QC check"

    assert history[1]["from_status"] == "TECHNICAL_QC"
    assert history[1]["to_status"] == "CLINICAL_QC"
    assert "sponsor_clinical" in history[1]["actor_role"]
    assert history[1]["reason_for_change"] == "Completed clinical QC check"

    assert history[2]["from_status"] == "CLINICAL_QC"
    assert history[2]["to_status"] == "REJECTED"
    assert "sponsor_clinical" in history[2]["actor_role"]
    assert history[2]["reason_for_change"] == "Fails protocol alignment guidelines"

    assert history[3]["from_status"] == "REJECTED"
    assert history[3]["to_status"] == "DRAFT"
    assert "sponsor_dm" in history[3]["actor_role"]
    assert history[3]["reason_for_change"] == "Reverted back to draft for correction"

    # Verify document is currently in DRAFT status
    doc_resp = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=headers_admin)
    assert doc_resp.json()["status"] == "DRAFT"


@pytest.mark.asyncio
async def test_qc_transitions_missing_doc():
    """
    Verify querying or transitioning a non-existent document yields 404.
    """
    client = TestClient(app)
    headers_admin = get_auth_headers(
        roles="admin", change_reason="Execute transition check"
    )

    res_trans = client.post(
        "/api/v1/etmf/documents/nonexistent-id/transition",
        json={"to_status": "TECHNICAL_QC", "reason_for_change": "Some random reason"},
        headers=headers_admin,
    )
    assert res_trans.status_code == 404

    res_history = client.get(
        "/api/v1/etmf/documents/nonexistent-id/transitions",
        headers=headers_admin,
    )
    assert res_history.status_code == 404
