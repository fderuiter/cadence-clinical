"""FastAPI router for granular data locking and unlocking API endpoints.

Requirements: PRD-SYS-001
"""

import uuid
from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException

import packages  # noqa: F401
from apps.execution.domain.lock_models import (
    DataLockRecord,
    LockStatusEnum,
)
from apps.execution.domain.lock_transport_models import (
    DataLockRequest,
    DataLockResponse,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/execution/locks", tags=["DataLock"])

# In-memory store for active lock records
_LOCK_STORE: dict[str, DataLockRecord] = {}


@router.post("/lock", response_model=DataLockResponse)
async def lock_data_endpoint(
    payload: DataLockRequest,
    current_user: dict = Depends(get_current_user),
) -> DataLockResponse:
    """Execute form, item-group, or field-level data lock or freeze operation.

    Requirements: PRD-SYS-001
    """
    if not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is required for data locking operations.",
        )

    lock_id = f"dl_{uuid.uuid4().hex[:8]}"
    now_iso = datetime.now(UTC).isoformat()
    action = payload.action.upper()

    status = LockStatusEnum.FROZEN if action == "FREEZE" else LockStatusEnum.LOCKED

    record = DataLockRecord(
        lock_id=lock_id,
        study_id=payload.study_id,
        subject_id=payload.subject_id,
        form_id=payload.form_id,
        item_group_id=payload.item_group_id,
        field_name=payload.field_name,
        scope=payload.scope,
        status=status,
        locked_by=current_user.get("sub", "datamanager_user"),
        reason_for_change=payload.reason_for_change,
        locked_at=now_iso,
    )

    _LOCK_STORE[lock_id] = record

    return DataLockResponse(
        lock_id=lock_id,
        status=status.value,
        message=f"Data successfully {status.value.lower()} for scope {payload.scope.value}",
        record=record,
    )


@router.post("/unlock", response_model=DataLockResponse)
async def unlock_data_endpoint(
    payload: DataLockRequest,
    current_user: dict = Depends(get_current_user),
) -> DataLockResponse:
    """Execute GxP data unlock override operation.

    Requirements: PRD-SYS-001
    """
    if not payload.reason_for_change.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is required for data unlock override.",
        )

    lock_id = f"dl_{uuid.uuid4().hex[:8]}"
    now_iso = datetime.now(UTC).isoformat()

    record = DataLockRecord(
        lock_id=lock_id,
        study_id=payload.study_id,
        subject_id=payload.subject_id,
        form_id=payload.form_id,
        item_group_id=payload.item_group_id,
        field_name=payload.field_name,
        scope=payload.scope,
        status=LockStatusEnum.UNLOCKED,
        locked_by=current_user.get("sub", "datamanager_user"),
        reason_for_change=payload.reason_for_change,
        locked_at=now_iso,
    )

    _LOCK_STORE[lock_id] = record

    return DataLockResponse(
        lock_id=lock_id,
        status=LockStatusEnum.UNLOCKED.value,
        message="Data lock successfully unlocked with GxP audit override",
        record=record,
    )


@router.get("/status/{form_id}", response_model=list[DataLockRecord])
async def get_form_lock_status_endpoint(
    form_id: str,
    current_user: dict = Depends(get_current_user),
) -> list[DataLockRecord]:
    """Retrieve active data locks for specified eCRF form submission.

    Requirements: PRD-SYS-001
    """
    return [r for r in _LOCK_STORE.values() if r.form_id == form_id]
