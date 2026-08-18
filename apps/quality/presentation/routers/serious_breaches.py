from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import SeriousBreachRecord
from apps.quality.application.services.serious_breach_service import (
    SeriousBreachService,
)
from apps.quality.presentation.dtos import (
    RegulatoryClockStatusResponse,
    SeriousBreachConfirmRequest,
    SeriousBreachReportRequest,
    SeriousBreachResponse,
    SeriousBreachStatusUpdate,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_serious_breach_service() -> SeriousBreachService:
    import apps.quality.main as main_module

    return main_module.get_serious_breach_service()


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
            detail="Forbidden: Quality oversight role required.",
        )
    return principal.roles


def map_breach_to_response(b: SeriousBreachRecord) -> SeriousBreachResponse:
    return SeriousBreachResponse(
        id=b.id,
        study_id=b.study_id,
        site_id=b.site_id,
        title=b.title,
        summary=b.summary,
        event_date=b.event_date.isoformat(),
        discovery_date=b.discovery_date.isoformat(),
        confirmation_date=b.confirmation_date.isoformat()
        if b.confirmation_date
        else None,
        reporting_deadline=b.reporting_deadline.isoformat()
        if b.reporting_deadline
        else None,
        affected_authorities=b.affected_authorities,
        status=b.status,
        regulatory_clock_hours_remaining=b.regulatory_clock_hours_remaining,
        lead_qa_id=b.lead_qa_id,
        created_at=b.created_at.isoformat(),
        created_by=b.created_by,
        version_index=b.version_index,
        reason_for_change=b.reason_for_change,
    )


@router.post(
    "/api/v1/quality/serious-breaches",
    response_model=SeriousBreachResponse,
    status_code=201,
)
@transactional
async def report_serious_breach(
    request: Request,
    payload: SeriousBreachReportRequest,
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    breach = await service.report_serious_breach(
        payload, user_id, user_role, change_reason
    )
    return map_breach_to_response(breach)


@router.get(
    "/api/v1/quality/serious-breaches", response_model=list[SeriousBreachResponse]
)
@transactional
async def list_serious_breaches(
    request: Request,
    study_id: str | None = Query(None, description="Filter by study ID"),
    status: str | None = Query(None, description="Filter by status"),
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    user_id, user_role, change_reason = get_user_context(principal)
    breaches = await service.list_serious_breaches(study_id, status, user_id, user_role)
    return [map_breach_to_response(b) for b in breaches]


@router.get(
    "/api/v1/quality/serious-breaches/{id}", response_model=SeriousBreachResponse
)
@transactional
async def get_serious_breach(
    request: Request,
    id: str,
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    breach = await service.repo.get_serious_breach_by_id(id)
    if not breach:
        raise HTTPException(status_code=404, detail="Serious breach not found")
    return map_breach_to_response(breach)


@router.post(
    "/api/v1/quality/serious-breaches/{id}/confirm",
    response_model=SeriousBreachResponse,
)
@transactional
async def confirm_serious_breach(
    request: Request,
    id: str,
    payload: SeriousBreachConfirmRequest,
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_oversight(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    breach = await service.confirm_serious_breach(
        id, payload.affected_authorities, user_id, user_role, change_reason
    )
    return map_breach_to_response(breach)


@router.get(
    "/api/v1/quality/serious-breaches/{id}/clock",
    response_model=RegulatoryClockStatusResponse,
)
@transactional
async def get_regulatory_clock_status(
    request: Request,
    id: str,
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    return await service.get_regulatory_clock_status(id)


@router.put(
    "/api/v1/quality/serious-breaches/{id}/status", response_model=SeriousBreachResponse
)
@transactional
async def update_serious_breach_status(
    request: Request,
    id: str,
    payload: SeriousBreachStatusUpdate,
    service: SeriousBreachService = Depends(get_serious_breach_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_oversight(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    breach = await service.update_breach_status(
        id, payload.status, user_id, user_role, change_reason
    )
    return map_breach_to_response(breach)
