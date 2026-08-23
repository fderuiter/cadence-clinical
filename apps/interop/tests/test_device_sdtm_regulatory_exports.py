"""Unit and Integration Test Suite for Device SDTM (DD / DO) and Regulatory Incident Exports.

Requirements:
- PRD-SYS-126: Medical Device & IVD Investigation Suite
- PRD-DEV-004: Regulatory Incident Exports & CDISC SDTM DD/DO
- ISO 14155:2020 / EU MDR 2017/745 / EU IVDR 2017/746
- ADR-2209 / ADR-2199
"""

import os

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.models import Base
from apps.interop.main import app
from packages.testing.security import create_test_auth_headers

pytestmark = pytest.mark.xdist_group("device_interop_exports")


@pytest_asyncio.fixture(autouse=True)
async def setup_interop_db():
    """Initializes in-memory database for interop microservice tests."""
    db_manager.init_db(
        os.getenv("INTEROP_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield


@pytest.fixture
def client():
    return TestClient(app)


def test_generate_sdtm_dd_endpoint(client):
    """Tests compiling CDISC SDTM DD (Device Deficiencies) dataset."""
    headers = create_test_auth_headers(
        user_id="biostat_data_manager",
        roles=["data_manager", "sponsor_admin"],
        change_reason="Compile SDTM DD dataset",
    )

    payload = {
        "study_id": "STUDY-DEV-101",
        "deficiencies": [
            {
                "id": "defic-001",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "subject_id": "SUBJ-0042",
                "device_name": "CardioPulse Pacemaker",
                "udi_di": "00840123456789",
                "lot_number": "LOT-99",
                "serial_number": "SN-4410",
                "deficiency_type": "MALFUNCTION",
                "deficiency_description": "Display unit frozen on initialization",
                "incident_date": "2026-08-20T10:00:00Z",
                "health_impact": "NONE",
                "action_taken_device": "QUARANTINED_AT_SITE",
                "safety_classification": "DEVICE_DEFICIENCY_INCIDENT",
            },
            {
                "id": "defic-002",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "subject_id": "SUBJ-0043",
                "device_name": "Infusion Pump",
                "udi_di": "00840987654321",
                "lot_number": "LOT-PUMP-01",
                "serial_number": "SN-PUMP-101",
                "deficiency_type": "SOFTWARE_FAULT",
                "deficiency_description": "Firmware assertion fault upon bolus command",
                "incident_date": "2026-08-21T11:00:00Z",
                "health_impact": "MILD_TRANSIENT",
                "action_taken_device": "RETURNED_TO_SPONSOR",
                "safety_classification": "ADE",
            },
        ],
    }

    res = client.post(
        "/api/v1/interop/devices/sdtm/dd",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 200
    data = res.json()
    assert data["domain"] == "DD"
    assert data["record_count"] == 2
    rec1 = data["records"][0]
    assert rec1["DOMAIN"] == "DD"
    assert rec1["DDTESTCD"] == "MALFUNC"
    assert rec1["DDDEVID"] == "00840123456789"
    assert rec1["DDLOT"] == "LOT-99"
    assert rec1["DDSER"] == "SN-4410"

    rec2 = data["records"][1]
    assert rec2["DDTESTCD"] == "SWFAULT"
    assert rec2["DDDEVID"] == "00840987654321"


def test_generate_sdtm_do_endpoint(client):
    """Tests compiling CDISC SDTM DO (Device Tracking and Operations) dataset."""
    headers = create_test_auth_headers(
        user_id="biostat_data_manager",
        roles=["data_manager", "sponsor_admin"],
        change_reason="Compile SDTM DO dataset",
    )

    payload = {
        "study_id": "STUDY-DEV-101",
        "devices": [
            {
                "id": "dev-001",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "device_identifier": "DEV-00840123456789-SN-4410",
                "brand_name": "CardioPulse Pacemaker",
                "device_type": "IMPLANTABLE",
                "udi_di": "00840123456789",
                "lot_number": "LOT-99",
                "serial_number": "SN-4410",
                "status": "DISPENSED",
                "allocated_subject_id": "SUBJ-0042",
                "dispensed_at": "2026-08-15T09:00:00Z",
                "expiration_date": "2028-06-30T00:00:00Z",
            },
            {
                "id": "dev-002",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "device_identifier": "DEV-00840123456789-SN-4411",
                "brand_name": "CardioPulse Pacemaker",
                "device_type": "IMPLANTABLE",
                "udi_di": "00840123456789",
                "lot_number": "LOT-99",
                "serial_number": "SN-4411",
                "status": "QUARANTINED",
                "quarantined_at": "2026-08-16T14:00:00Z",
                "expiration_date": "2028-06-30T00:00:00Z",
            },
        ],
    }

    res = client.post(
        "/api/v1/interop/devices/sdtm/do",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 200
    data = res.json()
    assert data["domain"] == "DO"
    assert data["record_count"] == 2
    rec1 = data["records"][0]
    assert rec1["DOMAIN"] == "DO"
    assert rec1["DOTESTCD"] == "DISP"
    assert rec1["DOSTATUS"] == "DISPENSED"
    assert rec1["DODI"] == "00840123456789"
    assert rec1["DOLOT"] == "LOT-99"

    rec2 = data["records"][1]
    assert rec2["DOTESTCD"] == "QUAR"
    assert rec2["DOSTATUS"] == "QUARANTINED"


def test_generate_mir_report_endpoint(client):
    """Tests generating an EU MDR / IVDR Manufacturer Incident Report (MIR v7.2 XML/JSON)."""
    headers = create_test_auth_headers(
        user_id="regulatory_affairs_01",
        roles=["data_manager", "sponsor_admin"],
        change_reason="MIR compilation",
    )

    payload = {
        "study_id": "STUDY-DEV-101",
        "site_id": "SITE-101",
        "deficiency_id": "DEFIC-9901",
        "brand_name": "CardioPulse Active Pacemaker",
        "udi_di": "00840123456789",
        "lot_number": "LOT-99",
        "serial_number": "SN-4410",
        "incident_date": "2026-08-20T10:00:00Z",
        "deficiency_type": "MALFUNCTION",
        "deficiency_description": "Intermittent telemetry disconnection during ventricular pacing.",
        "safety_classification": "SADE",
        "causality": "PROBABLE",
        "patient_involved": True,
        "health_impact": "MILD_TRANSIENT",
        "action_taken": "QUARANTINED_AT_SITE",
        "capa_required": True,
        "fsca_required": False,
    }

    res = client.post(
        "/api/v1/interop/devices/mir-report",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 200
    data = res.json()
    assert data["mir_version"] == "7.2"
    assert data["regulatory_framework"] == "EU_MDR_2017_745"
    assert "ManufacturerIncidentReport" in data["payload_xml"]
    assert (
        data["payload_json"]["device_identification"]["brand_name"]
        == "CardioPulse Active Pacemaker"
    )


def test_generate_export_package_endpoint(client):
    """Tests compiling the full regulatory investigation export package with SHA-256 Merkle audit manifest."""
    headers = create_test_auth_headers(
        user_id="regulatory_affairs_01",
        roles=["data_manager", "sponsor_admin"],
        change_reason="Full export package",
    )

    payload = {
        "study_id": "STUDY-DEV-101",
        "devices": [
            {
                "id": "dev-01",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "device_identifier": "DEV-01",
                "udi_di": "00840123456789",
                "lot_number": "LOT-99",
                "serial_number": "SN-4410",
                "status": "AVAILABLE",
            }
        ],
        "deficiencies": [
            {
                "id": "defic-01",
                "study_id": "STUDY-DEV-101",
                "site_id": "SITE-101",
                "device_name": "CardioPulse Pacemaker",
                "udi_di": "00840123456789",
                "lot_number": "LOT-99",
                "serial_number": "SN-4410",
                "deficiency_type": "MALFUNCTION",
                "deficiency_description": "Telemetry failure",
                "incident_date": "2026-08-20T10:00:00Z",
                "safety_classification": "SADE",
            }
        ],
    }

    res = client.post(
        "/api/v1/interop/devices/export-package",
        json=payload,
        headers=headers,
    )
    assert res.status_code == 200
    data = res.json()
    assert data["sdtm_dd_count"] == 1
    assert data["sdtm_do_count"] == 1
    assert len(data["incident_reports"]) == 1
    assert data["audit_manifest"]["digest_sha256"] is not None
    assert data["audit_manifest"]["gxp_compliant"] is True
