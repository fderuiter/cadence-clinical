import hashlib
import hmac
import json
import time

import httpx
import pytest
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalSubject,
    MedDRATerm,
)
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id: str = "coder_bob",
    roles: str = "Data Manager",
    change_reason: str = "Clinical coding review",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
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


@pytest.fixture(autouse=True)
async def setup_test_db():
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    await db_manager.close()


async def seed_data():
    """Seed minimum required database records for system coding query tests."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Seed MedDRA Headache so there is at least one dictionary version "26.0" term
            session.add(
                MedDRATerm(
                    dictionary_version="26.0",
                    code="10019211",
                    term_name="Headache",
                    level="LLT",
                )
            )
            # Seed a Clinical Subject
            session.add(
                ClinicalSubject(
                    id="SUBJ-UUID-1",
                    subject_id="SUBJ-001",
                    study_id="STUDY-001",
                )
            )


@pytest.mark.asyncio
async def test_uncodable_term_creates_query_pending_and_actionable_query():
    """Verify that a below-threshold/uncodable term creates a QUERY_PENDING assignment and a SYSTEM_CODING ClinicalQuery."""
    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Post uncodable AE observation (verbatim is complete gibberish)
        resp = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "xyz_completely_uncodable_random_word",
            },
            headers=get_auth_headers(),
        )
        assert resp.status_code == 200

        # 2. Retrieve the coding assignment and verify status is QUERY_PENDING
        resp_list = await client.get(
            "/api/v1/execution/coding/assignments",
            headers=get_auth_headers(),
        )
        assert resp_list.status_code == 200
        assignments = resp_list.json()
        assert len(assignments) == 1
        assign = assignments[0]
        assert assign["status"] == "QUERY_PENDING"
        assert assign["coded_code"] is None
        assert assign["coded_term"] is None

        # 3. Retrieve clinical queries and verify that one SYSTEM_CODING ClinicalQuery was created
        resp_queries = await client.get(
            "/api/v1/execution/queries",
            headers=get_auth_headers(),
        )
        assert resp_queries.status_code == 200
        queries = resp_queries.json()
        assert len(queries) == 1
        q = queries[0]
        assert q["status"] == "OPEN"
        assert q["origin"] == "SYSTEM_CODING"
        assert q["query_type"] == "SYSTEM_CODING"
        assert q["action_required"] == "RE-ENTER_VERBATIM"
        assert q["form_id"] == "AE_FORM"
        assert q["field_id"] == "AETERM"
        assert q["observation_id"] == assign["observation_id"]

        # Verify that query content identifies field without exposing unrelated subject demographics/data
        assert "xyz_completely_uncodable_random_word" in q["explanation"]
        assert "field AETERM" in q["explanation"]
        # Ensure name or PII are not leaked
        assert "SUBJ-001" not in q["explanation"]


@pytest.mark.asyncio
async def test_uncodable_term_query_creation_is_idempotent():
    """Verify that reprocessing/posting the same uncodable term for the same coordinates does not duplicate open queries."""
    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Post first time
        resp1 = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "gibberish_term_abc",
            },
            headers=get_auth_headers(),
        )
        assert resp1.status_code == 200

        # Post second time (reprocessing/simulating duplicate entry)
        resp2 = await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "gibberish_term_abc",
            },
            headers=get_auth_headers(),
        )
        assert resp2.status_code == 200

        # Verify only ONE ClinicalQuery exists in database
        resp_queries = await client.get(
            "/api/v1/execution/queries",
            headers=get_auth_headers(),
        )
        assert resp_queries.status_code == 200
        queries = resp_queries.json()
        assert len(queries) == 1


@pytest.mark.asyncio
async def test_events_captured_in_part_11_audit_history():
    """Verify that assignment and query creations and mutations are captured in AuditLog."""
    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create uncodable term
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "bad_verbatim_123",
            },
            headers=get_auth_headers(
                user_id="user_audited_create", change_reason="initial observation entry"
            ),
        )

        async with db_manager.get_session_maker()() as session:
            # Check ClinicalCodingAssignment logs
            res_assign = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "clinical_coding_assignments"
                )
            )
            assign_logs = res_assign.scalars().all()
            assert len(assign_logs) >= 1
            assert assign_logs[0].action == "INSERT"
            assert assign_logs[0].new_values["status"] == "QUERY_PENDING"

            # Check ClinicalQuery logs
            res_query = await session.execute(
                select(AuditLog).where(AuditLog.table_name == "clinical_queries")
            )
            query_logs = res_query.scalars().all()
            assert len(query_logs) >= 1
            assert query_logs[0].action == "INSERT"
            assert query_logs[0].new_values["origin"] == "SYSTEM_CODING"


@pytest.mark.asyncio
async def test_manual_coding_resolution_associates_with_query_and_closes_it():
    """Verify that a manual override/accept action closes the active SYSTEM_CODING query and records details."""
    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create the query-pending observation and assignment
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
        assign = resp_list.json()[0]
        assign_id = assign["id"]

        # 3. Perform manual override resolution (since gibberish_term_xyz has no suggestions)
        resp_override = await client.post(
            f"/api/v1/execution/coding/assignments/{assign_id}/action",
            json={
                "action": "OVERRIDE",
                "code": "10019211",
                "term": "Headache",
                "reason_for_change": "Manual classification of uncodable symptom",
            },
            headers=get_auth_headers(user_id="manual_coder_alice"),
        )
        assert resp_override.status_code == 200
        assert resp_override.json()["status"] == "CODED"

        # 4. Verify that the original ClinicalQuery is CLOSED and response updated
        resp_queries = await client.get(
            "/api/v1/execution/queries",
            headers=get_auth_headers(),
        )
        assert resp_queries.status_code == 200
        q = resp_queries.json()[0]
        assert q["status"] == "CLOSED"
        assert q["resolver"] == "manual_coder_alice"
        assert (
            "Resolved via manual coding action: OVERRIDE on code 10019211"
            in q["response"]
        )


@pytest.mark.asyncio
async def test_resolving_query_reverts_assignment_to_uncoded():
    """Verify that closing or cancelling a SYSTEM_CODING query reverts the associated assignment status to UNCODED."""
    await seed_data()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create the query-pending observation, assignment, and query
        await client.post(
            "/api/v1/execution/observations",
            json={
                "subject_id": "SUBJ-001",
                "study_id": "STUDY-001",
                "domain": "AE",
                "test_code": "AETERM",
                "test_name": "Adverse Event Verbatim",
                "value_string": "term_to_revert",
            },
            headers=get_auth_headers(),
        )

        # 2. Retrieve query and assignment IDs
        resp_q = await client.get(
            "/api/v1/execution/queries", headers=get_auth_headers()
        )
        q_id = resp_q.json()[0]["id"]

        resp_list = await client.get(
            "/api/v1/execution/coding/assignments", headers=get_auth_headers()
        )
        assign = resp_list.json()[0]
        assert assign["status"] == "QUERY_PENDING"

        # 3. Cancel the query
        resp_cancel = await client.post(
            f"/api/v1/execution/queries/{q_id}/cancel",
            json={"reason": "Cancelled by data manager"},
            headers=get_auth_headers(),
        )
        assert resp_cancel.status_code == 200
        assert resp_cancel.json()["status"] == "CANCELLED"

        # 4. Verify that the assignment's status is reverted to UNCODED
        resp_list_after = await client.get(
            "/api/v1/execution/coding/assignments", headers=get_auth_headers()
        )
        assign_after = resp_list_after.json()[0]
        assert assign_after["status"] == "UNCODED"
