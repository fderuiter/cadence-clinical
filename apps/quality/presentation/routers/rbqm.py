from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import (
    CentralizedMonitoringDossier,
    CtQFactor,
    KRIDefinition,
    KRIMetricEvaluation,
    QTLBreachEvent,
    QualityToleranceLimit,
    RACTRiskFactor,
    SiteRiskProfile,
)
from apps.quality.application.services.rbqm_service import RBQMService
from apps.quality.presentation.dtos import (
    CompileCSMDossierRequest,
    CSMDossierResponse,
    CtQFactorCreate,
    CtQFactorResponse,
    KRIBatchEvaluationRequest,
    KRIDefinitionCreate,
    KRIDefinitionResponse,
    KRIMetricEvaluationResponse,
    QTLBreachEventResponse,
    QTLCreate,
    QTLEvaluateBreachRequest,
    QTLResponse,
    RACTCategorySummary,
    RACTMatrixSummaryResponse,
    RACTRiskFactorCreate,
    RACTRiskFactorResponse,
    RACTRiskFactorUpdate,
    SiteRiskProfileResponse,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_rbqm_service() -> RBQMService:
    import apps.quality.main as main_module

    return main_module.get_rbqm_service()


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


def map_ctq_to_response(ctq: CtQFactor) -> CtQFactorResponse:
    return CtQFactorResponse(
        id=ctq.id,
        study_id=ctq.study_id,
        category=ctq.category,
        critical_aspect=ctq.critical_aspect,
        risk_description=ctq.risk_description,
        impact_area=ctq.impact_area,
        mitigation_strategy=ctq.mitigation_strategy,
        created_at=ctq.created_at.isoformat(),
        created_by=ctq.created_by,
        version_index=ctq.version_index,
        reason_for_change=ctq.reason_for_change,
    )


def map_kri_definition_to_response(kri: KRIDefinition) -> KRIDefinitionResponse:
    return KRIDefinitionResponse(
        id=kri.id,
        code=kri.code,
        name=kri.name,
        category=kri.category,
        description=kri.description,
        calculation_formula=kri.calculation_formula,
        green_threshold=kri.green_threshold,
        amber_threshold=kri.amber_threshold,
        red_threshold=kri.red_threshold,
        weight=kri.weight,
        is_active=kri.is_active,
        created_at=kri.created_at.isoformat(),
        created_by=kri.created_by,
        version_index=kri.version_index,
        reason_for_change=kri.reason_for_change,
    )


def map_kri_eval_to_response(e: KRIMetricEvaluation) -> KRIMetricEvaluationResponse:
    return KRIMetricEvaluationResponse(
        id=e.id,
        study_id=e.study_id,
        site_id=e.site_id,
        kri_code=e.kri_code,
        evaluation_date=e.evaluation_date.isoformat(),
        raw_value=e.raw_value,
        standardized_z_score=e.standardized_z_score,
        risk_tier=e.risk_tier,
        created_at=e.created_at.isoformat(),
        created_by=e.created_by,
        version_index=e.version_index,
        reason_for_change=e.reason_for_change,
    )


def map_profile_to_response(p: SiteRiskProfile) -> SiteRiskProfileResponse:
    return SiteRiskProfileResponse(
        id=p.id,
        study_id=p.study_id,
        site_id=p.site_id,
        evaluation_date=p.evaluation_date.isoformat(),
        composite_risk_score=p.composite_risk_score,
        risk_rank=p.risk_rank,
        percentile_rank=getattr(p, "percentile_rank", 0.0),
        risk_tier=getattr(p, "risk_tier", "LOW"),
        high_risk_kri_count=p.high_risk_kri_count,
        active_deviations_count=p.active_deviations_count,
        created_at=p.created_at.isoformat(),
        created_by=p.created_by,
        version_index=p.version_index,
        reason_for_change=p.reason_for_change,
    )


def map_qtl_to_response(q: QualityToleranceLimit) -> QTLResponse:
    return QTLResponse(
        id=q.id,
        study_id=q.study_id,
        parameter_name=q.parameter_name,
        target_value=q.target_value,
        tolerance_limit=q.tolerance_limit,
        unit=q.unit,
        is_breached=q.is_breached,
        breach_count=q.breach_count,
        created_at=q.created_at.isoformat(),
        created_by=q.created_by,
        version_index=q.version_index,
        reason_for_change=q.reason_for_change,
    )


def map_qtl_breach_to_response(b: QTLBreachEvent) -> QTLBreachEventResponse:
    return QTLBreachEventResponse(
        id=b.id,
        qtl_id=b.qtl_id,
        study_id=b.study_id,
        breach_date=b.breach_date.isoformat(),
        observed_value=b.observed_value,
        threshold_value=b.threshold_value,
        root_cause=b.root_cause,
        corrective_action_summary=b.corrective_action_summary,
        csr_narrative=b.csr_narrative,
        created_at=b.created_at.isoformat(),
        created_by=b.created_by,
        version_index=b.version_index,
        reason_for_change=b.reason_for_change,
    )


def map_ract_factor_to_response(f: RACTRiskFactor) -> RACTRiskFactorResponse:
    return RACTRiskFactorResponse(
        id=f.id,
        study_id=f.study_id,
        category=f.category,
        risk_topic=f.risk_topic,
        risk_description=f.risk_description,
        probability_score=f.probability_score,
        impact_score=f.impact_score,
        detectability_score=f.detectability_score,
        total_risk_score=f.total_risk_score,
        risk_tier=f.risk_tier,
        mitigation_strategy=f.mitigation_strategy,
        linked_kri_code=f.linked_kri_code,
        is_active=f.is_active,
        created_at=f.created_at.isoformat(),
        created_by=f.created_by,
        version_index=f.version_index,
        reason_for_change=f.reason_for_change,
    )


def map_dossier_to_response(d: CentralizedMonitoringDossier) -> CSMDossierResponse:
    return CSMDossierResponse(
        id=d.id,
        study_id=d.study_id,
        dossier_name=d.dossier_name,
        generated_at=d.generated_at.isoformat(),
        generated_by=d.generated_by,
        status=d.status,
        ract_summary=d.ract_summary,
        kri_surveillance_summary=d.kri_surveillance_summary,
        site_risk_rankings=d.site_risk_rankings,
        anomaly_forensics_summary=d.anomaly_forensics_summary,
        electronic_signature=d.electronic_signature,
        audit_trail_snapshot=d.audit_trail_snapshot,
        merkle_seal=d.merkle_seal,
        created_at=d.created_at.isoformat(),
        created_by=d.created_by,
        version_index=d.version_index,
        reason_for_change=d.reason_for_change,
    )


# --- TransCelerate RACT Endpoints (PRD-CSM-005) ---


@router.post(
    "/api/v1/quality/rbqm/ract/factors",
    response_model=RACTRiskFactorResponse,
    status_code=201,
)
@transactional
async def create_ract_risk_factor(
    request: Request,
    payload: RACTRiskFactorCreate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    factor = await service.create_ract_risk_factor(
        payload, user_id, user_role, change_reason
    )
    return map_ract_factor_to_response(factor)


@router.get(
    "/api/v1/quality/rbqm/ract/factors", response_model=list[RACTRiskFactorResponse]
)
@transactional
async def list_ract_risk_factors(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    category: str | None = Query(None, description="Optional RACT Category filter"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    factors = await service.list_ract_risk_factors(study_id, category=category)
    return [map_ract_factor_to_response(f) for f in factors]


@router.put(
    "/api/v1/quality/rbqm/ract/factors/{factor_id}",
    response_model=RACTRiskFactorResponse,
)
@transactional
async def update_ract_risk_factor(
    request: Request,
    factor_id: str,
    payload: RACTRiskFactorUpdate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    factor = await service.update_ract_risk_factor(
        factor_id, payload, user_id, user_role, change_reason
    )
    return map_ract_factor_to_response(factor)


@router.delete("/api/v1/quality/rbqm/ract/factors/{factor_id}")
@transactional
async def delete_ract_risk_factor(
    request: Request,
    factor_id: str,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    deleted = await service.delete_ract_risk_factor(
        factor_id, user_id, user_role, change_reason
    )
    return {"status": "DELETED", "id": factor_id, "deleted": deleted}


@router.post(
    "/api/v1/quality/rbqm/ract/seed-default",
    response_model=list[RACTRiskFactorResponse],
    status_code=201,
)
@transactional
async def seed_default_transcelerate_ract(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, _ = get_user_context(principal)
    factors = await service.seed_default_transcelerate_ract(
        study_id, user_id, user_role
    )
    return [map_ract_factor_to_response(f) for f in factors]


@router.get(
    "/api/v1/quality/rbqm/ract/summary", response_model=RACTMatrixSummaryResponse
)
@transactional
async def get_ract_matrix_summary(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    summary = await service.get_ract_matrix_summary(study_id)
    return RACTMatrixSummaryResponse(
        study_id=summary["study_id"],
        total_risk_factors=summary["total_risk_factors"],
        high_risk_count=summary["high_risk_count"],
        medium_risk_count=summary["medium_risk_count"],
        low_risk_count=summary["low_risk_count"],
        overall_average_score=summary["overall_average_score"],
        linked_kri_coverage_percentage=summary["linked_kri_coverage_percentage"],
        category_summaries=[
            RACTCategorySummary(**c) for c in summary["category_summaries"]
        ],
        risk_factors=[map_ract_factor_to_response(f) for f in summary["risk_factors"]],
    )


# --- CtQ Endpoints ---


@router.post(
    "/api/v1/quality/rbqm/ctq", response_model=CtQFactorResponse, status_code=201
)
@transactional
async def create_ctq_factor(
    request: Request,
    payload: CtQFactorCreate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    ctq = await service.create_ctq_factor(payload, user_id, user_role, change_reason)
    return map_ctq_to_response(ctq)


@router.get("/api/v1/quality/rbqm/ctq", response_model=list[CtQFactorResponse])
@transactional
async def list_ctq_factors(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    factors = await service.list_ctq_factors(study_id)
    return [map_ctq_to_response(f) for f in factors]


# --- KRI Endpoints ---


@router.post(
    "/api/v1/quality/rbqm/kris", response_model=KRIDefinitionResponse, status_code=201
)
@transactional
async def create_kri_definition(
    request: Request,
    payload: KRIDefinitionCreate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    kri = await service.create_kri_definition(
        payload, user_id, user_role, change_reason
    )
    return map_kri_definition_to_response(kri)


@router.get("/api/v1/quality/rbqm/kris", response_model=list[KRIDefinitionResponse])
@transactional
async def list_kri_definitions(
    request: Request,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    kris = await service.list_kri_definitions()
    return [map_kri_definition_to_response(k) for k in kris]


@router.post(
    "/api/v1/quality/rbqm/kris/evaluate-batch",
    response_model=list[KRIMetricEvaluationResponse],
)
@transactional
async def evaluate_kri_batch(
    request: Request,
    payload: KRIBatchEvaluationRequest,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    evals = await service.evaluate_site_kri_batch(
        study_id=payload.study_id,
        kri_code=payload.kri_code,
        site_raw_values=payload.site_raw_values,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason,
    )
    return [map_kri_eval_to_response(e) for e in evals]


@router.get(
    "/api/v1/quality/rbqm/evaluations", response_model=list[KRIMetricEvaluationResponse]
)
@transactional
async def list_kri_evaluations(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    site_id: str | None = Query(None, description="Optional Site ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    evals = await service.repo.get_kri_evaluations(study_id, site_id)
    return [map_kri_eval_to_response(e) for e in evals]


# --- Site Risk Profile Endpoints ---


@router.post(
    "/api/v1/quality/rbqm/site-risk-profiles/compute",
    response_model=list[SiteRiskProfileResponse],
)
@transactional
async def compute_site_risk_profiles(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    profiles = await service.compute_study_site_risk_profiles(
        study_id, user_id, user_role, change_reason
    )
    return [map_profile_to_response(p) for p in profiles]


@router.get(
    "/api/v1/quality/rbqm/site-risk-profiles",
    response_model=list[SiteRiskProfileResponse],
)
@transactional
async def get_site_risk_profiles(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    profiles = await service.repo.get_site_risk_profiles(study_id)
    return [map_profile_to_response(p) for p in profiles]


# --- Quality Tolerance Limits (QTL) Endpoints ---


@router.post("/api/v1/quality/rbqm/qtls", response_model=QTLResponse, status_code=201)
@transactional
async def create_qtl(
    request: Request,
    payload: QTLCreate,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    qtl = await service.create_qtl(payload, user_id, user_role, change_reason)
    return map_qtl_to_response(qtl)


@router.get("/api/v1/quality/rbqm/qtls", response_model=list[QTLResponse])
@transactional
async def list_qtls(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    qtls = await service.list_qtls(study_id)
    return [map_qtl_to_response(q) for q in qtls]


@router.post("/api/v1/quality/rbqm/qtls/{id}/evaluate-breach")
@transactional
async def evaluate_qtl_breach(
    request: Request,
    id: str,
    payload: QTLEvaluateBreachRequest,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    result = await service.evaluate_qtl_breach(
        qtl_id=id,
        observed_value=payload.observed_value,
        root_cause=payload.root_cause,
        corrective_action_summary=payload.corrective_action_summary,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason,
    )
    if isinstance(result, QTLBreachEvent):
        return map_qtl_breach_to_response(result)
    return result


@router.get(
    "/api/v1/quality/rbqm/qtls/breaches", response_model=list[QTLBreachEventResponse]
)
@transactional
async def list_qtl_breaches(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    breaches = await service.repo.get_qtl_breaches(study_id)
    return [map_qtl_breach_to_response(b) for b in breaches]


# --- Centralized Monitoring Inspection Dossiers (PRD-CSM-006) ---


@router.post(
    "/api/v1/quality/rbqm/csm/dossiers/compile",
    response_model=CSMDossierResponse,
    status_code=201,
)
@transactional
async def compile_csm_dossier(
    request: Request,
    payload: CompileCSMDossierRequest,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    dossier = await service.compile_centralized_monitoring_dossier(
        study_id=payload.study_id,
        signer_name=payload.signer_name,
        signer_role=payload.signer_role,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason,
        dossier_name=payload.dossier_name,
        signature_meaning=payload.signature_meaning,
    )
    return map_dossier_to_response(dossier)


@router.get(
    "/api/v1/quality/rbqm/csm/dossiers", response_model=list[CSMDossierResponse]
)
@transactional
async def list_csm_dossiers(
    request: Request,
    study_id: str = Query(..., description="Study ID"),
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    dossiers = await service.list_csm_dossiers(study_id)
    return [map_dossier_to_response(d) for d in dossiers]


@router.get(
    "/api/v1/quality/rbqm/csm/dossiers/{dossier_id}",
    response_model=CSMDossierResponse,
)
@transactional
async def get_csm_dossier(
    request: Request,
    dossier_id: str,
    service: RBQMService = Depends(get_rbqm_service),
    principal: Principal = Depends(get_principal),
):
    dossier = await service.get_csm_dossier_by_id(dossier_id)
    if not dossier:
        raise HTTPException(
            status_code=404,
            detail=f"Centralized Monitoring Dossier '{dossier_id}' not found.",
        )
    return map_dossier_to_response(dossier)
