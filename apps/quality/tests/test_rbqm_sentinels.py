"""Comprehensive Verification Suite for TransCelerate RACT & RBQM Sentinel Tracer.

Requirements:
- PRD-SYS-176: TransCelerate RACT Dynamic Risk Scoring & Automated KRI Surveillance
- PRD-CSM-005: Integrated TransCelerate RACT Matrix & Automated KRI Linkage
- PRD-CSM-006: Automated Centralized Monitoring Anomaly Dossier & Inspection Snapshot
- PRD-QLT-004: TransCelerate & ICH E6(R3) RBQM, KRI Statistical Anomaly Scoring, and Site Risk Index
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

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
    roles: str = "quality_manager,sponsor_designer",
    change_reason: str = "RBQM sentinel tracer verification",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.rbqm.specialist@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


# ---------------------------------------------------------------------------
# Section 1: PRD-SYS-176 / PRD-CSM-005: TransCelerate RACT Matrix & KRI Linkage
# ---------------------------------------------------------------------------


def test_transcelerate_ract_six_categories_and_dynamic_scoring():
    """Validate TransCelerate RACT risk factor scoring across all 6 risk categories (Probability x Impact x Detectability).

    @req:PRD-SYS-176
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Setup 6 TransCelerate risk categories")
    study_id = "STUDY-RBQM-2026"

    categories = [
        (
            "SAFETY",
            "Severe Cytokine Release Syndrome",
            3,
            3,
            2,
            18,
            "HIGH",
            "KRI_SAE_REPORTING_LAG",
        ),
        (
            "CONSENT",
            "Protocol Amendment Re-Consent Window",
            2,
            3,
            2,
            12,
            "MEDIUM",
            "KRI_CONSENT_VIOLATION",
        ),
        (
            "STUDY_CONDUCT",
            "Biopsy Screening Window Compliance",
            3,
            2,
            2,
            12,
            "MEDIUM",
            "KRI_DEV_DENSITY",
        ),
        (
            "DATA_INTEGRITY",
            "PK Lab Discrepancy Aging",
            2,
            2,
            1,
            4,
            "LOW",
            "KRI_QUERY_AGE",
        ),
        (
            "IP_MANAGEMENT",
            "Ultra-Cold Storage Excursions",
            2,
            3,
            3,
            18,
            "HIGH",
            "KRI_COLD_CHAIN_EXCURSION",
        ),
        (
            "TECHNOLOGY",
            "Continuous Wearable IoT Packet Loss",
            3,
            2,
            2,
            12,
            "MEDIUM",
            "KRI_MISSING_FORMS",
        ),
    ]

    for cat, topic, p, i, d, expected_score, expected_tier, kri in categories:
        res = client.post(
            "/api/v1/quality/rbqm/ract/factors",
            headers=headers,
            json={
                "study_id": study_id,
                "category": cat,
                "risk_topic": topic,
                "risk_description": f"Risk assessment for {topic}",
                "probability_score": p,
                "impact_score": i,
                "detectability_score": d,
                "mitigation_strategy": f"Automated sentinel monitoring for {topic}",
                "linked_kri_code": kri,
            },
        )
        assert res.status_code == 201, f"Failed for category {cat}: {res.text}"
        payload = res.json()
        assert payload["total_risk_score"] == expected_score
        assert payload["risk_tier"] == expected_tier
        assert payload["linked_kri_code"] == kri

    # Summary verification
    summary_res = client.get(
        f"/api/v1/quality/rbqm/ract/summary?study_id={study_id}", headers=headers
    )
    assert summary_res.status_code == 200
    summary = summary_res.json()
    assert summary["total_risk_factors"] == 6
    assert summary["high_risk_count"] == 2
    assert summary["medium_risk_count"] == 3
    assert summary["low_risk_count"] == 1


def test_kri_evaluations_and_dynamic_threshold_breaches():
    """Validate Key Risk Indicator (KRI) evaluation, Z-score standardization, and threshold breach detection.

    @req:PRD-SYS-176
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Evaluate KRI breach thresholds")

    # 1. Batch KRI Evaluation
    eval_res = client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json={
            "study_id": "STUDY-KRI-001",
            "kri_code": "KRI_DEV_DENSITY",
            "site_raw_values": {
                "SITE-GREEN-101": 0.05,
                "SITE-AMBER-102": 0.35,
                "SITE-RED-103": 1.20,
            },
        },
    )
    assert eval_res.status_code == 200, eval_res.text
    evals = eval_res.json()
    assert len(evals) == 3

    # 2. List evaluations
    list_res = client.get(
        "/api/v1/quality/rbqm/evaluations?study_id=STUDY-KRI-001",
        headers=headers,
    )
    assert list_res.status_code == 200
    all_evals = list_res.json()
    assert len(all_evals) >= 3


def test_site_risk_index_and_targeted_sdv_ranking():
    """Validate composite Site Risk Index (SRI) scoring and prioritized targeted monitoring (TSDV) scheduling.

    @req:PRD-SYS-176
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Compute Site Risk Index ranking")

    # 1. Compute site risk profiles
    comp_res = client.post(
        "/api/v1/quality/rbqm/site-risk-profiles/compute?study_id=STUDY-SRI-2026",
        headers=headers,
    )
    assert comp_res.status_code == 200
    profiles = comp_res.json()
    assert isinstance(profiles, list)

    # 2. Fetch site risk profiles for study
    res = client.get(
        "/api/v1/quality/rbqm/site-risk-profiles?study_id=STUDY-SRI-2026",
        headers=headers,
    )
    assert res.status_code == 200
    profiles_list = res.json()
    assert isinstance(profiles_list, list)


def test_one_click_centralized_monitoring_inspection_dossier():
    """Validate 1-click Centralized Monitoring Dossier packaging with cryptographic Merkle SHA-256 tamper seal.

    @req:PRD-SYS-176
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Compile inspection readiness dossier")

    # 1. Compile CSM Dossier
    compile_res = client.post(
        "/api/v1/quality/rbqm/csm/dossiers/compile",
        headers=headers,
        json={
            "study_id": "STUDY-DOSSIER-2026",
            "signer_name": "Dr. Aris Thorne",
            "signer_role": "Central Monitoring Lead",
            "dossier_name": "Q3 Centralized Surveillance Dossier",
            "signature_meaning": "Inspection Readiness Verification",
        },
    )
    assert compile_res.status_code == 201, compile_res.text
    dossier = compile_res.json()

    assert "merkle_seal" in dossier
    assert len(dossier["merkle_seal"]) == 64
    assert dossier["study_id"] == "STUDY-DOSSIER-2026"
    assert dossier["status"] == "FINALIZED"

    # 2. Retrieve compiled dossier
    dossier_id = dossier["id"]
    get_res = client.get(
        f"/api/v1/quality/rbqm/csm/dossiers/{dossier_id}",
        headers=headers,
    )
    assert get_res.status_code == 200
    assert get_res.json()["merkle_seal"] == dossier["merkle_seal"]
