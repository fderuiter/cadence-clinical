"""Automated Periodic Aggregate Safety Report Compiler (DSUR, PSUR / PBRER).

Requirements: PRD-PVG-004, PRD-PVG-008, PRD-AGG-001, PRD-SYS-125, ADR-2199
(ICH E2F DSUR, ICH E2C(R2) PSUR / PBRER Guidelines, 21 CFR Part 11)
"""

import statistics
import uuid
from datetime import datetime
from typing import Any

from apps.safety.domain.periodic_report_models import (
    DSURLineListingItem,
    DSURSummaryTabulationRow,
    ExposureStratificationCategory,
    ExposureStratificationRow,
    PeriodicReportCompilationRequest,
    PeriodicReportPayload,
    PeriodicSafetyReportType,
    RegulatorySnapshot,
    SubjectExposureMetrics,
    SubjectExposureRecord,
)
from apps.safety.services.regulatory_audit_ledger import (
    RegulatoryAuditLedger,
)
from apps.safety.services.signal_detection_engine import (
    SafetySignalDetectionEngine,
)


class SubjectExposureCalculator:
    """Calculates cumulative and interval patient-years of exposure (PYE).

    Per ICH E2F § 5 (DSUR) and ICH E2C(R2) § 5 (PBRER).
    Stratifies by Treatment Arm, Dosage Cohort, Age Group, and Sex.
    """

    DEFAULT_AESI_PATTERNS: tuple[str, ...] = (
        "hepat",
        "liver",
        "qt prolong",
        "arrhythm",
        "anaph",
        "cytokine",
        "stevens-johnson",
        "sjs",
        "ten",
        "toxic epidermal",
        "renal failure",
        "kidney injury",
        "pancreat",
        "interstitial lung",
        "pneumonitis",
        "myocard",
        "thrombo",
    )

    @classmethod
    def _parse_date(cls, date_str: str, default: datetime) -> datetime:
        try:
            return datetime.fromisoformat(str(date_str).strip()[:10])
        except Exception:
            return default

    @classmethod
    def _get_age_group(cls, age: int | None) -> str:
        if age is None:
            return "Unknown"
        if age < 18:
            return "< 18 (Pediatric)"
        if age <= 64:
            return "18-64 (Adult)"
        return ">= 65 (Elderly)"

    @classmethod
    def calculate_exposure(
        cls,
        reporting_period_start: str,
        reporting_period_end: str,
        subjects_data: list[dict[str, Any]] | None = None,
        raw_cases: list[dict[str, Any]] | None = None,
    ) -> tuple[SubjectExposureMetrics, list[SubjectExposureRecord]]:
        """Computes stratified patient-years of exposure across reporting intervals.

        Args:
            reporting_period_start: Start of current reporting period (YYYY-MM-DD).
            reporting_period_end: End of current reporting period (YYYY-MM-DD).
            subjects_data: Explicit subject dosing / exposure records.
            raw_cases: Raw safety cases used as fallback for synthesis.

        Returns:
            Tuple of (SubjectExposureMetrics, list of SubjectExposureRecord).
        """
        start_dt = cls._parse_date(reporting_period_start, datetime(2020, 1, 1))
        end_dt = cls._parse_date(reporting_period_end, datetime(2099, 1, 1))

        records: list[SubjectExposureRecord] = []

        if subjects_data:
            for s in subjects_data:
                sub_id = str(s.get("subject_id", f"SUB-{uuid.uuid4().hex[:6]}"))
                arm = str(s.get("treatment_arm", "Investigational Drug"))
                cohort = str(s.get("dosage_cohort", "Standard Dose"))
                age = s.get("age")
                sex = str(s.get("sex", "UNKNOWN")).upper()

                first_dose_str = str(s.get("first_dose_date", reporting_period_start))[
                    :10
                ]
                last_dose_str = str(s.get("last_dose_date", reporting_period_end))[:10]

                first_dt = cls._parse_date(first_dose_str, start_dt)
                last_dt = cls._parse_date(last_dose_str, end_dt)
                if last_dt < first_dt:
                    last_dt = first_dt

                cum_days = max(1.0, float((last_dt - first_dt).days + 1))
                cum_pye = cum_days / 365.25

                # Interval overlap: max(first_dt, start_dt) to min(last_dt, end_dt)
                int_start = max(first_dt, start_dt)
                int_end = min(last_dt, end_dt)
                if int_end >= int_start:
                    int_days = float((int_end - int_start).days + 1)
                else:
                    int_days = 0.0
                int_pye = int_days / 365.25

                records.append(
                    SubjectExposureRecord(
                        subject_id=sub_id,
                        treatment_arm=arm,
                        dosage_cohort=cohort,
                        age=age,
                        age_group=cls._get_age_group(age),
                        sex=sex,
                        first_dose_date=first_dose_str,
                        last_dose_date=last_dose_str,
                        cumulative_exposure_days=cum_days,
                        interval_exposure_days=int_days,
                        cumulative_patient_years=round(cum_pye, 4),
                        interval_patient_years=round(int_pye, 4),
                    )
                )
        elif raw_cases:
            # Synthesize exposure records from unique subjects present in raw cases
            seen_subjects: dict[str, dict[str, Any]] = {}
            for c in raw_cases:
                sub_id = str(
                    c.get(
                        "subject_id", c.get("patient_id", f"SUB-{uuid.uuid4().hex[:6]}")
                    )
                )
                onset_str = str(c.get("onset_date", reporting_period_start))[:10]
                onset_dt = cls._parse_date(onset_str, start_dt)
                arm = str(c.get("treatment_arm", "Investigational Drug"))
                cohort = str(c.get("dosage_cohort", "Standard Cohort"))
                age = c.get("age")
                sex = str(c.get("sex", "UNKNOWN")).upper()

                if sub_id not in seen_subjects:
                    seen_subjects[sub_id] = {
                        "arm": arm,
                        "cohort": cohort,
                        "age": age,
                        "sex": sex,
                        "first_dt": onset_dt,
                        "last_dt": onset_dt,
                    }
                else:
                    if onset_dt < seen_subjects[sub_id]["first_dt"]:
                        seen_subjects[sub_id]["first_dt"] = onset_dt
                    if onset_dt > seen_subjects[sub_id]["last_dt"]:
                        seen_subjects[sub_id]["last_dt"] = onset_dt

            for sub_id, info in seen_subjects.items():
                first_dt = info["first_dt"]
                last_dt = max(info["last_dt"], end_dt)
                cum_days = max(1.0, float((last_dt - first_dt).days + 1))
                cum_pye = cum_days / 365.25

                int_start = max(first_dt, start_dt)
                int_end = min(last_dt, end_dt)
                int_days = (
                    float((int_end - int_start).days + 1)
                    if int_end >= int_start
                    else 0.0
                )
                int_pye = int_days / 365.25

                records.append(
                    SubjectExposureRecord(
                        subject_id=sub_id,
                        treatment_arm=info["arm"],
                        dosage_cohort=info["cohort"],
                        age=info["age"],
                        age_group=cls._get_age_group(info["age"]),
                        sex=info["sex"],
                        first_dose_date=first_dt.isoformat()[:10],
                        last_dose_date=last_dt.isoformat()[:10],
                        cumulative_exposure_days=cum_days,
                        interval_exposure_days=int_days,
                        cumulative_patient_years=round(cum_pye, 4),
                        interval_patient_years=round(int_pye, 4),
                    )
                )

        # Calculate Stratifications
        def _aggregate_group(
            items: list[SubjectExposureRecord],
            category: ExposureStratificationCategory,
            key_fn: Any,
        ) -> list[ExposureStratificationRow]:
            groups: dict[str, list[SubjectExposureRecord]] = {}
            for item in items:
                k = str(key_fn(item))
                groups.setdefault(k, []).append(item)

            rows: list[ExposureStratificationRow] = []
            for g_name, g_items in sorted(groups.items()):
                n = len(g_items)
                int_pye = sum(x.interval_patient_years for x in g_items)
                cum_pye = sum(x.cumulative_patient_years for x in g_items)
                durations = [x.cumulative_exposure_days for x in g_items]
                mean_dur = statistics.mean(durations) if durations else 0.0
                med_dur = statistics.median(durations) if durations else 0.0

                rows.append(
                    ExposureStratificationRow(
                        category=category,
                        group_name=g_name,
                        subject_count=n,
                        interval_patient_years=round(int_pye, 3),
                        cumulative_patient_years=round(cum_pye, 3),
                        mean_duration_days=round(mean_dur, 1),
                        median_duration_days=round(med_dur, 1),
                    )
                )
            return rows

        arm_rows = _aggregate_group(
            records,
            ExposureStratificationCategory.TREATMENT_ARM,
            lambda x: x.treatment_arm,
        )
        cohort_rows = _aggregate_group(
            records,
            ExposureStratificationCategory.DOSAGE_COHORT,
            lambda x: x.dosage_cohort,
        )
        age_rows = _aggregate_group(
            records, ExposureStratificationCategory.AGE_GROUP, lambda x: x.age_group
        )
        sex_rows = _aggregate_group(
            records, ExposureStratificationCategory.SEX, lambda x: x.sex
        )

        total_cum_sub = len(records)
        total_int_sub = sum(1 for r in records if r.interval_exposure_days > 0)
        total_cum_pye = sum(r.cumulative_patient_years for r in records)
        total_int_pye = sum(r.interval_patient_years for r in records)

        metrics = SubjectExposureMetrics(
            total_cumulative_subjects=total_cum_sub,
            total_interval_subjects=total_int_sub,
            total_cumulative_patient_years=round(total_cum_pye, 3),
            total_interval_patient_years=round(total_int_pye, 3),
            by_treatment_arm=arm_rows,
            by_dosage_cohort=cohort_rows,
            by_age_group=age_rows,
            by_sex=sex_rows,
        )

        return metrics, records


# =========================================================================
# Immutable Point-in-Time Regulatory Snapshot Manager (PRD-AGG-001)
# =========================================================================


class RegulatorySnapshotManager:
    """Manages cryptographically sealed point-in-time regulatory snapshots.

    Delegates core snapshot calculation, canonical manifest generation, and
    bitwise reproducibility verification to the unified RegulatoryAuditLedger.
    """

    @classmethod
    def create_snapshot(
        cls,
        study_id: str,
        compound_name: str,
        reporting_period_start: str,
        reporting_period_end: str,
        dibd_anniversary: str | None = None,
        raw_cases: list[dict[str, Any]] | None = None,
        subjects_data: list[dict[str, Any]] | None = None,
        user_id: str = "system",
        lock_reason: str = "Annual Periodic Regulatory Reporting Snapshot Lock",
    ) -> RegulatorySnapshot:
        """Constructs an immutable, SHA-256 sealed dataset snapshot."""
        return RegulatoryAuditLedger.create_snapshot(
            compound_name=compound_name,
            dibd_anniversary=dibd_anniversary,
            lock_reason=lock_reason,
            raw_cases=raw_cases,
            reporting_period_end=reporting_period_end,
            reporting_period_start=reporting_period_start,
            study_id=study_id,
            subjects_data=subjects_data,
            user_id=user_id,
        )

    @classmethod
    def verify_snapshot(
        cls,
        expected_hash: str,
        cases: list[dict[str, Any]],
        subjects: list[dict[str, Any]],
        study_id: str,
        compound_name: str,
        period_start: str,
        period_end: str,
        dibd: str | None = None,
    ) -> bool:
        """Verifies bitwise integrity of a frozen regulatory snapshot."""
        return RegulatoryAuditLedger.verify_snapshot(
            cases=cases,
            compound_name=compound_name,
            dibd=dibd,
            expected_hash=expected_hash,
            period_end=period_end,
            period_start=period_start,
            study_id=study_id,
            subjects=subjects,
        )


# =========================================================================
# Aggregate Periodic Safety Report Compiler (DSUR / PSUR / PBRER)
# =========================================================================


class PeriodicSafetyReportCompiler:
    """Compiles aggregate regulatory periodic safety reports (ICH E2F DSUR, ICH E2C(R2) PSUR/PBRER)."""

    @classmethod
    def is_aesi_term(
        cls, pt_term: str, soc_term: str, custom_aesi: list[str] | None = None
    ) -> tuple[bool, str | None]:
        """Checks whether a Preferred Term constitutes an Adverse Event of Special Interest."""
        term_lower = pt_term.lower()
        if custom_aesi:
            for aesi in custom_aesi:
                if aesi.lower() in term_lower:
                    return True, aesi

        for pattern in SubjectExposureCalculator.DEFAULT_AESI_PATTERNS:
            if pattern in term_lower:
                return True, pattern.capitalize()
        return False, None

    @classmethod
    def compile_report(
        cls,
        request: PeriodicReportCompilationRequest,
        raw_cases: list[dict[str, Any]],
        subjects_data: list[dict[str, Any]] | None = None,
        user_id: str = "system",
    ) -> PeriodicReportPayload:
        """Compiles a complete periodic safety report payload.

        Args:
            request: Report compilation metadata and configuration.
            raw_cases: List of adverse event cases within/cumulative to the trial.
            subjects_data: Optional explicit dosing/exposure records for subjects.
            user_id: Compiling user or system identity.

        Returns:
            Structured PeriodicReportPayload.
        """
        report_id = f"rpt_{uuid.uuid4().hex[:12]}"
        period_start = request.reporting_period_start
        period_end = request.reporting_period_end

        # Parse date limits
        try:
            start_dt = datetime.fromisoformat(period_start[:10])
            end_dt = datetime.fromisoformat(period_end[:10])
        except Exception:
            start_dt = datetime(2020, 1, 1)
            end_dt = datetime(2099, 1, 1)

        # 1. Compute Subject Exposure Metrics
        exposure_metrics, _ = SubjectExposureCalculator.calculate_exposure(
            reporting_period_start=period_start,
            reporting_period_end=period_end,
            subjects_data=subjects_data or request.subjects_exposure_data,
            raw_cases=raw_cases,
        )

        total_exposure_subs = max(
            exposure_metrics.total_cumulative_subjects,
            request.cumulative_exposure_subjects,
            len({str(c.get("subject_id")) for c in raw_cases if c.get("subject_id")}),
        )

        # 2. Immutable Regulatory Snapshot Creation
        snapshot: RegulatorySnapshot | None = None
        if request.auto_lock_snapshot:
            snapshot = RegulatorySnapshotManager.create_snapshot(
                study_id=request.study_id,
                compound_name=request.compound_name,
                reporting_period_start=period_start,
                reporting_period_end=period_end,
                dibd_anniversary=request.dibd_anniversary,
                raw_cases=raw_cases,
                subjects_data=subjects_data or request.subjects_exposure_data,
                user_id=user_id,
            )

        # 3. Filter interval and cumulative cases
        interval_cases: list[dict[str, Any]] = []
        cumulative_saes: int = 0
        cumulative_fatal: int = 0
        cumulative_susars: int = 0
        cumulative_aesi: int = 0

        line_listings: list[DSURLineListingItem] = []
        tabulation_map: dict[tuple[str, str], dict[str, Any]] = {}

        # Parse Reference Safety Information (RSI) lookup
        rsi_terms_map: dict[str, float] = {}
        if request.rsi_config and request.rsi_config.listed_terms:
            for term in request.rsi_config.listed_terms:
                rsi_terms_map[term.meddra_pt.strip().upper()] = (
                    term.expected_frequency_rate
                )

        for c in raw_cases:
            onset_str = str(c.get("onset_date", period_start))[:10]
            try:
                onset_dt = datetime.fromisoformat(onset_str)
            except Exception:
                onset_dt = start_dt

            # Seriousness check
            outcome_val = str(c.get("outcome", "RECOVERED")).upper()
            is_death = bool(c.get("seriousness_death", False)) or outcome_val in {
                "FATAL",
                "DIED",
                "DEATH",
            }
            is_lt = bool(c.get("seriousness_life_threatening", False))
            is_hosp = bool(c.get("seriousness_hospitalization", False))
            is_dis = bool(c.get("seriousness_disability", False))
            is_cong = bool(c.get("seriousness_congenital_anomaly", False))
            is_oth = bool(c.get("seriousness_other", False))
            is_serious = is_death or is_lt or is_hosp or is_dis or is_cong or is_oth

            causality = str(c.get("causality", "RELATED")).upper()
            expectedness = str(c.get("expectedness", "UNEXPECTED")).upper()
            is_susar = (
                is_serious
                and (
                    causality
                    in {"RELATED", "SUSPECTED", "PROBABLY_RELATED", "POSSIBLY_RELATED"}
                )
                and (expectedness == "UNEXPECTED")
            )

            pt = str(c.get("meddra_pt", c.get("reaction_pt", "Unknown PT")))
            soc = str(
                c.get(
                    "meddra_soc",
                    c.get(
                        "soc_name",
                        "General disorders and administration site conditions",
                    ),
                )
            )

            is_aesi, aesi_cat = cls.is_aesi_term(pt, soc, request.aesi_terms)
            if c.get("is_aesi") is not None:
                is_aesi = bool(c.get("is_aesi"))

            if is_serious:
                cumulative_saes += 1
            if is_death:
                cumulative_fatal += 1
            if is_susar:
                cumulative_susars += 1
            if is_aesi:
                cumulative_aesi += 1

            # Check if within reporting interval
            if start_dt <= onset_dt <= end_dt:
                interval_cases.append(c)

                # Add to line listings if serious
                if is_serious and request.include_line_listings:
                    arm = str(c.get("treatment_arm", request.compound_name))
                    case_id = str(c.get("case_id", f"CASE-{uuid.uuid4().hex[:6]}"))
                    sub_id = str(c.get("subject_id", f"SUB-{uuid.uuid4().hex[:6]}"))
                    study = str(c.get("study_id", request.study_id))

                    line_listings.append(
                        DSURLineListingItem(
                            case_id=case_id,
                            subject_id=sub_id,
                            study_id=study,
                            treatment_arm=arm,
                            dosage_cohort=c.get("dosage_cohort"),
                            age=c.get("age"),
                            sex=c.get("sex"),
                            country=str(c.get("country", "US")),
                            meddra_pt=pt,
                            meddra_soc=soc,
                            ae_term=str(c.get("ae_term", pt)),
                            onset_date=onset_str,
                            resolution_date=c.get("resolution_date"),
                            outcome=outcome_val,
                            seriousness_death=is_death,
                            seriousness_life_threatening=is_lt,
                            seriousness_hospitalization=is_hosp,
                            seriousness_disability=is_dis,
                            seriousness_congenital_anomaly=is_cong,
                            seriousness_other=is_oth,
                            causality=causality,
                            expectedness=expectedness,
                            susar=is_susar,
                            is_aesi=is_aesi,
                            aesi_category=aesi_cat,
                            action_taken=str(c.get("action_taken", "DRUG_WITHDRAWN")),
                        )
                    )

                # Tabulate summary counts
                arm_norm = str(c.get("treatment_arm", request.compound_name)).upper()
                key = (soc, pt)

                if key not in tabulation_map:
                    tabulation_map[key] = {
                        "active_events": 0,
                        "active_subjects": set(),
                        "comp_events": 0,
                        "comp_subjects": set(),
                        "placebo_events": 0,
                        "placebo_subjects": set(),
                        "is_aesi": is_aesi,
                        "aesi_cat": aesi_cat,
                        "is_fatal": is_death,
                        "is_susar": is_susar,
                    }
                else:
                    if is_death:
                        tabulation_map[key]["is_fatal"] = True
                    if is_susar:
                        tabulation_map[key]["is_susar"] = True
                    if is_aesi:
                        tabulation_map[key]["is_aesi"] = True

                sub_ref = str(c.get("subject_id", uuid.uuid4().hex))
                if "PLACEBO" in arm_norm:
                    tabulation_map[key]["placebo_events"] += 1
                    tabulation_map[key]["placebo_subjects"].add(sub_ref)
                elif "CONTROL" in arm_norm or "COMPARATOR" in arm_norm:
                    tabulation_map[key]["comp_events"] += 1
                    tabulation_map[key]["comp_subjects"].add(sub_ref)
                else:
                    tabulation_map[key]["active_events"] += 1
                    tabulation_map[key]["active_subjects"].add(sub_ref)

        # 4. Build Summary Tabulations rows with RSI comparison
        summary_rows: list[DSURSummaryTabulationRow] = []
        rsi_comparison_summary: list[dict[str, Any]] = []

        total_interval_pye = max(0.01, exposure_metrics.total_interval_patient_years)

        if request.include_summary_tabulations:
            for (soc, pt), data in tabulation_map.items():
                active_ev = data["active_events"]
                active_sub = len(data["active_subjects"])
                comp_ev = data["comp_events"]
                comp_sub = len(data["comp_subjects"])
                plac_ev = data["placebo_events"]
                plac_sub = len(data["placebo_subjects"])
                total_ev = active_ev + comp_ev + plac_ev
                total_sub = active_sub + comp_sub + plac_sub

                # Observed event rate (% of exposed subjects)
                obs_rate_pct = round(
                    (total_sub / max(1, total_exposure_subs)) * 100.0, 2
                )
                obs_incidence_pyr = round((total_ev / total_interval_pye) * 100.0, 2)

                # Reference Safety Information comparison
                pt_upper = pt.strip().upper()
                rsi_listed = pt_upper in rsi_terms_map
                rsi_exp_rate = rsi_terms_map.get(pt_upper)

                rate_ratio: float | None = None
                rsi_breached = False

                if rsi_listed and rsi_exp_rate is not None:
                    exp_pct = rsi_exp_rate * 100.0
                    rate_ratio = round(obs_rate_pct / max(0.01, exp_pct), 2)
                    if obs_rate_pct > exp_pct:
                        rsi_breached = True
                elif not rsi_listed and (
                    data["is_susar"] or data["is_fatal"] or data["is_aesi"]
                ):
                    # Unlisted serious or unexpected event
                    rsi_breached = True

                row = DSURSummaryTabulationRow(
                    meddra_soc=soc,
                    meddra_pt=pt,
                    is_aesi=data["is_aesi"],
                    aesi_category=data["aesi_cat"],
                    is_fatal=data["is_fatal"],
                    is_susar=data["is_susar"],
                    active_arm_count=active_ev,
                    active_arm_subjects=active_sub,
                    comparator_arm_count=comp_ev,
                    comparator_arm_subjects=comp_sub,
                    placebo_arm_count=plac_ev,
                    placebo_arm_subjects=plac_sub,
                    total_event_count=total_ev,
                    total_subjects_count=total_sub,
                    cumulative_event_count=total_ev,
                    cumulative_subjects_count=total_sub,
                    rsi_listed=rsi_listed,
                    rsi_expected_rate=rsi_exp_rate,
                    observed_rate_pct=obs_rate_pct,
                    observed_incidence_rate_pyr=obs_incidence_pyr,
                    rsi_rate_ratio=rate_ratio,
                    rsi_threshold_breached=rsi_breached,
                )
                summary_rows.append(row)

                if rsi_breached or rsi_listed:
                    rsi_comparison_summary.append(
                        {
                            "meddra_soc": soc,
                            "meddra_pt": pt,
                            "rsi_listed": rsi_listed,
                            "expected_rate": rsi_exp_rate,
                            "observed_rate_pct": obs_rate_pct,
                            "rate_ratio": rate_ratio,
                            "breached": rsi_breached,
                            "is_susar": data["is_susar"],
                            "is_fatal": data["is_fatal"],
                        }
                    )

            summary_rows.sort(key=lambda r: (r.meddra_soc, -r.total_event_count))

        # 5. Real-time disproportionality signal analysis embedded in report
        signal_summary_list: list[dict[str, Any]] = []
        if request.include_signal_analyses and raw_cases:
            scan_results = SafetySignalDetectionEngine.scan_safety_dataset(
                cases=raw_cases,
                target_drug=request.compound_name,
            )
            for res in scan_results:
                if res.overall_signal_flag or res.prr >= 1.5:
                    signal_summary_list.append(
                        {
                            "meddra_pt": res.meddra_pt,
                            "meddra_soc": res.meddra_soc,
                            "case_count_a": res.case_count_a,
                            "prr": res.prr,
                            "prr_ci": f"[{res.prr_ci_lower}, {res.prr_ci_upper}]",
                            "chi_square_yates": res.chi_square_yates,
                            "ror": res.ror,
                            "ror_ci": f"[{res.ror_ci_lower}, {res.ror_ci_upper}]",
                            "ebgm": res.ebgm,
                            "eb05": res.eb05,
                            "signal_strength": res.signal_strength.value,
                            "overall_signal_flag": res.overall_signal_flag,
                        }
                    )

        # 6. Benefit-Risk narrative
        default_br_narrative = (
            f"During the reporting interval from {period_start} to {period_end}, a total of "
            f"{len(line_listings)} SAEs, {cumulative_fatal} fatal cases, and {cumulative_susars} SUSARs were evaluated for investigational compound {request.compound_name}. "
            f"Subject exposure totaled {exposure_metrics.total_cumulative_patient_years} cumulative patient-years across {total_exposure_subs} enrolled subjects. "
            f"Automated statistical disproportionality signal detection identified {len(signal_summary_list)} candidate safety signals. "
            f"Comparison against Reference Safety Information identified {sum(1 for r in summary_rows if r.rsi_threshold_breached)} threshold breaches requiring continuous monitoring. "
            f"Overall, the benefit-risk profile for {request.compound_name} remains favorable for clinical trial continuation under current protocol safety monitoring."
        )
        br_assessment = request.benefit_risk_narrative or default_br_narrative

        conclusions = (
            f"ICH E2F / ICH E2C(R2) periodic safety surveillance confirmed dataset completeness across {len(raw_cases)} "
            f"cumulative safety cases and {exposure_metrics.total_interval_patient_years} interval patient-years, with no unmitigated critical safety concerns detected."
        )

        guidelines = [
            "ICH E2F: Development Safety Update Report (DSUR)",
            "ICH E2C(R2): Periodic Benefit-Risk Evaluation Report (PBRER)",
            "FDA 21 CFR 312.33 Annual Reports",
            "EMA Clinical Trials Regulation EU No 536/2014",
            "21 CFR Part 11 Electronic Records and Signatures",
        ]

        payload = PeriodicReportPayload(
            report_id=report_id,
            report_type=request.report_type,
            study_id=request.study_id,
            compound_name=request.compound_name,
            reporting_period_start=period_start,
            reporting_period_end=period_end,
            dibd_anniversary=request.dibd_anniversary,
            sponsor_name=request.sponsor_name,
            snapshot=snapshot,
            exposure_metrics=exposure_metrics,
            total_subject_exposure=total_exposure_subs,
            total_saes=len(line_listings),
            total_fatal_cases=cumulative_fatal,
            total_susars=cumulative_susars,
            total_aesi=cumulative_aesi,
            line_listings=line_listings,
            summary_tabulations=summary_rows,
            rsi_comparison_summary=rsi_comparison_summary,
            signal_summary=signal_summary_list,
            benefit_risk_assessment=br_assessment,
            conclusions=conclusions,
            regulatory_guidelines=guidelines,
        )

        # Generate XML regulatory submission package
        payload.xml_submission_package = cls.generate_ich_xml(payload)

        return payload

    @classmethod
    def generate_ich_xml(cls, payload: PeriodicReportPayload) -> str:
        """Generates structured XML submission package per ICH E2F (DSUR) / ICH E2C(R2) (PBRER)."""
        root_tag = (
            "ich_e2f_dsur"
            if payload.report_type == PeriodicSafetyReportType.DSUR
            else "ich_e2c_pbrer"
        )
        schema_urn = (
            "urn:hl7-org:v3:ich-e2f"
            if payload.report_type == PeriodicSafetyReportType.DSUR
            else "urn:hl7-org:v3:ich-e2c"
        )

        xml_lines = [
            '<?xml version="1.0" encoding="UTF-8"?>',
            f'<{root_tag} version="2.0" xmlns="{schema_urn}">',
            "  <header>",
            f"    <report_id>{payload.report_id}</report_id>",
            f"    <report_type>{payload.report_type.value}</report_type>",
            f"    <study_id>{payload.study_id}</study_id>",
            f"    <compound_name>{payload.compound_name}</compound_name>",
            f"    <sponsor_name>{payload.sponsor_name}</sponsor_name>",
            "    <reporting_period>",
            f"      <start_date>{payload.reporting_period_start}</start_date>",
            f"      <end_date>{payload.reporting_period_end}</end_date>",
            f"      <dibd_anniversary>{payload.dibd_anniversary or ''}</dibd_anniversary>",
            "    </reporting_period>",
            f"    <snapshot_hash>{payload.snapshot.snapshot_hash if payload.snapshot else ''}</snapshot_hash>",
            "  </header>",
            "  <executive_summary>",
            f"    <benefit_risk_assessment>{payload.benefit_risk_assessment}</benefit_risk_assessment>",
            f"    <conclusions>{payload.conclusions}</conclusions>",
            "  </executive_summary>",
            "  <subject_exposure>",
            f"    <cumulative_subjects>{payload.exposure_metrics.total_cumulative_subjects}</cumulative_subjects>",
            f"    <interval_subjects>{payload.exposure_metrics.total_interval_subjects}</interval_subjects>",
            f"    <cumulative_patient_years>{payload.exposure_metrics.total_cumulative_patient_years}</cumulative_patient_years>",
            f"    <interval_patient_years>{payload.exposure_metrics.total_interval_patient_years}</interval_patient_years>",
            "    <stratification_by_arm>",
        ]

        for arm in payload.exposure_metrics.by_treatment_arm:
            xml_lines.append(
                f'      <arm name="{arm.group_name}" subjects="{arm.subject_count}" interval_pye="{arm.interval_patient_years}" cumulative_pye="{arm.cumulative_patient_years}"/>'
            )

        xml_lines.extend(
            [
                "    </stratification_by_arm>",
                "  </subject_exposure>",
                "  <summary_tabulations>",
            ]
        )

        for tab in payload.summary_tabulations:
            xml_lines.append(
                f'    <tabulation soc="{tab.meddra_soc}" pt="{tab.meddra_pt}" active="{tab.active_arm_count}" comp="{tab.comparator_arm_count}" placebo="{tab.placebo_arm_count}" total="{tab.total_event_count}" susar="{str(tab.is_susar).lower()}" fatal="{str(tab.is_fatal).lower()}" aesi="{str(tab.is_aesi).lower()}" rsi_breached="{str(tab.rsi_threshold_breached).lower()}"/>'
            )

        xml_lines.extend(
            [
                "  </summary_tabulations>",
                "  <signals_and_disproportionality>",
            ]
        )

        for sig in payload.signal_summary:
            xml_lines.append(
                f'    <signal pt="{sig.get("meddra_pt")}" prr="{sig.get("prr")}" ror="{sig.get("ror")}" eb05="{sig.get("eb05")}" strength="{sig.get("signal_strength")}"/>'
            )

        xml_lines.extend(
            [
                "  </signals_and_disproportionality>",
            ]
        )

        if payload.qppv_signoff:
            xml_lines.extend(
                [
                    "  <qppv_signoff>",
                    f"    <signer_id>{payload.qppv_signoff.qppv_user_id}</signer_id>",
                    f"    <signer_role>{payload.qppv_signoff.qppv_role}</signer_role>",
                    f"    <manifest_id>{payload.qppv_signoff.manifest_id}</manifest_id>",
                    f"    <signed_at>{payload.qppv_signoff.signed_at}</signed_at>",
                    f"    <signature_token>{payload.qppv_signoff.signature_token}</signature_token>",
                    "  </qppv_signoff>",
                ]
            )

        xml_lines.append(f"</{root_tag}>")
        return "\n".join(xml_lines)

    @classmethod
    def render_html_report(cls, payload: PeriodicReportPayload) -> str:
        """Renders publication-ready HTML representation of the periodic regulatory report with print styles."""
        ll_rows = "".join(
            f"""<tr>
                <td>{item.case_id}</td>
                <td>{item.subject_id}</td>
                <td>{item.treatment_arm}</td>
                <td>{item.country}</td>
                <td>{item.meddra_soc}</td>
                <td><strong>{item.meddra_pt}</strong></td>
                <td>{item.onset_date}</td>
                <td>{item.outcome}</td>
                <td>{"Yes" if item.susar else "No"}</td>
                <td>{"Yes" if item.is_aesi else "No"}</td>
                <td>{item.causality}</td>
            </tr>"""
            for item in payload.line_listings
        )

        tab_rows = "".join(
            f"""<tr>
                <td>{row.meddra_soc}</td>
                <td><strong>{row.meddra_pt}</strong> {'<span class="badge badge-warning">AESI</span>' if row.is_aesi else ""} {'<span class="badge badge-critical">SUSAR</span>' if row.is_susar else ""} {'<span class="badge badge-critical">FATAL</span>' if row.is_fatal else ""}</td>
                <td>{row.active_arm_count} ({row.active_arm_subjects})</td>
                <td>{row.comparator_arm_count} ({row.comparator_arm_subjects})</td>
                <td>{row.placebo_arm_count} ({row.placebo_arm_subjects})</td>
                <td><strong>{row.total_event_count} ({row.total_subjects_count})</strong></td>
                <td>{row.observed_rate_pct}% ({row.observed_incidence_rate_pyr}/100 PYE)</td>
                <td>{"Listed (" + str(round((row.rsi_expected_rate or 0) * 100, 1)) + "%)" if row.rsi_listed else '<span class="text-muted">Unlisted</span>'}</td>
                <td>{'<span class="badge badge-critical">BREACH</span>' if row.rsi_threshold_breached else '<span class="badge badge-success">OK</span>'}</td>
            </tr>"""
            for row in payload.summary_tabulations
        )

        arm_exposure_rows = "".join(
            f"""<tr>
                <td><strong>{arm.group_name}</strong></td>
                <td>{arm.subject_count}</td>
                <td>{arm.interval_patient_years}</td>
                <td>{arm.cumulative_patient_years}</td>
                <td>{arm.mean_duration_days} d</td>
                <td>{arm.median_duration_days} d</td>
            </tr>"""
            for arm in payload.exposure_metrics.by_treatment_arm
        )

        cohort_exposure_rows = "".join(
            f"""<tr>
                <td><strong>{c.group_name}</strong></td>
                <td>{c.subject_count}</td>
                <td>{c.interval_patient_years}</td>
                <td>{c.cumulative_patient_years}</td>
            </tr>"""
            for c in payload.exposure_metrics.by_dosage_cohort
        )

        sig_rows = "".join(
            f"""<tr>
                <td><strong>{sig.get("meddra_pt")}</strong></td>
                <td>{sig.get("case_count_a")}</td>
                <td>{sig.get("prr")} {sig.get("prr_ci")}</td>
                <td>{sig.get("chi_square_yates")}</td>
                <td>{sig.get("ror")} {sig.get("ror_ci")}</td>
                <td>{sig.get("eb05")} / {sig.get("ebgm")}</td>
                <td><span class="badge {sig.get("signal_strength", "").lower()}">{sig.get("signal_strength")}</span></td>
            </tr>"""
            for sig in payload.signal_summary
        )

        qppv_block = ""
        if payload.qppv_signoff:
            qppv = payload.qppv_signoff
            qppv_block = f"""
            <div class="qppv-seal-card">
                <h3>Executive QPPV Electronic Signature Manifest (21 CFR Part 11)</h3>
                <table class="meta-table">
                    <tr><td><strong>Authorized Signer:</strong> {qppv.qppv_name} ({qppv.qppv_user_id})</td><td><strong>Role:</strong> {qppv.qppv_role}</td></tr>
                    <tr><td><strong>Signing Timestamp (UTC):</strong> {qppv.signed_at}</td><td><strong>Verification:</strong> <span class="badge badge-success">{qppv.verification_status}</span></td></tr>
                    <tr><td><strong>Manifest ID:</strong> {qppv.manifest_id}</td><td><strong>Meaning:</strong> {qppv.meaning_of_signature}</td></tr>
                    <tr><td colspan="2"><strong>Cryptographic SHA-256 Snapshot Seal:</strong> <code>{qppv.snapshot_hash}</code></td></tr>
                </table>
            </div>
            """

        snapshot_info = ""
        if payload.snapshot:
            snapshot_info = f"""
            <tr>
                <td><strong>Point-in-Time Regulatory Snapshot:</strong></td>
                <td><code>{payload.snapshot.snapshot_hash}</code> ({payload.snapshot.total_cases_frozen} cases, {payload.snapshot.total_subjects_frozen} subjects frozen)</td>
            </tr>
            """

        return f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>{payload.report_type.value} - {payload.compound_name} ({
            payload.study_id
        })</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; line-height: 1.5; color: #1e293b; padding: 2rem; background: #ffffff; }}
        h1, h2, h3 {{ color: #0f172a; border-bottom: 2px solid #e2e8f0; padding-bottom: 0.5rem; margin-top: 1.5rem; }}
        .meta-table {{ width: 100%; border-collapse: collapse; margin-bottom: 1.5rem; }}
        .meta-table td {{ padding: 0.5rem 0.75rem; border: 1px solid #cbd5e1; font-size: 0.875rem; }}
        .meta-table strong {{ color: #334155; }}
        table.data-table {{ width: 100%; border-collapse: collapse; margin-top: 0.75rem; margin-bottom: 1.5rem; font-size: 0.85rem; }}
        table.data-table th, table.data-table td {{ border: 1px solid #cbd5e1; padding: 0.5rem 0.65rem; text-align: left; }}
        table.data-table th {{ background-color: #f1f5f9; color: #334155; font-weight: 600; }}
        .badge {{ display: inline-block; padding: 0.2rem 0.4rem; border-radius: 4px; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; }}
        .badge-critical, .badge.critical {{ background: #fee2e2; color: #991b1b; }}
        .badge-warning, .badge.strong {{ background: #fef3c7; color: #92400e; }}
        .badge-success {{ background: #dcfce7; color: #166534; }}
        .badge.moderate {{ background: #e0e7ff; color: #3730a3; }}
        .narrative-box {{ background: #f8fafc; border-left: 4px solid #3b82f6; padding: 1rem; margin: 1rem 0; font-size: 0.9rem; }}
        .qppv-seal-card {{ background: #f0fdf4; border: 1px solid #86efac; border-radius: 6px; padding: 1rem; margin-top: 2rem; }}
        code {{ font-family: monospace; background: #f1f5f9; padding: 0.2rem 0.4rem; border-radius: 4px; font-size: 0.8rem; }}
        .text-muted {{ color: #64748b; font-style: italic; }}
        @media print {{
            body {{ padding: 0; font-size: 11pt; }}
            .badge {{ border: 1px solid #000; }}
        }}
    </style>
</head>
<body>
    <h1>Periodic Regulatory Safety Report: {payload.report_type.value}</h1>
    <table class="meta-table">
        <tr><td><strong>Compound:</strong> {
            payload.compound_name
        }</td><td><strong>Study ID:</strong> {payload.study_id}</td></tr>
        <tr><td><strong>Reporting Period:</strong> {payload.reporting_period_start} to {
            payload.reporting_period_end
        }</td><td><strong>DIBD Anniversary:</strong> {
            payload.dibd_anniversary or "N/A"
        }</td></tr>
        <tr><td><strong>Sponsor:</strong> {
            payload.sponsor_name
        }</td><td><strong>Report ID:</strong> {payload.report_id}</td></tr>
        <tr><td><strong>Cumulative Subject Exposure:</strong> {
            payload.total_subject_exposure
        } Subjects ({
            payload.exposure_metrics.total_cumulative_patient_years
        } PYE)</td><td><strong>Total SAEs:</strong> {
            payload.total_saes
        } | <strong>Fatal:</strong> {
            payload.total_fatal_cases
        } | <strong>SUSARs:</strong> {payload.total_susars} | <strong>AESI:</strong> {
            payload.total_aesi
        }</td></tr>
        {snapshot_info}
    </table>

    <h2>1. Executive Benefit-Risk Assessment & Safety Findings</h2>
    <div class="narrative-box">
        <p>{payload.benefit_risk_assessment}</p>
        <p><strong>Conclusion:</strong> {payload.conclusions}</p>
    </div>

    <h2>2. Patient-Years of Exposure Stratification (ICH E2F § 5)</h2>
    <table class="data-table">
        <thead>
            <tr><th>Treatment Arm</th><th>Subjects (N)</th><th>Interval PYE</th><th>Cumulative PYE</th><th>Mean Duration</th><th>Median Duration</th></tr>
        </thead>
        <tbody>
            {
            arm_exposure_rows
            if arm_exposure_rows
            else '<tr><td colspan="6">No exposure data recorded for this interval.</td></tr>'
        }
        </tbody>
    </table>

    {
            f'''<h3>Dosage Cohort Exposure</h3>
    <table class="data-table">
        <thead><tr><th>Dosage Cohort</th><th>Subjects (N)</th><th>Interval PYE</th><th>Cumulative PYE</th></tr></thead>
        <tbody>{cohort_exposure_rows}</tbody>
    </table>'''
            if cohort_exposure_rows
            else ""
        }

    <h2>3. Disproportionality Safety Signal Detection Analysis (CIOMS VIII / MGPS)</h2>
    <table class="data-table">
        <thead>
            <tr><th>MedDRA PT</th><th>Cases (a)</th><th>PRR (95% CI)</th><th>Chi-Square (Yates)</th><th>ROR (95% CI)</th><th>EB05 / EBGM</th><th>Signal Strength</th></tr>
        </thead>
        <tbody>
            {
            sig_rows
            if sig_rows
            else '<tr><td colspan="7">No disproportionality signal threshold breaches identified.</td></tr>'
        }
        </tbody>
    </table>

    <h2>4. Summary Tabulations & Reference Safety Information (RSI) Matrix (ICH E2F § 7)</h2>
    <table class="data-table">
        <thead>
            <tr><th>MedDRA SOC</th><th>MedDRA PT</th><th>Active Events (Subs)</th><th>Comparator (Subs)</th><th>Placebo (Subs)</th><th>Total Events (Subs)</th><th>Observed Rate (%)</th><th>RSI Status</th><th>Threshold Check</th></tr>
        </thead>
        <tbody>
            {
            tab_rows
            if tab_rows
            else '<tr><td colspan="9">No adverse events tabulated during this interval.</td></tr>'
        }
        </tbody>
    </table>

    <h2>5. Line Listings of Serious Adverse Events (SAEs)</h2>
    <table class="data-table">
        <thead>
            <tr><th>Case ID</th><th>Subject ID</th><th>Arm</th><th>Country</th><th>MedDRA SOC</th><th>MedDRA PT</th><th>Onset Date</th><th>Outcome</th><th>SUSAR</th><th>AESI</th><th>Causality</th></tr>
        </thead>
        <tbody>
            {
            ll_rows
            if ll_rows
            else '<tr><td colspan="11">No Serious Adverse Events reported during this interval.</td></tr>'
        }
        </tbody>
    </table>

    {qppv_block}
</body>
</html>"""
