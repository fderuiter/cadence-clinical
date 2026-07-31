import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import (
    deploy_database_triggers,
    upgrade_existing_tables,
)
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    LabReferenceRange,
)
from apps.execution.trial_lock import TrialLockManager


@pytest.fixture(autouse=True)
async def setup_test_db():
    """Initializes and tears down the test database for lab reference range verification."""
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
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
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_lab_reference_range_crud_and_precision():
    # @req:PRD-LAB-001
    """
    Verify CRUD operations, numeric precision, and metadata storage
    for the LabReferenceRange model in the Relational DB.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Silence DB triggers to let SQLAlchemy listener record the audit log
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            # Create a reference range with fractional age and normal bounds to test numeric precision
            lab_range = LabReferenceRange(
                study_id="STUDY-123",
                test_code="WBC",
                test_name="White Blood Cell Count",
                source="CENTRAL",
                site_id=None,
                unit="10^9/L",
                normalized_unit="10^9/L",
                sex_applicability="ALL",
                age_low=18.5,
                age_high=120.0,
                low_bound=4.512345,
                high_bound=11.098765,
                critical_low=2.0,
                critical_high=20.0,
            )
            session.add(lab_range)

    # Retrieve and verify attributes and precise bounds
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabReferenceRange).where(LabReferenceRange.study_id == "STUDY-123")
        )
        saved_range = result.scalar_one()

        assert saved_range.test_code == "WBC"
        assert saved_range.test_name == "White Blood Cell Count"
        assert saved_range.source == "CENTRAL"
        assert saved_range.lab_source == "CENTRAL"
        assert saved_range.site_id is None
        assert saved_range.unit == "10^9/L"
        assert saved_range.normalized_unit == "10^9/L"
        assert saved_range.sex_applicability == "ALL"
        assert saved_range.sex == "ALL"

        # Assert full precision of floats is maintained
        assert saved_range.age_low == 18.5
        assert saved_range.age_high == 120.0
        assert saved_range.low_bound == 4.512345
        assert saved_range.range_low == 4.512345
        assert saved_range.high_bound == 11.098765
        assert saved_range.range_high == 11.098765
        assert saved_range.critical_low == 2.0
        assert saved_range.critical_high == 20.0
        assert saved_range.version == 1
        assert saved_range.is_deleted is False


@pytest.mark.asyncio
async def test_lab_reference_range_audit_and_triggers():
    # @req:PRD-LAB-001
    """
    Verify that updates, soft-deletes, and audit log generation
    for LabReferenceRange adhere to 21 CFR Part 11 requirements.
    """
    range_id = None
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            lab_range = LabReferenceRange(
                study_id="STUDY-123",
                test_code="RBC",
                test_name="Red Blood Cell Count",
                source="LOCAL",
                site_id="SITE-01",
                unit="10^12/L",
                normalized_unit="10^12/L",
                sex_applicability="F",
                age_low=0.0,
                age_high=99.0,
                low_bound=4.0,
                high_bound=5.2,
            )
            session.add(lab_range)
            await session.flush()
            range_id = lab_range.id

    # Verify insert audit log exists
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.table_name == "lab_reference_ranges")
        )
        logs = result.scalars().all()
        assert len(logs) == 1
        insert_log = logs[0]
        assert insert_log.action == "INSERT"
        assert insert_log.new_values["test_code"] == "RBC"
        assert insert_log.new_values["lab_source"] == "LOCAL"

    # Update range limits and verify audit tracking
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            )
            saved_range = result.scalar_one()
            saved_range.low_bound = 3.8
            saved_range.high_bound = 5.0

    # Verify update audit log and incremented version
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabReferenceRange).where(LabReferenceRange.id == range_id)
        )
        updated_range = result.scalar_one()
        assert updated_range.version == 2
        assert updated_range.low_bound == 3.8

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_reference_ranges")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 2
        update_log = logs[1]
        assert update_log.action == "UPDATE"
        assert update_log.old_values["range_low"] == 4.0
        assert update_log.new_values["range_low"] == 3.8

    # Soft-delete the reference range and verify state change and audit log action
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            )
            range_to_delete = result.scalar_one()
            range_to_delete.is_deleted = True

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabReferenceRange).where(LabReferenceRange.id == range_id)
        )
        deleted_range = result.scalar_one()
        assert deleted_range.is_deleted is True
        assert deleted_range.version == 3

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_reference_ranges")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 3
        delete_log = logs[2]
        assert delete_log.action == "DELETE"
        assert delete_log.new_values["is_deleted"] is True

    # Attempt to execute a hard delete and verify DB trigger-level prevention
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception, match="Hard deletions are strictly forbidden"
            ):
                await session.execute(
                    text("DELETE FROM lab_reference_ranges WHERE id = :id;").bindparams(
                        id=range_id
                    )
                )


@pytest.mark.asyncio
async def test_clinical_observation_extended_fields():
    # @req:PRD-LAB-001
    """
    Verify that ClinicalObservation allows storage, snapshots, and updates of
    source, site_id, and evaluation snapshot fields.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            obs = ClinicalObservation(
                subject_id="SUBJ-002",
                study_id="STUDY-123",
                domain="LB",
                test_code="ALT",
                test_name="Alanine Aminotransferase",
                value=45.0,
                unit="U/L",
                normalized_value=45.0,
                normalized_unit="U/L",
                lab_source="LOCAL",
                lab_site_id="SITE-A",
                lab_indicator="H",
                lab_out_of_range=True,
                matched_normal_bounds='{"low": 10.0, "high": 40.0}',
                range_indicator="H",
                is_out_of_range=True,
                reference_range_low=10.0,
                reference_range_high=40.0,
            )
            session.add(obs)

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-002"
            )
        )
        saved_obs = result.scalar_one()

        assert saved_obs.lab_source == "LOCAL"
        assert saved_obs.lab_site_id == "SITE-A"
        assert saved_obs.lab_indicator == "H"
        assert saved_obs.lab_out_of_range is True
        assert saved_obs.matched_normal_bounds == '{"low": 10.0, "high": 40.0}'
        assert saved_obs.range_indicator == "H"
        assert saved_obs.is_out_of_range is True
        assert saved_obs.reference_range_low == 10.0
        assert saved_obs.reference_range_high == 40.0


@pytest.mark.asyncio
async def test_schema_evolution_migration_upgrade():
    # @req:PRD-QRY-005
    # @req:PRD-LAB-001
    """
    Test pre-boot schema evolution by constructing a legacy SQLite schema
    without the newly added ClinicalObservation columns, running our
    upgrade_existing_tables function, and verifying the columns are added.
    """
    from sqlalchemy.ext.asyncio import create_async_engine

    # Create an isolated temporary database to simulate pre-existing database evolution
    db_url = "sqlite+aiosqlite:///:memory:"
    temp_engine = create_async_engine(db_url, echo=False)

    # 1. Create clinical_observations table with only legacy columns (omitting SDV columns)
    # We define a minimal model representing the legacy table schema.
    async with temp_engine.begin() as conn:
        # Create legacy table schema manually
        await conn.execute(
            text("""
            CREATE TABLE clinical_observations (
                id VARCHAR(36) PRIMARY KEY,
                version INTEGER,
                is_deleted BOOLEAN,
                subject_id VARCHAR(255),
                study_id VARCHAR(255),
                visit_id VARCHAR(255),
                domain VARCHAR(50),
                observation_date TIMESTAMP,
                test_code VARCHAR(100),
                test_name VARCHAR(255),
                value FLOAT,
                value_string VARCHAR(255),
                unit VARCHAR(50),
                normalized_value FLOAT,
                normalized_unit VARCHAR(50),
                is_outlier BOOLEAN
            );
        """)
        )

        # Seed at least one legacy row into this table so backfill behavior can be asserted
        await conn.execute(
            text("""
            INSERT INTO clinical_observations (
                id, version, is_deleted, subject_id, study_id, visit_id, domain, observation_date, test_code, test_name, value, unit
            ) VALUES (
                'legacy-obs-id-1', 1, 0, 'SUBJ-001', 'STUDY-123', 'VISIT-001', 'LB', '2026-08-14 12:00:00', 'ALT', 'Alanine Aminotransferase', 45.0, 'U/L'
            );
            """)
        )

    # 2. Run our upgrade_existing_tables function
    async with temp_engine.begin() as conn:
        await upgrade_existing_tables(conn)

    # 3. Verify that the new snapshot/source and SDV columns were successfully added
    async with temp_engine.begin() as conn:

        def get_columns(sync_conn):
            from sqlalchemy import inspect

            insp = inspect(sync_conn)
            return [col["name"] for col in insp.get_columns("clinical_observations")]

        updated_cols = await conn.run_sync(get_columns)

        expected_added_cols = [
            "lab_source",
            "lab_site_id",
            "lab_indicator",
            "lab_out_of_range",
            "matched_normal_bounds",
            "site_id",
            "is_sdv_verified",
            "sdv_verified_by",
            "sdv_verified_at",
            "page_id",
            "range_indicator",
            "is_out_of_range",
            "reference_range_low",
            "reference_range_high",
        ]
        for col in expected_added_cols:
            assert col in updated_cols, (
                f"Column {col} should have been added by migration."
            )

        # Assert backfilled default values on the seeded row
        res = await conn.execute(
            text(
                "SELECT is_sdv_verified, sdv_verified_by, sdv_verified_at, page_id FROM clinical_observations WHERE id = 'legacy-obs-id-1';"
            )
        )
        row = res.fetchone()
        assert row is not None
        assert row[0] is False or row[0] == 0
        assert row[1] is None
        assert row[2] is None
        assert row[3] is None

    # 4. Assert idempotency by running the upgrade a second time
    async with temp_engine.begin() as conn:
        await upgrade_existing_tables(conn)
        updated_cols_2 = await conn.run_sync(get_columns)
        for col in expected_added_cols:
            assert col in updated_cols_2, (
                f"Column {col} should remain intact after repeat migration."
            )

    # 5. Assert SDV write compatibility by performing an update on the upgraded table
    async with temp_engine.begin() as conn:
        await conn.execute(
            text("""
                UPDATE clinical_observations
                SET is_sdv_verified = :is_verified, sdv_verified_by = :verified_by, sdv_verified_at = :verified_at
                WHERE id = 'legacy-obs-id-1';
            """),
            {
                "is_verified": True,
                "verified_by": "CRA-001",
                "verified_at": "2026-08-14 13:00:00",
            },
        )
        res = await conn.execute(
            text(
                "SELECT is_sdv_verified, sdv_verified_by, sdv_verified_at FROM clinical_observations WHERE id = 'legacy-obs-id-1';"
            )
        )
        row = res.fetchone()
        assert row is not None
        assert row[0] is True or row[0] == 1
        assert row[1] == "CRA-001"
        assert row[2] is not None

    await temp_engine.dispose()
