import time
from datetime import datetime, timedelta, timezone

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.interop.database import db_manager
from apps.interop.main import app
from apps.interop.models import (
    Base,
    ClinicalQuery,
    EPROSubmission,
    EPROSubmissionDefeated,
    Instrument,
    InteropAuditLog,
    SubjectAssignment,
)
from apps.interop.sync_engine import (
    SyncMetadata,
    SyncRecord,
    get_signature_payload,
)
from packages.security.signing import generate_canonical_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory Interop database for defeated and structural conflict testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin", change_reason: str = "", user_id: str = "test_user"
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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
async def test_defeated_record_persistence_on_conflicts():
    """
    Verify that normal conflict outcomes (CLIENT_WINS, SERVER_WINS, MERGE)
    record the winning decision, version increment, and preserve defeated/overwritten
    inputs with status 'Defeated by online-merge conflict resolution' in EPROSubmissionDefeated.
    # @req:Trace-9
    """
    # @req:PRD-EDC-008
    # 1. Setup Instrument and SubjectAssignment
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        inst = Instrument(
            id="diary_1",
            name="Daily Symptom Tracker",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Setup tracker",
            version_index=1,
        )
        session.add(inst)

        now = datetime.now(timezone.utc).replace(tzinfo=None)
        assign = SubjectAssignment(
            subject_id="subject_conflict",
            instrument_id="diary_1",
            start_date=now - timedelta(days=2),
            end_date=now + timedelta(days=2),
            created_by="admin",
            reason_for_change="Setup assignment",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers(
        roles="Subject", change_reason="Initial submission", user_id="subject_conflict"
    )

    # 2. Submit initial record (Created)
    payload_init = {
        "subject_id": "subject_conflict",
        "diary_id": "diary_1",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 1, "nausea": "none"},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_phone",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_init, headers=headers
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"

    # 3. Conflict resolution via CLIENT_WINS: incoming overwrites existing.
    # The pre-existing/overwritten answers should be recorded in EPROSubmissionDefeated.
    headers_client = get_auth_headers(
        roles="Subject",
        change_reason="Client wins overwrite",
        user_id="subject_conflict",
    )
    payload_client = {
        "subject_id": "subject_conflict",
        "diary_id": "diary_1",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 5, "nausea": "severe"},
        "offline_sync_markers": {
            "sequence_number": 2,
            "client_id": "dev_phone",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_client, headers=headers_client
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "UPDATED_CLIENT_WINS"
    assert resp.json()["answers"]["pain"] == 5

    # Verify defeated table contains the initial record
    async with async_session() as session:
        stmt = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_conflict"
        )
        res = await session.execute(stmt)
        defeated_records = res.scalars().all()
        assert len(defeated_records) == 1
        assert defeated_records[0].answers == {"pain": 1, "nausea": "none"}
        assert (
            defeated_records[0].status == "Defeated by online-merge conflict resolution"
        )

        # Verify audit log contains decision and version increment
        stmt_audit = (
            select(InteropAuditLog)
            .where(InteropAuditLog.action == "EPRO_RECONCILE")
            .order_by(InteropAuditLog.timestamp.desc())
        )
        res_audit = await session.execute(stmt_audit)
        audits = res_audit.scalars().all()
        # Should have CREATED audit and CLIENT_WINS audit
        assert len(audits) >= 2
        assert "Decision: CLIENT_WINS." in audits[0].details
        assert "Version incremented to 2." in audits[0].details

    # 4. Conflict resolution via SERVER_WINS: existing is kept, incoming is ignored/defeated.
    headers_server = get_auth_headers(
        roles="Subject", change_reason="Server wins ignore", user_id="subject_conflict"
    )
    payload_server = {
        "subject_id": "subject_conflict",
        "diary_id": "diary_1",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 9, "nausea": "extreme"},
        "offline_sync_markers": {
            "sequence_number": 3,
            "client_id": "dev_phone",
            "conflict_strategy": "SERVER_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=payload_server, headers=headers_server
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "IGNORED_SERVER_WINS"

    # Verify defeated table now contains both the initial record (pain=1) and the ignored incoming record (pain=9)
    async with async_session() as session:
        stmt = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_conflict"
        )
        res = await session.execute(stmt)
        defeated_records = res.scalars().all()
        assert len(defeated_records) == 2

        pains = [r.answers["pain"] for r in defeated_records]
        assert 1 in pains
        assert 9 in pains

        for record in defeated_records:
            assert record.status == "Defeated by online-merge conflict resolution"

        # Verify audit log contains decision and current version (no increment)
        stmt_audit = (
            select(InteropAuditLog)
            .where(InteropAuditLog.action == "EPRO_RECONCILE")
            .order_by(InteropAuditLog.timestamp.desc())
        )
        res_audit = await session.execute(stmt_audit)
        audits = res_audit.scalars().all()
        assert "Decision: SERVER_WINS." in audits[0].details
        assert "Version index is 2." in audits[0].details


@pytest.mark.asyncio
async def test_structural_conflict_on_missing_target():
    """
    Verify that edits targeting missing/deleted target records trigger structural conflicts.
    They must be rejected (no normal EPROSubmission created/updated), retain reviewable state
    in EPROSubmissionDefeated, write audit logs with reason 'SYSTEM SYNC EXCEPTION TRIGGERED',
    and create an OPEN ClinicalQuery.
    """
    # @req:PRD-EDC-008
    client = TestClient(app)
    # Target records (Instrument/SubjectAssignment) do NOT exist in the database for this subject.
    headers = get_auth_headers(
        roles="Subject",
        change_reason="Submit against missing target",
        user_id="subject_ghost",
    )

    payload = {
        "subject_id": "subject_ghost",
        "diary_id": "diary_non_existent",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 10},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_ghost",
            "conflict_strategy": "CLIENT_WINS",
        },
    }

    # Submit
    resp = client.post("/api/v1/interop/epro/submit", json=payload, headers=headers)
    assert resp.status_code == 201
    result = resp.json()
    assert result["status"] == "STRUCTURAL_CONFLICT"
    assert "query" in result
    assert result["query"]["status"] == "OPEN"
    assert result["query"]["test_code"] == "diary_non_existent"

    # Verify database state
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        # 1. Rejection: Ensure no normal EPROSubmission exists for subject_ghost
        stmt_normal = select(EPROSubmission).where(
            EPROSubmission.subject_id == "subject_ghost"
        )
        res_normal = await session.execute(stmt_normal)
        assert res_normal.scalars().first() is None

        # 2. Retain reviewable state: Ensure it is persisted in EPROSubmissionDefeated
        stmt_defeated = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_ghost"
        )
        res_defeated = await session.execute(stmt_defeated)
        defeated = res_defeated.scalars().first()
        assert defeated is not None
        assert defeated.answers == {"pain": 10}
        assert defeated.status == "Defeated by online-merge conflict resolution"

        # 3. Create an OPEN clinical query
        stmt_query = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "subject_ghost"
        )
        res_query = await session.execute(stmt_query)
        query = res_query.scalars().first()
        assert query is not None
        assert query.status == "OPEN"
        assert "missing or deleted" in query.explanation

        # 4. Audit trail with reason 'SYSTEM SYNC EXCEPTION TRIGGERED'
        stmt_audit = select(InteropAuditLog).where(
            InteropAuditLog.action == "EPRO_STRUCTURAL_CONFLICT"
        )
        res_audit = await session.execute(stmt_audit)
        audit_entry = res_audit.scalars().first()
        assert audit_entry is not None
        assert audit_entry.change_reason == "SYSTEM SYNC EXCEPTION TRIGGERED"
        assert "missing or deleted" in audit_entry.details


@pytest.mark.asyncio
async def test_submit_with_valid_signature():
    """
    Test submitting an ePRO diary with a valid cryptographic signature.
    The response should propagate the valid signature validation status and reconciliation result.
    """
    # @req:PRD-EDC-008
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        inst = Instrument(
            id="diary_signed",
            name="Signed Diary",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Setup signed tracker",
            version_index=1,
        )
        session.add(inst)

        now = datetime.now(timezone.utc).replace(tzinfo=None)
        assign = SubjectAssignment(
            subject_id="subject_signed",
            instrument_id="diary_signed",
            start_date=now - timedelta(days=2),
            end_date=now + timedelta(days=2),
            created_by="admin",
            reason_for_change="Setup assignment",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    # Construct incoming record payload to sign
    subject_id = "subject_signed"
    diary_id = "diary_signed"
    client_id = "dev_secure"
    answers = {"pain": 3}
    device_timestamp = datetime.now(timezone.utc)

    # In main.py, timestamps default to device_timestamp for each key
    timestamps = {"pain": device_timestamp}

    incoming_record = SyncRecord(
        deduplication_key=f"{subject_id}:{diary_id}",
        data=answers,
        metadata=SyncMetadata(
            timestamps=timestamps,
            modified_by=client_id,
        ),
    )

    secret_bytes = b"internal-gateway-secret-12345"
    sig_payload = get_signature_payload(incoming_record)
    sig = generate_canonical_signature(sig_payload, secret_bytes)

    client = TestClient(app)
    headers = get_auth_headers(
        roles="Subject", change_reason="Signed submit", user_id="subject_signed"
    )

    payload = {
        "subject_id": subject_id,
        "diary_id": diary_id,
        "device_timestamp": device_timestamp.isoformat(),
        "answers": answers,
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": client_id,
            "conflict_strategy": "CLIENT_WINS",
            "signature": sig,
            "timestamps": {"pain": device_timestamp.isoformat()},
        },
    }

    resp = client.post("/api/v1/interop/epro/submit", json=payload, headers=headers)
    assert resp.status_code == 201
    res_data = resp.json()
    assert res_data["status"] == "CREATED"
    assert res_data["signature_validation"]["status"] == "VALID"
    assert res_data["reconciliation_result"]["status"] == "CREATED"


@pytest.mark.asyncio
async def test_submit_with_invalid_signature_fails():
    """
    Test submitting an ePRO diary with an invalid cryptographic signature.
    Should raise 400 Bad Request.
    """
    # @req:PRD-EDC-008
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        inst = Instrument(
            id="diary_signed_err",
            name="Signed Diary Err",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Setup signed tracker",
            version_index=1,
        )
        session.add(inst)

        now = datetime.now(timezone.utc).replace(tzinfo=None)
        assign = SubjectAssignment(
            subject_id="subject_signed_err",
            instrument_id="diary_signed_err",
            start_date=now - timedelta(days=2),
            end_date=now + timedelta(days=2),
            created_by="admin",
            reason_for_change="Setup assignment",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    client = TestClient(app)
    headers = get_auth_headers(
        roles="Subject",
        change_reason="Invalid signature submit",
        user_id="subject_signed_err",
    )

    payload = {
        "subject_id": "subject_signed_err",
        "diary_id": "diary_signed_err",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 3},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_secure",
            "conflict_strategy": "CLIENT_WINS",
            "signature": "invalid_sig_12345",
        },
    }

    resp = client.post("/api/v1/interop/epro/submit", json=payload, headers=headers)
    assert resp.status_code == 400
    assert "Invalid signature" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_bulk_sync_with_valid_signatures_and_tallies():
    """
    Test that bulk sync processes multiple signed submissions correctly,
    verifying tally counts (CREATED, UPDATED_CLIENT_WINS, MERGED, IGNORED_SERVER_WINS, STRUCTURAL_CONFLICT)
    and ensuring responses propagate details.
    """
    # @req:PRD-EDC-007
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        # Instrument exists
        inst = Instrument(
            id="bulk_diary",
            name="Bulk Signed Diary",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Setup bulk tracker",
            version_index=1,
        )
        session.add(inst)

        now = datetime.now(timezone.utc).replace(tzinfo=None)
        # Assignment exists for subject_bulk_1 but NOT subject_bulk_ghost
        assign = SubjectAssignment(
            subject_id="subject_bulk_1",
            instrument_id="bulk_diary",
            start_date=now - timedelta(days=2),
            end_date=now + timedelta(days=2),
            created_by="admin",
            reason_for_change="Setup assignment",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    # We will submit three entries:
    # 1. subject_bulk_1: bulk_diary - valid signature (CREATED)
    # 2. subject_bulk_ghost: bulk_diary - no signature (STRUCTURAL_CONFLICT)
    # 3. subject_bulk_1: bulk_diary - valid signature (UPDATED_CLIENT_WINS)

    secret_bytes = b"internal-gateway-secret-12345"
    device_timestamp = datetime.now(timezone.utc)

    # Entry 1
    incoming_1 = SyncRecord(
        deduplication_key="subject_bulk_1:bulk_diary",
        data={"pain": 2},
        metadata=SyncMetadata(
            timestamps={"pain": device_timestamp},
            modified_by="dev_bulk",
        ),
    )
    sig_1 = generate_canonical_signature(
        get_signature_payload(incoming_1), secret_bytes
    )

    # Entry 3
    incoming_3 = SyncRecord(
        deduplication_key="subject_bulk_1:bulk_diary",
        data={"pain": 6},
        metadata=SyncMetadata(
            timestamps={"pain": device_timestamp + timedelta(seconds=10)},
            modified_by="dev_bulk",
        ),
    )
    sig_3 = generate_canonical_signature(
        get_signature_payload(incoming_3), secret_bytes
    )

    bulk_payload = {
        "submissions": [
            {
                "subject_id": "subject_bulk_1",
                "diary_id": "bulk_diary",
                "device_timestamp": device_timestamp.isoformat(),
                "answers": {"pain": 2},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "dev_bulk",
                    "conflict_strategy": "CLIENT_WINS",
                    "signature": sig_1,
                },
            },
            {
                "subject_id": "subject_bulk_ghost",
                "diary_id": "bulk_diary",
                "device_timestamp": device_timestamp.isoformat(),
                "answers": {"pain": 5},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "dev_bulk",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
            {
                "subject_id": "subject_bulk_1",
                "diary_id": "bulk_diary",
                "device_timestamp": (
                    device_timestamp + timedelta(seconds=10)
                ).isoformat(),
                "answers": {"pain": 6},
                "offline_sync_markers": {
                    "sequence_number": 2,
                    "client_id": "dev_bulk",
                    "conflict_strategy": "CLIENT_WINS",
                    "signature": sig_3,
                    "timestamps": {
                        "pain": (device_timestamp + timedelta(seconds=10)).isoformat()
                    },
                },
            },
        ]
    }

    client = TestClient(app)
    # Since Bob cannot sync Alice's bulk sync, we must use staff/admin or match Bob's own bulk identity.
    # Let's use admin headers to bypass the verification check
    headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Bulk sync regression test"
    )

    resp = client.post("/api/v1/interop/epro/sync", json=bulk_payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()

    assert data["processed_count"] == 3
    assert data["created_count"] == 1
    assert data["updated_count"] == 1
    assert data["conflict_count"] == 1  # structural conflict
    assert data["ignored_count"] == 0

    results = data["results"]
    assert results[0]["status"] == "CREATED"
    assert results[0]["signature_validation"]["status"] == "VALID"

    assert results[1]["status"] == "STRUCTURAL_CONFLICT"
    assert results[1]["signature_validation"]["status"] == "SKIPPED"

    assert results[2]["status"] == "UPDATED_CLIENT_WINS"
    assert results[2]["signature_validation"]["status"] == "VALID"
