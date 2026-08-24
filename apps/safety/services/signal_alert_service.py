"""Orchestration service for Disproportionality Signal Detection, Alerts, and Periodic Reports.

Requirements: PRD-PVG-003, PRD-PVG-004, PRD-PVG-008, ADR-2199
"""

import logging
import uuid
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.domain.periodic_report_models import (
    PeriodicReportCompilationRequest,
    PeriodicReportResponseDTO,
    PeriodicReportSignResponse,
    PeriodicReportStatus,
)
from apps.safety.domain.signal_models import (
    SafetySignalAlertReviewRequest,
    SafetySignalAlertSeverity,
    SafetySignalAlertSignResponse,
    SafetySignalAlertStatus,
    SafetySignalReviewDecision,
    SignalDetectionScanRequest,
    SignalDetectionScanResponse,
    SignalStrength,
)
from apps.safety.infrastructure.models import (
    PeriodicSafetyReportRecord,
    SafetyCaseICSR,
    SafetySignalAlert,
    SafetySignalDetectionRun,
    write_audit_log,
)
from apps.safety.services.periodic_report_compiler import (
    PeriodicSafetyReportCompiler,
)
from apps.safety.services.signal_detection_engine import (
    SafetySignalDetectionEngine,
)
from packages.security.signing import generate_canonical_signature

logger = logging.getLogger("safety-signal-alert-service")

ALLOWED_PHYSICIAN_SIGNING_ROLES = {
    "medical_monitor",
    "qppv",
    "qppv_officer",
    "qualified_person_pv",
    "safety_officer",
    "safety_physician",
    "sponsor_admin",
    "sponsor_medical_monitor",
    "sponsor_pv_officer",
    "sponsor_safety_physician",
    "super_admin",
}


class SafetySignalAlertService:
    """Coordinates disproportionality signal analysis, alert triage, Part 11 signatures, and periodic reports."""

    def __init__(self, session: AsyncSession):
        self.session = session

    async def execute_scan(
        self,
        request: SignalDetectionScanRequest,
        user_id: str,
        reason_for_change: str,
    ) -> SignalDetectionScanResponse:
        """Executes a statistical disproportionality scan and optionally creates alerts.

        Args:
            request: Scan parameters and optional raw cases.
            user_id: User executing the scan.
            reason_for_change: GxP change rationale.

        Returns:
            SignalDetectionScanResponse with all metric tables and alert summary.
        """
        cases = request.cases
        if not cases:
            # Load cases from database
            stmt = select(SafetyCaseICSR)
            res = await self.session.execute(stmt)
            orm_cases = res.scalars().all()
            cases = []
            for c in orm_cases:
                cd = c.case_data or {}
                cases.append(
                    {
                        "case_id": c.worldwide_unique_case_id,
                        "subject_id": c.patient_id,
                        "drug_name": cd.get("medicinal_product")
                        or cd.get("drug_name", "UNKNOWN"),
                        "meddra_pt": cd.get("reaction_pt")
                        or cd.get("meddra_pt", "Unknown PT"),
                        "meddra_soc": cd.get("soc_name") or cd.get("meddra_soc"),
                        "onset_date": cd.get("reaction_start_date")
                        or c.created_at.isoformat(),
                        "seriousness_death": bool(cd.get("seriousness_death", False)),
                        "seriousness_life_threatening": bool(
                            cd.get("seriousness_life_threatening", False)
                        ),
                        "seriousness_hospitalization": bool(
                            cd.get("seriousness_hospitalization", False)
                        ),
                        "seriousness_disability": bool(
                            cd.get("seriousness_disability", False)
                        ),
                        "seriousness_congenital_anomaly": bool(
                            cd.get("seriousness_congenital_anomaly", False)
                        ),
                        "seriousness_other": bool(cd.get("seriousness_other", False)),
                        "causality": cd.get("causality", "RELATED"),
                        "expectedness": cd.get("expectedness", "UNEXPECTED"),
                    }
                )

        # Run vectorized disproportionality engine
        metric_results = SafetySignalDetectionEngine.scan_safety_dataset(
            cases=cases,
            target_drug=request.target_drug,
            stratification_levels=request.stratification_levels,
            benchmark_dataset=request.benchmark_dataset.value,
            benchmark_cases=request.benchmark_cases,
        )

        signals_count = sum(1 for m in metric_results if m.overall_signal_flag)
        run_id = f"run_{uuid.uuid4().hex[:12]}"
        now_dt = datetime.now(UTC).replace(tzinfo=None)

        # Create Run ORM record
        run_record = SafetySignalDetectionRun(
            id=run_id,
            study_id=request.study_id,
            target_drug=request.target_drug,
            benchmark_dataset=request.benchmark_dataset.value,
            total_cases_analyzed=len(cases),
            signals_detected_count=signals_count,
            summary_metrics=[m.model_dump() for m in metric_results[:50]],
            created_at=now_dt,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        self.session.add(run_record)
        await self.session.flush()

        alerts_triggered: int = 0

        # Trigger alerts if enabled
        if request.auto_trigger_alerts:
            for metric in metric_results:
                # Check threshold breaches
                breach = (
                    metric.overall_signal_flag
                    or (
                        metric.case_count_a >= request.min_case_count
                        and metric.prr >= request.prr_threshold
                        and metric.chi_square_yates >= request.chi_square_threshold
                    )
                    or (
                        metric.case_count_a >= request.min_case_count
                        and metric.ror_ci_lower > request.ror_ci_lower_threshold
                    )
                    or (
                        metric.case_count_a >= request.min_case_count
                        and metric.eb05 >= request.eb05_threshold
                    )
                )

                if breach:
                    # Determine severity
                    if (
                        metric.signal_strength == SignalStrength.CRITICAL
                        or metric.prr >= 5.0
                    ):
                        sev = SafetySignalAlertSeverity.CRITICAL.value
                    elif (
                        metric.signal_strength == SignalStrength.STRONG
                        or metric.prr >= 3.0
                    ):
                        sev = SafetySignalAlertSeverity.HIGH.value
                    elif metric.signal_strength == SignalStrength.MODERATE:
                        sev = SafetySignalAlertSeverity.MEDIUM.value
                    else:
                        sev = SafetySignalAlertSeverity.LOW.value

                    triage_summary = (
                        f"Statistical threshold breach detected for drug '{metric.drug_name}' and "
                        f"term '{metric.meddra_pt}' (Level: {metric.term_level.value}). "
                        f"PRR: {metric.prr:.2f} [{metric.prr_ci_lower:.2f}, {metric.prr_ci_upper:.2f}], "
                        f"Chi-Square: {metric.chi_square_yates:.2f}, "
                        f"ROR: {metric.ror:.2f} [{metric.ror_ci_lower:.2f}, {metric.ror_ci_upper:.2f}], "
                        f"EB05/EBGM: {metric.eb05:.2f}/{metric.ebgm:.2f} (a={metric.case_count_a}, E={metric.expected_count_e:.2f})."
                    )

                    alert_id = f"alert_{uuid.uuid4().hex[:12]}"
                    alert_record = SafetySignalAlert(
                        id=alert_id,
                        detection_run_id=run_id,
                        study_id=request.study_id,
                        target_drug=metric.drug_name,
                        meddra_term=metric.meddra_pt,
                        term_level=metric.term_level.value,
                        meddra_soc=metric.meddra_soc,
                        case_count_a=metric.case_count_a,
                        expected_count_e=metric.expected_count_e,
                        prr=metric.prr,
                        prr_ci_lower=metric.prr_ci_lower,
                        prr_ci_upper=metric.prr_ci_upper,
                        chi_square_yates=metric.chi_square_yates,
                        ror=metric.ror,
                        ror_ci_lower=metric.ror_ci_lower,
                        ror_ci_upper=metric.ror_ci_upper,
                        ebgm=metric.ebgm,
                        eb05=metric.eb05,
                        eb95=metric.eb95,
                        severity=sev,
                        status=SafetySignalAlertStatus.TRIGGERED.value,
                        triage_summary=triage_summary,
                        created_at=now_dt,
                        created_by=user_id,
                        reason_for_change=f"Automated threshold breach trigger: {metric.meddra_pt}",
                        version_index=1,
                    )
                    self.session.add(alert_record)
                    alerts_triggered += 1

        # Audit log
        await write_audit_log(
            session=self.session,
            created_by=user_id,
            action="DISPROPORTIONALITY_SIGNAL_SCAN_EXECUTED",
            details=(
                f"Executed disproportionality scan on drug '{request.target_drug}'. "
                f"Analyzed {len(cases)} cases across {len(metric_results)} terms. "
                f"Identified {signals_count} signals; created {alerts_triggered} alerts."
            ),
            record_id=run_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )

        return SignalDetectionScanResponse(
            run_id=run_id,
            study_id=request.study_id,
            target_drug=request.target_drug,
            benchmark_dataset=request.benchmark_dataset.value,
            total_cases_analyzed=len(cases),
            total_terms_evaluated=len(metric_results),
            signals_detected_count=signals_count,
            alerts_triggered_count=alerts_triggered,
            results=metric_results,
            multi_item_results=[],
            created_at=now_dt.isoformat(),
        )

    async def list_alerts(
        self,
        study_id: str | None = None,
        target_drug: str | None = None,
        status: str | None = None,
        severity: str | None = None,
    ) -> list[SafetySignalAlert]:
        """Lists safety signal review alerts with optional filtering."""
        stmt = select(SafetySignalAlert)
        if study_id:
            stmt = stmt.where(SafetySignalAlert.study_id == study_id)
        if target_drug:
            stmt = stmt.where(SafetySignalAlert.target_drug == target_drug)
        if status:
            stmt = stmt.where(SafetySignalAlert.status == status)
        if severity:
            stmt = stmt.where(SafetySignalAlert.severity == severity)

        stmt = stmt.order_by(SafetySignalAlert.created_at.desc())
        res = await self.session.execute(stmt)
        return list(res.scalars().all())

    async def get_alert(self, alert_id: str) -> SafetySignalAlert | None:
        """Retrieves a single safety signal alert."""
        stmt = select(SafetySignalAlert).where(SafetySignalAlert.id == alert_id)
        res = await self.session.execute(stmt)
        return res.scalars().first()

    async def review_alert(
        self,
        alert_id: str,
        request: SafetySignalAlertReviewRequest,
        user_id: str,
        reason_for_change: str,
    ) -> SafetySignalAlert:
        """Reviews and updates alert disposition."""
        alert = await self.get_alert(alert_id)
        if not alert:
            raise ValueError(f"SafetySignalAlert '{alert_id}' not found.")

        now_dt = datetime.now(UTC).replace(tzinfo=None)
        alert.review_decision = request.decision.value
        alert.review_justification = request.justification
        alert.clinical_summary = request.clinical_summary
        if request.assigned_safety_officer:
            alert.assigned_safety_officer = request.assigned_safety_officer

        if request.decision == SafetySignalReviewDecision.CONFIRM_SIGNAL:
            alert.status = SafetySignalAlertStatus.CONFIRMED_SIGNAL.value
        elif request.decision == SafetySignalReviewDecision.REFUTE_SIGNAL:
            alert.status = SafetySignalAlertStatus.REFUTED.value
        elif request.decision == SafetySignalReviewDecision.CLOSE_ALERT:
            alert.status = SafetySignalAlertStatus.CLOSED.value
        else:
            alert.status = SafetySignalAlertStatus.UNDER_REVIEW.value

        alert.reviewed_by = user_id
        alert.reviewed_at = now_dt
        alert.version_index += 1
        alert.reason_for_change = reason_for_change

        await write_audit_log(
            session=self.session,
            created_by=user_id,
            action="SAFETY_SIGNAL_ALERT_REVIEWED",
            details=f"Alert {alert_id} reviewed by {user_id}: Decision={request.decision.value}, Status={alert.status}",
            record_id=alert_id,
            reason_for_change=reason_for_change,
            version_index=alert.version_index,
        )
        return alert

    async def sign_alert_review(
        self,
        alert_id: str,
        decision: SafetySignalReviewDecision,
        justification: str,
        clinical_summary: str | None,
        user_id: str,
        user_roles: list[str],
        reason_for_change: str,
        signature_secret: str,
        meaning_of_signature: str,
    ) -> SafetySignalAlertSignResponse:
        """Applies a 21 CFR Part 11 Electronic Signature to approve a safety signal disposition."""
        alert = await self.get_alert(alert_id)
        if not alert:
            raise ValueError(f"SafetySignalAlert '{alert_id}' not found.")

        # Verify role authorization
        if user_roles:
            roles_set = (
                set(user_roles)
                if isinstance(user_roles, (list, set))
                else {str(user_roles)}
            )
            if not roles_set.intersection(ALLOWED_PHYSICIAN_SIGNING_ROLES):
                raise ValueError(
                    f"User '{user_id}' with roles {user_roles} is not authorized to sign safety signal reviews."
                )

        now_dt = datetime.now(UTC)
        manifest_id = f"sig_sig_{uuid.uuid4().hex[:12]}"
        payload_to_sign = {
            "alert_id": alert.id,
            "drug_name": alert.target_drug,
            "meddra_term": alert.meddra_term,
            "decision": decision.value,
            "justification": justification,
            "signer": user_id,
            "timestamp": now_dt.isoformat(),
            "meaning": meaning_of_signature,
        }
        _ = generate_canonical_signature(
            payload_to_sign, signature_secret.encode("utf-8")
        )

        alert.review_decision = decision.value
        alert.review_justification = justification
        if clinical_summary:
            alert.clinical_summary = clinical_summary
        alert.reviewed_by = user_id
        alert.reviewed_at = now_dt.replace(tzinfo=None)
        alert.esignature_manifest_id = manifest_id
        alert.status = (
            SafetySignalAlertStatus.CONFIRMED_SIGNAL.value
            if decision == SafetySignalReviewDecision.CONFIRM_SIGNAL
            else SafetySignalAlertStatus.REFUTED.value
            if decision == SafetySignalReviewDecision.REFUTE_SIGNAL
            else SafetySignalAlertStatus.CLOSED.value
        )
        alert.version_index += 1
        alert.reason_for_change = reason_for_change

        await write_audit_log(
            session=self.session,
            created_by=user_id,
            action="SAFETY_SIGNAL_ALERT_SIGNED",
            details=(
                f"21 CFR Part 11 e-signature applied on alert {alert_id} by {user_id} ({user_roles}). "
                f"Decision: {decision.value}. Manifest: {manifest_id}."
            ),
            record_id=alert.id,
            reason_for_change=reason_for_change,
            version_index=alert.version_index,
        )

        return SafetySignalAlertSignResponse(
            alert_id=alert.id,
            status=SafetySignalAlertStatus(alert.status),
            reviewed_by=user_id,
            reviewed_at=now_dt.isoformat(),
            esignature_manifest_id=manifest_id,
            message="Safety signal review assessment successfully approved with 21 CFR Part 11 electronic signature.",
        )

    async def compile_periodic_report(
        self,
        request: PeriodicReportCompilationRequest,
        user_id: str,
        reason_for_change: str,
    ) -> PeriodicReportResponseDTO:
        """Compiles, structures, and stores a DSUR / PSUR / PBRER periodic regulatory report."""
        cases = request.cases or []
        if not cases:
            # Query cases from DB
            stmt = select(SafetyCaseICSR)
            res = await self.session.execute(stmt)
            orm_cases = res.scalars().all()
            for c in orm_cases:
                cd = c.case_data or {}
                cases.append(
                    {
                        "case_id": c.worldwide_unique_case_id,
                        "subject_id": c.patient_id,
                        "study_id": request.study_id,
                        "treatment_arm": cd.get("treatment_arm", request.compound_name),
                        "drug_name": cd.get("medicinal_product")
                        or request.compound_name,
                        "meddra_pt": cd.get("reaction_pt")
                        or cd.get("meddra_pt", "Adverse Event"),
                        "meddra_soc": cd.get("soc_name")
                        or cd.get("meddra_soc", "General disorders"),
                        "onset_date": cd.get("reaction_start_date")
                        or c.created_at.isoformat()[:10],
                        "seriousness_death": bool(cd.get("seriousness_death", False)),
                        "seriousness_life_threatening": bool(
                            cd.get("seriousness_life_threatening", False)
                        ),
                        "seriousness_hospitalization": bool(
                            cd.get("seriousness_hospitalization", False)
                        ),
                        "seriousness_disability": bool(
                            cd.get("seriousness_disability", False)
                        ),
                        "seriousness_congenital_anomaly": bool(
                            cd.get("seriousness_congenital_anomaly", False)
                        ),
                        "seriousness_other": bool(cd.get("seriousness_other", False)),
                        "causality": cd.get("causality", "RELATED"),
                        "expectedness": cd.get("expectedness", "UNEXPECTED"),
                        "outcome": cd.get("outcome", "RECOVERED"),
                        "action_taken": cd.get("action_taken", "DRUG_WITHDRAWN"),
                    }
                )

        payload = PeriodicSafetyReportCompiler.compile_report(request, cases)
        report_id = payload.report_id
        now_dt = datetime.now(UTC).replace(tzinfo=None)

        start_dt = datetime.fromisoformat(request.reporting_period_start)
        end_dt = datetime.fromisoformat(request.reporting_period_end)

        record = PeriodicSafetyReportRecord(
            id=report_id,
            report_type=request.report_type.value,
            study_id=request.study_id,
            compound_name=request.compound_name,
            period_start=start_dt,
            period_end=end_dt,
            status=PeriodicReportStatus.DRAFT.value,
            total_subject_exposure=payload.total_subject_exposure,
            total_saes=payload.total_saes,
            report_payload=payload.model_dump(),
            created_at=now_dt,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        self.session.add(record)
        await self.session.flush()

        await write_audit_log(
            session=self.session,
            created_by=user_id,
            action="PERIODIC_SAFETY_REPORT_COMPILED",
            details=(
                f"Compiled {request.report_type.value} for study {request.study_id} ({request.compound_name}). "
                f"Total SAEs: {payload.total_saes}, Cumulative Exposure: {payload.total_subject_exposure}."
            ),
            record_id=report_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )

        return PeriodicReportResponseDTO(
            id=report_id,
            report_type=request.report_type.value,
            study_id=request.study_id,
            compound_name=request.compound_name,
            reporting_period_start=request.reporting_period_start,
            reporting_period_end=request.reporting_period_end,
            dibd_anniversary=payload.dibd_anniversary,
            status=PeriodicReportStatus.DRAFT.value,
            total_subject_exposure=payload.total_subject_exposure,
            total_saes=payload.total_saes,
            total_fatal_cases=payload.total_fatal_cases,
            total_susars=payload.total_susars,
            total_aesi=payload.total_aesi,
            snapshot_hash=payload.snapshot.snapshot_hash if payload.snapshot else None,
            line_listings_count=len(payload.line_listings),
            summary_tabulations_count=len(payload.summary_tabulations),
            signals_included_count=len(payload.signal_summary),
            payload=payload,
            created_at=now_dt.isoformat(),
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )

    async def sign_periodic_report(
        self,
        report_id: str,
        user_id: str,
        user_roles: list[str],
        reason_for_change: str,
        signature_secret: str,
        meaning_of_signature: str,
    ) -> PeriodicReportSignResponse:
        """Signs and locks a Periodic Regulatory Safety Report with 21 CFR Part 11 e-signature."""
        stmt = select(PeriodicSafetyReportRecord).where(
            PeriodicSafetyReportRecord.id == report_id
        )
        res = await self.session.execute(stmt)
        report = res.scalars().first()
        if not report:
            raise ValueError(f"PeriodicSafetyReportRecord '{report_id}' not found.")

        # Verify role authorization
        if user_roles:
            normalized_roles = {
                str(r)
                .lower()
                .replace(" ", "_")
                .replace("(", "")
                .replace(")", "")
                .replace("-", "_")
                for r in (
                    user_roles
                    if isinstance(user_roles, (list, set, tuple))
                    else [user_roles]
                )
            }
            is_authorized = bool(
                normalized_roles.intersection(ALLOWED_PHYSICIAN_SIGNING_ROLES)
                or any(
                    "qppv" in r
                    or "physician" in r
                    or "safety" in r
                    or "medical_monitor" in r
                    or "admin" in r
                    for r in normalized_roles
                )
            )
            if not is_authorized:
                raise ValueError(
                    f"User '{user_id}' with roles {user_roles} is not authorized to sign periodic safety reports."
                )

        report_payload = dict(report.report_payload or {})
        snapshot_data = report_payload.get("snapshot") or {}
        snapshot_hash = str(snapshot_data.get("snapshot_hash", ""))

        now_dt = datetime.now(UTC)
        from apps.safety.services.regulatory_audit_ledger import (
            RegulatoryAuditLedger,
        )

        qppv_manifest = RegulatoryAuditLedger.seal_qppv_signoff(
            compound_name=report.compound_name,
            meaning_of_signature=meaning_of_signature,
            report_id=report.id,
            report_payload=report_payload,
            report_type=report.report_type,
            signature_secret=signature_secret,
            snapshot_hash=snapshot_hash,
            study_id=report.study_id,
            user_id=user_id,
            user_roles=user_roles,
        )

        manifest_id = qppv_manifest.manifest_id
        report_payload["qppv_signoff"] = qppv_manifest.model_dump()
        report.report_payload = report_payload

        report.status = PeriodicReportStatus.APPROVED.value
        report.approved_by_user_id = user_id
        report.approved_at = now_dt.replace(tzinfo=None)
        report.esignature_manifest_id = manifest_id
        report.version_index += 1
        report.reason_for_change = reason_for_change

        await write_audit_log(
            session=self.session,
            created_by=user_id,
            action="PERIODIC_SAFETY_REPORT_SIGNED",
            details=f"21 CFR Part 11 e-signature applied on {report.report_type} {report_id} by {user_id}. Manifest: {manifest_id}.",
            record_id=report.id,
            reason_for_change=reason_for_change,
            version_index=report.version_index,
        )

        return PeriodicReportSignResponse(
            report_id=report.id,
            status=PeriodicReportStatus.APPROVED,
            approved_by_user_id=user_id,
            approved_at=now_dt.isoformat(),
            esignature_manifest_id=manifest_id,
            message="Periodic Safety Report successfully approved and signed.",
            qppv_manifest=qppv_manifest,
        )
