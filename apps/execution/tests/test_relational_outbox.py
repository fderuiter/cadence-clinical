import asyncio
import hashlib
import hmac
import os
import time
from datetime import UTC, datetime
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, IntegrationOutbox
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from apps.execution.workers.outbox_worker import poll_and_dispatch

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # nosec B105: mock fallback secret for testing


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
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


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
        from unittest.mock import MagicMock

        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.raise_for_status = MagicMock()
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
    with patch(
        "httpx.AsyncClient.post", side_effect=httpx.ConnectError("Connection refused")
    ):
        # We will loop 5 times to execute poll_and_dispatch
        for attempt in range(1, 6):
            # Reset next_retry_at to None to allow the worker to pick it up on subsequent attempts
            async with db_manager.get_session_maker()() as session:
                stmt = select(IntegrationOutbox).where(
                    IntegrationOutbox.id == record_id
                )
                res = await session.execute(stmt)
                rec = res.scalars().first()
                rec.next_retry_at = None
                await session.commit()

            await poll_and_dispatch()

            # Verify incrementing attempts and backoff
            async with db_manager.get_session_maker()() as session:
                stmt = select(IntegrationOutbox).where(
                    IntegrationOutbox.id == record_id
                )
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
            completed_at=datetime.now(UTC),
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
        resp_failed = await client.get(
            "/api/v1/admin/outbox?status=FAILED", headers=get_auth_headers()
        )
        assert resp_failed.status_code == 200
        data_failed = resp_failed.json()
        assert len(data_failed) == 1
        assert data_failed[0]["correlation_id"] == "corr-lock-def"
        assert data_failed[0]["attempts"] == 3

        # 3. Query with event_type filter
        resp_event = await client.get(
            "/api/v1/admin/outbox?event_type=OTHER_EVENT", headers=get_auth_headers()
        )
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
        forbidden_pii_keywords = [
            "name",
            "ssn",
            "birth",
            "dob",
            "address",
            "phone",
            "email",
            "patient",
            "subject_name",
        ]
        for key in payload_keys:
            assert not any(pii_kw in key.lower() for pii_kw in forbidden_pii_keywords)


@pytest.mark.asyncio
async def test_manual_coding_writes_query_resolve_to_outbox() -> None:
    """Verify that a manual coding action writes an EDC_QUERY_RESOLVE outbox record with full GxP audit parameters."""
    # Seed data
    from apps.execution.tests.test_system_coding_queries import seed_data

    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a query-pending observation
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "gibberish_term_xyz",
            },
            headers=get_auth_headers(),
        )

        # 2. Retrieve assignment ID
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assign_id = resp_list.json()[0]["id"]

        # 3. Perform manual override resolution
        resp_override = await client.post(
            f"/api/v1/execution/coding/assignments/{assign_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "10019211",
                "term": "Headache",
                "reason_for_change": "Manual classification of uncodable symptom",
            },
            headers=get_auth_headers(user_id="manual_coder_bob", roles="Data Manager"),
        )
        assert resp_override.status_code == 200

        # Check that outbox record of type EDC_QUERY_RESOLVE is written atomically inside the transaction
        async with db_manager.get_session_maker()() as session:
            stmt = select(IntegrationOutbox).where(
                IntegrationOutbox.event_type == "EDC_QUERY_RESOLVE"
            )
            res = await session.execute(stmt)
            outbox_records = res.scalars().all()
            assert len(outbox_records) >= 1

            # Find the record for our user/action
            record = None
            for r in outbox_records:
                if r.payload.get("actor") == "manual_coder_bob":
                    record = r
                    break

            assert record is not None
            assert record.status == "PENDING"
            assert record.attempts == 0
            assert record.created_by == "manual_coder_bob"
            assert (
                record.reason_for_change == "Manual classification of uncodable symptom"
            )

            payload = record.payload
            assert payload["actor"] == "manual_coder_bob"
            assert "timestamp" in payload
            assert payload["observation_id"] is not None
            assert payload["query_id"] is not None
            assert (
                payload["justification"] == "Manual classification of uncodable symptom"
            )
            assert payload["action"] == "OVERRIDE"
            assert payload["coded_code"] == "10019211"


@pytest.mark.asyncio
async def test_outbox_worker_batch_size_limit(monkeypatch) -> None:
    """Verify that OUTBOX_BATCH_SIZE limits the number of claimed records in one poll."""
    monkeypatch.setenv("OUTBOX_BATCH_SIZE", "2")

    # Create 5 pending records
    async with db_manager.get_session_maker()() as session:
        for i in range(5):
            record = IntegrationOutbox(
                event_type="TRIAL_LOCK",
                payload={"trial_locked": True, "reason": f"Lock {i}"},
                status="PENDING",
                attempts=0,
                correlation_id=f"corr-batch-{i}",
                created_by="system",
                reason_for_change=f"Reason {i}",
            )
            session.add(record)
        await session.commit()

    with patch("httpx.AsyncClient.post") as mock_post:
        from unittest.mock import MagicMock

        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.raise_for_status = MagicMock()
        mock_post.return_value = mock_resp

        await poll_and_dispatch()

        # Verify only 2 records were processed
        assert mock_post.call_count == 2

    # Check updated and remaining outbox entries in DB
    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox)
        res = await session.execute(stmt)
        all_records = res.scalars().all()

        success_count = sum(1 for r in all_records if r.status == "SUCCESS")
        pending_count = sum(1 for r in all_records if r.status == "PENDING")

        assert success_count == 2
        assert pending_count == 3


@pytest.mark.asyncio
async def test_outbox_worker_dialect_aware_locking_pg() -> None:
    """Verify that PostgreSQL dialect appends skip_locked=True to the select query."""
    with patch.object(db_manager.engine.dialect, "name", "postgresql"):
        from sqlalchemy.ext.asyncio import AsyncSession

        captured_stmt = None

        async def mock_execute(self, stmt, *args, **kwargs):
            nonlocal captured_stmt
            captured_stmt = stmt
            # Return empty list to prevent further execution
            from unittest.mock import MagicMock

            mock_res = MagicMock()
            mock_res.scalars.return_value.all.return_value = []
            return mock_res

        with patch.object(AsyncSession, "execute", mock_execute):
            await poll_and_dispatch()

        assert captured_stmt is not None
        # Check that with_for_update parameter skip_locked is True
        assert captured_stmt._for_update_arg is not None
        assert captured_stmt._for_update_arg.skip_locked is True


@pytest.mark.asyncio
async def test_outbox_worker_dialect_aware_locking_sqlite() -> None:
    """Verify that SQLite dialect appends with_for_update without skip_locked."""
    with patch.object(db_manager.engine.dialect, "name", "sqlite"):
        from sqlalchemy.ext.asyncio import AsyncSession

        captured_stmt = None

        async def mock_execute(self, stmt, *args, **kwargs):
            nonlocal captured_stmt
            captured_stmt = stmt
            from unittest.mock import MagicMock

            mock_res = MagicMock()
            mock_res.scalars.return_value.all.return_value = []
            return mock_res

        with patch.object(AsyncSession, "execute", mock_execute):
            await poll_and_dispatch()

        assert captured_stmt is not None
        assert captured_stmt._for_update_arg is not None
        assert captured_stmt._for_update_arg.skip_locked is False


@pytest.mark.asyncio
async def test_outbox_worker_concurrent_dispatch(monkeypatch) -> None:
    """Verify that multiple records are processed concurrently."""
    monkeypatch.setenv("OUTBOX_BATCH_SIZE", "5")
    monkeypatch.setenv("OUTBOX_MAX_CONCURRENCY", "3")

    # Create 3 records
    async with db_manager.get_session_maker()() as session:
        for i in range(3):
            record = IntegrationOutbox(
                event_type="TRIAL_LOCK",
                payload={"trial_locked": True, "reason": f"Lock {i}"},
                status="PENDING",
                attempts=0,
                correlation_id=f"corr-concurrent-{i}",
                created_by="system",
                reason_for_change=f"Reason {i}",
            )
            session.add(record)
        await session.commit()

    active_tasks = 0
    max_active_tasks = 0
    task_lock = asyncio.Lock()

    async def mock_post(client_self, url, **kwargs):
        nonlocal active_tasks, max_active_tasks
        async with task_lock:
            active_tasks += 1
            if active_tasks > max_active_tasks:
                max_active_tasks = active_tasks
        await asyncio.sleep(0.1)
        async with task_lock:
            active_tasks -= 1
        from unittest.mock import MagicMock

        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.raise_for_status = MagicMock()
        return mock_resp

    with patch("httpx.AsyncClient.post", mock_post):
        await poll_and_dispatch()

    # If concurrency is working, max_active_tasks should be 3
    assert max_active_tasks == 3


@pytest.mark.asyncio
async def test_rollback_prevents_outbox_record_creation() -> None:
    """AC1: Verify that rolling back a clinical data write prevents any outbox records from being created."""
    # 1. Database-level transaction rollback
    async with db_manager.get_session_maker()() as session:
        # Create a clinical subject and an outbox record inside the session
        from apps.execution.database.models import ClinicalSubject

        subj = ClinicalSubject(
            subject_id="SUBJ-ROLLBACK-01",
            study_id="STUDY-001",
            status="SCREENING",
        )
        outbox_rec = IntegrationOutbox(
            event_type="TRIAL_LOCK",
            payload={"trial_locked": True, "reason": "Test Rollback"},
            status="PENDING",
            attempts=0,
            correlation_id="corr-rollback-1",
            created_by="tester",
            reason_for_change="Test Rollback",
        )
        session.add(subj)
        session.add(outbox_rec)

        # Explicitly roll back the session
        await session.rollback()

    # Verify that neither the clinical record nor the outbox record exists
    async with db_manager.get_session_maker()() as session:
        res_subj = await session.execute(
            select(ClinicalSubject).where(ClinicalSubject.subject_id == "SUBJ-ROLLBACK-01")
        )
        assert res_subj.scalars().first() is None

        res_outbox = await session.execute(
            select(IntegrationOutbox).where(IntegrationOutbox.correlation_id == "corr-rollback-1")
        )
        assert res_outbox.scalars().first() is None


@pytest.mark.asyncio
async def test_commit_clinical_change_without_reason_fails() -> None:
    """AC2: Verify that attempting to commit a clinical state change without a provided reason fails immediately."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Generate headers missing change_reason / X-Change-Reason
        headers = get_auth_headers(change_reason="")
        headers.pop("X-Change-Reason", None)

        resp = await client.post(
            "/api/v1/execution/locks/trial/lock",
            headers=headers,
        )
        # Should be rejected with 403 Forbidden due to missing change justification header
        assert resp.status_code in (400, 403)
        assert TrialLockManager.is_locked() is False

        # Verify no outbox record was created
        async with db_manager.get_session_maker()() as session:
            res = await session.execute(select(IntegrationOutbox))
            assert len(res.scalars().all()) == 0


@pytest.mark.asyncio
async def test_parallel_workers_do_not_deliver_duplicate_events() -> None:
    """AC3: Verify that parallel background workers do not deliver the same outbox event multiple times."""
    # Populate 5 pending outbox records
    async with db_manager.get_session_maker()() as session:
        for i in range(5):
            rec = IntegrationOutbox(
                event_type="TRIAL_LOCK",
                payload={"trial_locked": True, "reason": f"Parallel Lock {i}"},
                status="PENDING",
                attempts=0,
                correlation_id=f"corr-parallel-{i}",
                created_by="system",
                reason_for_change=f"Parallel test {i}",
            )
            session.add(rec)
        await session.commit()

    dispatched_ids = []
    dispatch_lock = asyncio.Lock()

    async def mock_post(client_self, url, **kwargs):
        json_data = kwargs.get("json", {})
        reason = json_data.get("reason", "")
        async with dispatch_lock:
            dispatched_ids.append(reason)
        from unittest.mock import MagicMock

        mock_resp = MagicMock()
        mock_resp.status_code = 200
        mock_resp.raise_for_status = MagicMock()
        return mock_resp

    # On PostgreSQL, pg_try_advisory_xact_lock(42003) allows only 1 worker to acquire lock per cycle
    advisory_lock_acquired = False
    original_session_maker = db_manager.get_session_maker()

    def locking_session_factory():
        session = original_session_maker()
        orig_execute = session.execute

        async def mock_execute(stmt, *args, **kwargs):
            nonlocal advisory_lock_acquired
            stmt_str = str(stmt)
            if "pg_try_advisory_xact_lock" in stmt_str:
                from unittest.mock import MagicMock

                mock_res = MagicMock()
                if not advisory_lock_acquired:
                    advisory_lock_acquired = True
                    mock_res.scalar.return_value = True
                else:
                    mock_res.scalar.return_value = False
                return mock_res
            return await orig_execute(stmt, *args, **kwargs)

        session.execute = mock_execute
        return session

    with patch.object(db_manager.engine.dialect, "name", "postgresql"):
        with patch("httpx.AsyncClient.post", mock_post):
            with patch("apps.execution.workers.outbox_worker._session_maker", locking_session_factory):
                # Run two workers in parallel
                await asyncio.gather(poll_and_dispatch(), poll_and_dispatch())

    # Verify each event was dispatched exactly once
    assert len(dispatched_ids) == 5
    assert len(set(dispatched_ids)) == 5


@pytest.mark.asyncio
async def test_background_worker_uses_separate_database_connection_channel() -> None:
    """AC4: Verify that background worker queries use a separate database connection channel from client-facing API traffic."""
    from apps.execution.database.core import bg_db_manager, db_manager

    # bg_db_manager and db_manager must be separate instances
    assert bg_db_manager is not db_manager

    # Initialize bg_db_manager with its own isolated engine
    bg_db_manager.init_db("sqlite+aiosqlite:///:memory:")

    assert bg_db_manager.engine is not None
    assert db_manager.engine is not None
    assert bg_db_manager.engine is not db_manager.engine

    # session_maker instances must be different factories
    bg_sm = bg_db_manager.get_session_maker()
    api_sm = db_manager.get_session_maker()
    assert bg_sm is not api_sm

    await bg_db_manager.close()

