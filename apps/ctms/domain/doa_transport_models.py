from typing import Any

from pydantic import BaseModel, Field


class DelegationTaskRequest(BaseModel):
    site_id: str = Field(..., description="The unique investigator site ID")
    staff_user_id: str = Field(
        ..., description="Unique user Keycloak subject ID of the staff member"
    )
    task_codes: list[str] = Field(
        ..., description="List of delegated trial duty task codes"
    )
    start_date: str = Field(
        ..., description="Effective start date of the delegation (YYYY-MM-DD)"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP justification reason for delegation"
    )


class DOALogResponse(BaseModel):
    site_id: str = Field(..., description="Investigator site ID")
    pi_name: str = Field(
        ..., description="Name of the Principal Investigator at the site"
    )
    delegated_staff: list[dict[str, Any]] = Field(
        ...,
        description="List of active and inactive delegated site staff members and their tasks",
    )
    audit_history: list[dict[str, Any]] = Field(
        ...,
        description="Immutable chronologically-ordered CTMS audit log history for this site's DOA",
    )


class RevokeDelegationRequest(BaseModel):
    record_id: str = Field(..., description="The unique delegation record ID to revoke")
    reason_for_change: str = Field(
        ..., description="Mandatory justification reason for revocation"
    )


class DOASignOffRequest(BaseModel):
    record_id: str = Field(
        ..., description="The unique delegation record ID to sign off"
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP 21 CFR Part 11 justification reason"
    )
