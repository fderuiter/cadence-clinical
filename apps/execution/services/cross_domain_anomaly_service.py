"""Core cross-domain eCRF anomaly detection and candidate query staging service.

Requirements: PRD-QRY-008, PRD-SYS-001, PRD-SYS-051
"""

import contextlib
import logging
from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.ai_anomaly_client import AIAnomalyGatewayClient
from apps.execution.database.models import (
    AuditLog,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
)
from apps.execution.domain.anomaly import (
    AnomalyEvaluationResult,
    AnomalySeverity,
    CrossDomainAnomaly,
    CrossDomainAnomalyType,
)

logger = logging.getLogger("execution-cross-domain-anomaly-service")


def _parse_date_safe(val: Any) -> datetime | None:
    """Safely parse a date value or ISO string into a datetime object."""
    if not val:
        return None
    if isinstance(val, datetime):
        return val if val.tzinfo else val.replace(tzinfo=UTC)
    if isinstance(val, str):
        with contextlib.suppress(Exception):
            dt = datetime.fromisoformat(val.replace("Z", "+00:00"))
            return dt if dt.tzinfo else dt.replace(tzinfo=UTC)
    return None


class CrossDomainAnomalyService:
    """Service evaluating multi-domain eCRF observations to detect clinical inconsistencies and stage candidate queries."""

    def __init__(self, ai_client: AIAnomalyGatewayClient | None = None) -> None:
        self.ai_client = ai_client or AIAnomalyGatewayClient()

    async def evaluate_subject_cross_domain_anomalies(
        self,
        session: AsyncSession,
        subject_id: str,
        study_id: str,
        enable_ai: bool = True,
        auto_stage_queries: bool = True,
    ) -> AnomalyEvaluationResult:
        """Evaluates all observations for a subject across clinical domains and stages candidate queries.

        Args:
            session: Async database session.
            subject_id: The target clinical subject ID.
            study_id: The target clinical study ID.
            enable_ai: Whether to invoke AI Gateway semantic reasoning if available.
            auto_stage_queries: Whether to persist detected anomalies as CANDIDATE ClinicalQuery rows.

        Returns:
            AnomalyEvaluationResult containing detected anomalies and staging metrics.
        """
        # 1. Fetch Subject & Observations
        subj_stmt = select(ClinicalSubject).where(
            (ClinicalSubject.subject_id == subject_id)
            | (ClinicalSubject.id == subject_id),
            ClinicalSubject.is_deleted.is_(False),
        )
        subj_res = await session.execute(subj_stmt)
        subject = subj_res.scalars().first()
        site_id = subject.site_id if subject else None

        obs_stmt = (
            select(ClinicalObservation)
            .where(
                ClinicalObservation.subject_id == subject_id,
                ClinicalObservation.study_id == study_id,
                ClinicalObservation.is_deleted.is_(False),
            )
            .order_by(ClinicalObservation.observation_date.asc())
        )
        obs_res = await session.execute(obs_stmt)
        all_obs = list(obs_res.scalars().all())

        if not all_obs:
            return AnomalyEvaluationResult(
                subject_id=subject_id,
                study_id=study_id,
                anomalies=[],
                queries_staged_count=0,
            )

        # Categorize observations by domain
        obs_by_domain: dict[str, list[ClinicalObservation]] = {}
        for obs in all_obs:
            dom = (obs.domain or "UNKNOWN").upper().strip()
            obs_by_domain.setdefault(dom, []).append(obs)

        detected_anomalies: list[CrossDomainAnomaly] = []

        # 2. Run Deterministic Cross-Domain Rules
        detected_anomalies.extend(
            self._evaluate_ae_cm_rules(subject_id, study_id, site_id, obs_by_domain)
        )
        detected_anomalies.extend(
            self._evaluate_ae_lb_rules(subject_id, study_id, site_id, obs_by_domain)
        )
        detected_anomalies.extend(
            self._evaluate_ae_vs_rules(subject_id, study_id, site_id, obs_by_domain)
        )
        detected_anomalies.extend(
            self._evaluate_ds_ex_ae_rules(subject_id, study_id, site_id, obs_by_domain)
        )

        # 3. Optional AI Semantic Reasoner Evaluation
        if enable_ai and self.ai_client.base_url:
            ai_anomalies = await self._evaluate_ai_reasoner(
                subject_id, study_id, site_id, all_obs
            )
            detected_anomalies.extend(ai_anomalies)

        staged_count = 0
        if auto_stage_queries and detected_anomalies:
            staged_count = await self._stage_candidate_queries(
                session, subject_id, study_id, detected_anomalies
            )

        return AnomalyEvaluationResult(
            subject_id=subject_id,
            study_id=study_id,
            anomalies=detected_anomalies,
            queries_staged_count=staged_count,
        )

    def _evaluate_ae_cm_rules(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        obs_by_domain: dict[str, list[ClinicalObservation]],
    ) -> list[CrossDomainAnomaly]:
        """Evaluates Adverse Event <-> Concomitant Medication correlations."""
        anomalies: list[CrossDomainAnomaly] = []
        ae_obs = obs_by_domain.get("AE", [])
        cm_obs = obs_by_domain.get("CM", [])

        # Rule 1: Moderate / Severe AE without Concomitant Medication
        for ae in ae_obs:
            val_str = (ae.value_string or "").upper()
            add_props = ae.additional_properties or {}

            severity = str(
                add_props.get("AESEV") or add_props.get("severity") or val_str
            ).upper()
            is_serious = str(
                add_props.get("AESER") or add_props.get("serious") or ""
            ).upper() in ("Y", "YES", "TRUE")
            term = str(add_props.get("AETERM") or ae.test_code or "Adverse Event")

            if severity in ("SEVERE", "MODERATE") or is_serious:
                # Check if there are any concomitant medications
                if not cm_obs:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.AE_WITHOUT_CONCOMITANT_MED,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=ae.visit_id,
                            primary_domain="AE",
                            primary_test_code=ae.test_code,
                            correlated_domain="CM",
                            severity=AnomalySeverity.HIGH
                            if (severity == "SEVERE" or is_serious)
                            else AnomalySeverity.MEDIUM,
                            message=f"{severity.capitalize()} Adverse Event '{term}' recorded without concomitant medication.",
                            explanation=(
                                f"Subject has a documented {severity} adverse event ({term}), "
                                "but no corresponding concomitant medication was recorded to manage or treat the event."
                            ),
                            confidence_score=1.0,
                            observation_ids=[ae.id],
                            form_id=ae.page_id,
                        )
                    )

        # Rule 2: Concomitant Medication Indication without corresponding AE or Medical History
        mh_obs = obs_by_domain.get("MH", [])
        ae_terms = {
            str(ae.additional_properties.get("AETERM", "")).upper()
            for ae in ae_obs
            if ae.additional_properties
        }
        ae_terms.update({(ae.test_code or "").upper() for ae in ae_obs})
        ae_terms.update({str(ae.value_string or "").upper() for ae in ae_obs})
        mh_terms = {
            str(mh.additional_properties.get("MHTERM", "")).upper()
            for mh in mh_obs
            if mh.additional_properties
        }
        mh_terms.update({(mh.test_code or "").upper() for mh in mh_obs})

        for cm in cm_obs:
            add_props = cm.additional_properties or {}
            indication = str(
                add_props.get("CMINDC")
                or add_props.get("indication")
                or cm.value_string
                or ""
            ).strip()
            med_name = str(add_props.get("CMTRT") or cm.test_code or "Medication")

            if indication:
                ind_upper = indication.upper()
                # Check if indication matches known AE or MH conditions
                has_matching_condition = any(
                    ind_word in t or t in ind_word
                    for t in (ae_terms | mh_terms)
                    if t and len(t) > 3
                    for ind_word in ind_upper.split()
                    if len(ind_word) > 3
                )

                # Check if indication flags acute symptomatic treatment (e.g. pain, infection, allergy)
                acute_trigger_words = [
                    "HEADACHE",
                    "FEVER",
                    "PAIN",
                    "INFECTION",
                    "NAUSEA",
                    "RASH",
                    "HYPERTENSION",
                    "VOMITING",
                    "ALLERGY",
                    "COUGH",
                ]
                is_acute_indication = any(w in ind_upper for w in acute_trigger_words)

                if is_acute_indication and not has_matching_condition:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.CONCOMITANT_MED_WITHOUT_AE,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=cm.visit_id,
                            primary_domain="CM",
                            primary_test_code=cm.test_code,
                            correlated_domain="AE",
                            severity=AnomalySeverity.MEDIUM,
                            message=f"Concomitant medication '{med_name}' for '{indication}' lacks matching AE/MH record.",
                            explanation=(
                                f"Medication '{med_name}' was initiated with indication '{indication}', "
                                "but no corresponding Adverse Event or Medical History entry exists for this subject."
                            ),
                            confidence_score=0.95,
                            observation_ids=[cm.id],
                            form_id=cm.page_id,
                        )
                    )

        # Rule 3: Temporal sequence mismatch between CM and AE
        for cm in cm_obs:
            add_props = cm.additional_properties or {}
            cm_end = _parse_date_safe(add_props.get("CMENDTC"))

            for ae in ae_obs:
                ae_props = ae.additional_properties or {}
                ae_start = _parse_date_safe(
                    ae_props.get("AESTDTC") or ae.observation_date
                )

                # If CM ended before AE onset date
                if cm_end and ae_start and cm_end < ae_start:
                    cm_trt = add_props.get("CMTRT") or cm.test_code
                    ae_trt = ae_props.get("AETERM") or ae.test_code
                    # If explicitly marked as treatment for this AE
                    ind = str(add_props.get("CMINDC", "")).upper()
                    if ae_trt.upper() in ind:
                        anomalies.append(
                            CrossDomainAnomaly(
                                anomaly_type=CrossDomainAnomalyType.TEMPORAL_SEQUENCE_MISMATCH,
                                study_id=study_id,
                                subject_id=subject_id,
                                site_id=site_id,
                                visit_id=cm.visit_id,
                                primary_domain="CM",
                                primary_test_code=cm.test_code,
                                correlated_domain="AE",
                                severity=AnomalySeverity.HIGH,
                                message=f"Medication '{cm_trt}' stop date precedes AE '{ae_trt}' onset date.",
                                explanation=(
                                    f"Concomitant medication '{cm_trt}' indicated for '{ae_trt}' ended on {cm_end.date()}, "
                                    f"which is before the reported AE onset date {ae_start.date()}."
                                ),
                                confidence_score=1.0,
                                observation_ids=[cm.id, ae.id],
                            )
                        )

        return anomalies

    def _evaluate_ae_lb_rules(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        obs_by_domain: dict[str, list[ClinicalObservation]],
    ) -> list[CrossDomainAnomaly]:
        """Evaluates Adverse Event <-> Laboratory Diagnostics correlations."""
        anomalies: list[CrossDomainAnomaly] = []
        lb_obs = obs_by_domain.get("LB", [])
        ae_obs = obs_by_domain.get("AE", [])

        # Rule 4: Marked lab abnormality with no reported Adverse Event
        ae_text_corpus = " ".join(
            [
                str(ae.test_code or "")
                + " "
                + str(ae.value_string or "")
                + " "
                + str((ae.additional_properties or {}).get("AETERM", ""))
                for ae in ae_obs
            ]
        ).upper()

        for lb in lb_obs:
            is_critical = False
            lab_issue_name = ""
            test_code_up = (lb.test_code or "").upper()
            val = lb.value

            # Check critical lab indicators
            ind = (lb.lab_indicator or "").upper()
            if ind in (
                "CRITICAL_HIGH",
                "CRITICAL_LOW",
                "PANIC",
                "HIGH_HIGH",
                "LOW_LOW",
            ):
                is_critical = True
                lab_issue_name = f"Critical {lb.test_code} value ({val})"

            # Specific clinical biomarker threshold evaluations
            if val is not None:
                # Liver transaminases ALT/AST > 3x ULN or Bilirubin > 2x ULN
                if test_code_up in ("ALT", "AST", "SGPT", "SGOT") and val > 150.0:
                    is_critical = True
                    lab_issue_name = (
                        f"Marked Transaminitis ({test_code_up} = {val} U/L)"
                    )
                elif test_code_up in ("BILI", "TBIL") and val > 3.0:
                    is_critical = True
                    lab_issue_name = f"Marked Hyperbilirubinemia (BILI = {val} mg/dL)"
                # Renal function: Creatinine > 2.5 mg/dL
                elif test_code_up in ("CREAT", "CREATININE") and val > 2.5:
                    is_critical = True
                    lab_issue_name = f"Elevated Creatinine ({val} mg/dL)"
                # Electrolytes: Potassium > 6.0 or < 2.5
                elif test_code_up in ("K", "POTASSIUM") and (val > 6.0 or val < 2.5):
                    is_critical = True
                    lab_issue_name = f"Marked Electrolyte Disturbance (K = {val} mEq/L)"

            if is_critical:
                # Check if matching AE is reported
                expected_terms = [
                    "HEPAT",
                    "LIVER",
                    "TRANSAMIN",
                    "KIDNEY",
                    "RENAL",
                    "CREAT",
                    "POTASSIUM",
                    "ELECTROLYTE",
                    "LABORATORY",
                    "ABNORMAL",
                ]
                has_ae = any(t in ae_text_corpus for t in expected_terms)

                if not has_ae:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.MARKED_LAB_ABNORMALITY_WITHOUT_AE,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=lb.visit_id,
                            primary_domain="LB",
                            primary_test_code=lb.test_code,
                            correlated_domain="AE",
                            severity=AnomalySeverity.HIGH,
                            message=f"Marked laboratory abnormality ({lab_issue_name}) with no documented AE.",
                            explanation=(
                                f"Laboratory finding '{lb.test_code}' is at critical/marked levels ({val}), "
                                "but no corresponding Adverse Event was reported in the AE domain."
                            ),
                            confidence_score=1.0,
                            observation_ids=[lb.id],
                            form_id=lb.page_id,
                        )
                    )

        return anomalies

    def _evaluate_ae_vs_rules(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        obs_by_domain: dict[str, list[ClinicalObservation]],
    ) -> list[CrossDomainAnomaly]:
        """Evaluates Adverse Event <-> Vital Signs correlations."""
        anomalies: list[CrossDomainAnomaly] = []
        vs_obs = obs_by_domain.get("VS", [])
        ae_obs = obs_by_domain.get("AE", [])

        ae_text_corpus = " ".join(
            [
                str(ae.test_code or "")
                + " "
                + str(ae.value_string or "")
                + " "
                + str((ae.additional_properties or {}).get("AETERM", ""))
                for ae in ae_obs
            ]
        ).upper()

        for vs in vs_obs:
            code_up = (vs.test_code or "").upper()
            val = vs.value
            if val is None:
                continue

            anomaly_msg = None
            if code_up in ("SYSBP", "VSSBP") and val >= 180.0:
                anomaly_msg = f"Hypertensive crisis (Systolic BP = {val} mmHg)"
            elif code_up in ("DIABP", "VSDBP") and val >= 120.0:
                anomaly_msg = f"Hypertensive crisis (Diastolic BP = {val} mmHg)"
            elif code_up in ("HR", "PULSE", "VSHR") and val >= 130.0:
                anomaly_msg = f"Severe tachycardia (Heart Rate = {val} bpm)"
            elif code_up in ("HR", "PULSE", "VSHR") and val <= 40.0:
                anomaly_msg = f"Severe bradycardia (Heart Rate = {val} bpm)"
            elif code_up in ("TEMP", "VSTEMP") and val >= 39.5:
                anomaly_msg = f"High-grade pyrexia (Temperature = {val} °C)"

            if anomaly_msg:
                vital_terms = [
                    "HYPERTENSION",
                    "PRESSURE",
                    "TACHYCARDIA",
                    "BRADYCARDIA",
                    "FEVER",
                    "PYREXIA",
                    "VITAL",
                ]
                has_ae = any(t in ae_text_corpus for t in vital_terms)

                if not has_ae:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.CRITICAL_VITALS_WITHOUT_AE,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=vs.visit_id,
                            primary_domain="VS",
                            primary_test_code=vs.test_code,
                            correlated_domain="AE",
                            severity=AnomalySeverity.HIGH,
                            message=f"Critical vital sign finding ({anomaly_msg}) with no documented AE.",
                            explanation=(
                                f"Vital sign '{vs.test_code}' recorded at critical threshold ({val}), "
                                "without an associated Adverse Event entered in the AE domain."
                            ),
                            confidence_score=1.0,
                            observation_ids=[vs.id],
                            form_id=vs.page_id,
                        )
                    )

        return anomalies

    def _evaluate_ds_ex_ae_rules(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        obs_by_domain: dict[str, list[ClinicalObservation]],
    ) -> list[CrossDomainAnomaly]:
        """Evaluates Disposition / Exposure discontinuation reason vs documented AEs."""
        anomalies: list[CrossDomainAnomaly] = []
        ds_obs = obs_by_domain.get("DS", [])
        ex_obs = obs_by_domain.get("EX", [])
        ae_obs = obs_by_domain.get("AE", [])

        # Check DS (Study Discontinuation / Withdrawal)
        for ds in ds_obs:
            add_props = ds.additional_properties or {}
            term = str(add_props.get("DSTERM") or ds.value_string or "").upper()
            decod = str(add_props.get("DSDECOD") or "").upper()

            if "ADVERSE EVENT" in term or "ADVERSE EVENT" in decod or "SAFETY" in term:
                if not ae_obs:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.DRUG_DISCONTINUATION_WITHOUT_AE,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=ds.visit_id,
                            primary_domain="DS",
                            primary_test_code=ds.test_code,
                            correlated_domain="AE",
                            severity=AnomalySeverity.HIGH,
                            message="Subject discontinued study due to Adverse Event, but zero AEs are recorded.",
                            explanation=(
                                f"Disposition record indicates discontinuation reason '{term or decod}', "
                                "but no Adverse Event records exist for this subject in the trial database."
                            ),
                            confidence_score=1.0,
                            observation_ids=[ds.id],
                            form_id=ds.page_id,
                        )
                    )

        # Check EX (Dose Modification / Interruption)
        for ex in ex_obs:
            add_props = ex.additional_properties or {}
            reason = str(
                add_props.get("EXADJ")
                or add_props.get("reason")
                or ex.value_string
                or ""
            ).upper()
            if "ADVERSE" in reason or "TOXICITY" in reason:
                if not ae_obs:
                    anomalies.append(
                        CrossDomainAnomaly(
                            anomaly_type=CrossDomainAnomalyType.DRUG_DISCONTINUATION_WITHOUT_AE,
                            study_id=study_id,
                            subject_id=subject_id,
                            site_id=site_id,
                            visit_id=ex.visit_id,
                            primary_domain="EX",
                            primary_test_code=ex.test_code,
                            correlated_domain="AE",
                            severity=AnomalySeverity.HIGH,
                            message=f"Dose adjusted due to '{reason}', but zero AEs are recorded.",
                            explanation=(
                                f"Drug exposure record indicates dose adjustment due to '{reason}', "
                                "but no Adverse Event records exist for this subject."
                            ),
                            confidence_score=1.0,
                            observation_ids=[ex.id],
                            form_id=ex.page_id,
                        )
                    )

        return anomalies

    async def _evaluate_ai_reasoner(
        self,
        subject_id: str,
        study_id: str,
        site_id: str | None,
        all_obs: list[ClinicalObservation],
    ) -> list[CrossDomainAnomaly]:
        """Summarizes subject timeline and queries AI Gateway Tier 2 model."""
        summary_lines = []
        for o in all_obs[:40]:  # Limit to 40 most relevant observations
            dt_str = (
                o.observation_date.strftime("%Y-%m-%d") if o.observation_date else "N/A"
            )
            val = o.value if o.value is not None else o.value_string
            props = o.additional_properties or {}
            summary_lines.append(
                f"- [{o.domain}] {o.test_code}: {val} (Date: {dt_str}, Extra: {props})"
            )

        events_summary = "\n".join(summary_lines)
        return await self.ai_client.analyze_cross_domain_consistency(
            subject_id=subject_id,
            study_id=study_id,
            events_summary=events_summary,
            site_id=site_id,
        )

    async def _stage_candidate_queries(
        self,
        session: AsyncSession,
        subject_id: str,
        study_id: str,
        anomalies: list[CrossDomainAnomaly],
    ) -> int:
        """Stages detected anomalies as CANDIDATE ClinicalQuery records with deduplication."""
        # 1. Fetch active queries for this subject
        stmt_q = select(ClinicalQuery).where(
            ClinicalQuery.study_id == study_id,
            ClinicalQuery.subject_id == subject_id,
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res_q = await session.execute(stmt_q)
        active_queries = list(res_q.scalars().all())

        # Existing active query coordinate map: (domain, test_code, rule_id)
        existing_keys = {(q.domain, q.test_code, q.rule_id) for q in active_queries}

        staged_count = 0
        for anomaly in anomalies:
            rule_id = f"ANOMALY_{anomaly.anomaly_type}"
            key = (anomaly.primary_domain, anomaly.primary_test_code, rule_id)

            if key in existing_keys:
                continue

            obs_id = anomaly.observation_ids[0] if anomaly.observation_ids else None

            candidate_query = ClinicalQuery(
                study_id=study_id,
                site_id=anomaly.site_id,
                subject_id=subject_id,
                visit_id=anomaly.visit_id,
                domain=anomaly.primary_domain,
                test_code=anomaly.primary_test_code,
                observation_id=obs_id,
                field_link=f"{anomaly.primary_domain}.{anomaly.primary_test_code}",
                status="CANDIDATE",
                origin="AI_ASSISTED"
                if anomaly.model_identifier
                else "ANOMALY_DETECTOR",
                priority=anomaly.severity.value
                if isinstance(anomaly.severity, AnomalySeverity)
                else str(anomaly.severity),
                rule_id=rule_id,
                message=anomaly.message,
                explanation=anomaly.explanation,
                created_by="ANOMALY_DETECTOR_WORKER",
                query_type="CROSS_DOMAIN_ANOMALY",
                action_required="Data Manager review and adjudication required",
                form_id=anomaly.form_id,
                field_id=anomaly.field_id,
            )
            session.add(candidate_query)
            await session.flush()

            # Record Part 11 Audit Log
            audit_log = AuditLog(
                table_name="clinical_queries",
                record_id=candidate_query.id,
                action="INSERT",
                user_id="ANOMALY_DETECTOR_WORKER",
                change_reason=f"Staged candidate query via cross-domain anomaly detector ({anomaly.anomaly_type})",
            )
            session.add(audit_log)

            existing_keys.add(key)
            staged_count += 1
            logger.info(
                f"Staged new CANDIDATE query {candidate_query.id} for subject {subject_id} on rule {rule_id}"
            )

        return staged_count
