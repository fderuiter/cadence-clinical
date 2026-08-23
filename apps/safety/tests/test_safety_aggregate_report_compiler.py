"""Comprehensive unit and integration tests for Automated Aggregate Safety Report Compiler (DSUR / PSUR / PBRER) & Exposure Calculator.

Requirements:
@req:PRD-PVG-004
@req:PRD-PVG-008
@req:PRD-AGG-001
@req:PRD-AGG-002
@req:PRD-AGG-003
@req:PRD-SYS-125
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
    ReferenceSafetyInformationConfig,
    ReferenceSafetyInformationTerm,
)
from apps.safety.main import app
from apps.safety.models import (
    Base,
    PeriodicSafetyReportRecord,
    SafetyAuditLog,
)
from apps.safety.services.periodic_report_compiler import (
    PeriodicSafetyReportCompiler,
    RegulatorySnapshotManager,
    SubjectExposureCalculator,
)
from apps.safety.services.signal_alert_service import (
    SafetySignalAlertService,
)
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("safety_aggregate_compiler")


@pytest_asyncio.fixture(autouse=True)
async def setup_aggregate_db():
    """Setup isolated in-memory database for aggregate safety compiler tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_aggregate_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
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


def make_qppv_token(user_id: str, action: str = "") -> str:
    """Creates a mock 21 CFR Part 11 QPPV signature token JWT."""
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    claims = {
        "sub": user_id,
        "action": action,
        "roles": ["qppv", "safety_physician"],
        "iat": int(datetime.now(UTC).timestamp()),
        "exp": int(datetime.now(UTC).timestamp()) + 3600,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(claims, secret, algorithm="HS256")


# =========================================================================
# 1. Subject Exposure Calculator Tests (ICH E2F § 5, PRD-AGG-001)
# =========================================================================


def test_subject_exposure_calculator_patient_years_and_stratifications():
    """Validate subject exposure calculation for cumulative & interval patient-years stratified by arm, dosage, age, and sex.

    @req:PRD-AGG-002
    @req:PRD-PVG-004
    @req:ADR-2199
    """
    subjects_data = [
        # Subject 1: Full reporting period 2026-01-01 to 2026-12-31 (365 days)
        {
            "subject_id": "SUB-001",
            "treatment_arm": "Active 50mg",
            "dosage_cohort": "High Dose 50mg",
            "age": 45,
            "sex": "FEMALE",
            "first_dose_date": "2026-01-01",
            "last_dose_date": "2026-12-31",
        },
        # Subject 2: Half reporting period 2026-01-01 to 2026-06-30 (181 days)
        {
            "subject_id": "SUB-002",
            "treatment_arm": "Active 10mg",
            "dosage_cohort": "Low Dose 10mg",
            "age": 72,
            "sex": "MALE",
            "first_dose_date": "2026-01-01",
            "last_dose_date": "2026-06-30",
        },
        # Subject 3: Pediatric in Placebo arm (90 days)
        {
            "subject_id": "SUB-003",
            "treatment_arm": "Placebo",
            "dosage_cohort": "Placebo",
            "age": 14,
            "sex": "FEMALE",
            "first_dose_date": "2026-03-01",
            "last_dose_date": "2026-05-29",
        },
        # Subject 4: Historical patient dosed previous year (2025-01-01 to 2025-12-31), 0 interval days in 2026
        {
            "subject_id": "SUB-004",
            "treatment_arm": "Active 50mg",
            "dosage_cohort": "High Dose 50mg",
            "age": 60,
            "sex": "MALE",
            "first_dose_date": "2025-01-01",
            "last_dose_date": "2025-12-31",
        },
    ]

    metrics, records = SubjectExposureCalculator.calculate_exposure(
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        subjects_data=subjects_data,
    )

    assert len(records) == 4
    assert metrics.total_cumulative_subjects == 4
    assert metrics.total_interval_subjects == 3  # SUB-004 had 0 days in 2026 interval

    # Verify cumulative vs interval patient-years
    sub1 = next(r for r in records if r.subject_id == "SUB-001")
    assert sub1.cumulative_exposure_days == 365.0
    assert sub1.interval_exposure_days == 365.0
    assert abs(sub1.interval_patient_years - (365.0 / 365.25)) < 0.01

    sub4 = next(r for r in records if r.subject_id == "SUB-004")
    assert sub4.cumulative_exposure_days == 365.0
    assert sub4.interval_exposure_days == 0.0
    assert sub4.interval_patient_years == 0.0
    assert sub4.cumulative_patient_years > 0.99

    # Verify Stratification by Treatment Arm
    arms_map = {row.group_name: row for row in metrics.by_treatment_arm}
    assert "Active 50mg" in arms_map
    assert "Active 10mg" in arms_map
    assert "Placebo" in arms_map
    assert arms_map["Active 50mg"].subject_count == 2
    assert arms_map["Active 10mg"].subject_count == 1
    assert arms_map["Placebo"].subject_count == 1

    # Verify Stratification by Age Group
    age_map = {row.group_name: row for row in metrics.by_age_group}
    assert "< 18 (Pediatric)" in age_map
    assert "18-64 (Adult)" in age_map
    assert ">= 65 (Elderly)" in age_map
    assert age_map["< 18 (Pediatric)"].subject_count == 1
    assert age_map["18-64 (Adult)"].subject_count == 2
    assert age_map[">= 65 (Elderly)"].subject_count == 1

    # Verify Stratification by Sex
    sex_map = {row.group_name: row for row in metrics.by_sex}
    assert sex_map["FEMALE"].subject_count == 2
    assert sex_map["MALE"].subject_count == 2


# =========================================================================
# 2. Immutable Regulatory Snapshot Tests (PRD-AGG-001, 21 CFR Part 11)
# =========================================================================


def test_regulatory_snapshot_cryptographic_seal_and_reproducibility():
    """Validate that RegulatorySnapshot generates deterministic SHA-256 seal and detects data mutation.

    @req:PRD-AGG-001
    @req:PRD-SYS-001
    @req:ADR-2199
    """
    cases = [
        {
            "case_id": "CASE-101",
            "subject_id": "SUB-101",
            "meddra_pt": "Pneumonia",
            "onset_date": "2026-03-15",
            "seriousness_hospitalization": True,
        },
        {
            "case_id": "CASE-102",
            "subject_id": "SUB-102",
            "meddra_pt": "Pyrexia",
            "onset_date": "2026-04-10",
            "seriousness_hospitalization": False,
        },
    ]
    subjects = [
        {"subject_id": "SUB-101", "treatment_arm": "Drug A"},
        {"subject_id": "SUB-102", "treatment_arm": "Placebo"},
    ]

    snapshot1 = RegulatorySnapshotManager.create_snapshot(
        study_id="STUDY-DSUR-TEST",
        compound_name="CADENCE-X",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        dibd_anniversary="2026-06-01",
        raw_cases=cases,
        subjects_data=subjects,
        user_id="qppv.officer",
    )

    assert snapshot1.is_locked is True
    assert snapshot1.snapshot_hash is not None
    assert len(snapshot1.snapshot_hash) == 64
    assert snapshot1.total_cases_frozen == 2
    assert snapshot1.total_subjects_frozen == 2
    assert snapshot1.dibd_anniversary == "2026-06-01"

    # 100% Bitwise Reproducibility Test: Re-running snapshot creation on identical data yields identical SHA-256
    snapshot2 = RegulatorySnapshotManager.create_snapshot(
        study_id="STUDY-DSUR-TEST",
        compound_name="CADENCE-X",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        dibd_anniversary="2026-06-01",
        raw_cases=cases,
        subjects_data=subjects,
        user_id="another.officer",
    )
    assert snapshot1.snapshot_hash == snapshot2.snapshot_hash

    # Verification: Valid dataset passes
    is_valid = RegulatorySnapshotManager.verify_snapshot(
        expected_hash=snapshot1.snapshot_hash,
        cases=cases,
        subjects=subjects,
        study_id="STUDY-DSUR-TEST",
        compound_name="CADENCE-X",
        period_start="2026-01-01",
        period_end="2026-12-31",
        dibd="2026-06-01",
    )
    assert is_valid is True

    # Tamper Detection: Mutated dataset produces mismatch
    tampered_cases = [
        {
            "case_id": "CASE-101",
            "subject_id": "SUB-101",
            "meddra_pt": "Severe Pneumonia",  # Mutated term
            "onset_date": "2026-03-15",
            "seriousness_hospitalization": True,
        }
    ]
    is_tampered_valid = RegulatorySnapshotManager.verify_snapshot(
        expected_hash=snapshot1.snapshot_hash,
        cases=tampered_cases,
        subjects=subjects,
        study_id="STUDY-DSUR-TEST",
        compound_name="CADENCE-X",
        period_start="2026-01-01",
        period_end="2026-12-31",
        dibd="2026-06-01",
    )
    assert is_tampered_valid is False


# =========================================================================
# 3. Summary Tabulations & Reference Safety Information (RSI) Matrix
# =========================================================================


def test_periodic_report_compiler_summary_tabulations_and_rsi_breach():
    """Validate cross-tabulations, SAE/SUSAR/AESI categorization, and Reference Safety Information (RSI) comparison.

    @req:PRD-AGG-003
    @req:PRD-PVG-004
    @req:ADR-2199
    """
    rsi_config = ReferenceSafetyInformationConfig(
        rsi_document_id="IB-CADENCE-ED7",
        rsi_version="7.0",
        effective_date="2026-01-01",
        listed_terms=[
            ReferenceSafetyInformationTerm(
                meddra_pt="Nausea",
                meddra_soc="Gastrointestinal disorders",
                expected_frequency_rate=0.20,  # 20% expected
            ),
            ReferenceSafetyInformationTerm(
                meddra_pt="Headache",
                meddra_soc="Nervous system disorders",
                expected_frequency_rate=0.10,  # 10% expected
            ),
        ],
    )

    cases = [
        # Case 1: Fatal event in Active Arm (Unlisted term -> RSI breach)
        {
            "case_id": "CASE-F1",
            "subject_id": "SUB-1",
            "treatment_arm": "CAD-101 Active",
            "meddra_pt": "Acute hepatic failure",
            "meddra_soc": "Hepatobiliary disorders",
            "onset_date": "2026-02-10",
            "seriousness_death": True,
            "outcome": "FATAL",
            "causality": "RELATED",
            "expectedness": "UNEXPECTED",
        },
        # Case 2: SUSAR (Serious, Related, Unexpected)
        {
            "case_id": "CASE-S1",
            "subject_id": "SUB-2",
            "treatment_arm": "CAD-101 Active",
            "meddra_pt": "Anaphylactic reaction",
            "meddra_soc": "Immune system disorders",
            "onset_date": "2026-03-01",
            "seriousness_life_threatening": True,
            "outcome": "RECOVERED",
            "causality": "RELATED",
            "expectedness": "UNEXPECTED",
        },
        # Case 3: Listed term (Nausea) within expected rate
        {
            "case_id": "CASE-L1",
            "subject_id": "SUB-3",
            "treatment_arm": "CAD-101 Active",
            "meddra_pt": "Nausea",
            "meddra_soc": "Gastrointestinal disorders",
            "onset_date": "2026-04-15",
            "seriousness_hospitalization": False,
            "causality": "RELATED",
            "expectedness": "EXPECTED",
            "outcome": "RECOVERED",
        },
        # Case 4: Adverse Event of Special Interest (AESI: QT Prolongation)
        {
            "case_id": "CASE-A1",
            "subject_id": "SUB-4",
            "treatment_arm": "CAD-101 Active",
            "meddra_pt": "Electrocardiogram QT prolonged",
            "meddra_soc": "Investigations",
            "onset_date": "2026-05-20",
            "seriousness_hospitalization": True,
            "causality": "POSSIBLY_RELATED",
            "expectedness": "UNEXPECTED",
            "outcome": "RECOVERED",
            "is_aesi": True,
        },
        # Case 5: Placebo comparator case
        {
            "case_id": "CASE-P1",
            "subject_id": "SUB-5",
            "treatment_arm": "Placebo Control",
            "meddra_pt": "Headache",
            "meddra_soc": "Nervous system disorders",
            "onset_date": "2026-06-01",
            "seriousness_hospitalization": False,
            "outcome": "RECOVERED",
        },
    ]

    req = PeriodicReportCompilationRequest(
        report_type=PeriodicSafetyReportType.DSUR,
        study_id="STUDY-CAD-301",
        compound_name="CAD-101",
        reporting_period_start="2026-01-01",
        reporting_period_end="2026-12-31",
        dibd_anniversary="2026-05-15",
        cumulative_exposure_subjects=100,
        rsi_config=rsi_config,
        aesi_terms=["QT prolonged", "hepatic failure"],
    )

    payload = PeriodicSafetyReportCompiler.compile_report(req, cases)

    assert payload.report_type == PeriodicSafetyReportType.DSUR
    assert (
        payload.total_saes == 3
    )  # Fatal (Case 1), Anaphylaxis (Case 2), QT prolonged (Case 4)
    assert payload.total_fatal_cases == 1
    assert (
        payload.total_susars == 3
    )  # Case 1 (Hepatic failure), Case 2 (Anaphylaxis), Case 4 (QT prolonged)
    assert payload.total_aesi >= 2  # QT prolonged and hepatic failure

    # Verify Summary Tabulations rows
    tab_map = {r.meddra_pt: r for r in payload.summary_tabulations}
    assert "Acute hepatic failure" in tab_map
    assert "Anaphylactic reaction" in tab_map
    assert "Nausea" in tab_map
    assert "Electrocardiogram QT prolonged" in tab_map

    # Check RSI Comparison properties
    hep_row = tab_map["Acute hepatic failure"]
    assert hep_row.is_fatal is True
    assert hep_row.is_susar is True
    assert hep_row.rsi_listed is False
    assert hep_row.rsi_threshold_breached is True  # Unlisted serious fatal event

    nausea_row = tab_map["Nausea"]
    assert nausea_row.rsi_listed is True
    assert nausea_row.rsi_expected_rate == 0.20
    assert nausea_row.rsi_threshold_breached is False

    qt_row = tab_map["Electrocardiogram QT prolonged"]
    assert qt_row.is_aesi is True

    # Check XML Regulatory Submission Package Generation
    assert payload.xml_submission_package is not None
    assert "<ich_e2f_dsur" in payload.xml_submission_package
    assert "<snapshot_hash>" in payload.xml_submission_package
    assert "<stratification_by_arm>" in payload.xml_submission_package


# =========================================================================
# 4. Executive QPPV Sign-Off Workflow Tests (PRD-AGG-001, PRD-PVG-008)
# =========================================================================


@pytest.mark.asyncio
async def test_executive_qppv_signoff_workflow_and_role_gating():
    """Validate 21 CFR Part 11 gated QPPV electronic signature on aggregate safety report.

    @req:PRD-AGG-003
    @req:PRD-PVG-008
    @req:PRD-SYS-001
    @req:PRD-SYS-125
    @req:ADR-2199
    """
    async with db_manager.session_maker() as session:
        service = SafetySignalAlertService(session=session)

        req = PeriodicReportCompilationRequest(
            report_type=PeriodicSafetyReportType.DSUR,
            study_id="STUDY-QPPV-901",
            compound_name="CAD-AGG",
            reporting_period_start="2026-01-01",
            reporting_period_end="2026-12-31",
            dibd_anniversary="2026-03-01",
            cases=[
                {
                    "case_id": "CASE-Q1",
                    "subject_id": "SUB-Q1",
                    "meddra_pt": "Pyrexia",
                    "meddra_soc": "General disorders",
                    "onset_date": "2026-06-15",
                    "seriousness_hospitalization": True,
                    "causality": "RELATED",
                    "expectedness": "UNEXPECTED",
                }
            ],
        )

        report_dto = await service.compile_periodic_report(
            request=req,
            user_id="compiler.user",
            reason_for_change="Compile DSUR for QPPV Approval",
        )
        report_id = report_dto.id
        assert report_dto.status == PeriodicReportStatus.DRAFT.value

        # Negative Test: Unauthorized role (e.g. site_crc) cannot sign periodic safety report
        with pytest.raises(ValueError, match="is not authorized to sign"):
            await service.sign_periodic_report(
                report_id=report_id,
                user_id="crc.user",
                user_roles=["site_crc"],
                reason_for_change="Attempt unauthorized sign",
                signature_secret="secret-key-123",
                meaning_of_signature="Approval",
            )

        # Positive Test: Qualified Person for Pharmacovigilance (QPPV) signs and locks report
        sign_response = await service.sign_periodic_report(
            report_id=report_id,
            user_id="qppv.lead.officer",
            user_roles=["qppv", "safety_physician"],
            reason_for_change="21 CFR Part 11 Executive QPPV Regulatory Sign-Off",
            signature_secret="qppv-secret-key-999",
            meaning_of_signature="Executive QPPV Approval of Aggregate Safety Report for Regulatory Submission",
        )

        assert sign_response.status == PeriodicReportStatus.APPROVED
        assert sign_response.approved_by_user_id == "qppv.lead.officer"
        assert sign_response.esignature_manifest_id.startswith("sig_rep_")
        assert sign_response.qppv_manifest is not None
        assert sign_response.qppv_manifest.verification_status == "VALID"
        assert len(sign_response.qppv_manifest.snapshot_hash) == 64

        # Verify Database Record and Audit Log
        stmt = select(PeriodicSafetyReportRecord).where(
            PeriodicSafetyReportRecord.id == report_id
        )
        res = await session.execute(stmt)
        record = res.scalars().first()
        assert record is not None
        assert record.status == PeriodicReportStatus.APPROVED.value
        assert record.approved_by_user_id == "qppv.lead.officer"
        assert "qppv_signoff" in record.report_payload

        # Check Part 11 Audit Trail
        audit_stmt = select(SafetyAuditLog).where(
            SafetyAuditLog.record_id == report_id,
            SafetyAuditLog.action == "PERIODIC_SAFETY_REPORT_SIGNED",
        )
        audit_res = await session.execute(audit_stmt)
        audit_entry = audit_res.scalars().first()
        assert audit_entry is not None
        assert audit_entry.created_by == "qppv.lead.officer"


# =========================================================================
# 5. REST API Integration Tests (PRD-AGG-001, PRD-SYS-125)
# =========================================================================


def test_fastapi_rest_aggregate_safety_compiler_and_exposure_endpoints():
    """End-to-end integration test of FastAPI REST endpoints for exposure calculator, snapshot locking, compilation, QPPV sign-off, and XML export.

    @req:PRD-AGG-001
    @req:PRD-AGG-002
    @req:PRD-AGG-003
    @req:PRD-SYS-125
    @req:PRD-PVG-004
    @req:PRD-PVG-008
    @req:ADR-2199
    """
    client = TestClient(app)
    headers = create_test_auth_headers(
        user_id="qppv.test.user",
        roles=["qppv", "safety_officer"],
        change_reason="Automated Aggregate PV REST Test",
    )

    # 1. POST /api/v1/safety/exposure/calculate
    exp_resp = client.post(
        "/api/v1/safety/exposure/calculate",
        json={
            "study_id": "STUDY-EXP-01",
            "reporting_period_start": "2026-01-01",
            "reporting_period_end": "2026-12-31",
            "subjects_exposure_data": [
                {
                    "subject_id": "SUB-E1",
                    "treatment_arm": "Active Arm",
                    "dosage_cohort": "100mg",
                    "age": 52,
                    "sex": "FEMALE",
                    "first_dose_date": "2026-01-01",
                    "last_dose_date": "2026-12-31",
                },
                {
                    "subject_id": "SUB-E2",
                    "treatment_arm": "Placebo Arm",
                    "dosage_cohort": "Placebo",
                    "age": 34,
                    "sex": "MALE",
                    "first_dose_date": "2026-06-01",
                    "last_dose_date": "2026-12-31",
                },
            ],
        },
        headers=headers,
    )
    assert exp_resp.status_code == 200
    exp_data = exp_resp.json()
    assert exp_data["study_id"] == "STUDY-EXP-01"
    assert exp_data["metrics"]["total_cumulative_subjects"] == 2
    assert exp_data["metrics"]["total_cumulative_patient_years"] > 1.0

    # 2. POST /api/v1/safety/snapshots/lock
    snap_resp = client.post(
        "/api/v1/safety/snapshots/lock",
        json={
            "study_id": "STUDY-EXP-01",
            "compound_name": "CAD-REST",
            "reporting_period_start": "2026-01-01",
            "reporting_period_end": "2026-12-31",
            "dibd_anniversary": "2026-08-01",
            "lock_reason": "Annual Regulatory Submission Snapshot Lock",
            "cases": [
                {
                    "case_id": "C-101",
                    "subject_id": "SUB-E1",
                    "meddra_pt": "Hypotension",
                }
            ],
        },
        headers=headers,
    )
    assert snap_resp.status_code == 201
    snap_data = snap_resp.json()["snapshot"]
    snapshot_id = snap_data["snapshot_id"]
    snapshot_hash = snap_data["snapshot_hash"]
    assert snapshot_id.startswith("snap_")
    assert len(snapshot_hash) == 64

    # 3. POST /api/v1/safety/snapshots/verify
    verify_resp = client.post(
        "/api/v1/safety/snapshots/verify",
        json={
            "snapshot_id": snapshot_id,
            "snapshot_hash": snapshot_hash,
            "dataset_manifest": snap_data["dataset_manifest"],
        },
        headers=headers,
    )
    assert verify_resp.status_code == 200
    assert verify_resp.json()["is_valid"] is True
    assert verify_resp.json()["match_status"] == "VERIFIED_MATCH"

    # 4. POST /api/v1/safety/periodic-reports/compile
    comp_resp = client.post(
        "/api/v1/safety/periodic-reports/compile",
        json={
            "report_type": "DSUR",
            "study_id": "STUDY-EXP-01",
            "compound_name": "CAD-REST",
            "reporting_period_start": "2026-01-01",
            "reporting_period_end": "2026-12-31",
            "dibd_anniversary": "2026-08-01",
            "cases": [
                {
                    "case_id": "C-101",
                    "subject_id": "SUB-E1",
                    "treatment_arm": "Active Arm",
                    "meddra_pt": "Hypotension",
                    "meddra_soc": "Vascular disorders",
                    "onset_date": "2026-04-01",
                    "seriousness_hospitalization": True,
                    "causality": "RELATED",
                    "expectedness": "UNEXPECTED",
                }
            ],
        },
        headers=headers,
    )
    assert comp_resp.status_code == 201
    rep_data = comp_resp.json()
    report_id = rep_data["id"]
    assert rep_data["report_type"] == "DSUR"
    assert rep_data["snapshot_hash"] is not None

    # 5. GET /api/v1/safety/periodic-reports/{id}
    get_rep = client.get(
        f"/api/v1/safety/periodic-reports/{report_id}", headers=headers
    )
    assert get_rep.status_code == 200
    assert get_rep.json()["id"] == report_id

    # 6. POST /api/v1/safety/periodic-reports/{id}/qppv-sign
    qppv_token = make_qppv_token(
        "qppv.test.user", f"/api/v1/safety/periodic-reports/{report_id}/qppv-sign"
    )
    qppv_headers = create_test_auth_headers(
        user_id="qppv.test.user",
        roles=["qppv"],
        change_reason="QPPV Regulatory Approval",
        sig_token=qppv_token,
    )
    qppv_sign_resp = client.post(
        f"/api/v1/safety/periodic-reports/{report_id}/qppv-sign",
        json={
            "user_id": "qppv.test.user",
            "signature_secret": qppv_token,
            "role": "qppv",
            "meaning_of_signature": "Executive QPPV Approval for EMA/FDA Submission",
        },
        headers=qppv_headers,
    )
    assert qppv_sign_resp.status_code == 200
    sign_json = qppv_sign_resp.json()
    assert sign_json["status"] == "APPROVED"
    assert sign_json["qppv_manifest"]["verification_status"] == "VALID"

    # 7. GET /api/v1/safety/periodic-reports/{id}/export?format=xml
    xml_resp = client.get(
        f"/api/v1/safety/periodic-reports/{report_id}/export?format=xml",
        headers=headers,
    )
    assert xml_resp.status_code == 200
    assert "application/xml" in xml_resp.headers["content-type"]
    assert "<ich_e2f_dsur" in xml_resp.text
    assert "<qppv_signoff>" in xml_resp.text

    # 8. GET /api/v1/safety/periodic-reports/{id}/export?format=html
    html_resp = client.get(
        f"/api/v1/safety/periodic-reports/{report_id}/export?format=html",
        headers=headers,
    )
    assert html_resp.status_code == 200
    assert "text/html" in html_resp.headers["content-type"]
    assert "Periodic Regulatory Safety Report: DSUR" in html_resp.text
    assert "Executive QPPV Electronic Signature Manifest" in html_resp.text
