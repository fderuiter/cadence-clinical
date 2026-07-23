import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditLog, Base, TranslationJob
from apps.execution.translator import (
    poll_and_process_jobs,
    recover_active_jobs,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_recover_active_jobs():
    """Verify that any jobs left in "PROCESSING" status are reset to "PENDING" on startup."""
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        async with session.begin():
            # Setup an active job stuck in PROCESSING, and a completed one
            job_stuck = TranslationJob(
                study_id="stuck_study",
                status="PROCESSING",
                payload={"name": "Stuck"},
            )
            job_done = TranslationJob(
                study_id="done_study",
                status="COMPLETED",
                payload={"name": "Done"},
            )
            session.add_all([job_stuck, job_done])

    # Run recovery
    await recover_active_jobs()

    # Assert that the stuck job was reset and completed job was left unchanged
    async with session_maker() as session:
        res_stuck = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "stuck_study"
            )
        )
        j_stuck = res_stuck.mappings().first()
        assert j_stuck["status"] == "PENDING"
        assert "Recovered" in j_stuck["error_message"]

        res_done = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "done_study"
            )
        )
        j_done = res_done.mappings().first()
        assert j_done["status"] == "COMPLETED"


@pytest.mark.asyncio
async def test_poll_and_process_jobs_success():
    """Verify that a pending job is picked up by the polling process and processed successfully."""
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        async with session.begin():
            job = TranslationJob(
                study_id="test_poll_success",
                status="PENDING",
                payload={
                    "name": "Acme Polled Trial",
                    "protocol": {
                        "items": [
                            {"id": "q1", "name": "Question 1", "type": "string"},
                        ]
                    },
                },
            )
            session.add(job)

    # Execute polling
    processed = await poll_and_process_jobs()
    assert processed is True

    # Assert that the job is now COMPLETED and has payloads
    async with session_maker() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "test_poll_success"
            )
        )
        j = result.mappings().first()
        assert j["status"] == "COMPLETED"
        assert j["odm_payload"] is not None
        assert j["openrosa_payload"] is not None

        # Verify audit log was created for state changes
        audit_res = await session.execute(
            AuditLog.__table__.select().where(AuditLog.table_name == "translation_jobs")
        )
        logs = list(audit_res.mappings().all())
        # There should be an INSERT (status=PENDING), an UPDATE (status=PROCESSING), and an UPDATE (status=COMPLETED)
        assert len(logs) >= 3


@pytest.mark.asyncio
async def test_poll_and_process_jobs_failure():
    """Verify that a pending job with invalid payload is picked up and transitioned to FAILED status."""
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        async with session.begin():
            job = TranslationJob(
                study_id="test_poll_failure",
                status="PENDING",
                payload={"name": "Invalid Polled Trial"},  # missing protocol
            )
            session.add(job)

    # Execute polling
    processed = await poll_and_process_jobs()
    assert processed is True

    # Assert that the job is now FAILED and has an error message
    async with session_maker() as session:
        result = await session.execute(
            TranslationJob.__table__.select().where(
                TranslationJob.study_id == "test_poll_failure"
            )
        )
        j = result.mappings().first()
        assert j["status"] == "FAILED"
        assert "Validation Failed" in j["error_message"]


@pytest.mark.asyncio
async def test_poll_and_process_jobs_empty():
    """Verify that poll_and_process_jobs returns False if there are no pending jobs."""
    processed = await poll_and_process_jobs()
    assert processed is False
