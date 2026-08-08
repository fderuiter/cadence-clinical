"""Pydantic transport schemas for granular data locking and unlocking REST API.

Requirements: PRD-SYS-001
"""

from pydantic import BaseModel, Field

from .lock_models import DataLockRecord, LockScopeEnum


class DataLockRequest(BaseModel):
    """Request payload to execute form, item group, or field-level data locking.

    Requirements: PRD-SYS-001
    """

    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target subject ID")
    form_id: str = Field(..., description="Target eCRF form ID")
    item_group_id: str | None = Field(
        None, description="Optional target item group code"
    )
    field_name: str | None = Field(
        None, description="Optional target field variable name"
    )
    scope: LockScopeEnum = Field(
        LockScopeEnum.FORM, description="Lock scope: FORM, ITEM_GROUP, FIELD"
    )
    action: str = Field("LOCK", description="Action to perform: LOCK, FREEZE, UNLOCK")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )


class DataLockResponse(BaseModel):
    """Response payload for data lock/unlock operations.

    Requirements: PRD-SYS-001
    """

    lock_id: str = Field(..., description="Lock record identifier")
    status: str = Field(..., description="Resulting status: LOCKED, FROZEN, UNLOCKED")
    message: str = Field(..., description="Operation result confirmation message")
    record: DataLockRecord = Field(..., description="Updated data lock record")
