import time
from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.gateway.main import generate_signature
from apps.interop.database import db_manager
from apps.interop.main import app
from apps.interop.models import (
    Base,
    EPROSubmission,
    EPROSubmissionDefeated,
    EPROSubmissionQuarantine,
    Instrument,
    InteropAuditLog,
    SubjectAssignment,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory Interop database for unit and integration testing.
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
async def test_epro_quarantine_sync_pipeline():
    """
    Test the split-queue quarantine and triage pipeline.
    1. A mixture of valid and invalid submissions are synced.
    2. Valid submissions process successfully, while invalid ones are quarantined on server.
    3. The server response returns 'QUARANTINED' status so local queue won't block.
    4. Quarantined records can be queried, edited, and replayed with e-signature verification.
    """
    # Set up Instrument and Assignment to prevent structural conflicts
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        inst = Instrument(
            id="daily_diary",
            name="Daily Diary",
            items={},
            response_types={},
            scoring_metadata={},
            created_by="admin",
            reason_for_change="Setup",
            version_index=1,
        )
        session.add(inst)

        now = datetime.now(UTC).replace(tzinfo=None)
        assign = SubjectAssignment(
            subject_id="sub_alice",
            instrument_id="daily_diary",
            start_date=now - timedelta(days=1),
            end_date=now + timedelta(days=1),
            created_by="admin",
            reason_for_change="Setup",
            version_index=1,
        )
        session.add(assign)
        await session.commit()

    client = TestClient(app)

    # Bulk sync payload with one valid and one validation-failing submission (bad age)
    bulk_payload = {
        "submissions": [
            {
                "subject_id": "sub_alice",
                "diary_id": "daily_diary",
                "device_timestamp": "2026-08-04T12:00:00Z",
                "answers": {"pain_score": 5, "age": 25, "gender": "F"},
                "offline_sync_markers": {
                    "sequence_number": 1,
                    "client_id": "device_1",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
            {
                "subject_id": "sub_alice",
                "diary_id": "daily_diary",
                "device_timestamp": "2026-08-04T12:05:00Z",
                "answers": {
                    "pain_score": 12,
                    "age": 15,
                    "gender": "X",
                },  # fails pain_score (12), age (15), and gender (X)
                "offline_sync_markers": {
                    "sequence_number": 2,
                    "device_timestamp": "2026-08-04T12:05:00Z",
                    "client_id": "device_1",
                    "conflict_strategy": "CLIENT_WINS",
                },
            },
        ]
    }

    # Post sync request
    headers = get_auth_headers(
        roles="staff", change_reason="Bulk offline sync", user_id="sub_alice"
    )
    resp = client.post("/api/v1/interop/epro/sync", json=bulk_payload, headers=headers)
    assert resp.status_code == 200
    data = resp.json()
    assert data["processed_count"] == 2
    assert data["created_count"] == 1
    assert data["quarantine_count"] == 1

    # Verify result list statuses
    results = data["results"]
    assert results[0]["status"] == "CREATED"
    assert results[1]["status"] == "QUARANTINED"
    assert "Demographic Validation Error" in results[1]["validation_errors"][0]

    # Verify that the database has one EPROSubmission and one EPROSubmissionQuarantine entry
    async with async_session() as session:
        sub_stmt = select(EPROSubmission).where(
            EPROSubmission.subject_id == "sub_alice"
        )
        subs = (await session.execute(sub_stmt)).scalars().all()
        assert len(subs) == 1
        assert subs[0].answers["age"] == 25

        quar_stmt = select(EPROSubmissionQuarantine).where(
            EPROSubmissionQuarantine.subject_id == "sub_alice"
        )
        quars = (await session.execute(quar_stmt)).scalars().all()
        assert len(quars) == 1
        quar_id = quars[0].id
        assert quars[0].status == "QUARANTINED"
        assert (
            len(quars[0].validation_errors) == 3
        )  # pain_score, age, and gender errors

    # Check administrative permission guardrail: Subjects cannot access quarantine endpoints
    subject_headers = get_auth_headers(roles="subject", user_id="sub_alice")
    q_resp = client.get("/api/v1/interop/epro/quarantine", headers=subject_headers)
    assert q_resp.status_code == 403

    # Trial manager can list quarantined submissions
    manager_headers = get_auth_headers(roles="trial_manager", user_id="manager_bob")
    q_resp = client.get("/api/v1/interop/epro/quarantine", headers=manager_headers)
    assert q_resp.status_code == 200
    q_data = q_resp.json()
    assert len(q_data) == 1
    assert q_data[0]["id"] == quar_id

    # Retrieve single record by ID
    single_resp = client.get(
        f"/api/v1/interop/epro/quarantine/{quar_id}", headers=manager_headers
    )
    assert single_resp.status_code == 200
    assert single_resp.json()["subject_id"] == "sub_alice"

    # Try editing quarantined submission with WRONG password
    edit_payload_wrong_pwd = {
        "answers": {"pain_score": 4, "age": 30, "gender": "F"},
        "password": "wrong_password",  # pragma: allowlist secret
        "change_reason": "Correcting participant age and pain score",
    }
    manager_headers_mutation = get_auth_headers(
        roles="trial_manager",
        change_reason="Triage quarantined submission",
        user_id="manager_bob",
    )
    edit_resp = client.post(
        f"/api/v1/interop/epro/quarantine/{quar_id}/edit",
        json=edit_payload_wrong_pwd,
        headers=manager_headers_mutation,
    )
    assert edit_resp.status_code == 400
    assert edit_resp.json()["detail"] == "Invalid credentials for e-signature"

    # Verify that a failed edit logs a signature failure audit entry
    async with async_session() as session:
        audit_stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "EPRO_EDIT_SIGNATURE_FAILED"
        )
        audit_logs = (await session.execute(audit_stmt)).scalars().all()
        assert len(audit_logs) >= 1

    # Edit quarantined submission with CORRECT password
    edit_payload_correct = {
        "answers": {"pain_score": 4, "age": 30, "gender": "F"},
        "password": "valid_password",  # pragma: allowlist secret
        "change_reason": "Correcting participant age and pain score",
    }
    edit_resp = client.post(
        f"/api/v1/interop/epro/quarantine/{quar_id}/edit",
        json=edit_payload_correct,
        headers=manager_headers_mutation,
    )
    assert edit_resp.status_code == 200
    edited_data = edit_resp.json()
    assert edited_data["answers"]["age"] == 30
    assert (
        len(edited_data["validation_errors"]) == 0
    )  # Should be cleared since values are now valid!

    # Verify triage history update
    assert len(edited_data["triage_history"]) == 2  # QUARANTINED + EDIT
    assert edited_data["triage_history"][1]["action"] == "EDIT"

    # Try replaying quarantined submission with WRONG password
    replay_payload_wrong_pwd = {
        "password": "wrong_password",  # pragma: allowlist secret
        "change_reason": "Replaying corrected diary",
    }
    replay_resp = client.post(
        f"/api/v1/interop/epro/quarantine/{quar_id}/replay",
        json=replay_payload_wrong_pwd,
        headers=manager_headers_mutation,
    )
    assert replay_resp.status_code == 400
    assert replay_resp.json()["detail"] == "Invalid credentials for e-signature"

    # Replay with CORRECT password
    replay_payload_correct = {
        "password": "valid_password",  # pragma: allowlist secret
        "change_reason": "Replaying corrected diary",
    }
    replay_resp = client.post(
        f"/api/v1/interop/epro/quarantine/{quar_id}/replay",
        json=replay_payload_correct,
        headers=manager_headers_mutation,
    )
    assert replay_resp.status_code == 200
    replay_data = replay_resp.json()
    assert replay_data["status"] == "success"

    # Verify that the record is now REPLAYED and original raw remains preserved and archived
    async with async_session() as session:
        # Check quarantine status is updated
        q_record = (
            (
                await session.execute(
                    select(EPROSubmissionQuarantine).where(
                        EPROSubmissionQuarantine.id == quar_id
                    )
                )
            )
            .scalars()
            .first()
        )
        assert q_record.status == "REPLAYED"
        assert q_record.original_answers["age"] == 15  # Original raw remains immutable!

        # Check raw archive in EPROSubmissionDefeated
        defeated = (
            (
                await session.execute(
                    select(EPROSubmissionDefeated).where(
                        EPROSubmissionDefeated.subject_id == "sub_alice"
                    )
                )
            )
            .scalars()
            .all()
        )
        assert len(defeated) == 2
        # One of them has original raw answers with age 15
        assert any(d.answers["age"] == 15 for d in defeated)

        # Check newly active EPROSubmission contains replayed/corrected answers
        subs_active = (
            (
                await session.execute(
                    select(EPROSubmission).where(
                        EPROSubmission.subject_id == "sub_alice"
                    )
                )
            )
            .scalars()
            .all()
        )
        assert len(subs_active) >= 1

        # Check interop audit logs for EPRO_QUARANTINE_REPLAYED
        replay_audit = (
            (
                await session.execute(
                    select(InteropAuditLog).where(
                        InteropAuditLog.action == "EPRO_QUARANTINE_REPLAYED"
                    )
                )
            )
            .scalars()
            .first()
        )
        assert replay_audit is not None
