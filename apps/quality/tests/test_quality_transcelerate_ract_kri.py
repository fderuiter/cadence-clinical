"""Comprehensive unit and integration tests for TransCelerate RACT Matrix & Automated KRI Linkage.

Covers:
- Protocol risk categorization across all 6 TransCelerate categories (Safety, Consent, Study Conduct, Data Integrity, IP Management, Technology)
- Probability (1-3), Impact (1-3), and Detectability (1-3) scoring with auto-tier classification
- Direct linkage to automated Key Risk Indicators (KRIs) with Green/Amber/Red thresholds
- Weighted composite Site Risk Index (SRI) and percentile ranking across clinical sites
- 1-click Centralized Monitoring Inspection Dossier compilation with 21 CFR Part 11 e-signatures and Merkle seals
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
    change_reason: str = "TransCelerate RACT protocol risk calibration",
    user_id: str = "lead.cra@cadence.io",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


def test_ract_risk_factor_lifecycle():
    """Validate TransCelerate RACT risk factor creation, scoring, update, and deletion.

    @req:PRD-CSM-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Create initial protocol risk factor")

    # 1. Create High-Risk Factor (2 x 3 x 3 = 18 -> HIGH)
    res = client.post(
        "/api/v1/quality/rbqm/ract/factors",
        headers=headers,
        json={
            "study_id": "STUDY-RACT-001",
            "category": "IP_MANAGEMENT",
            "risk_topic": "Ultra-Cold Storage Excursions",
            "risk_description": "Risk of biologic degradation if temperature exceeds -80C during site storage.",
            "probability_score": 2,
            "impact_score": 3,
            "detectability_score": 3,
            "mitigation_strategy": "Continuous wireless IoT temperature logging and real-time alerts.",
            "linked_kri_code": "KRI_COLD_CHAIN_EXCURSION",
        },
    )
    assert res.status_code == 201, res.text
    data = res.json()
    assert data["study_id"] == "STUDY-RACT-001"
    assert data["category"] == "IP_MANAGEMENT"
    assert data["risk_topic"] == "Ultra-Cold Storage Excursions"
    assert data["probability_score"] == 2
    assert data["impact_score"] == 3
    assert data["detectability_score"] == 3
    assert data["total_risk_score"] == 18
    assert data["risk_tier"] == "HIGH"
    assert data["linked_kri_code"] == "KRI_COLD_CHAIN_EXCURSION"
    assert data["version_index"] == 1
    factor_id = data["id"]

    # 2. Update Risk Factor (reduce detectability to 1 -> 2 x 3 x 1 = 6 -> LOW)
    update_headers = get_auth_headers(
        change_reason="Installed real-time automated logger"
    )
    update_res = client.put(
        f"/api/v1/quality/rbqm/ract/factors/{factor_id}",
        headers=update_headers,
        json={
            "detectability_score": 1,
            "mitigation_strategy": "Automated alarm integration with central pharmacy.",
        },
    )
    assert update_res.status_code == 200, update_res.text
    updated = update_res.json()
    assert updated["detectability_score"] == 1
    assert updated["total_risk_score"] == 6
    assert updated["risk_tier"] == "LOW"
    assert updated["version_index"] == 2

    # 3. Delete Risk Factor
    del_headers = get_auth_headers(change_reason="Retired obsolete risk topic")
    del_res = client.delete(
        f"/api/v1/quality/rbqm/ract/factors/{factor_id}",
        headers=del_headers,
    )
    assert del_res.status_code == 200
    assert del_res.json()["deleted"] is True


def test_transcelerate_six_categories_and_kri_linkage():
    """Validate capturing risk factors across all 6 TransCelerate protocol risk categories

    and linking them to automated Key Risk Indicators.

    @req:PRD-CSM-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Setup 6-category RACT matrix")
    study_id = "STUDY-6CAT-2026"

    category_samples = [
        {
            "category": "SAFETY",
            "risk_topic": "Infusion-Related Toxicities",
            "risk_description": "Severe cytokine release syndrome during escalating cohort dosing.",
            "probability_score": 2,
            "impact_score": 3,
            "detectability_score": 2,
            "mitigation_strategy": "Mandatory 2-clinician sign-off before infusion.",
            "linked_kri_code": "KRI_SAE_REPORTING_LAG",
        },
        {
            "category": "CONSENT",
            "risk_topic": "Protocol Amendment Re-consent Timeliness",
            "risk_description": "Visits conducted without latest signed ICF version.",
            "probability_score": 2,
            "impact_score": 3,
            "detectability_score": 2,
            "mitigation_strategy": "Programmatic EDC write-lock gating visit entry.",
            "linked_kri_code": "KRI_CONSENT_VIOLATION",
        },
        {
            "category": "STUDY_CONDUCT",
            "risk_topic": "Biopsy Screening Window Compliance",
            "risk_description": "Protocol deviations from scheduling outside 72-hour window.",
            "probability_score": 3,
            "impact_score": 2,
            "detectability_score": 2,
            "mitigation_strategy": "Automated schedule window countdown notifications.",
            "linked_kri_code": "KRI_DEV_DENSITY",
        },
        {
            "category": "DATA_INTEGRITY",
            "risk_topic": "PK Query Resolution Aging",
            "risk_description": "Unresolved lab discrepancy backlog delaying interim analysis.",
            "probability_score": 2,
            "impact_score": 2,
            "detectability_score": 1,
            "mitigation_strategy": "Weekly CRA query escalation.",
            "linked_kri_code": "KRI_QUERY_AGE",
        },
        {
            "category": "IP_MANAGEMENT",
            "risk_topic": "Investigational Product Cold Chain Excursion",
            "risk_description": "Temperature excursions exceeding storage limits.",
            "probability_score": 2,
            "impact_score": 3,
            "detectability_score": 3,
            "mitigation_strategy": "IoT sensor with RTSM automated quarantine trigger.",
            "linked_kri_code": "KRI_COLD_CHAIN_EXCURSION",
        },
        {
            "category": "TECHNOLOGY",
            "risk_topic": "Wearable Biometric Stream Disconnections",
            "risk_description": "Missing continuous actigraphy sensor data.",
            "probability_score": 3,
            "impact_score": 2,
            "detectability_score": 2,
            "mitigation_strategy": "Real-time wearable heartbeat health checks.",
            "linked_kri_code": "KRI_MISSING_FORMS",
        },
    ]

    for item in category_samples:
        payload = {"study_id": study_id, **item}
        res = client.post(
            "/api/v1/quality/rbqm/ract/factors", headers=headers, json=payload
        )
        assert res.status_code == 201, f"Failed for {item['category']}: {res.text}"

    # Verify query by category
    safety_res = client.get(
        f"/api/v1/quality/rbqm/ract/factors?study_id={study_id}&category=SAFETY",
        headers=headers,
    )
    assert safety_res.status_code == 200
    safety_factors = safety_res.json()
    assert len(safety_factors) >= 1
    assert all(f["category"] == "SAFETY" for f in safety_factors)

    # Verify all factors
    all_res = client.get(
        f"/api/v1/quality/rbqm/ract/factors?study_id={study_id}", headers=headers
    )
    assert all_res.status_code == 200
    all_factors = all_res.json()
    assert len(all_factors) == 6


def test_ract_matrix_summary_and_default_seeding():
    """Validate automatic default TransCelerate RACT matrix seeding and summary calculations.

    @req:PRD-CSM-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Inspect RACT summary")
    study_id = "STUDY-SUMMARY-001"

    # Fetching summary for empty study should auto-seed default TransCelerate matrix
    res = client.get(
        f"/api/v1/quality/rbqm/ract/summary?study_id={study_id}", headers=headers
    )
    assert res.status_code == 200, res.text
    data = res.json()

    assert data["study_id"] == study_id
    assert data["total_risk_factors"] == 10
    assert data["high_risk_count"] >= 1
    assert data["medium_risk_count"] >= 1
    assert data["low_risk_count"] >= 1
    assert data["overall_average_score"] > 0
    assert data["linked_kri_coverage_percentage"] == 100.0
    assert len(data["category_summaries"]) == 6
    assert len(data["risk_factors"]) == 10

    # Test explicit seeding idempotency
    seed_res = client.post(
        f"/api/v1/quality/rbqm/ract/seed-default?study_id={study_id}",
        headers=headers,
    )
    assert seed_res.status_code == 201
    assert len(seed_res.json()) == 10


def test_automated_kri_definitions_and_statistical_evaluations():
    """Validate automated Key Risk Indicators (KRIs) across TransCelerate categories

    and statistical Z-score multi-site batch evaluations.

    @req:PRD-CSM-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Evaluate KRI batch metrics")
    study_id = "STUDY-KRI-EVAL"

    # List default KRIs
    kri_res = client.get("/api/v1/quality/rbqm/kris", headers=headers)
    assert kri_res.status_code == 200
    kris = kri_res.json()
    assert len(kris) >= 10
    kri_codes = {k["code"] for k in kris}
    assert "KRI_AE_RATE" in kri_codes
    assert "KRI_SAE_REPORTING_LAG" in kri_codes
    assert "KRI_CONSENT_VIOLATION" in kri_codes
    assert "KRI_DEV_DENSITY" in kri_codes
    assert "KRI_QUERY_AGE" in kri_codes
    assert "KRI_COLD_CHAIN_EXCURSION" in kri_codes

    # Perform batch evaluation on KRI_SAE_REPORTING_LAG
    eval_payload = {
        "study_id": study_id,
        "kri_code": "KRI_SAE_REPORTING_LAG",
        "site_raw_values": {
            "SITE-101": 10.0,  # Below green threshold (12h) -> Low/Normal
            "SITE-102": 11.0,
            "SITE-103": 12.0,
            "SITE-104": 72.0,  # Massive 72h delay -> Extreme Outlier
        },
    }
    eval_res = client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json=eval_payload,
    )
    assert eval_res.status_code == 200, eval_res.text
    evaluations = eval_res.json()
    assert len(evaluations) == 4

    eval_map = {e["site_id"]: e for e in evaluations}
    assert eval_map["SITE-104"]["standardized_z_score"] > 1.0
    assert eval_map["SITE-104"]["risk_tier"] in ("HIGH", "CRITICAL", "MEDIUM")


def test_composite_site_risk_index_and_percentile_rankings():
    """Validate weighted composite Site Risk Index (SRI) scoring and percentile rank computation.

    @req:PRD-CSM-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Compute Site Risk Index")
    study_id = "STUDY-SRI-001"

    # Evaluate two KRIs for 4 sites
    client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json={
            "study_id": study_id,
            "kri_code": "KRI_QUERY_AGE",
            "site_raw_values": {
                "SITE-A": 5.0,
                "SITE-B": 6.0,
                "SITE-C": 15.0,
                "SITE-D": 45.0,
            },
        },
    )

    client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json={
            "study_id": study_id,
            "kri_code": "KRI_DEV_DENSITY",
            "site_raw_values": {
                "SITE-A": 0.2,
                "SITE-B": 0.3,
                "SITE-C": 1.2,
                "SITE-D": 4.5,
            },
        },
    )

    # Add active deviation to SITE-D
    client.post(
        "/api/v1/quality/deviations",
        headers=headers,
        json={
            "study_id": study_id,
            "site_id": "SITE-D",
            "title": "Unreported Grade 4 Toxicity",
            "description": "Critical protocol violation with missing safety submission",
            "severity": "CRITICAL",
            "type": "SAFETY_REPORTING",
        },
    )

    # Compute Site Risk Profiles
    compute_res = client.post(
        f"/api/v1/quality/rbqm/site-risk-profiles/compute?study_id={study_id}",
        headers=headers,
    )
    assert compute_res.status_code == 200, compute_res.text
    profiles = compute_res.json()
    assert len(profiles) == 4

    # SITE-D should have Rank 1 (highest composite risk score)
    assert profiles[0]["site_id"] == "SITE-D"
    assert profiles[0]["risk_rank"] == 1
    assert profiles[0]["percentile_rank"] >= 80.0
    assert profiles[0]["risk_tier"] in ("HIGH", "CRITICAL")
    assert profiles[0]["active_deviations_count"] == 1

    # Lowest risk site should have lower percentile
    lowest_profile = profiles[-1]
    assert lowest_profile["risk_rank"] == 4
    assert lowest_profile["percentile_rank"] < 50.0
    assert lowest_profile["risk_tier"] == "LOW"


def test_one_click_centralized_monitoring_inspection_dossier_compilation():
    """Validate compiling an on-demand Centralized Monitoring Inspection Dossier

    with 21 CFR Part 11 electronic signatures and cryptographic Merkle sealing.

    @req:PRD-CSM-006
    @req:PRD-SYS-001
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Inspection Readiness Dossier generation")
    study_id = "STUDY-DOSSIER-2026"

    # Seed RACT and evaluate KRIs
    client.post(
        f"/api/v1/quality/rbqm/ract/seed-default?study_id={study_id}",
        headers=headers,
    )
    client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json={
            "study_id": study_id,
            "kri_code": "KRI_AE_RATE",
            "site_raw_values": {"SITE-01": 1.2, "SITE-02": 3.8},
        },
    )

    # 1. Compile 1-Click Dossier
    compile_payload = {
        "study_id": study_id,
        "dossier_name": "CSM-Inspection-Readiness-Snapshot-Q3-2026",
        "signer_name": "Dr. Eleanor Vance, MD",
        "signer_role": "Head of Clinical Quality Assurance",
        "signature_meaning": "I have reviewed and approve this Centralized Monitoring Inspection Dossier in accordance with 21 CFR Part 11 and ICH E6(R3).",
    }
    compile_res = client.post(
        "/api/v1/quality/rbqm/csm/dossiers/compile",
        headers=headers,
        json=compile_payload,
    )
    assert compile_res.status_code == 201, compile_res.text
    dossier = compile_res.json()

    assert dossier["study_id"] == study_id
    assert dossier["dossier_name"] == "CSM-Inspection-Readiness-Snapshot-Q3-2026"
    assert dossier["status"] == "FINALIZED"
    assert dossier["merkle_seal"] is not None
    assert len(dossier["merkle_seal"]) == 64  # SHA-256 hash length

    # Validate TransCelerate RACT section in dossier
    assert "ract_summary" in dossier
    assert dossier["ract_summary"]["total_risk_factors"] >= 10

    # Validate KRI surveillance section
    assert "kri_surveillance_summary" in dossier
    assert dossier["kri_surveillance_summary"]["total_kris_defined"] >= 10

    # Validate Site rankings section
    assert "site_risk_rankings" in dossier
    assert len(dossier["site_risk_rankings"]) >= 2

    # Validate Part 11 Electronic Signature section
    sig = dossier["electronic_signature"]
    assert sig["signer_name"] == "Dr. Eleanor Vance, MD"
    assert sig["signer_role"] == "Head of Clinical Quality Assurance"
    assert sig["part11_compliant"] is True
    assert sig["signature_hash"] is not None
    assert len(sig["signature_hash"]) == 64

    # Validate Audit trail snapshot
    assert "audit_trail_snapshot" in dossier
    assert len(dossier["audit_trail_snapshot"]) > 0

    dossier_id = dossier["id"]

    # 2. List dossiers for study
    list_res = client.get(
        f"/api/v1/quality/rbqm/csm/dossiers?study_id={study_id}", headers=headers
    )
    assert list_res.status_code == 200
    dossiers = list_res.json()
    assert len(dossiers) >= 1
    assert any(d["id"] == dossier_id for d in dossiers)

    # 3. Retrieve specific dossier
    get_res = client.get(
        f"/api/v1/quality/rbqm/csm/dossiers/{dossier_id}", headers=headers
    )
    assert get_res.status_code == 200
    retrieved = get_res.json()
    assert retrieved["id"] == dossier_id
    assert retrieved["merkle_seal"] == dossier["merkle_seal"]


def test_ract_validation_errors_and_unauthorized_access():
    """Validate boundary score constraints and role authorization restrictions.

    @req:PRD-CSM-005
    @req:PRD-SYS-003
    """
    client = TestClient(app)
    auth_headers = get_auth_headers(change_reason="Validation test")

    # 1. Invalid Probability Score > 3
    invalid_res = client.post(
        "/api/v1/quality/rbqm/ract/factors",
        headers=auth_headers,
        json={
            "study_id": "STUDY-ERR",
            "category": "SAFETY",
            "risk_topic": "Invalid Score",
            "risk_description": "Score out of range",
            "probability_score": 5,  # Invalid: must be 1-3
            "impact_score": 2,
            "detectability_score": 1,
            "mitigation_strategy": "None",
        },
    )
    assert invalid_res.status_code == 422

    # 2. Read-only role (Auditor) attempting to create risk factor
    auditor_headers = get_auth_headers(
        roles="auditor", change_reason="Unauthorized attempt"
    )
    unauth_res = client.post(
        "/api/v1/quality/rbqm/ract/factors",
        headers=auditor_headers,
        json={
            "study_id": "STUDY-ERR",
            "category": "CONSENT",
            "risk_topic": "Read-only Test",
            "risk_description": "Should fail",
            "probability_score": 1,
            "impact_score": 1,
            "detectability_score": 1,
            "mitigation_strategy": "None",
        },
    )
    assert unauth_res.status_code == 403

    # 3. Missing change justification reason
    no_reason_headers = get_auth_headers(change_reason="")
    no_reason_res = client.post(
        "/api/v1/quality/rbqm/ract/factors",
        headers=no_reason_headers,
        json={
            "study_id": "STUDY-ERR",
            "category": "CONSENT",
            "risk_topic": "No Reason Test",
            "risk_description": "Should fail",
            "probability_score": 1,
            "impact_score": 1,
            "detectability_score": 1,
            "mitigation_strategy": "None",
        },
    )
    assert no_reason_res.status_code == 403
