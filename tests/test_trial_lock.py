from typing import Any
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
    site_id: Mapped[str | None] = mapped_column(String(50), nullable=True)
    visit_id: Mapped[str | None] = mapped_column(String(50), nullable=True)


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
async def test_trial_lock_freeze(
    mock_webhook: Any, mock_sms: Any, mock_email: Any
) -> None:
    # @req:Trace-3
    # @req:PRD-SYS-003
    session_maker = db_manager.get_session_maker()

    @transactional(session_maker)
    async def create_record() -> str:
        session = current_session.get()
        record = LockClinicalRecord(data_value="patient_1")
        session.add(record)
        await session.flush()
        return str(record.id)

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
    @transactional(session_maker)
    async def write_while_locked() -> None:
        session = current_session.get()
        record = LockClinicalRecord(data_value="patient_2")
        session.add(record)
        await session.flush()

    with pytest.raises(
        PermissionError, match="Trial is currently locked in a read-only state"
    ):
        await write_while_locked()

    # Read operations should still work
    @transactional(session_maker)
    async def read_while_locked() -> str | None:
        session = current_session.get()
        result = await session.execute(
            select(LockClinicalRecord).where(LockClinicalRecord.id == record_id)
        )
        record = result.scalars().first()
        return record.data_value if record else None

    val = await read_while_locked()
    assert val == "patient_1"


@pytest.mark.asyncio
async def test_site_and_visit_locks() -> None:
    session_maker = db_manager.get_session_maker()

    # 1. Test helpers
    @transactional(session_maker)
    async def create_record(site: str | None = None, visit: str | None = None) -> str:
        session = current_session.get()
        record = LockClinicalRecord(
            data_value="patient_x", site_id=site, visit_id=visit
        )
        session.add(record)
        await session.flush()
        return str(record.id)

    @transactional(session_maker)
    async def update_record(rec_id: str, new_value: str) -> None:
        session = current_session.get()
        result = await session.execute(
            select(LockClinicalRecord).where(LockClinicalRecord.id == rec_id)
        )
        record = result.scalars().first()
        if record:
            record.data_value = new_value
        await session.flush()

    @transactional(session_maker)
    async def soft_delete_record(rec_id: str) -> None:
        session = current_session.get()
        result = await session.execute(
            select(LockClinicalRecord).where(LockClinicalRecord.id == rec_id)
        )
        record = result.scalars().first()
        if record:
            record.is_deleted = True
        await session.flush()

    @transactional(session_maker)
    async def hard_delete_record(rec_id: str) -> None:
        session = current_session.get()
        result = await session.execute(
            select(LockClinicalRecord).where(LockClinicalRecord.id == rec_id)
        )
        record = result.scalars().first()
        if record:
            await session.delete(record)
        await session.flush()

    rec_id = await create_record(site="site_001", visit="visit_001")
    assert rec_id is not None

    # 2. Lock a specific site
    TrialLockManager.lock_site("site_999")
    assert TrialLockManager.is_site_locked("site_999")
    assert not TrialLockManager.is_site_locked("site_001")

    # A write to another site should succeed
    other_rec_id = await create_record(site="site_001", visit="visit_001")
    assert other_rec_id is not None

    # A write to the locked site should fail
    with pytest.raises(PermissionError, match="Site site_999 is currently locked"):
        await create_record(site="site_999", visit="visit_001")

    # Creating a record for locked site and then modifying it
    TrialLockManager.unlock_site("site_999")
    locked_site_rec_id = await create_record(site="site_999", visit="visit_001")
    assert locked_site_rec_id is not None

    # Re-lock site and try to modify / soft-delete the record
    TrialLockManager.lock_site("site_999")
    with pytest.raises(PermissionError, match="Site site_999 is currently locked"):
        await update_record(locked_site_rec_id, "updated_val")

    with pytest.raises(PermissionError, match="Site site_999 is currently locked"):
        await soft_delete_record(locked_site_rec_id)

    # 3. Unlock site and write/soft-delete should succeed
    TrialLockManager.unlock_site("site_999")
    await update_record(locked_site_rec_id, "updated_val")
    await soft_delete_record(locked_site_rec_id)

    # 4. Lock a specific visit
    TrialLockManager.lock_visit("visit_999")
    assert TrialLockManager.is_visit_locked("visit_999")
    assert not TrialLockManager.is_visit_locked("visit_001")

    # A write to another visit should succeed
    other_visit_rec_id = await create_record(site="site_001", visit="visit_001")
    assert other_visit_rec_id is not None

    # A write to the locked visit should fail
    with pytest.raises(PermissionError, match="Visit visit_999 is currently locked"):
        await create_record(site="site_001", visit="visit_999")

    # Creating a record for locked visit and then modifying it
    TrialLockManager.unlock_visit("visit_999")
    locked_visit_rec_id = await create_record(site="site_001", visit="visit_999")
    assert locked_visit_rec_id is not None

    # Re-lock visit and try to modify / soft-delete the record
    TrialLockManager.lock_visit("visit_999")
    with pytest.raises(PermissionError, match="Visit visit_999 is currently locked"):
        await update_record(locked_visit_rec_id, "updated_val_visit")

    with pytest.raises(PermissionError, match="Visit visit_999 is currently locked"):
        await soft_delete_record(locked_visit_rec_id)

    # 5. Unlock visit and write/soft-delete should succeed
    TrialLockManager.unlock_visit("visit_999")
    await update_record(locked_visit_rec_id, "updated_val_visit")
    await soft_delete_record(locked_visit_rec_id)

    # 6. Hard delete attempt should be prevented by GxP trigger policy regardless of locks
    with pytest.raises(
        ValueError, match="Hard deletion of LockClinicalRecord is forbidden"
    ):
        await hard_delete_record(locked_visit_rec_id)
