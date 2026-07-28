import pytest
import pytest_asyncio
from sqlalchemy import select, text
from sqlalchemy.exc import DatabaseError

from apps.ctms.database import db_manager as ctms_db_manager
from apps.ctms.models import Base as CTMSBase
from apps.ctms.models import CTMSAuditLog, CTMSStudy
from apps.execution.trial_lock import TrialLockManager
from apps.quality.database import db_manager as quality_db_manager
from apps.quality.models import Base as QualityBase
from apps.quality.models import (
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    QualityAuditLedgerSeal,
    QualityAuditLog,
)
from apps.quality.sealer import (
    execute_quality_audit_sealing_cycle,
    validate_quality_ledger_integrity,
)
from packages.security.context import audit_context


@pytest_asyncio.fixture(autouse=True)
async def setup_test_databases():
    # Setup CTMS DB
    ctms_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CTMSBase.metadata.create_all)

    # Setup Quality DB
    quality_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with quality_db_manager.engine.begin() as conn:
        await conn.run_sync(QualityBase.metadata.create_all)

    # Unlock trial if locked
    TrialLockManager.unlock_trial()

    yield

    # Clean up CTMS DB
    if ctms_db_manager.engine is not None:
        async with ctms_db_manager.engine.begin() as conn:
            await conn.run_sync(CTMSBase.metadata.drop_all)
        await ctms_db_manager.close()

    # Clean up Quality DB
    if quality_db_manager.engine is not None:
        async with quality_db_manager.engine.begin() as conn:
            await conn.run_sync(QualityBase.metadata.drop_all)
        await quality_db_manager.close()

    TrialLockManager.unlock_trial()


@pytest.mark.asyncio
async def test_automatic_gxp_audit_logging_and_validation():
    """
    Verify insertions and updates automatically generate audit records with correct
    user, IP, change reason, and action, and that validation enforces correct context.
    """
    # 1. Insertion with valid user context
    with audit_context(user_id="user_john", change_reason="Initial CTMS Study Setup"):
        async with ctms_db_manager.get_session_maker()() as session:
            study = CTMSStudy(
                study_id="study-abc-123",
                name="COVID-19 Vaccine Efficacy Study",
                created_by="user_john",
                reason_for_change="Initial CTMS Study Setup",
            )
            session.add(study)
            await session.commit()

    # Verify audit log was automatically generated
    async with ctms_db_manager.get_session_maker()() as session:
        result = await session.execute(select(CTMSAuditLog))
        logs = result.scalars().all()
        assert len(logs) == 1
        assert logs[0].action == "INSERT"
        assert logs[0].user_id == "user_john"
        assert "Created CTMSStudy" in logs[0].details

    # 2. Update with valid user context
    with audit_context(user_id="user_mary", change_reason="Update Study Sponsor"):
        async with ctms_db_manager.get_session_maker()() as session:
            stmt = select(CTMSStudy).where(CTMSStudy.study_id == "study-abc-123")
            res = await session.execute(stmt)
            retrieved_study = res.scalar_one()

            retrieved_study.name = "COVID-19 Vaccine Efficacy Study V2"
            retrieved_study.reason_for_change = "Update Study Sponsor"
            await session.commit()

    # Verify update audit log was automatically generated and version incremented
    async with ctms_db_manager.get_session_maker()() as session:
        result = await session.execute(select(CTMSStudy))
        updated_study = result.scalar_one()
        assert updated_study.version_index == 2

        result_logs = await session.execute(
            select(CTMSAuditLog).order_by(CTMSAuditLog.timestamp.asc())
        )
        logs = result_logs.scalars().all()
        assert len(logs) == 2
        assert logs[1].action == "UPDATE"
        assert logs[1].user_id == "user_mary"
        assert "Updated CTMSStudy" in logs[1].details
        assert "name" in logs[1].details

    # 3. Invalid context validation (missing change reason)
    with audit_context(user_id="user_hacker", change_reason=""):
        async with ctms_db_manager.get_session_maker()() as session:
            study2 = CTMSStudy(
                study_id="study-def-456",
                name="Malicious Study",
                created_by="user_hacker",
                reason_for_change="",
            )
            session.add(study2)
            with pytest.raises(
                ValueError, match="A valid change justification is required"
            ):
                await session.commit()


@pytest.mark.asyncio
async def test_hard_delete_prevention():
    """
    Verify that attempted hard deletions of clinical models fail with explicit database-level error.
    """
    with audit_context(user_id="user_john", change_reason="Quality Deviation Log"):
        async with quality_db_manager.get_session_maker()() as session:
            dev = Deviation(
                study_id="study_999",
                site_id="site_111",
                title="Temperature Excursion",
                description="IP stored out of bounds",
                severity=DeviationSeverity.CRITICAL,
                status=DeviationStatus.REPORTED,
                type=DeviationType.IP_MANAGEMENT,
                is_protocol_violation=True,
                created_by="user_john",
                reason_for_change="Quality Deviation Log",
            )
            session.add(dev)
            await session.commit()

    async with quality_db_manager.get_session_maker()() as session:
        stmt = select(Deviation).where(Deviation.study_id == "study_999")
        res = await session.execute(stmt)
        retrieved_dev = res.scalar_one()

        await session.delete(retrieved_dev)
        with pytest.raises(
            DatabaseError,
            match="Hard deletion of clinical model Deviation is forbidden",
        ):
            await session.commit()


@pytest.mark.asyncio
async def test_cryptographic_sealing_and_tamper_detection():
    """
    Verify that sealing cycle generates cryptographic seals for unsealed records,
    and manual tampering or integrity mismatches successfully lock the trial and alert.
    """
    # 1. Generate some audit logs
    with audit_context(user_id="user_alice", change_reason="Create deviation 1"):
        async with quality_db_manager.get_session_maker()() as session:
            dev1 = Deviation(
                study_id="study_1",
                title="Deviation 1",
                description="Details 1",
                severity=DeviationSeverity.MINOR,
                type=DeviationType.OTHER,
                created_by="user_alice",
                reason_for_change="Create deviation 1",
            )
            session.add(dev1)
            await session.commit()

    with audit_context(user_id="user_bob", change_reason="Create deviation 2"):
        async with quality_db_manager.get_session_maker()() as session:
            dev2 = Deviation(
                study_id="study_2",
                title="Deviation 2",
                description="Details 2",
                severity=DeviationSeverity.MAJOR,
                type=DeviationType.OTHER,
                created_by="user_bob",
                reason_for_change="Create deviation 2",
            )
            session.add(dev2)
            await session.commit()

    # Verify they are unsealed initially
    async with quality_db_manager.get_session_maker()() as session:
        logs_res = await session.execute(select(QualityAuditLog))
        logs = logs_res.scalars().all()
        assert len(logs) == 2
        for log in logs:
            assert log.cryptographic_seal is None

    # 2. Run sealing cycle
    async with quality_db_manager.get_session_maker()() as session:
        block_hash = await execute_quality_audit_sealing_cycle(session)
        assert block_hash is not None

    # Verify seal block was created
    async with quality_db_manager.get_session_maker()() as session:
        seals_res = await session.execute(select(QualityAuditLedgerSeal))
        seals = seals_res.scalars().all()
        assert len(seals) == 1
        assert seals[0].current_block_hash == block_hash
        assert seals[0].sealed_record_count == 2
        assert seals[0].previous_block_hash == "0" * 64

        logs_res = await session.execute(select(QualityAuditLog))
        logs = logs_res.scalars().all()
        for log in logs:
            assert log.cryptographic_seal == block_hash

    # 3. Validate integrity (should succeed)
    async with quality_db_manager.get_session_maker()() as session:
        valid = await validate_quality_ledger_integrity(session)
        assert valid is True
        assert TrialLockManager.is_locked() is False

    # 4. Tamper with sealed records and verify lock trigger
    async with quality_db_manager.get_session_maker()() as session:
        # Simulate out-of-band direct database edit bypassing app validation
        await session.execute(
            text(
                "UPDATE quality_audit_logs SET details = 'Tampered details' WHERE action = 'INSERT' LIMIT 1;"
            )
        )
        await session.commit()

    # Re-validate (should fail and lock the trial)
    async with quality_db_manager.get_session_maker()() as session:
        with pytest.raises(ValueError, match="Quality GxP Data Integrity Breach"):
            await validate_quality_ledger_integrity(session)

    assert TrialLockManager.is_locked() is True
