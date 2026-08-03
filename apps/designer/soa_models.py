"""
Typed Schedule of Activities (SoA) contracts and models for Designer module.

Defines Pydantic v2 entity-specific contracts for StudyArm, Epoch, Visit, Procedure, TimingWindow,
relationships, audit metadata, and projection cells.
"""

from datetime import UTC, datetime
from typing import Literal

from pydantic import BaseModel, Field, model_validator

# Try to import from protocol_render if available, otherwise define placeholders to ensure robust parsing
try:
    from protocol_render import (
        SoAHeaderArm,
        SoAHeaderEncounter,
        SoAHeaderEpoch,
        SoARowView,
    )
except ImportError:
    # Minimal fallback Pydantic definitions if not in PYTHONPATH during static analysis
    class SoAHeaderEpoch(BaseModel):
        epoch_id: str
        epoch_name: str
        sequence: int
        arm_id: str | None = None

    class SoAHeaderEncounter(BaseModel):
        encounter_id: str
        encounter_name: str
        epoch_id: str
        sequence: int
        arm_id: str | None = None

    class SoACellView(BaseModel):
        activity_id: str
        encounter_id: str
        epoch_id: str
        is_applicable: bool
        details: str | None = None
        arm_id: str | None = None
        derived_from_soa: bool = False

    class SoARowView(BaseModel):
        activity_id: str
        activity_name: str
        cells: list[SoACellView] = []

    class SoAHeaderArm(BaseModel):
        arm_id: str
        arm_name: str


# --- Task 1: Audit Metadata and Shared Convention ---


class SoAAuditMixin(BaseModel):
    """
    Shared 21 CFR Part 11 audit fields mixin/base model.
    Exposes created_at, created_by, updated_at, updated_by, reason_for_change, and version,
    mirroring ConceptDetail in main.py.
    """

    version: str = Field("1.0.0", description="Version identifier of the entity.")
    created_at: datetime = Field(
        default_factory=lambda: datetime.now(UTC),
        description="Timestamp of creation.",
    )
    created_by: str = Field(..., description="User ID of the creator.")
    updated_at: datetime | None = Field(
        None, description="Timestamp of the last update."
    )
    updated_by: str | None = Field(None, description="User ID of the last updater.")
    reason_for_change: str | None = Field(
        None, description="Justification/reason for the change."
    )

    model_config = {"extra": "allow"}


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
    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))


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


# --- Task 2: Define StudyArm and Epoch domain models ---


class StudyArm(SoAAuditMixin):
    """
    Pydantic v2 model for a clinical trial Study Arm.
    """

    id: str = Field(..., description="Unique identifier for the study arm.")
    study_id: str | None = Field(None, description="Study identifier scoping this arm.")
    study_version_id: str = Field(
        ..., description="Study version identifier scoping this arm."
    )
    name: str = Field(
        ...,
        min_length=1,
        description="The name of the study arm, e.g., 'Active' or 'Placebo'.",
    )
    arm_type: str = Field(
        ..., min_length=1, description="The classification type of the arm."
    )
    sequence: int | None = Field(None, ge=1, description="Sequential ordering rank.")

    @model_validator(mode="before")
    @classmethod
    def populate_type_and_arm_type(cls, data: dict) -> dict:
        if isinstance(data, dict):
            if "type" in data and "arm_type" not in data:
                data["arm_type"] = data["type"]
            elif "arm_type" in data and "type" not in data:
                data["type"] = data["arm_type"]
        return data


class Epoch(SoAAuditMixin):
    """
    Pydantic v2 model for a Study Epoch.
    """

    id: str = Field(..., description="Unique identifier for the epoch.")
    study_id: str | None = Field(
        None, description="Study identifier scoping this epoch."
    )
    study_version_id: str = Field(
        ..., description="Study version identifier scoping this epoch."
    )
    name: str = Field(
        ...,
        min_length=1,
        description="The name of the study epoch, e.g., 'Screening'.",
    )
    sequence_order: int = Field(
        ..., ge=1, description="Sequential ordering rank of the epoch."
    )

    @model_validator(mode="before")
    @classmethod
    def populate_epoch_fields(cls, data: dict) -> dict:
        if isinstance(data, dict):
            if "epoch_name" in data and "name" not in data:
                data["name"] = data["epoch_name"]
            elif "name" in data and "epoch_name" not in data:
                data["epoch_name"] = data["name"]
            if "sequence" in data and "sequence_order" not in data:
                data["sequence_order"] = data["sequence"]
            elif "sequence_order" in data and "sequence" not in data:
                data["sequence"] = data["sequence_order"]
        return data


# --- Task 3: Define Visit and Procedure domain models ---


class Visit(SoAAuditMixin):
    """
    Pydantic v2 model for a Visit / Encounter.
    """

    id: str = Field(..., description="Unique identifier for the visit.")
    study_id: str | None = Field(
        None, description="Study identifier scoping this visit."
    )
    study_version_id: str = Field(
        ..., description="Study version identifier scoping this visit."
    )
    name: str = Field(..., min_length=1, description="The display name of the visit.")
    epoch_id: str = Field(..., min_length=1, description="Epoch identifier reference.")
    sequence: int = Field(
        ..., ge=1, description="Sequential ordering rank of the visit."
    )
    visit_window_days: int | None = Field(
        None, description="Generalized window fields."
    )
    arm_ids: list[str] = Field(
        default_factory=list,
        description="IDs of study arms applicable to this visit.",
    )

    @model_validator(mode="before")
    @classmethod
    def populate_visit_fields(cls, data: dict) -> dict:
        if isinstance(data, dict):
            if "encounter_name" in data and "name" not in data:
                data["name"] = data["encounter_name"]
            elif "name" in data and "encounter_name" not in data:
                data["encounter_name"] = data["name"]
        return data


class Procedure(SoAAuditMixin):
    """
    Pydantic v2 model for a clinical Procedure / Activity.
    """

    id: str = Field(..., description="Unique identifier for the procedure.")
    study_id: str | None = Field(
        None, description="Study identifier scoping this procedure."
    )
    study_version_id: str = Field(
        ..., description="Study version identifier scoping this procedure."
    )
    name: str = Field(
        ..., min_length=1, description="The display name of the procedure."
    )
    description: str | None = Field(
        None, description="Detailed description of the procedure."
    )
    visit_ids: list[str] = Field(
        default_factory=list, description="Associated visit/encounter references."
    )
    arm_ids: list[str] = Field(
        default_factory=list,
        description="IDs of study arms applicable to this procedure.",
    )

    @model_validator(mode="before")
    @classmethod
    def populate_proc_fields(cls, data: dict) -> dict:
        if isinstance(data, dict):
            if "activity_name" in data and "name" not in data:
                data["name"] = data["activity_name"]
            elif "name" in data and "activity_name" not in data:
                data["activity_name"] = data["name"]
        return data


# --- Task 4: Define the TimingWindow and conditional-timing model ---


class TimingWindow(SoAAuditMixin):
    """
    Pydantic v2 model for a Timing Window.
    """

    id: str = Field(..., description="Unique identifier for the timing window.")
    study_id: str | None = Field(
        None, description="Study identifier scoping this timing window."
    )
    study_version_id: str = Field(
        ..., description="Study version identifier scoping this timing window."
    )
    name: str = Field(
        ...,
        min_length=1,
        description="Label or duration specification of the timing window.",
    )
    anchor_reference: str | None = Field(
        None, description="Anchor reference, e.g. a visit name."
    )
    target_day: int | None = Field(None, description="Target scheduled day.")
    min_offset: int | None = Field(None, description="Minimum day offset.")
    max_offset: int | None = Field(None, description="Maximum day offset.")
    conditional: bool = Field(
        False, description="Flag indicating if timing/applicability is conditional."
    )
    reason: str | None = Field(
        None,
        description="Mandatory justification reason required if conditional is True.",
    )

    @model_validator(mode="after")
    def validate_conditional_timing_reason(self) -> TimingWindow:
        if self.conditional and (not self.reason or not self.reason.strip()):
            raise ValueError(
                "A non-empty 'reason' must be provided when timing/applicability is conditional."
            )
        return self

    @model_validator(mode="after")
    def validate_numeric_ranges(self) -> TimingWindow:
        if self.max_offset is not None and self.max_offset < 0:
            raise ValueError("max_offset must not be negative.")
        if self.min_offset is not None and self.max_offset is not None:
            if self.min_offset > self.max_offset:
                raise ValueError(
                    "Field 'min_offset' must be less than or equal to 'max_offset'."
                )
        if self.target_day is not None and self.target_day < 0:
            raise ValueError("Field 'target_day' cannot be negative.")
        return self


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
    sequence: int | None = Field(None, ge=1, description="Sequential ordering rank.")


class EpochProperties(BaseModel):
    """
    Properties specific to a Study Epoch.
    """

    name: str | None = Field(
        None,
        min_length=1,
        description="The name of the study epoch, e.g., 'Screening'.",
    )
    epoch_name: str | None = Field(
        None, min_length=1, description="Alternate/legacy field name for epoch name."
    )
    sequence: int = Field(
        ..., ge=1, description="Sequential ordering rank of the epoch."
    )

    @model_validator(mode="after")
    def validate_epoch_name_fields(self) -> EpochProperties:
        if not self.name and not self.epoch_name:
            raise ValueError(
                "Either 'name' or 'epoch_name' must be provided and non-empty."
            )
        return self


class VisitProperties(BaseModel):
    """
    Properties specific to a Visit / Encounter.
    """

    name: str | None = Field(
        None, min_length=1, description="The display name of the visit."
    )
    encounter_name: str | None = Field(
        None,
        min_length=1,
        description="Alternate/legacy field name for encounter/visit.",
    )
    sequence: int = Field(
        ..., ge=1, description="Sequential ordering rank of the visit."
    )

    @model_validator(mode="after")
    def validate_visit_name_fields(self) -> VisitProperties:
        if not self.name and not self.encounter_name:
            raise ValueError(
                "Either 'name' or 'encounter_name' must be provided and non-empty."
            )
        return self


class ProcedureProperties(BaseModel):
    """
    Properties specific to a clinical Procedure / Activity.
    """

    name: str | None = Field(
        None, min_length=1, description="The display name of the procedure."
    )
    activity_name: str | None = Field(
        None, min_length=1, description="Alternate/legacy field name for the procedure."
    )

    @model_validator(mode="after")
    def validate_proc_name_fields(self) -> ProcedureProperties:
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
    anchor_reference: str | None = Field(
        None, description="Anchor reference, e.g. a visit name."
    )
    target_day: int | None = Field(None, description="Target scheduled day.")
    min_offset: int | None = Field(None, description="Minimum day offset.")
    max_offset: int | None = Field(None, description="Maximum day offset.")
    conditional: bool | None = Field(
        None,
        description="Flag indicating if the timing or applicability is conditional.",
    )
    reason: str | None = Field(
        None,
        min_length=1,
        description="Mandatory justification reason required if conditional is True.",
    )

    @model_validator(mode="after")
    def validate_conditional_timing_reason(self) -> TimingWindowProperties:
        if self.conditional and (not self.reason or not self.reason.strip()):
            raise ValueError(
                "A non-empty 'reason' must be provided when timing/applicability is conditional."
            )
        return self

    @model_validator(mode="after")
    def validate_numeric_ranges(self) -> TimingWindowProperties:
        if self.max_offset is not None and self.max_offset < 0:
            raise ValueError("max_offset must not be negative.")
        if self.min_offset is not None and self.max_offset is not None:
            if self.min_offset > self.max_offset:
                raise ValueError(
                    "Field 'min_offset' must be less than or equal to 'max_offset'."
                )
        if self.target_day is not None and self.target_day < 0:
            raise ValueError("Field 'target_day' cannot be negative.")
        return self


# --- Task 5: Endpoint Request / Response Contracts ---


class CreateStudyArmRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the study arm."
    )
    properties: StudyArmProperties
    change_reason: str = Field(
        default="Created study arm", description="Change reason for audit trail"
    )


class UpdateStudyArmRequest(BaseModel):
    properties: StudyArmProperties
    reason_for_change: str = Field(
        default="Updated study arm", description="Reason for change for audit trail"
    )


class CreateEpochRequest(BaseModel):
    id: str = Field(..., min_length=1, description="Unique identifier for the epoch.")
    properties: EpochProperties
    change_reason: str = Field(
        default="Created epoch", description="Change reason for audit trail"
    )


class UpdateEpochRequest(BaseModel):
    properties: EpochProperties
    reason_for_change: str = Field(
        default="Updated epoch", description="Reason for change for audit trail"
    )


class CreateVisitRequest(BaseModel):
    id: str = Field(..., min_length=1, description="Unique identifier for the visit.")
    properties: VisitProperties
    change_reason: str = Field(
        default="Created visit", description="Change reason for audit trail"
    )


class UpdateVisitRequest(BaseModel):
    properties: VisitProperties
    reason_for_change: str = Field(
        default="Updated visit", description="Reason for change for audit trail"
    )


class CreateProcedureRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the procedure."
    )
    properties: ProcedureProperties
    change_reason: str = Field(
        default="Created procedure", description="Change reason for audit trail"
    )


class UpdateProcedureRequest(BaseModel):
    properties: ProcedureProperties
    reason_for_change: str = Field(
        default="Updated procedure", description="Reason for change for audit trail"
    )


class CreateTimingWindowRequest(BaseModel):
    id: str = Field(
        ..., min_length=1, description="Unique identifier for the timing window."
    )
    properties: TimingWindowProperties
    change_reason: str = Field(
        default="Created timing window", description="Change reason for audit trail"
    )


class UpdateTimingWindowRequest(BaseModel):
    properties: TimingWindowProperties
    reason_for_change: str = Field(
        default="Updated timing window",
        description="Reason for change for audit trail",
    )


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
    details: str | None = Field(
        None, description="Optional timing windows, constraints, or instruction notes."
    )
    arm_id: str | None = Field(None, description="Optional associated arm ID.")
    derived_from_soa: bool = Field(
        False, description="Flag indicating selective lineage."
    )


class SoAMatrixProjectionResponse(BaseModel):
    """
    Response contract representing the complete Schedule of Activities (SoA) presentation matrix.
    Assembles the arm x epoch x visit x procedure structure with timing/conditional metadata.
    Follows existing designer response conventions.
    """

    epochs: list[SoAHeaderEpoch] = Field(
        default_factory=list, description="Ordered list of Study Epoch columns."
    )
    encounters: list[SoAHeaderEncounter] = Field(
        default_factory=list,
        description="Ordered list of Encounter/Visit sub-columns.",
    )
    rows: list[SoARowView] = Field(
        default_factory=list,
        description="Ordered list of row-wise activity procedures.",
    )
    arms: list[SoAHeaderArm] = Field(
        default_factory=list, description="Ordered list of Study Arm columns."
    )
