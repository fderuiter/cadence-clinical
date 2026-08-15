import hashlib
import hmac
import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base, TMFAuditLog, TMFDocument
from apps.etmf.main import app


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup in-memory eTMF database for unit and integration testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.fixture(autouse=True)
def mock_env_secret(monkeypatch):
    """Monkeypatch INBOUND_EMAIL_HMAC_SECRET for testing."""
    monkeypatch.setenv("INBOUND_EMAIL_HMAC_SECRET", "test-secret-key-12345")
    monkeypatch.setenv(
        "INBOUND_EMAIL_MAX_SIZE_BYTES", "50000"
    )  # 50KB size limit for testing


def compute_signature(
    timestamp: str, token: str, secret: str = "test-secret-key-12345"
) -> str:
    """Helper to compute valid HMAC-SHA256 signature for testing."""
    return hmac.new(
        secret.encode("utf-8"), f"{timestamp}{token}".encode(), hashlib.sha256
    ).hexdigest()


def test_valid_inbound_email_ingestion():
    """Test that a valid signed inbound email is successfully ingested and routed."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-1"
    signature = compute_signature(timestamp, token)

    response = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Trial Monitoring Plan Update",
            "body-plain": "Please see the attached monitoring plan report.",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<valid-msg-1@example.com>",
        },
    )

    assert response.status_code == 201
    assert response.json() == {"status": "accepted"}


def test_invalid_signature_rejection():
    """Test that an invalid signature is rejected with HTTP 401."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-2"
    signature = "wrong-signature-12345"

    response = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-2@example.com>",
        },
    )

    assert response.status_code == 401
    assert "Unauthorized" in response.text


def test_stale_timestamp_rejection():
    """Test that a stale timestamp (older than 300 seconds) is rejected with HTTP 401."""
    client = TestClient(app)
    timestamp = str(time.time() - 301)  # Stale timestamp
    token = "unique-token-3"
    signature = compute_signature(timestamp, token)

    response = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-3@example.com>",
        },
    )

    assert response.status_code == 401


def test_replay_protection():
    """Test that duplicate requests with the same token/Message-Id are rejected as replays."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-replay"
    signature = compute_signature(timestamp, token)

    # First request
    response1 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-replay@example.com>",
        },
    )
    assert response1.status_code == 201

    # Second request (replayed token)
    response2 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-replay2@example.com>",
        },
    )
    assert response2.status_code == 401


def test_oversized_payload_rejection():
    """Test that payloads exceeding INBOUND_EMAIL_MAX_SIZE_BYTES are rejected with HTTP 413."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-oversized"
    signature = compute_signature(timestamp, token)

    # Large body content
    large_body = "A" * 60000

    response = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": large_body,
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-large@example.com>",
        },
    )

    assert response.status_code == 413


def test_unresolvable_recipient_address():
    """Test that unresolvable recipient addresses return HTTP 422 without leaking study info."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-unresolvable"
    signature = compute_signature(timestamp, token)

    # Case 1: missing study- prefix
    response1 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<msg-unres1@example.com>",
        },
    )
    assert response1.status_code == 422
    assert "Invalid routing metadata" in response1.text

    # Case 2: unresolvable binder hint
    token2 = "unique-token-unresolvable2"
    signature2 = compute_signature(timestamp, token2)
    response2 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+invalid_hint_here@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp,
            "token": token2,
            "signature": signature2,
            "Message-Id": "<msg-unres2@example.com>",
        },
    )
    assert response2.status_code == 422
    assert "Invalid routing metadata" in response2.text


def test_multi_attachment_ingestion():
    """Test that each attachment is ingested as its own versioned document."""
    client = TestClient(app)
    timestamp = str(time.time())
    token = "unique-token-multi"
    signature = compute_signature(timestamp, token)

    response = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Trial Monitoring Plan Update",
            "body-plain": "Cover letter text.",
            "timestamp": timestamp,
            "token": token,
            "signature": signature,
            "Message-Id": "<multi-msg@example.com>",
        },
        files=[
            ("attachment1", ("plan1.txt", b"Content of plan 1", "text/plain")),
            ("attachment2", ("plan2.txt", b"Content of plan 2", "text/plain")),
        ],
    )

    assert response.status_code == 201
    assert response.json() == {"status": "accepted"}


@pytest.mark.asyncio
async def test_idempotency():
    """Test that posting the same Message-Id twice results in a safe no-op on the second call."""
    client = TestClient(app)
    timestamp1 = str(time.time())
    token1 = "unique-token-idempotent1"
    signature1 = compute_signature(timestamp1, token1)

    message_id = "<idempotent-msg@example.com>"

    # First ingestion
    response1 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Idempotent Subject",
            "body-plain": "Content",
            "timestamp": timestamp1,
            "token": token1,
            "signature": signature1,
            "Message-Id": message_id,
        },
    )
    assert response1.status_code == 201

    # Verify document rows
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        stmt = select(TMFDocument).where(TMFDocument.study_id == "xyz")
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1

    # Second ingestion with same Message-Id but different token/signature
    timestamp2 = str(time.time())
    token2 = "unique-token-idempotent2"
    signature2 = compute_signature(timestamp2, token2)

    response2 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Idempotent Subject",
            "body-plain": "Content",
            "timestamp": timestamp2,
            "token": token2,
            "signature": signature2,
            "Message-Id": message_id,
        },
    )
    assert response2.status_code == 201
    assert response2.json() == {"status": "accepted"}

    # Verify no new document rows were created
    async with session_maker() as session:
        stmt = select(TMFDocument).where(TMFDocument.study_id == "xyz")
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1


@pytest.mark.asyncio
async def test_immutability_violation_inbound_email():
    """Test that inbound-email to an already-SIGNED artifact yields MUTATION_REJECTED/403."""
    client = TestClient(app)
    timestamp1 = str(time.time())
    token1 = "unique-token-immutability-1"
    signature1 = compute_signature(timestamp1, token1)

    # Ingest initial document
    response1 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body",
            "timestamp": timestamp1,
            "token": token1,
            "signature": signature1,
            "Message-Id": "<msg-immut-1@example.com>",
        },
    )
    assert response1.status_code == 201

    # Mark the document as SIGNED / APPROVED
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        stmt = select(TMFDocument).where(TMFDocument.study_id == "xyz")
        res = await session.execute(stmt)
        doc = res.scalars().first()
        doc.status = "SIGNED"
        doc.approval_status = "APPROVED"
        doc.signature_manifestation = {"mock": "manifestation"}
        await session.commit()

    # Attempt to ingest new version into already-SIGNED artifact path
    timestamp2 = str(time.time())
    token2 = "unique-token-immutability-2"
    signature2 = compute_signature(timestamp2, token2)

    response2 = client.post(
        "/api/v1/etmf/inbound-email",
        data={
            "sender": "sender@example.com",
            "recipient": "study-xyz+conduct@example.com",
            "subject": "Subject",
            "body-plain": "Body New Version",
            "timestamp": timestamp2,
            "token": token2,
            "signature": signature2,
            "Message-Id": "<msg-immut-2@example.com>",
        },
    )
    assert response2.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in response2.text

    # Verify MUTATION_REJECTED audit log was written
    async with session_maker() as session:
        stmt = select(TMFAuditLog).where(TMFAuditLog.action == "MUTATION_REJECTED")
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) == 1
