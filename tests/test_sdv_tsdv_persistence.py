from datetime import datetime

import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    SDVSignOff,
    TSDVConfig,
)
from apps.execution.trial_lock import TrialLockManager


@pytest.fixture(autouse=True)
async def setup_test_db():
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
async def test_clinical_observation_sdv_defaults():
    # @req:PRD-QRY-005
    # @req:PRD-QRY-007
    """
    Verify that ClinicalObservation supports field-level SDV state columns and optional page grouping,
    and defaults correctly for backwards compatibility.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Silence DB triggers to let SQLAlchemy listener record the audit log
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            obs = ClinicalObservation(
                subject_id="SUBJ-001",
                study_id="STUDY-XYZ",
                domain="VS",
                test_code="SYSBP",
                test_name="Systolic Blood Pressure",
                value=120.0,
            )
            session.add(obs)

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-001"
            )
        )
        saved_obs = result.scalar_one()

        # Assert default behavior and nullability allows existing observations to remain valid
        assert saved_obs.is_sdv_verified is False
        assert saved_obs.sdv_verified_by is None
        assert saved_obs.sdv_verified_at is None
        assert saved_obs.page_id is None

        # Modify values and verify persistence
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', 1);")
        )
        saved_obs.is_sdv_verified = True
        saved_obs.sdv_verified_by = "CRA-007"
        saved_obs.sdv_verified_at = datetime(2026, 7, 28, 12, 0, 0)
        saved_obs.page_id = "FORM-VITAL-01"
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(ClinicalObservation).where(
                ClinicalObservation.subject_id == "SUBJ-001"
            )
        )
        updated_obs = result.scalar_one()
        assert updated_obs.is_sdv_verified is True
        assert updated_obs.sdv_verified_by == "CRA-007"
        assert updated_obs.sdv_verified_at == datetime(2026, 7, 28, 12, 0, 0)
        assert updated_obs.page_id == "FORM-VITAL-01"


@pytest.mark.asyncio
async def test_sdv_sign_off_persistence_and_audit():
    # @req:PRD-QRY-005
    """
    Verify that SDVSignOff records aggregate sign-offs, respects defaults,
    inherits from AuditedModel, and registers triggers for audit trail capture.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Silence DB triggers to let SQLAlchemy listener record the audit log
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            sign_off = SDVSignOff(
                scope="PAGE",
                target_id="PAGE-01",
                subject_id="SUBJ-001",
                study_id="STUDY-XYZ",
                is_verified=True,
                verified_by="CRA-123",
                verified_at=datetime(2026, 7, 28, 14, 0, 0),
            )
            session.add(sign_off)

    # Verify audit trail triggered on insert
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.table_name == "sdv_sign_offs")
        )
        logs = result.scalars().all()
        assert len(logs) == 1
        insert_log = logs[0]
        assert insert_log.action == "INSERT"
        assert insert_log.new_values["scope"] == "PAGE"
        assert insert_log.new_values["target_id"] == "PAGE-01"
        assert insert_log.new_values["is_verified"] is True

    # Drop verification and check update auditing
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(SDVSignOff).where(SDVSignOff.target_id == "PAGE-01")
            )
            sign_off = result.scalar_one()
            sign_off.is_verified = False
            sign_off.dropped_reason = "Data updated in source"
            sign_off.dropped_at = datetime(2026, 7, 28, 15, 0, 0)

    # Verify update auditing
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "sdv_sign_offs")
            .order_by(AuditLog.timestamp)
        )
        logs = result.scalars().all()
        assert len(logs) == 2
        update_log = logs[1]
        assert update_log.action == "UPDATE"
        assert update_log.new_values["is_verified"] is False
        assert update_log.new_values["dropped_reason"] == "Data updated in source"

    # Verify hard deletion prevention
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception, match="Hard deletions are strictly forbidden"
            ):
                await session.execute(
                    text("DELETE FROM sdv_sign_offs WHERE target_id = 'PAGE-01';")
                )


@pytest.mark.asyncio
async def test_tsdv_config_persistence():
    # @req:PRD-QRY-007
    """
    Verify TSDVConfig stores study-specific configuration settings, JSON lists,
    enforces a unique study_id, and registers audit logs.
    """
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            cfg = TSDVConfig(
                study_id="STUDY-SAMPLING",
                sampling_model="FIELD_BASED",
                initial_full_sdv_subject_count=5,
                random_sample_percentage=25.5,
                full_sdv_domains=["VS", "EG"],
                safety_endpoints=["AE", "SAE"],
                zero_sdv_domains=["DM"],
                trial_random_seed=42,
            )
            session.add(cfg)

    # Retrieve and verify types (including JSON columns list mapping)
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(TSDVConfig).where(TSDVConfig.study_id == "STUDY-SAMPLING")
        )
        saved_cfg = result.scalar_one()
        assert saved_cfg.sampling_model == "FIELD_BASED"
        assert saved_cfg.initial_full_sdv_subject_count == 5
        assert saved_cfg.random_sample_percentage == 25.5
        assert saved_cfg.full_sdv_domains == ["VS", "EG"]
        assert saved_cfg.safety_endpoints == ["AE", "SAE"]
        assert saved_cfg.zero_sdv_domains == ["DM"]
        assert saved_cfg.trial_random_seed == 42

    # Verify unique constraint on study_id
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            duplicate_cfg = TSDVConfig(
                study_id="STUDY-SAMPLING",
                sampling_model="SUBJECT_BASED",
            )
            session.add(duplicate_cfg)
            with pytest.raises(Exception):
                await session.flush()
