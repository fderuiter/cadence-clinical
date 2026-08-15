from fastapi import APIRouter, Depends, HTTPException, status

from apps.ctms.adapters.clients import QualityClient, SafetyClient
from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyProtocolDeviationRepository,
    get_ctms_repository,
    get_protocol_deviation_repository,
)
from apps.ctms.application.deviations_service import ProtocolDeviationService
from apps.ctms.domain.exceptions import ActionItemNotFoundError, DeviationNotFoundError
from apps.ctms.presentation.dtos import (
    DeviationActionItemComplete,
    DeviationActionItemCreate,
    DeviationActionItemResponse,
    ProtocolDeviationCreate,
    ProtocolDeviationRCA,
    ProtocolDeviationResponse,
)
from packages.security.rbac import Principal, get_principal

router = APIRouter(prefix="/api/v1/ctms/deviations", tags=["CTMS Protocol Deviations"])


def get_deviations_service(
    deviation_repo: SQLAlchemyProtocolDeviationRepository = Depends(
        get_protocol_deviation_repository
    ),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> ProtocolDeviationService:
    return ProtocolDeviationService(
        deviation_repo=deviation_repo,
        quality_client=QualityClient(),
        safety_client=SafetyClient(),
        doa_repo=doa_repo,
    )


@router.post(
    "", response_model=ProtocolDeviationResponse, status_code=status.HTTP_201_CREATED
)
async def log_protocol_deviation(
    payload: ProtocolDeviationCreate,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> ProtocolDeviationResponse:
    entity = await service.log_deviation(
        study_id=payload.study_id,
        site_id=payload.site_id,
        deviation_category=payload.deviation_category,
        severity=payload.severity,
        title=payload.title,
        description=payload.description,
        date_occurred=payload.date_occurred,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Protocol deviation reporting",
        subject_id=payload.subject_id,
        visit_id=payload.visit_id,
    )
    return ProtocolDeviationResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        subject_id=entity.subject_id,
        visit_id=entity.visit_id,
        deviation_category=entity.deviation_category,
        severity=entity.severity,
        title=entity.title,
        description=entity.description,
        date_occurred=entity.date_occurred,
        date_identified=entity.date_identified,
        status=entity.status,
        root_cause_5whys=entity.root_cause_5whys,
        root_cause_summary=entity.root_cause_summary,
        corrective_action_plan=entity.corrective_action_plan,
        preventive_action_plan=entity.preventive_action_plan,
        quality_capa_id=entity.quality_capa_id,
        reported_by=entity.reported_by,
        resolved_by=entity.resolved_by,
        resolved_at=entity.resolved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post("/{deviation_id}/rca", response_model=ProtocolDeviationResponse)
async def perform_root_cause_analysis(
    deviation_id: str,
    payload: ProtocolDeviationRCA,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> ProtocolDeviationResponse:
    try:
        entity = await service.perform_root_cause_analysis(
            deviation_id=deviation_id,
            root_cause_5whys=payload.root_cause_5whys,
            root_cause_summary=payload.root_cause_summary,
            corrective_action_plan=payload.corrective_action_plan,
            preventive_action_plan=payload.preventive_action_plan,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "5-Why Root Cause Analysis submission",
        )
    except DeviationNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return ProtocolDeviationResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        subject_id=entity.subject_id,
        visit_id=entity.visit_id,
        deviation_category=entity.deviation_category,
        severity=entity.severity,
        title=entity.title,
        description=entity.description,
        date_occurred=entity.date_occurred,
        date_identified=entity.date_identified,
        status=entity.status,
        root_cause_5whys=entity.root_cause_5whys,
        root_cause_summary=entity.root_cause_summary,
        corrective_action_plan=entity.corrective_action_plan,
        preventive_action_plan=entity.preventive_action_plan,
        quality_capa_id=entity.quality_capa_id,
        reported_by=entity.reported_by,
        resolved_by=entity.resolved_by,
        resolved_at=entity.resolved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post("/{deviation_id}/escalate-capa", response_model=ProtocolDeviationResponse)
async def escalate_to_quality_capa(
    deviation_id: str,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> ProtocolDeviationResponse:
    try:
        entity = await service.escalate_to_quality_capa(
            deviation_id=deviation_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Formal escalation to Quality CAPA",
        )
    except DeviationNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return ProtocolDeviationResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        subject_id=entity.subject_id,
        visit_id=entity.visit_id,
        deviation_category=entity.deviation_category,
        severity=entity.severity,
        title=entity.title,
        description=entity.description,
        date_occurred=entity.date_occurred,
        date_identified=entity.date_identified,
        status=entity.status,
        root_cause_5whys=entity.root_cause_5whys,
        root_cause_summary=entity.root_cause_summary,
        corrective_action_plan=entity.corrective_action_plan,
        preventive_action_plan=entity.preventive_action_plan,
        quality_capa_id=entity.quality_capa_id,
        reported_by=entity.reported_by,
        resolved_by=entity.resolved_by,
        resolved_at=entity.resolved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post("/{deviation_id}/resolve", response_model=ProtocolDeviationResponse)
async def resolve_protocol_deviation(
    deviation_id: str,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> ProtocolDeviationResponse:
    try:
        entity = await service.resolve_deviation(
            deviation_id=deviation_id,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason
            or "Deviation resolution and closeout",
        )
    except DeviationNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return ProtocolDeviationResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        subject_id=entity.subject_id,
        visit_id=entity.visit_id,
        deviation_category=entity.deviation_category,
        severity=entity.severity,
        title=entity.title,
        description=entity.description,
        date_occurred=entity.date_occurred,
        date_identified=entity.date_identified,
        status=entity.status,
        root_cause_5whys=entity.root_cause_5whys,
        root_cause_summary=entity.root_cause_summary,
        corrective_action_plan=entity.corrective_action_plan,
        preventive_action_plan=entity.preventive_action_plan,
        quality_capa_id=entity.quality_capa_id,
        reported_by=entity.reported_by,
        resolved_by=entity.resolved_by,
        resolved_at=entity.resolved_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("", response_model=list[ProtocolDeviationResponse])
async def list_protocol_deviations(
    study_id: str,
    site_id: str | None = None,
    severity: str | None = None,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> list[ProtocolDeviationResponse]:
    entities = await service.list_deviations(study_id, site_id, severity)
    return [
        ProtocolDeviationResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            subject_id=e.subject_id,
            visit_id=e.visit_id,
            deviation_category=e.deviation_category,
            severity=e.severity,
            title=e.title,
            description=e.description,
            date_occurred=e.date_occurred,
            date_identified=e.date_identified,
            status=e.status,
            root_cause_5whys=e.root_cause_5whys,
            root_cause_summary=e.root_cause_summary,
            corrective_action_plan=e.corrective_action_plan,
            preventive_action_plan=e.preventive_action_plan,
            quality_capa_id=e.quality_capa_id,
            reported_by=e.reported_by,
            resolved_by=e.resolved_by,
            resolved_at=e.resolved_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post(
    "/{deviation_id}/action-items",
    response_model=DeviationActionItemResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_deviation_action_item(
    deviation_id: str,
    payload: DeviationActionItemCreate,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> DeviationActionItemResponse:
    entity = await service.create_action_item(
        deviation_id=deviation_id,
        site_id=payload.site_id,
        description=payload.description,
        assignee_user_id=payload.assignee_user_id,
        assignee_role=payload.assignee_role,
        due_date=payload.due_date,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "Action item assignment",
    )
    return DeviationActionItemResponse(
        id=entity.id or "",
        deviation_id=entity.deviation_id,
        site_id=entity.site_id,
        description=entity.description,
        assignee_user_id=entity.assignee_user_id,
        assignee_role=entity.assignee_role,
        due_date=entity.due_date,
        status=entity.status,
        resolution_notes=entity.resolution_notes,
        completed_by=entity.completed_by,
        completed_at=entity.completed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.post(
    "/action-items/{action_item_id}/complete",
    response_model=DeviationActionItemResponse,
)
async def complete_deviation_action_item(
    action_item_id: str,
    payload: DeviationActionItemComplete,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> DeviationActionItemResponse:
    try:
        entity = await service.complete_action_item(
            action_item_id=action_item_id,
            resolution_notes=payload.resolution_notes,
            user_id=principal.user_id,
            user_roles=",".join(principal.raw_roles),
            reason_for_change=principal.change_reason or "Action item completed",
        )
    except ActionItemNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))

    return DeviationActionItemResponse(
        id=entity.id or "",
        deviation_id=entity.deviation_id,
        site_id=entity.site_id,
        description=entity.description,
        assignee_user_id=entity.assignee_user_id,
        assignee_role=entity.assignee_role,
        due_date=entity.due_date,
        status=entity.status,
        resolution_notes=entity.resolution_notes,
        completed_by=entity.completed_by,
        completed_at=entity.completed_at,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/action-items", response_model=list[DeviationActionItemResponse])
async def list_deviation_action_items(
    deviation_id: str | None = None,
    site_id: str | None = None,
    service: ProtocolDeviationService = Depends(get_deviations_service),
    principal: Principal = Depends(get_principal),
) -> list[DeviationActionItemResponse]:
    entities = await service.list_action_items(deviation_id, site_id)
    return [
        DeviationActionItemResponse(
            id=e.id or "",
            deviation_id=e.deviation_id,
            site_id=e.site_id,
            description=e.description,
            assignee_user_id=e.assignee_user_id,
            assignee_role=e.assignee_role,
            due_date=e.due_date,
            status=e.status,
            resolution_notes=e.resolution_notes,
            completed_by=e.completed_by,
            completed_at=e.completed_at,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]
