"""
Typed Schedule of Activities (SoA) contracts and models for Designer module.

Defines Pydantic v2 entity-specific contracts for StudyArm, Epoch, Visit, Procedure, TimingWindow,
relationships, audit metadata, and projection cells.
"""

from datetime import datetime, timezone
from typing import Literal, Optional

from pydantic import BaseModel, Field, model_validator

# --- Audit Metadata and Basic Response ---


class AuditMetadata(BaseModel):
    """
    Part 11/GxP audit metadata for any mutation operation.
    """

    user_id: str = Field(
        ..., min_length=1, description="Unique user identifier initiating the mutation."
    )
    change_reason: str = Field(
        ...,
        min_length=1,
        max_length=255,
        description="Auditable justification reason for this change.",
    )
    timestamp: datetime = Field(default_factory=lambda: datetime.now(timezone.utc))


class SoAEntityCreatedResponse(BaseModel):
    """
    Standard successful creation response.
    """

    status: str = "success"
    id: str


class SoAEntityDetail(BaseModel):
    """
    Standard details of a versioned SoA entity.
    """

    id: str
    version_index: int
    created_by: str
    created_at: str
    is_retired: bool = False
    is_deleted: bool = False

    model_config = {"extra": "allow"}


# --- Entity-Specific Properties Contracts ---


class StudyArmProperties(BaseModel):
    """
    Properties specific to a clinical trial Study Arm.
    """

    name: str = Field(
        ...,
        min_length=1,
        description="The name of the study arm, e.g., 'Active' or 'Placebo'.",
    )
    type: str = Field(
        ..., min_length=1, description="The classification type of the arm."
    )
    sequence: Optional[int] = Field(None, ge=1, description="Sequential ordering rank.")


class EpochProperties(BaseModel):
    """
    Properties specific to a Study Epoch.
    """

    name: Optional[str] = Field(
        None,
        min_length=1,
        description="The name of the study epoch, e.g., 'Screening'.",
    )
    epoch_name: Optional[str] = Field(
        None, min_length=1, description="Alternate/legacy field name for epoch name."
    )
    sequence: int = Field(
        ..., ge=1, description="Sequential ordering rank of the epoch."
    )

    @model_validator(mode="after")
    def validate_epoch_name_fields(self) -> "EpochProperties":
        if not self.name and not self.epoch_name:
            raise ValueError(
                "Either 'name' or 'epoch_name' must be provided and non-empty."
            )
        return self


class VisitProperties(BaseModel):
    """
    Properties specific to a Visit / Encounter.
    """

    name: Optional[str] = Field(
        None, min_length=1, description="The display name of the visit."
    )
    encounter_name: Optional[str] = Field(
        None,
        min_length=1,
        description="Alternate/legacy field name for encounter/visit.",
    )
    sequence: int = Field(
        ..., ge=1, description="Sequential ordering rank of the visit."
    )

    @model_validator(mode="after")
    def validate_visit_name_fields(self) -> "VisitProperties":
        if not self.name and not self.encounter_name:
            raise ValueError(
                "Either 'name' or 'encounter_name' must be provided and non-empty."
            )
        return self


class ProcedureProperties(BaseModel):
    """
    Properties specific to a clinical Procedure / Activity.
    """

    name: Optional[str] = Field(
        None, min_length=1, description="The display name of the procedure."
    )
    activity_name: Optional[str] = Field(
        None, min_length=1, description="Alternate/legacy field name for the procedure."
    )

    @model_validator(mode="after")
    def validate_proc_name_fields(self) -> "ProcedureProperties":
        if not self.name and not self.activity_name:
            raise ValueError(
                "Either 'name' or 'activity_name' must be provided and non-empty."
            )
        return self


class TimingWindowProperties(BaseModel):
    """
    Properties specific to a Timing Window. Enforces cross-field conditional justification.
    """

    name: str = Field(
        ...,
        min_length=1,
        description="Label or duration specification of the timing window.",
    )
    conditional: Optional[bool] = Field(
        None,
        description="Flag indicating if the timing or applicability is conditional.",
    )
    reason: Optional[str] = Field(
        None,
        min_length=1,
        description="Mandatory justification reason required if conditional is True.",
    )

    @model_validator(mode="after")
    def validate_conditional_timing_reason(self) -> "TimingWindowProperties":
        if self.conditional and (not self.reason or not self.reason.strip()):
            raise ValueError(
                "A non-empty 'reason' must be provided when timing/applicability is conditional."
            )
        return self


# --- Endpoint Request Contracts ---


class CreateStudyArmRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the study arm."
    )
    properties: StudyArmProperties


class UpdateStudyArmRequest(BaseModel):
    properties: StudyArmProperties


class CreateEpochRequest(BaseModel):
    id: str = Field(..., min_length=1, description="Unique identifier for the epoch.")
    properties: EpochProperties


class UpdateEpochRequest(BaseModel):
    properties: EpochProperties


class CreateVisitRequest(BaseModel):
    id: str = Field(..., min_length=1, description="Unique identifier for the visit.")
    properties: VisitProperties


class UpdateVisitRequest(BaseModel):
    properties: VisitProperties


class CreateProcedureRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the procedure."
    )
    properties: ProcedureProperties


class UpdateProcedureRequest(BaseModel):
    properties: ProcedureProperties


class CreateTimingWindowRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the timing window."
    )
    properties: TimingWindowProperties


class UpdateTimingWindowRequest(BaseModel):
    properties: TimingWindowProperties


# --- Association / Link Request Contracts ---


class LinkEpochVisitRequest(BaseModel):
    epoch_id: str = Field(..., min_length=1)
    visit_id: str = Field(..., min_length=1)


class LinkVisitProcedureRequest(BaseModel):
    visit_id: str = Field(..., min_length=1)
    procedure_id: str = Field(..., min_length=1)


class LinkTimingRequest(BaseModel):
    source_id: str = Field(..., min_length=1)
    timing_id: str = Field(..., min_length=1)
    source_type: Literal["visit", "procedure"] = Field("visit")


class LinkArmApplicabilityRequest(BaseModel):
    arm_id: str = Field(..., min_length=1)
    target_id: str = Field(..., min_length=1)
    target_type: Literal["visit", "procedure", "epoch"] = Field("visit")


class SoALinkResponse(BaseModel):
    status: str = "success"
    message: str = "Link established successfully"


# --- Projection Cells and Matrices ---


class ProjectionCell(BaseModel):
    activity_id: str = Field(..., description="Target activity/procedure identifier.")
    encounter_id: str = Field(..., description="Target encounter/visit identifier.")
    epoch_id: str = Field(..., description="Associated study epoch identifier.")
    is_applicable: bool = Field(
        ...,
        description="Whether the activity is planned to occur during this encounter.",
    )
    details: Optional[str] = Field(
        None, description="Optional timing windows, constraints, or instruction notes."
    )
    arm_id: Optional[str] = Field(None, description="Optional associated arm ID.")
    derived_from_soa: bool = Field(
        False, description="Flag indicating selective lineage."
    )
