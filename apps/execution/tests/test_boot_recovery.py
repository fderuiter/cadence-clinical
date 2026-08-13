import os
from collections.abc import AsyncGenerator

import pytest
import pytest_asyncio
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    DictionaryImportJob,
    DictionaryType,
    ImportState,
)
from apps.execution.main import recover_orphaned_dictionary_imports


@pytest_asyncio.fixture(autouse=True)
async def setup_db() -> AsyncGenerator[None]:
    from apps.execution.database.migrate import deploy_database_triggers

    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        ),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_recover_orphaned_dictionary_imports() -> None:
    """Verify that recover_orphaned_dictionary_imports transitions active jobs to FAILED and creates audit trails."""
    # 1. Seed different types of dictionary import jobs
    async with db_manager.get_session_maker()() as session, session.begin():
        job_pending = DictionaryImportJob(
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="26.0",
            status=ImportState.PENDING,
            progress_percentage=0,
            records_imported=0,
        )
        job_processing = DictionaryImportJob(
            dictionary_type=DictionaryType.WHODRUG,
            dictionary_version="2024-03",
            status=ImportState.PROCESSING,
            progress_percentage=50,
            records_imported=100,
        )
        job_completed = DictionaryImportJob(
            dictionary_type=DictionaryType.MEDDRA,
            dictionary_version="25.0",
            status=ImportState.COMPLETED,
            progress_percentage=100,
            records_imported=500,
        )
        job_failed = DictionaryImportJob(
            dictionary_type=DictionaryType.WHODRUG,
            dictionary_version="2023-09",
            status=ImportState.FAILED,
            progress_percentage=10,
            records_imported=5,
        )
        session.add_all([job_pending, job_processing, job_completed, job_failed])
        await session.flush()

        id_pending = job_pending.id
        id_processing = job_processing.id
        id_completed = job_completed.id
        id_failed = job_failed.id

    # 2. Run the boot recovery scan
    await recover_orphaned_dictionary_imports(db_manager.get_session_maker())

    # Verify zero connection leakage (no connections remain checked out)
    if hasattr(db_manager.engine.sync_engine.pool, "checkedout"):
        assert db_manager.engine.sync_engine.pool.checkedout() == 0

    # 3. Assert states
    async with db_manager.get_session_maker()() as session:
        # Check transitioned jobs
        res = await session.execute(
            select(DictionaryImportJob).where(DictionaryImportJob.id == id_pending)
        )
        job = res.scalar_one()
        assert job.status == ImportState.FAILED
        assert job.completed_at is not None
        assert "interrupted by a server reboot" in job.error_details

        res = await session.execute(
            select(DictionaryImportJob).where(DictionaryImportJob.id == id_processing)
        )
        job = res.scalar_one()
        assert job.status == ImportState.FAILED
        assert job.completed_at is not None
        assert "interrupted by a server reboot" in job.error_details

        # Check unmodified jobs
        res = await session.execute(
            select(DictionaryImportJob).where(DictionaryImportJob.id == id_completed)
        )
        job = res.scalar_one()
        assert job.status == ImportState.COMPLETED

        res = await session.execute(
            select(DictionaryImportJob).where(DictionaryImportJob.id == id_failed)
        )
        job = res.scalar_one()
        assert job.status == ImportState.FAILED

        # 4. Check audit logs to verify background_service identity as change agent
        res_audit = await session.execute(
            select(AuditLog).where(
                AuditLog.table_name == "dictionary_import_jobs",
                AuditLog.record_id.in_([id_pending, id_processing]),
                AuditLog.action == "UPDATE",
            )
        )
        audit_rows = res_audit.scalars().all()
        assert len(audit_rows) == 2
        for audit in audit_rows:
            assert audit.user_id == "background_service"
            assert "GxP FMEA-aligned boot recovery" in audit.change_reason
