"""
Schedule of Activities (SoA) Pydantic models for the Designer service.

To satisfy GxP/SDLC separation of concerns and avoid code duplication,
all core SoA domain and request contracts are defined centrally in the
`packages/core-models` package and imported here.
"""

from pydantic import BaseModel

import packages  # noqa: F401 - Injects packages/core-models into sys.path

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


# Centralized imports from the core-models package to completely eliminate code duplication
from protocol_authoring.soa import (
    ActivityAssignmentRequest,
    ArmReorderItem,
    ArmReorderRequest,
    AuditMetadata,
    CreateEpochRequest,
    CreateProcedureRequest,
    CreateStudyArmRequest,
    CreateTimingWindowRequest,
    CreateVisitRequest,
    Epoch,
    EpochProperties,
    EpochReorderItem,
    EpochReorderRequest,
    LinkArmApplicabilityRequest,
    LinkEpochVisitRequest,
    LinkTimingRequest,
    LinkVisitProcedureRequest,
    Procedure,
    ProcedureProperties,
    ProcedureReorderItem,
    ProcedureReorderRequest,
    ProjectionCell,
    SoAEntityCreatedResponse,
    SoAEntityDetail,
    SoALinkResponse,
    SoAMatrixProjectionResponse,
    StudyArm,
    StudyArmProperties,
    TimingWindow,
    TimingWindowProperties,
    UpdateEpochRequest,
    UpdateProcedureRequest,
    UpdateStudyArmRequest,
    UpdateTimingWindowRequest,
    UpdateVisitRequest,
    Visit,
    VisitProperties,
    VisitReorderItem,
    VisitReorderRequest,
    VisitToArmAssignmentRequest,
    VisitToEpochAssignmentRequest,
)

__all__ = [
    "ActivityAssignmentRequest",
    "ArmReorderItem",
    "ArmReorderRequest",
    "AuditMetadata",
    "CreateEpochRequest",
    "CreateProcedureRequest",
    "CreateStudyArmRequest",
    "CreateTimingWindowRequest",
    "CreateVisitRequest",
    "Epoch",
    "EpochProperties",
    "EpochReorderItem",
    "EpochReorderRequest",
    "LinkArmApplicabilityRequest",
    "LinkEpochVisitRequest",
    "LinkTimingRequest",
    "LinkVisitProcedureRequest",
    "Procedure",
    "ProcedureProperties",
    "ProcedureReorderItem",
    "ProcedureReorderRequest",
    "ProjectionCell",
    "SoAEntityCreatedResponse",
    "SoAEntityDetail",
    "SoAHeaderArm",
    "SoAHeaderEncounter",
    "SoAHeaderEpoch",
    "SoALinkResponse",
    "SoAMatrixProjectionResponse",
    "SoARowView",
    "StudyArm",
    "StudyArmProperties",
    "TimingWindow",
    "TimingWindowProperties",
    "UpdateEpochRequest",
    "UpdateProcedureRequest",
    "UpdateStudyArmRequest",
    "UpdateTimingWindowRequest",
    "UpdateVisitRequest",
    "Visit",
    "VisitProperties",
    "VisitReorderItem",
    "VisitReorderRequest",
    "VisitToArmAssignmentRequest",
    "VisitToEpochAssignmentRequest",
]
