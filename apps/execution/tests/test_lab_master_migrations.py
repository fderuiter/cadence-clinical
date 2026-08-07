"""Tests for Lab Master and Lab Unit Conversion migrations and schema.

Requirements: PRD-SYS-001
"""

import os
import tempfile

import pytest
from sqlalchemy import inspect
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
                        col["name"] for col in insp.get_columns("lab_test_masters")
                    ]
                    cols_conversion = [
                        col["name"] for col in insp.get_columns("lab_unit_conversions")
                    ]
                    return tables, cols_ref, cols_master, cols_conversion

                tables, cols_ref, cols_master, cols_conversion = await conn.run_sync(
                    get_tables_and_columns
                )

                # Assert that the tables are created
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables
                assert "lab_reference_ranges" in tables

                # Assert that lab_reference_ranges has the new GxP audit columns
                assert "created_at" in cols_ref
                assert "created_by" in cols_ref
                assert "reason_for_change" in cols_ref
                assert "version_index" in cols_ref

                # Assert that lab_test_masters has correct columns
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


@pytest.mark.asyncio
async def test_migration_upgrade_and_idempotency_explicit():
    """
    Task 3: Confirm the migration creates the catalog tables and backfills the reference-range audit columns.
    Assert run_migrations() creates lab_test_masters and lab_unit_conversions with version_index,
    and that lab_reference_ranges gains created_at, created_by, reason_for_change, and version_index.
    Assert that the migration is idempotent.
    """
    from sqlalchemy import text

    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as tmp:
        db_path = tmp.name

    try:
        db_url = f"sqlite+aiosqlite:///{db_path}"
        engine = create_async_engine(db_url)

        # 1. Create legacy schema for lab_reference_ranges
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE lab_reference_ranges (
                    id VARCHAR(36) PRIMARY KEY,
                    study_id VARCHAR(255) NOT NULL,
                    test_code VARCHAR(100) NOT NULL,
                    test_name VARCHAR(255) NOT NULL,
                    lab_source VARCHAR(50) NOT NULL,
                    site_id VARCHAR(255),
                    unit VARCHAR(50),
                    normalized_unit VARCHAR(50),
                    sex VARCHAR(50),
                    age_low FLOAT,
                    age_high FLOAT,
                    range_low FLOAT,
                    range_high FLOAT,
                    critical_low FLOAT,
                    critical_high FLOAT,
                    version INTEGER,
                    is_deleted BOOLEAN
                );
                """)
            )
            # Insert a legacy row
            await conn.execute(
                text("""
                INSERT INTO lab_reference_ranges (
                    id, study_id, test_code, test_name, lab_source, version, is_deleted
                ) VALUES (
                    'legacy-r-1', 'STUDY-GX-9', 'WBC', 'White Blood Cells', 'CENTRAL', 1, 0
                );
                """)
            )
        await engine.dispose()

        # 2. Run migrations first time
        await run_migrations(db_url)

        # 3. Assert schema gains columns and creates tables
        engine = create_async_engine(db_url)
        try:
            async with engine.connect() as conn:

                def get_columns_explicit(sync_conn):
                    insp = inspect(sync_conn)
                    tables = insp.get_table_names()
                    ref_cols = [
                        c["name"] for c in insp.get_columns("lab_reference_ranges")
                    ]
                    master_cols = [
                        c["name"] for c in insp.get_columns("lab_test_masters")
                    ]
                    conv_cols = [
                        c["name"] for c in insp.get_columns("lab_unit_conversions")
                    ]
                    return tables, ref_cols, master_cols, conv_cols

                tables, ref_cols, master_cols, conv_cols = await conn.run_sync(
                    get_columns_explicit
                )

                # Assert tables exist
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables

                # Assert audit columns in lab_reference_ranges
                assert "created_at" in ref_cols
                assert "created_by" in ref_cols
                assert "reason_for_change" in ref_cols
                assert "version_index" in ref_cols

                # Assert version_index in both catalog tables
                assert "version_index" in master_cols
                assert "version_index" in conv_cols

                # Assert legacy row gained defaulted version_index=1
                row = (
                    await conn.execute(
                        text(
                            "SELECT version_index FROM lab_reference_ranges WHERE id = 'legacy-r-1';"
                        )
                    )
                ).fetchone()
                assert row is not None
                assert row[0] == 1

        finally:
            await engine.dispose()

        # 4. Run migrations second time (idempotency check)
        await run_migrations(db_url)

        # 5. Assert idempotency (did not fail, columns did not duplicate)
        engine = create_async_engine(db_url)
        try:
            async with engine.connect() as conn:
                tables, ref_cols, master_cols, conv_cols = await conn.run_sync(
                    get_columns_explicit
                )
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables

                # Check exact count of version_index column (it should be exactly 1, not duplicate)
                assert ref_cols.count("version_index") == 1
                assert master_cols.count("version_index") == 1
                assert conv_cols.count("version_index") == 1
        finally:
            await engine.dispose()

    finally:
        if os.path.exists(db_path):
            os.remove(db_path)


@pytest.mark.asyncio
async def test_lab_reference_range_migration_upgrade_and_idempotency():
    """
    Assert that migration upgrade adds columns to lab_reference_ranges,
    creates lab_test_masters and lab_unit_conversions with version_index, and is idempotent.
    """
    from sqlalchemy import text

    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as tmp:
        db_path = tmp.name

    try:
        db_url = f"sqlite+aiosqlite:///{db_path}"
        engine = create_async_engine(db_url)

        # 1. Manually create a legacy lab_reference_ranges table without GxP columns
        async with engine.begin() as conn:
            await conn.execute(
                text("""
                CREATE TABLE lab_reference_ranges (
                    id VARCHAR(36) PRIMARY KEY,
                    study_id VARCHAR(255) NOT NULL,
                    test_code VARCHAR(100) NOT NULL,
                    test_name VARCHAR(255) NOT NULL,
                    lab_source VARCHAR(50) NOT NULL,
                    site_id VARCHAR(255),
                    unit VARCHAR(50),
                    normalized_unit VARCHAR(50),
                    sex VARCHAR(50),
                    age_low FLOAT,
                    age_high FLOAT,
                    range_low FLOAT,
                    range_high FLOAT,
                    critical_low FLOAT,
                    critical_high FLOAT,
                    version INTEGER,
                    is_deleted BOOLEAN
                );
                """)
            )
            # Insert a legacy row
            await conn.execute(
                text("""
                INSERT INTO lab_reference_ranges (
                    id, study_id, test_code, test_name, lab_source, version, is_deleted
                ) VALUES (
                    'range-legacy-1', 'STUDY-123', 'WBC', 'White Blood Cells', 'CENTRAL', 1, 0
                );
                """)
            )
        await engine.dispose()

        # 2. Run run_migrations()
        await run_migrations(db_url)

        # 3. Connect and verify the upgrade
        engine = create_async_engine(db_url)
        try:
            async with engine.connect() as conn:

                def inspect_db(sync_conn):
                    insp = inspect(sync_conn)
                    tables = insp.get_table_names()
                    cols_ref = [
                        col["name"] for col in insp.get_columns("lab_reference_ranges")
                    ]
                    cols_master = [
                        col["name"] for col in insp.get_columns("lab_test_masters")
                    ]
                    cols_conversion = [
                        col["name"] for col in insp.get_columns("lab_unit_conversions")
                    ]
                    return tables, cols_ref, cols_master, cols_conversion

                tables, cols_ref, cols_master, cols_conversion = await conn.run_sync(
                    inspect_db
                )

                # Assert tables exist
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables
                assert "lab_reference_ranges" in tables

                # Assert lab_reference_ranges has gained GxP columns
                assert "created_at" in cols_ref
                assert "created_by" in cols_ref
                assert "reason_for_change" in cols_ref
                assert "version_index" in cols_ref

                # Assert that lab_test_masters has correct columns, including version_index
                assert "version_index" in cols_master

                # Assert that lab_unit_conversions has correct columns, including version_index
                assert "version_index" in cols_conversion

                # Check backfilled values of legacy row
                res = await conn.execute(
                    text(
                        "SELECT version_index FROM lab_reference_ranges WHERE id = 'range-legacy-1';"
                    )
                )
                row = res.fetchone()
                assert row is not None
                # Since we added INTEGER DEFAULT 1, version_index should be 1
                assert row[0] == 1

        finally:
            await engine.dispose()

        # 4. Run run_migrations() a second time to assert idempotency
        await run_migrations(db_url)

        # 5. Connect and verify nothing changed/duplicated and no errors
        engine = create_async_engine(db_url)
        try:
            async with engine.connect() as conn:
                tables, cols_ref, cols_master, cols_conversion = await conn.run_sync(
                    inspect_db
                )
                assert "lab_test_masters" in tables
                assert "lab_unit_conversions" in tables
                assert "lab_reference_ranges" in tables

                # Verify exact same GxP columns exist
                assert "created_at" in cols_ref
                assert "created_by" in cols_ref
                assert "reason_for_change" in cols_ref
                assert "version_index" in cols_ref

                # Check backfilled legacy row still intact
                res = await conn.execute(
                    text(
                        "SELECT version_index FROM lab_reference_ranges WHERE id = 'range-legacy-1';"
                    )
                )
                row = res.fetchone()
                assert row is not None
                assert row[0] == 1
        finally:
            await engine.dispose()

    finally:
        if os.path.exists(db_path):
            os.remove(db_path)
