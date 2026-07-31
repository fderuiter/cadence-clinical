import asyncio
import hashlib
import hmac
import json
import os
import time

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from apps.execution.main import app
from apps.execution.translator import process_translation

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")


def get_auth_headers(
    user_id="test_user", roles="admin", change_reason="system_operation"
):
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
async def setup_test_db():
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        )
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_translation_status_and_listing_success():
    """Test standard flow: Post job, retrieve status by ID, and list history."""
    study_payload = {
        "study_id": "test_recovery_study",
        "payload": {
            "name": "Recovery Trial",
            "protocol": {
                "items": [
                    {"id": "q1", "name": "Question 1", "type": "string"},
                ]
            },
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
        assert response.status_code == 200
        res_json = response.json()
        assert res_json["status"] == "accepted"
        assert "job_id" in res_json
        job_id = res_json["job_id"]

        # Wait for the job to complete
        for _ in range(50):
            status_response = await client.get(
                f"/api/v1/execution/translation/jobs/{job_id}",
                headers=get_auth_headers(),
            )
            assert status_response.status_code == 200
            status_json = status_response.json()
            if status_json["status"] in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.1)

        # Verify exact details of completed job
        assert status_json["status"] == "COMPLETED"
        assert status_json["study_id"] == "test_recovery_study"
        assert status_json["odm_payload"] is not None
        assert status_json["openrosa_payload"] is not None
        assert status_json["error_message"] is None

        # Verify listing endpoint
        list_response = await client.get(
            "/api/v1/execution/translation/jobs", headers=get_auth_headers()
        )
        assert list_response.status_code == 200
        list_json = list_response.json()
        assert len(list_json) >= 1
        matching_job = [j for j in list_json if j["id"] == job_id][0]
        assert matching_job["status"] == "COMPLETED"


@pytest.mark.asyncio
async def test_translation_error_status_and_rollback():
    """Test failure flow: invalid study triggers rollback and writes FAILED status.
    # @req:Trace-12
    """
    study_payload = {
        "study_id": "test_failed_study",
        "payload": {
            "name": "Failed Trial"
            # protocol is missing, will trigger ValueError
        },
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        response = await client.post(
            "/events/study-published", json=study_payload, headers=get_auth_headers()
        )
        assert response.status_code == 200
        res_json = response.json()
        job_id = res_json["job_id"]

        # Wait for job to fail
        for _ in range(50):
            status_response = await client.get(
                f"/api/v1/execution/translation/jobs/{job_id}",
                headers=get_auth_headers(),
            )
            assert status_response.status_code == 200
            status_json = status_response.json()
            if status_json["status"] in ("COMPLETED", "FAILED"):
                break
            await asyncio.sleep(0.1)

        # Verify job recorded failed status and details
        assert status_json["status"] == "FAILED"
        assert "protocol" in status_json["error_message"]


@pytest.mark.asyncio
async def test_security_gate_unauthenticated_requests():
    """Verify unauthenticated requests are blocked with authorized signature error."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Single job status with missing headers
        response = await client.get("/api/v1/execution/translation/jobs/some_id")
        assert response.status_code == 401
        assert "Missing gateway authentication headers" in response.json()["detail"]

        # List jobs with missing headers
        response2 = await client.get("/api/v1/execution/translation/jobs")
        assert response2.status_code == 401
        assert "Missing gateway authentication headers" in response2.json()["detail"]


@pytest.mark.asyncio
async def test_worker_context_and_session_cleanup():
    """Verify thread-local database session and security context variables are cleared after processing."""
    # Ensure starting in a clean state
    assert current_session.get() is None
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"

    # Run processing with an invalid payload (will fail)
    await process_translation(
        study_id="failed_cleanup_test",
        payload={"invalid": "data"},
        session_factory=db_manager.get_session_maker(),
        user_id="special_user",
        change_reason="testing_cleanup",
    )

    # Verify context variables and sessions are fully reset even after a failure
    assert current_session.get() is None
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"


@pytest.mark.asyncio
async def test_startup_translation_job_recovery():
    """Verify that translation jobs in PROCESSING status are recovered on startup.

    Requirements: Trace-12
    """
    from sqlalchemy import select

    from apps.execution.database.models import TranslationJob
    from apps.execution.main import recover_translation_jobs

    session_maker = db_manager.get_session_maker()

    # Step 1: Create a mock TranslationJob with "PROCESSING" status
    async with session_maker() as session:
        async with session.begin():
            job = TranslationJob(
                id="stuck_job_1", study_id="test_stuck_study", status="PROCESSING"
            )
            session.add(job)

    # Step 2: Trigger the recovery routine
    await recover_translation_jobs(session_maker)

    # Step 3: Assert that the job has been recovered and status set to FAILED with message
    async with session_maker() as session:
        stmt = select(TranslationJob).where(TranslationJob.id == "stuck_job_1")
        result = await session.execute(stmt)
        recovered_job = result.scalar_one_or_none()

        assert recovered_job is not None
        assert recovered_job.status == "FAILED"
        assert "interrupted by a system restart" in recovered_job.error_message


@pytest.mark.asyncio
async def test_startup_translation_job_recovery_performance():
    """Verify that the recovery database query completes in under 2 seconds under a load of 5000 legacy records.

    Requirements: Trace-12
    """
    import time

    from sqlalchemy import select

    from apps.execution.database.models import TranslationJob
    from apps.execution.main import recover_translation_jobs

    session_maker = db_manager.get_session_maker()

    # Generate 5,000 legacy records to simulate load
    # Insert them efficiently inside one transaction
    async with session_maker() as session:
        async with session.begin():
            # Let's insert a mix: some PROCESSING, some COMPLETED
            for i in range(5000):
                status = "PROCESSING" if i % 10 == 0 else "COMPLETED"
                job = TranslationJob(
                    id=f"perf_job_{i}", study_id=f"study_{i}", status=status
                )
                session.add(job)

    # Measure recovery execution time
    start_time = time.perf_counter()
    await recover_translation_jobs(session_maker)
    end_time = time.perf_counter()
    duration = end_time - start_time

    # Assert duration is well under 2 seconds
    assert duration < 2.0, f"Recovery took too long: {duration:.2f} seconds"

    # Verify that PROCESSING records were transitioned, and COMPLETED records were untouched
    async with session_maker() as session:
        # Check a sample of PROCESSING record
        result = await session.execute(
            select(TranslationJob).where(TranslationJob.id == "perf_job_0")
        )
        job_0 = result.scalar_one_or_none()
        assert job_0 is not None
        assert job_0.status == "FAILED"
        assert "interrupted by a system restart" in job_0.error_message

        # Check a sample of COMPLETED record
        result = await session.execute(
            select(TranslationJob).where(TranslationJob.id == "perf_job_1")
        )
        job_1 = result.scalar_one_or_none()
        assert job_1 is not None
        assert job_1.status == "COMPLETED"
        assert job_1.error_message is None
