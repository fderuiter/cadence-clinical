"""Unit test suite for granular data locking Pydantic models.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime

import packages  # noqa: F401
from apps.execution.src.domain.lock_models import (
    DataLockRecord,
    DataUnlockRecord,
    LockScopeEnum,
    LockStatusEnum,
)


def test_data_lock_record_creation() -> None:
    """Validate DataLockRecord instantiation with form and field-level scopes.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()

    lock_record = DataLockRecord(
        lock_id="dl_001",
        study_id="study_01",
        subject_id="sub_101",
        form_id="form_vs_01",
        field_name="SYSBP",
        scope=LockScopeEnum.FIELD,
        status=LockStatusEnum.LOCKED,
        locked_by="datamanager_01",
        reason_for_change="Database lock for interim analysis",
        locked_at=now_iso,
    )

    assert lock_record.lock_id == "dl_001"
    assert lock_record.scope == LockScopeEnum.FIELD
    assert lock_record.status == LockStatusEnum.LOCKED
    assert lock_record.field_name == "SYSBP"
    assert lock_record.reason_for_change == "Database lock for interim analysis"


def test_data_unlock_record_creation() -> None:
    """Validate DataUnlockRecord instantiation with GxP audit parameters.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()

    unlock_record = DataUnlockRecord(
        unlock_id="du_001",
        lock_id="dl_001",
        unlocked_by="lead_dm_99",
        reason_for_change="Query resolution requires site value update",
        unlocked_at=now_iso,
    )

    assert unlock_record.unlock_id == "du_001"
    assert unlock_record.lock_id == "dl_001"
    assert unlock_record.unlocked_by == "lead_dm_99"
    assert (
        unlock_record.reason_for_change == "Query resolution requires site value update"
    )
