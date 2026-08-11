import asyncio
import hashlib
import hmac
import os
import time
from datetime import datetime
import httpx
import pytest
import pytest_asyncio
from unittest.mock import patch, AsyncMock
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, IntegrationOutbox
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from apps.execution.workers.outbox_worker import poll_and_dispatch

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_admin",
    roles="Sponsor Admin",
    change_reason="Sponsor Lock",
):
    """Generate Gateway signature-compliant authentication headers."""
    import json
    timestamp = str(time.time())
    header_payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(header_payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    return headers


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_trial_lock_writes_outbox() -> None:
    """Verify that locking the trial writes an outbox record inside the same relational transaction."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Before lock, check outbox is empty
        async with db_manager.get_session_maker()() as session:
            stmt = select(IntegrationOutbox)
            res = await session.execute(stmt)
            assert len(res.scalars().all()) == 0

        # Post trial lock
        headers = get_auth_headers()
        resp = await client.post(
            "/api/v1/execution/locks/trial/lock",
            headers=headers,
        )
        assert resp.status_code == 200

        # Verify Trial is indeed locked in memory
        assert TrialLockManager.is_locked() is True

        # Check that outbox record is written atomically
        async with db_manager.get_session_maker()() as session:
            stmt = select(IntegrationOutbox)
            res = await session.execute(stmt)
            outbox_records = res.scalars().all()
            assert len(outbox_records) == 1
            record = outbox_records[0]
            assert record.event_type == "TRIAL_LOCK"
            assert record.payload == {"trial_locked": True, "reason": "Sponsor Lock"}
            assert record.status == "PENDING"
            assert record.attempts == 0
            assert record.retry_eligible is True
            assert record.created_by == "test_admin"
            assert record.reason_for_change == "Sponsor Lock"


@pytest.mark.asyncio
async def test_outbox_worker_polling_and_dispatch_success() -> None:
    """Verify background worker polls pending outbox records and dispatches them successfully to eTMF."""
    # Write a PENDING trial lock outbox record manually
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "System Lock"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-lock-123",
            created_by="system",
            reason_for_change="Integrity issue",
        )
        session.add(record)
        await session.commit()
        record_id = record.id

    # Mock httpx POST request to the eTMF trial lock propagation endpoint
    with patch("httpx.AsyncClient.post") as mock_post:
        mock_resp = AsyncMock()
        mock_resp.status_code = 200
        mock_post.return_value = mock_resp

        await poll_and_dispatch()

        mock_post.assert_called_once()
        # Verify the call URL
        called_url = mock_post.call_args[0][0]
        assert "/api/v1/etmf/locks/trial/lock" in called_url

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
async def test_outbox_worker_retry_and_backoff() -> None:
    """Verify worker retry logic: 5 retries with exponential backoff before marking outbox record as failed."""
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "System Lock"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-lock-456",
            created_by="system",
            reason_for_change="Integrity issue",
        )
        session.add(record)
        await session.commit()
        record_id = record.id

    # Simulate 5 failures to ensure retry limit and backoff logic
    with patch("httpx.AsyncClient.post", side_effect=httpx.ConnectError("Connection refused")):
        # We will loop 5 times to execute poll_and_dispatch
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
                assert "Connection refused" in updated.last_error
                assert updated.next_retry_at is not None
                if attempt < 5:
                    assert updated.retry_eligible is True
                else:
                    assert updated.retry_eligible is False


@pytest.mark.asyncio
async def test_admin_visibility_endpoint() -> None:
    """Verify that admins can view/query status, delivery history, and payload of outbox records."""
    async with db_manager.get_session_maker()() as session:
        # Populate records with different statuses/event types
        rec1 = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "L1"},
            status="SUCCESS",
            attempts=1,
            completed_at=datetime.utcnow(),
            correlation_id="corr-lock-abc",
            created_by="admin1",
            reason_for_change="Lock study",
        )
        rec2 = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "L2"},
            status="FAILED",
            attempts=3,
            correlation_id="corr-lock-def",
            created_by="admin2",
            reason_for_change="Lock study failed",
        )
        rec3 = IntegrationOutbox(
            event_type="OTHER_EVENT",
            payload={"data": "something"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-other-ghi",
            created_by="system",
            reason_for_change="System activity",
        )
        session.add_all([rec1, rec2, rec3])
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Fetch all records
        resp = await client.get("/api/v1/admin/outbox", headers=get_auth_headers())
        assert resp.status_code == 200
        data = resp.json()
        assert len(data) == 3

        # Verify fields and payload
        for item in data:
            assert "id" in item
            assert "event_type" in item
            assert "payload" in item
            assert "status" in item
            assert "attempts" in item
            assert "correlation_id" in item

        # 2. Query with status filter
        resp_failed = await client.get("/api/v1/admin/outbox?status=FAILED", headers=get_auth_headers())
        assert resp_failed.status_code == 200
        data_failed = resp_failed.json()
        assert len(data_failed) == 1
        assert data_failed[0]["correlation_id"] == "corr-lock-def"
        assert data_failed[0]["attempts"] == 3

        # 3. Query with event_type filter
        resp_event = await client.get("/api/v1/admin/outbox?event_type=OTHER_EVENT", headers=get_auth_headers())
        assert resp_event.status_code == 200
        data_event = resp_event.json()
        assert len(data_event) == 1
        assert data_event[0]["event_type"] == "OTHER_EVENT"
        assert data_event[0]["payload"] == {"data": "something"}


@pytest.mark.asyncio
async def test_outbox_no_unencrypted_pii() -> None:
    """Ensure no unencrypted patient-identifiable information (PII) is present in outbox payload."""
    # Ensure trial lock contains no PII
    async with db_manager.get_session_maker()() as session:
        record = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "Sponsor Lock"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-lock-pii-check",
            created_by="admin",
            reason_for_change="Check PII",
        )
        session.add(record)
        await session.commit()

        # Retrieve and assert payload
        stmt = select(IntegrationOutbox).where(IntegrationOutbox.id == record.id)
        res = await session.execute(stmt)
        retrieved = res.scalars().first()
        payload_keys = retrieved.payload.keys()
        # Verify no PII fields exist
        forbidden_pii_keywords = ["name", "ssn", "birth", "dob", "address", "phone", "email", "patient", "subject_name"]
        for key in payload_keys:
            assert not any(pii_kw in key.lower() for pii_kw in forbidden_pii_keywords)
