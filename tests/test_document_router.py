"""Unit and integration tests for document operations and study archival routers.

Requirements Traceability: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import hashlib
import io
import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager as etmf_db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base as EtmfBase
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import AuditLog
from apps.execution.database.models import Base as ExecBase
from apps.execution.main import app as exec_app
from apps.execution.routers.documents import _DOCUMENTS_DB
from apps.gateway.main import generate_signature


@pytest.fixture(autouse=True)
def clear_documents_db():
    """Clear the in-memory documents database for test isolation."""
    _DOCUMENTS_DB.clear()


@pytest_asyncio.fixture(autouse=True)
async def setup_dbs():
    """Setup in-memory SQLite databases for execution and eTMF apps.

    Requirements: PRD-SYS-001
    """
    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.create_all)

    etmf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.create_all)

    yield

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()

    async with etmf_db_manager.engine.begin() as conn:
        await conn.run_sync(EtmfBase.metadata.drop_all)
    await etmf_db_manager.close()


def get_auth_headers(
    roles: str = "SponsorAdmin", change_reason: str = "Test operation"
) -> dict:
    """Helper to generate valid gateway V2 signed headers for testing.

    Requirements: PRD-SYS-001
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
        "X-Change-Reason": change_reason,
    }
    return headers


def test_document_upload_success():
    """Verify 200 OK document upload returns correct response metadata and SHA-256 hash.

    Requirements: PRD-SYS-001
    """
    client = TestClient(exec_app)
    headers = get_auth_headers(
        roles="SponsorAdmin", change_reason="Uploading protocol v1"
    )

    file_content = b"This is clinical trial protocol content under PRD-SYS-001"
    expected_hash = hashlib.sha256(file_content).hexdigest()

    response = client.post(
        "/api/v1/documents/upload",
        files={"file": ("protocol.pdf", io.BytesIO(file_content), "application/pdf")},
        data={
            "dia_tmf_code": "01.01.01",
            "reason_for_change": "Initial document draft",
        },
        headers=headers,
    )

    assert response.status_code == 201
    data = response.json()
    assert data["document_id"].startswith("doc_")
    assert data["filename"] == "protocol.pdf"
    assert data["version_index"] == "1.0"
    assert data["sha256_hash"] == expected_hash


def test_document_upload_missing_permission():
    """Verify document upload fails with 403 when user lacks documents:write permission.

    Requirements: PRD-SYS-001
    """
    client = TestClient(exec_app)
    # SponsorDesigner role only has study:read, protocol:author, etc. It lacks documents:write
    headers = get_auth_headers(
        roles="SponsorDesigner", change_reason="Unauthorized upload"
    )

    file_content = b"Some content"
    response = client.post(
        "/api/v1/documents/upload",
        files={"file": ("protocol.pdf", io.BytesIO(file_content), "application/pdf")},
        data={
            "dia_tmf_code": "01.01.01",
            "reason_for_change": "Initial document draft",
        },
        headers=headers,
    )

    assert response.status_code == 403
    assert "Missing required permission" in response.json()["detail"]


@pytest.mark.asyncio
async def test_document_download_logs_audit_and_watermarks():
    """Verify downloading a document applies watermarking and logs GxP DOCUMENT_VIEW audit event.

    Requirements: PRD-SYS-001
    """
    client = TestClient(exec_app)
    admin_headers = get_auth_headers(
        roles="SponsorAdmin", change_reason="Prepare download test"
    )

    # 1. Upload a document
    file_content = b"Regulated trial content for PRD-SYS-001"
    upload_resp = client.post(
        "/api/v1/documents/upload",
        files={"file": ("trial_doc.txt", io.BytesIO(file_content), "text/plain")},
        data={
            "dia_tmf_code": "01.01.01",
            "reason_for_change": "Setup document to download",
        },
        headers=admin_headers,
    )
    assert upload_resp.status_code == 201
    doc_id = upload_resp.json()["document_id"]

    # 2. Download the document with auditor headers (triggers watermarking)
    auditor_headers = get_auth_headers(
        roles="Auditor", change_reason="Auditing document"
    )
    download_resp = client.get(
        f"/api/v1/documents/{doc_id}",
        headers=auditor_headers,
    )

    assert download_resp.status_code == 200
    downloaded_text = download_resp.text
    assert "CONFIDENTIAL — Auditor Copy" in downloaded_text
    assert "test_user" in downloaded_text

    # 3. Query the relational AuditLog table in execution DB to verify GxP audit event (DOCUMENT_VIEW) was logged
    async with exec_db_manager.get_session_maker()() as session:
        stmt = select(AuditLog).where(
            AuditLog.action == "DOCUMENT_VIEW",
            AuditLog.record_id == doc_id,
        )
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
        assert logs[0].table_name == "clinical_documents"
        assert logs[0].user_id == "test_user"


def test_document_versions_lineage():
    """Verify document version lineage retrieval.

    Requirements: PRD-SYS-001
    """
    client = TestClient(exec_app)
    admin_headers = get_auth_headers(
        roles="SponsorAdmin", change_reason="Lineage testing"
    )

    # Ingest version 1
    resp1 = client.post(
        "/api/v1/documents/upload",
        files={
            "file": ("protocol_v1.pdf", io.BytesIO(b"content 1"), "application/pdf")
        },
        data={"dia_tmf_code": "01.01.01", "reason_for_change": "v1 draft"},
        headers=admin_headers,
    )
    assert resp1.status_code == 201
    doc_id_1 = resp1.json()["document_id"]

    # Get versions of the uploaded document
    resp_versions = client.get(
        f"/api/v1/documents/{doc_id_1}/versions",
        headers=admin_headers,
    )

    assert resp_versions.status_code == 200
    versions_list = resp_versions.json()
    assert len(versions_list) == 1
    assert versions_list[0]["document_id"] == doc_id_1
    assert versions_list[0]["filename"] == "protocol_v1.pdf"
    assert versions_list[0]["version_index"] == "1.0"


def test_study_archival_job_flow():
    """Verify study archival background task triggering and status tracking.

    Requirements: PRD-SYS-001
    """
    client = TestClient(etmf_app)
    headers = get_auth_headers(
        roles="SponsorAdmin", change_reason="Trigger bulk study archive"
    )

    # 1. Initiate bulk study archival
    response = client.post(
        "/api/v1/archive/studies/study_123/export",
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    job_id = data["job_id"]
    assert data["study_id"] == "study_123"
    assert data["status"] == "PENDING"
    assert data["download_url"] is None

    # 2. Check the job status
    status_response = client.get(
        f"/api/v1/archive/jobs/{job_id}",
        headers=headers,
    )

    assert status_response.status_code == 200
    status_data = status_response.json()
    assert status_data["job_id"] == job_id
    assert status_data["status"] in ("PENDING", "PROCESSING", "COMPLETED")
