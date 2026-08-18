from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import (
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
)
from apps.quality.application.services.quality_service import QualityService
from apps.quality.presentation.dtos import (
    DeviationCreate,
    DeviationIngestRequest,
    DeviationResponse,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_quality_service() -> QualityService:
    import apps.quality.main as main_module

    return main_module.get_quality_service()


def get_user_context(principal: Principal):
    import apps.quality.main as main_module

    return main_module.get_user_context(principal)


def authorize_quality_write(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Read-only roles are restricted to read-only access.",
        )
    return principal.roles


def map_deviation_to_response(dev: Deviation) -> DeviationResponse:
    return DeviationResponse(
        id=dev.id,
        study_id=dev.study_id,
        site_id=dev.site_id,
        title=dev.title,
        description=dev.description,
        severity=dev.severity,
        status=dev.status,
        type=dev.type,
        category=dev.category,
        is_protocol_violation=dev.is_protocol_violation,
        impact_safety=dev.impact_safety,
        impact_data=dev.impact_data,
        impact_compliance=dev.impact_compliance,
        source_system=dev.source_system,
        source_reference_id=dev.source_reference_id,
        created_at=dev.created_at.isoformat(),
        created_by=dev.created_by,
        version_index=dev.version_index,
        reason_for_change=dev.reason_for_change,
    )


@router.post(
    "/api/v1/quality/deviations", response_model=DeviationResponse, status_code=201
)
@transactional
async def create_deviation(
    request: Request,
    payload: DeviationCreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    dev = await service.create_deviation(payload, user_id, user_role, change_reason)
    return map_deviation_to_response(dev)


@router.post(
    "/api/v1/quality/ingest/event", response_model=DeviationResponse, status_code=201
)
@transactional
async def ingest_quality_event(
    request: Request,
    payload: DeviationIngestRequest,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    dev = await service.ingest_quality_event(payload, user_id, user_role, change_reason)
    return map_deviation_to_response(dev)


@router.get("/api/v1/quality/deviations", response_model=list[DeviationResponse])
@transactional
async def list_deviations(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    status: DeviationStatus | None = Query(None, description="Filter by status"),
    severity: DeviationSeverity | None = Query(None, description="Filter by severity"),
    type: DeviationType | None = Query(None, description="Filter by deviation type"),
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    deviations = await service.list_deviations(
        study_id, site_id, status, severity, type, user_id, user_role
    )
    return [map_deviation_to_response(dev) for dev in deviations]


@router.get("/api/v1/quality/deviations/{id}", response_model=DeviationResponse)
@transactional
async def view_deviation(
    request: Request,
    id: str,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    dev = await service.view_deviation(id, user_id, user_role)
    return map_deviation_to_response(dev)
