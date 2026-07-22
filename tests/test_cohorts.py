import time

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.database.context import current_session
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import AuditLog, Base, Cohort
from apps.execution.main import app


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers():
    import hashlib
    import hmac
    import os
    import time

    gateway_secret = os.getenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    ).encode()
    user_id = "test_user"
    roles = "admin"
    timestamp = str(time.time())

    message = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(gateway_secret, message.encode(), hashlib.sha256).hexdigest()

    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
    }


@pytest.mark.asyncio
async def test_cohort_status_update_with_justification():
    # Insert cohort
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_cohort():
        session = current_session.get()
        cohort = Cohort(name="Cohort A", status="active", target_enrollment=10)
        session.add(cohort)
        await session.flush()
        return cohort.id

    cohort_id = await create_cohort()

    headers = get_auth_headers()
    headers["X-Change-Reason"] = "Target reached"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.put(
            f"/cohorts/{cohort_id}/status", json={"status": "closed"}, headers=headers
        )

    assert response.status_code == 200
    assert response.json()["new_status"] == "closed"

    # Verify audit
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(
                AuditLog.record_id == cohort_id, AuditLog.action == "UPDATE"
            )
        )
        log = result.scalars().first()
        assert log is not None
        assert log.change_reason == "Target reached"
        assert log.user_id == "test_user"


@pytest.mark.asyncio
async def test_cohort_status_update_without_justification_fails():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_cohort():
        session = current_session.get()
        cohort = Cohort(name="Cohort B", status="active", target_enrollment=10)
        session.add(cohort)
        await session.flush()
        return cohort.id

    cohort_id = await create_cohort()

    headers = get_auth_headers()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.put(
            f"/cohorts/{cohort_id}/status", json={"status": "closed"}, headers=headers
        )

    assert response.status_code == 400
    assert "Missing change justification" in response.json()["detail"]


@pytest.mark.asyncio
async def test_subject_enrollment_closed_cohort_fails():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_cohort():
        session = current_session.get()
        cohort = Cohort(name="Cohort C", status="closed", target_enrollment=10)
        session.add(cohort)
        await session.flush()
        return cohort.id

    cohort_id = await create_cohort()

    headers = get_auth_headers()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/subjects/enroll",
            json={"subject_uid": "sub_1", "cohort_id": cohort_id},
            headers=headers,
        )

    assert response.status_code == 400
    assert "Cohort is not active" in response.json()["detail"]


@pytest.mark.asyncio
async def test_performance_cohort_evaluation():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_cohort():
        session = current_session.get()
        cohort = Cohort(name="Cohort Perf", status="active", target_enrollment=10)
        session.add(cohort)
        await session.flush()
        return cohort.id

    cohort_id = await create_cohort()
    headers = get_auth_headers()

    start_time = time.perf_counter()
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/subjects/enroll",
            json={"subject_uid": "sub_perf", "cohort_id": cohort_id},
            headers=headers,
        )
    end_time = time.perf_counter()
    duration_ms = (end_time - start_time) * 1000

    assert response.status_code == 200
    assert duration_ms < 50
