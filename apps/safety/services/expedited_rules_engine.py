"""Deterministic Multi-Country Expedited Safety Reporting Rules Engine.

Evaluates Individual Case Safety Reports (ICSRs) against FDA 7/15-day IND,
EMA EudraVigilance, PMDA, MHRA, and Health Canada statutory rules, computing
real-time Day 0 countdown clocks, threshold alerts (75% and 90%), and gating
regulatory approvals behind 21 CFR Part 11 Safety Physician electronic signatures.

Requirements: PRD-PVG-001, PRD-PVG-008, ADR-2199
"""

import logging
import uuid
from datetime import UTC, datetime, timedelta

from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.adapters.models import (
    ICSRReportingEvaluation,
    write_audit_log,
)
from apps.safety.domain.expedited_rules import (
    AuthorityRuleEvaluationResult,
    CausalityCategory,
    ClockAlertLevel,
    CountdownClockStatus,
    ExpectednessCategory,
    ExpeditedClassification,
    ICSRApprovalStatus,
    ICSRRuleEvaluationInput,
    ICSRRuleEvaluationSummary,
    ReportingAuthority,
    SignICSRRuleEvaluationResponse,
)
from packages.security.signing import generate_canonical_signature

logger = logging.getLogger("expedited-safety-rules")

ALLOWED_PHYSICIAN_SIGNING_ROLES = {
    "safety_physician",
    "sponsor_safety_physician",
    "sponsor_medical_monitor",
    "medical_monitor",
    "sponsor_pv_officer",
    "super_admin",
}

CAUSAL_CATEGORIES = {
    CausalityCategory.RELATED,
    CausalityCategory.SUSPECTED,
    CausalityCategory.POSSIBLY_RELATED,
    CausalityCategory.PROBABLY_RELATED,
    CausalityCategory.DEFINITELY_RELATED,
}


def _parse_datetime(dt_val: str | datetime) -> datetime:
    """Safely parse an ISO-8601 or CDISC DTC string into a datetime object."""
    if isinstance(dt_val, datetime):
        return dt_val
    cleaned = dt_val.strip()
    if cleaned.endswith("Z"):
        cleaned = cleaned[:-1] + "+00:00"
    try:
        return datetime.fromisoformat(cleaned)
    except ValueError:
        # Fallback for date-only formats like YYYY-MM-DD
        if len(cleaned) == 10:
            return datetime.strptime(cleaned, "%Y-%m-%d")
        raise ValueError(f"Unable to parse date string: '{dt_val}'")


class ExpeditedSafetyReportingRulesEngine:
    """
    Deterministic rules engine evaluating adverse events against multi-country
    expedited reporting mandates, maintaining real-time Day 0 countdown clocks,
    and enforcing 21 CFR Part 11 Safety Physician signature gating.
    """

    def compute_countdown(
        self,
        receipt_date: datetime | str,
        timeframe_days: int,
        as_of: datetime | None = None,
    ) -> CountdownClockStatus:
        """Compute real-time statutory countdown clock and threshold alerts.

        Args:
            receipt_date: Day 0 initial receipt timestamp.
            timeframe_days: Statutory duration in calendar days (e.g. 7 or 15).
            as_of: Evaluation reference timestamp (defaults to current UTC time).

        Returns:
            CountdownClockStatus with elapsed %, remaining hours, overdue flag, and alert level.
        """
        start_dt = _parse_datetime(receipt_date)
        due_dt = start_dt + timedelta(days=timeframe_days)

        current_time = as_of if as_of is not None else datetime.now(UTC)

        # Normalize timezone awareness for delta calculation
        start_for_math = (
            start_dt.replace(tzinfo=UTC) if start_dt.tzinfo is None else start_dt
        )
        due_for_math = due_dt.replace(tzinfo=UTC) if due_dt.tzinfo is None else due_dt
        curr_for_math = (
            current_time.replace(tzinfo=UTC)
            if current_time.tzinfo is None
            else current_time
        )

        total_hours = float(timeframe_days * 24.0)
        elapsed_seconds = (curr_for_math - start_for_math).total_seconds()
        elapsed_hours = max(0.0, round(elapsed_seconds / 3600.0, 2))

        elapsed_pct = (
            round((elapsed_hours / total_hours) * 100.0, 2)
            if total_hours > 0
            else 100.0
        )
        remaining_seconds = (due_for_math - curr_for_math).total_seconds()
        remaining_hours = max(0.0, round(remaining_seconds / 3600.0, 2))
        is_overdue = curr_for_math > due_for_math

        if is_overdue or elapsed_pct >= 100.0:
            alert_level = ClockAlertLevel.OVERDUE
        elif elapsed_pct >= 90.0:
            alert_level = ClockAlertLevel.CRITICAL
        elif elapsed_pct >= 75.0:
            alert_level = ClockAlertLevel.WARNING
        else:
            alert_level = ClockAlertLevel.NORMAL

        return CountdownClockStatus(
            clock_start_date=start_dt.isoformat(),
            clock_due_date=due_dt.isoformat(),
            total_hours=total_hours,
            elapsed_hours=elapsed_hours,
            elapsed_percentage=elapsed_pct,
            remaining_hours=remaining_hours,
            is_overdue=is_overdue,
            alert_level=alert_level,
        )

    def evaluate_authority(
        self,
        authority: ReportingAuthority,
        input_data: ICSRRuleEvaluationInput,
        as_of: datetime | None = None,
    ) -> AuthorityRuleEvaluationResult:
        """Evaluate deterministic expedited reporting rule for a specific regulatory authority.

        Args:
            authority: Target regulatory authority (FDA, EMA, PMDA, MHRA, Health Canada).
            input_data: Adverse event criteria and Day 0 metadata.
            as_of: Reference timestamp for countdown calculation.

        Returns:
            AuthorityRuleEvaluationResult detailing statutory mandate, due date, and clock.
        """
        is_fatal_or_lt = (
            input_data.seriousness_death or input_data.seriousness_life_threatening
        )
        is_other_serious = (
            input_data.seriousness_hospitalization
            or input_data.seriousness_disability
            or input_data.seriousness_congenital_anomaly
            or input_data.seriousness_other
        )
        is_serious = is_fatal_or_lt or is_other_serious
        is_unexpected = input_data.expectedness == ExpectednessCategory.UNEXPECTED
        is_causal = input_data.causality in CAUSAL_CATEGORIES
        is_susar = is_serious and is_unexpected and is_causal

        receipt_dt = _parse_datetime(input_data.receipt_date)

        if authority == ReportingAuthority.US_FDA:
            country_code = "US"
            if is_susar:
                if is_fatal_or_lt:
                    timeframe_days = 7
                    due_date_iso = (
                        receipt_dt + timedelta(days=timeframe_days)
                    ).isoformat()
                    countdown = self.compute_countdown(
                        receipt_dt, timeframe_days, as_of
                    )
                    return AuthorityRuleEvaluationResult(
                        authority=authority,
                        country_code=country_code,
                        is_expedited=True,
                        timeframe_days=timeframe_days,
                        due_date=due_date_iso,
                        rule_name="FDA 7-Day Fatal/Life-Threatening SUSAR",
                        statutory_citation="21 CFR 312.32(c)(1)(i)",
                        classification=ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY,
                        rationale="Fatal or life-threatening unexpected suspected adverse reaction requires expedited notification to FDA within 7 calendar days.",
                        countdown=countdown,
                    )
                timeframe_days = 15
                due_date_iso = (receipt_dt + timedelta(days=timeframe_days)).isoformat()
                countdown = self.compute_countdown(receipt_dt, timeframe_days, as_of)
                return AuthorityRuleEvaluationResult(
                    authority=authority,
                    country_code=country_code,
                    is_expedited=True,
                    timeframe_days=timeframe_days,
                    due_date=due_date_iso,
                    rule_name="FDA 15-Day Serious Unexpected SAR",
                    statutory_citation="21 CFR 312.32(c)(1)(ii)",
                    classification=ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY,
                    rationale="Serious unexpected suspected adverse reaction requires expedited IND safety report to FDA within 15 calendar days.",
                    countdown=countdown,
                )
            return AuthorityRuleEvaluationResult(
                authority=authority,
                country_code=country_code,
                is_expedited=False,
                timeframe_days=None,
                due_date=None,
                rule_name="FDA Periodic / Annual Safety Report",
                statutory_citation="21 CFR 312.33",
                classification=ExpeditedClassification.NON_EXPEDITED_PERIODIC,
                rationale="Adverse event does not meet expedited SUSAR criteria (expected, non-serious, or non-causal); reported in annual IND progress report.",
                countdown=None,
            )

        if authority == ReportingAuthority.EMA:
            country_code = "EU"
            if is_susar:
                if is_fatal_or_lt:
                    timeframe_days = 7
                    due_date_iso = (
                        receipt_dt + timedelta(days=timeframe_days)
                    ).isoformat()
                    countdown = self.compute_countdown(
                        receipt_dt, timeframe_days, as_of
                    )
                    return AuthorityRuleEvaluationResult(
                        authority=authority,
                        country_code=country_code,
                        is_expedited=True,
                        timeframe_days=timeframe_days,
                        due_date=due_date_iso,
                        rule_name="EMA 7-Day Fatal/Life-Threatening SUSAR (EudraVigilance)",
                        statutory_citation="Regulation (EU) No 536/2014 Art. 40(1)",
                        classification=ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY,
                        rationale="Fatal or life-threatening SUSAR requires direct electronic transmission to EudraVigilance within 7 calendar days.",
                        countdown=countdown,
                    )
                timeframe_days = 15
                due_date_iso = (receipt_dt + timedelta(days=timeframe_days)).isoformat()
                countdown = self.compute_countdown(receipt_dt, timeframe_days, as_of)
                return AuthorityRuleEvaluationResult(
                    authority=authority,
                    country_code=country_code,
                    is_expedited=True,
                    timeframe_days=timeframe_days,
                    due_date=due_date_iso,
                    rule_name="EMA 15-Day Other SUSAR (EudraVigilance)",
                    statutory_citation="Regulation (EU) No 536/2014 Art. 40(2)",
                    classification=ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY,
                    rationale="Non-fatal/non-life-threatening SUSAR requires electronic transmission to EudraVigilance within 15 calendar days.",
                    countdown=countdown,
                )
            return AuthorityRuleEvaluationResult(
                authority=authority,
                country_code=country_code,
                is_expedited=False,
                timeframe_days=None,
                due_date=None,
                rule_name="EMA Annual Safety Report (ASR / DSUR)",
                statutory_citation="Regulation (EU) No 536/2014 Art. 43",
                classification=ExpeditedClassification.NON_EXPEDITED_PERIODIC,
                rationale="Event does not meet SUSAR criteria; documented in annual safety report to Member States.",
                countdown=None,
            )

        if authority == ReportingAuthority.PMDA:
            country_code = "JP"
            if is_susar:
                if is_fatal_or_lt:
                    timeframe_days = 7
                    due_date_iso = (
                        receipt_dt + timedelta(days=timeframe_days)
                    ).isoformat()
                    countdown = self.compute_countdown(
                        receipt_dt, timeframe_days, as_of
                    )
                    return AuthorityRuleEvaluationResult(
                        authority=authority,
                        country_code=country_code,
                        is_expedited=True,
                        timeframe_days=timeframe_days,
                        due_date=due_date_iso,
                        rule_name="PMDA 7-Day Fatal/Life-Threatening Unexpected Adverse Reaction",
                        statutory_citation="PMD Act Art. 80-2 / MHLW Ordinance 28",
                        classification=ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY,
                        rationale="Fatal or life-threatening unexpected adverse reaction subject to 7-day expedited reporting to PMDA Japan.",
                        countdown=countdown,
                    )
                timeframe_days = 15
                due_date_iso = (receipt_dt + timedelta(days=timeframe_days)).isoformat()
                countdown = self.compute_countdown(receipt_dt, timeframe_days, as_of)
                return AuthorityRuleEvaluationResult(
                    authority=authority,
                    country_code=country_code,
                    is_expedited=True,
                    timeframe_days=timeframe_days,
                    due_date=due_date_iso,
                    rule_name="PMDA 15-Day Serious Unexpected Adverse Reaction",
                    statutory_citation="PMD Act Art. 80-2 / MHLW Ordinance 28",
                    classification=ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY,
                    rationale="Serious unexpected adverse reaction subject to 15-day expedited reporting to PMDA Japan.",
                    countdown=countdown,
                )
            return AuthorityRuleEvaluationResult(
                authority=authority,
                country_code=country_code,
                is_expedited=False,
                timeframe_days=None,
                due_date=None,
                rule_name="PMDA Periodic Safety Report",
                statutory_citation="MHLW Ordinance 28 Art. 43",
                classification=ExpeditedClassification.NON_EXPEDITED_PERIODIC,
                rationale="Event does not meet expedited criteria; tracked for periodic clinical safety reporting.",
                countdown=None,
            )

        if authority == ReportingAuthority.MHRA:
            country_code = "GB"
            if is_susar:
                if is_fatal_or_lt:
                    timeframe_days = 7
                    due_date_iso = (
                        receipt_dt + timedelta(days=timeframe_days)
                    ).isoformat()
                    countdown = self.compute_countdown(
                        receipt_dt, timeframe_days, as_of
                    )
                    return AuthorityRuleEvaluationResult(
                        authority=authority,
                        country_code=country_code,
                        is_expedited=True,
                        timeframe_days=timeframe_days,
                        due_date=due_date_iso,
                        rule_name="MHRA 7-Day Fatal/Life-Threatening SUSAR",
                        statutory_citation="SI 2004/1031 Regulation 33(1)",
                        classification=ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY,
                        rationale="Fatal or life-threatening SUSAR must be reported to the MHRA within 7 calendar days.",
                        countdown=countdown,
                    )
                timeframe_days = 15
                due_date_iso = (receipt_dt + timedelta(days=timeframe_days)).isoformat()
                countdown = self.compute_countdown(receipt_dt, timeframe_days, as_of)
                return AuthorityRuleEvaluationResult(
                    authority=authority,
                    country_code=country_code,
                    is_expedited=True,
                    timeframe_days=timeframe_days,
                    due_date=due_date_iso,
                    rule_name="MHRA 15-Day Serious Unexpected SUSAR",
                    statutory_citation="SI 2004/1031 Regulation 33(2)",
                    classification=ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY,
                    rationale="All other serious unexpected suspected adverse reactions must be reported to the MHRA within 15 calendar days.",
                    countdown=countdown,
                )
            return AuthorityRuleEvaluationResult(
                authority=authority,
                country_code=country_code,
                is_expedited=False,
                timeframe_days=None,
                due_date=None,
                rule_name="MHRA Development Safety Update Report (DSUR)",
                statutory_citation="SI 2004/1031 Regulation 35",
                classification=ExpeditedClassification.NON_EXPEDITED_PERIODIC,
                rationale="Non-SUSAR adverse event documented in the annual Development Safety Update Report.",
                countdown=None,
            )

        if authority == ReportingAuthority.HEALTH_CANADA:
            country_code = "CA"
            if is_susar:
                if is_fatal_or_lt:
                    timeframe_days = 7
                    due_date_iso = (
                        receipt_dt + timedelta(days=timeframe_days)
                    ).isoformat()
                    countdown = self.compute_countdown(
                        receipt_dt, timeframe_days, as_of
                    )
                    return AuthorityRuleEvaluationResult(
                        authority=authority,
                        country_code=country_code,
                        is_expedited=True,
                        timeframe_days=timeframe_days,
                        due_date=due_date_iso,
                        rule_name="Health Canada 7-Day Fatal/Life-Threatening ADR",
                        statutory_citation="Food and Drug Regulations C.05.014(1)(a)",
                        classification=ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY,
                        rationale="Fatal or life-threatening unexpected adverse drug reaction requires report to Health Canada within 7 calendar days.",
                        countdown=countdown,
                    )
                timeframe_days = 15
                due_date_iso = (receipt_dt + timedelta(days=timeframe_days)).isoformat()
                countdown = self.compute_countdown(receipt_dt, timeframe_days, as_of)
                return AuthorityRuleEvaluationResult(
                    authority=authority,
                    country_code=country_code,
                    is_expedited=True,
                    timeframe_days=timeframe_days,
                    due_date=due_date_iso,
                    rule_name="Health Canada 15-Day Serious Unexpected ADR",
                    statutory_citation="Food and Drug Regulations C.05.014(1)(b)",
                    classification=ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY,
                    rationale="Serious unexpected adverse drug reaction requires report to Health Canada within 15 calendar days.",
                    countdown=countdown,
                )
            return AuthorityRuleEvaluationResult(
                authority=authority,
                country_code=country_code,
                is_expedited=False,
                timeframe_days=None,
                due_date=None,
                rule_name="Health Canada Annual Safety Report",
                statutory_citation="Food and Drug Regulations C.05.014(3)",
                classification=ExpeditedClassification.NON_EXPEDITED_PERIODIC,
                rationale="Non-expedited safety event included in annual clinical trial report.",
                countdown=None,
            )

        raise ValueError(f"Unsupported regulatory reporting authority: {authority}")

    def evaluate(
        self,
        input_data: ICSRRuleEvaluationInput,
        as_of: datetime | None = None,
    ) -> list[AuthorityRuleEvaluationResult]:
        """Evaluate input across specified target authorities or all supported authorities."""
        target_authorities = input_data.target_authorities or [
            ReportingAuthority.US_FDA,
            ReportingAuthority.EMA,
            ReportingAuthority.PMDA,
            ReportingAuthority.MHRA,
            ReportingAuthority.HEALTH_CANADA,
        ]
        return [
            self.evaluate_authority(auth, input_data, as_of=as_of)
            for auth in target_authorities
        ]

    async def evaluate_and_persist(
        self,
        session: AsyncSession,
        input_data: ICSRRuleEvaluationInput,
        user_id: str,
        reason_for_change: str,
        as_of: datetime | None = None,
    ) -> ICSRRuleEvaluationSummary:
        """Evaluate adverse event against global rules, persist record, and emit GxP audit log."""
        results = self.evaluate(input_data, as_of=as_of)

        is_fatal_or_lt = (
            input_data.seriousness_death or input_data.seriousness_life_threatening
        )
        is_other_serious = (
            input_data.seriousness_hospitalization
            or input_data.seriousness_disability
            or input_data.seriousness_congenital_anomaly
            or input_data.seriousness_other
        )
        is_serious = is_fatal_or_lt or is_other_serious
        is_unexpected = input_data.expectedness == ExpectednessCategory.UNEXPECTED
        is_causal = input_data.causality in CAUSAL_CATEGORIES
        is_susar = is_serious and is_unexpected and is_causal

        any_expedited = any(r.is_expedited for r in results)
        expedited_days = [
            r.timeframe_days for r in results if r.timeframe_days is not None
        ]
        shortest_days = min(expedited_days) if expedited_days else None

        # Determine overall alert level
        alert_levels = [
            r.countdown.alert_level for r in results if r.countdown is not None
        ]
        if ClockAlertLevel.OVERDUE in alert_levels:
            overall_alert = ClockAlertLevel.OVERDUE
        elif ClockAlertLevel.CRITICAL in alert_levels:
            overall_alert = ClockAlertLevel.CRITICAL
        elif ClockAlertLevel.WARNING in alert_levels:
            overall_alert = ClockAlertLevel.WARNING
        else:
            overall_alert = ClockAlertLevel.NORMAL

        eval_id = str(uuid.uuid4())
        receipt_dt = _parse_datetime(input_data.receipt_date)

        # Build ORM model
        eval_orm = ICSRReportingEvaluation(
            id=eval_id,
            case_id=input_data.case_id,
            study_id=input_data.study_id,
            subject_id=input_data.subject_id,
            receipt_date=receipt_dt,
            seriousness_death=input_data.seriousness_death,
            seriousness_life_threatening=input_data.seriousness_life_threatening,
            seriousness_hospitalization=input_data.seriousness_hospitalization,
            seriousness_disability=input_data.seriousness_disability,
            seriousness_congenital_anomaly=input_data.seriousness_congenital_anomaly,
            seriousness_other=input_data.seriousness_other,
            causality=input_data.causality.value,
            expectedness=input_data.expectedness.value,
            is_serious=is_serious,
            is_unexpected=is_unexpected,
            is_causal=is_causal,
            is_susar=is_susar,
            any_expedited=any_expedited,
            shortest_timeframe_days=shortest_days,
            evaluations=[r.model_dump() for r in results],
            approval_status=ICSRApprovalStatus.DRAFT.value,
            created_by=user_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )
        session.add(eval_orm)
        await session.flush()

        # Emit audit log
        await write_audit_log(
            session=session,
            created_by=user_id,
            action="EXPEDITED_SAFETY_RULES_EVALUATED",
            details=(
                f"Evaluated ICSR {input_data.case_id} (study: {input_data.study_id}, subject: {input_data.subject_id}). "
                f"SUSAR: {is_susar}, Any Expedited: {any_expedited}, Shortest: {shortest_days}d, Alert: {overall_alert.value}."
            ),
            record_id=eval_id,
            reason_for_change=reason_for_change,
            version_index=1,
        )

        return self._map_orm_to_dto(eval_orm, as_of=as_of)

    async def get_evaluation(
        self,
        session: AsyncSession,
        evaluation_id: str,
        user_id: str,
        reason_for_change: str | None = None,
        as_of: datetime | None = None,
    ) -> ICSRRuleEvaluationSummary:
        """Retrieve evaluation and recalculate real-time countdown clocks against current time."""
        stmt = select(ICSRReportingEvaluation).where(
            ICSRReportingEvaluation.id == evaluation_id
        )
        res = await session.execute(stmt)
        record = res.scalars().first()
        if not record:
            raise HTTPException(
                status_code=404,
                detail=f"Safety reporting evaluation '{evaluation_id}' not found.",
            )

        await write_audit_log(
            session=session,
            created_by=user_id,
            action="EXPEDITED_SAFETY_EVALUATION_VIEW",
            details=f"Viewed expedited safety evaluation {evaluation_id}.",
            record_id=evaluation_id,
            reason_for_change=reason_for_change or "View evaluation",
            version_index=record.version_index,
        )

        return self._map_orm_to_dto(record, as_of=as_of)

    async def list_evaluations(
        self,
        session: AsyncSession,
        study_id: str | None = None,
        case_id: str | None = None,
        subject_id: str | None = None,
        approval_status: str | None = None,
        any_expedited: bool | None = None,
        as_of: datetime | None = None,
    ) -> list[ICSRRuleEvaluationSummary]:
        """List evaluations matching filter criteria with recalculated countdown clocks."""
        stmt = select(ICSRReportingEvaluation)
        if study_id:
            stmt = stmt.where(ICSRReportingEvaluation.study_id == study_id)
        if case_id:
            stmt = stmt.where(ICSRReportingEvaluation.case_id == case_id)
        if subject_id:
            stmt = stmt.where(ICSRReportingEvaluation.subject_id == subject_id)
        if approval_status:
            stmt = stmt.where(
                ICSRReportingEvaluation.approval_status == approval_status
            )
        if any_expedited is not None:
            stmt = stmt.where(ICSRReportingEvaluation.any_expedited.is_(any_expedited))

        stmt = stmt.order_by(ICSRReportingEvaluation.created_at.desc())
        res = await session.execute(stmt)
        records = res.scalars().all()
        return [self._map_orm_to_dto(r, as_of=as_of) for r in records]

    async def sign_evaluation(
        self,
        session: AsyncSession,
        evaluation_id: str,
        user_id: str,
        user_roles: str,
        reason_for_change: str,
        signature_secret: str | None = None,
        meaning_of_signature: str | None = None,
    ) -> SignICSRRuleEvaluationResponse:
        """Applies a 21 CFR Part 11 cryptographic electronic signature to finalize/approve an ICSR package.

        Requirements: PRD-PVG-008
        """
        roles_set = {r.strip().lower() for r in user_roles.split(",") if r.strip()}
        if not roles_set.intersection(ALLOWED_PHYSICIAN_SIGNING_ROLES):
            raise HTTPException(
                status_code=403,
                detail=(
                    f"Forbidden: User '{user_id}' with roles '{user_roles}' is not authorized "
                    "to execute 21 CFR Part 11 Safety Physician electronic signatures on ICSR packages."
                ),
            )

        if not reason_for_change or not reason_for_change.strip():
            raise HTTPException(
                status_code=400,
                detail="Reason for change is mandatory for 21 CFR Part 11 electronic signatures.",
            )

        stmt = select(ICSRReportingEvaluation).where(
            ICSRReportingEvaluation.id == evaluation_id
        )
        res = await session.execute(stmt)
        record = res.scalars().first()
        if not record:
            raise HTTPException(
                status_code=404,
                detail=f"Safety reporting evaluation '{evaluation_id}' not found.",
            )

        if record.approval_status == ICSRApprovalStatus.PHYSICIAN_APPROVED.value:
            raise HTTPException(
                status_code=400,
                detail=f"Safety reporting evaluation '{evaluation_id}' has already been approved by a Safety Physician.",
            )

        now_dt = datetime.now(UTC)
        signing_secret = signature_secret or "safety-physician-signing-secret"
        payload_to_sign = {
            "evaluation_id": record.id,
            "case_id": record.case_id,
            "study_id": record.study_id,
            "subject_id": record.subject_id,
            "receipt_date": record.receipt_date.isoformat(),
            "is_susar": record.is_susar,
            "any_expedited": record.any_expedited,
            "signer": user_id,
            "roles": user_roles,
            "timestamp": now_dt.isoformat(),
            "meaning_of_signature": meaning_of_signature
            or "Approval of Expedited ICSR Regulatory Submission and Causality Assessment",
            "reason_for_change": reason_for_change,
        }
        manifest_id = f"sig_eval_{uuid.uuid4().hex[:12]}"
        _ = generate_canonical_signature(
            payload_to_sign, signing_secret.encode("utf-8")
        )

        record.approval_status = ICSRApprovalStatus.PHYSICIAN_APPROVED.value
        record.approved_by_user_id = user_id
        record.approved_at = now_dt.replace(tzinfo=None)
        record.esignature_manifest_id = manifest_id
        record.version_index += 1
        record.reason_for_change = reason_for_change

        await session.flush()

        await write_audit_log(
            session=session,
            created_by=user_id,
            action="EXPEDITED_SAFETY_EVALUATION_SIGNED",
            details=(
                f"21 CFR Part 11 Safety Physician e-signature applied by {user_id} ({user_roles}). "
                f"Manifest: {manifest_id}. Status: PHYSICIAN_APPROVED."
            ),
            record_id=record.id,
            reason_for_change=reason_for_change,
            version_index=record.version_index,
        )

        return SignICSRRuleEvaluationResponse(
            evaluation_id=record.id,
            approval_status=ICSRApprovalStatus.PHYSICIAN_APPROVED,
            approved_by_user_id=user_id,
            approved_at=now_dt.isoformat(),
            esignature_manifest_id=manifest_id,
            message="ICSR reporting evaluation successfully approved with 21 CFR Part 11 Safety Physician signature.",
        )

    def _map_orm_to_dto(
        self,
        orm: ICSRReportingEvaluation,
        as_of: datetime | None = None,
    ) -> ICSRRuleEvaluationSummary:
        """Map ORM entity to DTO, recalculating live countdown clocks for each expedited authority."""
        live_evaluations: list[AuthorityRuleEvaluationResult] = []
        raw_evaluations = orm.evaluations or []

        for item in raw_evaluations:
            auth_val = ReportingAuthority(item["authority"])
            country_code = item["country_code"]
            is_expedited = item["is_expedited"]
            timeframe_days = item.get("timeframe_days")
            due_date = item.get("due_date")
            rule_name = item["rule_name"]
            statutory_citation = item["statutory_citation"]
            classification = ExpeditedClassification(item["classification"])
            rationale = item["rationale"]

            countdown = None
            if is_expedited and timeframe_days is not None:
                countdown = self.compute_countdown(
                    orm.receipt_date, timeframe_days, as_of=as_of
                )

            live_evaluations.append(
                AuthorityRuleEvaluationResult(
                    authority=auth_val,
                    country_code=country_code,
                    is_expedited=is_expedited,
                    timeframe_days=timeframe_days,
                    due_date=due_date,
                    rule_name=rule_name,
                    statutory_citation=statutory_citation,
                    classification=classification,
                    rationale=rationale,
                    countdown=countdown,
                )
            )

        alert_levels = [
            ev.countdown.alert_level
            for ev in live_evaluations
            if ev.countdown is not None
        ]
        if ClockAlertLevel.OVERDUE in alert_levels:
            overall_alert = ClockAlertLevel.OVERDUE
        elif ClockAlertLevel.CRITICAL in alert_levels:
            overall_alert = ClockAlertLevel.CRITICAL
        elif ClockAlertLevel.WARNING in alert_levels:
            overall_alert = ClockAlertLevel.WARNING
        else:
            overall_alert = ClockAlertLevel.NORMAL

        return ICSRRuleEvaluationSummary(
            id=orm.id,
            case_id=orm.case_id,
            study_id=orm.study_id,
            subject_id=orm.subject_id,
            receipt_date=orm.receipt_date.isoformat(),
            is_serious=orm.is_serious,
            is_unexpected=orm.is_unexpected,
            is_causal=orm.is_causal,
            is_susar=orm.is_susar,
            any_expedited=orm.any_expedited,
            shortest_timeframe_days=orm.shortest_timeframe_days,
            overall_alert_level=overall_alert,
            evaluations=live_evaluations,
            approval_status=ICSRApprovalStatus(orm.approval_status),
            approved_by_user_id=orm.approved_by_user_id,
            approved_at=orm.approved_at.isoformat() if orm.approved_at else None,
            esignature_manifest_id=orm.esignature_manifest_id,
            created_at=orm.created_at.isoformat(),
            created_by=orm.created_by,
            reason_for_change=orm.reason_for_change,
            version_index=orm.version_index,
        )
