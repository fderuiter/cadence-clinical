import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app, map_artifact_to_tmf
from apps.etmf.models import Base, DocumentQCTransition, TMFAuditLog, TMFDocument
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


def test_tmf_taxonomy_mapping():
    """
    Test direct DIA TMF Zone and Section taxonomy mappings for standard clinical artifacts.
    """
    assert map_artifact_to_tmf("Approved Protocol") == (1, "1.1 Protocol")
    assert map_artifact_to_tmf("Define-XML") == (
        10,
        "10.1 Data Management Specifications",
    )
    assert map_artifact_to_tmf("Blank CRF") == (10, "10.2 Case Report Forms")
    assert map_artifact_to_tmf("Blank CRFs") == (10, "10.2 Case Report Forms")
    assert map_artifact_to_tmf("Data Lock Certificate") == (
        11,
        "11.1 Statistical Analysis",
    )
    assert map_artifact_to_tmf("Data Lock Certificates") == (
        11,
        "11.1 Statistical Analysis",
    )
    assert map_artifact_to_tmf("Ad-hoc document") == (2, "2.1 Study Files")


@pytest.mark.asyncio
async def test_automated_ingestion_and_version_indexing():
    """
    Verify that post requests automatically ingest searchable document archives,
    perform DIA TMF taxonomy assignment, increment version indices, and log to the audit ledger.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingest study data"
    )

    # Ingest Version 1
    payload = {
        "study_id": "study_001",
        "artifact_type": "Approved Protocol",
        "filename": "protocol_v1.pdf",
        "content": "This is the clinical trial protocol for study 001. Enforces double blind randomized controls.",
        "mime_type": "application/pdf",
        "metadata_json": {"sponsor": "Acme Corp"},
    }
    response = client.post("/events/publish", json=payload, headers=headers)
    assert response.status_code == 201
    data = response.json()
    assert data["status"] == "success"
    assert data["zone"] == 1
    assert data["section"] == "1.1 Protocol"
    assert data["version_index"] == 1

    # Ingest Version 2 (Same study_id and artifact_type)
    payload_v2 = {
        "study_id": "study_001",
        "artifact_type": "Approved Protocol",
        "filename": "protocol_v2.pdf",
        "content": "This is the clinical trial protocol version 2.",
        "mime_type": "application/pdf",
    }
    response_v2 = client.post("/api/v1/etmf/ingest", json=payload_v2, headers=headers)
    assert response_v2.status_code == 201
    data_v2 = response_v2.json()
    assert data_v2["version_index"] == 2

    # Verify database contents
    async with db_manager.get_session_maker()() as session:
        # Check documents
        stmt = (
            select(TMFDocument)
            .where(TMFDocument.study_id == "study_001")
            .order_by(TMFDocument.version_index)
        )
        result = await session.execute(stmt)
        docs = result.scalars().all()
        assert len(docs) == 2
        assert docs[0].filename == "protocol_v1.pdf"
        assert docs[0].version_index == 1
        assert docs[1].filename == "protocol_v2.pdf"
        assert docs[1].version_index == 2

        # Check immutable audit log
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.action == "INGEST")
        result_audit = await session.execute(stmt_audit)
        audit_logs = result_audit.scalars().all()
        assert len(audit_logs) == 2
        assert "Ingested artifact type 'Approved Protocol'" in audit_logs[0].details
        assert audit_logs[0].user_id == "test_user"


@pytest.mark.asyncio
async def test_inspector_portal_read_only_access_limits():
    """
    Ensure that users with regulatory inspector roles can view, download,
    and query the eTMF repository but are strictly forbidden from mutating/ingesting files.
    """
    client = TestClient(app)
    # Include change_reason since POST mutations under V2 require it for signature validation
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Unauthorized ingestion attempt"
    )

    # Try to ingest using inspector role -> should fail
    payload = {
        "study_id": "study_001",
        "artifact_type": "Approved Protocol",
        "filename": "protocol.pdf",
        "content": "Secret protocol",
        "mime_type": "application/pdf",
    }
    response = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=inspector_headers
    )
    assert response.status_code == 403
    assert "restricted to read-only" in response.json()["detail"]


@pytest.mark.asyncio
async def test_view_download_audit_logging():
    """
    Verify that document list searches, metadata views, and content downloads
    automatically write immutable, chronological logs to the TMFAuditLog ledger.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Setup study docs")

    # Ingest a document first
    payload = {
        "study_id": "study_abc",
        "artifact_type": "Define-XML",
        "filename": "define.xml",
        "content": "SDTM standard data specification structure.",
        "mime_type": "application/xml",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=admin_headers
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Perform List search
    inspector_headers = get_auth_headers(roles="regulatory_inspector")
    list_resp = client.get(
        "/api/v1/etmf/documents?study_id=study_abc", headers=inspector_headers
    )
    assert list_resp.status_code == 200
    assert len(list_resp.json()) == 1

    # Perform View metadata
    view_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}", headers=inspector_headers
    )
    assert view_resp.status_code == 200
    assert view_resp.json()["filename"] == "define.xml"

    # Perform Download content
    download_resp = client.get(
        f"/api/v1/etmf/documents/{doc_id}/download", headers=inspector_headers
    )
    assert download_resp.status_code == 200
    assert download_resp.text == "SDTM standard data specification structure."

    # Retrieve all audit logs
    audit_resp = client.get("/api/v1/etmf/audit-logs", headers=inspector_headers)
    assert audit_resp.status_code == 200
    logs = audit_resp.json()

    # The latest logs should be in descending order (newest first)
    # We expect: AUDIT_VIEW, DOWNLOAD, VIEW, LIST, INGEST
    actions = [log["action"] for log in logs]
    assert "AUDIT_VIEW" in actions
    assert "DOWNLOAD" in actions
    assert "VIEW" in actions
    assert "LIST" in actions
    assert "INGEST" in actions

    # Verify correct document association in logs
    download_log = next(log for log in logs if log["action"] == "DOWNLOAD")
    assert download_log["document_id"] == doc_id
    assert download_log["user_role"] == "regulatory_inspector"


@pytest.mark.asyncio
async def test_completeness_checking_transitions():
    """
    Test eTMF completeness validation checks against study milestones.
    Ensure completeness logic correctly parses mandatory documents for
    INITIATION, CONDUCT, and CLOSEOUT.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Add documents")

    # Initially, study_xyz is completely empty.
    # Check INITIATION milestone -> requires Approved Protocol -> should be False.
    headers = get_auth_headers(roles="regulatory_inspector")
    res_init = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=INITIATION",
        headers=headers,
    )
    assert res_init.status_code == 200
    data_init = res_init.json()
    assert data_init["is_complete"] is False
    assert "Approved Protocol" in data_init["missing_artifacts"]

    # Ingest Approved Protocol
    payload_prot = {
        "study_id": "study_xyz",
        "artifact_type": "Approved Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol content",
        "mime_type": "application/pdf",
    }
    client.post("/api/v1/etmf/ingest", json=payload_prot, headers=admin_headers)

    # Check INITIATION again -> should now be True!
    res_init_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=INITIATION",
        headers=headers,
    )
    assert res_init_2.json()["is_complete"] is True

    # Check CONDUCT milestone -> requires Approved Protocol, Define-XML, Blank CRF -> should be False.
    res_cond = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CONDUCT",
        headers=headers,
    )
    assert res_cond.json()["is_complete"] is False
    assert "Define-XML" in res_cond.json()["missing_artifacts"]
    assert "Blank CRF" in res_cond.json()["missing_artifacts"]

    # Ingest the remaining documents for CONDUCT
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Define-XML",
            "filename": "define.xml",
            "content": "Metadata Spec",
            "mime_type": "application/xml",
        },
        headers=admin_headers,
    )
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Blank CRF",
            "filename": "crf.xml",
            "content": "Blank CRF layout",
            "mime_type": "application/xml",
        },
        headers=admin_headers,
    )

    # Check CONDUCT again -> should now be True!
    res_cond_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CONDUCT",
        headers=headers,
    )
    assert res_cond_2.json()["is_complete"] is True

    # Check CLOSEOUT milestone -> requires Data Lock Certificate -> should be False.
    res_close = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CLOSEOUT",
        headers=headers,
    )
    assert res_close.json()["is_complete"] is False

    # Ingest Data Lock Certificate
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_xyz",
            "artifact_type": "Data Lock Certificate",
            "filename": "lock_cert.pdf",
            "content": "Locked database verified.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # Check CLOSEOUT -> should now be True!
    res_close_2 = client.get(
        "/api/v1/etmf/completeness?study_id=study_xyz&milestone=CLOSEOUT",
        headers=headers,
    )
    assert res_close_2.json()["is_complete"] is True


@pytest.mark.asyncio
async def test_qc_lifecycle_end_to_end():
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Initial ingestion")

    # 1. Ingest document and verify default status is DRAFT
    payload = {
        "study_id": "study_qc_1",
        "artifact_type": "Approved Protocol",
        "filename": "protocol.pdf",
        "content": "Protocol text",
        "mime_type": "application/pdf",
    }
    ingest_resp = client.post(
        "/api/v1/etmf/ingest", json=payload, headers=admin_headers
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Verify default status is DRAFT via View endpoint
    view_resp = client.get(f"/api/v1/etmf/documents/{doc_id}", headers=admin_headers)
    assert view_resp.status_code == 200
    assert view_resp.json()["status"] == "DRAFT"

    # 2. Test missing change-reason rejection
    no_reason_headers = get_auth_headers(roles="technical_qc_reviewer")
    no_reason_headers.pop("X-Change-Reason", None)
    resp_no_reason = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "TECHNICAL_QC"},
        headers=no_reason_headers,
    )
    assert resp_no_reason.status_code in (400, 403)

    # 3. Test role-based rejection: Wrong role
    wrong_role_headers = get_auth_headers(
        roles="clinical_qc_reviewer", change_reason="Reviewing technical details"
    )
    resp_wrong_role = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "TECHNICAL_QC"},
        headers=wrong_role_headers,
    )
    assert resp_wrong_role.status_code == 403

    # 4. Test blocked invalid transitions
    invalid_trans_headers = get_auth_headers(
        roles="clinical_qc_reviewer", change_reason="Bypassing technical review"
    )
    resp_invalid_trans = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "CLINICAL_QC"},
        headers=invalid_trans_headers,
    )
    assert resp_invalid_trans.status_code == 400

    # 5. Successful Transition: DRAFT -> TECHNICAL_QC
    tech_reviewer_headers = get_auth_headers(
        roles="technical_qc_reviewer", change_reason="Technical review passed"
    )
    resp_tech = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "target_status": "TECHNICAL_QC",
            "context": {"checked_items": ["signature", "metadata"]},
        },
        headers=tech_reviewer_headers,
    )
    assert resp_tech.status_code == 200
    assert resp_tech.json()["new_status"] == "TECHNICAL_QC"

    # Verify transition writes to both DB tables
    async with db_manager.get_session_maker()() as session:
        # Check DocumentQCTransition
        stmt = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == doc_id)
            .order_by(DocumentQCTransition.timestamp.desc())
        )
        transitions = (await session.execute(stmt)).scalars().all()
        assert len(transitions) == 1
        assert transitions[0].from_status == "DRAFT"
        assert transitions[0].to_status == "TECHNICAL_QC"
        assert transitions[0].reason_for_change == "Technical review passed"

        # Check TMFAuditLog
        stmt_audit = (
            select(TMFAuditLog)
            .where(TMFAuditLog.document_id == doc_id)
            .where(TMFAuditLog.action == "QC_TRANSITION")
        )
        audit_logs = (await session.execute(stmt_audit)).scalars().all()
        assert len(audit_logs) == 1
        assert "Transitioned document" in audit_logs[0].details

    # 6. Rejection path: TECHNICAL_QC -> REJECTED (by technical_qc_reviewer) -> DRAFT (by author)
    tech_reject_headers = get_auth_headers(
        roles="technical_qc_reviewer", change_reason="Fails technical review"
    )
    resp_reject = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "REJECTED"},
        headers=tech_reject_headers,
    )
    assert resp_reject.status_code == 200
    assert resp_reject.json()["new_status"] == "REJECTED"

    # Transition back to DRAFT (requires author or data_manager role)
    author_headers = get_auth_headers(
        roles="author", change_reason="Resubmitting after fix"
    )
    resp_to_draft = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "DRAFT"},
        headers=author_headers,
    )
    assert resp_to_draft.status_code == 200
    assert resp_to_draft.json()["new_status"] == "DRAFT"

    # 7. Complete forward progression: DRAFT -> TECHNICAL_QC -> CLINICAL_QC -> APPROVED -> ARCHIVED
    # DRAFT -> TECHNICAL_QC
    client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "TECHNICAL_QC"},
        headers=tech_reviewer_headers,
    )
    # TECHNICAL_QC -> CLINICAL_QC
    clinical_reviewer_headers = get_auth_headers(
        roles="clinical_qc_reviewer", change_reason="Clinical check passed"
    )
    resp_clin = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "CLINICAL_QC"},
        headers=clinical_reviewer_headers,
    )
    assert resp_clin.status_code == 200

    # CLINICAL_QC -> APPROVED
    approver_headers = get_auth_headers(
        roles="approver", change_reason="Final approval granted"
    )
    resp_app = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "APPROVED"},
        headers=approver_headers,
    )
    assert resp_app.status_code == 200

    # APPROVED -> ARCHIVED
    resp_arch = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "ARCHIVED"},
        headers=approver_headers,
    )
    assert resp_arch.status_code == 200

    # ARCHIVED cannot transition anywhere
    resp_invalid_arch = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"target_status": "DRAFT"},
        headers=author_headers,
    )
    assert resp_invalid_arch.status_code == 400

    # 8. GET qc-history and verify response and VIEW_QC_HISTORY audit log
    history_headers = get_auth_headers(roles="regulatory_inspector")
    resp_history = client.get(
        f"/api/v1/etmf/documents/{doc_id}/qc-history",
        headers=history_headers,
    )
    assert resp_history.status_code == 200
    history_data = resp_history.json()
    assert len(history_data) == 7
    # Ordered chronologically
    assert history_data[0]["from_status"] == "DRAFT"
    assert history_data[0]["to_status"] == "TECHNICAL_QC"
    assert history_data[-1]["from_status"] == "APPROVED"
    assert history_data[-1]["to_status"] == "ARCHIVED"

    # Verify VIEW_QC_HISTORY audit log entry was written
    async with db_manager.get_session_maker()() as session:
        stmt_audit = (
            select(TMFAuditLog)
            .where(TMFAuditLog.document_id == doc_id)
            .where(TMFAuditLog.action == "VIEW_QC_HISTORY")
        )
        audit_logs = (await session.execute(stmt_audit)).scalars().all()
        assert len(audit_logs) == 1
        assert "Viewed QC transition history" in audit_logs[0].details
