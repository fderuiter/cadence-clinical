"""Tests for Clinical Audit Engagements, Findings, 1-Click CAPA Promotion, and Inspection Readiness Dossier."""

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
    roles: str = "auditor,quality_manager",
    change_reason: str = "Periodic GCP audit compliance verification",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.auditor@gxp-assurance.com",
        roles=roles,
        change_reason=change_reason,
    )


def test_audit_engagement_lifecycle():
    """Validate Clinical Audit creation, status progression, and filtering.

    @req:PRD-QLT-006
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Audit setup")

    # 1. Create Audit
    res = client.post(
        "/api/v1/quality/audits",
        headers=headers,
        json={
            "audit_number": "AUD-2026-SITE-101",
            "study_id": "STUDY-CARDIO-002",
            "site_id": "SITE-101",
            "audit_type": "SITE_AUDIT",
            "lead_auditor": "lead.auditor@gxp-assurance.com",
            "planned_start_date": "2026-09-01T09:00:00",
            "planned_end_date": "2026-09-03T17:00:00",
            "scope_summary": "Routine Phase III investigator site GCP compliance inspection and IP accountability check.",
        },
    )
    assert res.status_code == 201, res.text
    audit = res.json()
    audit_id = audit["id"]
    assert audit["status"] == "PLANNED"
    assert audit["audit_number"] == "AUD-2026-SITE-101"

    # 2. Update Status to IN_PROGRESS
    status_res = client.put(
        f"/api/v1/quality/audits/{audit_id}/status",
        headers=headers,
        json={
            "status": "IN_PROGRESS",
            "actual_start_date": "2026-09-01T08:45:00",
        },
    )
    assert status_res.status_code == 200
    assert status_res.json()["status"] == "IN_PROGRESS"

    # 3. List Audits
    list_res = client.get(
        "/api/v1/quality/audits?study_id=STUDY-CARDIO-002", headers=headers
    )
    assert list_res.status_code == 200
    audits = list_res.json()
    assert len(audits) >= 1
    assert audits[0]["id"] == audit_id


def test_audit_findings_and_one_click_capa_promotion():
    """Validate audit finding creation, 1-click CAPA promotion, and bi-directional traceability linking.

    @req:PRD-QLT-006
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Finding promotion")

    # 1. Create an audit
    audit_res = client.post(
        "/api/v1/quality/audits",
        headers=headers,
        json={
            "audit_number": "AUD-2026-FINDING-TEST",
            "study_id": "STUDY-ONC-FINDING",
            "site_id": "SITE-202",
            "audit_type": "SITE_AUDIT",
            "lead_auditor": "lead.auditor@gxp-assurance.com",
            "planned_start_date": "2026-09-10T09:00:00",
            "planned_end_date": "2026-09-12T17:00:00",
            "scope_summary": "Investigational product temperature logging audit.",
        },
    )
    assert audit_res.status_code == 201, audit_res.text
    audit_id = audit_res.json()["id"]

    # 2. Log a Critical Finding
    finding_res = client.post(
        f"/api/v1/quality/audits/{audit_id}/findings",
        headers=headers,
        json={
            "finding_number": "FINDING-01",
            "severity": "CRITICAL",
            "category": "Investigational Product Storage",
            "condition": "Pharmacy refrigerator temperature log showed 4-day excursion above +8°C without quarantine.",
            "criteria": "ICH GCP E6(R2) Section 4.6.4 (Storage according to specifications).",
            "cause": "Backup thermometer calibration failed and audible alarm was switched off.",
            "effect": "Integrity of 12 IP vials compromised; potential safety risk to 3 dosed subjects.",
        },
    )
    assert finding_res.status_code == 201, finding_res.text
    finding = finding_res.json()
    finding_id = finding["id"]
    assert finding["finding_number"] == "FINDING-01"
    assert finding["severity"] == "CRITICAL"
    assert finding["capa_id"] is None

    # 3. 1-Click Promote Finding to formal CAPA
    promote_res = client.post(
        f"/api/v1/quality/audits/findings/{finding_id}/promote-capa",
        headers=headers,
        json={
            "action_plan": "Quarantine remaining IP batch, notify Medical Monitor, replace alarm battery and re-calibrate digital data logger.",
            "preventive_measures": "Implement 24/7 IoT automated cloud temperature alerts to Principal Investigator and Site Pharmacist.",
            "target_completion_date": "2026-09-25T17:00:00",
        },
    )
    assert promote_res.status_code == 201, promote_res.text
    capa = promote_res.json()
    assert capa["audit_finding_id"] == finding_id
    assert capa["status"] == "INITIATED"
    assert capa["risk_level"] == "HIGH"
    assert capa["deviation_id"] is not None

    # 4. Verify Finding now references the CAPA
    get_audit = client.get(f"/api/v1/quality/audits/{audit_id}", headers=headers)
    assert get_audit.status_code == 200
    updated_findings = client.get(
        f"/api/v1/quality/audits/{audit_id}/findings", headers=headers
    )
    assert updated_findings.status_code == 200
    f_list = updated_findings.json()
    assert f_list[0]["capa_id"] == capa["id"]


def test_inspection_readiness_dossier_compilation_and_tamper_seal():
    """Validate 1-click Inspection Readiness Dossier compilation with cryptographic SHA-256 tamper seal.

    @req:PRD-QLT-008
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Compile inspection dossier")
    study_id = "STUDY-DOSSIER-001"

    # Create a deviation for the study
    client.post(
        "/api/v1/quality/deviations",
        headers=headers,
        json={
            "study_id": study_id,
            "title": "Minor visit window delay",
            "description": "Visit 3 delayed by 2 days",
            "severity": "MINOR",
            "type": "VISIT_WINDOW",
        },
    )

    # Compile dossier
    dossier_res = client.get(
        f"/api/v1/quality/audits/inspection-dossier/{study_id}", headers=headers
    )
    assert dossier_res.status_code == 200, dossier_res.text
    dossier = dossier_res.json()

    assert dossier["study_id"] == study_id
    assert "cryptographic_tamper_seal" in dossier
    assert len(dossier["cryptographic_tamper_seal"]) == 64  # Valid SHA-256 hex string
    assert dossier["summary_statistics"]["total_deviations"] >= 1
    assert "deviations" in dossier
    assert "capas" in dossier
    assert "audits" in dossier
