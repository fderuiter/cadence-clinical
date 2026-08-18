import math
from collections.abc import Sequence
from datetime import datetime
from typing import Any

from apps.quality.domain.models import RiskCategory, RiskTier
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class RBQMServiceError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


DEFAULT_KRIS = [
    {
        "code": "KRI_QUERY_AGE",
        "name": "Query Resolution Aging",
        "category": RiskCategory.DATA_INTEGRITY,
        "description": "Average duration in days that clinical queries remain unresolved.",
        "calculation_formula": "sum(query_open_days) / count(open_queries)",
        "green_threshold": 7.0,
        "amber_threshold": 14.0,
        "red_threshold": 21.0,
        "weight": 1.5,
    },
    {
        "code": "KRI_AE_RATE",
        "name": "Adverse Event Under-reporting Rate",
        "category": RiskCategory.PATIENT_SAFETY,
        "description": "Rate of reported AEs per subject relative to study cohort median.",
        "calculation_formula": "count(adverse_events) / count(enrolled_subjects)",
        "green_threshold": 1.0,
        "amber_threshold": 2.0,
        "red_threshold": 3.0,
        "weight": 2.0,
    },
    {
        "code": "KRI_MISSING_FORMS",
        "name": "Overdue eCRF Submission Rate",
        "category": RiskCategory.OPERATIONAL_EXECUTION,
        "description": "Percentage of clinical visit forms past submission window.",
        "calculation_formula": "(count(overdue_forms) / count(expected_forms)) * 100",
        "green_threshold": 3.0,
        "amber_threshold": 8.0,
        "red_threshold": 15.0,
        "weight": 1.2,
    },
    {
        "code": "KRI_DEV_DENSITY",
        "name": "Protocol Deviation Density",
        "category": RiskCategory.REGULATORY_COMPLIANCE,
        "description": "Protocol deviations logged per active participant.",
        "calculation_formula": "count(deviations) / count(active_subjects)",
        "green_threshold": 0.5,
        "amber_threshold": 1.5,
        "red_threshold": 2.5,
        "weight": 1.8,
    },
    {
        "code": "KRI_SDV_BACKLOG",
        "name": "Source Data Verification (SDV) Lag",
        "category": RiskCategory.DATA_INTEGRITY,
        "description": "Percentage of completed forms awaiting CRA verification.",
        "calculation_formula": "(count(unverified_forms) / count(completed_forms)) * 100",
        "green_threshold": 10.0,
        "amber_threshold": 25.0,
        "red_threshold": 40.0,
        "weight": 1.0,
    },
]


class RBQMService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    # --- CtQ Factors ---

    async def create_ctq_factor(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        ctq = self.repo.create_ctq_entity(
            study_id=payload.study_id,
            category=payload.category,
            critical_aspect=payload.critical_aspect,
            risk_description=payload.risk_description,
            impact_area=payload.impact_area,
            mitigation_strategy=payload.mitigation_strategy,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_ctq(ctq)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CTQ_CREATE",
            details=f"Created CtQ factor for study '{payload.study_id}' ({payload.critical_aspect}).",
            record_id=ctq.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return ctq

    async def list_ctq_factors(self, study_id: str) -> Sequence[Any]:
        return await self.repo.get_ctq_factors(study_id)

    # --- KRI Definitions & Population Seeding ---

    async def seed_default_kris(self, user_id: str, user_role: str) -> list[Any]:
        seeded = []
        for d in DEFAULT_KRIS:
            existing = await self.repo.get_kri_definition_by_code(d["code"])
            if not existing:
                kri = self.repo.create_kri_definition_entity(
                    code=d["code"],
                    name=d["name"],
                    category=d["category"],
                    description=d["description"],
                    calculation_formula=d["calculation_formula"],
                    green_threshold=d["green_threshold"],
                    amber_threshold=d["amber_threshold"],
                    red_threshold=d["red_threshold"],
                    weight=d["weight"],
                    is_active=True,
                    created_by=user_id,
                    version_index=1,
                    reason_for_change="Seed TransCelerate default KRI",
                )
                await self.repo.save_kri_definition(kri)
                seeded.append(kri)
        return seeded

    async def create_kri_definition(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        existing = await self.repo.get_kri_definition_by_code(payload.code)
        if existing:
            raise RBQMServiceError(
                f"KRI code '{payload.code}' already exists.", status_code=409
            )

        kri = self.repo.create_kri_definition_entity(
            code=payload.code,
            name=payload.name,
            category=payload.category,
            description=payload.description,
            calculation_formula=payload.calculation_formula,
            green_threshold=payload.green_threshold,
            amber_threshold=payload.amber_threshold,
            red_threshold=payload.red_threshold,
            weight=getattr(payload, "weight", 1.0),
            is_active=True,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_kri_definition(kri)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="KRI_DEFINITION_CREATE",
            details=f"Defined Key Risk Indicator: {payload.code} ({payload.name}).",
            record_id=kri.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return kri

    async def list_kri_definitions(self) -> Sequence[Any]:
        definitions = await self.repo.get_kri_definitions()
        if not definitions:
            await self.seed_default_kris("system", "admin")
            definitions = await self.repo.get_kri_definitions()
        return definitions

    # --- Statistical Anomaly Detection & KRI Evaluations ---

    async def evaluate_site_kri_batch(
        self,
        study_id: str,
        kri_code: str,
        site_raw_values: dict[str, float],
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> list[Any]:
        """
        Takes raw metric values for multiple sites, computes sample mean & standard deviation,
        determines Z-scores, assigns risk tiers, and persists KRIMetricEvaluation entities.
        """
        if not site_raw_values:
            return []

        values = list(site_raw_values.values())
        n = len(values)
        mean_val = sum(values) / n if n > 0 else 0.0

        if n > 1:
            variance = sum((x - mean_val) ** 2 for x in values) / (n - 1)
            std_dev = math.sqrt(variance)
        else:
            std_dev = 0.0

        evaluations = []
        for site_id, raw_val in site_raw_values.items():
            z_score = round((raw_val - mean_val) / std_dev, 3) if std_dev > 0 else 0.0

            abs_z = abs(z_score)
            if abs_z >= 3.0:
                tier = RiskTier.CRITICAL
            elif abs_z >= 2.0:
                tier = RiskTier.HIGH
            elif abs_z >= 1.0:
                tier = RiskTier.MEDIUM
            else:
                tier = RiskTier.LOW

            evaluation = self.repo.create_kri_evaluation_entity(
                study_id=study_id,
                site_id=site_id,
                kri_code=kri_code,
                evaluation_date=datetime.now(),
                raw_value=raw_val,
                standardized_z_score=z_score,
                risk_tier=tier,
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason
                or "Automated batch KRI statistical scoring",
            )
            await self.repo.save_kri_evaluation(evaluation)
            evaluations.append(evaluation)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="KRI_EVALUATION_BATCH",
            details=f"Evaluated KRI '{kri_code}' for {n} sites in study '{study_id}'. Mean={mean_val:.2f}, StdDev={std_dev:.2f}.",
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return evaluations

    async def compute_study_site_risk_profiles(
        self, study_id: str, user_id: str, user_role: str, change_reason: str
    ) -> Sequence[Any]:
        """
        Aggregates all site KRI evaluations and active deviations to produce a composite Site Risk Index.
        """
        evaluations = await self.repo.get_kri_evaluations(study_id)
        deviations = await self.repo.get_deviations()

        # Group evaluations by site
        site_evals: dict[str, list[Any]] = {}
        for ev in evaluations:
            site_evals.setdefault(ev.site_id, []).append(ev)

        # Count active deviations per site
        site_devs: dict[str, int] = {}
        for d in deviations:
            if d.study_id == study_id and d.site_id:
                if d.status not in ("CLOSED", "RESOLVED"):
                    site_devs[d.site_id] = site_devs.get(d.site_id, 0) + 1

        all_site_ids = set(site_evals.keys()) | set(site_devs.keys())
        if not all_site_ids:
            return []

        tier_weights = {
            RiskTier.LOW: 1.0,
            RiskTier.MEDIUM: 2.5,
            RiskTier.HIGH: 5.0,
            RiskTier.CRITICAL: 10.0,
        }

        site_scores: list[dict[str, Any]] = []
        for site_id in all_site_ids:
            evals = site_evals.get(site_id, [])
            dev_count = site_devs.get(site_id, 0)
            high_kri_count = sum(
                1 for e in evals if e.risk_tier in (RiskTier.HIGH, RiskTier.CRITICAL)
            )

            raw_kri_score = sum(tier_weights.get(e.risk_tier, 1.0) for e in evals)
            composite_score = round(raw_kri_score + (dev_count * 1.5), 2)

            site_scores.append(
                {
                    "site_id": site_id,
                    "composite_score": composite_score,
                    "high_kri_count": high_kri_count,
                    "dev_count": dev_count,
                }
            )

        # Rank by composite score descending
        site_scores.sort(key=lambda s: s["composite_score"], reverse=True)

        saved_profiles = []
        for rank, item in enumerate(site_scores, start=1):
            profile = self.repo.create_site_risk_profile_entity(
                study_id=study_id,
                site_id=item["site_id"],
                evaluation_date=datetime.now(),
                composite_risk_score=item["composite_score"],
                risk_rank=rank,
                high_risk_kri_count=item["high_kri_count"],
                active_deviations_count=item["dev_count"],
                created_by=user_id,
                version_index=1,
                reason_for_change=change_reason
                or "Automated Site Risk Index calculation",
            )
            await self.repo.save_site_risk_profile(profile)
            saved_profiles.append(profile)

        return saved_profiles

    # --- Quality Tolerance Limits (QTLs) ---

    async def create_qtl(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        qtl = self.repo.create_qtl_entity(
            study_id=payload.study_id,
            parameter_name=payload.parameter_name,
            target_value=payload.target_value,
            tolerance_limit=payload.tolerance_limit,
            unit=getattr(payload, "unit", "%"),
            is_breached=False,
            breach_count=0,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_qtl(qtl)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="QTL_CREATE",
            details=f"Defined Quality Tolerance Limit for '{payload.study_id}': {payload.parameter_name} (Limit: {payload.tolerance_limit}{getattr(payload, 'unit', '%')}).",
            record_id=qtl.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return qtl

    async def list_qtls(self, study_id: str) -> Sequence[Any]:
        return await self.repo.get_qtls(study_id)

    async def evaluate_qtl_breach(
        self,
        qtl_id: str,
        observed_value: float,
        root_cause: str,
        corrective_action_summary: str,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        """
        Evaluates an observed parameter value against a defined QTL. If breached, records QTLBreachEvent
        and compiles the Clinical Study Report (CSR Section 9.6) impact narrative.
        """
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        # Find QTL across all studies
        target_qtl = None
        for q in await self.repo.get_qtls(""):
            if q.id == qtl_id:
                target_qtl = q
                break

        if not target_qtl:
            raise RBQMServiceError(
                f"Quality Tolerance Limit with ID '{qtl_id}' not found.",
                status_code=404,
            )

        is_breached = observed_value > target_qtl.tolerance_limit
        if not is_breached:
            return {
                "status": "NO_BREACH",
                "observed_value": observed_value,
                "tolerance_limit": target_qtl.tolerance_limit,
            }

        target_qtl.is_breached = True
        target_qtl.breach_count += 1
        target_qtl.version_index += 1
        target_qtl.reason_for_change = f"QTL Breach observed: {observed_value}{target_qtl.unit} exceeded limit {target_qtl.tolerance_limit}{target_qtl.unit}."
        await self.repo.save_qtl(target_qtl)

        # Auto-generate CSR Section 9.6 narrative
        csr_narrative = (
            f"CSR Section 9.6 QTL Summary: For study '{target_qtl.study_id}', the Quality Tolerance Limit for "
            f"'{target_qtl.parameter_name}' was breached with an observed rate of {observed_value:.2f}{target_qtl.unit} "
            f"(pre-defined protocol tolerance threshold: {target_qtl.tolerance_limit:.2f}{target_qtl.unit}). "
            f"Root Cause: {root_cause}. Corrective and Preventive Mitigation Implemented: {corrective_action_summary}."
        )

        breach_event = self.repo.create_qtl_breach_entity(
            qtl_id=qtl_id,
            study_id=target_qtl.study_id,
            breach_date=datetime.now(),
            observed_value=observed_value,
            threshold_value=target_qtl.tolerance_limit,
            root_cause=root_cause,
            corrective_action_summary=corrective_action_summary,
            csr_narrative=csr_narrative,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_qtl_breach(breach_event)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="QTL_BREACH_RECORDED",
            details=f"Recorded QTL breach on '{target_qtl.parameter_name}': {observed_value} > {target_qtl.tolerance_limit}.",
            record_id=breach_event.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return breach_event
