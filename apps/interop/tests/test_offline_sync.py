"""Integration test suite for offline data integrity and synchronization.

Requirements: PRD-SYS-001 | GxP 21 CFR Part 11 Regulated
"""

import hashlib
import hmac
import os
import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, FormSubmission
from apps.execution.main import app
from apps.execution.services.offline_sync import OfflineSyncEngine
from packages.security.signing import generate_canonical_signature

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
    """Generate Gateway signature-compliant authentication headers."""
    import json

    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
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
    """Setup in-memory SQLite database before each test and clear down after.

    Requirements: PRD-SYS-001
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest_asyncio.fixture
async def db_session():
    """Get a standard clean database session for test isolation.

    Requirements: PRD-SYS-001
    """
    async with db_manager.get_session_maker()() as session:
        yield session


@pytest_asyncio.fixture
def sample_offline_batch():
    """Fixture to generate a queued offline delta batch of 5 form submissions.

    Requirements: PRD-SYS-001
    """
    return {
        "client_batch_id": "batch_88001",
        "device_id": "ipad_site01_01",
        "deltas": [
            {
                "entity_type": "ECRF_FORM",
                "entity_id": f"form_v1_{i}",
                "client_timestamp_utc": f"2026-07-30T10:0{i}:00Z",
                "action": "SUBMIT",
                "payload": {
                    "study_id": "STUDY-001",
                    "site_id": "SITE-001",
                    "subject_id": "SUBJ-101",
                    "visit_id": "VISIT-201",
                    "VS.SYSBP": 120 + i,
                    "VS.DIABP": 80,
                },
                "reason_for_change": "Initial offline capture",
            }
            for i in range(1, 6)
        ],
    }


@pytest.mark.asyncio
async def test_offline_delta_ingestion_integrity(db_session):
    """Validate server-side idempotent ingestion of queued offline eCRF delta transactions.

    Requirements: PRD-SYS-001
    """
    engine = OfflineSyncEngine(session=db_session)
    batch_payload = {
        "client_batch_id": "batch_99201",
        "device_id": "ipad_site01_04",
        "deltas": [
            {
                "entity_type": "ECRF_FORM",
                "entity_id": "form_v1_101",
                "client_timestamp_utc": "2026-07-30T14:00:00Z",
                "action": "SUBMIT",
                "payload": {"VS.SYSBP": 120, "VS.DIABP": 80},
                "reason_for_change": "Initial offline data capture",
            }
        ],
    }

    result = await engine.process_delta_batch(batch_payload)
    assert result["status"] == "SUCCESS"
    assert result["synced_count"] == 1


@pytest.mark.asyncio
async def test_offline_batch_sync_success(sample_offline_batch):
    """Submit batch of 5 eCRF form submissions queued offline; assert server processes all items and returns synced_count = 5.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        res = await client.post(
            "/api/v1/execution/offline/sync",
            json=sample_offline_batch,
            headers=get_auth_headers(),
        )
        assert res.status_code == 200
        data = res.json()
        assert data["status"] == "SUCCESS"
        assert data["synced_count"] == 5

        # Query DB to assert records were created
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission)
            res_db = await session.execute(stmt)
            submissions = res_db.scalars().all()
            assert len(submissions) == 5


@pytest.mark.asyncio
async def test_offline_sync_idempotency(sample_offline_batch):
    """Re-send identical sync batch with same client_batch_id; assert server returns HTTP 200 with zero duplicate database insertions.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # First send
        res1 = await client.post(
            "/api/v1/execution/offline/sync",
            json=sample_offline_batch,
            headers=get_auth_headers(),
        )
        assert res1.status_code == 200
        assert res1.json()["synced_count"] == 5

        # Re-send identical sync batch
        res2 = await client.post(
            "/api/v1/execution/offline/sync",
            json=sample_offline_batch,
            headers=get_auth_headers(),
        )
        assert res2.status_code == 200
        assert res2.json()["synced_count"] == 0
        assert "already processed" in res2.json()["message"]

        # Ensure no duplicate insertions
        async with db_manager.get_session_maker()() as session:
            stmt = select(FormSubmission)
            res_db = await session.execute(stmt)
            submissions = res_db.scalars().all()
            assert len(submissions) == 5


@pytest.mark.asyncio
async def test_offline_sync_conflict_resolution(db_session):
    """Submit offline delta modifying form F-101 where server already holds updated F-101 record; assert conflict resolution engine flags record NEEDS_REVIEW in audit database.

    Requirements: PRD-SYS-001
    """
    # 1. Populate DB with an existing FormSubmission for "F-101"
    existing_form = FormSubmission(
        id="F-101",
        study_id="STUDY-001",
        site_id="SITE-001",
        subject_id="SUBJ-101",
        visit_id="VISIT-201",
        form_id="F-101",
        status="COMPLETED",
    )
    db_session.add(existing_form)
    await db_session.commit()

    # 2. Add an AuditLog row with a newer timestamp than the client's delta
    audit_record = AuditLog(
        table_name="form_submissions",
        record_id="F-101",
        action="UPDATE",
        user_id="other_user",
        timestamp=datetime(
            2026, 7, 31, 10, 0, 0
        ),  # Newer than client's client_timestamp_utc
        old_values={"status": "DRAFT"},
        new_values={"status": "COMPLETED"},
        change_reason="Server-side update",
    )
    db_session.add(audit_record)
    await db_session.commit()

    # 3. Submit offline delta with an older client timestamp
    engine = OfflineSyncEngine(session=db_session)
    batch_payload = {
        "client_batch_id": "batch_conflict_999",
        "device_id": "ipad_site01_04",
        "deltas": [
            {
                "entity_type": "ECRF_FORM",
                "entity_id": "F-101",
                "client_timestamp_utc": "2026-07-30T14:00:00Z",  # Older than server's audit log timestamp
                "action": "SUBMIT",
                "payload": {"VS.SYSBP": 130, "VS.DIABP": 85},
                "reason_for_change": "Correction",
            }
        ],
    }

    result = await engine.process_delta_batch(batch_payload)
    assert result["status"] == "SUCCESS"
    assert result["synced_count"] == 1

    # 4. Verify that conflict resolution flags record NEEDS_REVIEW
    await db_session.refresh(existing_form)
    assert existing_form.status == "NEEDS_REVIEW"

    # Query the AuditLog table to assert a conflict entry with NEEDS_REVIEW is logged
    stmt = select(AuditLog).where(
        AuditLog.table_name == "form_submissions",
        AuditLog.record_id == "F-101",
        AuditLog.action == "CONFLICT",
    )
    res_db = await db_session.execute(stmt)
    conflict_logs = res_db.scalars().all()
    assert len(conflict_logs) == 1
    assert conflict_logs[0].change_reason == "NEEDS_REVIEW"
    assert conflict_logs[0].new_values["status"] == "NEEDS_REVIEW"


@pytest.mark.asyncio
async def test_offline_sync_cryptographic_verification():
    """Verify that cryptographic signature validation on offline sync batch behaves correctly.

    Requirements: PRD-SYS-001
    """
    batch_data = {
        "client_batch_id": "batch_sig_111",
        "device_id": "device_secure_01",
        "deltas": [
            {
                "entity_type": "ECRF_FORM",
                "entity_id": "form_sig_1",
                "client_timestamp_utc": "2026-07-30T10:00:00Z",
                "action": "SUBMIT",
                "payload": {"VS.SYSBP": 120, "VS.DIABP": 80},
                "reason_for_change": "Initial capture",
            }
        ],
    }

    # Generate valid signature
    secret = b"internal-gateway-secret-12345"
    sig = generate_canonical_signature(batch_data, secret)

    # Attach signature
    batch_payload_signed = {**batch_data, "signature": sig}

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Happy Path with valid signature
        res1 = await client.post(
            "/api/v1/execution/offline/sync",
            json=batch_payload_signed,
            headers=get_auth_headers(),
        )
        assert res1.status_code == 200
        assert res1.json()["synced_count"] == 1

        # Tampered Path: modify a value in the payload but keep the same signature
        tampered_payload = {**batch_payload_signed, "client_batch_id": "batch_sig_222"}
        res2 = await client.post(
            "/api/v1/execution/offline/sync",
            json=tampered_payload,
            headers=get_auth_headers(),
        )
        # Should fail with 400 Bad Request because of invalid cryptographic signature
        assert res2.status_code == 400
        assert "Invalid cryptographic signature" in res2.json()["detail"]
