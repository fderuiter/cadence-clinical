from unittest.mock import patch

import pytest
import pytest_asyncio
from sqlalchemy import String, select
from sqlalchemy.orm import Mapped, mapped_column

from apps.execution.database.context import current_session
from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.models import AuditedModel, Base
from apps.execution.trial_lock import TrialLockManager


class LockClinicalRecord(AuditedModel):
    __tablename__ = "lock_clinical_records"
    data_value: Mapped[str] = mapped_column(String(255), nullable=True)


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    import os

    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"), echo=False
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
@patch("apps.execution.trial_lock.NotificationRouter.send_email")
@patch("apps.execution.trial_lock.NotificationRouter.send_sms")
@patch("apps.execution.trial_lock.NotificationRouter.send_webhook")
async def test_trial_lock_freeze(mock_webhook, mock_sms, mock_email):
    # @req:Trace-3
    # @req:PRD-SYS-003
    @transactional(lambda: db_manager.get_session_maker()())
    async def create_record():
        session = current_session.get()
        record = LockClinicalRecord(data_value="patient_1")
        session.add(record)
        await session.flush()
        return record.id

    # Normal state
    record_id = await create_record()
    assert record_id is not None

    # Simulate security compromise
    TrialLockManager.lock_trial("Simulated Data Tampering")
    assert TrialLockManager.is_locked()

    # Alerts dispatched?
    mock_email.assert_called_once()
    mock_sms.assert_called_once()
    mock_webhook.assert_called_once()

    # Try write operations (should fail)
    @transactional(lambda: db_manager.get_session_maker()())
    async def write_while_locked():
        session = current_session.get()
        record = LockClinicalRecord(data_value="patient_2")
        session.add(record)
        await session.flush()

    with pytest.raises(
        PermissionError, match="Trial is currently locked in a read-only state"
    ):
        await write_while_locked()

    # Read operations should still work
    @transactional(lambda: db_manager.get_session_maker()())
    async def read_while_locked():
        session = current_session.get()
        result = await session.execute(
            select(LockClinicalRecord).where(LockClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        return record.data_value

    val = await read_while_locked()
    assert val == "patient_1"
