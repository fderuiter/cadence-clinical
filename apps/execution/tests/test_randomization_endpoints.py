"""Unit tests for RTSM Subject State and Demographics endpoints.

# @Req:PRD-SUB-003
# @req:PRD-SUB-003
# @Req:PRD-SUB-004
# @req:PRD-SUB-004
"""

import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, ClinicalSubject
from apps.execution.main import app

GATEWAY_SECRET = os.getenv(
    "GATEWAY_SECRET", "internal-gateway-secret-12345"
)  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_dm",
    roles="Data Manager",
    change_reason="system_operation",
    tenant_id="tenant_default",
):
    """Generate Gateway signature-compliant authentication headers."""
    from packages.security.signing import generate_gateway_signature

    timestamp = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id or "",
        roles=roles or "",
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        tenant_id=tenant_id,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": tenant_id,
        "X-Gateway-Signature": sig,
    }


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_subject_state_transition_endpoint() -> None:
    """Verify subject state transition validation and endpoint responses."""
    # 1. Create a subject via db
    headers = get_auth_headers(
        roles="site investigator", change_reason="Initial Screening"
    )
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a subject
        create_res = await client.post(
            "/api/v1/execution/subjects",
            headers=headers,
            json={
                "subject_id": "SUBJ-901",
                "study_id": "STUDY-XYZ",
                "demographics": {"gender": "F", "birthdate": "1990-01-01"},
            },
        )
        assert create_res.status_code == 200
        subj_data = create_res.json()
        assert subj_data["subject_id"] == "SUBJ-901"

        # Try illegal transition SCREENING -> RANDOMIZED directly
        illegal_res = await client.patch(
            "/api/v1/execution/subjects/SUBJ-901/state",
            headers=headers,
            json={"status": "RANDOMIZED"},
        )
        # Should raise 400 INVALID_STATE_TRANSITION
        assert illegal_res.status_code == 400
        assert "INVALID_STATE_TRANSITION" in illegal_res.json()["detail"]

        # Try legal transition SCREENING -> ENROLLED
        legal_res = await client.patch(
            "/api/v1/execution/subjects/SUBJ-901/state",
            headers=headers,
            json={"status": "ENROLLED"},
        )
        assert legal_res.status_code == 200

        # Verify status in db is ENROLLED
        async with db_manager.get_session_maker()() as session:
            stmt = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == "SUBJ-901"
            )
            res = await session.execute(stmt)
            subj = res.scalars().one()
            assert subj.status == "ENROLLED"


@pytest.mark.asyncio
async def test_subject_demographics_mutation_and_deletion_endpoints() -> None:
    """Verify that demographics and stratification factors can be mutated in pre-randomization state,

    but mutations/deletions are strictly rejected post-randomization.
    """
    headers = get_auth_headers(
        roles="site investigator", change_reason="Baseline Updates"
    )
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a subject
        create_res = await client.post(
            "/api/v1/execution/subjects",
            headers=headers,
            json={
                "subject_id": "SUBJ-902",
                "study_id": "STUDY-XYZ",
                "demographics": {"gender": "M", "birthdate": "1980-05-15"},
            },
        )
        assert create_res.status_code == 200

        # Set stratification factors in pre-randomization (SCREENING) state
        put_res = await client.put(
            "/api/v1/execution/subjects/SUBJ-902/demographics",
            headers=headers,
            json={
                "strat_factors": {"site": "SITE-A"},
                "demographics": {
                    "gender": "M",
                    "birthdate": "1980-05-15",
                    "race": "White",
                },
            },
        )
        assert put_res.status_code == 200

        # 2. Simulate transition to post-randomization status
        # Transition to ENROLLED first
        state_res = await client.patch(
            "/api/v1/execution/subjects/SUBJ-902/state",
            headers=headers,
            json={"status": "ENROLLED"},
        )
        assert state_res.status_code == 200

        # Force subject state to RANDOMIZED in DB
        async with db_manager.get_session_maker()() as session, session.begin():
            stmt = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == "SUBJ-902"
            )
            res = await session.execute(stmt)
            subj = res.scalars().one()
            # Bypass validators by changing underlying status and setting random ID
            subj.randomization_id = "RAND-ASSIGN-902"
            subj.status = "RANDOMIZED"
            session.add(subj)

        # 3. Test idempotent PUT (setting identical strat_factors and demographics) -> Allowed
        headers_put = get_auth_headers(
            roles="site investigator", change_reason="Idempotent Update"
        )
        put_idempotent = await client.put(
            "/api/v1/execution/subjects/SUBJ-902/demographics",
            headers=headers_put,
            json={
                "strat_factors": {"site": "SITE-A"},
                "demographics": {
                    "gender": "M",
                    "birthdate": "1980-05-15",
                    "race": "White",
                },
            },
        )
        assert put_idempotent.status_code == 200

        # 4. Test mutating strat_factors post-randomization -> LockedFactorMutationError 422
        put_mutated = await client.put(
            "/api/v1/execution/subjects/SUBJ-902/demographics",
            headers=headers_put,
            json={
                "strat_factors": {"site": "SITE-B"},
            },
        )
        assert put_mutated.status_code == 422
        assert put_mutated.json()["detail"] == "LOCKED_FACTOR_MUTATION"

        # 5. Test mutating demographics post-randomization -> LockedFactorMutationError 422
        put_mutated_demo = await client.put(
            "/api/v1/execution/subjects/SUBJ-902/demographics",
            headers=headers_put,
            json={
                "demographics": {"gender": "F"},
            },
        )
        assert put_mutated_demo.status_code == 422
        assert put_mutated_demo.json()["detail"] == "LOCKED_FACTOR_MUTATION"

        # 6. Test deleting demographics post-randomization -> SOFT_DELETE_BLOCKED 403
        headers_delete = get_auth_headers(
            roles="site investigator", change_reason="Delete Request"
        )
        delete_res = await client.delete(
            "/api/v1/execution/subjects/SUBJ-902/demographics",
            headers=headers_delete,
        )
        assert delete_res.status_code == 403
        assert delete_res.json()["detail"] == "SOFT_DELETE_BLOCKED"

        # 7. Test deleting pre-randomized subject demographics (create a new one)
        create_res2 = await client.post(
            "/api/v1/execution/subjects",
            headers=headers,
            json={
                "subject_id": "SUBJ-903",
                "study_id": "STUDY-XYZ",
                "demographics": {"gender": "F"},
            },
        )
        assert create_res2.status_code == 200

        delete_pre_res = await client.delete(
            "/api/v1/execution/subjects/SUBJ-903/demographics",
            headers=headers,
        )
        assert delete_pre_res.status_code == 200
        assert delete_pre_res.json()["encrypted_demographics"] is None
