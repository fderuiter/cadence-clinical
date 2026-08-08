"""
Pydantic Request/Response Schemas for Organization Directory microservice.
"""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator

from apps.org.domain.models import ClinicalStaffRole, OrganizationType


class OrganizationCreate(BaseModel):
    name: str = Field(..., description="Name of the organization")
    org_type: OrganizationType = Field(..., description="Type of the organization")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class OrganizationUpdate(BaseModel):
    name: str | None = Field(None, description="Updated name of the organization")
    org_type: OrganizationType | None = Field(
        None, description="Updated type of the organization"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class OrganizationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    name: str
    org_type: str
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class PersonnelAssignmentCreate(BaseModel):
    site_id: str = Field(..., description="The clinical site ID")
    study_id: str = Field(..., description="The clinical study ID")
    is_active: bool = Field(True, description="Whether the assignment is active")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelAssignmentUpdate(BaseModel):
    site_id: str | None = Field(None, description="Updated clinical site ID")
    study_id: str | None = Field(None, description="Updated clinical study ID")
    is_active: bool | None = Field(None, description="Updated active status")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelAssignmentResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    personnel_id: str
    site_id: str
    study_id: str
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class AssignmentResolutionResponse(BaseModel):
    personnel_id: str
    roles: list[str]
    assigned_sites: list[str]
    assigned_studies: list[str]


class SiteCreate(BaseModel):
    site_id: str = Field(
        ..., description="Unique client-defined identifier for the site"
    )
    name: str = Field(..., description="Name of the site")
    organization_id: str = Field(
        ..., description="Reference to the parent organization ID"
    )
    study_id: str | None = Field(None, description="Optional clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class SiteUpdate(BaseModel):
    site_id: str | None = Field(None, description="Updated identifier for the site")
    name: str | None = Field(None, description="Updated name of the site")
    organization_id: str | None = Field(
        None, description="Updated reference to parent organization ID"
    )
    study_id: str | None = Field(None, description="Updated clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class SiteResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    site_id: str
    name: str
    organization_id: str
    study_id: str | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class PersonnelCreate(BaseModel):
    keycloak_user_id: str | None = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: str = Field(..., description="First name")
    last_name: str = Field(..., description="Last name")
    email: str = Field(..., description="Unique email address")
    role: ClinicalStaffRole = Field(..., description="Clinical staff role")
    organization_id: str | None = Field(
        None, description="Reference to parent organization ID"
    )
    site_id: str | None = Field(None, description="Reference to parent site_id")
    study_id: str | None = Field(None, description="Optional clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelUpdate(BaseModel):
    keycloak_user_id: str | None = Field(
        None, description="OIDC user ID linked to this staff member"
    )
    first_name: str | None = Field(None, description="Updated first name")
    last_name: str | None = Field(None, description="Updated last name")
    email: str | None = Field(None, description="Updated email address")
    role: ClinicalStaffRole | None = Field(
        None, description="Updated clinical staff role"
    )
    organization_id: str | None = Field(
        None, description="Updated reference to parent organization ID"
    )
    site_id: str | None = Field(None, description="Updated reference to parent site_id")
    study_id: str | None = Field(None, description="Updated clinical study ID")
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class PersonnelResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    keycloak_user_id: str | None = None
    first_name: str
    last_name: str
    email: str
    role: str
    organization_id: str | None = None
    site_id: str | None = None
    study_id: str | None = None
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class TrainingLogCreate(BaseModel):
    personnel_id: str = Field(..., description="The clinical personnel ID")
    site_id: str = Field(..., description="The clinical site ID")
    study_id: str = Field(..., description="The clinical study ID")
    training_topic: str = Field(
        ..., description="The training topic or certification name"
    )
    completion_date: datetime = Field(
        ..., description="Date when training was completed"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class TrainingLogUpdate(BaseModel):
    personnel_id: str | None = Field(None, description="Updated personnel ID")
    site_id: str | None = Field(None, description="Updated site ID")
    study_id: str | None = Field(None, description="Updated study ID")
    training_topic: str | None = Field(None, description="Updated training topic")
    completion_date: datetime | None = Field(
        None, description="Updated completion date"
    )
    reason_for_change: str | None = Field(
        None, description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str | None) -> str | None:
        if v is not None and not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class TrainingLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    personnel_id: str
    site_id: str
    study_id: str
    training_topic: str
    completion_date: datetime
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int
    signature_manifestation: dict[str, Any] | None = None
    signer: str | None = None
    signing_timestamp: datetime | None = None


class TrainingLogSignRequest(BaseModel):
    payload: dict[str, Any] = Field(
        ..., description="The canonical training log payload to sign"
    )
    signature: str = Field(
        ..., description="The HMAC-SHA256 signature validating payload integrity"
    )
    reason_for_change: str = Field(
        ..., description="The justification reason for the sign-off action"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationCreate(BaseModel):
    delegator_id: str = Field(
        ..., description="The Personnel ID of the delegator (typically the PI)"
    )
    delegatee_id: str = Field(..., description="The Personnel ID of the delegatee")
    site_id: str = Field(
        ..., description="The site ID where authority is being delegated"
    )
    study_id: str = Field(..., description="The study ID")
    duties: list[str] = Field(..., description="List of delegated duties")
    start_date: datetime = Field(
        ..., description="The effective start date of the delegation"
    )
    end_date: datetime | None = Field(
        None, description="Optional effective end date of the delegation"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationSign(BaseModel):
    payload: dict = Field(
        ..., description="The canonical delegation payload to sign and verify"
    )
    signature: str = Field(
        ..., description="The symmetric HMAC canonical signature of the payload"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 change justification reason"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationRevoke(BaseModel):
    reason_for_change: str = Field(
        ..., description="The revocation reason / Part 11 justification"
    )

    @field_validator("reason_for_change")
    @classmethod
    def validate_reason(cls, v: str) -> str:
        if not v.strip():
            raise ValueError(
                "Reason for change cannot be empty or consist only of whitespace."
            )
        return v


class DelegationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    delegator_id: str
    delegatee_id: str
    site_id: str
    study_id: str
    duties: list[str]
    start_date: datetime
    end_date: datetime | None = None
    is_active: bool
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    signature: str | None = None
    signed_payload: dict | None = None
    signed_at: datetime | None = None
    signed_by: str | None = None

    revoked_at: datetime | None = None
    revoked_by: str | None = None
    revocation_reason: str | None = None


class OrgAuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: datetime
    actor_id: str
    actor_role: str
    action: str
    record_id: str | None = None
    details: str
    reason_for_change: str
