"""Unit and Integration Test Suite for Device Safety Investigations & EU MDR/IVDR Vigilance.

Requirements:
- PRD-SYS-126: Medical Device & IVD Investigation Suite
- PRD-DEV-003: Automated Adverse Device Effect & RTSM Quarantine Triggers
- PRD-DEV-004: Regulatory Incident Reports & CDISC SDTM DD/DO Exports
- ISO 14155:2020 / EU MDR 2017/745 / EU IVDR 2017/746 / 21 CFR Part 11
- ADR-2209 / ADR-2199
"""

import os

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.safety.database import db_manager
from apps.safety.infrastructure.models import (
    Base,
)
from apps.safety.main import app
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("device_safety_investigations")


@pytest_asyncio.fixture(autouse=True)
async def setup_safety_db():
    """Initializes in-memory database for safety microservice tests."""
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


def test_open_device_safety_investigation_endpoint(client):
    """Tests opening a new Adverse Device Effect safety investigation case with vigilance countdown."""
    headers = create_test_auth_headers(
        user_id="safety_physician_01",
        roles=["safety_officer", "sponsor_admin"],
        change_reason="Open device safety case",
    )

    payload = {
        "study_id": "STUDY-DEV-101",
        "site_id": "SITE-101",
        "subject_id": "SUBJ-101-0042",
        "deficiency_id": "defic-test-001",
        "device_name": "CardioPulse Pacemaker",
        "device_udi_di": "00840123456789",
        "lot_number": "LOT-99",
        "serial_number": "SN-4410",
        "classification": "SADE",
        "causality": "POSSIBLE",
        "is_usade": False,
        "investigation_notes": "Telemetry loss during exercise; pacing maintained baseline.",
        "reason_for_change": "Initial SADE safety inquiry",
    }

    res = client.post(
        "/api/v1/safety/device-investigations",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 201
    data = res.json()
    assert data["study_id"] == "STUDY-DEV-101"
    assert data["classification"] == "SADE"
    assert data["vigilance_deadline_days"] == 10  # 10 days for SADE
    assert data["status"] == "OPEN"
    assert data["days_remaining"] <= 10
    assert data["is_overdue"] is False


def test_list_and_get_device_safety_investigations(client):
    """Tests listing and filtering device safety investigation cases."""
    headers = create_test_auth_headers(
        user_id="safety_physician_01",
        roles=["safety_officer"],
        change_reason="Query safety cases",
    )

    # 1. Create two cases (one ADE, one SADE)
    for cls in ("ADE", "SADE"):
        client.post(
            "/api/v1/safety/device-investigations",
            json={
                "study_id": "STUDY-DEV-LIST",
                "site_id": "SITE-101",
                "deficiency_id": f"defic-{cls.lower()}",
                "device_name": "Infusion Pump",
                "device_udi_di": "00840123456789",
                "lot_number": "LOT-PUMP",
                "classification": cls,
                "reason_for_change": f"Open {cls} case",
            },
            headers=headers,
        )

    # 2. List all
    list_res = client.get(
        "/api/v1/safety/device-investigations?study_id=STUDY-DEV-LIST",
        headers=headers,
    )
    assert list_res.status_code == 200
    items = list_res.json()
    assert len(items) == 2

    # 3. Filter by classification=SADE
    sade_res = client.get(
        "/api/v1/safety/device-investigations?study_id=STUDY-DEV-LIST&classification=SADE",
        headers=headers,
    )
    assert sade_res.status_code == 200
    sade_items = sade_res.json()
    assert len(sade_items) == 1
    assert sade_items[0]["classification"] == "SADE"

    # 4. Get by ID
    first_id = items[0]["id"]
    get_res = client.get(
        f"/api/v1/safety/device-investigations/{first_id}",
        headers=headers,
    )
    assert get_res.status_code == 200
    assert get_res.json()["id"] == first_id


def test_adjudicate_device_safety_investigation(client):
    """Tests Safety Physician formal adjudication with 21 CFR Part 11 electronic signature."""
    headers = create_test_auth_headers(
        user_id="safety_physician_01",
        roles=["safety_officer", "sponsor_admin"],
        change_reason="Adjudicate case",
    )

    # 1. Create case
    create_res = client.post(
        "/api/v1/safety/device-investigations",
        json={
            "study_id": "STUDY-DEV-ADJ",
            "site_id": "SITE-101",
            "deficiency_id": "defic-adj-01",
            "device_name": "Active Stent",
            "device_udi_di": "00840123456789",
            "lot_number": "LOT-ADJ",
            "classification": "SADE",
            "reason_for_change": "Open case",
        },
        headers=headers,
    )
    case_id = create_res.json()["id"]

    # 2. Adjudicate
    adj_payload = {
        "classification": "SADE",
        "causality": "PROBABLE",
        "is_usade": False,
        "investigation_notes": "Technical bench test confirms wire fracture under cyclic fatigue.",
        "root_cause_summary": "Mechanical stress failure at proximal connector weld.",
        "capa_required": True,
        "capa_reference": "CAPA-2026-DEV-088",
        "fsca_required": False,
        "signature_token": "valid_part11_physician_e_signature_token_xyz",
        "reason_for_change": "Formal Safety Physician Adjudication",
    }
    adj_res = client.put(
        f"/api/v1/safety/device-investigations/{case_id}/adjudicate",
        json=adj_payload,
        headers=headers,
    )
    assert adj_res.status_code == 200
    adj_data = adj_res.json()
    assert adj_data["status"] == "RESOLVED"
    assert adj_data["causality"] == "PROBABLE"
    assert adj_data["capa_required"] is True
    assert adj_data["capa_reference"] == "CAPA-2026-DEV-088"
    assert adj_data["safety_physician_user_id"] == "safety_physician_01"


def test_generate_device_vigilance_mir_report(client):
    """Tests generating an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2)."""
    headers = create_test_auth_headers(
        user_id="safety_physician_01",
        roles=["safety_officer"],
        change_reason="MIR compilation",
    )

    # 1. Create case
    create_res = client.post(
        "/api/v1/safety/device-investigations",
        json={
            "study_id": "STUDY-DEV-MIR",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-001",
            "deficiency_id": "defic-mir-99",
            "device_name": "CardioPulse Pacemaker",
            "device_udi_di": "00840123456789",
            "lot_number": "LOT-MIR-01",
            "serial_number": "SN-MIR-001",
            "classification": "SADE",
            "reason_for_change": "Open SADE case",
        },
        headers=headers,
    )
    case_id = create_res.json()["id"]

    # 2. Generate MIR report
    mir_res = client.post(
        f"/api/v1/safety/device-investigations/{case_id}/regulatory-report",
        headers=headers,
    )
    assert mir_res.status_code == 200
    mir_data = mir_res.json()
    assert mir_data["report_type"] == "EU_MDR_MIR_V7_2"
    assert "ManufacturerIncidentReport" in mir_data["payload_xml"]
    assert (
        mir_data["payload_json"]["device_identification"]["udi_di"] == "00840123456789"
    )
    assert (
        mir_data["payload_json"]["incident_information"]["safety_classification"]
        == "SADE"
    )
