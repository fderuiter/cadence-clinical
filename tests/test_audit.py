import pytest
import pytest_asyncio
from sqlalchemy.ext.asyncio import create_async_engine, async_sessionmaker, AsyncSession
from sqlalchemy import select, String
from sqlalchemy.orm import Mapped, mapped_column
from sqlalchemy.pool import StaticPool

from apps.execution.database.models import Base, AuditLog, AuditedModel
from apps.execution.database.context import current_session, current_user_id, current_change_reason
from apps.execution.database.decorators import transactional
import apps.execution.database.audit  # noqa: F401 (Imported for side-effects: registers event listener)

# Create a test model that extends AuditedModel
class ClinicalRecord(AuditedModel):
    __tablename__ = 'clinical_records'
    data_value: Mapped[str] = mapped_column(String(255), nullable=True)

# Test DB Setup
engine = create_async_engine("sqlite+aiosqlite:///:memory:", poolclass=StaticPool, echo=False)
TestingSessionLocal = async_sessionmaker(engine, expire_on_commit=False, class_=AsyncSession)

@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)

@pytest.mark.asyncio
async def test_insert_generates_audit_log():
    current_user_id.set("user_123")
    current_change_reason.set("initial setup")

    @transactional(TestingSessionLocal)
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_a")
        session.add(record)
        await session.flush()
        await session.flush()
        return record

    record = await create_record()

    async with TestingSessionLocal() as session:
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
    @transactional(TestingSessionLocal)
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

    @transactional(TestingSessionLocal)
    async def update_record():
        session = current_session.get()
        result = await session.execute(select(ClinicalRecord).where(ClinicalRecord.id == record_id))
        record = result.scalars().first()
        record.data_value = "patient_a_updated"
        await session.flush()
        return record.version

    new_version = await update_record()

    async with TestingSessionLocal() as session:
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
    @transactional(TestingSessionLocal)
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_to_delete")
        session.add(record)
        await session.flush()
        return record.id

    record_id = await create_record()

    @transactional(TestingSessionLocal)
    async def delete_record():
        session = current_session.get()
        result = await session.execute(select(ClinicalRecord).where(ClinicalRecord.id == record_id))
        record = result.scalars().first()
        record.is_deleted = True

    await delete_record()

    async with TestingSessionLocal() as session:
        result = await session.execute(select(AuditLog).order_by(AuditLog.timestamp))
        logs = result.scalars().all()
        
        assert len(logs) == 2
        assert logs[1].action == "DELETE"
        assert logs[1].old_values["is_deleted"] is False
        assert logs[1].new_values["is_deleted"] is True

@pytest.mark.asyncio
async def test_hard_delete_is_prevented():
    @transactional(TestingSessionLocal)
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="patient_to_delete")
        session.add(record)
        await session.flush()
        return record.id

    record_id = await create_record()

    @transactional(TestingSessionLocal)
    async def hard_delete_record():
        session = current_session.get()
        result = await session.execute(select(ClinicalRecord).where(ClinicalRecord.id == record_id))
        record = result.scalars().first()
        await session.delete(record)

    with pytest.raises(ValueError, match="Hard deletion .* is forbidden"):
        await hard_delete_record()

@pytest.mark.asyncio
async def test_rollback_prevents_orphan_audit_logs():
    async with TestingSessionLocal() as session:
        result = await session.execute(select(AuditLog))
        initial_count = len(result.scalars().all())

    @transactional(TestingSessionLocal)
    async def failing_transaction():
        session = current_session.get()
        record = ClinicalRecord(data_value="will_fail")
        session.add(record)
        await session.flush()
        session.add(AuditLog(table_name="dummy", record_id="dummy", action="INSERT")) # This is to make sure nothing flushes successfully
        # Trigger an exception intentionally
        raise RuntimeError("Intentional Failure")

    with pytest.raises(RuntimeError):
        await failing_transaction()

    # Verify no audit logs were persisted
    async with TestingSessionLocal() as session:
        result = await session.execute(select(AuditLog))
        final_count = len(result.scalars().all())
        assert final_count == initial_count

@pytest.mark.asyncio
async def test_read_only_queries_do_not_generate_audit_logs():
    @transactional(TestingSessionLocal)
    async def create_record():
        session = current_session.get()
        record = ClinicalRecord(data_value="read_only_test")
        session.add(record)
        await session.flush()
        await session.flush()
        return record.id

    record_id = await create_record()
    
    # Check current audit count
    async with TestingSessionLocal() as session:
        result = await session.execute(select(AuditLog))
        initial_count = len(result.scalars().all())
        
    @transactional(TestingSessionLocal)
    async def read_record():
        session = current_session.get()
        result = await session.execute(select(ClinicalRecord).where(ClinicalRecord.id == record_id))
        record = result.scalars().first()
        return record.data_value
        
    val = await read_record()
    assert val == "read_only_test"
    
    # Verify count did not increase
    async with TestingSessionLocal() as session:
        result = await session.execute(select(AuditLog))
        final_count = len(result.scalars().all())
        assert final_count == initial_count

