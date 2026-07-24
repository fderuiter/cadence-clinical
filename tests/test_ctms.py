import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.ctms.database import db_manager
from apps.ctms.main import app
from apps.ctms.models import Base, CTMSAuditLog, CTMSStudy
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory CTMS database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "test_user"
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


def test_ctms_health_check():
    """
    Verify health check of independent CTMS service.
    """
    client = TestClient(app)
    response = client.get("/health")
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
    assert data["service"] == "ctms"


@pytest.mark.asyncio
async def test_create_and_list_studies_rbac():
    """
    Verify creation of studies with appropriate RBAC roles, Part 11 audit fields,
    and automatic writing to the append-only CTMSAuditLog.
    """
    client = TestClient(app)

    # 1. Non-permitted role (e.g. Site Investigator) trying to POST study -> 403 Forbidden
    unauthorized_headers = get_auth_headers(
        roles="Site Investigator", change_reason="Unauthorized attempt"
    )
    payload = {
        "study_id": "study_ctms_101",
        "name": "CTMS Trial site operations",
        "status": "ACTIVE",
    }
    response = client.post(
        "/api/v1/ctms/studies", json=payload, headers=unauthorized_headers
    )
    assert response.status_code == 403
    assert "Access denied" in response.json()["detail"]

    # 2. Permitted role (e.g. Monitor) trying to POST study -> 201 Created
    authorized_headers = get_auth_headers(
        roles="Monitor", change_reason="Initiate study tracking in CTMS"
    )
    response_created = client.post(
        "/api/v1/ctms/studies", json=payload, headers=authorized_headers
    )
    assert response_created.status_code == 201
    created_data = response_created.json()
    assert created_data["study_id"] == "study_ctms_101"
    assert created_data["name"] == "CTMS Trial site operations"
    assert created_data["status"] == "ACTIVE"
    assert created_data["created_by"] == "test_user"
    assert created_data["reason_for_change"] == "Initiate study tracking in CTMS"
    assert created_data["version_index"] == 1

    # 3. Permitted role (e.g. Grants Manager) listing studies -> 200 OK
    grants_headers = get_auth_headers(roles="Grants Manager")
    response_list = client.get("/api/v1/ctms/studies", headers=grants_headers)
    assert response_list.status_code == 200
    studies = response_list.json()
    assert len(studies) == 1
    assert studies[0]["study_id"] == "study_ctms_101"

    # 4. Check CTMSAuditLog database records
    async with db_manager.get_session_maker()() as session:
        stmt = select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        assert len(logs) >= 2
        actions = [log.action for log in logs]
        assert "LIST_STUDIES" in actions
        assert "CREATE_STUDY" in actions

        create_log = next(log for log in logs if log.action == "CREATE_STUDY")
        assert create_log.user_id == "test_user"
        assert create_log.user_role == "Monitor"
        assert "Initiate study tracking" in create_log.details


@pytest.mark.asyncio
async def test_get_audit_trail_rbac():
    """
    Verify auditing trail endpoint RBAC restriction and logging behavior.
    """
    client = TestClient(app)

    # 1. Auditor retrieves the audit log list
    auditor_headers = get_auth_headers(roles="Auditor")
    response_logs = client.get("/api/v1/ctms/audit-logs", headers=auditor_headers)
    assert response_logs.status_code == 200
    logs = response_logs.json()
    assert len(logs) >= 1
    assert logs[0]["action"] == "VIEW_AUDIT_LOGS"

    # 2. Non-auditor (e.g. Site Investigator) denied access to audit logs
    investigator_headers = get_auth_headers(roles="Site Investigator")
    response_denied = client.get(
        "/api/v1/ctms/audit-logs", headers=investigator_headers
    )
    assert response_denied.status_code == 403


@pytest.mark.asyncio
async def test_database_manager_uninitialized():
    """
    Test exception when getting session maker from an uninitialized CTMS database manager.
    """
    from apps.ctms.database import CTMSDatabaseManager

    mgr = CTMSDatabaseManager()
    with pytest.raises(Exception) as exc_info:
        mgr.get_session_maker()
    assert "not initialized" in str(exc_info.value)
