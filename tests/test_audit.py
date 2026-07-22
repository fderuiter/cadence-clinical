import pytest
import pytest_asyncio
from sqlalchemy import String, select
from sqlalchemy.orm import Mapped, mapped_column

import apps.execution.database.audit  # noqa: F401 (Imported for side-effects: registers event listener)
from apps.execution.database.context import (
    current_change_reason,
    current_session,
    current_user_id,
)
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import AuditedModel, AuditLog, Base


# Create a test model that extends AuditedModel
class ClinicalRecord(AuditedModel):
    __tablename__ = "clinical_records"
    data_value: Mapped[str] = mapped_column(String(255), nullable=True)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    import apps.execution.ledger as ledger

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
        from apps.execution.database.core import setup_database_triggers

        await setup_database_triggers(conn)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_insert_generates_audit_log():
    current_user_id.set("user_123")
    current_change_reason.set("initial setup")

    # Pass the getter method from db_manager so transactional gets the factory
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_a")
        session.add(record)
        await session.flush()
        await session.flush()
        return record

    record = await create_record()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog))
        logs = result.scalars().all()

        assert len(logs) == 1
        assert logs[0].action == "INSERT"
        assert logs[0].user_id == "user_123"
        assert logs[0].change_reason == "initial setup"
        assert logs[0].new_values["data_value"] == "patient_a"
        assert logs[0].record_id == record.id
        assert logs[0].table_name == "clinical_records"
        assert logs[0].old_values is None
        assert logs[0].version_index == 1


@pytest.mark.asyncio
async def test_update_generates_audit_log():
    # Insert initially
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_a")
        session.add(record)
        await session.flush()
        await session.flush()
        return record.id

    record_id = await create_record()

    current_user_id.set("user_456")
    current_change_reason.set("correction")

    @transactional(lambda: db_manager.get_session_maker()())
    async def update_record():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalRecord).where(ClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        record.data_value = "patient_a_updated"
        await session.flush()
        return record.version

    new_version = await update_record()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog).order_by(AuditLog.timestamp))
        logs = result.scalars().all()

        assert len(logs) == 2
        assert logs[1].action == "UPDATE"
        assert logs[1].user_id == "user_456"
        assert logs[1].change_reason == "correction"
        assert logs[1].old_values["data_value"] == "patient_a"
        assert logs[1].new_values["data_value"] == "patient_a_updated"
        assert logs[1].version_index == 2
        assert new_version == 2


@pytest.mark.asyncio
async def test_soft_delete_generates_audit_log():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_to_delete")
        session.add(record)
        await session.flush()
        return record.id

    record_id = await create_record()

    @transactional(lambda: db_manager.get_session_maker()())
    async def delete_record():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalRecord).where(ClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        record.is_deleted = True

    await delete_record()

    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog).order_by(AuditLog.timestamp))
        logs = result.scalars().all()

        assert len(logs) == 2
        assert logs[1].action == "DELETE"
        assert logs[1].old_values["is_deleted"] is False
        assert logs[1].new_values["is_deleted"] is True


@pytest.mark.asyncio
async def test_hard_delete_is_prevented():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_to_delete")
        session.add(record)
        await session.flush()
        return record.id

    record_id = await create_record()

    @transactional(lambda: db_manager.get_session_maker()())
    async def hard_delete_record():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalRecord).where(ClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        await session.delete(record)

    with pytest.raises(ValueError, match="Hard deletion .* is forbidden"):
        await hard_delete_record()


@pytest.mark.asyncio
async def test_rollback_prevents_orphan_audit_logs():
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog))
        initial_count = len(result.scalars().all())

    @transactional(lambda: db_manager.get_session_maker()())
    async def failing_transaction():
        session = current_session.get()
        record = ClinicalRecord(data_value="will_fail")
        session.add(record)
        await session.flush()
        session.add(
            AuditLog(table_name="dummy", record_id="dummy", action="INSERT")
        )  # This is to make sure nothing flushes successfully
        # Trigger an exception intentionally
        raise RuntimeError("Intentional Failure")

    with pytest.raises(RuntimeError):
        await failing_transaction()

    # Verify no audit logs were persisted
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog))
        final_count = len(result.scalars().all())
        assert final_count == initial_count


@pytest.mark.asyncio
async def test_read_only_queries_do_not_generate_audit_logs():
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="read_only_test")
        session.add(record)
        await session.flush()
        await session.flush()
        return record.id

    record_id = await create_record()

    # Check current audit count
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog))
        initial_count = len(result.scalars().all())

    @transactional(lambda: db_manager.get_session_maker()())
    async def read_record():
        session = current_session.get()
        result = await session.execute(
            select(ClinicalRecord).where(ClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        return record.data_value

    val = await read_record()
    assert val == "read_only_test"

    # Verify count did not increase
    async with db_manager.get_session_maker()() as session:
        result = await session.execute(select(AuditLog))
        final_count = len(result.scalars().all())
        assert final_count == initial_count
