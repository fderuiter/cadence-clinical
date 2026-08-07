"""Pydantic v2 transport models for Delegation of Authority (DOA) API serialization.

Requirements: PRD-SYS-001
"""

from datetime import date, datetime

from pydantic import BaseModel, Field


class SiteStaffMemberCreate(BaseModel):
    id: str = Field(..., description="Unique staff member identifier")
    site_id: str = Field(..., description="Target site identifier")
    user_id: str = Field(..., description="Central user identifier")
    first_name: str = Field(..., description="First name")
    last_name: str = Field(..., description="Last name")
    email: str = Field(..., description="Email address")
    primary_role: str = Field(..., description="Primary clinical role")
    license_number: str | None = Field(None, description="Professional license number")
    gcp_certified: bool = Field(False, description="GCP certification status")
    created_by: str = Field(..., description="Creator user ID")
    reason_for_change: str = Field(
        "Initial Staff Registration", description="GxP change reason"
    )


class SiteStaffMemberResponse(BaseModel):
    id: str
    site_id: str
    user_id: str
    first_name: str
    last_name: str
    email: str
    primary_role: str
    license_number: str | None = None
    gcp_certified: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int
    is_active: bool
    is_deleted: bool


class DOADelegationRecordCreate(BaseModel):
    id: str = Field(..., description="Unique record identifier")
    site_id: str = Field(..., description="Target site identifier")
    staff_user_id: str = Field(..., description="Staff member user ID")
    task_code: str = Field(..., description="Task delegation code")
    start_date: date = Field(..., description="Start date of delegation")
    end_date: date | None = Field(None, description="End date of delegation")
    status: str = Field("PENDING_PI_APPROVAL", description="Delegation status")
    pi_signature_hash: str | None = Field(None, description="PI eSignature hash")
    pi_approved_at: datetime | None = Field(None, description="PI approval timestamp")
    created_by: str = Field(..., description="Creator user ID")
    reason_for_change: str = Field(..., description="GxP change reason")


class DOADelegationRecordResponse(BaseModel):
    id: str
    site_id: str
    staff_user_id: str
    task_code: str
    start_date: date
    end_date: date | None = None
    status: str
    pi_signature_hash: str | None = None
    pi_approved_at: datetime | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int
    is_active: bool
    is_deleted: bool
