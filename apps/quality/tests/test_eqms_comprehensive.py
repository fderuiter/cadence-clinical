"""Comprehensive test suite for Multi-Methodology RCA, 6-Stage Gate CAPAs, Action Items, and Automated Ingestion."""

import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base
from apps.quality.main import app
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS audit_schema.audit_logs (
                id VARCHAR(36) PRIMARY KEY,
                table_name VARCHAR(255) NOT NULL,
                record_id VARCHAR(255) NOT NULL,
                action VARCHAR(50) NOT NULL,
                user_id VARCHAR(255) NOT NULL,
                ip_address VARCHAR(45),
                timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                old_values JSON,
                new_values JSON,
                version_index INTEGER DEFAULT 1,
                change_reason TEXT,
                cryptographic_seal VARCHAR(64)
            );
        """)
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_auth_headers(
    roles: str = "quality_oversight,quality_manager,sponsor_designer",
    change_reason: str = "Comprehensive eQMS verification",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="quality.officer@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


def test_automated_quality_event_ingestion_api():
    """Validate automated ingestion of quality events from external systems (EDC, CTMS, eTMF).

    @req:PRD-QLT-001
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Automated EDC query escalation event")

    # Ingest event from EDC
    payload = {
        "study_id": "STUDY-INGEST-01",
        "site_id": "SITE-501",
        "title": "Automated Trigger: Unresolved SAE query exceeded 14-day regulatory limit",
        "description": "Query Q-88129 on SAE form remains unanswered past mandatory sponsor safety notification window.",
        "severity": "CRITICAL",
        "type": "SAFETY_REPORTING",
        "category": "Adverse Event Reporting Lag",
        "is_protocol_violation": True,
        "impact_safety": True,
        "impact_data": True,
        "impact_compliance": True,
        "source_system": "EDC",
        "source_reference_id": "EDC-QUERY-88129",
    }

    res = client.post("/api/v1/quality/ingest/event", headers=headers, json=payload)
    assert res.status_code == 201, res.text
    dev = res.json()
    assert dev["source_system"] == "EDC"
    assert dev["source_reference_id"] == "EDC-QUERY-88129"
    assert dev["severity"] == "CRITICAL"
    assert dev["status"] == "REPORTED"
    assert dev["impact_safety"] is True


def test_multi_methodology_rca_5whys_and_fishbone():
    """Validate structured 5-Whys causal hierarchy and 6M Ishikawa/Fishbone root cause analysis.

    @req:PRD-QLT-002
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Multi-methodology RCA investigation")

    # 1. Create a critical deviation
    dev_res = client.post(
        "/api/v1/quality/deviations",
        headers=headers,
        json={
            "study_id": "STUDY-RCA-001",
            "title": "Dosing calculation error for weight-tiered biologic",
            "description": "Patient was dosed at 200mg instead of 150mg based on outdated screening weight.",
            "severity": "CRITICAL",
            "type": "INVESTIGATIONAL_PRODUCT",
        },
    )
    dev_id = dev_res.json()["id"]

    # 2. Attach 5-Whys + 6M Fishbone RCA
    rca_payload = {
        "methodology": "FIVE_WHYS",
        "investigation_details": "Comprehensive multidisciplinary root cause panel conducted with site PI and study CRA.",
        "root_cause_summary": "Absence of real-time weight verification prompt in dispensing eCRF module.",
        "five_whys_chain": {
            "why_1": "Patient received 200mg dose instead of 150mg",
            "why_2": "Pharmacist used baseline weight from Day -14 rather than Day 1 pre-dose weight",
            "why_3": "Day 1 vital signs sheet was in paper chart and not yet transcribed into EDC",
            "why_4": "Site had 48-hour data entry backlog due to staffing shortage",
            "why_5": "No pre-dispense verification check in eCRF to enforce recent weight confirmation",
        },
        "fishbone_categories": {
            "man": ["Staffing shortage at site pharmacy"],
            "machine": ["EDC dispensing workflow lacked weight validation guardrail"],
            "material": ["Paper weight logs prone to transcription lag"],
            "method": ["Dosing procedure did not specify re-measurement timing window"],
            "measurement": [
                "Screening vs pre-dose weight delta threshold not configured"
            ],
            "milieu": ["High patient influx on clinic morning"],
        },
        "contributing_factors": [
            "HUMAN_ERROR",
            "PROCESS_AMBIGUITY",
            "SYSTEM_VALIDATION_GAP",
        ],
    }

    rca_res = client.post(
        f"/api/v1/quality/deviations/{dev_id}/rca", headers=headers, json=rca_payload
    )
    assert rca_res.status_code == 200, rca_res.text
    rca = rca_res.json()
    assert rca["deviation_id"] == dev_id
    assert (
        rca["five_whys_chain"]["why_5"]
        == "No pre-dispense verification check in eCRF to enforce recent weight confirmation"
    )
    assert len(rca["fishbone_categories"]["man"]) == 1
    assert "HUMAN_ERROR" in rca["contributing_factors"]


def test_capa_action_items_and_effectiveness_evaluations():
    """Validate full 6-Stage Gate CAPA lifecycle, sub-action item completion gating, and scheduled effectiveness checks.

    @req:PRD-QLT-003
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="CAPA lifecycle execution")

    # 1. Create Deviation
    dev_res = client.post(
        "/api/v1/quality/deviations",
        headers=headers,
        json={
            "study_id": "STUDY-CAPA-GATE",
            "title": "Temperature excursion in drug depot",
            "description": "Depot temperature rose to 12C for 3 hours.",
            "severity": "MAJOR",
            "type": "INVESTIGATIONAL_PRODUCT",
        },
    )
    dev_id = dev_res.json()["id"]

    # 2. Create CAPA
    capa_res = client.post(
        "/api/v1/quality/capas",
        headers=headers,
        json={
            "deviation_id": dev_id,
            "capa_type": "BOTH",
            "action_plan": "Replace cooling unit thermostat and retrain facility technicians.",
            "preventive_measures": "Install dual redundant cooling and automated SMS alarm dispatcher.",
            "risk_level": "HIGH",
            "target_completion_date": "2026-10-01T12:00:00",
            "effectiveness_interval_days": 60,
        },
    )
    assert capa_res.status_code == 201
    capa = capa_res.json()
    capa_id = capa["id"]
    assert capa["status"] == "INITIATED"
    assert capa["effectiveness_interval_days"] == 60

    # 3. Create sub-action item
    item_res = client.post(
        f"/api/v1/quality/capas/{capa_id}/action-items",
        headers=headers,
        json={
            "title": "Install secondary compressor unit",
            "description": "HVAC contractor to mount and wire auxiliary refrigeration unit.",
            "action_type": "PREVENTIVE",
            "assigned_to": "facilities.lead@cadence.io",
        },
    )
    assert item_res.status_code == 201
    item = item_res.json()
    item_id = item["id"]
    assert item["status"] == "OPEN"

    # 4. Advance CAPA stages: INITIATED -> UNDER_REVIEW -> APPROVED -> IMPLEMENTATION
    t1 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "UNDER_REVIEW"},
    )
    assert t1.status_code == 200
    assert t1.json()["status"] == "UNDER_REVIEW"

    t2 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "APPROVED"},
    )
    assert t2.status_code == 200
    assert t2.json()["status"] == "APPROVED"

    t3 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "IMPLEMENTATION"},
    )
    assert t3.status_code == 200
    assert t3.json()["status"] == "IMPLEMENTATION"

    # 5. Attempting to transition to IMPLEMENTATION_VERIFIED should fail if action items are still open!
    fail_t4 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "IMPLEMENTATION_VERIFIED"},
    )
    assert fail_t4.status_code == 422
    assert "Cannot verify implementation" in fail_t4.text

    # 6. Complete the action item
    complete_item = client.put(
        f"/api/v1/quality/capas/action-items/{item_id}",
        headers=headers,
        json={
            "status": "COMPLETED",
            "evidence_url": "https://tmf.cadence.io/docs/hvac_signoff.pdf",
        },
    )
    assert complete_item.status_code == 200
    assert complete_item.json()["status"] == "COMPLETED"

    # 7. Now transition to IMPLEMENTATION_VERIFIED -> EFFECTIVENESS_CHECK
    t4 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "IMPLEMENTATION_VERIFIED"},
    )
    assert t4.status_code == 200

    t5 = client.post(
        f"/api/v1/quality/capas/{capa_id}/transition",
        headers=headers,
        json={"to_status": "EFFECTIVENESS_CHECK"},
    )
    assert t5.status_code == 200
    assert t5.json()["status"] == "EFFECTIVENESS_CHECK"

    # 8. Record Effectiveness Verification Evaluation
    eff_res = client.post(
        f"/api/v1/quality/capas/{capa_id}/effectiveness",
        headers=headers,
        json={
            "planned_date": "2026-11-01T09:00:00",
            "metric_evaluated": "Depot temperature stability across 60-day continuous logging period",
            "baseline_value": "3 temperature excursions per quarter",
            "target_value": "0 temperature excursions outside 2-8C range",
            "actual_value": "0 excursions logged over 60 days",
            "outcome": "EFFECTIVE",
            "comments": "Automated alarm tests succeeded; dual refrigeration units operating with zero downtime.",
        },
    )
    assert eff_res.status_code == 201, eff_res.text
    check = eff_res.json()
    assert check["outcome"] == "EFFECTIVE"
    assert check["capa_id"] == capa_id
