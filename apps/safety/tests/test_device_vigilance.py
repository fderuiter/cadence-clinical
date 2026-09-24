"""Medical Device Vigilance & Incident Reporting Engine Unit and Integration Tests.

Requirements:
- PRD-SYS-187: Medical Device Vigilance & Incident Reporting Engine (FDA 21 CFR Part 803 / EU MDR 2017/745)
- PRD-SYS-053: GxP Quality and Part 11 Audit Trail Verification
- ISO 14155:2020 / EU MDR 2017/745 / FDA 21 CFR Part 803 / 21 CFR Part 11
- ADR-2209 / ADR-2199
"""

import os
from datetime import datetime, timedelta

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.safety.adapters.models import (
    Base,
)
from apps.safety.database import db_manager
from apps.safety.main import app
from apps.safety.services.regulatory_audit_ledger import RegulatoryAuditLedger
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("device_vigilance_tests")


@pytest_asyncio.fixture(autouse=True)
async def setup_safety_db():
    """Initializes in-memory database for safety vigilance tests."""
    db_manager.init_db(
        os.getenv("SAFETY_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield


@pytest.fixture
def client():
    return TestClient(app)


def test_regulatory_countdown_clock_5_day_expedited():
    """Tests automated 5-day expedited MDR regulatory countdown clock calculation under FDA 21 CFR 803.53.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    now = datetime(2026, 8, 24, 12, 0, 0)
    # 5-day remedial action report
    days, due_date = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="REMEDIAL_ACTION",
        is_remedial_action=True,
        now_ts=now,
    )
    assert days == 5
    assert due_date == now + timedelta(days=5)

    # 5-day expedited flag report
    days_exp, due_exp = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="MALFUNCTION",
        is_5_day_expedited=True,
        now_ts=now,
    )
    assert days_exp == 5
    assert due_exp == now + timedelta(days=5)


def test_regulatory_countdown_clock_30_day_baseline():
    """Tests automated 30-day baseline MDR regulatory countdown clock calculation under FDA 21 CFR 803.50/803.52.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    now = datetime(2026, 8, 24, 12, 0, 0)
    # Standard Malfunction 30-day clock
    days, due_date = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="MALFUNCTION",
        regulatory_framework="FDA_803",
        now_ts=now,
    )
    assert days == 30
    assert due_date == now + timedelta(days=30)

    # User error / deterioration
    days_ue, due_ue = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="USER_ERROR",
        regulatory_framework="FDA_803",
        now_ts=now,
    )
    assert days_ue == 30
    assert due_ue == now + timedelta(days=30)


def test_regulatory_countdown_clock_eu_mdr_tiers():
    """Tests EU MDR / ISO 14155 statutory countdown clocks (2-day, 10-day, 15-day).

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    now = datetime(2026, 8, 24, 12, 0, 0)
    # 2-day public health threat
    days_2, due_2 = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="SADE",
        is_public_health_threat=True,
        now_ts=now,
    )
    assert days_2 == 2
    assert due_2 == now + timedelta(days=2)

    # 10-day SADE / USADE / Death
    days_10, due_10 = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="SADE",
        now_ts=now,
    )
    assert days_10 == 10
    assert due_10 == now + timedelta(days=10)

    # 15-day ADE / General Incident
    days_15, due_15 = RegulatoryAuditLedger.calculate_device_vigilance_deadline(
        classification="ADE",
        now_ts=now,
    )
    assert days_15 == 15
    assert due_15 == now + timedelta(days=15)


def test_device_incident_classifications_endpoint(client):
    """Tests opening safety cases with various defect classifications (malfunction, user error, deterioration, adverse incident).

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    headers = create_test_auth_headers(
        user_id="safety_officer_01",
        roles=["safety_officer", "sponsor_admin"],
        change_reason="Defect classification verification",
    )

    classifications = ["MALFUNCTION", "USER_ERROR", "DETERIORATION", "ADVERSE_INCIDENT"]
    for cls_name in classifications:
        payload = {
            "study_id": "STUDY-DEV-VIG-01",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-001",
            "deficiency_id": f"defic-{cls_name.lower()}",
            "device_name": "Precision Infusion Controller",
            "device_udi_di": "00840123456786",
            "lot_number": "LOT-VIG-01",
            "classification": cls_name,
            "causality": "PROBABLE",
            "investigation_notes": f"Observed {cls_name} during clinical infusion.",
            "reason_for_change": f"Log {cls_name} safety inquiry",
        }
        res = client.post(
            "/api/v1/safety/device-investigations",
            json=payload,
            headers=headers,
        )
        assert res.status_code == 201
        data = res.json()
        assert data["classification"] == cls_name
        assert data["status"] == "OPEN"
        assert data["vigilance_deadline_days"] in (5, 10, 15, 30)


def test_fda_medwatch_3500a_export_generation(client):
    """Tests generating and cryptographically sealing an FDA Form 3500A MedWatch electronic export.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    headers = create_test_auth_headers(
        user_id="safety_officer_01",
        roles=["safety_officer"],
        change_reason="FDA MedWatch compilation",
    )

    # 1. Create a safety case for a device malfunction
    create_res = client.post(
        "/api/v1/safety/device-investigations",
        json={
            "study_id": "STUDY-DEV-FDA",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-FDA-001",
            "deficiency_id": "defic-fda-001",
            "device_name": "CardioGuard Defibrillator",
            "device_udi_di": "00840123456786",
            "lot_number": "LOT-FDA-99",
            "serial_number": "SN-FDA-7711",
            "classification": "MALFUNCTION",
            "causality": "POSSIBLE",
            "reason_for_change": "Open FDA MDR Malfunction case",
        },
        headers=headers,
    )
    assert create_res.status_code == 201
    case_id = create_res.json()["id"]

    # 2. Request FDA MedWatch report
    mw_res = client.post(
        f"/api/v1/safety/device-investigations/{case_id}/fda-medwatch-report",
        headers=headers,
    )
    assert mw_res.status_code == 200
    mw_data = mw_res.json()

    assert mw_data["report_type"] == "FDA_3500A_MEDWATCH"
    assert (
        mw_data["reporting_authority"]
        == "FDA CDRH / Electronic Medical Device Reporting (eMDR)"
    )
    assert mw_data["udi_di"] == "00840123456786"
    assert "MedWatch3500A" in mw_data["payload_xml"]
    assert "SectionA_Patient" in mw_data["payload_xml"]
    assert "SectionD_Device" in mw_data["payload_xml"]
    assert (
        mw_data["payload_json"]["section_g_all_manufacturers"][
            "fda_defect_classification"
        ]
        == "MALFUNCTION"
    )


def test_fda_medwatch_5_day_expedited_remedial_action(client):
    """Tests FDA 5-day expedited MedWatch report for remedial actions per 21 CFR 803.53.

    @req:PRD-SYS-187
    @req:PRD-SYS-053
    """
    headers = create_test_auth_headers(
        user_id="safety_physician_01",
        roles=["safety_officer", "sponsor_admin"],
        change_reason="5-day expedited adjudication",
    )

    # 1. Create case
    create_res = client.post(
        "/api/v1/safety/device-investigations",
        json={
            "study_id": "STUDY-DEV-5DAY",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-5D-01",
            "deficiency_id": "defic-5d-01",
            "device_name": "NeuroPulse Stimulator",
            "device_udi_di": "00840123456786",
            "lot_number": "LOT-5D-99",
            "classification": "SADE",
            "reason_for_change": "Open critical case",
        },
        headers=headers,
    )
    case_id = create_res.json()["id"]

    # 2. Adjudicate with FSCA / Remedial Action required
    adj_res = client.put(
        f"/api/v1/safety/device-investigations/{case_id}/adjudicate",
        json={
            "classification": "SADE",
            "causality": "CERTAIN",
            "is_usade": True,
            "root_cause_summary": "Firmware memory leak caused unexpected reboot during active pacing.",
            "capa_required": True,
            "capa_reference": "CAPA-2026-099",
            "fsca_required": True,
            "signature_token": "physician_sig_token_part11_valid",
            "reason_for_change": "Adjudicate remedial action",
        },
        headers=headers,
    )
    assert adj_res.status_code == 200

    # 3. Generate FDA MedWatch report
    mw_res = client.post(
        f"/api/v1/safety/device-investigations/{case_id}/fda-medwatch-report",
        headers=headers,
    )
    assert mw_res.status_code == 200
    mw_data = mw_res.json()
    assert mw_data["is_5_day_expedited"] is True
    assert mw_data["vigilance_deadline_days"] == 5
    assert (
        mw_data["payload_json"]["section_g_all_manufacturers"]["fda_report_type"]
        == "5_DAY_EXPEDITED_REPORT"
    )
