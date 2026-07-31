"""Pydantic data models for granular form, item-group, and field-level data locking.

Requirements: PRD-SYS-001
"""

from enum import StrEnum

from pydantic import BaseModel, Field


class LockScopeEnum(StrEnum):
    """Granular lock scope boundaries.

    Requirements: PRD-SYS-001
    """

    FORM = "FORM"
    ITEM_GROUP = "ITEM_GROUP"
    FIELD = "FIELD"


class LockStatusEnum(StrEnum):
    """Data lock lifecycle status.

    Requirements: PRD-SYS-001
    """

    UNLOCKED = "UNLOCKED"
    FROZEN = "FROZEN"
    LOCKED = "LOCKED"


class DataLockRecord(BaseModel):
    """Data lock state record representing frozen or locked clinical eCRF data.

    Requirements: PRD-SYS-001
    """

    lock_id: str = Field(..., description="Unique data lock record identifier")
    study_id: str = Field(..., description="Target protocol study ID")
    subject_id: str = Field(..., description="Target clinical trial subject ID")
    form_id: str = Field(..., description="Target eCRF form submission ID")
    item_group_id: str | None = Field(
        None, description="Optional target item group code"
    )
    field_name: str | None = Field(
        None, description="Optional target field variable name"
    )
    scope: LockScopeEnum = Field(..., description="Lock scope: FORM, ITEM_GROUP, FIELD")
    status: LockStatusEnum = Field(
        LockStatusEnum.LOCKED, description="Lock status: UNLOCKED, FROZEN, LOCKED"
    )
    locked_by: str = Field(..., description="User ID who executed data lock")
    reason_for_change: str = Field(
        ..., description="GxP 21 CFR Part 11 justification reason"
    )
    locked_at: str = Field(..., description="UTC ISO timestamp of lock execution")


class DataUnlockRecord(BaseModel):
    """GxP audit record tracking data unlock override operations.

    Requirements: PRD-SYS-001
    """

    unlock_id: str = Field(..., description="Unique data unlock audit ID")
    lock_id: str = Field(..., description="Reference DataLockRecord ID being unlocked")
    unlocked_by: str = Field(..., description="User ID executing unlock override")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP reason for unlocking data"
    )
    unlocked_at: str = Field(..., description="UTC ISO timestamp of unlock override")
