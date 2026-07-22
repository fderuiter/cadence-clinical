import pytest
import pytest_asyncio
from sqlalchemy import select, text

import apps.execution.ledger as ledger
from apps.execution.database.core import db_manager, setup_database_triggers
from apps.execution.database.models import (
    AuditLedgerBlock,
    AuditLog,
    Base,
    TranslationJob,
)
from apps.execution.ledger import (
    enable_safety_freeze,
    seal_logs,
    verify_chain,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    ledger._SAFETY_FREEZE_ACTIVE = False
    db_manager.init_db(
        os.getenv(
            "TEST_DATABASE_URL",
            "sqlite+aiosqlite:///:memory:",
        ),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await setup_database_triggers(conn)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_seal_logs():
    async with db_manager.get_session_maker()() as session:
        for i in range(5):
            session.add(
                AuditLog(
                    table_name="test_table",
                    record_id=f"rec_{i}",
                    action="INSERT",
                    user_id="user_1",
                    new_values={"key": "val"},
                )
            )
        await session.commit()

        await seal_logs(session)
        await session.commit()

        result = await session.execute(select(AuditLedgerBlock))
        blocks = result.scalars().all()
        assert len(blocks) == 1
        block = blocks[0]
        assert block.block_number == 0
        assert block.previous_block_hash == "0" * 64
        assert len(block.sealed_log_ids) == 5

        log_result = await session.execute(
            select(AuditLog).where(AuditLog.table_name != "audit_ledger_blocks")
        )
        logs = log_result.scalars().all()
        assert len(logs) == 5
        for log in logs:
            assert log.block_id == block.id


@pytest.mark.asyncio
async def test_verify_chain_valid():
    async with db_manager.get_session_maker()() as session:
        session.add(AuditLog(table_name="t1", record_id="r1", action="INSERT"))
        await session.commit()
        await seal_logs(session)
        await session.commit()

        session.add(AuditLog(table_name="t1", record_id="r2", action="INSERT"))
        await session.commit()
        await seal_logs(session)
        await session.commit()

        is_valid = await verify_chain(session)
        assert is_valid is True


@pytest.mark.asyncio
async def test_database_triggers_prevent_modifications():
    async with db_manager.get_session_maker()() as session:
        log = AuditLog(table_name="t1", record_id="r1", action="INSERT")
        session.add(log)
        await session.commit()

        with pytest.raises(Exception):
            await session.execute(
                text(f"UPDATE audit_logs SET action='UPDATE' WHERE id='{log.id}'")
            )
            await session.commit()

        with pytest.raises(Exception):
            await session.execute(text(f"DELETE FROM audit_logs WHERE id='{log.id}'"))
            await session.commit()


@pytest.mark.asyncio
async def test_safety_freeze_prevents_writes():
    enable_safety_freeze()
    try:
        async with db_manager.get_session_maker()() as session:
            # Creating a record should fail
            job = TranslationJob(study_id="s1", status="PENDING")
            session.add(job)

            with pytest.raises(RuntimeError, match="SAFETY FREEZE ACTIVE"):
                await session.commit()
    finally:
        import apps.execution.ledger as ledger

        ledger._SAFETY_FREEZE_ACTIVE = False
