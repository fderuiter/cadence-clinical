import hashlib
import hmac
import os
import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import String
from sqlalchemy.orm import Mapped, mapped_column

from apps.execution.database.context import current_session
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import AuditedModel, Base
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


class MockClinicalObservation(AuditedModel):
    __tablename__ = "mock_clinical_observations_lock_test"
    data_value: Mapped[str] = mapped_column(String(255), nullable=True)
    site_id: Mapped[str | None] = mapped_column(String(50), nullable=True)
    visit_id: Mapped[str | None] = mapped_column(String(50), nullable=True)
    subject_id: Mapped[str | None] = mapped_column(String(50), nullable=True)
    form_id: Mapped[str | None] = mapped_column(String(50), nullable=True)


def get_auth_headers(
    user_id="test_dm", roles="Data Manager", change_reason="system_operation"
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
async def setup_test_db_and_locks():
    """Setup in-memory SQLite database before each test, reset locks, and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    TrialLockManager.reset()
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_lock_status_retrieval() -> None:
    """Verify that lock status starts clean and is retrieved correctly via GET."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Check initial clean state (CRA or Data Manager can read)
        headers = get_auth_headers(roles="cra")
        res = await client.get("/api/v1/execution/locks", headers=headers)
        assert res.status_code == 200
        data = res.json()
        assert data["locked_sites"] == []
        assert data["locked_visits"] == []
        assert data["locked_forms"] == []
        assert data["locked_subjects"] == []
        assert data["trial_locked"] is False


@pytest.mark.asyncio
async def test_roles_authorization_restrictions() -> None:
    """Verify that only authorized Data Manager roles can modify lock states."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Investigator role should be blocked (HTTP 403)
        headers = get_auth_headers(roles="site investigator")
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-01/lock", headers=headers
        )
        assert res.status_code == 403

        # Missing change justification should be blocked
        headers_no_reason = get_auth_headers(roles="Data Manager", change_reason="")
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-01/lock", headers=headers_no_reason
        )
        assert res.status_code == 403


@pytest.mark.asyncio
async def test_site_lock_and_unlock_lifecycle() -> None:
    """Verify lock and unlock lifecycle for site level, using both freeze and lock endpoints."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager", change_reason="Site Freeze")

        # 1. Lock site
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-01/lock", headers=headers
        )
        assert res.status_code == 200
        assert res.json()["status"] == "success"

        # Check state
        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SITE-01" in res_status.json()["locked_sites"]

        # 2. Unlock site
        res_unlock = await client.post(
            "/api/v1/execution/locks/site/SITE-01/unlock", headers=headers
        )
        assert res_unlock.status_code == 200

        # Check state again
        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SITE-01" not in res_status.json()["locked_sites"]

        # 3. Freeze site (as alias)
        res_freeze = await client.post(
            "/api/v1/execution/locks/site/SITE-02/freeze", headers=headers
        )
        assert res_freeze.status_code == 200

        # Check state
        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SITE-02" in res_status.json()["locked_sites"]

        # 4. Unfreeze site (as alias)
        res_unfreeze = await client.post(
            "/api/v1/execution/locks/site/SITE-02/unfreeze", headers=headers
        )
        assert res_unfreeze.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SITE-02" not in res_status.json()["locked_sites"]


@pytest.mark.asyncio
async def test_visit_lock_and_unlock_lifecycle() -> None:
    """Verify lock and unlock lifecycle for visit level."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager", change_reason="Visit Lock")

        # Lock visit
        res = await client.post(
            "/api/v1/execution/locks/visit/VISIT-01/lock", headers=headers
        )
        assert res.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "VISIT-01" in res_status.json()["locked_visits"]

        # Unlock visit
        res_unlock = await client.post(
            "/api/v1/execution/locks/visit/VISIT-01/unlock", headers=headers
        )
        assert res_unlock.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "VISIT-01" not in res_status.json()["locked_visits"]


@pytest.mark.asyncio
async def test_form_lock_and_unlock_lifecycle() -> None:
    """Verify lock and unlock lifecycle for form level."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager", change_reason="Form Freeze")

        # Lock form
        res = await client.post(
            "/api/v1/execution/locks/form/FORM-01/lock", headers=headers
        )
        assert res.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "FORM-01" in res_status.json()["locked_forms"]

        # Unlock form
        res_unlock = await client.post(
            "/api/v1/execution/locks/form/FORM-01/unlock", headers=headers
        )
        assert res_unlock.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "FORM-01" not in res_status.json()["locked_forms"]


@pytest.mark.asyncio
async def test_subject_lock_and_unlock_lifecycle() -> None:
    """Verify lock and unlock lifecycle for subject level."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager", change_reason="Subject Lock")

        # Lock subject
        res = await client.post(
            "/api/v1/execution/locks/subject/SUB-01/lock", headers=headers
        )
        assert res.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SUB-01" in res_status.json()["locked_subjects"]

        # Unlock subject
        res_unlock = await client.post(
            "/api/v1/execution/locks/subject/SUB-01/unlock", headers=headers
        )
        assert res_unlock.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert "SUB-01" not in res_status.json()["locked_subjects"]


@pytest.mark.asyncio
async def test_trial_lock_and_unlock_lifecycle() -> None:
    """Verify lock and unlock lifecycle for study/trial-wide level."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(roles="Data Manager", change_reason="Study Lock")

        # Lock trial
        res = await client.post("/api/v1/execution/locks/trial/lock", headers=headers)
        assert res.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert res_status.json()["trial_locked"] is True

        # Unlock trial
        res_unlock = await client.post(
            "/api/v1/execution/locks/trial/unlock", headers=headers
        )
        assert res_unlock.status_code == 200

        res_status = await client.get("/api/v1/execution/locks", headers=headers)
        assert res_status.json()["trial_locked"] is False


@pytest.mark.asyncio
async def test_locked_write_prevention() -> None:
    """Verify that writing/modifying data under locked entity scopes raises PermissionError."""
    session_maker = db_manager.get_session_maker()

    # Create a record normally
    @transactional(session_maker)
    async def create_normal_record() -> str:
        session = current_session.get()
        rec = MockClinicalObservation(
            data_value="initial",
            site_id="SITE-A",
            visit_id="VIS_1",
            subject_id="SUB_1",
            form_id="FORM_1",
        )
        session.add(rec)
        await session.flush()
        return str(rec.id)

    rec_id = await create_normal_record()
    assert rec_id is not None

    # Now lock site "SITE-A"
    TrialLockManager.lock_site("SITE-A")

    # Trying to modify or insert a record under SITE-A should fail at before_flush
    @transactional(session_maker)
    async def create_failed_record() -> None:
        session = current_session.get()
        rec = MockClinicalObservation(
            data_value="fails",
            site_id="SITE-A",
        )
        session.add(rec)
        await session.flush()

    with pytest.raises(PermissionError, match="SITE-A is currently locked"):
        await create_failed_record()

    # Unlock site, and verify writes work again
    TrialLockManager.unlock_site("SITE-A")
    # Verify no longer raises
    await create_normal_record()
