from datetime import UTC, datetime

from apps.ctms.domain.models import (
    CTMSAuditLogEntity,
    RBQMKRIMetricEntity,
    SiteRiskScoreEntity,
)
from apps.ctms.domain.ports import ICTMSDelegationRepository, IRBQMRepository


class RBQMService:
    """Application service for Risk-Based Quality Management (RBQM) & Centralized Monitoring."""

    def __init__(
        self,
        rbqm_repo: IRBQMRepository,
        doa_repo: ICTMSDelegationRepository | None = None,
    ):
        self.rbqm_repo = rbqm_repo
        self.doa_repo = doa_repo

    async def record_kri_metric(
        self,
        study_id: str,
        site_id: str,
        metric_type: str,  # QUERY_VELOCITY, SAE_REPORTING_LAG_DAYS, PROTOCOL_DEVIATION_RATE, FORM_ENTRY_LAG_DAYS, SDV_BACKLOG_RATE
        metric_value: float,
        threshold_low: float,
        threshold_high: float,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        notes: str | None = None,
    ) -> RBQMKRIMetricEntity:
        if metric_value > threshold_high:
            breach_status = "BREACHED"
        elif metric_value < threshold_low:
            breach_status = "WARNING"
        else:
            breach_status = "NORMAL"

        entity = RBQMKRIMetricEntity(
            study_id=study_id,
            site_id=site_id,
            metric_type=metric_type.upper(),
            metric_value=metric_value,
            threshold_low=threshold_low,
            threshold_high=threshold_high,
            breach_status=breach_status,
            calculation_date=datetime.now(UTC).isoformat(),
            notes=notes,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.rbqm_repo.save_kri_metric(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="RBQM_KRI_COMPUTED",
                details=f"Calculated KRI {metric_type} for site {site_id}: {metric_value} (Status: {breach_status}). Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def compute_site_risk_score(
        self,
        study_id: str,
        site_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
    ) -> SiteRiskScoreEntity:
        metrics = await self.rbqm_repo.list_kri_metrics(study_id, site_id)
        if not metrics:
            composite = 10.0
            risk_level = "LOW"
            recommended = "ROUTINE_ON_SITE"
            interval = 45
        else:
            breach_count = sum(1 for m in metrics if m.breach_status == "BREACHED")
            warning_count = sum(1 for m in metrics if m.breach_status == "WARNING")
            total = len(metrics)
            composite = min(
                100.0,
                (breach_count * 35.0 + warning_count * 15.0) / max(1, total) * 2.5,
            )

            if composite >= 60.0 or breach_count >= 2:
                risk_level = "HIGH"
                recommended = "TARGETED_FOR_CAUSE"
                interval = 14
            elif composite >= 30.0 or breach_count >= 1 or warning_count >= 2:
                risk_level = "MEDIUM"
                recommended = "ROUTINE_ON_SITE"
                interval = 30
            else:
                risk_level = "LOW"
                recommended = "REMOTE"
                interval = 60

        entity = SiteRiskScoreEntity(
            study_id=study_id,
            site_id=site_id,
            composite_score=round(composite, 2),
            risk_level=risk_level,
            assessment_date=datetime.now(UTC).isoformat(),
            recommended_monitoring_type=recommended,
            monitoring_interval_days=interval,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        saved = await self.rbqm_repo.save_site_risk_score(entity)

        if self.doa_repo:
            audit = CTMSAuditLogEntity(
                user_id=user_id,
                user_role=user_roles,
                action="SITE_RISK_SCORE_EVALUATED",
                details=f"Evaluated Risk Score for site {site_id}: {entity.composite_score} ({risk_level}). Recommended: {recommended} every {interval} days. Reason: {reason_for_change}",
                timestamp=datetime.now(UTC).isoformat(),
            )
            await self.doa_repo.save_audit_log(audit)

        return saved

    async def list_kri_metrics(
        self, study_id: str, site_id: str | None = None
    ) -> list[RBQMKRIMetricEntity]:
        return await self.rbqm_repo.list_kri_metrics(study_id, site_id)

    async def get_latest_risk_score(self, site_id: str) -> SiteRiskScoreEntity | None:
        return await self.rbqm_repo.get_latest_site_risk_score(site_id)
