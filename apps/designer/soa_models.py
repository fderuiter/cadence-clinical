"""
Typed Schedule of Activities (SoA) contracts and models for Designer module.

Defines Pydantic v2 entity-specific contracts for StudyArm, Epoch, Visit, Procedure, TimingWindow,
relationships, audit metadata, and projection cells.
"""

from pydantic import BaseModel, Field, model_validator

from apps.designer.src.domain.protocol_authoring import (
    ActivityAssignmentRequest,
    AuditMetadata,
    CreateEpochRequest,
    CreateProcedureRequest,
    CreateStudyArmRequest,
    CreateTimingWindowRequest,
    CreateVisitRequest,
    Epoch,
    EpochProperties,
    LinkArmApplicabilityRequest,
    LinkEpochVisitRequest,
    LinkTimingRequest,
    LinkVisitProcedureRequest,
    Procedure,
    ProcedureProperties,
    ProjectionCell,
    SoAEntityCreatedResponse,
    SoAEntityDetail,
    SoALinkResponse,
    SoAMatrixProjectionResponse,
    StudyArm,
    StudyArmProperties,
    UpdateEpochRequest,
    UpdateProcedureRequest,
    UpdateStudyArmRequest,
    UpdateTimingWindowRequest,
    UpdateVisitRequest,
    Visit,
    VisitProperties,
    VisitReorderItem,
    VisitReorderRequest,
)
from apps.designer.src.domain.protocol_authoring import (
    TimingWindow as CoreTimingWindow,  # Subclassed below to add validation rules
)

__all__ = [
    "TimingWindow",
    "TimingWindowProperties",
    "ArmReorderItem",
    "ArmReorderRequest",
    "EpochReorderItem",
    "EpochReorderRequest",
    "ProcedureReorderItem",
    "ProcedureReorderRequest",
    "VisitToArmAssignmentRequest",
    "VisitToEpochAssignmentRequest",
    "ActivityAssignmentRequest",
    "AuditMetadata",
    "CreateEpochRequest",
    "CreateProcedureRequest",
    "CreateStudyArmRequest",
    "CreateTimingWindowRequest",
    "CreateVisitRequest",
    "Epoch",
    "EpochProperties",
    "LinkArmApplicabilityRequest",
    "LinkEpochVisitRequest",
    "LinkTimingRequest",
    "LinkVisitProcedureRequest",
    "Procedure",
    "ProcedureProperties",
    "ProjectionCell",
    "SoAEntityCreatedResponse",
    "SoAEntityDetail",
    "SoALinkResponse",
    "SoAMatrixProjectionResponse",
    "StudyArm",
    "StudyArmProperties",
    "UpdateEpochRequest",
    "UpdateProcedureRequest",
    "UpdateStudyArmRequest",
    "UpdateTimingWindowRequest",
    "UpdateVisitRequest",
    "Visit",
    "VisitProperties",
    "VisitReorderItem",
    "VisitReorderRequest",
]

# Import header views from protocol_render
try:
    from protocol_render import (
        SoACellView,
        SoAHeaderArm,
        SoAHeaderEncounter,
        SoAHeaderEpoch,
        SoARowView,
    )
except ImportError:
    try:
        from protocol_render.models import (
            SoACellView,
            SoAHeaderArm,
            SoAHeaderEncounter,
            SoAHeaderEpoch,
            SoARowView,
        )
    except ImportError:
        # Fallbacks if not in path during initialization
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


class TimingWindow(CoreTimingWindow):
    """
    Pydantic v2 model for a Timing Window with local range validation rules.
    """

    @model_validator(mode="after")
    def validate_numeric_ranges_domain(self) -> TimingWindow:
        if self.max_offset is not None and self.max_offset < 0:
            raise ValueError("max_offset must not be negative.")
        if self.min_offset is not None and self.max_offset is not None:
            if self.min_offset > self.max_offset:
                raise ValueError(
                    "Field 'min_offset' must be less than or equal to 'max_offset'. min_offset must not be greater than max_offset."
                )
        if self.target_day is not None and self.target_day < 0:
            raise ValueError("Field 'target_day' cannot be negative.")
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
                    "Field 'min_offset' must be less than or equal to 'max_offset'. min_offset must not be greater than max_offset."
                )
        if self.target_day is not None and self.target_day < 0:
            raise ValueError("Field 'target_day' cannot be negative.")
        return self


# --- Reordering and Assignment Request Contracts specific to designer ---


class ArmReorderItem(BaseModel):
    arm_id: str = Field(..., min_length=1)
    sequence: int = Field(..., ge=1)


class ArmReorderRequest(BaseModel):
    arms: list[ArmReorderItem] = Field(...)


class EpochReorderItem(BaseModel):
    epoch_id: str = Field(..., min_length=1)
    sequence: int = Field(..., ge=1)


class EpochReorderRequest(BaseModel):
    epochs: list[EpochReorderItem] = Field(...)


class ProcedureReorderItem(BaseModel):
    procedure_id: str = Field(..., min_length=1)
    sequence: int = Field(..., ge=1)


class ProcedureReorderRequest(BaseModel):
    procedures: list[ProcedureReorderItem] = Field(...)


class VisitToArmAssignmentRequest(BaseModel):
    arm_id: str = Field(..., min_length=1)
    visit_ids: list[str] = Field(..., min_length=1)


class VisitToEpochAssignmentRequest(BaseModel):
    epoch_id: str = Field(..., min_length=1)
    visit_ids: list[str] = Field(..., min_length=1)
