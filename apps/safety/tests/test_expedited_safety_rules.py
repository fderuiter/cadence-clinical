"""Comprehensive test suite for Deterministic Multi-Country Expedited Safety Reporting Rules Engine.

Requirements: PRD-PVG-001, PRD-PVG-008, ADR-2199
"""

import os
import time
import uuid
from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from fastapi import HTTPException
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select, text

from apps.safety.database import db_manager
from apps.safety.domain.expedited_rules import (
    CausalityCategory,
    ClockAlertLevel,
    ExpectednessCategory,
    ExpeditedClassification,
    ICSRApprovalStatus,
    ICSRRuleEvaluationInput,
    ReportingAuthority,
)
from apps.safety.main import app
from apps.safety.models import (
    Base,
    SafetyAuditLog,
)
from apps.safety.services.expedited_rules_engine import (
    ExpeditedSafetyReportingRulesEngine,
)
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("safety_expedited_rules")


@pytest_asyncio.fixture(autouse=True)
async def setup_rules_db():
    """Setup isolated in-memory database for expedited safety rules tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_rules_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
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
    secret = os.getenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    )  # pragma: allowlist secret
    payload = {
        "sub": user_id,
        "action": action,
        "exp": int(time.time()) + 300,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(payload, secret, algorithm="HS256")


def get_signed_headers(
    roles: str = "sponsor_safety_physician",
    user_id: str = "dr_medical_monitor_01",
    change_reason: str = "Expedited Safety Rules Action",
    sig_token: str | None = None,
) -> dict[str, str]:
    """Helper to generate internal Gateway HMAC authentication headers."""
    roles_list = [r.strip() for r in roles.split(",") if r.strip()]
    return create_test_auth_headers(
        user_id=user_id,
        roles=roles_list,
        change_reason=change_reason,
        sig_token=sig_token,
    )


# =========================================================================
# Unit Tests: Rules Engine & Countdown Math (PRD-PVG-001)
# =========================================================================


def test_fda_7_day_fatal_susar_evaluation():
    """Validate US FDA 7-day fatal/life-threatening SUSAR expedited reporting determination.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = "2026-08-01T09:00:00Z"
    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-2026-001",
        study_id="STUDY-ONCO-101",
        subject_id="SUBJ-1001",
        receipt_date=receipt_date,
        seriousness_death=True,
        seriousness_life_threatening=False,
        causality=CausalityCategory.SUSPECTED,
        expectedness=ExpectednessCategory.UNEXPECTED,
        target_authorities=[ReportingAuthority.US_FDA],
    )

    as_of = datetime.fromisoformat("2026-08-02T09:00:00+00:00")
    results = engine.evaluate(input_data, as_of=as_of)

    assert len(results) == 1
    res = results[0]
    assert res.authority == ReportingAuthority.US_FDA
    assert res.country_code == "US"
    assert res.is_expedited is True
    assert res.timeframe_days == 7
    assert (
        res.classification == ExpeditedClassification.SUSAR_FATAL_LIFE_THREATENING_7_DAY
    )
    assert "21 CFR 312.32(c)(1)(i)" in res.statutory_citation
    assert res.countdown is not None
    assert res.countdown.total_hours == 168.0
    assert res.countdown.elapsed_hours == 24.0
    assert res.countdown.elapsed_percentage == 14.29
    assert res.countdown.remaining_hours == 144.0
    assert res.countdown.is_overdue is False
    assert res.countdown.alert_level == ClockAlertLevel.NORMAL


def test_fda_15_day_serious_unexpected_susar_evaluation():
    """Validate US FDA 15-day other serious unexpected SUSAR expedited reporting determination.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = "2026-08-01T00:00:00Z"
    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-2026-002",
        study_id="STUDY-CARD-202",
        subject_id="SUBJ-2002",
        receipt_date=receipt_date,
        seriousness_death=False,
        seriousness_life_threatening=False,
        seriousness_hospitalization=True,
        causality=CausalityCategory.RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
        target_authorities=[ReportingAuthority.US_FDA],
    )

    as_of = datetime.fromisoformat("2026-08-01T00:00:00+00:00")
    results = engine.evaluate(input_data, as_of=as_of)

    assert len(results) == 1
    res = results[0]
    assert res.is_expedited is True
    assert res.timeframe_days == 15
    assert res.classification == ExpeditedClassification.SUSAR_SERIOUS_UNEXPECTED_15_DAY
    assert "21 CFR 312.32(c)(1)(ii)" in res.statutory_citation
    assert res.countdown is not None
    assert res.countdown.total_hours == 360.0


def test_ema_eudravigilance_rules_evaluation():
    """Validate EMA 7-day and 15-day EudraVigilance SUSAR rules.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = "2026-08-10T12:00:00Z"

    # 1. 7-day Fatal SUSAR
    fatal_input = ICSRRuleEvaluationInput(
        case_id="ICSR-EMA-001",
        study_id="STUDY-EU-01",
        subject_id="SUBJ-EU-01",
        receipt_date=receipt_date,
        seriousness_death=True,
        causality=CausalityCategory.DEFINITELY_RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
        target_authorities=[ReportingAuthority.EMA],
    )
    res_fatal = engine.evaluate(fatal_input)[0]
    assert res_fatal.authority == ReportingAuthority.EMA
    assert res_fatal.country_code == "EU"
    assert res_fatal.is_expedited is True
    assert res_fatal.timeframe_days == 7
    assert "536/2014 Art. 40(1)" in res_fatal.statutory_citation

    # 2. 15-day Non-fatal SUSAR
    nonfatal_input = ICSRRuleEvaluationInput(
        case_id="ICSR-EMA-002",
        study_id="STUDY-EU-01",
        subject_id="SUBJ-EU-02",
        receipt_date=receipt_date,
        seriousness_disability=True,
        causality=CausalityCategory.POSSIBLY_RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
        target_authorities=[ReportingAuthority.EMA],
    )
    res_nonfatal = engine.evaluate(nonfatal_input)[0]
    assert res_nonfatal.is_expedited is True
    assert res_nonfatal.timeframe_days == 15
    assert "536/2014 Art. 40(2)" in res_nonfatal.statutory_citation


def test_pmda_mhra_health_canada_multi_country_evaluation():
    """Validate PMDA Japan, MHRA UK, and Health Canada country rules evaluation.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = "2026-08-15T00:00:00Z"
    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-MULTI-001",
        study_id="STUDY-GLOBAL-99",
        subject_id="SUBJ-GL-01",
        receipt_date=receipt_date,
        seriousness_life_threatening=True,
        causality=CausalityCategory.PROBABLY_RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
        target_authorities=[
            ReportingAuthority.PMDA,
            ReportingAuthority.MHRA,
            ReportingAuthority.HEALTH_CANADA,
        ],
    )

    results = engine.evaluate(input_data)
    assert len(results) == 3

    pmda_res = next(r for r in results if r.authority == ReportingAuthority.PMDA)
    assert pmda_res.country_code == "JP"
    assert pmda_res.is_expedited is True
    assert pmda_res.timeframe_days == 7
    assert "PMD Act" in pmda_res.statutory_citation

    mhra_res = next(r for r in results if r.authority == ReportingAuthority.MHRA)
    assert mhra_res.country_code == "GB"
    assert mhra_res.is_expedited is True
    assert mhra_res.timeframe_days == 7
    assert "SI 2004/1031" in mhra_res.statutory_citation

    hc_res = next(r for r in results if r.authority == ReportingAuthority.HEALTH_CANADA)
    assert hc_res.country_code == "CA"
    assert hc_res.is_expedited is True
    assert hc_res.timeframe_days == 7
    assert "Food and Drug Regulations" in hc_res.statutory_citation


def test_non_expedited_periodic_determination():
    """Validate expected, non-serious, or non-causal adverse events are classified as non-expedited.

    @req:PRD-PVG-001
    @req:PRD-SYS-167
    @req:ADR-2199
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = "2026-08-01T00:00:00Z"

    # Case 1: Expected SAE (Listed in Investigator Brochure / RSI)
    input_expected = ICSRRuleEvaluationInput(
        case_id="ICSR-EXP-001",
        study_id="STUDY-001",
        subject_id="SUBJ-001",
        receipt_date=receipt_date,
        seriousness_hospitalization=True,
        causality=CausalityCategory.RELATED,
        expectedness=ExpectednessCategory.EXPECTED,
    )
    results_exp = engine.evaluate(input_expected)
    for res in results_exp:
        assert res.is_expedited is False
        assert res.timeframe_days is None
        assert res.due_date is None
        assert res.classification == ExpeditedClassification.NON_EXPEDITED_PERIODIC
        assert res.countdown is None

    # Case 2: Not Related SAE
    input_unrelated = ICSRRuleEvaluationInput(
        case_id="ICSR-UNREL-002",
        study_id="STUDY-001",
        subject_id="SUBJ-002",
        receipt_date=receipt_date,
        seriousness_death=True,
        causality=CausalityCategory.NOT_RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
    )
    results_unrel = engine.evaluate(input_unrelated)
    for res in results_unrel:
        assert res.is_expedited is False
        assert res.classification == ExpeditedClassification.NON_EXPEDITED_PERIODIC

    # Case 3: Non-Serious Event
    input_nonserious = ICSRRuleEvaluationInput(
        case_id="ICSR-NONSER-003",
        study_id="STUDY-001",
        subject_id="SUBJ-003",
        receipt_date=receipt_date,
        seriousness_death=False,
        seriousness_hospitalization=False,
        causality=CausalityCategory.RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
    )
    results_nonser = engine.evaluate(input_nonserious)
    for res in results_nonser:
        assert res.is_expedited is False


def test_real_time_countdown_threshold_alert_escalation():
    """Validate 75% warning, 90% critical, and overdue threshold transitions on statutory clocks.

    @req:PRD-PVG-001
    @req:PRD-SYS-167
    @req:ADR-2199
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    receipt_date = datetime(2026, 8, 1, 0, 0, 0, tzinfo=UTC)
    timeframe_days = 7  # 168 hours total

    # 1. Normal status (< 75% elapsed): 50% elapsed (84 hours)
    as_of_normal = receipt_date + timedelta(hours=84)
    clock_normal = engine.compute_countdown(
        receipt_date, timeframe_days, as_of=as_of_normal
    )
    assert clock_normal.elapsed_percentage == 50.0
    assert clock_normal.alert_level == ClockAlertLevel.NORMAL
    assert clock_normal.is_overdue is False

    # 2. Warning threshold (>= 75% and < 90%): 75% elapsed (126 hours)
    as_of_warning = receipt_date + timedelta(hours=126)
    clock_warning = engine.compute_countdown(
        receipt_date, timeframe_days, as_of=as_of_warning
    )
    assert clock_warning.elapsed_percentage == 75.0
    assert clock_warning.alert_level == ClockAlertLevel.WARNING
    assert clock_warning.is_overdue is False

    # 3. Critical threshold (>= 90% and <= 100%): 92% elapsed (154.56 hours)
    as_of_critical = receipt_date + timedelta(hours=155)
    clock_critical = engine.compute_countdown(
        receipt_date, timeframe_days, as_of=as_of_critical
    )
    assert clock_critical.elapsed_percentage >= 90.0
    assert clock_critical.alert_level == ClockAlertLevel.CRITICAL
    assert clock_critical.is_overdue is False

    # 4. Overdue threshold (> 100% / past due): 8 days elapsed (192 hours)
    as_of_overdue = receipt_date + timedelta(days=8)
    clock_overdue = engine.compute_countdown(
        receipt_date, timeframe_days, as_of=as_of_overdue
    )
    assert clock_overdue.elapsed_percentage > 100.0
    assert clock_overdue.alert_level == ClockAlertLevel.OVERDUE
    assert clock_overdue.is_overdue is True
    assert clock_overdue.remaining_hours == 0.0


# =========================================================================
# Service & 21 CFR Part 11 Electronic Signature Gating Tests (PRD-PVG-008)
# =========================================================================


@pytest.mark.asyncio
async def test_part11_physician_signature_success_and_audit():
    """Validate 21 CFR Part 11 Safety Physician electronic signature on ICSR reporting evaluation.

    @req:PRD-PVG-008
    @req:ADR-2199
    @req:PRD-PVG-001
    @req:PRD-SYS-167
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    session_maker = db_manager.get_session_maker()

    receipt_date = "2026-08-01T00:00:00Z"
    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-SIGN-001",
        study_id="STUDY-SIGN-01",
        subject_id="SUBJ-SIGN-01",
        receipt_date=receipt_date,
        seriousness_death=True,
        causality=CausalityCategory.SUSPECTED,
        expectedness=ExpectednessCategory.UNEXPECTED,
    )

    async with session_maker() as session:
        summary = await engine.evaluate_and_persist(
            session=session,
            input_data=input_data,
            user_id="safety_officer_01",
            reason_for_change="Initial ICSR Intake Evaluation",
        )
        await session.commit()

        assert summary.approval_status == ICSRApprovalStatus.DRAFT
        assert summary.approved_by_user_id is None
        assert summary.esignature_manifest_id is None

        # Sign as qualified Safety Physician
        sign_resp = await engine.sign_evaluation(
            session=session,
            evaluation_id=summary.id,
            user_id="dr_chief_safety_physician",
            user_roles="sponsor_safety_physician,medical_monitor",
            reason_for_change="Medical causality review and regulatory reporting package approval",
            signature_secret="physician-crypto-secret-xyz",
            meaning_of_signature="Approval of Expedited ICSR Regulatory Submission",
        )
        await session.commit()

        assert sign_resp.approval_status == ICSRApprovalStatus.PHYSICIAN_APPROVED
        assert sign_resp.approved_by_user_id == "dr_chief_safety_physician"
        assert sign_resp.esignature_manifest_id.startswith("sig_eval_")

        # Verify persisted state
        updated_summary = await engine.get_evaluation(
            session=session,
            evaluation_id=summary.id,
            user_id="auditor_01",
        )
        assert updated_summary.approval_status == ICSRApprovalStatus.PHYSICIAN_APPROVED
        assert updated_summary.approved_by_user_id == "dr_chief_safety_physician"
        assert updated_summary.version_index == 2

        # Verify audit logs
        stmt_audit = select(SafetyAuditLog).order_by(SafetyAuditLog.created_at.asc())
        audit_res = await session.execute(stmt_audit)
        audit_logs = audit_res.scalars().all()
        actions = [log.action for log in audit_logs]
        assert "EXPEDITED_SAFETY_RULES_EVALUATED" in actions
        assert "EXPEDITED_SAFETY_EVALUATION_SIGNED" in actions
        assert "EXPEDITED_SAFETY_EVALUATION_VIEW" in actions


@pytest.mark.asyncio
async def test_part11_physician_signature_unauthorized_role_rejection():
    """Validate rejection of unauthorized roles attempting to sign ICSR packages.

    @req:PRD-PVG-008
    @req:PRD-PVG-001
    @req:PRD-SYS-167
    @req:ADR-2199
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    session_maker = db_manager.get_session_maker()

    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-UNAUTH-001",
        study_id="STUDY-001",
        subject_id="SUBJ-001",
        receipt_date="2026-08-01T00:00:00Z",
        seriousness_hospitalization=True,
        causality=CausalityCategory.RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
    )

    async with session_maker() as session:
        summary = await engine.evaluate_and_persist(
            session=session,
            input_data=input_data,
            user_id="user_crc",
            reason_for_change="Intake",
        )
        await session.commit()

        # Attempt to sign with site_crc role (Unauthorized)
        with pytest.raises(HTTPException) as exc_info:
            await engine.sign_evaluation(
                session=session,
                evaluation_id=summary.id,
                user_id="crc_user_01",
                user_roles="site_crc,data_manager",
                reason_for_change="Approve report",
            )
        assert exc_info.value.status_code == 403
        assert "not authorized" in str(exc_info.value.detail)


@pytest.mark.asyncio
async def test_part11_physician_signature_missing_reason_rejection():
    """Validate rejection when reason_for_change is missing or empty.

    @req:PRD-PVG-008
    @req:PRD-PVG-001
    @req:PRD-SYS-167
    @req:ADR-2199
    """
    engine = ExpeditedSafetyReportingRulesEngine()
    session_maker = db_manager.get_session_maker()

    input_data = ICSRRuleEvaluationInput(
        case_id="ICSR-NOREASON-001",
        study_id="STUDY-001",
        subject_id="SUBJ-001",
        receipt_date="2026-08-01T00:00:00Z",
        seriousness_hospitalization=True,
        causality=CausalityCategory.RELATED,
        expectedness=ExpectednessCategory.UNEXPECTED,
    )

    async with session_maker() as session:
        summary = await engine.evaluate_and_persist(
            session=session,
            input_data=input_data,
            user_id="physician_01",
            reason_for_change="Intake",
        )
        await session.commit()

        with pytest.raises(HTTPException) as exc_info:
            await engine.sign_evaluation(
                session=session,
                evaluation_id=summary.id,
                user_id="dr_safety",
                user_roles="safety_physician",
                reason_for_change="   ",
            )
        assert exc_info.value.status_code == 400
        assert "Reason for change is mandatory" in str(exc_info.value.detail)


@pytest.mark.asyncio
async def test_audit_log_immutability_enforcement():
    """Validate 21 CFR Part 11 append-only immutability preventing modification or deletion of SafetyAuditLog.

    @req:PRD-PVG-001
    @req:PRD-PVG-008
    @req:PRD-SYS-167
    @req:ADR-2199
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        log_entry = SafetyAuditLog(
            created_by="system",
            action="EXPEDITED_SAFETY_RULES_TEST",
            details="Test log entry",
            reason_for_change="Test",
            version_index=1,
        )
        session.add(log_entry)
        await session.commit()

        # Attempt modification
        log_entry.details = "Illegal mutated details"
        with pytest.raises(ValueError) as exc_mod:
            await session.flush()
        assert "strictly forbidden" in str(exc_mod.value)
        await session.rollback()

        # Attempt deletion
        res = await session.execute(
            select(SafetyAuditLog).where(
                SafetyAuditLog.action == "EXPEDITED_SAFETY_RULES_TEST"
            )
        )
        existing_log = res.scalars().first()
        assert existing_log is not None
        await session.delete(existing_log)
        with pytest.raises(ValueError) as exc_del:
            await session.flush()
        assert "strictly forbidden" in str(exc_del.value)
        await session.rollback()


# =========================================================================
# Integration Tests: REST API Endpoints (PRD-PVG-001, PRD-PVG-008)
# =========================================================================


def test_api_evaluate_and_get_evaluation():
    """Test POST /api/v1/safety/rules/evaluate and GET /api/v1/safety/rules/evaluations/{id}.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    client = TestClient(app)
    headers = get_signed_headers(
        roles="sponsor_safety_physician",
        user_id="dr_safety_01",
        change_reason="Evaluate ICSR regulatory reportability",
    )

    payload = {
        "case_id": "ICSR-REST-001",
        "study_id": "STUDY-REST-01",
        "subject_id": "SUBJ-REST-01",
        "receipt_date": "2026-08-01T00:00:00Z",
        "seriousness_death": True,
        "seriousness_life_threatening": False,
        "seriousness_hospitalization": False,
        "seriousness_disability": False,
        "seriousness_congenital_anomaly": False,
        "seriousness_other": False,
        "causality": "SUSPECTED",
        "expectedness": "UNEXPECTED",
        "reason_for_change": "Evaluate fatal SUSAR case",
    }

    resp = client.post("/api/v1/safety/rules/evaluate", json=payload, headers=headers)
    assert resp.status_code == 201
    data = resp.json()
    eval_id = data["id"]
    assert data["case_id"] == "ICSR-REST-001"
    assert data["is_susar"] is True
    assert data["any_expedited"] is True
    assert data["shortest_timeframe_days"] == 7
    assert len(data["evaluations"]) == 5  # FDA, EMA, PMDA, MHRA, HC

    # Retrieve specific evaluation
    get_resp = client.get(
        f"/api/v1/safety/rules/evaluations/{eval_id}", headers=headers
    )
    assert get_resp.status_code == 200
    get_data = get_resp.json()
    assert get_data["id"] == eval_id
    assert get_data["case_id"] == "ICSR-REST-001"


def test_api_list_evaluations_and_countdown_alerts():
    """Test GET /api/v1/safety/rules/evaluations and GET /api/v1/safety/rules/countdown-alerts.

    @req:PRD-PVG-001
    @req:ADR-2199
    @req:PRD-SYS-167
    """
    client = TestClient(app)
    headers = get_signed_headers(
        roles="sponsor_medical_monitor",
        user_id="dr_monitor_02",
        change_reason="List evaluations",
    )

    # 1. Create a historical case to simulate overdue / critical clock
    historical_receipt = (datetime.now(UTC) - timedelta(days=10)).isoformat()
    client.post(
        "/api/v1/safety/rules/evaluate",
        json={
            "case_id": "ICSR-ALERT-001",
            "study_id": "STUDY-ALERT-99",
            "subject_id": "SUBJ-ALERT-99",
            "receipt_date": historical_receipt,
            "seriousness_life_threatening": True,
            "causality": "RELATED",
            "expectedness": "UNEXPECTED",
            "reason_for_change": "Overdue case evaluation",
        },
        headers=headers,
    )

    # List all evaluations
    list_resp = client.get(
        "/api/v1/safety/rules/evaluations?study_id=STUDY-ALERT-99",
        headers=headers,
    )
    assert list_resp.status_code == 200
    list_data = list_resp.json()
    assert len(list_data) >= 1
    assert any(e["case_id"] == "ICSR-ALERT-001" for e in list_data)

    # Query countdown alerts
    alerts_resp = client.get(
        "/api/v1/safety/rules/countdown-alerts?study_id=STUDY-ALERT-99",
        headers=headers,
    )
    assert alerts_resp.status_code == 200
    alerts_data = alerts_resp.json()
    assert len(alerts_data) >= 1
    alert_case = next(e for e in alerts_data if e["case_id"] == "ICSR-ALERT-001")
    assert alert_case["overall_alert_level"] == ClockAlertLevel.OVERDUE.value


def test_api_sign_evaluation_gated_workflow():
    """Test POST /api/v1/safety/rules/evaluations/{id}/sign with 21 CFR Part 11 e-signature.

    @req:PRD-PVG-008
    @req:ADR-2199
    @req:PRD-PVG-001
    @req:PRD-SYS-167
    """
    client = TestClient(app)

    # Create draft evaluation
    intake_headers = get_signed_headers(
        roles="sponsor_safety_physician",
        user_id="safety_doc_01",
        change_reason="Evaluate intake",
    )
    eval_resp = client.post(
        "/api/v1/safety/rules/evaluate",
        json={
            "case_id": "ICSR-SIGN-API-01",
            "study_id": "STUDY-SIGN-API",
            "subject_id": "SUBJ-SIGN-API",
            "receipt_date": "2026-08-01T00:00:00Z",
            "seriousness_hospitalization": True,
            "causality": "PROBABLY_RELATED",
            "expectedness": "UNEXPECTED",
            "reason_for_change": "Evaluate hospitalization SUSAR",
        },
        headers=intake_headers,
    )
    assert eval_resp.status_code == 201
    eval_id = eval_resp.json()["id"]

    # 1. Attempt sign as unauthorized CRA Monitor (403 Forbidden)
    cra_headers = get_signed_headers(
        roles="cra_monitor",
        user_id="cra_user_01",
        change_reason="Attempt sign",
        sig_token=make_sig_token(
            "cra_user_01", f"/api/v1/safety/rules/evaluations/{eval_id}/sign"
        ),
    )
    sign_unauth = client.post(
        f"/api/v1/safety/rules/evaluations/{eval_id}/sign",
        json={
            "reason_for_change": "CRA Approval",
        },
        headers=cra_headers,
    )
    assert sign_unauth.status_code == 403

    # 2. Sign as authorized Safety Physician (200 OK)
    physician_headers = get_signed_headers(
        roles="sponsor_safety_physician",
        user_id="dr_safety_approver",
        change_reason="Medical causality review and Part 11 approval",
        sig_token=make_sig_token(
            "dr_safety_approver",
            f"/api/v1/safety/rules/evaluations/{eval_id}/sign",
        ),
    )
    sign_auth = client.post(
        f"/api/v1/safety/rules/evaluations/{eval_id}/sign",
        json={
            "reason_for_change": "Approved 15-Day Expedited Reporting Package per 21 CFR Part 11",
            "signature_secret": "physician-reauth-key-888",
            "meaning_of_signature": "Approval of Expedited ICSR Regulatory Submission",
        },
        headers=physician_headers,
    )
    assert sign_auth.status_code == 200
    sign_data = sign_auth.json()
    assert sign_data["approval_status"] == ICSRApprovalStatus.PHYSICIAN_APPROVED.value
    assert sign_data["approved_by_user_id"] == "dr_safety_approver"
    assert sign_data["esignature_manifest_id"].startswith("sig_eval_")

    # 3. Attempt duplicate sign on already approved evaluation (400 Bad Request)
    dup_headers = get_signed_headers(
        roles="sponsor_safety_physician",
        user_id="dr_safety_approver",
        change_reason="Duplicate sign attempt",
        sig_token=make_sig_token(
            "dr_safety_approver",
            f"/api/v1/safety/rules/evaluations/{eval_id}/sign",
        ),
    )
    dup_sign = client.post(
        f"/api/v1/safety/rules/evaluations/{eval_id}/sign",
        json={
            "reason_for_change": "Duplicate sign attempt",
        },
        headers=dup_headers,
    )
    assert dup_sign.status_code == 400
    assert "already been approved" in dup_sign.json()["detail"]
