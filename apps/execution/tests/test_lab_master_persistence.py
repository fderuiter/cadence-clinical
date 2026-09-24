import pytest
from sqlalchemy import select, text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import (
    deploy_database_triggers,
)
from apps.execution.database.models import (
    AuditLog,
    Base,
    LabTestMaster,
    LabUnitConversion,
)
from apps.execution.trial_lock import TrialLockManager
from packages.security.context import audit_context


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
async def test_lab_test_master_crud_and_audit():
    # @req:PRD-LAB-001
    # @Req:PRD-LAB-001
    """
    Verify CRUD operations, metadata storage, and GxP audit trigger integration
    for the LabTestMaster model.
    """
    master_id = None
    with audit_context(user_id="user_abc", change_reason="Initial ingestion"):
        async with db_manager.get_session_maker()() as session, session.begin():
            # Let SQLAlchemy listener record audit logs
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            test_master = LabTestMaster(
                study_id="STUDY-XYZ",
                test_code="HEMOGLOBIN",
                test_name="Hemoglobin Concentration",
                default_unit="g/dL",
                normalized_unit="g/L",
                loinc_code="718-7",
                created_by="user_abc",
                reason_for_change="Initial ingestion",
                version_index=1,
            )
            session.add(test_master)
            await session.flush()
            master_id = test_master.id

    # Verify attributes are stored correctly
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabTestMaster).where(LabTestMaster.id == master_id)
        )
        saved = result.scalar_one()
        assert saved.study_id == "STUDY-XYZ"
        assert saved.test_code == "HEMOGLOBIN"
        assert saved.test_name == "Hemoglobin Concentration"
        assert saved.default_unit == "g/dL"
        assert saved.normalized_unit == "g/L"
        assert saved.loinc_code == "718-7"
        assert saved.created_at is not None
        assert saved.created_by == "user_abc"
        assert saved.reason_for_change == "Initial ingestion"
        assert saved.version_index == 1
        assert saved.version == 1
        assert saved.is_deleted is False

    # Verify audit trail logs created by database triggers for INSERT
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.table_name == "lab_test_masters")
        )
        logs = result.scalars().all()
        assert len(logs) == 1
        assert logs[0].action == "INSERT"
        assert logs[0].new_values["test_code"] == "HEMOGLOBIN"
        assert logs[0].new_values["study_id"] == "STUDY-XYZ"
        assert logs[0].change_reason == "Initial ingestion"
        assert logs[0].version_index == 1

    # Verify UPDATE audit and trigger version tracking
    with audit_context(user_id="user_abc", change_reason="Updated name for clarity"):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabTestMaster).where(LabTestMaster.id == master_id)
            )
            saved = result.scalar_one()
            saved.test_name = "Hemoglobin Level"
            saved.reason_for_change = "Updated name for clarity"
            saved.version_index = 2

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabTestMaster).where(LabTestMaster.id == master_id)
        )
        updated = result.scalar_one()
        assert updated.test_name == "Hemoglobin Level"
        assert updated.version == 2
        assert updated.version_index == 2
        assert updated.created_at is not None
        assert updated.created_by == "user_abc"
        assert updated.reason_for_change == "Updated name for clarity"

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_test_masters")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 2
        update_log = logs[1]
        assert update_log.action == "UPDATE"
        assert update_log.old_values["test_name"] == "Hemoglobin Concentration"
        assert update_log.new_values["test_name"] == "Hemoglobin Level"
        assert update_log.table_name == "lab_test_masters"
        assert update_log.change_reason == "Updated name for clarity"
        assert update_log.version_index == 2

    # Soft delete and check
    with audit_context(user_id="user_abc", change_reason="Obsoleted test code"):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabTestMaster).where(LabTestMaster.id == master_id)
            )
            saved = result.scalar_one()
            saved.is_deleted = True
            saved.reason_for_change = "Obsoleted test code"
            saved.version_index = 3

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabTestMaster).where(LabTestMaster.id == master_id)
        )
        deleted = result.scalar_one()
        assert deleted.is_deleted is True
        assert deleted.version == 3
        assert deleted.version_index == 3

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_test_masters")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 3
        delete_log = logs[2]
        assert delete_log.action == "DELETE"
        assert delete_log.new_values["is_deleted"] is True
        assert delete_log.change_reason == "Obsoleted test code"
        assert delete_log.version_index == 3

    # Attempt hard delete and ensure trigger prevents it
    async with db_manager.get_session_maker()() as session, session.begin():
        with pytest.raises(Exception, match="Hard deletions are strictly forbidden"):
            await session.execute(
                text("DELETE FROM lab_test_masters WHERE id = :id;").bindparams(
                    id=master_id
                )
            )


@pytest.mark.asyncio
async def test_lab_unit_conversion_crud_and_audit():
    # @req:PRD-LAB-001
    # @Req:PRD-LAB-001
    """
    Verify CRUD operations, metadata storage, and GxP audit trigger integration
    for the LabUnitConversion model.
    """
    conversion_id = None
    with audit_context(
        user_id="user_xyz", change_reason="Standard creatinine conversion factor"
    ):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            unit_conv = LabUnitConversion(
                study_id="STUDY-XYZ",
                test_code="CREATININE",
                from_unit="mg/dL",
                to_unit="umol/L",
                factor=88.4,
                offset=None,
                created_by="user_xyz",
                reason_for_change="Standard creatinine conversion factor",
                version_index=1,
            )
            session.add(unit_conv)
            await session.flush()
            conversion_id = unit_conv.id

    # Verify attributes are stored correctly
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabUnitConversion).where(LabUnitConversion.id == conversion_id)
        )
        saved = result.scalar_one()
        assert saved.study_id == "STUDY-XYZ"
        assert saved.test_code == "CREATININE"
        assert saved.from_unit == "mg/dL"
        assert saved.to_unit == "umol/L"
        assert saved.factor == 88.4
        assert saved.offset is None
        assert saved.created_at is not None
        assert saved.created_by == "user_xyz"
        assert saved.reason_for_change == "Standard creatinine conversion factor"
        assert saved.version_index == 1
        assert saved.version == 1
        assert saved.is_deleted is False

    # Verify audit logs for INSERT
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.table_name == "lab_unit_conversions")
        )
        logs = result.scalars().all()
        assert len(logs) == 1
        assert logs[0].action == "INSERT"
        assert logs[0].new_values["test_code"] == "CREATININE"
        assert logs[0].change_reason == "Standard creatinine conversion factor"
        assert logs[0].version_index == 1

    # Verify UPDATE audit
    with audit_context(
        user_id="user_xyz", change_reason="Added small offset correction"
    ):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabUnitConversion).where(LabUnitConversion.id == conversion_id)
            )
            saved = result.scalar_one()
            saved.offset = 0.01
            saved.reason_for_change = "Added small offset correction"
            saved.version_index = 2

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabUnitConversion).where(LabUnitConversion.id == conversion_id)
        )
        updated = result.scalar_one()
        assert updated.offset == 0.01
        assert updated.version == 2
        assert updated.version_index == 2
        assert updated.created_at is not None
        assert updated.created_by == "user_xyz"
        assert updated.reason_for_change == "Added small offset correction"

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_unit_conversions")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 2
        assert logs[1].action == "UPDATE"
        assert logs[1].new_values["offset"] == 0.01
        assert logs[1].table_name == "lab_unit_conversions"
        assert logs[1].change_reason == "Added small offset correction"
        assert logs[1].version_index == 2

    # Soft delete and check for LabUnitConversion
    with audit_context(user_id="user_xyz", change_reason="Obsoleted conversion"):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            result = await session.execute(
                select(LabUnitConversion).where(LabUnitConversion.id == conversion_id)
            )
            saved = result.scalar_one()
            saved.is_deleted = True
            saved.reason_for_change = "Obsoleted conversion"
            saved.version_index = 3

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(LabUnitConversion).where(LabUnitConversion.id == conversion_id)
        )
        deleted = result.scalar_one()
        assert deleted.is_deleted is True
        assert deleted.version == 3
        assert deleted.version_index == 3

        result_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_unit_conversions")
            .order_by(AuditLog.timestamp)
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 3
        delete_log = logs[2]
        assert delete_log.action == "DELETE"
        assert delete_log.new_values["is_deleted"] is True
        assert delete_log.change_reason == "Obsoleted conversion"
        assert delete_log.version_index == 3

    # Attempt hard delete and ensure trigger prevents it
    async with db_manager.get_session_maker()() as session, session.begin():
        with pytest.raises(Exception, match="Hard deletions are strictly forbidden"):
            await session.execute(
                text("DELETE FROM lab_unit_conversions WHERE id = :id;").bindparams(
                    id=conversion_id
                )
            )


@pytest.mark.asyncio
async def test_lab_catalog_explicit_audit_persistence():
    """
    Task 2: Confirm both LabTestMaster and LabUnitConversion persist their audit fields
    and write audit-log entries on update. Verify hard deletes are blocked.
    """
    master_id = None
    conv_id = None

    # 1. Insert both models under audit_context
    with audit_context(user_id="cat_auditor", change_reason="Initial catalog setup"):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            master = LabTestMaster(
                study_id="STUDY-CAT-1",
                test_code="GLUCOSE",
                test_name="Blood Glucose",
                default_unit="mg/dL",
                normalized_unit="mmol/L",
                created_by="cat_auditor",
                reason_for_change="Initial catalog setup",
                version_index=1,
            )
            conv = LabUnitConversion(
                study_id="STUDY-CAT-1",
                test_code="GLUCOSE",
                from_unit="mg/dL",
                to_unit="mmol/L",
                factor=0.0555,
                created_by="cat_auditor",
                reason_for_change="Initial catalog setup",
                version_index=1,
            )
            session.add_all([master, conv])
            await session.flush()
            master_id = master.id
            conv_id = conv.id

    # 2. Verify persistence on insert
    async with db_manager.get_session_maker()() as session:
        m_saved = (
            await session.execute(
                select(LabTestMaster).where(LabTestMaster.id == master_id)
            )
        ).scalar_one()
        assert m_saved.created_at is not None
        assert m_saved.created_by == "cat_auditor"
        assert m_saved.reason_for_change == "Initial catalog setup"
        assert m_saved.version_index == 1

        c_saved = (
            await session.execute(
                select(LabUnitConversion).where(LabUnitConversion.id == conv_id)
            )
        ).scalar_one()
        assert c_saved.created_at is not None
        assert c_saved.created_by == "cat_auditor"
        assert c_saved.reason_for_change == "Initial catalog setup"
        assert c_saved.version_index == 1

    # 3. Update operations
    with audit_context(user_id="cat_auditor", change_reason="Refined glucose details"):
        async with db_manager.get_session_maker()() as session, session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            m_row = (
                await session.execute(
                    select(LabTestMaster).where(LabTestMaster.id == master_id)
                )
            ).scalar_one()
            c_row = (
                await session.execute(
                    select(LabUnitConversion).where(LabUnitConversion.id == conv_id)
                )
            ).scalar_one()

            m_row.test_name = "Blood Glucose level"
            m_row.reason_for_change = "Refined glucose details"
            m_row.version_index = 2

            c_row.factor = 0.05551
            c_row.reason_for_change = "Refined glucose details"
            c_row.version_index = 2

    # 4. Verify audit fields updated on both models
    async with db_manager.get_session_maker()() as session:
        m_up = (
            await session.execute(
                select(LabTestMaster).where(LabTestMaster.id == master_id)
            )
        ).scalar_one()
        assert m_up.created_by == "cat_auditor"
        assert m_up.reason_for_change == "Refined glucose details"
        assert m_up.version_index == 2

        c_up = (
            await session.execute(
                select(LabUnitConversion).where(LabUnitConversion.id == conv_id)
            )
        ).scalar_one()
        assert c_up.created_by == "cat_auditor"
        assert c_up.reason_for_change == "Refined glucose details"
        assert c_up.version_index == 2

    # 5. Verify update operations produce AuditLog rows with correct fields
    async with db_manager.get_session_maker()() as session:
        # Check LabTestMaster audit log
        result_m_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_test_masters")
            .order_by(AuditLog.timestamp)
        )
        m_logs = result_m_logs.scalars().all()
        assert len(m_logs) >= 2
        m_up_log = m_logs[-1]
        assert m_up_log.action == "UPDATE"
        assert m_up_log.change_reason == "Refined glucose details"
        assert m_up_log.version_index == 2

        # Check LabUnitConversion audit log
        result_c_logs = await session.execute(
            select(AuditLog)
            .where(AuditLog.table_name == "lab_unit_conversions")
            .order_by(AuditLog.timestamp)
        )
        c_logs = result_c_logs.scalars().all()
        assert len(c_logs) >= 2
        c_up_log = c_logs[-1]
        assert c_up_log.action == "UPDATE"
        assert c_up_log.change_reason == "Refined glucose details"
        assert c_up_log.version_index == 2

    # 6. Verify hard deletes are blocked
    async with db_manager.get_session_maker()() as session, session.begin():
        with pytest.raises(Exception, match="Hard deletions are strictly forbidden"):
            await session.execute(
                text("DELETE FROM lab_test_masters WHERE id = :id;").bindparams(
                    id=master_id
                )
            )
        with pytest.raises(Exception, match="Hard deletions are strictly forbidden"):
            await session.execute(
                text("DELETE FROM lab_unit_conversions WHERE id = :id;").bindparams(
                    id=conv_id
                )
            )
