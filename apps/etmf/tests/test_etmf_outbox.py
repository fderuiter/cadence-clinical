import asyncio
import os
import httpx
import pytest
import pytest_asyncio
from unittest.mock import patch, AsyncMock
from datetime import datetime, UTC
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFDocument
from apps.etmf.infrastructure.models import IntegrationOutbox
from apps.etmf.workers.outbox_worker import poll_and_dispatch
from apps.etmf.tests.test_etmf import get_auth_headers


@pytest.fixture(autouse=True)
def allow_legacy_signatures_for_this_suite(monkeypatch):
    monkeypatch.setenv("ALLOW_LEGACY_MOCK_SIGNATURES", "true")


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory eTMF database for outbox testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_headers(roles: str = "admin", change_reason: str = "Testing outbox") -> dict:
    return get_auth_headers(roles=roles, change_reason=change_reason)


@pytest.mark.asyncio
async def test_document_signing_writes_outbox() -> None:
    """Verify that signing a document writes a DOCUMENT_ARCHIVAL outbox record atomically."""
    client = httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test")
    headers = get_headers()

    # 1. Ingest an unsigned document first
    ingest_resp = await client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_outbox_01",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_outbox.pdf",
            "content": "Protocol content to archive",
            "mime_type": "application/pdf",
        },
        headers=headers,
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Check outbox is currently empty
    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox)
        res = await session.execute(stmt)
        assert len(res.scalars().all()) == 0

    # 2. Sign the document (this should trigger outbox entry creation atomically)
    from jose import jwt
    import time
    import uuid
    
    GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    user_id = "test_user"
    sig_payload = {
        "sub": user_id,
        "action": f"/api/v1/etmf/documents/{doc_id}/sign-off",
        "semantic_action": "execution.form.signoff",
        "exp": time.time() + 300.0,
        "jti": str(uuid.uuid4()),
    }
    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    
    sign_headers = get_headers(roles="admin", change_reason="Form 1572 Investigator Sign-off")
    sign_headers["X-Sig-Token"] = sig_token
    sign_headers["X-User-Id"] = user_id
    
    sign_resp = await client.post(
        f"/api/v1/etmf/documents/{doc_id}/sign-off",
        json={"signing_reason": "APPROVAL"},
        headers=sign_headers,
    )
    assert sign_resp.status_code == 200

    # 3. Verify that the IntegrationOutbox record is created
    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox)
        res = await session.execute(stmt)
        outbox_records = res.scalars().all()
        assert len(outbox_records) == 1
        record = outbox_records[0]
        assert record.event_type == "DOCUMENT_ARCHIVAL"
        assert record.status == "PENDING"
        assert record.attempts == 0
        assert record.retry_eligible is True
        assert record.payload["document_id"] == doc_id
        assert record.payload["filename"] == "protocol_outbox.pdf"
        assert record.payload["content"] == "Protocol content to archive"
        assert record.payload["study_id"] == "study_outbox_01"


@pytest.mark.asyncio
async def test_etmf_outbox_worker_polling_and_dispatch_success() -> None:
    """Verify eTMF outbox worker polls pending DOCUMENT_ARCHIVAL records and dispatches them to external archival system."""
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="DOCUMENT_ARCHIVAL",
            payload={
                "document_id": "doc-111",
                "filename": "file-111.pdf",
                "content": "Protocol text",
                "study_id": "study-111",
                "site_id": "site-111",
            },
            status="PENDING",
            attempts=0,
            correlation_id="corr-arch-111",
            created_by="investigator1",
            reason_for_change="Investigator signoff",
        )
        session.add(record)
        await session.commit()
        record_id = record.id

    # Mock outbound post to external archival system
    with patch("httpx.AsyncClient.post") as mock_post:
        mock_resp = AsyncMock()
        mock_resp.status_code = 200
        mock_post.return_value = mock_resp

        await poll_and_dispatch()

        mock_post.assert_called_once()
        called_url = mock_post.call_args[0][0]
        assert "/archive" in called_url
        called_payload = mock_post.call_args[1]["json"]
        assert called_payload["document_id"] == "doc-111"
        assert called_payload["content"] == "Protocol text"

    # Check updated outbox entry in DB
    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox).where(IntegrationOutbox.id == record_id)
        res = await session.execute(stmt)
        updated = res.scalars().first()
        assert updated is not None
        assert updated.status == "SUCCESS"
        assert updated.completed_at is not None
        assert updated.last_error is None


@pytest.mark.asyncio
async def test_etmf_outbox_worker_retry_and_backoff() -> None:
    """Verify retry and exponential backoff logic on eTMF outbox dispatcher worker on failure."""
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="DOCUMENT_ARCHIVAL",
            payload={
                "document_id": "doc-222",
                "filename": "file-222.pdf",
                "content": "Protocol text",
                "study_id": "study-222",
                "site_id": "site-222",
            },
            status="PENDING",
            attempts=0,
            correlation_id="corr-arch-222",
            created_by="investigator2",
            reason_for_change="Investigator signoff",
        )
        session.add(record)
        await session.commit()
        record_id = record.id

    # Mock outbound archival POST to raise an exception
    with patch("httpx.AsyncClient.post", side_effect=httpx.ConnectError("Archival system offline")):
        for attempt in range(1, 6):
            # Reset next_retry_at to None to allow the worker to pick it up on subsequent attempts
            async with db_manager.get_session_maker()() as session:
                stmt = select(IntegrationOutbox).where(IntegrationOutbox.id == record_id)
                res = await session.execute(stmt)
                rec = res.scalars().first()
                rec.next_retry_at = None
                await session.commit()

            await poll_and_dispatch()

            # Verify incrementing attempts and backoff
            async with db_manager.get_session_maker()() as session:
                stmt = select(IntegrationOutbox).where(IntegrationOutbox.id == record_id)
                res = await session.execute(stmt)
                updated = res.scalars().first()
                assert updated is not None
                assert updated.status == "FAILED"
                assert updated.attempts == attempt
                assert "Archival system offline" in updated.last_error
                assert updated.next_retry_at is not None
                if attempt < 5:
                    assert updated.retry_eligible is True
                else:
                    assert updated.retry_eligible is False


@pytest.mark.asyncio
async def test_etmf_admin_visibility_endpoint() -> None:
    """Verify that admins can view/query status, delivery history, and payload of eTMF outbox records."""
    async with db_manager.get_session_maker()() as session:
        rec1 = IntegrationOutbox(
            event_type="DOCUMENT_ARCHIVAL",
            payload={"doc_id": "doc-abc"},
            status="SUCCESS",
            attempts=1,
            completed_at=datetime.now(UTC),
            correlation_id="corr-arch-abc",
            created_by="admin1",
            reason_for_change="Finalized",
        )
        rec2 = IntegrationOutbox(
            event_type="DOCUMENT_ARCHIVAL",
            payload={"doc_id": "doc-def"},
            status="FAILED",
            attempts=4,
            correlation_id="corr-arch-def",
            created_by="admin2",
            reason_for_change="Failed archive",
        )
        rec3 = IntegrationOutbox(
            event_type="OTHER_EVENT",
            payload={"data": "meta"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-arch-other",
            created_by="system",
            reason_for_change="Activity check",
        )
        session.add_all([rec1, rec2, rec3])
        await session.commit()

    client = httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test")
    headers = get_headers()

    # 1. Fetch all records
    resp = await client.get("/api/v1/admin/outbox", headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert len(data) == 3

    # 2. Query with status filter
    resp_failed = await client.get("/api/v1/admin/outbox?status=FAILED", headers=headers)
    assert resp_failed.status_code == 200
    data_failed = resp_failed.json()
    assert len(data_failed) == 1
    assert data_failed[0]["correlation_id"] == "corr-arch-def"
    assert data_failed[0]["attempts"] == 4

    # 3. Query with event_type filter
    resp_event = await client.get("/api/v1/admin/outbox?event_type=OTHER_EVENT", headers=headers)
    assert resp_event.status_code == 200
    data_event = resp_event.json()
    assert len(data_event) == 1
    assert data_event[0]["event_type"] == "OTHER_EVENT"
    assert data_event[0]["payload"] == {"data": "meta"}


@pytest.mark.asyncio
async def test_etmf_outbox_no_unencrypted_pii() -> None:
    """Ensure no unencrypted patient-identifiable information (PII) is present in eTMF outbox payload."""
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="DOCUMENT_ARCHIVAL",
            payload={
                "document_id": "doc-999",
                "filename": "protocol_signed.pdf",
                "content": "No patient details here.",
                "study_id": "study-999",
            },
            status="PENDING",
            attempts=0,
            correlation_id="corr-arch-pii",
            created_by="investigator",
            reason_for_change="Check PII",
        )
        session.add(record)
        await session.commit()

        # Retrieve and assert payload contains no unencrypted PII
        stmt = select(IntegrationOutbox).where(IntegrationOutbox.id == record.id)
        res = await session.execute(stmt)
        retrieved = res.scalars().first()
        payload_keys = retrieved.payload.keys()
        forbidden_pii_keywords = ["ssn", "birth_date", "dob", "home_address", "phone_number", "email_address", "patient_name", "subject_name"]
        for key in payload_keys:
            assert not any(pii_kw in key.lower() for pii_kw in forbidden_pii_keywords)
