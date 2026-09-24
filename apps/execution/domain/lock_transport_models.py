"""Pydantic transport schemas for granular data locking and unlocking REST API.

Requirements: PRD-SYS-001, PRD-SYS-002, PRD-MDR-002, Trace-1, Trace-3, Trace-13, Trace-17
"""

from pydantic import BaseModel, Field

from .lock_models import DataLockRecord, LockScopeEnum


class DataLockRequest(BaseModel):
    """Request payload to execute form, item group, field, subject, visit, site, or study data locking."""

    study_id: str | None = Field(None, description="Target protocol study ID")
    site_id: str | None = Field(None, description="Target site ID")
    subject_id: str | None = Field(None, description="Target subject ID")
    visit_id: str | None = Field(None, description="Target visit ID")
    form_id: str | None = Field(None, description="Target eCRF form ID")
    item_group_id: str | None = Field(
        None, description="Optional target item group code"
    )
    field_name: str | None = Field(
        None, description="Optional target field variable name"
    )
    scope: LockScopeEnum | str | None = Field(
        None,
        description="Lock scope: STUDY, TRIAL, SITE, SUBJECT, VISIT, FORM, ITEM_GROUP, FIELD",
    )
    scope_type: str | None = Field(
        None, description="Scope type name: STUDY, SITE, SUBJECT, VISIT, FORM, FIELD"
    )
    scope_id: str | None = Field(
        None, description="Target identifier for the specified scope"
    )
    action: str = Field(
        "LOCK", description="Action to perform: LOCK, FREEZE, HARD_LOCK, UNLOCK"
    )
    lock_type: str | None = Field(
        None, description="Lock classification: FROZEN, LOCKED, HARD_LOCK, SOFT_LOCK"
    )
    reason_for_change: str | None = Field(
        None, description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
    reason: str | None = Field(
        None, description="Convenience alias for reason_for_change"
    )
    justification: str | None = Field(
        None, description="Mandatory >=50 character justification for unlock operations"
    )
    lock_id: str | None = Field(
        None, description="Specific lock ID targeted for unlock operation"
    )


class DataLockResponse(BaseModel):
    """Response payload for data lock/unlock operations."""

    lock_id: str = Field(..., description="Lock record identifier")
    status: str = Field(
        ..., description="Resulting status: LOCKED, FROZEN, HARD_LOCK, UNLOCKED"
    )
    message: str = Field(..., description="Operation result confirmation message")
    record: DataLockRecord | dict | None = Field(
        None, description="Updated data lock record"
    )
    scope_type: str | None = Field(None, description="Scope type")
    scope_id: str | None = Field(None, description="Scope ID")
    lock_type: str | None = Field(None, description="Lock type")
    is_active: bool = Field(True, description="Active status")
    locked_at: str | None = Field(None, description="Lock timestamp")
    unlocked_at: str | None = Field(None, description="Unlock timestamp")


class LockStatusResponse(BaseModel):
    """Status summary of all in-memory and global locks."""

    locked_sites: list[str] = Field(default_factory=list)
    locked_visits: list[str] = Field(default_factory=list)
    locked_forms: list[str] = Field(default_factory=list)
    locked_subjects: list[str] = Field(default_factory=list)
    trial_locked: bool = False
