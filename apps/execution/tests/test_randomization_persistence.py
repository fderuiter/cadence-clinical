import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.cryptography import AllocationKeyManager
from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import (
    AuditLog,
    Base,
    RandomizationConfig,
    StratumState,
    SubjectRandomization,
)
from apps.execution.trial_lock import TrialLockManager


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_randomization_entities_audit_trail_and_soft_delete():
    """Verify that RandomizationConfig, StratumState, and SubjectRandomization participate in the GxP audit trail, version index, and soft deletion."""
    current_user_id.set("user_rtsm_mgr")
    current_change_reason.set("Configure RTSM for Study A")

    # 1. Setup cryptography key manager
    key_mgr = AllocationKeyManager()
    encrypted_block = key_mgr.encrypt({"block_sizes": [4, 6]})
    encrypted_seq = key_mgr.encrypt({"sequence": ["Arm A", "Arm B", "Arm B", "Arm A"]})
    encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A"})

    # 2. Insert RandomizationConfig and StratumState
    @transactional(lambda: db_manager.get_session_maker()())
    async def configure_randomization():
        session = current_session.get()
        config = RandomizationConfig(
            study_id="STUDY_A",
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 1},
            stratification_factors={"gender": ["M", "F"]},
            encrypted_block_config=encrypted_block,
            seed=42,
        )
        stratum = StratumState(
            study_id="STUDY_A",
            stratum_key="gender_M",
            block_index=0,
            encrypted_sequence=encrypted_seq,
        )
        session.add(config)
        session.add(stratum)
        await session.flush()
        return config.id, stratum.id

    config_id, stratum_id = await configure_randomization()

    # Verify audit logs for the insert
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.record_id == config_id)
        )
        config_log = result.scalars().first()
        assert config_log is not None
        assert config_log.action == "INSERT"
        assert config_log.table_name == "randomization_configs"
        assert config_log.user_id == "user_rtsm_mgr"
        assert config_log.change_reason == "Configure RTSM for Study A"
        assert config_log.version_index == 1

        # Decrypt block configuration to verify encryption compliance
        decrypted_block = key_mgr.decrypt(
            config_log.new_values["encrypted_block_config"]
        )
        assert decrypted_block["block_sizes"] == [4, 6]

    # 3. Update StratumState (e.g. advance block index) and verify audit log update
    current_user_id.set("user_rtsm_mgr")
    current_change_reason.set("Advance block index after subject assignment")

    @transactional(lambda: db_manager.get_session_maker()())
    async def advance_stratum():
        session = current_session.get()
        result = await session.execute(
            select(StratumState).where(StratumState.id == stratum_id)
        )
        stratum = result.scalars().one()
        stratum.block_index = 1
        await session.flush()
        return stratum.version

    new_version = await advance_stratum()
    assert new_version == 2

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.record_id == stratum_id)
        )
        stratum_logs = result.scalars().all()
        assert len(stratum_logs) >= 2  # INSERT and UPDATE
        update_log = next(log for log in stratum_logs if log.action == "UPDATE")
        assert update_log.user_id == "user_rtsm_mgr"
        assert (
            update_log.change_reason == "Advance block index after subject assignment"
        )
        assert update_log.old_values["block_index"] == 0
        assert update_log.new_values["block_index"] == 1
        assert update_log.version_index == 2

    # 4. Perform Subject assignment and verify it enforces one assignment per subject
    current_user_id.set("user_site")
    current_change_reason.set("Subject Randomization")

    @transactional(lambda: db_manager.get_session_maker()())
    async def randomize_subject():
        session = current_session.get()
        assignment = SubjectRandomization(
            study_id="STUDY_A",
            subject_id="SUBJ_001",
            stratum_key="gender_M",
            encrypted_allocation=encrypted_alloc,
            kit_reference="KIT-1004",
        )
        session.add(assignment)
        await session.flush()
        return assignment.id

    assignment_id = await randomize_subject()

    # Decrypt allocation from assignment
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(SubjectRandomization).where(SubjectRandomization.id == assignment_id)
        )
        assignment = result.scalars().one()
        decrypted_alloc = key_mgr.decrypt(assignment.encrypted_allocation)
        assert decrypted_alloc["allocation"] == "Arm A"

    # Try to insert another assignment for the same subject_id -> should fail unique constraint
    @transactional(lambda: db_manager.get_session_maker()())
    async def duplicate_randomize_subject():
        session = current_session.get()
        assignment2 = SubjectRandomization(
            study_id="STUDY_A",
            subject_id="SUBJ_001",  # Duplicate subject_id
            stratum_key="gender_M",
            encrypted_allocation=encrypted_alloc,
            kit_reference="KIT-1005",
        )
        session.add(assignment2)
        await session.flush()

    with pytest.raises(Exception):
        await duplicate_randomize_subject()

    # 5. Soft-deletion check
    current_change_reason.set("Soft delete randomization config")

    @transactional(lambda: db_manager.get_session_maker()())
    async def soft_delete_config():
        session = current_session.get()
        result = await session.execute(
            select(RandomizationConfig).where(RandomizationConfig.id == config_id)
        )
        config = result.scalars().one()
        config.is_deleted = True
        await session.flush()

    await soft_delete_config()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(
            select(AuditLog).where(AuditLog.record_id == config_id)
        )
        logs = result.scalars().all()
        delete_log = next(log for log in logs if log.action == "DELETE")
        assert delete_log.new_values["is_deleted"] is True


@pytest.mark.asyncio
async def test_randomization_entities_hard_delete_prevented():
    """Verify that hard deletes are strictly prevented for RandomizationConfig, StratumState, and SubjectRandomization."""
    # Configure first
    key_mgr = AllocationKeyManager()
    encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A"})

    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_assignment():
        session = current_session.get()
        assignment = SubjectRandomization(
            study_id="STUDY_A",
            subject_id="SUBJ_002",
            encrypted_allocation=encrypted_alloc,
        )
        session.add(assignment)
        await session.flush()
        return assignment.id

    assignment_id = await setup_assignment()

    @transactional(lambda: db_manager.get_session_maker()())
    async def hard_delete_assignment():
        session = current_session.get()
        result = await session.execute(
            select(SubjectRandomization).where(SubjectRandomization.id == assignment_id)
        )
        assignment = result.scalars().one()
        await session.delete(assignment)
        await session.flush()

    with pytest.raises(ValueError, match="Hard deletion .* is forbidden"):
        await hard_delete_assignment()


@pytest.mark.asyncio
async def test_randomization_entities_trial_lock_conformity():
    """Verify that RTSM models respect trial-level locks and block write mutations when locked."""
    key_mgr = AllocationKeyManager()
    encrypted_alloc = key_mgr.encrypt({"allocation": "Arm A"})

    @transactional(lambda: db_manager.get_session_maker()())
    async def setup_assignment():
        session = current_session.get()
        assignment = SubjectRandomization(
            study_id="STUDY_A",
            subject_id="SUBJ_003",
            encrypted_allocation=encrypted_alloc,
        )
        session.add(assignment)
        await session.flush()
        return assignment.id

    assignment_id = await setup_assignment()

    # Activate trial lock
    TrialLockManager.lock_trial()

    # Try mutating existing record -> should raise PermissionError
    @transactional(lambda: db_manager.get_session_maker()())
    async def update_assignment():
        session = current_session.get()
        result = await session.execute(
            select(SubjectRandomization).where(SubjectRandomization.id == assignment_id)
        )
        assignment = result.scalars().one()
        assignment.kit_reference = "KIT-9999"
        await session.flush()

    with pytest.raises(
        PermissionError, match="Trial is currently locked in a read-only state"
    ):
        await update_assignment()

    # Try creating a new config -> should raise PermissionError
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_new_config():
        session = current_session.get()
        config = RandomizationConfig(
            study_id="STUDY_B",
            algorithm_type="PERMUTED_BLOCK",
            arms_ratios={"Arm A": 1, "Arm B": 1},
        )
        session.add(config)
        await session.flush()

    with pytest.raises(
        PermissionError, match="Trial is currently locked in a read-only state"
    ):
        await create_new_config()
