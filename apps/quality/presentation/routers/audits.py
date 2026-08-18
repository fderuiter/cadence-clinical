from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import AuditFinding, QualityAudit
from apps.quality.application.services.audit_service import ClinicalAuditService
from apps.quality.presentation.dtos import (
    AuditCreate,
    AuditFindingCreate,
    AuditFindingResponse,
    AuditResponse,
    AuditStatusUpdate,
    CAPAResponse,
    PromoteFindingToCAPARequest,
)
from apps.quality.presentation.routers.capas import map_capa_to_response
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_audit_service() -> ClinicalAuditService:
    import apps.quality.main as main_module

    return main_module.get_audit_service()


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


def map_finding_to_response(f: AuditFinding) -> AuditFindingResponse:
    return AuditFindingResponse(
        id=f.id,
        audit_id=f.audit_id,
        finding_number=f.finding_number,
        severity=f.severity,
        category=f.category,
        condition=f.condition,
        criteria=f.criteria,
        cause=f.cause,
        effect=f.effect,
        capa_id=f.capa_id,
        created_at=f.created_at.isoformat(),
        created_by=f.created_by,
        version_index=f.version_index,
        reason_for_change=f.reason_for_change,
    )


def map_audit_to_response(a: QualityAudit) -> AuditResponse:
    findings = (
        [map_finding_to_response(f) for f in a.findings]
        if "findings" in a.__dict__ and a.findings
        else []
    )
    return AuditResponse(
        id=a.id,
        audit_number=a.audit_number,
        study_id=a.study_id,
        site_id=a.site_id,
        vendor_name=a.vendor_name,
        audit_type=a.audit_type,
        lead_auditor=a.lead_auditor,
        planned_start_date=a.planned_start_date.isoformat(),
        planned_end_date=a.planned_end_date.isoformat(),
        actual_start_date=a.actual_start_date.isoformat()
        if a.actual_start_date
        else None,
        actual_end_date=a.actual_end_date.isoformat() if a.actual_end_date else None,
        status=a.status,
        scope_summary=a.scope_summary,
        findings=findings,
        created_at=a.created_at.isoformat(),
        created_by=a.created_by,
        version_index=a.version_index,
        reason_for_change=a.reason_for_change,
    )


# --- Audit Operations ---


@router.post("/api/v1/quality/audits", response_model=AuditResponse, status_code=201)
@transactional
async def create_audit(
    request: Request,
    payload: AuditCreate,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    audit = await service.create_audit(payload, user_id, user_role, change_reason)
    return map_audit_to_response(audit)


@router.get("/api/v1/quality/audits", response_model=list[AuditResponse])
@transactional
async def list_audits(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    site_id: str | None = Query(None, description="Filter by site ID"),
    audit_type: str | None = Query(None, description="Filter by audit type"),
    status: str | None = Query(None, description="Filter by audit status"),
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    audits = await service.list_audits(
        study_id, site_id, audit_type, status, user_id, user_role
    )
    return [map_audit_to_response(a) for a in audits]


@router.get("/api/v1/quality/audits/{id}", response_model=AuditResponse)
@transactional
async def get_audit(
    request: Request,
    id: str,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    audit = await service.get_audit_by_id(id, user_id, user_role)
    return map_audit_to_response(audit)


@router.put("/api/v1/quality/audits/{id}/status", response_model=AuditResponse)
@transactional
async def update_audit_status(
    request: Request,
    id: str,
    payload: AuditStatusUpdate,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    audit = await service.update_audit_status(
        audit_id=id,
        status=payload.status,
        actual_start_date=payload.actual_start_date,
        actual_end_date=payload.actual_end_date,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason,
    )
    return map_audit_to_response(audit)


# --- Findings & 1-Click CAPA Promotion ---


@router.post(
    "/api/v1/quality/audits/{id}/findings",
    response_model=AuditFindingResponse,
    status_code=201,
)
@transactional
async def create_audit_finding(
    request: Request,
    id: str,
    payload: AuditFindingCreate,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    finding = await service.create_finding(
        id, payload, user_id, user_role, change_reason
    )
    return map_finding_to_response(finding)


@router.get(
    "/api/v1/quality/audits/{id}/findings", response_model=list[AuditFindingResponse]
)
@transactional
async def list_audit_findings(
    request: Request,
    id: str,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    findings = await service.list_findings_by_audit(id)
    return [map_finding_to_response(f) for f in findings]


@router.post(
    "/api/v1/quality/audits/findings/{finding_id}/promote-capa",
    response_model=CAPAResponse,
    status_code=201,
)
@transactional
async def promote_finding_to_capa(
    request: Request,
    finding_id: str,
    payload: PromoteFindingToCAPARequest,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    capa = await service.promote_finding_to_capa(
        finding_id=finding_id,
        action_plan=payload.action_plan,
        preventive_measures=payload.preventive_measures,
        target_completion_date=payload.target_completion_date,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason,
    )
    return map_capa_to_response(capa)


# --- 1-Click Inspection Readiness Dossier ---


@router.get("/api/v1/quality/audits/inspection-dossier/{study_id}")
@transactional
async def get_inspection_readiness_dossier(
    request: Request,
    study_id: str,
    service: ClinicalAuditService = Depends(get_audit_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    return await service.compile_inspection_readiness_dossier(
        study_id, user_id, user_role
    )
