"""FastAPI Router for Quality microservice."""

import os

from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.application.services.quality_service import QualityService
from apps.quality.infrastructure.database import transactional
from apps.quality.infrastructure.models import (
    CAPARecord,
    CAPAStatus,
    Deviation,
    DeviationStatus,
    RootCauseAnalysis,
)
from apps.quality.presentation.dtos import (
    AuditLogResponse,
    CAPACreate,
    CAPAResponse,
    CAPATransitionRequest,
    CAPAUpdate,
    DeviationCreate,
    DeviationResponse,
    RCACreateOrUpdate,
    RCAResponse,
)
from packages.security.rbac import (
    Principal,
    get_principal,
    has_permission,
)

router = APIRouter()


def get_quality_service() -> QualityService:
    import apps.quality.main as main_module

    return main_module.get_quality_service()


def authorize_quality_write(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Read-only roles are restricted to read-only access.",
        )
    return principal.roles


def authorize_quality_oversight(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:investigate"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Quality oversight role required for CAPA approval or closure.",
        )
    return principal.roles


def get_user_context(principal: Principal):
    import apps.quality.main as main_module

    return main_module.get_user_context(principal)


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
        is_protocol_violation=dev.is_protocol_violation,
        created_at=dev.created_at.isoformat(),
        created_by=dev.created_by,
        version_index=dev.version_index,
        reason_for_change=dev.reason_for_change,
    )


def map_rca_to_response(rca: RootCauseAnalysis) -> RCAResponse:
    return RCAResponse(
        id=rca.id,
        deviation_id=rca.deviation_id,
        methodology=rca.methodology,
        investigation_details=rca.investigation_details,
        root_cause_summary=rca.root_cause_summary,
        study_id=rca.study_id,
        site_id=rca.site_id,
        created_at=rca.created_at.isoformat(),
        created_by=rca.created_by,
        version_index=rca.version_index,
        reason_for_change=rca.reason_for_change,
    )


def map_capa_to_response(capa: CAPARecord) -> CAPAResponse:
    return CAPAResponse(
        id=capa.id,
        deviation_id=capa.deviation_id,
        rca_id=capa.rca_id,
        capa_type=capa.capa_type,
        action_plan=capa.action_plan,
        status=capa.status,
        preventive_measures=capa.preventive_measures,
        target_completion_date=(
            capa.target_completion_date.isoformat()
            if capa.target_completion_date
            else None
        ),
        study_id=capa.study_id,
        site_id=capa.site_id,
        created_at=capa.created_at.isoformat(),
        created_by=capa.created_by,
        version_index=capa.version_index,
        reason_for_change=capa.reason_for_change,
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


@router.get("/api/v1/quality/deviations", response_model=list[DeviationResponse])
@transactional
async def list_deviations(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    status: DeviationStatus | None = Query(None, description="Filter by status"),
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    deviations = await service.list_deviations(
        study_id, site_id, status, user_id, user_role
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


@router.post("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
@router.put("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
@transactional
async def create_or_update_rca(
    request: Request,
    id: str,
    payload: RCACreateOrUpdate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    rca = await service.create_or_update_rca(
        id, payload, user_id, user_role, change_reason
    )
    return map_rca_to_response(rca)


@router.post("/api/v1/quality/capas", response_model=CAPAResponse, status_code=201)
@transactional
async def create_capa(
    request: Request,
    payload: CAPACreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.create_capa(payload, user_id, user_role, change_reason)
    return map_capa_to_response(capa)


@router.post("/api/v1/quality/capas/{id}/transition", response_model=CAPAResponse)
@transactional
async def transition_capa(
    request: Request,
    id: str,
    payload: CAPATransitionRequest,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    if payload.to_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
        authorize_quality_oversight(principal)

        from packages.security.middleware import (
            downstream_replay_cache,
            verify_sig_token,
        )
        from packages.security.regulated_actions import SemanticAction

        sig_token = request.headers.get("X-Sig-Token") or request.headers.get(
            "x-sig-token"
        )
        secret = os.getenv(
            "GATEWAY_SECRET", default="internal-gateway-secret-12345"
        ).encode()
        expected_semantic = (
            SemanticAction.CAPA_CLOSE
            if payload.to_status == CAPAStatus.CLOSED
            else SemanticAction.CAPA_CANCEL
        )

        success, result = verify_sig_token(
            sig_token=sig_token,
            user_id=principal.user_id,
            request_path=request.url.path,
            secret=secret,
            replay_cache=downstream_replay_cache,
            expected_semantic_action=expected_semantic,
            check_replay=False,
        )
        if not success:
            raise HTTPException(status_code=401, detail="REAUTHENTICATION_REQUIRED")
    else:
        authorize_quality_write(principal)

    user_id, user_role, change_reason = get_user_context(principal)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )
    capa = await service.transition_capa(
        id, payload.to_status, payload.version_index, user_id, user_role, change_reason
    )
    return map_capa_to_response(capa)


@router.put("/api/v1/quality/capas/{id}", response_model=CAPAResponse)
@transactional
async def update_capa(
    request: Request,
    id: str,
    payload: CAPAUpdate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.update_capa(id, payload, user_id, user_role, change_reason)
    return map_capa_to_response(capa)


@router.get("/api/v1/quality/audit-logs", response_model=list[AuditLogResponse])
@transactional
async def list_audit_logs(
    request: Request,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    logs = await service.list_audit_logs(user_id, user_role)
    return [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat() if log.timestamp else None,
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
            change_reason=log.change_reason,
        )
        for log in logs
    ]
