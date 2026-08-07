"""Integration test suite for granular data lock persistence, enforcement, and unlock override audit trail.

Requirements: PRD-SYS-001
"""

from execution.lock_models import DataLockRecord, LockStatusEnum
from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.tests.test_lock_router import _make_auth_headers
from apps.execution.main import app
from apps.execution.services.lock_enforcement import (
    DataLockEnforcer,
    FormLockedError,
)

client = TestClient(app)


def test_granular_locking_end_to_end_audit_flow() -> None:
    """Validate full end-to-end data lock, enforcement block, unlock override, and audit trail flow.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers(
        user_id="dm_lead_100",
        roles="datamanager",
        change_reason="Freeze data for interim analysis",
    )

    form_id = "form_audit_e2e_01"
    study_id = "study_audit_e2e"
    subject_id = "sub_audit_01"

    # Step 1: Execute form-level lock via API endpoint
    res_lock = client.post(
        "/api/v1/execution/locks/lock",
        json={
            "study_id": study_id,
            "subject_id": subject_id,
            "form_id": form_id,
            "scope": "FORM",
            "action": "LOCK",
            "reason_for_change": "Freeze data for interim analysis",
        },
        headers=headers,
    )
    assert res_lock.status_code == 200
    lock_data = res_lock.json()
    assert lock_data["status"] == "LOCKED"
    assert "lock_id" in lock_data

    # Step 2: Fetch active form lock status via GET endpoint
    res_status = client.get(
        f"/api/v1/execution/locks/status/{form_id}", headers=headers
    )
    assert res_status.status_code == 200
    active_locks = [DataLockRecord(**r) for r in res_status.json()]
    assert len(active_locks) >= 1
    assert active_locks[0].status == LockStatusEnum.LOCKED

    # Step 3: Assert DataLockEnforcer blocks eCRF field submission while locked
    enforcer = DataLockEnforcer()
    try:
        enforcer.assert_submission_allowed(
            form_id=form_id,
            field_updates={"SYSBP": 125, "DIABP": 82},
            active_locks=active_locks,
        )
        assert False, "Should have raised FormLockedError"
    except FormLockedError as exc:
        assert "is in LOCKED state" in str(exc)

    # Step 4: Execute GxP Unlock Override via API endpoint
    unlock_headers = _make_auth_headers(
        user_id="dm_lead_100",
        roles="datamanager",
        change_reason="Unlock override approved for CRA query correction",
    )

    res_unlock = client.post(
        "/api/v1/execution/locks/unlock",
        json={
            "study_id": study_id,
            "subject_id": subject_id,
            "form_id": form_id,
            "scope": "FORM",
            "action": "UNLOCK",
            "reason_for_change": "Unlock override approved for CRA query correction",
        },
        headers=unlock_headers,
    )

    assert res_unlock.status_code == 200
    unlock_data = res_unlock.json()
    assert unlock_data["status"] == "UNLOCKED"

    # Step 5: Verify DataLockEnforcer permits submission when unlocked
    unlocked_record = DataLockRecord(**unlock_data["record"])
    enforcer.assert_submission_allowed(
        form_id=form_id,
        field_updates={"SYSBP": 125, "DIABP": 82},
        active_locks=[unlocked_record],
    )
