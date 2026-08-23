"""Centralized Statistical Monitoring (CSM) & Statistical Forensics Router.

Endpoints for Benford's Law analysis, Terminal Digit Rounding Bias, Mahalanobis Distance,
and Variance Shrinkage surveillance under ICH E6(R2)/(R3).
"""

from fastapi import APIRouter, Depends, HTTPException, Query, Request

from apps.quality.adapters.database import transactional
from apps.quality.adapters.models import (
    SiteForensicsRecord,
    StatisticalForensicsRun,
)
from apps.quality.application.services.statistical_forensics_service import (
    StatisticalForensicsService,
)
from apps.quality.presentation.dtos import (
    BenfordDigitDistributionResponse,
    ForensicsSimulationRequest,
    ForensicsSimulationResponse,
    KRIMetricEvaluationResponse,
    MahalanobisMetricsResponse,
    SiteForensicsResponse,
    StatisticalForensicsAnalyzeRequest,
    StatisticalForensicsRunResponse,
    TerminalDigitDistributionResponse,
    VarianceShrinkageMetricsResponse,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_forensics_service() -> StatisticalForensicsService:
    import apps.quality.main as main_module

    return main_module.get_forensics_service()


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


def map_site_forensics_to_response(rec: SiteForensicsRecord) -> SiteForensicsResponse:
    # Theoretical Benford expected frequencies
    n = rec.sample_size * len(rec.site_mean_vector or [1])
    expected_benford_map = {
        "1": round(n * 0.30103, 2),
        "2": round(n * 0.17609, 2),
        "3": round(n * 0.12494, 2),
        "4": round(n * 0.09691, 2),
        "5": round(n * 0.07918, 2),
        "6": round(n * 0.06695, 2),
        "7": round(n * 0.05799, 2),
        "8": round(n * 0.05115, 2),
        "9": round(n * 0.04576, 2),
    }

    exp_term_map = {str(d): round(n * 0.10, 2) for d in range(10)}

    return SiteForensicsResponse(
        id=rec.id,
        run_id=rec.run_id,
        study_id=rec.study_id,
        site_id=rec.site_id,
        sample_size=rec.sample_size,
        benford=BenfordDigitDistributionResponse(
            sample_size=rec.sample_size,
            chi_square=rec.benford_chi_square,
            p_value=rec.benford_p_value,
            mad=rec.benford_mad,
            conformity=rec.benford_conformity,
            observed_frequencies={
                str(k): int(v)
                for k, v in (
                    rec.benford_leading_digits
                    if isinstance(rec.benford_leading_digits, dict)
                    else {}
                ).items()
            },
            expected_frequencies=expected_benford_map,
        ),
        terminal_digit=TerminalDigitDistributionResponse(
            sample_size=rec.sample_size,
            chi_square=rec.terminal_digit_chi_square,
            p_value=rec.terminal_digit_p_value,
            zero_five_index=rec.terminal_digit_zero_five_index,
            rounding_bias_tier=rec.rounding_bias_tier,
            observed_frequencies={
                str(k): int(v)
                for k, v in (
                    rec.terminal_digit_distribution
                    if isinstance(rec.terminal_digit_distribution, dict)
                    else {}
                ).items()
            },
            expected_frequencies=exp_term_map,
        ),
        multivariate=MahalanobisMetricsResponse(
            mahalanobis_distance=rec.mahalanobis_distance,
            mahalanobis_p_value=rec.mahalanobis_p_value,
            correlation_matrix_distance=rec.correlation_matrix_distance,
            site_mean_vector=rec.site_mean_vector
            if isinstance(rec.site_mean_vector, dict)
            else {},
            site_covariance_matrix=rec.site_covariance_matrix
            if isinstance(rec.site_covariance_matrix, dict)
            else {},
        ),
        variance_shrinkage=VarianceShrinkageMetricsResponse(
            variance_ratio=rec.variance_ratio,
            variance_shrinkage_p_value=rec.variance_shrinkage_p_value,
            kl_divergence=rec.kl_divergence,
            is_variance_shrunk=rec.is_variance_shrunk,
        ),
        composite_risk_score=rec.composite_risk_score,
        risk_tier=rec.risk_tier,
        is_anomalous_outlier=rec.is_anomalous_outlier,
        flagged_anomaly_reasons=rec.flagged_anomaly_reasons
        if isinstance(rec.flagged_anomaly_reasons, list)
        else [],
        kri_evaluation_id=rec.kri_evaluation_id,
        created_at=rec.created_at.isoformat(),
        created_by=rec.created_by,
        version_index=rec.version_index,
        reason_for_change=rec.reason_for_change,
    )


def map_run_to_response(
    run: StatisticalForensicsRun,
) -> StatisticalForensicsRunResponse:
    results = [map_site_forensics_to_response(r) for r in (run.site_results or [])]
    return StatisticalForensicsRunResponse(
        id=run.id,
        study_id=run.study_id,
        run_name=run.run_name,
        analysis_timestamp=run.analysis_timestamp.isoformat(),
        target_variables=run.target_variables
        if isinstance(run.target_variables, list)
        else [],
        total_sites=run.total_sites,
        total_observations=run.total_observations,
        anomalous_sites_count=run.anomalous_sites_count,
        status=run.status,
        site_results=results,
        created_at=run.created_at.isoformat(),
        created_by=run.created_by,
        version_index=run.version_index,
        reason_for_change=run.reason_for_change,
    )


@router.post(
    "/api/v1/quality/forensics/analyze",
    response_model=StatisticalForensicsRunResponse,
    status_code=201,
)
@transactional
async def execute_forensics_analysis(
    request: Request,
    payload: StatisticalForensicsAnalyzeRequest,
    service: StatisticalForensicsService = Depends(get_forensics_service),
    principal: Principal = Depends(get_principal),
):
    """Executes automated multi-site statistical forensics surveillance."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    site_data = payload.site_observations or {}
    if not site_data and payload.site_data:
        site_data = {item.site_id: item.observations for item in payload.site_data}

    if not site_data:
        # Fall back to high-fidelity simulated dataset if none provided
        site_data = StatisticalForensicsService.generate_simulated_clinical_dataset(
            study_id=payload.study_id
        )

    run = await service.execute_forensics_surveillance(
        study_id=payload.study_id,
        target_variables=payload.target_variables,
        site_observation_data=site_data,
        run_name=payload.run_name,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason or "Automated CSM Forensics Run execution",
        auto_trigger_kri=payload.auto_trigger_kri,
    )
    return map_run_to_response(run)


@router.get(
    "/api/v1/quality/forensics/runs",
    response_model=list[StatisticalForensicsRunResponse],
)
@transactional
async def list_forensics_runs(
    request: Request,
    study_id: str | None = Query(None, description="Optional study ID filter"),
    service: StatisticalForensicsService = Depends(get_forensics_service),
    principal: Principal = Depends(get_principal),
):
    """Lists historical statistical forensics surveillance runs."""
    runs = await service.list_runs(study_id)
    return [map_run_to_response(r) for r in runs]


@router.get(
    "/api/v1/quality/forensics/runs/{run_id}",
    response_model=StatisticalForensicsRunResponse,
)
@transactional
async def get_forensics_run(
    request: Request,
    run_id: str,
    service: StatisticalForensicsService = Depends(get_forensics_service),
    principal: Principal = Depends(get_principal),
):
    """Retrieves detailed results for a specific forensics surveillance run."""
    run = await service.get_run_details(run_id)
    return map_run_to_response(run)


@router.get(
    "/api/v1/quality/forensics/sites/{site_id}",
    response_model=list[SiteForensicsResponse],
)
@transactional
async def get_site_forensics_history(
    request: Request,
    site_id: str,
    service: StatisticalForensicsService = Depends(get_forensics_service),
    principal: Principal = Depends(get_principal),
):
    """Retrieves chronological forensics surveillance results for an individual site."""
    records = await service.get_site_history(site_id)
    return [map_site_forensics_to_response(r) for r in records]


@router.post(
    "/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts",
    response_model=list[KRIMetricEvaluationResponse],
)
@transactional
async def trigger_run_kri_alerts(
    request: Request,
    run_id: str,
    service: StatisticalForensicsService = Depends(get_forensics_service),
    principal: Principal = Depends(get_principal),
):
    """Dispatches KRI breach events for all flagged anomalous sites in a forensics run."""
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)
    kris = await service.trigger_kri_alerts_for_run(
        run_id=run_id,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason
        or "Manual KRI alert dispatch from forensics console",
    )
    from apps.quality.presentation.routers.rbqm import map_kri_eval_to_response

    return [map_kri_eval_to_response(k) for k in kris]


@router.post(
    "/api/v1/quality/forensics/simulate",
    response_model=ForensicsSimulationResponse,
)
async def simulate_forensics_dataset(
    request: Request,
    payload: ForensicsSimulationRequest,
    principal: Principal = Depends(get_principal),
):
    """Generates synthetic multi-site clinical datasets for testing and calibration."""
    data = StatisticalForensicsService.generate_simulated_clinical_dataset(
        study_id=payload.study_id,
        num_normal_sites=payload.num_normal_sites,
        include_benford_anomaly=payload.include_benford_anomaly,
        include_rounding_bias=payload.include_rounding_bias,
        include_variance_shrinkage=payload.include_variance_shrinkage,
        include_multivariate_outlier=payload.include_multivariate_outlier,
        samples_per_site=payload.samples_per_site,
    )
    total_obs = sum(len(obs) for obs in data.values())
    anomalies = []
    if payload.include_benford_anomaly:
        anomalies.append("SITE-BENFORD-ANOMALY: Uniform leading digit fabrication")
    if payload.include_rounding_bias:
        anomalies.append("SITE-ROUNDING-BIAS: Terminal digit 0 and 5 rounding excess")
    if payload.include_variance_shrinkage:
        anomalies.append("SITE-VARIANCE-SHRINKAGE: Artificial low standard deviation")
    if payload.include_multivariate_outlier:
        anomalies.append(
            "SITE-MULTIVARIATE-OUTLIER: Inverted correlation and centroid shift"
        )

    return ForensicsSimulationResponse(
        study_id=payload.study_id,
        target_variables=[
            "systolic_bp",
            "diastolic_bp",
            "heart_rate",
            "lab_creatinine",
        ],
        site_data=data,
        total_sites=len(data),
        total_observations=total_obs,
        simulated_anomalies=anomalies,
    )
