from fastapi import APIRouter, Depends, status

from apps.ctms.adapters.repositories import (
    SQLAlchemCTMSDelegationRepository,
    SQLAlchemyRBQMRepository,
    get_ctms_repository,
    get_rbqm_repository,
)
from apps.ctms.application.rbqm_service import RBQMService
from apps.ctms.presentation.dtos import (
    RBQMKRIMetricCreate,
    RBQMKRIMetricResponse,
    SiteRiskScoreResponse,
)
from packages.security.rbac import Principal, get_principal

router = APIRouter(prefix="/api/v1/ctms/rbqm", tags=["CTMS RBQM"])


def get_rbqm_service(
    rbqm_repo: SQLAlchemyRBQMRepository = Depends(get_rbqm_repository),
    doa_repo: SQLAlchemCTMSDelegationRepository = Depends(get_ctms_repository),
) -> RBQMService:
    return RBQMService(rbqm_repo=rbqm_repo, doa_repo=doa_repo)


@router.post(
    "/kri", response_model=RBQMKRIMetricResponse, status_code=status.HTTP_201_CREATED
)
async def record_kri_metric(
    payload: RBQMKRIMetricCreate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
) -> RBQMKRIMetricResponse:
    entity = await service.record_kri_metric(
        study_id=payload.study_id,
        site_id=payload.site_id,
        metric_type=payload.metric_type,
        metric_value=payload.metric_value,
        threshold_low=payload.threshold_low,
        threshold_high=payload.threshold_high,
        notes=payload.notes,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason or "KRI calculation run",
    )
    return RBQMKRIMetricResponse(
        id=entity.id or "",
        study_id=entity.study_id,
        site_id=entity.site_id,
        metric_type=entity.metric_type,
        metric_value=entity.metric_value,
        threshold_low=entity.threshold_low,
        threshold_high=entity.threshold_high,
        breach_status=entity.breach_status,
        calculation_date=entity.calculation_date,
        notes=entity.notes,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/kri", response_model=list[RBQMKRIMetricResponse])
async def list_kri_metrics(
    study_id: str,
    site_id: str | None = None,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
) -> list[RBQMKRIMetricResponse]:
    entities = await service.list_kri_metrics(study_id, site_id)
    return [
        RBQMKRIMetricResponse(
            id=e.id or "",
            study_id=e.study_id,
            site_id=e.site_id,
            metric_type=e.metric_type,
            metric_value=e.metric_value,
            threshold_low=e.threshold_low,
            threshold_high=e.threshold_high,
            breach_status=e.breach_status,
            calculation_date=e.calculation_date,
            notes=e.notes,
            created_at=e.created_at,
            created_by=e.created_by,
            reason_for_change=e.reason_for_change,
            version_index=e.version_index,
        )
        for e in entities
    ]


@router.post("/sites/{site_id}/evaluate-risk", response_model=SiteRiskScoreResponse)
async def evaluate_site_risk_score(
    site_id: str,
    study_id: str,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
) -> SiteRiskScoreResponse:
    entity = await service.compute_site_risk_score(
        study_id=study_id,
        site_id=site_id,
        user_id=principal.user_id,
        user_roles=",".join(principal.raw_roles),
        reason_for_change=principal.change_reason
        or "Automated site risk scoring evaluation",
    )
    return SiteRiskScoreResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        composite_score=entity.composite_score,
        risk_level=entity.risk_level,
        assessment_date=entity.assessment_date,
        recommended_monitoring_type=entity.recommended_monitoring_type,
        monitoring_interval_days=entity.monitoring_interval_days,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )


@router.get("/sites/{site_id}/risk-score", response_model=SiteRiskScoreResponse | None)
async def get_site_risk_score(
    site_id: str,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
) -> SiteRiskScoreResponse | None:
    entity = await service.get_latest_risk_score(site_id)
    if not entity:
        return None
    return SiteRiskScoreResponse(
        id=entity.id,
        study_id=entity.study_id,
        site_id=entity.site_id,
        composite_score=entity.composite_score,
        risk_level=entity.risk_level,
        assessment_date=entity.assessment_date,
        recommended_monitoring_type=entity.recommended_monitoring_type,
        monitoring_interval_days=entity.monitoring_interval_days,
        created_at=entity.created_at,
        created_by=entity.created_by,
        reason_for_change=entity.reason_for_change,
        version_index=entity.version_index,
    )
