"""Autonomous Quality Surveillance & Sentinel Orchestration Router.

Exposes REST APIs for autonomous multi-domain quality surveillance runs,
sample-gated Centralized Statistical Monitoring (CSM), perceptual image hashing,
duplicate record detection, and automated 21 CFR Part 11 Deviation dispatch.

Requirements:
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture
"""

from fastapi import APIRouter, Depends, HTTPException, Request

from apps.quality.adapters.database import transactional
from apps.quality.application.services.quality_surveillance_runner import (
    QualitySurveillanceRunner,
)
from apps.quality.domain.surveillance_models import QualitySurveillanceSummary
from apps.quality.presentation.dtos import (
    QualitySurveillanceRunRequest,
    QualitySurveillanceRunResponse,
    SiteSurveillanceSummaryResponse,
)
from packages.security.rbac import Principal, get_principal, has_permission

router = APIRouter()


def get_surveillance_runner() -> QualitySurveillanceRunner:
    import apps.quality.main as main_module

    return main_module.get_quality_surveillance_runner()


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


def map_summary_to_response(
    summary: QualitySurveillanceSummary,
) -> QualitySurveillanceRunResponse:
    site_responses = [
        SiteSurveillanceSummaryResponse(
            site_id=ss.site_id,
            sample_size=ss.sample_size,
            observation_count=ss.observation_count,
            power_status=ss.power_status.value
            if hasattr(ss.power_status, "value")
            else str(ss.power_status),
            is_sample_gated=ss.is_sample_gated,
            composite_risk_score=ss.composite_risk_score,
            risk_tier=ss.risk_tier.value
            if hasattr(ss.risk_tier, "value")
            else str(ss.risk_tier),
            anomalies_detected=ss.anomalies_detected,
            deviations_created=ss.deviations_created,
            kri_alerts_triggered=ss.kri_alerts_triggered,
            details=ss.details,
        )
        for ss in summary.site_summaries
    ]

    return QualitySurveillanceRunResponse(
        run_id=summary.run_id,
        study_id=summary.study_id,
        run_name=summary.run_name,
        timestamp=summary.timestamp.isoformat()
        if hasattr(summary.timestamp, "isoformat")
        else str(summary.timestamp),
        total_sites=summary.total_sites,
        gated_sites_count=summary.gated_sites_count,
        analyzed_sites_count=summary.analyzed_sites_count,
        total_subjects=summary.total_subjects,
        total_observations=summary.total_observations,
        total_images_scanned=summary.total_images_scanned,
        total_events_scanned=summary.total_events_scanned,
        anomalies_count=summary.anomalies_count,
        deviations_dispatched_count=summary.deviations_dispatched_count,
        status=summary.status,
        site_summaries=site_responses,
        findings=summary.findings,
        deviations=summary.deviations,
        forensics_run_id=summary.forensics_run_id,
        sentinel_run_id=summary.sentinel_run_id,
    )


@router.post(
    "/api/v1/quality/surveillance/run",
    response_model=QualitySurveillanceRunResponse,
    status_code=201,
)
@transactional
async def execute_quality_surveillance_run(
    request: Request,
    payload: QualitySurveillanceRunRequest,
    runner: QualitySurveillanceRunner = Depends(get_surveillance_runner),
    principal: Principal = Depends(get_principal),
):
    """Executes a complete, autonomous cross-domain quality surveillance run.

    Harvests clinical data across domains, enforces sample power gating,
    evaluates statistical forensics & computer vision sentinels,
    and idempotently dispatches 21 CFR Part 11 Deviations.
    """
    authorize_quality_write(principal)
    user_id, user_role, change_reason = get_user_context(principal)

    summary = await runner.execute_surveillance(
        study_id=payload.study_id,
        run_name=payload.run_name,
        auto_dispatch_deviations=payload.auto_dispatch_deviations,
        target_variables=payload.target_variables,
        min_subjects_power=payload.min_subjects_power,
        min_observations_power=payload.min_observations_power,
        similarity_threshold=payload.similarity_threshold,
        hamming_threshold=payload.hamming_threshold,
        user_id=user_id,
        user_role=user_role,
        change_reason=change_reason or "Autonomous Quality Surveillance Runner trigger",
    )
    return map_summary_to_response(summary)
