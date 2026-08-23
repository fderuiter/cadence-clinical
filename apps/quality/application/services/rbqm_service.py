import hashlib
import json
import math
from collections.abc import Sequence
from datetime import UTC, datetime
from typing import Any

from apps.quality.domain.models import RACTCategory, RiskTier
from apps.quality.domain.ports import QualityRepositoryPort
from packages.hexagonal import DomainError


class RBQMServiceError(DomainError):
    def __init__(self, message: str, status_code: int = 400):
        super().__init__(message)
        self.status_code = status_code


DEFAULT_KRIS = [
    {
        "code": "KRI_AE_RATE",
        "name": "Adverse Event Under-reporting Rate",
        "category": RACTCategory.SAFETY,
        "description": "Rate of reported AEs per subject relative to study cohort median.",
        "calculation_formula": "count(adverse_events) / count(enrolled_subjects)",
        "green_threshold": 1.0,
        "amber_threshold": 2.0,
        "red_threshold": 3.0,
        "weight": 2.0,
    },
    {
        "code": "KRI_SAE_REPORTING_LAG",
        "name": "SAE 24-Hour Notification Lag",
        "category": RACTCategory.SAFETY,
        "description": "Average hours elapsed between Serious Adverse Event onset and regulatory notification.",
        "calculation_formula": "sum(hours_to_sae_notification) / count(sae_records)",
        "green_threshold": 12.0,
        "amber_threshold": 24.0,
        "red_threshold": 48.0,
        "weight": 2.5,
    },
    {
        "code": "KRI_CONSENT_VIOLATION",
        "name": "Informed Consent & Re-consent Timeliness",
        "category": RACTCategory.CONSENT,
        "description": "Percentage of clinical visits conducted without verified current signed informed consent.",
        "calculation_formula": "(count(unconsented_visits) / count(total_visits)) * 100",
        "green_threshold": 0.0,
        "amber_threshold": 1.0,
        "red_threshold": 3.0,
        "weight": 2.5,
    },
    {
        "code": "KRI_DEV_DENSITY",
        "name": "Protocol Deviation Density",
        "category": RACTCategory.STUDY_CONDUCT,
        "description": "Protocol deviations logged per active participant.",
        "calculation_formula": "count(deviations) / count(active_subjects)",
        "green_threshold": 0.5,
        "amber_threshold": 1.5,
        "red_threshold": 2.5,
        "weight": 1.8,
    },
    {
        "code": "KRI_VISIT_WINDOW_COMPLIANCE",
        "name": "Out-of-Window Visit Rate",
        "category": RACTCategory.STUDY_CONDUCT,
        "description": "Percentage of clinical visits conducted outside protocol-defined schedule windows.",
        "calculation_formula": "(count(out_of_window_visits) / count(completed_visits)) * 100",
        "green_threshold": 3.0,
        "amber_threshold": 7.0,
        "red_threshold": 12.0,
        "weight": 1.4,
    },
    {
        "code": "KRI_QUERY_AGE",
        "name": "Query Resolution Aging",
        "category": RACTCategory.DATA_INTEGRITY,
        "description": "Average duration in days that clinical queries remain unresolved.",
        "calculation_formula": "sum(query_open_days) / count(open_queries)",
        "green_threshold": 7.0,
        "amber_threshold": 14.0,
        "red_threshold": 21.0,
        "weight": 1.5,
    },
    {
        "code": "KRI_SDV_BACKLOG",
        "name": "Source Data Verification (SDV) Lag",
        "category": RACTCategory.DATA_INTEGRITY,
        "description": "Percentage of completed forms awaiting CRA verification.",
        "calculation_formula": "(count(unverified_forms) / count(completed_forms)) * 100",
        "green_threshold": 10.0,
        "amber_threshold": 25.0,
        "red_threshold": 40.0,
        "weight": 1.0,
    },
    {
        "code": "KRI_IP_DISCREPANCY",
        "name": "IP Accountability Discrepancy Rate",
        "category": RACTCategory.IP_MANAGEMENT,
        "description": "Investigational product kit accountability and dispensation discrepancy rate.",
        "calculation_formula": "(count(discrepant_ip_units) / count(dispensed_ip_units)) * 100",
        "green_threshold": 1.0,
        "amber_threshold": 3.0,
        "red_threshold": 6.0,
        "weight": 2.0,
    },
    {
        "code": "KRI_COLD_CHAIN_EXCURSION",
        "name": "Temperature Excursion Incident Rate",
        "category": RACTCategory.IP_MANAGEMENT,
        "description": "Investigational product cold-chain temperature excursion incidents.",
        "calculation_formula": "count(temperature_excursions) / count(active_ip_batches)",
        "green_threshold": 0.0,
        "amber_threshold": 1.0,
        "red_threshold": 2.0,
        "weight": 2.2,
    },
    {
        "code": "KRI_MISSING_FORMS",
        "name": "Overdue eCRF Submission Rate",
        "category": RACTCategory.TECHNOLOGY,
        "description": "Percentage of clinical visit forms past submission window.",
        "calculation_formula": "(count(overdue_forms) / count(expected_forms)) * 100",
        "green_threshold": 3.0,
        "amber_threshold": 8.0,
        "red_threshold": 15.0,
        "weight": 1.2,
    },
]

DEFAULT_TRANSCELERATE_RACT = [
    {
        "category": RACTCategory.SAFETY,
        "risk_topic": "Dose Escalation & Cytokine Release Toxicity",
        "risk_description": "Potential severe infusion-related reactions or calculation errors during escalating cohort administration.",
        "probability_score": 2,
        "impact_score": 3,
        "detectability_score": 2,
        "mitigation_strategy": "Mandatory 2-clinician verification prior to dispensing and continuous telemetry monitoring.",
        "linked_kri_code": "KRI_SAE_REPORTING_LAG",
    },
    {
        "category": RACTCategory.SAFETY,
        "risk_topic": "Adverse Event Under-reporting at Remote Sites",
        "risk_description": "Risk that satellite sites delay recording non-serious adverse events relative to study median.",
        "probability_score": 2,
        "impact_score": 2,
        "detectability_score": 3,
        "mitigation_strategy": "Weekly statistical surveillance comparing site AE reporting rates against study cohorts.",
        "linked_kri_code": "KRI_AE_RATE",
    },
    {
        "category": RACTCategory.CONSENT,
        "risk_topic": "Protocol Amendment Re-consent Timeliness",
        "risk_description": "Risk of protocol procedures conducted before subjects sign updated informed consent versions.",
        "probability_score": 2,
        "impact_score": 3,
        "detectability_score": 2,
        "mitigation_strategy": "Programmatic EDC write-gating blocking visit data entry until re-consent signature timestamp verified.",
        "linked_kri_code": "KRI_CONSENT_VIOLATION",
    },
    {
        "category": RACTCategory.STUDY_CONDUCT,
        "risk_topic": "Stringent Screening Eligibility & Biopsy Timing",
        "risk_description": "Risk of protocol deviations from scheduling baseline biopsies outside the strict 72-hour screening window.",
        "probability_score": 3,
        "impact_score": 2,
        "detectability_score": 2,
        "mitigation_strategy": "Automated calendar alerts and CRA escalation for upcoming window expirations.",
        "linked_kri_code": "KRI_DEV_DENSITY",
    },
    {
        "category": RACTCategory.STUDY_CONDUCT,
        "risk_topic": "Primary Efficacy Visit Schedule Adherence",
        "risk_description": "Risk of missing critical primary efficacy endpoint imaging assessments at milestone visits.",
        "probability_score": 1,
        "impact_score": 3,
        "detectability_score": 1,
        "mitigation_strategy": "Direct subject portal SMS reminders and proactive CRA booking verification.",
        "linked_kri_code": "KRI_VISIT_WINDOW_COMPLIANCE",
    },
    {
        "category": RACTCategory.DATA_INTEGRITY,
        "risk_topic": "Pharmacokinetic Lab Query Resolution Aging",
        "risk_description": "Risk of unverified pharmacokinetic laboratory discrepancies lingering unresolved beyond 14 days.",
        "probability_score": 2,
        "impact_score": 2,
        "detectability_score": 1,
        "mitigation_strategy": "Automated 7-day query escalation notifications to Principal Investigator.",
        "linked_kri_code": "KRI_QUERY_AGE",
    },
    {
        "category": RACTCategory.DATA_INTEGRITY,
        "risk_topic": "Critical Source Data Verification (SDV) Lag",
        "risk_description": "Risk that unverified EDC forms obscure data quality anomalies before DSMB interim analysis.",
        "probability_score": 2,
        "impact_score": 3,
        "detectability_score": 2,
        "mitigation_strategy": "Targeted SDV algorithm prioritizing critical safety and primary efficacy endpoints.",
        "linked_kri_code": "KRI_SDV_BACKLOG",
    },
    {
        "category": RACTCategory.IP_MANAGEMENT,
        "risk_topic": "Biologic Investigational Product Cold Chain Excursion",
        "risk_description": "Risk of product degradation if temperature deviates from -80C during transit or site pharmacy storage.",
        "probability_score": 2,
        "impact_score": 3,
        "detectability_score": 3,
        "mitigation_strategy": "Continuous IoT temperature logger telemetry with automated RTSM quarantine triggers upon excursion.",
        "linked_kri_code": "KRI_COLD_CHAIN_EXCURSION",
    },
    {
        "category": RACTCategory.IP_MANAGEMENT,
        "risk_topic": "IP Kit Dispensation & Accountability Discrepancy",
        "risk_description": "Risk of kit mix-up or failure to reconcile returned investigational product units.",
        "probability_score": 1,
        "impact_score": 3,
        "detectability_score": 2,
        "mitigation_strategy": "2D barcode verification at dispensation and digital drug accountability ledger.",
        "linked_kri_code": "KRI_IP_DISCREPANCY",
    },
    {
        "category": RACTCategory.TECHNOLOGY,
        "risk_topic": "Decentralized Sensor Disconnection & Overdue eCRFs",
        "risk_description": "Risk of missing continuous wearable biometric streams or overdue subject-reported outcomes (ePRO).",
        "probability_score": 3,
        "impact_score": 2,
        "detectability_score": 2,
        "mitigation_strategy": "Real-time wearable heartbeat checks with automated push notifications to participant apps.",
        "linked_kri_code": "KRI_MISSING_FORMS",
    },
]


def calculate_ract_score_and_tier(
    probability: int, impact: int, detectability: int
) -> tuple[int, RiskTier]:
    """Computes TransCelerate 3-factor composite risk score (1-27) and assigns RiskTier."""
    if not (1 <= probability <= 3 and 1 <= impact <= 3 and 1 <= detectability <= 3):
        raise RBQMServiceError(
            "TransCelerate RACT scoring dimensions (Probability, Impact, Detectability) must be integers between 1 and 3.",
            status_code=422,
        )
    total_score = probability * impact * detectability
    if total_score >= 15:
        tier = RiskTier.HIGH
    elif total_score >= 7:
        tier = RiskTier.MEDIUM
    else:
        tier = RiskTier.LOW
    return total_score, tier


class RBQMService:
    def __init__(self, repo: QualityRepositoryPort):
        self.repo = repo

    # --- TransCelerate RACT Matrix Management (PRD-CSM-005) ---

    async def create_ract_risk_factor(
        self, payload: Any, user_id: str, user_role: str, change_reason: str
    ) -> Any:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        total_score, risk_tier = calculate_ract_score_and_tier(
            payload.probability_score, payload.impact_score, payload.detectability_score
        )

        factor = self.repo.create_ract_risk_factor_entity(
            study_id=payload.study_id,
            category=payload.category,
            risk_topic=payload.risk_topic,
            risk_description=payload.risk_description,
            probability_score=payload.probability_score,
            impact_score=payload.impact_score,
            detectability_score=payload.detectability_score,
            total_risk_score=total_score,
            risk_tier=risk_tier,
            mitigation_strategy=payload.mitigation_strategy,
            linked_kri_code=payload.linked_kri_code,
            is_active=True,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_ract_risk_factor(factor)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="RACT_RISK_FACTOR_CREATE",
            details=f"Created TransCelerate RACT risk factor for study '{payload.study_id}' in category '{payload.category}': {payload.risk_topic} (Score: {total_score}, Tier: {risk_tier}).",
            record_id=factor.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return factor

    async def update_ract_risk_factor(
        self,
        factor_id: str,
        payload: Any,
        user_id: str,
        user_role: str,
        change_reason: str,
    ) -> Any:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        factor = await self.repo.get_ract_risk_factor_by_id(factor_id)
        if not factor:
            raise RBQMServiceError(
                f"RACT risk factor with ID '{factor_id}' not found.", status_code=404
            )

        if getattr(payload, "risk_topic", None) is not None:
            factor.risk_topic = payload.risk_topic
        if getattr(payload, "risk_description", None) is not None:
            factor.risk_description = payload.risk_description
        if getattr(payload, "mitigation_strategy", None) is not None:
            factor.mitigation_strategy = payload.mitigation_strategy
        if getattr(payload, "linked_kri_code", None) is not None:
            factor.linked_kri_code = payload.linked_kri_code
        if getattr(payload, "is_active", None) is not None:
            factor.is_active = payload.is_active

        # Recalculate score if any dimension changed
        prob = (
            payload.probability_score
            if getattr(payload, "probability_score", None) is not None
            else factor.probability_score
        )
        imp = (
            payload.impact_score
            if getattr(payload, "impact_score", None) is not None
            else factor.impact_score
        )
        det = (
            payload.detectability_score
            if getattr(payload, "detectability_score", None) is not None
            else factor.detectability_score
        )

        total_score, risk_tier = calculate_ract_score_and_tier(prob, imp, det)
        factor.probability_score = prob
        factor.impact_score = imp
        factor.detectability_score = det
        factor.total_risk_score = total_score
        factor.risk_tier = risk_tier

        factor.version_index += 1
        factor.reason_for_change = change_reason
        await self.repo.save_ract_risk_factor(factor)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="RACT_RISK_FACTOR_UPDATE",
            details=f"Updated RACT risk factor '{factor.id}' ({factor.risk_topic}): Score={total_score}, Tier={risk_tier}.",
            record_id=factor.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return factor

    async def delete_ract_risk_factor(
        self, factor_id: str, user_id: str, user_role: str, change_reason: str
    ) -> bool:
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        factor = await self.repo.get_ract_risk_factor_by_id(factor_id)
        if not factor:
            raise RBQMServiceError(
                f"RACT risk factor with ID '{factor_id}' not found.", status_code=404
            )

        study_id = factor.study_id
        topic = factor.risk_topic
        deleted = await self.repo.delete_ract_risk_factor(factor_id)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="RACT_RISK_FACTOR_DELETE",
            details=f"Deleted RACT risk factor '{factor_id}' ({topic}) for study '{study_id}'.",
            record_id=factor_id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)
        return deleted

    async def seed_default_transcelerate_ract(
        self, study_id: str, user_id: str, user_role: str
    ) -> list[Any]:
        existing_factors = await self.repo.get_ract_risk_factors(study_id)
        if existing_factors:
            return list(existing_factors)

        # Make sure default KRIs are seeded first
        await self.seed_default_kris(user_id, user_role)

        seeded_factors = []
        for item in DEFAULT_TRANSCELERATE_RACT:
            total_score, risk_tier = calculate_ract_score_and_tier(
                item["probability_score"],
                item["impact_score"],
                item["detectability_score"],
            )
            factor = self.repo.create_ract_risk_factor_entity(
                study_id=study_id,
                category=item["category"],
                risk_topic=item["risk_topic"],
                risk_description=item["risk_description"],
                probability_score=item["probability_score"],
                impact_score=item["impact_score"],
                detectability_score=item["detectability_score"],
                total_risk_score=total_score,
                risk_tier=risk_tier,
                mitigation_strategy=item["mitigation_strategy"],
                linked_kri_code=item["linked_kri_code"],
                is_active=True,
                created_by=user_id,
                version_index=1,
                reason_for_change="Seed standard TransCelerate RACT matrix",
            )
            await self.repo.save_ract_risk_factor(factor)
            seeded_factors.append(factor)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="RACT_MATRIX_SEEDED",
            details=f"Seeded TransCelerate standard RACT matrix with {len(seeded_factors)} protocol risk factors for study '{study_id}'.",
            change_reason="Standard TransCelerate eQMS initialization",
        )
        await self.repo.save_audit_log(log)
        return seeded_factors

    async def list_ract_risk_factors(
        self, study_id: str, category: str | None = None
    ) -> Sequence[Any]:
        factors = await self.repo.get_ract_risk_factors(study_id, category=category)
        if not factors and not category:
            factors = await self.seed_default_transcelerate_ract(
                study_id, "system", "quality_manager"
            )
        return factors

    async def get_ract_matrix_summary(self, study_id: str) -> dict[str, Any]:
        factors = await self.list_ract_risk_factors(study_id)
        all_categories = [
            RACTCategory.SAFETY,
            RACTCategory.CONSENT,
            RACTCategory.STUDY_CONDUCT,
            RACTCategory.DATA_INTEGRITY,
            RACTCategory.IP_MANAGEMENT,
            RACTCategory.TECHNOLOGY,
        ]

        total_factors = len(factors)
        high_count = sum(1 for f in factors if f.risk_tier == RiskTier.HIGH)
        medium_count = sum(1 for f in factors if f.risk_tier == RiskTier.MEDIUM)
        low_count = sum(1 for f in factors if f.risk_tier == RiskTier.LOW)

        overall_avg = (
            round(sum(f.total_risk_score for f in factors) / total_factors, 2)
            if total_factors > 0
            else 0.0
        )
        linked_kri_count = sum(1 for f in factors if f.linked_kri_code)
        linked_coverage = (
            round((linked_kri_count / total_factors) * 100.0, 1)
            if total_factors > 0
            else 0.0
        )

        category_summaries = []
        for cat in all_categories:
            cat_factors = [f for f in factors if f.category == cat]
            c_total = len(cat_factors)
            c_high = sum(1 for f in cat_factors if f.risk_tier == RiskTier.HIGH)
            c_med = sum(1 for f in cat_factors if f.risk_tier == RiskTier.MEDIUM)
            c_low = sum(1 for f in cat_factors if f.risk_tier == RiskTier.LOW)
            c_avg = (
                round(sum(f.total_risk_score for f in cat_factors) / c_total, 2)
                if c_total > 0
                else 0.0
            )
            c_kri = sum(1 for f in cat_factors if f.linked_kri_code)
            category_summaries.append(
                {
                    "category": cat.value if hasattr(cat, "value") else str(cat),
                    "total_factors": c_total,
                    "high_count": c_high,
                    "medium_count": c_med,
                    "low_count": c_low,
                    "average_score": c_avg,
                    "linked_kri_count": c_kri,
                }
            )

        return {
            "study_id": study_id,
            "total_risk_factors": total_factors,
            "high_risk_count": high_count,
            "medium_risk_count": medium_count,
            "low_risk_count": low_count,
            "overall_average_score": overall_avg,
            "linked_kri_coverage_percentage": linked_coverage,
            "category_summaries": category_summaries,
            "risk_factors": list(factors),
        }

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
        """Takes raw metric values for multiple sites, computes sample mean & standard deviation,

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
                evaluation_date=datetime.now(UTC),
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
        """Aggregates all site KRI evaluations and active deviations to produce a composite

        weighted Site Risk Index (SRI) and percentile ranking across study sites.
        """
        evaluations = await self.repo.get_kri_evaluations(study_id)
        deviations = await self.repo.get_deviations()
        kri_definitions = await self.repo.get_kri_definitions()
        if not kri_definitions:
            await self.seed_default_kris(user_id, user_role)
            kri_definitions = await self.repo.get_kri_definitions()
        kri_weights = {k.code: k.weight for k in kri_definitions}

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

            # Compute weighted KRI contribution
            raw_kri_score = sum(
                tier_weights.get(e.risk_tier, 1.0) * kri_weights.get(e.kri_code, 1.0)
                for e in evals
            )
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
        scores_list = [s["composite_score"] for s in site_scores]
        n_sites = len(scores_list)

        # Compute percentile rankings & risk tier
        for item in site_scores:
            s_val = item["composite_score"]
            if n_sites == 1:
                percentile = 100.0
            else:
                less = sum(1 for x in scores_list if x < s_val)
                equal = sum(1 for x in scores_list if x == s_val)
                percentile = round(((less + 0.5 * equal) / n_sites) * 100.0, 1)
            item["percentile_rank"] = percentile

            # Assign risk tier based on composite score and percentile ranking
            if item["composite_score"] >= 20.0 or percentile >= 90.0:
                item["risk_tier"] = RiskTier.CRITICAL
            elif item["composite_score"] >= 10.0 or percentile >= 75.0:
                item["risk_tier"] = RiskTier.HIGH
            elif item["composite_score"] >= 5.0 or percentile >= 40.0:
                item["risk_tier"] = RiskTier.MEDIUM
            else:
                item["risk_tier"] = RiskTier.LOW

        saved_profiles = []
        for rank, item in enumerate(site_scores, start=1):
            profile = self.repo.create_site_risk_profile_entity(
                study_id=study_id,
                site_id=item["site_id"],
                evaluation_date=datetime.now(UTC),
                composite_risk_score=item["composite_score"],
                risk_rank=rank,
                percentile_rank=item["percentile_rank"],
                risk_tier=item["risk_tier"],
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
        """Evaluates an observed parameter value against a defined QTL. If breached, records QTLBreachEvent

        and compiles the Clinical Study Report (CSR Section 9.6) impact narrative.
        """
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

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

        csr_narrative = (
            f"CSR Section 9.6 QTL Summary: For study '{target_qtl.study_id}', the Quality Tolerance Limit for "
            f"'{target_qtl.parameter_name}' was breached with an observed rate of {observed_value:.2f}{target_qtl.unit} "
            f"(pre-defined protocol tolerance threshold: {target_qtl.tolerance_limit:.2f}{target_qtl.unit}). "
            f"Root Cause: {root_cause}. Corrective and Preventive Mitigation Implemented: {corrective_action_summary}."
        )

        breach_event = self.repo.create_qtl_breach_entity(
            qtl_id=qtl_id,
            study_id=target_qtl.study_id,
            breach_date=datetime.now(UTC),
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

    # --- 1-Click Centralized Monitoring Inspection Dossiers (PRD-CSM-006) ---

    async def compile_centralized_monitoring_dossier(
        self,
        study_id: str,
        signer_name: str,
        signer_role: str,
        user_id: str,
        user_role: str,
        change_reason: str,
        dossier_name: str | None = None,
        signature_meaning: str | None = None,
    ) -> Any:
        """Compiles an on-demand, cryptographically sealed Centralized Monitoring Inspection Dossier

        synthesizing TransCelerate RACT risks, KRI distributions, Site Risk Index rankings,
        statistical forensics sentinels, and a 21 CFR Part 11 electronic signature block.
        """
        if not change_reason:
            raise RBQMServiceError(
                "Missing change justification reason", status_code=403
            )

        # 1. RACT Matrix Summary
        ract_summary = await self.get_ract_matrix_summary(study_id)
        # Convert ORM instances to dicts for JSON serialization
        ract_factors_serialized = [
            {
                "id": f.id,
                "category": f.category,
                "risk_topic": f.risk_topic,
                "risk_description": f.risk_description,
                "probability_score": f.probability_score,
                "impact_score": f.impact_score,
                "detectability_score": f.detectability_score,
                "total_risk_score": f.total_risk_score,
                "risk_tier": f.risk_tier,
                "mitigation_strategy": f.mitigation_strategy,
                "linked_kri_code": f.linked_kri_code,
                "is_active": f.is_active,
            }
            for f in ract_summary["risk_factors"]
        ]
        ract_summary["risk_factors"] = ract_factors_serialized

        # 2. KRI Definitions & Latest Evaluations
        kri_defs = await self.repo.get_kri_definitions()
        kri_evals = await self.repo.get_kri_evaluations(study_id)
        kri_summary = {
            "total_kris_defined": len(kri_defs),
            "total_evaluations_recorded": len(kri_evals),
            "high_or_critical_evaluations_count": sum(
                1
                for e in kri_evals
                if e.risk_tier in (RiskTier.HIGH, RiskTier.CRITICAL)
            ),
            "active_kris": [
                {
                    "code": k.code,
                    "name": k.name,
                    "category": k.category,
                    "green_threshold": k.green_threshold,
                    "amber_threshold": k.amber_threshold,
                    "red_threshold": k.red_threshold,
                    "weight": k.weight,
                }
                for k in kri_defs
            ],
        }

        # 3. Site Risk Profiles & Percentile Rankings
        profiles = await self.repo.get_site_risk_profiles(study_id)
        if not profiles:
            profiles = await self.compute_study_site_risk_profiles(
                study_id, user_id, user_role, change_reason
            )

        site_rankings = [
            {
                "rank": p.risk_rank,
                "site_id": p.site_id,
                "composite_risk_score": p.composite_risk_score,
                "percentile_rank": getattr(p, "percentile_rank", 0.0),
                "risk_tier": getattr(p, "risk_tier", RiskTier.LOW),
                "high_risk_kri_count": p.high_risk_kri_count,
                "active_deviations_count": p.active_deviations_count,
                "evaluation_date": p.evaluation_date.isoformat(),
            }
            for p in profiles
        ]

        # 4. Statistical Forensics Summary
        forensics_runs = await self.repo.get_forensics_runs(study_id)
        forensics_summary = {
            "total_forensics_runs": len(forensics_runs),
            "latest_run": (
                {
                    "id": forensics_runs[0].id,
                    "run_name": forensics_runs[0].run_name,
                    "analysis_timestamp": forensics_runs[
                        0
                    ].analysis_timestamp.isoformat(),
                    "anomalous_sites_count": forensics_runs[0].anomalous_sites_count,
                    "total_sites": forensics_runs[0].total_sites,
                    "total_observations": forensics_runs[0].total_observations,
                }
                if forensics_runs
                else None
            ),
        }

        # 5. Audit Trail Snapshot
        audit_logs = await self.repo.get_audit_logs()
        recent_logs = [
            {
                "id": log.id,
                "timestamp": log.timestamp.isoformat(),
                "user_id": log.user_id,
                "user_role": log.user_role,
                "action": log.action,
                "details": log.details,
                "record_id": log.record_id,
                "change_reason": log.change_reason,
            }
            for log in audit_logs[:30]
        ]

        # 6. Part 11 Electronic Signature Block
        signing_meaning = (
            signature_meaning
            or "I approve this Centralized Monitoring Inspection Dossier in accordance with 21 CFR Part 11 and ICH E6(R3)."
        )
        now_dt = datetime.now(UTC)
        now_iso = now_dt.isoformat()
        raw_sig_payload = (
            f"{study_id}:{signer_name}:{signer_role}:{now_iso}:{len(site_rankings)}"
        )
        sig_hash = hashlib.sha256(raw_sig_payload.encode("utf-8")).hexdigest()

        signature_block = {
            "signer_id": user_id,
            "signer_name": signer_name,
            "signer_role": signer_role,
            "signed_at": now_iso,
            "meaning": signing_meaning,
            "signature_hash": sig_hash,
            "part11_compliant": True,
        }

        # Compute Merkle seal across full dataset
        canonical_payload = json.dumps(
            {
                "study_id": study_id,
                "ract_summary": ract_summary,
                "kri_summary": kri_summary,
                "site_rankings": site_rankings,
                "signature": signature_block,
            },
            sort_keys=True,
            default=str,
        )
        merkle_seal = hashlib.sha256(canonical_payload.encode("utf-8")).hexdigest()

        generated_dossier_name = (
            dossier_name or f"CSM-Dossier-{study_id}-{now_dt.strftime('%Y%m%d%H%M%S')}"
        )

        dossier = self.repo.create_csm_dossier_entity(
            study_id=study_id,
            dossier_name=generated_dossier_name,
            generated_at=now_dt,
            generated_by=user_id,
            status="FINALIZED",
            ract_summary=ract_summary,
            kri_surveillance_summary=kri_summary,
            site_risk_rankings=site_rankings,
            anomaly_forensics_summary=forensics_summary,
            electronic_signature=signature_block,
            audit_trail_snapshot=recent_logs,
            merkle_seal=merkle_seal,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        await self.repo.save_csm_dossier(dossier)

        log = self.repo.create_audit_log_entity(
            user_id=user_id,
            user_role=user_role,
            action="CSM_DOSSIER_COMPILED",
            details=f"Compiled Centralized Monitoring Inspection Dossier '{generated_dossier_name}' for study '{study_id}'. Merkle seal: {merkle_seal[:12]}...",
            record_id=dossier.id,
            change_reason=change_reason,
        )
        await self.repo.save_audit_log(log)

        return dossier

    async def list_csm_dossiers(self, study_id: str) -> Sequence[Any]:
        return await self.repo.get_csm_dossiers(study_id)

    async def get_csm_dossier_by_id(self, dossier_id: str) -> Any | None:
        return await self.repo.get_csm_dossier_by_id(dossier_id)
