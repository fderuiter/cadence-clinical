"""Pydantic schemas for Quality service presentation layer."""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field

from apps.quality.infrastructure.models import (
    CAPAStatus,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
)


class DeviationCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(None, description="Optional clinical site ID")
    title: str = Field(
        ..., max_length=255, description="A short summary of the deviation"
    )
    description: str = Field(..., description="Detailed explanation of the deviation")
    severity: DeviationSeverity = Field(
        ..., description="Severity level: MINOR, MAJOR, CRITICAL"
    )
    type: DeviationType = Field(
        ..., description="Type of deviation, e.g., INFORMED_CONSENT"
    )
    is_protocol_violation: bool = Field(
        False, description="Whether this constitutes a protocol violation"
    )


class DeviationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str | None = None
    title: str
    description: str
    severity: DeviationSeverity
    status: DeviationStatus
    type: DeviationType
    is_protocol_violation: bool
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RCACreateOrUpdate(BaseModel):
    methodology: str = Field(
        ..., max_length=255, description="RCA methodology used, e.g., 5 Whys, Fishbone"
    )
    investigation_details: str = Field(
        ..., description="Full details of the investigation"
    )
    root_cause_summary: str = Field(
        ..., description="Summary of the determined root cause"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class RCAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    methodology: str
    investigation_details: str
    root_cause_summary: str
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPACreate(BaseModel):
    deviation_id: str = Field(..., description="Reference to the parent deviation ID")
    rca_id: str | None = Field(
        None, description="Optional reference to the Root Cause Analysis ID"
    )
    capa_type: str = Field(..., description="Type of CAPA: CORRECTIVE or PREVENTIVE")
    action_plan: str = Field(
        ..., description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )


class CAPATransitionRequest(BaseModel):
    to_status: CAPAStatus = Field(
        ..., description="Target CAPA Status to transition to"
    )
    version_index: int | None = Field(
        None, description="Expected version index for optimistic locking"
    )


class CAPAUpdate(BaseModel):
    action_plan: str | None = Field(
        None, description="The planned corrective/preventive action steps"
    )
    preventive_measures: str | None = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: datetime | None = Field(
        None, description="Optional expected completion timestamp"
    )
    version_index: int | None = Field(
        None, description="Current expected version index for optimistic locking"
    )


class CAPAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    rca_id: str | None = None
    capa_type: str
    action_plan: str
    status: CAPAStatus
    preventive_measures: str | None = None
    target_completion_date: str | None = None
    study_id: str
    site_id: str | None = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class AuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str
    record_id: str | None = None
    change_reason: str | None = None
