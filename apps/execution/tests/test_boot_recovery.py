from collections.abc import AsyncGenerator
from datetime import UTC, datetime

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.boot_recovery import run_boot_recovery
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    DictionaryImportJob,
    ImportState,
)
from apps.execution.database.models import DictionaryType as DBDictionaryType


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None]:
    """Setup in-memory SQLite database before each test and clear down after."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_boot_recovery_transitions_active_jobs() -> None:
    """Verify that orphaned dictionary import jobs (PENDING/PROCESSING) are transitioned to FAILED.

    Also verifies that completed or already failed jobs are not touched, and that GxP audit trail
    correctly lists 'boot_recovery_service' as the change agent.
    """
    session_maker = db_manager.get_session_maker()

    # Create test jobs in various states
    async with session_maker() as session, session.begin():
        job_pending = DictionaryImportJob(
            dictionary_type=DBDictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=ImportState.PENDING,
            started_at=datetime.now(UTC).replace(tzinfo=None),
        )
        job_processing = DictionaryImportJob(
            dictionary_type=DBDictionaryType.WHODRUG,
            dictionary_version="2023-03",
            status=ImportState.PROCESSING,
            started_at=datetime.now(UTC).replace(tzinfo=None),
        )
        job_completed = DictionaryImportJob(
            dictionary_type=DBDictionaryType.MEDDRA,
            dictionary_version="25.1",
            status=ImportState.COMPLETED,
            started_at=datetime.now(UTC).replace(tzinfo=None),
            completed_at=datetime.now(UTC).replace(tzinfo=None),
        )
        job_failed = DictionaryImportJob(
            dictionary_type=DBDictionaryType.WHODRUG,
            dictionary_version="2022-09",
            status=ImportState.FAILED,
            started_at=datetime.now(UTC).replace(tzinfo=None),
            completed_at=datetime.now(UTC).replace(tzinfo=None),
            error_details="Original import failed",
        )
        session.add_all([job_pending, job_processing, job_completed, job_failed])

    # Execute boot recovery
    await run_boot_recovery(session_maker)

    # Verify states after recovery scan
    async with session_maker() as session:
        # Check job_pending -> FAILED
        stmt = select(DictionaryImportJob).where(
            DictionaryImportJob.dictionary_version == "26.0"
        )
        res = await session.execute(stmt)
        job1 = res.scalar_one()
        assert job1.status == ImportState.FAILED
        assert "boot recovery scan" in job1.error_details
        assert job1.completed_at is not None

        # Check job_processing -> FAILED
        stmt = select(DictionaryImportJob).where(
            DictionaryImportJob.dictionary_version == "2023-03"
        )
        res = await session.execute(stmt)
        job2 = res.scalar_one()
        assert job2.status == ImportState.FAILED
        assert "boot recovery scan" in job2.error_details
        assert job2.completed_at is not None

        # Check job_completed is still COMPLETED
        stmt = select(DictionaryImportJob).where(
            DictionaryImportJob.dictionary_version == "25.1"
        )
        res = await session.execute(stmt)
        job3 = res.scalar_one()
        assert job3.status == ImportState.COMPLETED

        # Check job_failed is still FAILED with original error details
        stmt = select(DictionaryImportJob).where(
            DictionaryImportJob.dictionary_version == "2022-09"
        )
        res = await session.execute(stmt)
        job4 = res.scalar_one()
        assert job4.status == ImportState.FAILED
        assert job4.error_details == "Original import failed"

        # Verify Audit Trails in database
        stmt_audit = select(AuditLog).where(
            AuditLog.table_name == "dictionary_import_jobs", AuditLog.action == "UPDATE"
        )
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()

        # We transitioned 2 jobs (pending and processing), so there should be 2 UPDATE audit logs
        assert len(audit_logs) == 2
        for audit in audit_logs:
            assert audit.user_id == "boot_recovery_service"
            assert (
                "boot_recovery_service" in audit.change_reason
                or "Boot Recovery" in audit.change_reason
            )


@pytest.mark.asyncio
async def test_boot_recovery_no_jobs() -> None:
    """Verify boot recovery execution when zero active dictionary import jobs are found."""
    session_maker = db_manager.get_session_maker()

    # Create no active jobs, only a completed one
    async with session_maker() as session, session.begin():
        job_completed = DictionaryImportJob(
            dictionary_type=DBDictionaryType.MEDDRA,
            dictionary_version="25.1",
            status=ImportState.COMPLETED,
            started_at=datetime.now(UTC).replace(tzinfo=None),
            completed_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(job_completed)

    # Execute boot recovery should run without errors
    await run_boot_recovery(session_maker)

    async with session_maker() as session:
        stmt = select(DictionaryImportJob)
        res = await session.execute(stmt)
        jobs = res.scalars().all()
        assert len(jobs) == 1
        assert jobs[0].status == ImportState.COMPLETED


@pytest.mark.asyncio
async def test_boot_recovery_lifespan_integration() -> None:
    """Verify that starting the app with TestClient (which triggers lifespan) executes boot recovery scan."""
    from fastapi.testclient import TestClient

    from apps.execution.main import app

    session_maker = db_manager.get_session_maker()

    # Create a stuck job
    async with session_maker() as session, session.begin():
        job_processing = DictionaryImportJob(
            dictionary_type=DBDictionaryType.MEDDRA,
            dictionary_version="24.0",
            status=ImportState.PROCESSING,
            started_at=datetime.now(UTC).replace(tzinfo=None),
        )
        session.add(job_processing)

    # Triggering lifespan using FastAPI's TestClient
    with TestClient(app) as _:
        # After lifespan triggers, the job should have been transitioned
        pass

    async with session_maker() as session:
        stmt = select(DictionaryImportJob).where(
            DictionaryImportJob.dictionary_version == "24.0"
        )
        res = await session.execute(stmt)
        job = res.scalar_one()
        assert job.status == ImportState.FAILED
