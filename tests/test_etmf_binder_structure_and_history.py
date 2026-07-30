import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFAuditLog
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


def get_auth_headers(
    roles: str = "admin", change_reason: str = "", user_id: str = "test_user"
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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
async def test_empty_binder_structure():
    """
    Test empty binder scenario (fresh study_id with no documents).
    All catalog artifacts should be marked expected/missing (EXPECTED or MISSING), none present.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin,sponsor_dm")

    # Fetch structure for fresh study with milestone INITIATION (will seed EDL)
    response = client.get(
        "/api/v1/etmf/studies/study_empty/binder/structure?milestone=INITIATION",
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()

    assert data["study_id"] == "study_empty"
    assert data["milestone"] == "INITIATION"
    assert data["site_id"] is None
    assert len(data["zones"]) == 11  # DIA taxonomy has 11 zones

    # Since no documents exist, present_artifacts should be empty
    assert len(data["present_artifacts"]) == 0
    # For INITIATION, "Clinical Trial Protocol" (01.01.01) is expected
    assert "Clinical Trial Protocol" in data["missing_artifacts"]

    # Verify that Zone 1, Section "01.01", Artifact "01.01.01" is marked as MISSING
    zone_1 = next(z for z in data["zones"] if z["zone_code"] == 1)
    sec_1_1 = next(s for s in zone_1["sections"] if s["section_code"] == "01.01")
    art_1_1_1 = next(
        a for a in sec_1_1["artifacts"] if a["artifact_code"] == "01.01.01"
    )
    assert art_1_1_1["status"] == "MISSING"
    assert art_1_1_1["document_id"] is None
    assert art_1_1_1["version_index"] is None

    # Other non-expected artifacts in Zone 1 (e.g. 01.01.02) should be EXPECTED (or not present/optional)
    art_1_1_2 = next(
        a for a in sec_1_1["artifacts"] if a["artifact_code"] == "01.01.02"
    )
    assert art_1_1_2["status"] == "EXPECTED"

    # Verify that BINDER_STRUCTURE_VIEW audit log is created
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFAuditLog).where(TMFAuditLog.action == "BINDER_STRUCTURE_VIEW")
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        assert "study_empty" in logs[0].details


@pytest.mark.asyncio
async def test_partial_binder_structure():
    """
    Test a partial binder scenario.
    Ingest a document, check that it is PRESENT, expected ones are MISSING, and other ones EXPECTED.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingest protocol document"
    )

    # 1. Ingest Clinical Trial Protocol (01.01.01) for study_partial
    payload = {
        "study_id": "study_partial",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    resp_ingest = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp_ingest.status_code == 201
    doc_data = resp_ingest.json()
    doc_id = doc_data["id"]

    # 2. Query structure for milestone INITIATION
    response = client.get(
        "/api/v1/etmf/studies/study_partial/binder/structure?milestone=INITIATION",
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()

    assert data["study_id"] == "study_partial"
    assert "Clinical Trial Protocol" in data["present_artifacts"]
    assert "Clinical Trial Protocol" not in data["missing_artifacts"]

    # Verify status is PRESENT for 01.01.01
    zone_1 = next(z for z in data["zones"] if z["zone_code"] == 1)
    sec_1_1 = next(s for s in zone_1["sections"] if s["section_code"] == "01.01")
    art_1_1_1 = next(
        a for a in sec_1_1["artifacts"] if a["artifact_code"] == "01.01.01"
    )
    assert art_1_1_1["status"] == "PRESENT"
    assert art_1_1_1["document_id"] == doc_id
    assert art_1_1_1["version_index"] == 1


@pytest.mark.asyncio
async def test_document_version_history_lineage():
    """
    Test the version history lineage endpoint.
    Create a multi-version document lineage, transition QC status, and assert order and structure.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin,sponsor_dm", change_reason="initial ingest")

    # 1. Ingest version 1
    payload_v1 = {
        "study_id": "study_v",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Protocol V1",
        "mime_type": "application/pdf",
    }
    res_v1 = client.post("/api/v1/etmf/ingest", json=payload_v1, headers=headers)
    assert res_v1.status_code == 201
    v1_id = res_v1.json()["id"]

    # Transition v1 status through QC states to ARCHIVED
    headers_transition = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Approve and archive"
    )
    for s in ["TECHNICAL_QC", "CLINICAL_QC", "APPROVED", "ARCHIVED"]:
        res_trans1 = client.post(
            f"/api/v1/etmf/documents/{v1_id}/transition",
            json={
                "to_status": s,
                "reason_for_change": f"Transitioning document to {s} status",
            },
            headers=headers_transition,
        )
        assert res_trans1.status_code == 200

    # 2. Ingest version 2
    payload_v2 = {
        "study_id": "study_v",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v2.pdf",
        "content": "Protocol V2",
        "mime_type": "application/pdf",
    }
    res_v2 = client.post("/api/v1/etmf/ingest", json=payload_v2, headers=headers)
    assert res_v2.status_code == 201
    v2_id = res_v2.json()["id"]

    # 3. Request version history
    response = client.get(f"/api/v1/etmf/documents/{v2_id}/versions", headers=headers)
    assert response.status_code == 200
    data = response.json()

    assert data["study_id"] == "study_v"
    assert data["artifact_code"] == "01.01.01"
    assert len(data["versions"]) == 2

    # Deterministic ascending order of version_index
    v1_entry = data["versions"][0]
    v2_entry = data["versions"][1]

    assert v1_entry["id"] == v1_id
    assert v1_entry["version_index"] == 1
    assert v1_entry["filename"] == "protocol_v1.pdf"
    assert len(v1_entry["transitions"]) >= 1
    assert v1_entry["transitions"][0]["from_status"] == "DRAFT"
    assert v1_entry["transitions"][-1]["to_status"] == "ARCHIVED"

    assert v2_entry["id"] == v2_id
    assert v2_entry["version_index"] == 2
    assert v2_entry["filename"] == "protocol_v2.pdf"

    # Verify VERSION_HISTORY_VIEW audit log
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFAuditLog).where(TMFAuditLog.action == "VERSION_HISTORY_VIEW")
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        assert v2_id in logs[0].details or "study_v" in logs[0].details


@pytest.mark.asyncio
async def test_versions_404_not_found():
    """
    Test version history returns 404 for unknown document_id.
    """
    client = TestClient(app)
    headers = get_auth_headers(roles="admin,sponsor_dm")
    response = client.get(
        "/api/v1/etmf/documents/unknown_doc_id/versions", headers=headers
    )
    assert response.status_code == 404
    assert response.json()["detail"] == "eTMF Document not found"
