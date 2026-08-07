"""Unit test suite for data lock execution enforcement.

Requirements: PRD-SYS-001
"""

from datetime import UTC, datetime

import pytest
from execution.lock_models import (
    DataLockRecord,
    LockScopeEnum,
    LockStatusEnum,
)

import packages  # noqa: F401
from apps.execution.services.lock_enforcement import (
    DataLockEnforcer,
    FormLockedError,
)


def test_lock_enforcement_form_level_blocked() -> None:
    """Validate form-level lock blocks all modifications.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()
    form_lock = DataLockRecord(
        lock_id="dl_form_01",
        study_id="study_01",
        subject_id="sub_01",
        form_id="form_vs_01",
        scope=LockScopeEnum.FORM,
        status=LockStatusEnum.LOCKED,
        locked_by="dm_01",
        reason_for_change="Database freeze",
        locked_at=now_iso,
    )

    enforcer = DataLockEnforcer()

    with pytest.raises(FormLockedError) as exc:
        enforcer.assert_submission_allowed(
            form_id="form_vs_01",
            field_updates={"SYSBP": 120},
            active_locks=[form_lock],
        )

    assert "is in LOCKED state" in str(exc.value)


def test_lock_enforcement_field_level_blocked() -> None:
    """Validate field-level lock blocks modifications to the locked field only.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(UTC).isoformat()
    field_lock = DataLockRecord(
        lock_id="dl_field_01",
        study_id="study_01",
        subject_id="sub_01",
        form_id="form_vs_01",
        field_name="SYSBP",
        scope=LockScopeEnum.FIELD,
        status=LockStatusEnum.LOCKED,
        locked_by="dm_01",
        reason_for_change="Lock Systolic BP field",
        locked_at=now_iso,
    )

    enforcer = DataLockEnforcer()

    # Updating locked field SYSBP raises FormLockedError
    with pytest.raises(FormLockedError) as exc:
        enforcer.assert_submission_allowed(
            form_id="form_vs_01",
            field_updates={"SYSBP": 130, "DIABP": 80},
            active_locks=[field_lock],
        )
    assert "Field 'SYSBP'" in str(exc.value)

    # Updating unlocked field DIABP alone succeeds
    enforcer.assert_submission_allowed(
        form_id="form_vs_01",
        field_updates={"DIABP": 85},
        active_locks=[field_lock],
    )
