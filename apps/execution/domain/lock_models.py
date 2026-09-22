"""Pydantic data models for granular multi-tier data locking and unlock governance.

Requirements: PRD-SYS-001, PRD-SYS-002, PRD-MDR-002, Trace-1, Trace-3, Trace-13, Trace-17
"""

from enum import StrEnum

from pydantic import BaseModel, Field


class LockScopeEnum(StrEnum):
    """Granular lock scope boundaries."""

    STUDY = "STUDY"
    TRIAL = "TRIAL"
    SITE = "SITE"
    SUBJECT = "SUBJECT"
    VISIT = "VISIT"
    FORM = "FORM"
    ITEM_GROUP = "ITEM_GROUP"
    FIELD = "FIELD"


class LockStatusEnum(StrEnum):
    """Data lock lifecycle status."""

    UNLOCKED = "UNLOCKED"
    FROZEN = "FROZEN"
    LOCKED = "LOCKED"
    HARD_LOCK = "HARD_LOCK"
    SOFT_LOCK = "SOFT_LOCK"


class DataLockRecord(BaseModel):
    """Data lock state record representing frozen or locked clinical eCRF data."""

    lock_id: str = Field(..., description="Unique data lock record identifier")
    study_id: str | None = Field(None, description="Target protocol study ID")
    site_id: str | None = Field(None, description="Target clinical site ID")
    subject_id: str | None = Field(None, description="Target clinical trial subject ID")
    visit_id: str | None = Field(None, description="Target clinical visit ID")
    form_id: str | None = Field(None, description="Target eCRF form submission ID")
    item_group_id: str | None = Field(
        None, description="Optional target item group code"
    )
    field_name: str | None = Field(
        None, description="Optional target field variable name"
    )
    scope: LockScopeEnum | str = Field(
        LockScopeEnum.FORM,
        description="Lock scope: STUDY, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD",
    )
    scope_type: str | None = Field(None, description="Scope type identifier string")
    scope_id: str | None = Field(
        None, description="Target entity ID for specified scope"
    )
    status: LockStatusEnum | str = Field(
        LockStatusEnum.LOCKED,
        description="Lock status: UNLOCKED, FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK",
    )
    lock_type: str | None = Field(
        None, description="Lock type string: FROZEN, LOCKED, HARD_LOCK"
    )
    is_active: bool = Field(
        True, description="Indicates if the lock is actively enforced"
    )
    locked_by: str = Field(..., description="User ID who executed data lock")
    created_by: str | None = Field(None, description="Alias for locked_by user ID")
    reason_for_change: str = Field(
        ..., description="GxP 21 CFR Part 11 justification reason"
    )
    locked_at: str = Field(..., description="UTC ISO timestamp of lock execution")
    created_at: str | None = Field(None, description="Alias for locked_at timestamp")
    unlocked_by: str | None = Field(
        None, description="User ID executing unlock override"
    )
    unlocked_at: str | None = Field(
        None, description="UTC ISO timestamp of unlock override"
    )
    unlock_justification: str | None = Field(
        None,
        description="Mandatory >=50 character GxP unlock justification reason",
    )
    signature_token: str | None = Field(
        None, description="21 CFR Part 11 step-up token digest"
    )


class DataUnlockRecord(BaseModel):
    """GxP audit record tracking data unlock override operations."""

    unlock_id: str = Field(..., description="Unique data unlock audit ID")
    lock_id: str = Field(..., description="Reference DataLockRecord ID being unlocked")
    unlocked_by: str = Field(..., description="User ID executing unlock override")
    reason_for_change: str = Field(
        ..., description="Mandatory GxP reason for unlocking data"
    )
    unlock_justification: str | None = Field(
        None, description="Mandatory >= 50 character unlock justification"
    )
    unlocked_at: str = Field(..., description="UTC ISO timestamp of unlock override")
