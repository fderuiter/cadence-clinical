from fastapi import APIRouter, Depends, HTTPException, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import RootCauseAnalysis
from apps.quality.application.services.quality_service import QualityService
from apps.quality.presentation.dtos import RCACreateOrUpdate, RCAResponse
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


def map_rca_to_response(rca: RootCauseAnalysis) -> RCAResponse:
    return RCAResponse(
        id=rca.id,
        deviation_id=rca.deviation_id,
        methodology=rca.methodology,
        investigation_details=rca.investigation_details,
        root_cause_summary=rca.root_cause_summary,
        five_whys_chain=rca.five_whys_chain,
        fishbone_categories=rca.fishbone_categories,
        contributing_factors=rca.contributing_factors,
        study_id=rca.study_id,
        site_id=rca.site_id,
        created_at=rca.created_at.isoformat(),
        created_by=rca.created_by,
        version_index=rca.version_index,
        reason_for_change=rca.reason_for_change,
    )


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
