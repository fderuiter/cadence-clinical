import os

import pytest
import pytest_asyncio
from sqlalchemy import text

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base
from scripts.introspect_pg_schema import generate_typescript_schemas


@pytest_asyncio.fixture
async def local_test_db(tmp_path):
    """Setup and teardown a file-based SQLite test database with GxP triggers."""
    db_file = tmp_path / "test_introspect.db"
    db_url = f"sqlite+aiosqlite:///{db_file}"
    db_manager.init_db(db_url)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        # Import migrate to deploy triggers
        from apps.execution.database.migrate import deploy_database_triggers

        await deploy_database_triggers(conn, "sqlite")

    yield db_manager
    await db_manager.close()


@pytest.mark.asyncio
async def test_trigger_rejects_missing_user_identifier(local_test_db):
    """
    Test that database triggers reject write attempts if the session lacks
    the session-level user identifier (current_user_id is empty or NULL).
    """
    session_maker = local_test_db.get_session_maker()

    async with session_maker() as session:
        conn = await session.connection()
        # Set current_user_id to empty
        await conn.execute(
            text("SELECT set_config('cadence.current_user_id', '', true);")
        )

        # Directly execute raw SQL INSERT to bypass SQLAlchemy's before_flush hook
        # which would otherwise override the empty user_id back to a default 'system'.
        # This emulates a direct external write without an authorized session context.
        with pytest.raises(Exception) as exc_info:
            await conn.execute(
                text(
                    "INSERT INTO clinical_subjects (id, subject_id, study_id, status, version, is_deleted, is_unblinded) VALUES ('test-id', 'SUBJ-002', 'STUDY-XYZ', 'SCREENING', 1, 0, 0);"
                )
            )
            await session.commit()

        assert (
            "GxP Compliance Violation: Write operations lacking session-level user identifiers"
            in str(exc_info.value)
        )


@pytest.mark.asyncio
async def test_trigger_rejects_missing_change_justification_on_update(local_test_db):
    """
    Test that database triggers reject update attempts if the session lacks
    the session-level change justification (current_change_reason is empty or NULL).
    """
    session_maker = local_test_db.get_session_maker()

    # Insert a record first
    async with session_maker() as session:
        conn = await session.connection()
        # Set variables so insert succeeds
        await conn.execute(
            text("SELECT set_config('cadence.current_user_id', 'test-user', true);")
        )
        await conn.execute(
            text(
                "SELECT set_config('cadence.current_change_reason', 'initial insert', true);"
            )
        )
        await conn.execute(
            text(
                "INSERT INTO clinical_subjects (id, subject_id, study_id, status, version, is_deleted, is_unblinded) VALUES ('test-id-2', 'SUBJ-003', 'STUDY-XYZ', 'SCREENING', 1, 0, 0);"
            )
        )
        await session.commit()

    # Now attempt an update with user_id but empty change justification
    async with session_maker() as session:
        conn = await session.connection()
        await conn.execute(
            text("SELECT set_config('cadence.current_user_id', 'test-user', true);")
        )
        await conn.execute(
            text("SELECT set_config('cadence.current_change_reason', '', true);")
        )

        with pytest.raises(Exception) as exc_info:
            await conn.execute(
                text(
                    "UPDATE clinical_subjects SET status = 'ACTIVE' WHERE id = 'test-id-2';"
                )
            )
            await session.commit()

        assert (
            "GxP Compliance Violation: Write operations lacking session-level change justification"
            in str(exc_info.value)
        )


@pytest.mark.asyncio
async def test_introspection_engine_excludes_compliance_tables(local_test_db, tmp_path):
    """
    Test that the schema introspection engine correctly filters out
    internal/compliance-only tables and writes clean clinical interfaces.
    """
    output_file = tmp_path / "db_schemas.ts"

    # Use the test file-based SQLite connection string
    db_url = local_test_db.engine.url.render_as_string(hide_password=False)

    # Run the introspection engine
    success = generate_typescript_schemas(db_url, str(output_file))
    assert success is True
    assert output_file.exists()

    content = output_file.read_text()

    # 1. Clinical models MUST be present
    assert "export interface ClinicalSubject" in content
    assert "export interface ClinicalVisit" in content
    assert "export interface ClinicalObservation" in content
    assert "export interface ClinicalQuery" in content

    # 2. Audit/compliance internal tables MUST NOT be exported
    assert "export interface AuditLog" not in content
    assert "export interface AuditLedgerSeal" not in content
    assert "export interface IntegrationOutbox" not in content

    # 3. Guard against production run
    with pytest.raises(PermissionError):
        os.environ["APP_ENV"] = "production"
        try:
            generate_typescript_schemas(
                "postgresql://prod-db/clinical", str(output_file)
            )
        finally:
            os.environ["APP_ENV"] = "development"
