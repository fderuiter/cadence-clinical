"""Tests for Lab Master and Lab Unit Conversion migrations and schema.

Requirements: PRD-SYS-001
"""

import os
import tempfile
import pytest
from sqlalchemy import text, inspect
from sqlalchemy.ext.asyncio import create_async_engine
from apps.execution.database.migrate import run_migrations


@pytest.mark.asyncio
async def test_lab_master_migrations():
    """
    Verify that executing migrations correctly adds GxP columns to existing
    tables and creates the new lab_test_masters and lab_unit_conversions tables.
    """
    # Use a temporary file for the SQLite database so that it persists across connections
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as tmp:
        db_path = tmp.name

    try:
        db_url = f"sqlite+aiosqlite:///{db_path}"

        # Run the migrations to initialize and upgrade the database
        await run_migrations(db_url)

        engine = create_async_engine(db_url)
        try:
            async with engine.connect() as conn:

                def get_tables_and_columns(sync_conn):
                    insp = inspect(sync_conn)
                    tables = insp.get_table_names()
                    cols_ref = [
                        col["name"] for col in insp.get_columns("lab_reference_ranges")
                    ]
                    cols_master = [
                        col["name"] for col in insp.get_columns("lab_test_master")
                    ]
                    cols_conversion = [
                        col["name"] for col in insp.get_columns("lab_unit_conversions")
                    ]
                    return tables, cols_ref, cols_master, cols_conversion

                tables, cols_ref, cols_master, cols_conversion = await conn.run_sync(
                    get_tables_and_columns
                )

                # Assert that the tables are created
                assert "lab_test_master" in tables
                assert "lab_unit_conversions" in tables
                assert "lab_reference_ranges" in tables

                # Assert that lab_reference_ranges has the new GxP audit columns
                assert "created_at" in cols_ref
                assert "created_by" in cols_ref
                assert "reason_for_change" in cols_ref
                assert "version_index" in cols_ref

                # Assert that lab_test_master has correct columns
                assert "study_id" in cols_master
                assert "test_code" in cols_master
                assert "test_name" in cols_master
                assert "default_unit" in cols_master
                assert "normalized_unit" in cols_master
                assert "loinc_code" in cols_master
                assert "version_index" in cols_master

                # Assert that lab_unit_conversions has correct columns
                assert "study_id" in cols_conversion
                assert "test_code" in cols_conversion
                assert "from_unit" in cols_conversion
                assert "to_unit" in cols_conversion
                assert "factor" in cols_conversion
                assert "offset" in cols_conversion
                assert "version_index" in cols_conversion
        finally:
            await engine.dispose()
    finally:
        if os.path.exists(db_path):
            os.remove(db_path)
