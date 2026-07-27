# @req:PRD-ECOA-002 - eCOA backend contract and regression test suite
# This file provides cross-cutting automated coverage for the eCOA backend,
# following existing repository testing conventions.
# It covers subject-only authorization, cross-subject 403 rejection, instrument retrieval,
# assignment/compliance states, submission conflict resolution (including structural conflicts),
# notifications, and audit-relevant mutation behaviors.
#
# Note on intentionally deferred integration/provider behavior:
# - SMTP/Email delivery is stubbed (no real servers are connected during testing).
# - Identity-provider authentication (Keycloak OIDC) is simulated using Gateway v2 signed headers,
#   avoiding dependencies on external IAM networks.

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
    InteropAuditLog,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_ecoa_db():
    """
    Setup in-memory Interop database for eCOA contract and regression testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "Subject",
    change_reason: str = "eCOA Operation",
    user_id: str = "subject_001",
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
async def test_subject_only_authorization_and_cross_subject_rejection():
    """
    Verify subject-only authorization and cross-subject 403 rejection.
    - Authenticated Subject can access and submit their own ePRO record.
    - Subject cannot access or mutate records of another subject (cross-subject 403).
    - Subject cannot access staff-only endpoints (e.g., creating instruments or assignments).
    - Staff members can access all endpoints.
    """
    client = TestClient(app)

    # 1. Staff setup: Create an instrument and assign it to subject_alice
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff Setup", user_id="staff_user"
    )

    inst_payload = {
        "name": "Daily Pain Scale",
        "description": "Pain scale tracker.",
        "items": {"pain_level": "Pain level rating"},
        "response_types": {"pain_level": {"type": "numeric", "min": 0, "max": 10}},
        "scoring_metadata": {},
        "reason_for_change": "Initial authoring",
    }
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=staff_headers
    )
    assert resp.status_code == 201
    inst_id = resp.json()["id"]

    now = datetime.now(timezone.utc).replace(tzinfo=None)
    assign_payload = {
        "subject_id": "subject_alice",
        "instrument_id": inst_id,
        "start_date": (now - timedelta(days=1)).isoformat(),
        "end_date": (now + timedelta(days=1)).isoformat(),
        "recurrence_pattern": "DAILY",
        "reason_for_change": "Assigning to Alice",
    }
    resp = client.post(
        "/api/v1/interop/assignments", json=assign_payload, headers=staff_headers
    )
    assert resp.status_code == 201

    # 2. Subject alice retrieving her own assignments -> 200 OK
    alice_headers = get_auth_headers(
        roles="Subject", change_reason="Alice login", user_id="subject_alice"
    )
    resp = client.get(
        "/api/v1/interop/assignments/subject/subject_alice", headers=alice_headers
    )
    assert resp.status_code == 200
    assert len(resp.json()) == 1

    # 3. Cross-Subject Boundary Check: Subject bob trying to retrieve Alice's assignments -> 403 Forbidden
    bob_headers = get_auth_headers(
        roles="Subject", change_reason="Bob login", user_id="subject_bob"
    )
    resp = client.get(
        "/api/v1/interop/assignments/subject/subject_alice", headers=bob_headers
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # 4. Subject Alice submitting her own ePRO response -> 201 Created
    sub_payload = {
        "subject_id": "subject_alice",
        "diary_id": inst_id,
        "device_timestamp": now.isoformat(),
        "answers": {"pain_level": 5},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "device_alice",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload, headers=alice_headers
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"

    # 5. Cross-Subject Boundary Check: Subject Bob trying to submit ePRO for Alice -> 403 Forbidden
    resp = client.post(
        "/api/v1/interop/epro/submit", json=sub_payload, headers=bob_headers
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # 6. Cross-Subject Boundary Check: Subject Bob trying to bulk sync for Alice -> 403 Forbidden
    bulk_payload = {"submissions": [sub_payload]}
    resp = client.post(
        "/api/v1/interop/epro/sync", json=bulk_payload, headers=bob_headers
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # 7. Subject Alice trying to author an instrument -> 403 Forbidden
    resp = client.post(
        "/api/v1/interop/instruments", json=inst_payload, headers=alice_headers
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_instrument_retrieval_and_assignment_boundaries():
    """
    Verify instrument retrieval boundaries.
    - Assigned subjects can retrieve the instrument definition.
    - Unassigned subjects are blocked from retrieving the instrument definition (403).
    - Staff members can retrieve any instrument definition.
    """
    client = TestClient(app)
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff Setup", user_id="staff_user"
    )

    # Create two instruments
    inst_1 = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Instrument One",
            "items": {"q": "one"},
            "response_types": {"q": {"type": "text"}},
            "scoring_metadata": {},
            "reason_for_change": "First instrument",
        },
        headers=staff_headers,
    ).json()

    inst_2 = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Instrument Two",
            "items": {"q": "two"},
            "response_types": {"q": {"type": "text"}},
            "scoring_metadata": {},
            "reason_for_change": "Second instrument",
        },
        headers=staff_headers,
    ).json()

    # Assign inst_1 to subject_alice only
    now = datetime.now(timezone.utc).replace(tzinfo=None)
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "subject_alice",
            "instrument_id": inst_1["id"],
            "start_date": now.isoformat(),
            "end_date": (now + timedelta(days=1)).isoformat(),
            "reason_for_change": "Assigning inst_1 to Alice",
        },
        headers=staff_headers,
    )

    # Alice headers
    alice_headers = get_auth_headers(
        roles="Subject", user_id="subject_alice", change_reason="Alice login"
    )

    # 1. Alice can retrieve inst_1 (assigned) -> 200 OK
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_1['id']}", headers=alice_headers
    )
    assert resp.status_code == 200
    assert resp.json()["name"] == "Instrument One"

    # 2. Alice cannot retrieve inst_2 (unassigned) -> 403 Forbidden
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_2['id']}", headers=alice_headers
    )
    assert resp.status_code == 403
    assert "Access denied" in resp.json()["detail"]

    # 3. Staff can retrieve both inst_1 and inst_2 -> 200 OK
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_1['id']}", headers=staff_headers
    )
    assert resp.status_code == 200
    resp = client.get(
        f"/api/v1/interop/instruments/{inst_2['id']}", headers=staff_headers
    )
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_assignment_compliance_states_and_recalculations():
    """
    Verify assignment due states (PENDING, OVERDUE, COMPLETED) and compliance rate calculations.
    - An assignment in the future is PENDING.
    - An assignment in the past is OVERDUE.
    - Submitting a diary response matches with assignments chronologically, transitioning them to COMPLETED.
    - Check compliance metrics update correctly after submission.
    """
    client = TestClient(app)
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff Setup", user_id="staff_user"
    )

    # Create Instrument
    inst = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Compliance Survey",
            "items": {"f1": "f1"},
            "response_types": {"f1": {"type": "text"}},
            "scoring_metadata": {},
            "reason_for_change": "Compliance setup",
        },
        headers=staff_headers,
    ).json()

    now = datetime.now(timezone.utc).replace(tzinfo=None)

    # 1. Create one overdue assignment (due_at in the past)
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "subject_comp",
            "instrument_id": inst["id"],
            "start_date": (now - timedelta(days=2)).isoformat(),
            "end_date": (now - timedelta(hours=12)).isoformat(),
            "due_at": (now - timedelta(hours=12)).isoformat(),
            "reason_for_change": "Overdue assignment",
        },
        headers=staff_headers,
    )

    # 2. Create one pending assignment (due_at in the future)
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "subject_comp",
            "instrument_id": inst["id"],
            "start_date": (now - timedelta(hours=1)).isoformat(),
            "end_date": (now + timedelta(days=2)).isoformat(),
            "due_at": (now + timedelta(days=1)).isoformat(),
            "reason_for_change": "Pending assignment",
        },
        headers=staff_headers,
    )

    # Get compliance metrics before submission
    comp_headers = get_auth_headers(
        roles="Subject", user_id="subject_comp", change_reason="Get compliance"
    )
    resp = client.get(
        "/api/v1/interop/subjects/subject_comp/compliance", headers=comp_headers
    )
    assert resp.status_code == 200
    metrics = resp.json()
    assert metrics["completed_count"] == 0
    assert metrics["overdue_count"] == 1
    assert metrics["pending_count"] == 1
    assert metrics["compliance_rate"] == 0.0

    # Submit an ePRO response
    sub_payload = {
        "subject_id": "subject_comp",
        "diary_id": inst["id"],
        "device_timestamp": now.isoformat(),
        "answers": {"f1": "first response"},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_comp",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    client.post("/api/v1/interop/epro/submit", json=sub_payload, headers=comp_headers)

    # Check compliance metrics after submission
    resp = client.get(
        "/api/v1/interop/subjects/subject_comp/compliance", headers=comp_headers
    )
    metrics_post = resp.json()
    # First assignment should be matched and COMPLETED, second should still be PENDING
    assert metrics_post["completed_count"] == 1
    assert metrics_post["overdue_count"] == 0
    assert metrics_post["pending_count"] == 1
    assert metrics_post["compliance_rate"] == 50.0


@pytest.mark.asyncio
async def test_offline_submission_conflict_resolution_lifecycles():
    """
    Verify ePRO offline queue reconciliation and normal sync conflict strategies.
    Specifically tests:
    - CLIENT_WINS: incoming overwrites existing. Defeated is saved.
    - SERVER_WINS: existing is preserved. Defeated is saved.
    - MERGE: answers are combined. Defeated is saved.
    - All outcomes log detailed audit records with version increments.
    """
    client = TestClient(app)
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Staff Setup", user_id="staff_user"
    )

    inst = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Symptom Tracker",
            "items": {"symptom": "Symptom description", "severity": "Severity rating"},
            "response_types": {
                "symptom": {"type": "text"},
                "severity": {"type": "text"},
            },
            "scoring_metadata": {},
            "reason_for_change": "Initial authoring",
        },
        headers=staff_headers,
    ).json()

    now = datetime.now(timezone.utc).replace(tzinfo=None)
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "subject_sync",
            "instrument_id": inst["id"],
            "start_date": (now - timedelta(days=1)).isoformat(),
            "end_date": (now + timedelta(days=1)).isoformat(),
            "reason_for_change": "Initial setup",
        },
        headers=staff_headers,
    )

    headers = get_auth_headers(
        roles="Subject", user_id="subject_sync", change_reason="Sync operation"
    )

    # 1. Initial creation
    sub_1 = {
        "subject_id": "subject_sync",
        "diary_id": inst["id"],
        "device_timestamp": now.isoformat(),
        "answers": {"symptom": "Headache", "severity": "Mild"},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_sync",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post("/api/v1/interop/epro/submit", json=sub_1, headers=headers)
    assert resp.status_code == 201
    assert resp.json()["status"] == "CREATED"
    assert resp.json()["version_index"] == 1

    # 2. CLIENT_WINS conflict
    sub_2 = {
        "subject_id": "subject_sync",
        "diary_id": inst["id"],
        "device_timestamp": (now + timedelta(seconds=10)).isoformat(),
        "answers": {"symptom": "Headache", "severity": "Severe"},
        "offline_sync_markers": {
            "sequence_number": 2,
            "client_id": "dev_sync",
            "conflict_strategy": "CLIENT_WINS",
        },
    }
    resp = client.post("/api/v1/interop/epro/submit", json=sub_2, headers=headers)
    assert resp.status_code == 201
    assert resp.json()["status"] == "UPDATED_CLIENT_WINS"
    assert resp.json()["answers"]["severity"] == "Severe"
    assert resp.json()["version_index"] == 2

    # Verify defeated table contains the overwritten mild record
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        stmt = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_sync"
        )
        res = await session.execute(stmt)
        defeated_list = res.scalars().all()
        assert len(defeated_list) == 1
        assert defeated_list[0].answers["severity"] == "Mild"
        assert defeated_list[0].status == "Defeated by online-merge conflict resolution"

    # 3. SERVER_WINS conflict
    sub_3 = {
        "subject_id": "subject_sync",
        "diary_id": inst["id"],
        "device_timestamp": (now + timedelta(seconds=20)).isoformat(),
        "answers": {"symptom": "Migraine", "severity": "Critical"},
        "offline_sync_markers": {
            "sequence_number": 3,
            "client_id": "dev_sync",
            "conflict_strategy": "SERVER_WINS",
        },
    }
    resp = client.post("/api/v1/interop/epro/submit", json=sub_3, headers=headers)
    assert resp.status_code == 201
    assert resp.json()["status"] == "IGNORED_SERVER_WINS"
    # Answers should still represent the server's value (severity: Severe)
    assert resp.json()["answers"]["severity"] == "Severe"

    async with async_session() as session:
        stmt = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_sync"
        )
        res = await session.execute(stmt)
        defeated_list = res.scalars().all()
        assert len(defeated_list) == 2
        # Check that Critical got archived/defeated
        severities = [r.answers["severity"] for r in defeated_list]
        assert "Critical" in severities

    # 4. MERGE conflict
    sub_4 = {
        "subject_id": "subject_sync",
        "diary_id": inst["id"],
        "device_timestamp": (now + timedelta(seconds=30)).isoformat(),
        "answers": {"nausea": "Moderate"},
        "offline_sync_markers": {
            "sequence_number": 4,
            "client_id": "dev_sync",
            "conflict_strategy": "MERGE",
        },
    }
    resp = client.post("/api/v1/interop/epro/submit", json=sub_4, headers=headers)
    assert resp.status_code == 201
    assert resp.json()["status"] == "MERGED"
    assert resp.json()["answers"]["severity"] == "Severe"
    assert resp.json()["answers"]["nausea"] == "Moderate"
    assert resp.json()["version_index"] == 3

    # Check final EPRO audit logs
    async with async_session() as session:
        stmt = (
            select(InteropAuditLog)
            .where(
                InteropAuditLog.action == "EPRO_RECONCILE",
                InteropAuditLog.user_id == "subject_sync",
            )
        )
        res = await session.execute(stmt)
        audits = res.scalars().all()
        assert len(audits) >= 3
        details_list = [a.details for a in audits]
        assert any("Decision: CREATED." in d for d in details_list)
        assert any("Decision: CLIENT_WINS." in d for d in details_list)
        assert any("Decision: SERVER_WINS." in d for d in details_list)
        assert any("Decision: MERGE." in d for d in details_list)


@pytest.mark.asyncio
async def test_structural_conflict_on_missing_or_deleted_targets():
    """
    Verify structural conflict detection and resolution.
    - When an ePRO is submitted against a missing/deleted Instrument or SubjectAssignment:
      1. Reject the EPROSubmission creation/update (no record in epro_submissions).
      2. Save the incoming payload inside EPROSubmissionDefeated for administrative review.
      3. Create an OPEN ClinicalQuery with default study_id='SYSTEM-SYNC' and explanatory message.
      4. Log an EPRO_STRUCTURAL_CONFLICT audit trail entry with 'SYSTEM SYNC EXCEPTION TRIGGERED' change reason.
    """
    client = TestClient(app)
    headers = get_auth_headers(
        roles="Subject", user_id="subject_ghost", change_reason="Ghost submission"
    )

    sub_payload = {
        "subject_id": "subject_ghost",
        "diary_id": "non_existent_diary",
        "device_timestamp": datetime.now(timezone.utc).isoformat(),
        "answers": {"pain": 10},
        "offline_sync_markers": {
            "sequence_number": 1,
            "client_id": "dev_ghost",
            "conflict_strategy": "CLIENT_WINS",
        },
    }

    # Submit
    resp = client.post("/api/v1/interop/epro/submit", json=sub_payload, headers=headers)
    assert resp.status_code == 201
    result = resp.json()
    assert result["status"] == "STRUCTURAL_CONFLICT"
    assert "query" in result
    assert result["query"]["status"] == "OPEN"
    assert result["query"]["subject_id"] == "subject_ghost"
    assert result["query"]["test_code"] == "non_existent_diary"
    assert "missing or deleted" in result["query"]["explanation"]

    # Verify database side-effects
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        # No normal EPROSubmission
        stmt_normal = select(EPROSubmission).where(
            EPROSubmission.subject_id == "subject_ghost"
        )
        res_normal = await session.execute(stmt_normal)
        assert res_normal.scalars().first() is None

        # Defeated submission exists
        stmt_def = select(EPROSubmissionDefeated).where(
            EPROSubmissionDefeated.subject_id == "subject_ghost"
        )
        res_def = await session.execute(stmt_def)
        defeated = res_def.scalars().first()
        assert defeated is not None
        assert defeated.answers == {"pain": 10}
        assert defeated.status == "Defeated by online-merge conflict resolution"

        # Open Clinical Query exists
        stmt_q = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "subject_ghost"
        )
        res_q = await session.execute(stmt_q)
        query = res_q.scalars().first()
        assert query is not None
        assert query.status == "OPEN"
        assert query.study_id == "SYSTEM-SYNC"

        # Audit trail logs have the system sync exception trigger reason
        stmt_audit = select(InteropAuditLog).where(
            InteropAuditLog.action == "EPRO_STRUCTURAL_CONFLICT",
            InteropAuditLog.user_id == "subject_ghost",
        )
        res_audit = await session.execute(stmt_audit)
        audit = res_audit.scalars().first()
        assert audit is not None
        assert audit.change_reason == "SYSTEM SYNC EXCEPTION TRIGGERED"


@pytest.mark.asyncio
async def test_notifications_lifecycle_reminders_and_acknowledgments():
    """
    Verify complete subject reminders & notification lifecycle.
    - Generate reminders for overdue assignments.
    - Fetch notifications (subject-scoped 403 checks).
    - Acknowledge notifications (mutates read state, increments version, logs audit entry).
    - Checks 21 CFR Part 11 compliant change reasons.
    """
    client = TestClient(app)
    staff_headers = get_auth_headers(
        roles="admin,sponsor_dm",
        change_reason="Setup Notifications",
        user_id="staff_user",
    )

    inst = client.post(
        "/api/v1/interop/instruments",
        json={
            "name": "Daily Diary",
            "items": {"s": "s"},
            "response_types": {"s": {"type": "text"}},
            "scoring_metadata": {},
            "reason_for_change": "Survey authoring",
        },
        headers=staff_headers,
    ).json()

    now = datetime.now(timezone.utc).replace(tzinfo=None)

    # Overdue assignment
    client.post(
        "/api/v1/interop/assignments",
        json={
            "subject_id": "subject_notif",
            "instrument_id": inst["id"],
            "start_date": (now - timedelta(days=2)).isoformat(),
            "end_date": (now - timedelta(hours=2)).isoformat(),
            "due_at": (now - timedelta(hours=2)).isoformat(),
            "reason_for_change": "Overdue assign",
        },
        headers=staff_headers,
    )

    # 1. Compute reminders
    resp = client.post(
        "/api/v1/interop/reminders/compute",
        params={"subject_id": "subject_notif"},
        headers=staff_headers,
    )
    assert resp.status_code == 200
    assert resp.json()["created_count"] == 4  # EMAIL, SMS, WEBHOOK, IN_APP

    # 2. Retrieve notifications
    headers_subject = get_auth_headers(
        roles="Subject", user_id="subject_notif", change_reason="Subject operation"
    )
    resp = client.get(
        "/api/v1/interop/subjects/subject_notif/notifications", headers=headers_subject
    )
    assert resp.status_code == 200
    notifications = resp.json()
    assert len(notifications) == 4

    # 3. Acknowledge one notification (is_read=False initially)
    notif = notifications[0]
    assert notif["is_read"] is False
    assert notif["version_index"] == 1

    ack_payload = {"reason_for_change": "Acknowledge test reminder"}
    resp = client.post(
        f"/api/v1/interop/notifications/{notif['id']}/acknowledge",
        json=ack_payload,
        headers=headers_subject,
    )
    assert resp.status_code == 200
    acknowledged = resp.json()
    assert acknowledged["is_read"] is True
    assert acknowledged["read_at"] is not None
    assert acknowledged["version_index"] == 2
    assert acknowledged["reason_for_change"] == "Acknowledge test reminder"

    # 4. Cross-subject boundary: bob cannot acknowledge alice's notification
    headers_bob = get_auth_headers(
        roles="Subject", user_id="subject_bob", change_reason="Bob action"
    )
    resp = client.post(
        f"/api/v1/interop/notifications/{notif['id']}/acknowledge",
        json=ack_payload,
        headers=headers_bob,
    )
    assert resp.status_code == 403

    # 5. Check audit logs
    async_session = db_manager.get_session_maker()
    async with async_session() as session:
        stmt = select(InteropAuditLog).where(
            InteropAuditLog.action == "ACKNOWLEDGE_NOTIFICATION",
            InteropAuditLog.user_id == "subject_notif",
        )
        res = await session.execute(stmt)
        audit = res.scalars().first()
        assert audit is not None
        assert audit.change_reason == "Acknowledge test reminder"
