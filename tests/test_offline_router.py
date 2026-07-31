"""Integration test suite for the server-side offline idempotent batch delta sync API.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import os
import time
import uuid

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    FormSubmission,
    SyncedBatchIdempotencyKey,
)
from apps.execution.main import app
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "datamanager_test_user",
    roles: str = "datamanager",
    change_reason: str = "Execute Offline Synchronization",
) -> dict:
    """Generate signed Gateway authentication headers for testing.

    Requirements: PRD-SYS-001
    """
    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id="tenant_default",
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test, and clear down after.

    Requirements: PRD-SYS-001
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_offline_sync_batch_success_and_idempotency() -> None:
    """Validate POST /api/v1/offline/sync-batch processes a batch delta successfully and enforces idempotency.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    client_batch_id = f"batch_{uuid.uuid4().hex[:12]}"
    form_id_1 = f"form_{uuid.uuid4().hex[:12]}"

    # Payload for offline batch synchronization
    sync_payload = {
        "client_batch_id": client_batch_id,
        "device_id": "pwa_device_site_01",
        "deltas": [
            {
                "delta_id": f"dl_{uuid.uuid4().hex[:8]}",
                "entity_type": "form_submission",
                "entity_id": form_id_1,
                "action": "CREATE",
                "payload": {
                    "study_id": "study_offline_01",
                    "site_id": "site_offline_01",
                    "subject_id": "sub_offline_01",
                    "form_id": "form_vs_01",
                    "status": "DRAFT",
                },
                "client_timestamp_utc": "2026-08-30T10:00:00Z",
                "reason_for_change": "Initial offline data entry",
            }
        ],
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Execute first sync call (SUCCESS expected)
        response = await client.post(
            "/api/v1/offline/sync-batch",
            json=sync_payload,
            headers=headers,
        )

        assert response.status_code == 200, f"Sync failed: {response.text}"
        data = response.json()
        assert data["client_batch_id"] == client_batch_id
        assert data["status"] == "SUCCESS"
        assert data["processed_count"] == 1
        assert len(data["conflicts"]) == 0

        # 2. Verify that the FormSubmission record was created in the database
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == form_id_1)
            res = await session.execute(stmt)
            record = res.scalar_one_or_none()
            assert record is not None
            assert record.study_id == "study_offline_01"
            assert record.status == "DRAFT"
            assert record.version == 1

            # Verify that the SyncedBatchIdempotencyKey was created
            stmt_key = select(SyncedBatchIdempotencyKey).where(
                SyncedBatchIdempotencyKey.client_batch_id == client_batch_id
            )
            res_key = await session.execute(stmt_key)
            key_record = res_key.scalar_one_or_none()
            assert key_record is not None
            assert key_record.device_id == "pwa_device_site_01"
            assert key_record.processed_count == 1

            # Verify that the OFFLINE_SYNC_BATCH AuditLog record was created
            stmt_audit = select(AuditLog).where(
                AuditLog.action == "OFFLINE_SYNC_BATCH",
                AuditLog.record_id == client_batch_id,
            )
            res_audit = await session.execute(stmt_audit)
            audit_record = res_audit.scalar_one_or_none()
            assert audit_record is not None
            assert audit_record.user_id == "datamanager_test_user"
            assert audit_record.change_reason == "Execute Offline Synchronization"
            assert audit_record.new_values["device_id"] == "pwa_device_site_01"

        # 3. Re-send identical batch (ALREADY_PROCESSED expected, idempotency guard)
        dup_response = await client.post(
            "/api/v1/offline/sync-batch",
            json=sync_payload,
            headers=headers,
        )

        assert dup_response.status_code == 200
        dup_data = dup_response.json()
        assert dup_data["client_batch_id"] == client_batch_id
        assert dup_data["status"] == "ALREADY_PROCESSED"
        assert dup_data["processed_count"] == 1
        assert len(dup_data["conflicts"]) == 0

        # 4. Verify no new record or duplicate mutations occurred (FormSubmission remains version 1)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == form_id_1)
            res = await session.execute(stmt)
            record = res.scalar_one_or_none()
            assert record is not None
            assert (
                record.version == 1
            )  # version did not increment, confirming zero duplicate mutations


@pytest.mark.asyncio
async def test_offline_sync_batch_update_action() -> None:
    """Validate POST /api/v1/offline/sync-batch handles UPDATE operations on existing entities correctly.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()
    form_id_2 = f"form_{uuid.uuid4().hex[:12]}"

    # Seed an existing form submission in the database first
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            existing_sub = FormSubmission(
                id=form_id_2,
                study_id="study_offline_02",
                site_id="site_offline_02",
                subject_id="sub_offline_02",
                form_id="form_vs_02",
                status="DRAFT",
                version=1,
            )
            session.add(existing_sub)

    client_batch_id = f"batch_{uuid.uuid4().hex[:12]}"
    update_payload = {
        "client_batch_id": client_batch_id,
        "device_id": "pwa_device_site_01",
        "deltas": [
            {
                "delta_id": f"dl_{uuid.uuid4().hex[:8]}",
                "entity_type": "form_submission",
                "entity_id": form_id_2,
                "action": "UPDATE",
                "payload": {
                    "status": "COMPLETED",
                },
                "client_timestamp_utc": "2026-08-30T11:00:00Z",
                "reason_for_change": "Update form status to completed",
            }
        ],
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/offline/sync-batch",
            json=update_payload,
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "SUCCESS"
        assert data["processed_count"] == 1

        # Verify that the database record has been updated and version incremented (part 11 audit)
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission).where(FormSubmission.id == form_id_2)
            res = await session.execute(stmt)
            record = res.scalar_one_or_none()
            assert record is not None
            assert record.status == "COMPLETED"
            assert record.version == 2
