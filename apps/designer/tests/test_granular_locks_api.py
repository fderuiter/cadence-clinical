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
    user_id="test_dm",
    roles="Data Manager",
    change_reason="system_operation",
    signature_version="2",
    timestamp=None,
    signature=None,
    omit_roles=False,
    omit_signature=False,
    omit_timestamp=False,
    omit_version=False,
    tamper_signature=False,
    tenant_id="tenant_default",
):
    """Generate Gateway signature-compliant authentication headers with flexibility for testing."""
    from packages.security.signing import generate_gateway_signature

    if timestamp is None:
        timestamp = str(time.time())

    headers = {}
    if user_id is not None:
        headers["X-User-Id"] = user_id

    if not omit_roles:
        headers["X-User-Roles"] = roles

    if not omit_timestamp:
        headers["X-Gateway-Timestamp"] = timestamp

    if not omit_version:
        headers["X-Signature-Version"] = signature_version

    if change_reason is not None:
        headers["X-Change-Reason"] = change_reason

    if tenant_id is not None:
        headers["X-Tenant-Id"] = tenant_id

    # If signature is not forced, compute it
    if signature is None and not omit_signature:
        sig_roles = roles if roles is not None else ""
        signature = generate_gateway_signature(
            user_id=user_id or "",
            roles=sig_roles,
            timestamp=timestamp,
            secret=GATEWAY_SECRET.encode(),
            change_reason=change_reason,
            tenant_id=tenant_id,
        )

    if tamper_signature and signature is not None:
        signature = signature + "-tampered"

    if signature is not None:
        headers["X-Gateway-Signature"] = signature

    return headers


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


@pytest.mark.asyncio
async def test_allowed_roles_matrix() -> None:
    """Verify that only authorized Data Manager and Sponsor Admin roles (and their aliases) can perform lock mutations.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Sponsor Admin (canonical) locks/unlocks site
        headers_sa = get_auth_headers(
            roles="Sponsor Admin", change_reason="SA Site Lock"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-SA/lock", headers=headers_sa
        )
        assert res.status_code == 200
        assert "SITE-SA" in TrialLockManager._locked_sites

        res_un = await client.post(
            "/api/v1/execution/locks/site/SITE-SA/unlock", headers=headers_sa
        )
        assert res_un.status_code == 200
        assert "SITE-SA" not in TrialLockManager._locked_sites

        # 2. sponsor_admin alias freezes/unfreezes visit
        headers_sa_alias = get_auth_headers(
            roles="sponsor_admin", change_reason="SA Visit Freeze"
        )
        res = await client.post(
            "/api/v1/execution/locks/visit/VIS-SA/freeze", headers=headers_sa_alias
        )
        assert res.status_code == 200
        assert "VIS-SA" in TrialLockManager._locked_visits

        res_un = await client.post(
            "/api/v1/execution/locks/visit/VIS-SA/unfreeze", headers=headers_sa_alias
        )
        assert res_un.status_code == 200
        assert "VIS-SA" not in TrialLockManager._locked_visits

        # 3. data_manager alias locks form
        headers_dm = get_auth_headers(
            roles="data_manager", change_reason="DM Form Lock"
        )
        res = await client.post(
            "/api/v1/execution/locks/form/FORM-DM/lock", headers=headers_dm
        )
        assert res.status_code == 200
        assert "FORM-DM" in TrialLockManager._locked_forms

        # 4. dm alias locks subject
        headers_dm_alias = get_auth_headers(roles="dm", change_reason="DM Subject Lock")
        res = await client.post(
            "/api/v1/execution/locks/subject/SUB-DM/lock", headers=headers_dm_alias
        )
        assert res.status_code == 200
        assert "SUB-DM" in TrialLockManager._locked_subjects

        # 5. admin alias locks/unlocks trial
        headers_admin = get_auth_headers(
            roles="admin", change_reason="Admin Trial Lock"
        )
        res = await client.post(
            "/api/v1/execution/locks/trial/lock", headers=headers_admin
        )
        assert res.status_code == 200
        assert TrialLockManager.is_locked() is True

        res_un = await client.post(
            "/api/v1/execution/locks/trial/unlock", headers=headers_admin
        )
        assert res_un.status_code == 200
        assert TrialLockManager.is_locked() is False


@pytest.mark.asyncio
async def test_forbidden_roles_matrix() -> None:
    """Verify that unauthorized roles are rejected and do not mutate state.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        unauthorized_roles = [
            "CRA",
            "cra",
            "site investigator",
            "investigator",
            "Auditor",
            "auditor",
            "sysadmin",
            "unknown-role",
        ]

        for role in unauthorized_roles:
            # We must verify at least one endpoint per granularity plus freeze/unfreeze alias
            headers = get_auth_headers(
                roles=role, change_reason="Illegal lock attempt"
            )  # pragma: allowlist secret

            # Site level
            res = await client.post(
                "/api/v1/execution/locks/site/SITE-FORBIDDEN/lock", headers=headers
            )
            assert res.status_code == 403, (
                f"Role {role} should be forbidden on site lock"
            )
            assert "SITE-FORBIDDEN" not in TrialLockManager._locked_sites

            # Visit freeze alias
            res = await client.post(
                "/api/v1/execution/locks/visit/VIS-FORBIDDEN/freeze", headers=headers
            )
            assert res.status_code == 403, (
                f"Role {role} should be forbidden on visit freeze"
            )
            assert "VIS-FORBIDDEN" not in TrialLockManager._locked_visits

            # Form level
            res = await client.post(
                "/api/v1/execution/locks/form/FORM-FORBIDDEN/lock", headers=headers
            )
            assert res.status_code == 403, (
                f"Role {role} should be forbidden on form lock"
            )
            assert "FORM-FORBIDDEN" not in TrialLockManager._locked_forms

            # Subject level
            res = await client.post(
                "/api/v1/execution/locks/subject/SUB-FORBIDDEN/lock", headers=headers
            )
            assert res.status_code == 403, (
                f"Role {role} should be forbidden on subject lock"
            )
            assert "SUB-FORBIDDEN" not in TrialLockManager._locked_subjects

            # Trial level
            res = await client.post(
                "/api/v1/execution/locks/trial/lock", headers=headers
            )
            assert res.status_code == 403, (
                f"Role {role} should be forbidden on trial lock"
            )
            assert TrialLockManager.is_locked() is False


@pytest.mark.asyncio
async def test_absent_and_malformed_roles() -> None:
    """Verify that absent, empty, whitespace-only, and garbage roles are strictly rejected with 403.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Case A: Absent X-User-Roles
        headers_absent = get_auth_headers(
            roles="", omit_roles=True, change_reason="Absent roles"
        )  # pragma: allowlist secret
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-MALFORMED/lock", headers=headers_absent
        )
        assert res.status_code == 403
        assert "SITE-MALFORMED" not in TrialLockManager._locked_sites

        # Case B: Empty role values
        headers_empty = get_auth_headers(
            roles="", change_reason="Empty roles"
        )  # pragma: allowlist secret
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-MALFORMED/lock", headers=headers_empty
        )
        assert res.status_code == 403
        assert "SITE-MALFORMED" not in TrialLockManager._locked_sites

        # Case C: Whitespace-only role values
        headers_ws = get_auth_headers(
            roles="   ", change_reason="WS roles"
        )  # pragma: allowlist secret
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-MALFORMED/lock", headers=headers_ws
        )
        assert res.status_code == 403
        assert "SITE-MALFORMED" not in TrialLockManager._locked_sites

        # Case D: Garbage role values
        headers_garbage = get_auth_headers(
            roles="garbage_role_here_abc", change_reason="Garbage roles"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-MALFORMED/lock", headers=headers_garbage
        )
        assert res.status_code == 403
        assert "SITE-MALFORMED" not in TrialLockManager._locked_sites


@pytest.mark.asyncio
async def test_gateway_bypass_prevention() -> None:
    """Verify that direct microservice requests bypassing trusted gateway authentication are strictly blocked.

    Requirements: PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Case 1: Missing gateway headers entirely
        res = await client.post("/api/v1/execution/locks/site/SITE-BYPASS/lock")
        # Middleware returns 403 on POST if authorization headers are missing
        assert res.status_code in (401, 403)
        assert "SITE-BYPASS" not in TrialLockManager._locked_sites

        # Case 2: Missing signature version
        headers_no_ver = get_auth_headers(
            omit_version=True, change_reason="No signature version"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-BYPASS/lock", headers=headers_no_ver
        )
        assert res.status_code in (401, 403)
        assert "SITE-BYPASS" not in TrialLockManager._locked_sites

        # Case 3: Missing signature
        headers_no_sig = get_auth_headers(
            omit_signature=True, change_reason="No signature"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-BYPASS/lock", headers=headers_no_sig
        )
        assert res.status_code in (401, 403)
        assert "SITE-BYPASS" not in TrialLockManager._locked_sites

        # Case 4: Invalid/tampered signature
        headers_tampered = get_auth_headers(
            tamper_signature=True, change_reason="Tampered signature"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-BYPASS/lock", headers=headers_tampered
        )
        assert res.status_code in (401, 403)
        assert "SITE-BYPASS" not in TrialLockManager._locked_sites

        # Case 5: Expired timestamp scenarios
        expired_ts = str(time.time() - 301)
        headers_expired = get_auth_headers(
            timestamp=expired_ts, change_reason="Expired timestamp"
        )
        res = await client.post(
            "/api/v1/execution/locks/site/SITE-BYPASS/lock", headers=headers_expired
        )
        assert res.status_code in (401, 403)
        assert "SITE-BYPASS" not in TrialLockManager._locked_sites
