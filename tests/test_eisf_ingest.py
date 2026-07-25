import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.eisf.database import db_manager
from apps.eisf.main import app as eisf_app
from apps.eisf.models import Base, ISFAuditLog, ISFDocument
from tests.test_eisf_api import get_eisf_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_eisf_db_for_ingest():
    """
    Setup in-memory eISF database for testing FastAPI endpoints.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_eisf_ingest_document_success() -> None:
    """
    Test successful ingestion of a document with manual API and event publication alias.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Filing required site document",
    )

    # 1. Manual Ingestion
    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr. Smith CV content",
        "mime_type": "application/pdf",
        "reason_for_change": "Initial filing of CV",
    }
    resp = client.post("/api/v1/eisf/ingest", json=payload, headers=headers)
    assert resp.status_code == 201
    data = resp.json()
    assert data["version_index"] == 1
    assert data["content_checksum"] is not None
    assert data["created_by"] == "pi-boston"

    # Verify checksum is deterministic (sha256 of "Dr. Smith CV content")
    import hashlib

    expected_checksum = hashlib.sha256(b"Dr. Smith CV content").hexdigest()
    assert data["content_checksum"] == expected_checksum

    # 2. Re-filing produces incremented version
    payload_v2 = payload.copy()
    payload_v2["content"] = "Dr. Smith CV content updated"
    payload_v2["reason_for_change"] = "Updated CV with newer experience"
    resp_v2 = client.post("/api/v1/eisf/ingest", json=payload_v2, headers=headers)
    assert resp_v2.status_code == 201
    data_v2 = resp_v2.json()
    assert data_v2["version_index"] == 2
    assert (
        data_v2["content_checksum"]
        == hashlib.sha256(b"Dr. Smith CV content updated").hexdigest()
    )

    # Verify that the database contains both versions, and they are not mutated
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(ISFDocument)
            .where(
                ISFDocument.study_id == "study-100",
                ISFDocument.site_id == "site-boston-01",
                ISFDocument.binder_classification == "Investigator CV",
            )
            .order_by(ISFDocument.version_index.asc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 2
        assert docs[0].content == "Dr. Smith CV content"
        assert docs[0].version_index == 1
        assert docs[1].content == "Dr. Smith CV content updated"
        assert docs[1].version_index == 2

        # Check INGEST audit logs
        stmt_audit = select(ISFAuditLog).where(ISFAuditLog.action == "INGEST")
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) == 2
        assert audit_logs[0].actor_id == "pi-boston"
        assert audit_logs[0].reason_for_change == "Initial filing of CV"
        assert audit_logs[1].reason_for_change == "Updated CV with newer experience"


@pytest.mark.asyncio
async def test_eisf_ingest_document_event_alias() -> None:
    """
    Test document ingestion using the event publication alias /events/publish with artifact_type alias.
    """
    client = TestClient(eisf_app)
    headers = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Event ingestion justification",
    )

    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "artifact_type": "Delegation Log",
        "filename": "doa.pdf",
        "content": "DOA log content",
        "mime_type": "application/pdf",
        "reason_for_change": "Event triggered filing",
    }
    resp = client.post("/events/publish", json=payload, headers=headers)
    assert resp.status_code == 201
    data = resp.json()
    assert data["binder_classification"] == "Delegation Log"
    assert data["version_index"] == 1

    # Verify audit logs
    async with db_manager.get_session_maker()() as session:
        stmt_audit = select(ISFAuditLog).where(ISFAuditLog.action == "INGEST")
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) == 1
        assert audit_logs[0].reason_for_change == "Event triggered filing"


@pytest.mark.asyncio
async def test_eisf_ingest_missing_change_reason_fails() -> None:
    """
    Test that ingestion fails with 400 Bad Request if the change reason is missing or too short.
    """
    client = TestClient(eisf_app)
    get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Valid header but body misses it",
    )

    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "binder_classification": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr. Smith CV content",
        "mime_type": "application/pdf",
        # reason_for_change is missing in payload, and not set in gateway mock
    }
    # For POST mutation under V2 Gateway middleware, signature validation will fail if X-Change-Reason header is missing
    # Let's use version 1 signature format or sign properly, or just pass short change_reason to verify 400
    headers_short = get_eisf_auth_headers(
        user_id="pi-boston",
        roles="site investigator",
        site_id="site-boston-01",
        change_reason="Short",
    )
    resp = client.post("/api/v1/eisf/ingest", json=payload, headers=headers_short)
    assert resp.status_code == 400
    assert "at least 10 characters" in resp.json()["detail"]
