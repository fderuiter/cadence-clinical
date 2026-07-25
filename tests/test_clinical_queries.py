import hashlib
import hmac
import json
import time
from typing import AsyncGenerator

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalQuery,
)
from apps.execution.main import app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "test_user",
    roles: str = "admin",
    change_reason: str = "test operation",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers.

    Args:
        user_id (str): The unique identifier of the user.
        roles (str): Comma-separated list of user roles.
        change_reason (str): The mandatory signed change reason.

    Returns:
        dict[str, str]: The fully constructed and HMAC-signed header dict.
    """
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


def get_v1_auth_headers(
    user_id: str = "test_user", roles: str = "admin"
) -> dict[str, str]:
    """Generate signature version 1 authentication headers.

    Args:
        user_id (str): Unique identifier of the user.
        roles (str): User roles.

    Returns:
        dict[str, str]: HMAC-signed header dict using version 1.
    """
    timestamp = str(time.time())
    message = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(
        GATEWAY_SECRET.encode(), message.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None, None]:
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        # Create all tables including clinical_queries and audit logs
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_create_clinical_query_success() -> None:
    """Test successfully raising a new clinical query by a Data Manager."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Raise discrepancy query on out-of-range systolic BP",
    )
    payload = {
        "study_id": "STUDY-ABC",
        "subject_id": "SUBJ-101",
        "visit_id": "VISIT-01",
        "domain": "VS",
        "test_code": "SYSBP",
        "explanation": "Systolic blood pressure entry of 210 mmHg seems unrealistically high.",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers
        )

    assert response.status_code == 201
    data = response.json()
    assert data["status"] == "OPEN"
    assert data["explanation"] == payload["explanation"]
    assert data["study_id"] == "STUDY-ABC"
    assert data["subject_id"] == "SUBJ-101"
    assert data["test_code"] == "SYSBP"
    assert len(data["history"]) == 1
    assert data["history"][0]["action"] == "INSERT"
    assert (
        data["history"][0]["change_reason"]
        == "Raise discrepancy query on out-of-range systolic BP"
    )

    # Verify persistent state and loading query details via GET
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        get_resp = await client.get(
            f"/api/v1/execution/queries/{data['id']}", headers=headers
        )
    assert get_resp.status_code == 200
    get_data = get_resp.json()
    assert get_data["status"] == "OPEN"
    assert len(get_data["history"]) == 1


@pytest.mark.asyncio
async def test_create_clinical_query_authorization_failures() -> None:
    """Test role check and signature version boundaries on query creation."""
    # 1. Reject state-changing requests using v1 signature (lacking verified justification)
    headers_v1 = get_v1_auth_headers(user_id="dm_user", roles="Data Manager")
    payload = {
        "study_id": "STUDY-ABC",
        "subject_id": "SUBJ-101",
        "test_code": "SYSBP",
        "explanation": "High pressure.",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers_v1
        )
    assert resp.status_code == 403
    assert (
        "Missing or obsolete signature format. Version 1 or Version 2 signature is required."
        in resp.json()["detail"]
    )

    # 2. Reject state-changing requests from non-authorized roles (e.g. Investigator trying to open query)
    headers_inv = get_v2_auth_headers(
        user_id="inv_user", roles="Investigator", change_reason="Try to open query"
    )
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp2 = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers_inv
        )
    assert resp2.status_code == 403
    assert "User role is not authorized" in resp2.json()["detail"]


@pytest.mark.asyncio
async def test_duplicate_active_query_rejected() -> None:
    """Test that duplicate active queries on the exact same coordinate are rejected."""
    headers = get_v2_auth_headers(roles="Data Manager", change_reason="First raise")
    payload = {
        "study_id": "STUDY-ABC",
        "subject_id": "SUBJ-101",
        "test_code": "SYSBP",
        "explanation": "Reason 1",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers
        )
        assert resp.status_code == 201

        # Attempt duplicate
        resp2 = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers
        )
        assert resp2.status_code == 400
        assert "An active query already exists" in resp2.json()["detail"]


@pytest.mark.asyncio
async def test_query_state_transition_and_role_boundaries() -> None:
    """Test full discrepancy query sequence NONE -> OPEN -> ANSWERED -> CLOSED."""
    dm_headers = get_v2_auth_headers(
        user_id="dm_user", roles="Data Manager", change_reason="DM raises query"
    )
    inv_headers = get_v2_auth_headers(
        user_id="inv_user",
        roles="Investigator",
        change_reason="Investigator responds to query",
    )

    payload = {
        "study_id": "STUDY-123",
        "subject_id": "SUBJ-002",
        "test_code": "TEMP",
        "explanation": "Temperature is out of bounds.",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Raise/Open query (NONE -> OPEN)
        resp_open = await client.post(
            "/api/v1/execution/queries", json=payload, headers=dm_headers
        )
        assert resp_open.status_code == 201
        query_id = resp_open.json()["id"]

        # 2. Reject Data Manager directly answering query
        resp_dm_ans = await client.post(
            f"/api/v1/execution/queries/{query_id}/respond",
            json={"response": "I cannot answer this, I am DM"},
            headers=dm_headers,
        )
        assert resp_dm_ans.status_code == 403

        # 3. Reject Investigator closing query directly
        resp_inv_close = await client.post(
            f"/api/v1/execution/queries/{query_id}/close", headers=inv_headers
        )
        assert resp_inv_close.status_code == 403

        # 4. Reject Investigator attempting invalid direct transition (OPEN -> CLOSED)
        # Using general patch endpoint as investigator
        resp_patch_invalid = await client.patch(
            f"/api/v1/execution/queries/{query_id}",
            json={"status": "CLOSED"},
            headers=inv_headers,
        )
        assert resp_patch_invalid.status_code == 400
        assert "Invalid transition" in resp_patch_invalid.json()["detail"]

        # 5. Reject Data Manager attempting invalid direct transition (OPEN -> CLOSED)
        resp_dm_close_invalid = await client.post(
            f"/api/v1/execution/queries/{query_id}/close", headers=dm_headers
        )
        assert resp_dm_close_invalid.status_code == 400
        assert "Invalid transition" in resp_dm_close_invalid.json()["detail"]

        # 6. Investigator successfully answers query (OPEN -> ANSWERED)
        resp_ans = await client.post(
            f"/api/v1/execution/queries/{query_id}/respond",
            json={"response": "The thermometer was re-calibrated; entry is correct."},
            headers=inv_headers,
        )
        assert resp_ans.status_code == 200
        assert resp_ans.json()["status"] == "ANSWERED"
        assert (
            resp_ans.json()["response"]
            == "The thermometer was re-calibrated; entry is correct."
        )

        # 7. Data Manager closes answered query (ANSWERED -> CLOSED)
        dm_close_headers = get_v2_auth_headers(
            user_id="dm_user", roles="Data Manager", change_reason="Closing query"
        )
        resp_close = await client.post(
            f"/api/v1/execution/queries/{query_id}/close",
            headers=dm_close_headers,
        )
        assert resp_close.status_code == 200
        assert resp_close.json()["status"] == "CLOSED"

        # 8. Check history trace of query transitions in AuditLog
        final_get = await client.get(
            f"/api/v1/execution/queries/{query_id}", headers=dm_headers
        )
        history = final_get.json()["history"]
        assert len(history) == 3
        assert history[0]["action"] == "INSERT"
        assert history[0]["change_reason"] == "DM raises query"
        assert history[1]["action"] == "UPDATE"
        assert history[1]["change_reason"] == "Investigator responds to query"
        assert history[2]["action"] == "UPDATE"
        assert history[2]["change_reason"] == "Closing query"


@pytest.mark.asyncio
async def test_reopen_transitions() -> None:
    """Test reopening query state transitions."""
    dm_headers = get_v2_auth_headers(
        roles="Data Manager", change_reason="DM raises query"
    )
    inv_headers = get_v2_auth_headers(
        roles="Investigator", change_reason="Investigator answers"
    )

    payload = {
        "study_id": "S1",
        "subject_id": "SUBJ-A",
        "test_code": "HR",
        "explanation": "Heart rate is zero.",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Open
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=dm_headers
        )
        q_id = resp.json()["id"]

        # Respond (OPEN -> ANSWERED)
        await client.post(
            f"/api/v1/execution/queries/{q_id}/respond",
            json={"response": "Patient is alive"},
            headers=inv_headers,
        )

        # Reopen (ANSWERED -> REOPENED)
        dm_reopen_headers = get_v2_auth_headers(
            roles="Data Manager", change_reason="Not good enough explanation"
        )
        resp_reopen = await client.post(
            f"/api/v1/execution/queries/{q_id}/reopen",
            headers=dm_reopen_headers,
        )
        assert resp_reopen.status_code == 200
        assert resp_reopen.json()["status"] == "REOPENED"

        # Respond again (REOPENED -> ANSWERED)
        resp_ans2 = await client.post(
            f"/api/v1/execution/queries/{q_id}/respond",
            json={"response": "Double checked, patient was sleeping, HR was 45"},
            headers=inv_headers,
        )
        assert resp_ans2.status_code == 200
        assert resp_ans2.json()["status"] == "ANSWERED"


@pytest.mark.asyncio
async def test_database_events_prevent_deletions() -> None:
    """Test that direct database-level deletions are blocked by triggers / SQLAlchemy event listeners."""
    dm_headers = get_v2_auth_headers(roles="Data Manager", change_reason="Create query")
    payload = {
        "study_id": "S1",
        "subject_id": "SUBJ-A",
        "test_code": "HR",
        "explanation": "Explain",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=dm_headers
        )
        q_id = resp.json()["id"]

    # Attempt direct deletion of the created query from the database using Session
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(ClinicalQuery.id == q_id)
        res = await session.execute(stmt)
        query_obj = res.scalars().first()
        assert query_obj is not None

        # Verify that calling delete on session raises ValueError
        await session.delete(query_obj)
        with pytest.raises(ValueError) as excinfo:
            await session.commit()

        assert "Hard deletion of ClinicalQuery is forbidden" in str(excinfo.value)


@pytest.mark.asyncio
async def test_clinical_query_creation_with_all_audited_fields() -> None:
    """Test creating a clinical query with all new audited persistence fields."""
    headers = get_v2_auth_headers(
        user_id="dm_user_001",
        roles="Data Manager",
        change_reason="Raise query with extended metadata",
    )
    payload = {
        "study_id": "STUDY-ABC",
        "subject_id": "SUBJ-101",
        "visit_id": "VISIT-01",
        "domain": "VS",
        "test_code": "SYSBP",
        "explanation": "Too high",
        "observation_id": "obs-123",
        "field_link": "ecrf_link_321",
        "message": "Message body here",
        "origin": "SYSTEM",
        "priority": "HIGH",
        "rule_id": "rule_01_sysbp_max",
        "created_by": "dm_user_001",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/api/v1/execution/queries", json=payload, headers=headers
        )

    assert response.status_code == 201
    data = response.json()
    assert data["observation_id"] == "obs-123"
    assert data["field_link"] == "ecrf_link_321"
    assert data["message"] == "Message body here"
    assert data["origin"] == "SYSTEM"
    assert data["priority"] == "HIGH"
    assert data["rule_id"] == "rule_01_sysbp_max"
    assert data["created_by"] == "dm_user_001"


@pytest.mark.asyncio
async def test_clinical_query_trial_lock_enforcement_at_visit_level() -> None:
    """Test that clinical query updates are blocked when the visit is locked."""
    from apps.execution.trial_lock import TrialLockManager

    # Lock the visit
    TrialLockManager.lock_visit("VISIT-LOCKED")

    dm_headers = get_v2_auth_headers(
        roles="Data Manager", change_reason="DM raises query on locked visit"
    )
    payload = {
        "study_id": "STUDY-ABC",
        "subject_id": "SUBJ-101",
        "visit_id": "VISIT-LOCKED",
        "domain": "VS",
        "test_code": "SYSBP",
        "explanation": "Locked visit test",
    }

    try:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            with pytest.raises(PermissionError) as excinfo:
                await client.post(
                    "/api/v1/execution/queries", json=payload, headers=dm_headers
                )
            assert "locked in a read-only state" in str(excinfo.value)
    finally:
        # Cleanup lock state
        TrialLockManager.unlock_visit("VISIT-LOCKED")


@pytest.mark.asyncio
async def test_candidate_creation_and_opening_workflow() -> None:
    """Test raising a query in CANDIDATE state, then transitioning it to OPEN."""
    dm_headers = get_v2_auth_headers(
        roles="Data Manager", change_reason="DM raises candidate query"
    )
    payload = {
        "study_id": "STUDY-CANDIDATE",
        "subject_id": "SUBJ-X",
        "test_code": "HR",
        "explanation": "Is this a valid entry?",
        "status": "CANDIDATE",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create query in CANDIDATE status
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=dm_headers
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["status"] == "CANDIDATE"
        q_id = data["id"]

        # 2. Transition CANDIDATE -> OPEN using PATCH
        resp_patch = await client.patch(
            f"/api/v1/execution/queries/{q_id}",
            json={"status": "OPEN"},
            headers=dm_headers,
        )
        assert resp_patch.status_code == 200
        assert resp_patch.json()["status"] == "OPEN"


@pytest.mark.asyncio
async def test_rejection_and_cancellation_reason_requirements() -> None:
    """Test that reject (ANSWERED -> REOPENED) and cancel actions require non-empty reasons."""
    dm_headers = get_v2_auth_headers(
        roles="Data Manager", change_reason="DM raises query"
    )
    inv_headers = get_v2_auth_headers(
        roles="Investigator", change_reason="Investigator answers"
    )

    payload = {
        "study_id": "STUDY-REJECT",
        "subject_id": "SUBJ-Y",
        "test_code": "SYSBP",
        "explanation": "Out of range BP",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create OPEN query
        resp = await client.post(
            "/api/v1/execution/queries", json=payload, headers=dm_headers
        )
        q_id = resp.json()["id"]

        # Transition OPEN -> ANSWERED
        await client.post(
            f"/api/v1/execution/queries/{q_id}/respond",
            json={"response": "Valid reading"},
            headers=inv_headers,
        )

        # Reopen/Reject with no reason should fail with 400
        resp_reopen_fail = await client.post(
            f"/api/v1/execution/queries/{q_id}/reopen",
            json={"reason": ""},
            headers=dm_headers,
        )
        assert resp_reopen_fail.status_code == 400

        # Reopen/Reject with reason should succeed
        resp_reopen_ok = await client.post(
            f"/api/v1/execution/queries/{q_id}/reopen",
            headers=get_v2_auth_headers(
                roles="Data Manager", change_reason="Please re-verify"
            ),
        )
        assert resp_reopen_ok.status_code == 200
        assert resp_reopen_ok.json()["status"] == "REOPENED"
        assert resp_reopen_ok.json()["explanation"] == "Please re-verify"

        # Cancel with no reason should fail with 400
        resp_cancel_fail = await client.post(
            f"/api/v1/execution/queries/{q_id}/cancel",
            json={"reason": ""},
            headers=dm_headers,
        )
        assert resp_cancel_fail.status_code == 400

        # Cancel with reason should succeed
        resp_cancel_ok = await client.post(
            f"/api/v1/execution/queries/{q_id}/cancel",
            json={"reason": "Entered in error"},
            headers=dm_headers,
        )
        assert resp_cancel_ok.status_code == 200
        assert resp_cancel_ok.json()["status"] == "CANCELLED"
        assert resp_cancel_ok.json()["cancellation_reason"] == "Entered in error"


@pytest.mark.asyncio
async def test_query_role_gates_robustness() -> None:
    """Test detailed role-based access control requirements on query execution APIs."""
    cra_headers = get_v2_auth_headers(roles="CRA", change_reason="CRA raises query")
    inv_headers = get_v2_auth_headers(
        roles="Investigator", change_reason="Investigator responds"
    )
    auditor_headers = get_v2_auth_headers(
        roles="Auditor", change_reason="Auditor trying to raise query"
    )
    sponsor_headers = get_v2_auth_headers(
        roles="Sponsor Admin", change_reason="Sponsor Admin trying to raise query"
    )

    payload = {
        "study_id": "STUDY-RG",
        "subject_id": "SUBJ-Z",
        "test_code": "HR",
        "explanation": "Check heart rate",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Auditor trying to raise query should fail with 403
        resp_auditor = await client.post(
            "/api/v1/execution/queries", json=payload, headers=auditor_headers
        )
        assert resp_auditor.status_code == 403

        # 2. Sponsor Admin trying to raise query should fail with 403
        resp_sponsor = await client.post(
            "/api/v1/execution/queries", json=payload, headers=sponsor_headers
        )
        assert resp_sponsor.status_code == 403

        # 3. CRA raising query should succeed (201)
        resp_cra = await client.post(
            "/api/v1/execution/queries", json=payload, headers=cra_headers
        )
        assert resp_cra.status_code == 201
        q_id = resp_cra.json()["id"]

        # 4. CRA trying to respond/answer should fail (403)
        resp_cra_respond = await client.post(
            f"/api/v1/execution/queries/{q_id}/respond",
            json={"response": "We are CRA"},
            headers=cra_headers,
        )
        assert resp_cra_respond.status_code == 403

        # 5. Investigator responding/answering should succeed
        resp_inv_respond = await client.post(
            f"/api/v1/execution/queries/{q_id}/respond",
            json={"response": "Re-verified correct"},
            headers=inv_headers,
        )
        assert resp_inv_respond.status_code == 200

        # 6. Investigator trying to close query should fail (403)
        resp_inv_close = await client.post(
            f"/api/v1/execution/queries/{q_id}/close",
            headers=inv_headers,
        )
        assert resp_inv_close.status_code == 403

        # 7. CRA closing query should succeed
        resp_cra_close = await client.post(
            f"/api/v1/execution/queries/{q_id}/close",
            headers=cra_headers,
        )
        assert resp_cra_close.status_code == 200
