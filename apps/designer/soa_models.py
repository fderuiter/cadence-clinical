"""
Typed Schedule of Activities (SoA) contracts and models for Designer module.

Imports models from the central protocol_authoring.soa package to enforce a single source of truth.
"""

from protocol_authoring.soa import (
    AuditMetadata,
    CreateEpochRequest,
    CreateProcedureRequest,
    CreateStudyArmRequest,
    CreateTimingWindowRequest,
    CreateVisitRequest,
    EpochProperties,
    LinkArmApplicabilityRequest,
    LinkEpochVisitRequest,
    LinkTimingRequest,
    LinkVisitProcedureRequest,
    ProcedureProperties,
    ProjectionCell,
    SoAEntityCreatedResponse,
    SoAEntityDetail,
    SoALinkResponse,
    StudyArmProperties,
    TimingWindowProperties,
    UpdateEpochRequest,
    UpdateProcedureRequest,
    UpdateStudyArmRequest,
    UpdateTimingWindowRequest,
    UpdateVisitRequest,
    VisitProperties,
)

__all__ = [
    "AuditMetadata",
    "CreateEpochRequest",
    "CreateProcedureRequest",
    "CreateStudyArmRequest",
    "CreateTimingWindowRequest",
    "CreateVisitRequest",
    "EpochProperties",
    "LinkArmApplicabilityRequest",
    "LinkEpochVisitRequest",
    "LinkTimingRequest",
    "LinkVisitProcedureRequest",
    "ProcedureProperties",
    "ProjectionCell",
    "SoAEntityCreatedResponse",
    "SoAEntityDetail",
    "SoALinkResponse",
    "StudyArmProperties",
    "TimingWindowProperties",
    "UpdateEpochRequest",
    "UpdateProcedureRequest",
    "UpdateStudyArmRequest",
    "UpdateTimingWindowRequest",
    "UpdateVisitRequest",
    "VisitProperties",
]
