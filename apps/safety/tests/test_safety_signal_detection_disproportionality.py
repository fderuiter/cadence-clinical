"""Comprehensive test suite for Disproportionality Safety Signal Detection Engine (PRR, ROR, EBGM/MGPS) and Periodic Safety Reports (DSUR, PSUR/PBRER).

@req:PRD-PVG-003
@req:PRD-PVG-004
@req:PRD-PVG-008
@req:PRD-SYS-001
@req:ADR-2199
"""

import os
import uuid
from datetime import UTC, datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select, text

from apps.safety.database import db_manager
from apps.safety.domain.periodic_report_models import (
    PeriodicReportCompilationRequest,
    PeriodicReportStatus,
    PeriodicSafetyReportType,
)
from apps.safety.domain.signal_models import (
    BenchmarkReferenceDataset,
    DisproportionalityContingency,
    SafetySignalAlertReviewRequest,
    SafetySignalAlertSeverity,
    SafetySignalAlertStatus,
    SafetySignalReviewDecision,
    SignalDetectionScanRequest,
    SignalStrength,
    StratificationLevel,
)
from apps.safety.main import app
from apps.safety.models import (
    Base,
    SafetyAuditLog,
    SafetySignalAlert,
)
from apps.safety.services.periodic_report_compiler import (
    PeriodicSafetyReportCompiler,
)
from apps.safety.services.signal_alert_service import (
    SafetySignalAlertService,
)
from apps.safety.services.signal_detection_engine import (
    SafetySignalDetectionEngine,
)
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("safety_signal_detection")


@pytest_asyncio.fixture(autouse=True)
async def setup_signal_db():
    """Setup isolated in-memory database for signal detection tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_signal_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def make_sig_token(user_id: str, action: str = "") -> str:
    """Creates a mock 21 CFR Part 11 signature token JWT."""
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    claims = {
        "sub": user_id,
        "action": action,
        "roles": ["safety_officer", "safety_physician"],
        "iat": int(datetime.now(UTC).timestamp()),
        "exp": int(datetime.now(UTC).timestamp()) + 3600,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(claims, secret, algorithm="HS256")


# =========================================================================
# Unit Tests: Statistical Calculations (PRD-PVG-003)
# =========================================================================


def test_prr_calculation_point_estimate_and_ci():
    """Validate PRR point estimate, exact log standard error, and 95% Confidence Intervals.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # a = 15, b = 85 (Total target drug reports = 100)
    # c = 10, d = 990 (Total other drug reports = 1000)
    # PRR = (15 / 100) / (10 / 1000) = 0.15 / 0.01 = 15.0
    contingency = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Hepatic failure",
        a=15,
        b=85,
        c=10,
        d=990,
    )
    metrics = SafetySignalDetectionEngine.calculate_metrics(contingency)

    assert metrics.prr == 15.0
    assert metrics.prr_ci_lower > 6.0
    assert metrics.prr_ci_upper > metrics.prr
    assert metrics.is_prr_signal is True
    assert metrics.overall_signal_flag is True
    assert metrics.signal_strength in {SignalStrength.CRITICAL, SignalStrength.STRONG}


def test_yates_chi_square_continuity_correction():
    """Validate Yates' corrected Chi-Square on strong and null signals.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # Strong signal table
    strong = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Stevens-Johnson syndrome",
        a=20,
        b=80,
        c=5,
        d=995,
    )
    m_strong = SafetySignalDetectionEngine.calculate_metrics(strong)
    assert m_strong.chi_square_yates > 50.0

    # Null signal table: a=2, b=98, c=20, d=980
    null_case = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Headache",
        a=2,
        b=98,
        c=20,
        d=980,
    )
    m_null = SafetySignalDetectionEngine.calculate_metrics(null_case)
    assert m_null.chi_square_yates < 4.0
    assert m_null.is_prr_signal is False


def test_ror_point_estimate_and_confidence_interval():
    """Validate Reporting Odds Ratio (ROR) point estimate and 95% CI bounds.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # a = 12, b = 88, c = 8, d = 992
    # ROR = (12 * 992) / (88 * 8) = 11904 / 704 = 16.9091
    contingency = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Agranulocytosis",
        a=12,
        b=88,
        c=8,
        d=992,
    )
    metrics = SafetySignalDetectionEngine.calculate_metrics(contingency)

    assert metrics.ror == pytest.approx(16.9091, rel=1e-3)
    assert metrics.ror_ci_lower > 6.0
    assert metrics.ror_ci_lower > 1.0
    assert metrics.is_ror_signal is True


def test_empirical_bayes_shrinkage_mgps_ebgm_and_eb05():
    """Validate Multi-item Gamma Poisson Shrinker (MGPS) shrinkage on sparse adverse events.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    # Small count with high observed/expected: a = 4, expected E = 0.5
    contingency = DisproportionalityContingency(
        drug_name="CADENCE-101",
        meddra_pt="Torsade de pointes",
        a=4,
        b=96,
        c=2,
        d=998,
    )
    metrics = SafetySignalDetectionEngine.calculate_metrics(contingency)

    # EBGM shrinks towards prior (alpha0=1.0, beta0=1.0)
    assert metrics.ebgm > 1.0
    assert metrics.eb05 > 0.5
    assert metrics.eb95 > metrics.ebgm
    assert metrics.is_ebgm_signal is True or metrics.overall_signal_flag is True


def test_multi_item_mgps_interaction_shrinkage():
    """Validate multi-item interaction shrinkage for drug-drug-event combinations.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    combos = [
        {
            "items": ["CADENCE-101", "WARFARIN", "Gastrointestinal haemorrhage"],
            "observed_n": 8,
            "expected_e": 1.2,
        },
        {
            "items": ["CADENCE-101", "ASPIRIN", "Headache"],
            "observed_n": 1,
            "expected_e": 2.5,
        },
    ]
    results = SafetySignalDetectionEngine.calculate_mgps_multi_item(combos)

    assert len(results) == 2
    top = results[0]
    assert top.item_set == ["CADENCE-101", "WARFARIN", "Gastrointestinal haemorrhage"]
    assert top.observed_n == 8
    assert top.ebgm > 3.0
    assert top.interaction_score > 2.0
    assert top.is_interaction_signal is True


def test_pt_and_soc_level_stratification_scan():
    """Validate disproportionality scanning across both Preferred Term and System Organ Class levels.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    cases = [
        {
            "drug_name": "DRUG_A",
            "meddra_pt": "Hepatic failure",
            "meddra_soc": "Hepatobiliary disorders",
        },
        {
            "drug_name": "DRUG_A",
            "meddra_pt": "Hepatic failure",
            "meddra_soc": "Hepatobiliary disorders",
        },
        {
            "drug_name": "DRUG_A",
            "meddra_pt": "Hepatitis",
            "meddra_soc": "Hepatobiliary disorders",
        },
        {
            "drug_name": "DRUG_A",
            "meddra_pt": "Hepatitis",
            "meddra_soc": "Hepatobiliary disorders",
        },
        {
            "drug_name": "DRUG_A",
            "meddra_pt": "Nausea",
            "meddra_soc": "Gastrointestinal disorders",
        },
        {
            "drug_name": "COMP_B",
            "meddra_pt": "Nausea",
            "meddra_soc": "Gastrointestinal disorders",
        },
        {
            "drug_name": "COMP_B",
            "meddra_pt": "Headache",
            "meddra_soc": "Nervous system disorders",
        },
        {
            "drug_name": "COMP_B",
            "meddra_pt": "Headache",
            "meddra_soc": "Nervous system disorders",
        },
        {
            "drug_name": "COMP_B",
            "meddra_pt": "Hepatic failure",
            "meddra_soc": "Hepatobiliary disorders",
        },
    ]

    results = SafetySignalDetectionEngine.scan_safety_dataset(
        cases=cases,
        target_drug="DRUG_A",
        stratification_levels=[StratificationLevel.PT, StratificationLevel.SOC],
    )

    pt_results = [r for r in results if r.term_level == StratificationLevel.PT]
    soc_results = [r for r in results if r.term_level == StratificationLevel.SOC]

    assert len(pt_results) > 0
    assert len(soc_results) > 0

    # Hepatobiliary disorders at SOC level should combine Hepatic failure + Hepatitis
    hep_soc = next((r for r in soc_results if "Hepatobiliary" in r.meddra_pt), None)
    assert hep_soc is not None
    assert hep_soc.case_count_a == 4


def test_external_benchmark_dataset_comparison():
    """Validate comparison against external benchmark background rates (e.g. FAERS / EudraVigilance).

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    trial_cases = [
        {
            "drug_name": "DRUG_X",
            "meddra_pt": "Pancreatitis acute",
            "meddra_soc": "Gastrointestinal disorders",
        },
        {
            "drug_name": "DRUG_X",
            "meddra_pt": "Pancreatitis acute",
            "meddra_soc": "Gastrointestinal disorders",
        },
        {
            "drug_name": "DRUG_X",
            "meddra_pt": "Pancreatitis acute",
            "meddra_soc": "Gastrointestinal disorders",
        },
        {
            "drug_name": "DRUG_X",
            "meddra_pt": "Fatigue",
            "meddra_soc": "General disorders",
        },
    ]
    benchmark_cases = [
        {"drug_name": "FAERS_BACKGROUND", "meddra_pt": "Fatigue"},
        {"drug_name": "FAERS_BACKGROUND", "meddra_pt": "Nausea"},
        {"drug_name": "FAERS_BACKGROUND", "meddra_pt": "Dizziness"},
        {"drug_name": "FAERS_BACKGROUND", "meddra_pt": "Pancreatitis acute"},
    ]

    results = SafetySignalDetectionEngine.scan_safety_dataset(
        cases=trial_cases,
        target_drug="DRUG_X",
        benchmark_dataset=BenchmarkReferenceDataset.FAERS.value,
        benchmark_cases=benchmark_cases,
    )

    panc = next((r for r in results if r.meddra_pt == "Pancreatitis acute"), None)
    assert panc is not None
    assert panc.case_count_a == 3
    assert panc.benchmark_dataset == BenchmarkReferenceDataset.FAERS.value
    assert panc.prr > 1.0


# =========================================================================
# Integration Tests: Signal Alert Workflow & Part 11 E-Signature (PRD-PVG-003, PRD-PVG-008)
# =========================================================================


@pytest.mark.asyncio
async def test_signal_alert_triggering_and_severity_workflow():
    """Validate automated creation of SafetySignalAlert records upon statistical threshold breach.

    @req:PRD-PVG-003
    @req:PRD-SYS-001
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    async with db_manager.session_maker() as session:
        service = SafetySignalAlertService(session=session)

        # Prepare cases with clear signal for Hepatic failure (a=10)
        cases = []
        for _ in range(10):
            cases.append(
                {
                    "drug_name": "CAD-202",
                    "meddra_pt": "Hepatic failure",
                    "meddra_soc": "Hepatobiliary disorders",
                }
            )
        for _ in range(20):
            cases.append(
                {
                    "drug_name": "CAD-202",
                    "meddra_pt": "Nausea",
                    "meddra_soc": "Gastrointestinal disorders",
                }
            )
        for _ in range(100):
            cases.append(
                {
                    "drug_name": "COMPARATOR",
                    "meddra_pt": "Nausea",
                    "meddra_soc": "Gastrointestinal disorders",
                }
            )
        for _ in range(2):
            cases.append(
                {
                    "drug_name": "COMPARATOR",
                    "meddra_pt": "Hepatic failure",
                    "meddra_soc": "Hepatobiliary disorders",
                }
            )

        req = SignalDetectionScanRequest(
            target_drug="CAD-202",
            study_id="STUDY-SIGNAL-01",
            auto_trigger_alerts=True,
            cases=cases,
        )

        resp = await service.execute_scan(
            request=req,
            user_id="safety_officer_01",
            reason_for_change="Quarterly signal detection scan",
        )
        await session.commit()

        assert resp.total_cases_analyzed == 132
        assert resp.signals_detected_count >= 1
        assert resp.alerts_triggered_count >= 1

        # Verify alert persisted in DB
        alerts = await service.list_alerts(study_id="STUDY-SIGNAL-01")
        assert len(alerts) >= 1
        top_alert = alerts[0]
        assert top_alert.target_drug == "CAD-202"
        assert top_alert.severity in {
            SafetySignalAlertSeverity.CRITICAL.value,
            SafetySignalAlertSeverity.HIGH.value,
        }
        assert top_alert.status == SafetySignalAlertStatus.TRIGGERED.value


@pytest.mark.asyncio
async def test_signal_alert_review_and_part11_esignature_approval():
    """Validate review disposition and 21 CFR Part 11 e-signature approval on safety signal alerts.

    @req:PRD-PVG-003
    @req:PRD-PVG-008
    @req:ADR-2199
    @req:PRD-PVG-004
    @req:PRD-SYS-169
    """
    async with db_manager.session_maker() as session:
        service = SafetySignalAlertService(session=session)

        # Create alert directly
        alert_id = f"alert_test_{uuid.uuid4().hex[:8]}"
        alert = SafetySignalAlert(
            id=alert_id,
            study_id="STUDY-SIG-SIGN",
            target_drug="CAD-999",
            meddra_term="Anaphylactic reaction",
            term_level="PT",
            case_count_a=6,
            expected_count_e=0.8,
            prr=7.5,
            prr_ci_lower=2.8,
            prr_ci_upper=20.1,
            chi_square_yates=18.4,
            ror=8.2,
            ror_ci_lower=3.1,
            ror_ci_upper=21.5,
            ebgm=4.5,
            eb05=2.2,
            eb95=8.9,
            severity="CRITICAL",
            status="TRIGGERED",
            triage_summary="Critical hypersensitivity signal detected.",
            created_by="system",
            reason_for_change="Threshold breach",
            version_index=1,
        )
        session.add(alert)
        await session.commit()

        # Step 1: Medical Monitor reviews alert
        review_req = SafetySignalAlertReviewRequest(
            decision=SafetySignalReviewDecision.CONFIRM_SIGNAL,
            justification="Review confirmed 6 verified anaphylaxis cases within 24h of infusion.",
            clinical_summary="Urgent safety labeling update recommended.",
            assigned_safety_officer="Dr. Emily Roberts",
        )
        updated_alert = await service.review_alert(
            alert_id=alert_id,
            request=review_req,
            user_id="pv_physician_01",
            reason_for_change="Medical causality review",
        )
        await session.commit()
        assert updated_alert.status == SafetySignalAlertStatus.CONFIRMED_SIGNAL.value

        # Step 2: 21 CFR Part 11 Electronic Signature sign-off
        sig_token = make_sig_token("pv_physician_01", "SIGN_SIGNAL_REVIEW")

        sign_resp = await service.sign_alert_review(
            alert_id=alert_id,
            decision=SafetySignalReviewDecision.CONFIRM_SIGNAL,
            justification="Part 11 electronic approval of confirmed safety signal.",
            clinical_summary="Escalated to Global Safety Committee.",
            user_id="pv_physician_01",
            user_roles=["safety_officer"],
            reason_for_change="Formal Safety Signal Approval",
            signature_secret=sig_token,
            meaning_of_signature="Approval of Signal Review Assessment & Disposition",
        )
        await session.commit()

        assert sign_resp.status == SafetySignalAlertStatus.CONFIRMED_SIGNAL
        assert sign_resp.esignature_manifest_id.startswith("sig_sig_")

        # Verify audit log recorded
        stmt = select(SafetyAuditLog).where(SafetyAuditLog.record_id == alert_id)
        res = await session.execute(stmt)
        logs = res.scalars().all()
        assert len(logs) >= 2


# =========================================================================
# Integration Tests: Periodic Aggregate Safety Reports (DSUR, PSUR/PBRER) (PRD-PVG-004)
# =========================================================================


def test_dsur_periodic_report_compilation_and_line_listings():
    """Validate ICH E2F DSUR compilation with SAE line listings and interval summary tabulations.

    @req:PRD-PVG-004
    @req:PRD-PVG-003
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    cases = [
        {
            "case_id": "CASE-001",
            "subject_id": "SUB-101",
            "study_id": "STUDY-DSUR-01",
            "treatment_arm": "CADENCE-101 50mg",
            "drug_name": "CADENCE-101",
            "meddra_pt": "Hepatic failure",
            "meddra_soc": "Hepatobiliary disorders",
            "onset_date": "2026-03-15",
            "seriousness_death": False,
            "seriousness_life_threatening": True,
            "seriousness_hospitalization": True,
            "causality": "RELATED",
            "expectedness": "UNEXPECTED",
            "outcome": "RECOVERED",
            "country": "US",
        },
        {
            "case_id": "CASE-002",
            "subject_id": "SUB-102",
            "study_id": "STUDY-DSUR-01",
            "treatment_arm": "Placebo",
            "drug_name": "Placebo",
            "meddra_pt": "Headache",
            "meddra_soc": "Nervous system disorders",
            "onset_date": "2026-04-10",
            "seriousness_hospitalization": True,
            "causality": "NOT_RELATED",
            "expectedness": "EXPECTED",
            "outcome": "RECOVERED",
            "country": "UK",
        },
    ]

    req = PeriodicReportCompilationRequest(
        report_type=PeriodicSafetyReportType.DSUR,
        study_id="STUDY-DSUR-01",
        compound_name="CADENCE-101",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-06-30",
        cumulative_exposure_subjects=150,
    )

    payload = PeriodicSafetyReportCompiler.compile_report(req, cases)

    assert payload.report_type == PeriodicSafetyReportType.DSUR
    assert payload.total_saes == 2
    assert payload.total_susars == 1  # CASE-001 is fatal/LT + related + unexpected
    assert len(payload.line_listings) == 2
    assert len(payload.summary_tabulations) >= 2

    # Check line listing fields
    ll_0 = payload.line_listings[0]
    assert ll_0.case_id == "CASE-001"
    assert ll_0.meddra_pt == "Hepatic failure"
    assert ll_0.susar is True

    # Check HTML generation
    html = PeriodicSafetyReportCompiler.render_html_report(payload)
    assert "Periodic Regulatory Safety Report: DSUR" in html
    assert "Hepatic failure" in html
    assert "Line Listings of Serious Adverse Events" in html


@pytest.mark.asyncio
async def test_psur_periodic_report_compilation_and_part11_signature():
    """Validate compilation and 21 CFR Part 11 electronic signature on ICH E2C(R2) PSUR report.

    @req:PRD-PVG-004
    @req:PRD-PVG-008
    @req:PRD-PVG-003
    @req:PRD-SYS-169
    @req:ADR-2199
    """
    async with db_manager.session_maker() as session:
        service = SafetySignalAlertService(session=session)

        req = PeriodicReportCompilationRequest(
            report_type=PeriodicSafetyReportType.PSUR,
            study_id="STUDY-PSUR-01",
            compound_name="CADENCE-101",
            reporting_period_start="2025-08-01",
            reporting_period_end="2026-08-01",
            cumulative_exposure_subjects=500,
            cases=[
                {
                    "case_id": "CASE-P1",
                    "subject_id": "SUB-P1",
                    "study_id": "STUDY-PSUR-01",
                    "treatment_arm": "CADENCE-101",
                    "drug_name": "CADENCE-101",
                    "meddra_pt": "Pneumonitis",
                    "meddra_soc": "Respiratory, thoracic and mediastinal disorders",
                    "onset_date": "2026-02-15",
                    "seriousness_hospitalization": True,
                    "causality": "RELATED",
                    "expectedness": "UNEXPECTED",
                }
            ],
        )

        resp = await service.compile_periodic_report(
            request=req,
            user_id="qppv_officer_01",
            reason_for_change="Annual PSUR Submission Preparation",
        )
        await session.commit()

        assert resp.report_type == "PSUR"
        assert resp.status == PeriodicReportStatus.DRAFT.value
        assert resp.total_saes == 1

        # Sign Periodic Report
        sig_token = make_sig_token("qppv_officer_01", "SIGN_PERIODIC_REPORT")
        sign_resp = await service.sign_periodic_report(
            report_id=resp.id,
            user_id="qppv_officer_01",
            user_roles=["safety_officer"],
            reason_for_change="Final Qualified Person PV Sign-Off",
            signature_secret=sig_token,
            meaning_of_signature="Approval of ICH E2F DSUR / ICH E2C(R2) Periodic Safety Report",
        )
        await session.commit()

        assert sign_resp.status == PeriodicReportStatus.APPROVED
        assert sign_resp.esignature_manifest_id.startswith("sig_rep_")


# =========================================================================
# Integration Tests: FastAPI REST Endpoints (PRD-PVG-003, PRD-PVG-004, ADR-2199)
# =========================================================================


def test_fastapi_rest_signal_detection_and_alerts_endpoints():
    """Validate full REST API execution of disproportionality signal detection and alert reviews.

    @req:PRD-PVG-003
    @req:PRD-PVG-004
    @req:PRD-PVG-008
    @req:PRD-SYS-001
    @req:ADR-2199
    @req:PRD-SYS-169
    """
    client = TestClient(app)
    headers = create_test_auth_headers(
        user_id="safety_lead_01",
        roles=["safety_officer"],
        change_reason="Automated Disproportionality API Test",
    )

    cases_payload = []
    for _ in range(8):
        cases_payload.append(
            {
                "drug_name": "CAD-API",
                "meddra_pt": "Renal failure",
                "meddra_soc": "Renal and urinary disorders",
            }
        )
    for _ in range(12):
        cases_payload.append(
            {
                "drug_name": "CAD-API",
                "meddra_pt": "Headache",
                "meddra_soc": "Nervous system disorders",
            }
        )
    for _ in range(80):
        cases_payload.append(
            {
                "drug_name": "CONTROL-API",
                "meddra_pt": "Headache",
                "meddra_soc": "Nervous system disorders",
            }
        )
    for _ in range(80):
        cases_payload.append(
            {
                "drug_name": "CONTROL-API",
                "meddra_pt": "Nausea",
                "meddra_soc": "Gastrointestinal disorders",
            }
        )
    cases_payload.append(
        {
            "drug_name": "CONTROL-API",
            "meddra_pt": "Renal failure",
            "meddra_soc": "Renal and urinary disorders",
        }
    )

    # 1. POST /api/v1/safety/signals/detect
    scan_resp = client.post(
        "/api/v1/safety/signals/detect",
        json={
            "target_drug": "CAD-API",
            "study_id": "STUDY-API-TEST",
            "stratification_levels": ["PT", "SOC"],
            "auto_trigger_alerts": True,
            "cases": cases_payload,
        },
        headers=headers,
    )
    assert scan_resp.status_code == 200
    scan_data = scan_resp.json()
    assert scan_data["target_drug"] == "CAD-API"
    assert scan_data["total_cases_analyzed"] == 181
    assert len(scan_data["results"]) >= 2
    run_id = scan_data["run_id"]

    # 2. GET /api/v1/safety/signals/runs
    runs_resp = client.get(
        "/api/v1/safety/signals/runs?target_drug=CAD-API", headers=headers
    )
    assert runs_resp.status_code == 200
    runs_list = runs_resp.json()
    assert len(runs_list) >= 1

    # 3. GET /api/v1/safety/signals/runs/{id}
    run_get = client.get(f"/api/v1/safety/signals/runs/{run_id}", headers=headers)
    assert run_get.status_code == 200
    assert run_get.json()["id"] == run_id

    # 4. GET /api/v1/safety/signals/alerts
    alerts_resp = client.get(
        "/api/v1/safety/signals/alerts?study_id=STUDY-API-TEST", headers=headers
    )
    assert alerts_resp.status_code == 200
    alerts = alerts_resp.json()
    assert len(alerts) >= 1
    alert_id = alerts[0]["id"]

    # 5. POST /api/v1/safety/signals/alerts/{id}/review
    review_resp = client.post(
        f"/api/v1/safety/signals/alerts/{alert_id}/review",
        json={
            "decision": "CONFIRM_SIGNAL",
            "justification": "Confirmed signal of renal toxicity in CAD-API arm.",
            "clinical_summary": "Dose modification recommended.",
        },
        headers=headers,
    )
    assert review_resp.status_code == 200
    assert review_resp.json()["status"] == "CONFIRMED_SIGNAL"

    # 6. POST /api/v1/safety/signals/alerts/{id}/sign
    sig_path = f"/api/v1/safety/signals/alerts/{alert_id}/sign"
    sig_token = make_sig_token("safety_lead_01", sig_path)
    sign_headers = create_test_auth_headers(
        user_id="safety_lead_01",
        roles=["safety_officer"],
        change_reason="Automated Disproportionality API Test Sign",
        sig_token=sig_token,
    )
    sign_resp = client.post(
        sig_path,
        json={
            "user_id": "safety_lead_01",
            "decision": "CONFIRM_SIGNAL",
            "justification": "Approved safety signal disposition under Part 11.",
            "signature_secret": sig_token,
            "meaning_of_signature": "Approval of Signal Review Assessment & Disposition",
        },
        headers=sign_headers,
    )
    assert sign_resp.status_code == 200
    assert sign_resp.json()["esignature_manifest_id"].startswith("sig_sig_")

    # 7. POST /api/v1/safety/periodic-reports/compile
    comp_resp = client.post(
        "/api/v1/safety/periodic-reports/compile",
        json={
            "report_type": "DSUR",
            "study_id": "STUDY-API-TEST",
            "compound_name": "CAD-API",
            "reporting_period_start": "2026-01-01",
            "reporting_period_end": "2026-12-31",
            "cases": [
                {
                    "case_id": "C-1",
                    "subject_id": "S-1",
                    "study_id": "STUDY-API-TEST",
                    "treatment_arm": "CAD-API",
                    "meddra_pt": "Renal failure",
                    "meddra_soc": "Renal and urinary disorders",
                    "onset_date": "2026-05-01",
                    "seriousness_hospitalization": True,
                    "causality": "RELATED",
                    "expectedness": "UNEXPECTED",
                }
            ],
        },
        headers=headers,
    )
    assert comp_resp.status_code == 201
    report_data = comp_resp.json()
    report_id = report_data["id"]

    # 8. GET /api/v1/safety/periodic-reports
    list_rep = client.get(
        "/api/v1/safety/periodic-reports?study_id=STUDY-API-TEST", headers=headers
    )
    assert list_rep.status_code == 200
    assert len(list_rep.json()) >= 1

    # 9. POST /api/v1/safety/periodic-reports/{id}/sign
    rep_sig_path = f"/api/v1/safety/periodic-reports/{report_id}/sign"
    rep_sig_token = make_sig_token("safety_lead_01", rep_sig_path)
    sign_rep_headers = create_test_auth_headers(
        user_id="safety_lead_01",
        roles=["safety_officer"],
        change_reason="Automated Periodic Report Sign",
        sig_token=rep_sig_token,
    )
    sign_rep_resp = client.post(
        rep_sig_path,
        json={
            "user_id": "safety_lead_01",
            "signature_secret": rep_sig_token,
            "meaning_of_signature": "Approval of ICH E2F DSUR / ICH E2C(R2) Periodic Safety Report",
        },
        headers=sign_rep_headers,
    )
    assert sign_rep_resp.status_code == 200
    assert sign_rep_resp.json()["status"] == "APPROVED"

    # 10. GET /api/v1/safety/periodic-reports/{id}/export (HTML)
    export_resp = client.get(
        f"/api/v1/safety/periodic-reports/{report_id}/export", headers=headers
    )
    assert export_resp.status_code == 200
    assert "text/html" in export_resp.headers["content-type"]
    assert "Periodic Regulatory Safety Report: DSUR" in export_resp.text
