import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import (
    Base,
    DocumentQCTransition,
    TMFAuditLog,
    TMFDocument,
)
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
async def test_bulk_archival_successful_progression():
    """Verify study-level bulk archive of eligible APPROVED documents transitions them to ARCHIVED.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Setup eligible study documents"
    )

    # Ingest two documents
    p1 = {
        "study_id": "study_bulk_ok",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    resp1 = client.post("/api/v1/etmf/ingest", json=p1, headers=admin_headers)
    assert resp1.status_code == 201
    doc1_id = resp1.json()["document_id"]

    p2 = {
        "study_id": "study_bulk_ok",
        "artifact_type": "Define-XML Specifications",
        "filename": "define.xml",
        "content": "Define specifications content",
        "mime_type": "application/xml",
    }
    resp2 = client.post("/api/v1/etmf/ingest", json=p2, headers=admin_headers)
    assert resp2.status_code == 201
    doc2_id = resp2.json()["document_id"]

    # Transition both documents to APPROVED status (via DRAFT -> TECHNICAL_QC -> CLINICAL_QC -> APPROVED)
    # Target status order: TECHNICAL_QC, CLINICAL_QC, APPROVED
    for doc_id in (doc1_id, doc2_id):
        client.post(
            f"/api/v1/etmf/documents/{doc_id}/transition",
            json={
                "to_status": "TECHNICAL_QC",
                "reason_for_change": "Technical check passed",
            },
            headers=get_auth_headers(
                roles="sponsor_dm", change_reason="Technical check"
            ),
        )
        client.post(
            f"/api/v1/etmf/documents/{doc_id}/transition",
            json={
                "to_status": "CLINICAL_QC",
                "reason_for_change": "Clinical check passed",
            },
            headers=get_auth_headers(
                roles="sponsor_clinical", change_reason="Clinical check"
            ),
        )
        client.post(
            f"/api/v1/etmf/documents/{doc_id}/transition",
            json={"to_status": "APPROVED", "reason_for_change": "Approved finalized"},
            headers=get_auth_headers(roles="admin", change_reason="Final approval"),
        )

    # Perform bulk archive for the study
    archive_headers = get_auth_headers(
        roles="admin", change_reason="Bulk study archive execution"
    )
    payload = {
        "reason_for_change": "Study completed, archiving all documents.",
        "all_or_nothing": True,
    }
    archive_resp = client.post(
        "/api/v1/etmf/studies/study_bulk_ok/archive",
        json=payload,
        headers=archive_headers,
    )
    assert archive_resp.status_code == 200
    res_data = archive_resp.json()
    assert res_data["status"] == "success"
    assert res_data["total_processed"] == 2
    assert res_data["successful_count"] == 2
    assert res_data["skipped_count"] == 0
    assert res_data["failed_count"] == 0

    # Verify both documents are ARCHIVED in the database and transition log exists
    async with db_manager.get_session_maker()() as session:
        for doc_id in (doc1_id, doc2_id):
            doc_stmt = select(TMFDocument).where(TMFDocument.id == doc_id)
            doc_res = await session.execute(doc_stmt)
            doc = doc_res.scalars().one()
            assert doc.status == "ARCHIVED"

            # Check DocumentQCTransition sequence
            qc_stmt = (
                select(DocumentQCTransition)
                .where(DocumentQCTransition.document_id == doc_id)
                .order_by(DocumentQCTransition.transition_sequence.desc())
            )
            qc_res = await session.execute(qc_stmt)
            last_transition = qc_res.scalars().first()
            assert last_transition.to_status == "ARCHIVED"
            assert (
                last_transition.reason_for_change
                == "Study completed, archiving all documents."
            )

        # Verify high-level study-level archive audit log
        audit_stmt = select(TMFAuditLog).where(TMFAuditLog.action == "STUDY_ARCHIVE")
        audit_res = await session.execute(audit_stmt)
        audit_logs = audit_res.scalars().all()
        assert len(audit_logs) == 1
        assert (
            "Bulk study archive completed for study 'study_bulk_ok'"
            in audit_logs[0].details
        )


@pytest.mark.asyncio
async def test_bulk_archival_all_or_nothing_rollback():
    """Verify that all-or-nothing rollback fails and rolls back entire batch on mixed eligibility documents.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Setup mixed study documents"
    )

    # Ingest Doc A (will remain in DRAFT, ineligible for transition to ARCHIVED)
    p1 = {
        "study_id": "study_bulk_fail",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    resp1 = client.post("/api/v1/etmf/ingest", json=p1, headers=admin_headers)
    assert resp1.status_code == 201
    doc1_id = resp1.json()["document_id"]

    # Ingest Doc B (will transition to APPROVED, eligible for transition to ARCHIVED)
    p2 = {
        "study_id": "study_bulk_fail",
        "artifact_type": "Define-XML Specifications",
        "filename": "define.xml",
        "content": "Define specifications content",
        "mime_type": "application/xml",
    }
    resp2 = client.post("/api/v1/etmf/ingest", json=p2, headers=admin_headers)
    assert resp2.status_code == 201
    doc2_id = resp2.json()["document_id"]

    # Transition Doc B to APPROVED
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Technical check passed",
        },
        headers=get_auth_headers(roles="sponsor_dm", change_reason="Technical check"),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={"to_status": "CLINICAL_QC", "reason_for_change": "Clinical check passed"},
        headers=get_auth_headers(
            roles="sponsor_clinical", change_reason="Clinical check"
        ),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={"to_status": "APPROVED", "reason_for_change": "Approved finalized"},
        headers=get_auth_headers(roles="admin", change_reason="Final approval"),
    )

    # Perform bulk archive with all_or_nothing=True
    archive_headers = get_auth_headers(
        roles="admin", change_reason="Bulk study archive execution"
    )
    payload = {
        "reason_for_change": "Study completed, archiving all documents.",
        "all_or_nothing": True,
    }
    archive_resp = client.post(
        "/api/v1/etmf/studies/study_bulk_fail/archive",
        json=payload,
        headers=archive_headers,
    )
    # Should reject with HTTP 400 due to all-or-nothing validation failure
    assert archive_resp.status_code == 400
    assert "All-or-nothing validation failure" in archive_resp.json()["detail"]

    # Verify both documents are NOT changed (Doc B remains APPROVED, Doc A remains DRAFT)
    async with db_manager.get_session_maker()() as session:
        # Check Doc A
        doc1_stmt = select(TMFDocument).where(TMFDocument.id == doc1_id)
        doc1_res = await session.execute(doc1_stmt)
        doc1 = doc1_res.scalars().one()
        assert doc1.status == "DRAFT"

        # Check Doc B
        doc2_stmt = select(TMFDocument).where(TMFDocument.id == doc2_id)
        doc2_res = await session.execute(doc2_stmt)
        doc2 = doc2_res.scalars().one()
        assert doc2.status == "APPROVED"


@pytest.mark.asyncio
async def test_bulk_archival_partial_success():
    """Verify that all_or_nothing=False processes eligible documents and reports failed ones.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Setup mixed study documents"
    )

    # Ingest Doc A (remain in DRAFT, ineligible)
    p1 = {
        "study_id": "study_bulk_partial",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    resp1 = client.post("/api/v1/etmf/ingest", json=p1, headers=admin_headers)
    assert resp1.status_code == 201
    doc1_id = resp1.json()["document_id"]

    # Ingest Doc B (transition to APPROVED, eligible)
    p2 = {
        "study_id": "study_bulk_partial",
        "artifact_type": "Define-XML Specifications",
        "filename": "define.xml",
        "content": "Define specifications content",
        "mime_type": "application/xml",
    }
    resp2 = client.post("/api/v1/etmf/ingest", json=p2, headers=admin_headers)
    assert resp2.status_code == 201
    doc2_id = resp2.json()["document_id"]

    # Transition Doc B to APPROVED
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Technical check passed",
        },
        headers=get_auth_headers(roles="sponsor_dm", change_reason="Technical check"),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={"to_status": "CLINICAL_QC", "reason_for_change": "Clinical check passed"},
        headers=get_auth_headers(
            roles="sponsor_clinical", change_reason="Clinical check"
        ),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc2_id}/transition",
        json={"to_status": "APPROVED", "reason_for_change": "Approved finalized"},
        headers=get_auth_headers(roles="admin", change_reason="Final approval"),
    )

    # Perform bulk archive with all_or_nothing=False
    archive_headers = get_auth_headers(
        roles="admin", change_reason="Bulk study archive execution"
    )
    payload = {
        "reason_for_change": "Study completed, partial archiving.",
        "all_or_nothing": False,
    }
    archive_resp = client.post(
        "/api/v1/etmf/studies/study_bulk_partial/archive",
        json=payload,
        headers=archive_headers,
    )
    assert archive_resp.status_code == 200
    res_data = archive_resp.json()
    assert res_data["status"] == "partial_success"
    assert res_data["total_processed"] == 2
    assert res_data["successful_count"] == 1
    assert res_data["failed_count"] == 1
    assert res_data["skipped_count"] == 0

    # Check Doc A result inside results list
    r1 = next(r for r in res_data["results"] if r["document_id"] == doc1_id)
    assert r1["status"] == "failed"
    assert "Invalid transition" in r1["error_message"]

    # Check Doc B result inside results list
    r2 = next(r for r in res_data["results"] if r["document_id"] == doc2_id)
    assert r2["status"] == "success"

    # Verify statuses in database
    async with db_manager.get_session_maker()() as session:
        # Check Doc A (should remain DRAFT)
        doc1_stmt = select(TMFDocument).where(TMFDocument.id == doc1_id)
        doc1_res = await session.execute(doc1_stmt)
        doc1 = doc1_res.scalars().one()
        assert doc1.status == "DRAFT"

        # Check Doc B (should transition to ARCHIVED)
        doc2_stmt = select(TMFDocument).where(TMFDocument.id == doc2_id)
        doc2_res = await session.execute(doc2_stmt)
        doc2 = doc2_res.scalars().one()
        assert doc2.status == "ARCHIVED"


@pytest.mark.asyncio
async def test_bulk_archival_repeating_safe_and_observable():
    """Verify that repeating an already-completed or empty archive request is safe and observable (no-op).

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Setup eligible study documents"
    )

    # Repeat on empty study
    archive_headers = get_auth_headers(
        roles="admin", change_reason="Bulk study archive execution"
    )
    payload = {
        "reason_for_change": "Empty study archive test.",
        "all_or_nothing": True,
    }
    archive_resp1 = client.post(
        "/api/v1/etmf/studies/study_empty/archive",
        json=payload,
        headers=archive_headers,
    )
    assert archive_resp1.status_code == 200
    assert archive_resp1.json()["total_processed"] == 0
    assert archive_resp1.json()["skipped_count"] == 0

    # Ingest and Transition to ARCHIVED
    p1 = {
        "study_id": "study_repeat",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol Content",
        "mime_type": "application/pdf",
    }
    resp1 = client.post("/api/v1/etmf/ingest", json=p1, headers=admin_headers)
    doc_id = resp1.json()["document_id"]

    # Transition to APPROVED
    client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Technical check passed",
        },
        headers=get_auth_headers(roles="sponsor_dm", change_reason="Technical check"),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"to_status": "CLINICAL_QC", "reason_for_change": "Clinical check passed"},
        headers=get_auth_headers(
            roles="sponsor_clinical", change_reason="Clinical check"
        ),
    )
    client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"to_status": "APPROVED", "reason_for_change": "Approved finalized"},
        headers=get_auth_headers(roles="admin", change_reason="Final approval"),
    )

    # First archive -> transitions to ARCHIVED
    archive_resp2 = client.post(
        "/api/v1/etmf/studies/study_repeat/archive",
        json=payload,
        headers=archive_headers,
    )
    assert archive_resp2.status_code == 200
    assert archive_resp2.json()["successful_count"] == 1
    assert archive_resp2.json()["skipped_count"] == 0

    # Second archive -> safe no-op, marked as skipped
    archive_resp3 = client.post(
        "/api/v1/etmf/studies/study_repeat/archive",
        json=payload,
        headers=archive_headers,
    )
    assert archive_resp3.status_code == 200
    assert archive_resp3.json()["successful_count"] == 0
    assert archive_resp3.json()["skipped_count"] == 1


@pytest.mark.asyncio
async def test_bulk_archival_authorization_and_rejections():
    """Verify that unauthorized callers and missing/too-short reasons are rejected.

    Requirements: PRD-SYS-001
    """
    client = TestClient(app)

    # Unauthorized role (CRA does not have etmf_document:transition_archived permission)
    cra_headers = get_auth_headers(roles="cra", change_reason="CRA archive attempt")
    payload = {
        "reason_for_change": "Trying to archive as CRA role",
        "all_or_nothing": True,
    }
    resp_unauth = client.post(
        "/api/v1/etmf/studies/study_auth_test/archive",
        json=payload,
        headers=cra_headers,
    )
    assert resp_unauth.status_code == 403
    assert "Forbidden" in resp_unauth.json()["detail"]

    # Missing reason
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Admin archive attempt"
    )
    payload_missing = {
        "all_or_nothing": True,
    }
    resp_missing = client.post(
        "/api/v1/etmf/studies/study_auth_test/archive",
        json=payload_missing,
        headers=admin_headers,
    )
    assert resp_missing.status_code == 422

    # Reason too short (< 10 chars)
    payload_short = {
        "reason_for_change": "Short",
        "all_or_nothing": True,
    }
    resp_short = client.post(
        "/api/v1/etmf/studies/study_auth_test/archive",
        json=payload_short,
        headers=admin_headers,
    )
    assert resp_short.status_code == 422
