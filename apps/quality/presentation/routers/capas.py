import os

from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import (
    CAPAActionItem,
    CAPAEffectivenessCheck,
    CAPARecord,
    CAPAStatus,
)
from apps.quality.application.services.quality_service import QualityService
from apps.quality.presentation.dtos import (
    CAPAActionItemCreate,
    CAPAActionItemResponse,
    CAPAActionItemUpdate,
    CAPACreate,
    CAPAEffectivenessCheckCreate,
    CAPAEffectivenessCheckResponse,
    CAPAResponse,
    CAPATransitionRequest,
    CAPAUpdate,
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


def authorize_quality_oversight(principal: Principal) -> list[str]:
    if not has_permission(principal, "quality_event:investigate"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Quality oversight role required for CAPA approval or closure.",
        )
    return principal.roles


def map_action_item_to_response(item: CAPAActionItem) -> CAPAActionItemResponse:
    return CAPAActionItemResponse(
        id=item.id,
        capa_id=item.capa_id,
        title=item.title,
        description=item.description,
        action_type=item.action_type,
        assigned_to=item.assigned_to,
        due_date=item.due_date.isoformat() if item.due_date else None,
        status=item.status,
        completed_at=item.completed_at.isoformat() if item.completed_at else None,
        evidence_url=item.evidence_url,
        created_at=item.created_at.isoformat(),
        created_by=item.created_by,
        version_index=item.version_index,
        reason_for_change=item.reason_for_change,
    )


def map_effectiveness_check_to_response(
    check: CAPAEffectivenessCheck,
) -> CAPAEffectivenessCheckResponse:
    return CAPAEffectivenessCheckResponse(
        id=check.id,
        capa_id=check.capa_id,
        planned_date=check.planned_date.isoformat(),
        executed_date=check.executed_date.isoformat() if check.executed_date else None,
        metric_evaluated=check.metric_evaluated,
        baseline_value=check.baseline_value,
        target_value=check.target_value,
        actual_value=check.actual_value,
        outcome=check.outcome,
        evaluator_id=check.evaluator_id,
        comments=check.comments,
        created_at=check.created_at.isoformat(),
        created_by=check.created_by,
        version_index=check.version_index,
        reason_for_change=check.reason_for_change,
    )


def map_capa_to_response(capa: CAPARecord) -> CAPAResponse:
    action_items_resp = (
        [map_action_item_to_response(item) for item in capa.action_items]
        if "action_items" in capa.__dict__ and capa.action_items
        else []
    )
    eff_checks_resp = (
        [map_effectiveness_check_to_response(c) for c in capa.effectiveness_checks]
        if "effectiveness_checks" in capa.__dict__ and capa.effectiveness_checks
        else []
    )

    return CAPAResponse(
        id=capa.id,
        deviation_id=capa.deviation_id,
        rca_id=capa.rca_id,
        capa_type=capa.capa_type,
        action_plan=capa.action_plan,
        status=capa.status,
        preventive_measures=capa.preventive_measures,
        risk_level=capa.risk_level,
        lead_investigator_id=capa.lead_investigator_id,
        qa_approver_id=capa.qa_approver_id,
        target_completion_date=(
            capa.target_completion_date.isoformat()
            if capa.target_completion_date
            else None
        ),
        actual_completion_date=(
            capa.actual_completion_date.isoformat()
            if capa.actual_completion_date
            else None
        ),
        effectiveness_interval_days=capa.effectiveness_interval_days,
        effectiveness_review_date=(
            capa.effectiveness_review_date.isoformat()
            if capa.effectiveness_review_date
            else None
        ),
        effectiveness_outcome=capa.effectiveness_outcome,
        recurrence_detected=capa.recurrence_detected,
        audit_finding_id=capa.audit_finding_id,
        study_id=capa.study_id,
        site_id=capa.site_id,
        created_at=capa.created_at.isoformat(),
        created_by=capa.created_by,
        version_index=capa.version_index,
        reason_for_change=capa.reason_for_change,
        action_items=action_items_resp,
        effectiveness_checks=eff_checks_resp,
    )


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


@router.get("/api/v1/quality/capas", response_model=list[CAPAResponse])
@transactional
async def list_capas(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    status: CAPAStatus | None = Query(None, description="Filter by status"),
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    capas = await service.repo.get_capas()
    filtered = []
    for c in capas:
        if study_id and c.study_id != study_id:
            continue
        if status and c.status != status:
            continue
        filtered.append(c)
    return [map_capa_to_response(c) for c in filtered]


@router.get("/api/v1/quality/capas/{id}", response_model=CAPAResponse)
@transactional
async def view_capa(
    request: Request,
    id: str,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.repo.get_capa_by_id(id)
    if not capa:
        raise HTTPException(status_code=404, detail="CAPA not found")
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


@router.post(
    "/api/v1/quality/capas/{id}/action-items",
    response_model=CAPAActionItemResponse,
    status_code=201,
)
@transactional
async def create_action_item(
    request: Request,
    id: str,
    payload: CAPAActionItemCreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    item = await service.create_action_item(
        id, payload, user_id, user_role, change_reason
    )
    return map_action_item_to_response(item)


@router.put(
    "/api/v1/quality/capas/action-items/{item_id}",
    response_model=CAPAActionItemResponse,
)
@transactional
async def update_action_item_status(
    request: Request,
    item_id: str,
    payload: CAPAActionItemUpdate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    item = await service.update_action_item_status(
        item_id, payload.status, payload.evidence_url, user_id, user_role, change_reason
    )
    return map_action_item_to_response(item)


@router.post(
    "/api/v1/quality/capas/{id}/effectiveness",
    response_model=CAPAEffectivenessCheckResponse,
    status_code=201,
)
@transactional
async def record_effectiveness_check(
    request: Request,
    id: str,
    payload: CAPAEffectivenessCheckCreate,
    service: QualityService = Depends(get_quality_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_oversight(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    check = await service.record_effectiveness_evaluation(
        id, payload, user_id, user_role, change_reason
    )
    return map_effectiveness_check_to_response(check)
