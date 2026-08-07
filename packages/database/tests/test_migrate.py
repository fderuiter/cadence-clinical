import contextlib
from unittest.mock import AsyncMock, patch

import pytest

from apps.execution.database.migrate import main, run_migrations


@pytest.mark.asyncio
async def test_run_migrations_success():
    from unittest.mock import MagicMock

    with patch(
        "apps.execution.database.migrate.create_async_engine"
    ) as mock_create_engine:
        mock_engine = AsyncMock()
        mock_create_engine.return_value = mock_engine

        # Mock engine.begin() context manager
        class MockBegin:
            async def __aenter__(self):
                self.conn = AsyncMock()
                mock_result = MagicMock()
                mock_result.fetchall.return_value = []
                mock_result.all.return_value = []
                mock_result.scalar.return_value = None
                self.conn.execute.return_value = mock_result
                return self.conn

            async def __aexit__(self, exc_type, exc, tb):
                pass

        mock_engine.begin = MagicMock(return_value=MockBegin())

        await run_migrations("sqlite+aiosqlite:///:memory:")

        mock_create_engine.assert_called_once()
        mock_engine.begin.assert_called_once()
        mock_engine.dispose.assert_called_once()


@pytest.mark.asyncio
async def test_run_migrations_failure():
    from unittest.mock import MagicMock

    with patch(
        "apps.execution.database.migrate.create_async_engine"
    ) as mock_create_engine:
        mock_engine = AsyncMock()
        mock_create_engine.return_value = mock_engine

        class MockBeginFail:
            async def __aenter__(self):
                raise Exception("DB Error")

            async def __aexit__(self, exc_type, exc, tb):
                pass

        mock_engine.begin = MagicMock(return_value=MockBeginFail())

        with patch("sys.exit") as mock_exit:
            await run_migrations("sqlite+aiosqlite:///:memory:")
            mock_exit.assert_called_once_with(1)


def test_main_cli():
    # To prevent the "coroutine never awaited" runtime warning, we consume the coroutine in the mock.
    def mock_run_impl(coro):
        with contextlib.suppress(Exception):
            coro.close()

    with (
        patch(
            "apps.execution.database.migrate.asyncio.run", side_effect=mock_run_impl
        ) as mock_run,
        patch("sys.argv", ["migrate.py", "--db-url", "sqlite+aiosqlite:///:memory:"]),
    ):
        main()
        mock_run.assert_called_once()


@pytest.mark.asyncio
async def test_run_migrations_real_sqlite():
    """
    Test executing run_migrations against a real SQLite database
    to ensure full coverage of SQLite trigger generation branches.
    """
    await run_migrations("sqlite+aiosqlite:///:memory:")


@pytest.mark.asyncio
async def test_lab_reference_ranges_evolution():
    """
    Test that lab_reference_ranges schema is evolved correctly with audit columns.

    Requirements: PRD-SYS-001
    """
    from sqlalchemy import inspect, text
    from sqlalchemy.ext.asyncio import create_async_engine

    from apps.execution.database.migrate import upgrade_existing_tables

    db_url = "sqlite+aiosqlite:///:memory:"
    engine = create_async_engine(db_url, echo=False)

    try:
        async with engine.begin() as conn:
            # Create lab_reference_ranges table manually with a subset of columns (no GxP audit columns)
            await conn.execute(
                text(
                    """
                    CREATE TABLE lab_reference_ranges (
                        id VARCHAR(36) PRIMARY KEY,
                        study_id VARCHAR(255) NOT NULL,
                        test_code VARCHAR(100) NOT NULL,
                        test_name VARCHAR(255) NOT NULL,
                        lab_source VARCHAR(50) NOT NULL,
                        site_id VARCHAR(255)
                    );
                    """
                )
            )

            # Let's inspect before migration
            def get_cols(sync_conn):
                insp = inspect(sync_conn)
                return [col["name"] for col in insp.get_columns("lab_reference_ranges")]

            cols_before = await conn.run_sync(get_cols)
            assert "created_at" not in cols_before
            assert "created_by" not in cols_before
            assert "reason_for_change" not in cols_before
            assert "version_index" not in cols_before

            # Now run the upgrade process
            await upgrade_existing_tables(conn)

            # Check after migration
            cols_after = await conn.run_sync(get_cols)
            assert "created_at" in cols_after
            assert "created_by" in cols_after
            assert "reason_for_change" in cols_after
            assert "version_index" in cols_after

    finally:
        await engine.dispose()


@pytest.mark.asyncio
async def test_new_tables_metadata_creation():
    """
    Test that the new tables are created automatically during Base.metadata.create_all.

    Requirements: PRD-SYS-001
    """
    import os
    import tempfile

    # Create a temporary file name for SQLite with a unique suffix to prevent parallel collisions
    import uuid

    from sqlalchemy import inspect
    from sqlalchemy.ext.asyncio import create_async_engine

    from apps.execution.database.migrate import run_migrations

    temp_dir = tempfile.gettempdir()
    db_file = os.path.join(temp_dir, f"test_migrate_temp_{uuid.uuid4().hex}.db")
    if os.path.exists(db_file):
        os.remove(db_file)

    db_url = f"sqlite+aiosqlite:///{db_file}"
    try:
        await run_migrations(db_url)

        engine = create_async_engine(db_url, echo=False)
        try:
            async with engine.begin() as conn:

                def check_tables(sync_conn):
                    insp = inspect(sync_conn)
                    return insp.get_table_names()

                tables = await conn.run_sync(check_tables)
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables
        finally:
            await engine.dispose()
    finally:
        if os.path.exists(db_file):
            os.remove(db_file)


def test_placeholders():
    from apps.execution.database import provision_tenant, rollback

    provision_tenant.main()
    rollback.main()
