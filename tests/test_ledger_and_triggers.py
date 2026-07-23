import os

import pytest
import pytest_asyncio
from sqlalchemy import String, select, text
from sqlalchemy.orm import Mapped, mapped_column

from apps.execution.database.core import db_manager
from apps.execution.database.models import AuditedModel, AuditLedgerSeal, AuditLog, Base
from apps.execution.database.sealer import (
    clean_query,
    execute_audit_sealing_cycle,
    validate_ledger_integrity,
)
from apps.execution.trial_lock import TrialLockManager


# Define a temporary test audited model
class AuditedClinicalRecord(AuditedModel):
    __tablename__ = "audited_clinical_records"
    data_value: Mapped[str] = mapped_column(String(255), nullable=True)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()

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
    TrialLockManager.reset()


@pytest.mark.asyncio
async def test_prevent_audit_log_mutation():
    # Insert an audit log record first
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            log = AuditLog(
                table_name="dummy",
                record_id="rec_1",
                action="INSERT",
                user_id="user_1",
                change_reason="test",
            )
            session.add(log)
            await session.commit()

    # Try to modify it directly via raw SQL
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await session.execute(
                    text(
                        clean_query(
                            "UPDATE audit_schema.audit_logs SET user_id = 'hacker' WHERE record_id = 'rec_1';",
                            session,
                        )
                    )
                )

            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await session.execute(
                    text(
                        clean_query(
                            "DELETE FROM audit_schema.audit_logs WHERE record_id = 'rec_1';",
                            session,
                        )
                    )
                )


@pytest.mark.asyncio
async def test_prevent_audit_ledger_seals_mutation():
    # Insert an audit ledger seal record first
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            seal = AuditLedgerSeal(
                previous_block_hash="0" * 64,
                current_block_hash="abc",
                sealed_record_count=1,
                merkle_root_hash="merkle",
            )
            session.add(seal)
            await session.commit()

    # Try to modify it directly via raw SQL
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await session.execute(
                    text(
                        clean_query(
                            "UPDATE audit_schema.audit_ledger_seals SET current_block_hash = 'tampered';",
                            session,
                        )
                    )
                )

            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await session.execute(
                    text(
                        clean_query(
                            "DELETE FROM audit_schema.audit_ledger_seals;",
                            session,
                        )
                    )
                )


@pytest.mark.asyncio
async def test_prevent_hard_delete_on_audited_model():
    # Insert an audited record first
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            rec = AuditedClinicalRecord(id="rec_100", data_value="important")
            session.add(rec)
            await session.commit()

    # Try to hard-delete it via raw SQL
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            with pytest.raises(
                Exception, match="Hard deletions are strictly forbidden"
            ):
                await session.execute(
                    text("DELETE FROM audited_clinical_records WHERE id = 'rec_100';")
                )


@pytest.mark.asyncio
async def test_out_of_band_update_triggers_audit_entry():
    # Insert an audited record
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            rec = AuditedClinicalRecord(id="rec_200", data_value="original")
            session.add(rec)
            await session.commit()

    # Direct out-of-band SQL update (simulating direct DB admin change, app_writing is default 'false')
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text(
                    "UPDATE audited_clinical_records SET data_value = 'tampered' WHERE id = 'rec_200';"
                )
            )
            await session.commit()

    # Verify that the DB trigger captured the out-of-band change and inserted an AuditLog record
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(
                    AuditLog.table_name == "audited_clinical_records"
                )
            )
            logs = res.scalars().all()
            update_logs = [log for log in logs if log.action == "UPDATE"]

            assert len(update_logs) == 1
            assert update_logs[0].new_values["data_value"] == "tampered"
            assert update_logs[0].user_id in (
                "system",
                "system_process",
            )  # default out-of-band value
            assert update_logs[0].change_reason in (
                "system_operation",
                "Automated system operation",
            )  # default out-of-band value


@pytest.mark.asyncio
async def test_ledger_sealing_and_validation():
    # Generate some unsealed audit logs
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            log1 = AuditLog(
                id="log_1",
                table_name="tb",
                record_id="r1",
                action="INSERT",
                user_id="u1",
                change_reason="r",
            )
            log2 = AuditLog(
                id="log_2",
                table_name="tb",
                record_id="r2",
                action="UPDATE",
                user_id="u2",
                change_reason="r",
            )
            session.add_all([log1, log2])
            await session.commit()

    # Execute sealing cycle
    async with db_manager.get_session_maker()() as session:
        block_hash = await execute_audit_sealing_cycle(session)
        assert block_hash is not None

    # Check that logs were sealed correctly
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            res = await session.execute(
                select(AuditLog).where(AuditLog.id.in_(["log_1", "log_2"]))
            )
            logs = res.scalars().all()
            assert len(logs) == 2
            for log in logs:
                assert log.cryptographic_seal == block_hash

            res_seals = await session.execute(select(AuditLedgerSeal))
            seals = res_seals.scalars().all()
            assert len(seals) == 1
            assert seals[0].current_block_hash == block_hash
            assert seals[0].sealed_record_count == 2

    # Validate the intact ledger (should pass successfully)
    async with db_manager.get_session_maker()() as session:
        is_valid = await validate_ledger_integrity(session)
        assert is_valid is True
        assert TrialLockManager.is_locked() is False

    # Tampering: Simulate a DB Admin dropping the lock trigger and modifying a sealed audit log
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            if db_manager.engine.dialect.name == "postgresql":
                await session.execute(
                    text(
                        "DROP TRIGGER IF EXISTS trg_lock_audit_trail_logs ON audit_schema.audit_logs;"
                    )
                )
            else:
                await session.execute(
                    text("DROP TRIGGER IF EXISTS trg_lock_audit_trail_logs_update;")
                )
            await session.execute(
                text(
                    clean_query(
                        "UPDATE audit_schema.audit_logs SET change_reason = 'tampered' WHERE id = 'log_1';",
                        session,
                    )
                )
            )
            await session.commit()

    # Validate ledger (should detect tampering, raise Value/Integrity breach alert, and safety freeze/lock trial)
    async with db_manager.get_session_maker()() as session:
        with pytest.raises(ValueError, match="GxP Core Data Integrity Breach"):
            await validate_ledger_integrity(session)

        assert TrialLockManager.is_locked() is True
