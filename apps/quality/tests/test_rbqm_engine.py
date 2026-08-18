"""Tests for Risk-Based Quality Management (RBQM), KRI Statistical Z-Scores, and QTL Breaches."""

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
    change_reason: str = "RBQM protocol risk calibration",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.cra@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


def test_ctq_factor_lifecycle():
    """Validate Critical-to-Quality (CtQ) factor setup and retrieval.

    @req:PRD-QLT-004
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="CtQ setup")

    # 1. Create CtQ Factor
    res = client.post(
        "/api/v1/quality/rbqm/ctq",
        headers=headers,
        json={
            "study_id": "STUDY-ONC-2026",
            "category": "PATIENT_SAFETY",
            "critical_aspect": "Absolute Neutrophil Count (ANC) Verification prior to Infusion",
            "risk_description": "Risk of severe neutropenic sepsis if dose is administered with ANC < 1.0",
            "impact_area": "Patient Safety & Toxicity Grading",
            "mitigation_strategy": "Mandatory eCRF hard-stop rule blocking IP kit dispensing until lab verified",
        },
    )
    assert res.status_code == 201, res.text
    data = res.json()
    assert data["study_id"] == "STUDY-ONC-2026"
    assert (
        data["critical_aspect"]
        == "Absolute Neutrophil Count (ANC) Verification prior to Infusion"
    )
    assert data["version_index"] == 1

    # 2. List CtQ Factors for study
    list_res = client.get(
        "/api/v1/quality/rbqm/ctq?study_id=STUDY-ONC-2026", headers=headers
    )
    assert list_res.status_code == 200
    factors = list_res.json()
    assert len(factors) >= 1
    assert any(f["id"] == data["id"] for f in factors)


def test_kri_definitions_and_auto_seeding():
    """Validate default TransCelerate KRI definition auto-seeding and custom KRI creation.

    @req:PRD-QLT-004
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="KRI definitions config")

    # 1. Listing should automatically seed default KRIs if empty
    res = client.get("/api/v1/quality/rbqm/kris", headers=headers)
    assert res.status_code == 200
    kris = res.json()
    assert len(kris) >= 5
    codes = [k["code"] for k in kris]
    assert "KRI_QUERY_AGE" in codes
    assert "KRI_AE_RATE" in codes

    # 2. Create custom KRI
    custom_res = client.post(
        "/api/v1/quality/rbqm/kris",
        headers=headers,
        json={
            "code": "KRI_PK_SAMPLE_HEMOLYSIS",
            "name": "PK Blood Sample Hemolysis Rate",
            "category": "DATA_INTEGRITY",
            "description": "Percentage of pharmacokinetic blood samples rejected due to hemolysis.",
            "calculation_formula": "(count(hemolyzed_samples) / count(total_samples)) * 100",
            "green_threshold": 2.0,
            "amber_threshold": 5.0,
            "red_threshold": 10.0,
            "weight": 2.5,
        },
    )
    assert custom_res.status_code == 201
    custom_kri = custom_res.json()
    assert custom_kri["code"] == "KRI_PK_SAMPLE_HEMOLYSIS"
    assert custom_kri["weight"] == 2.5


def test_kri_batch_evaluation_and_statistical_z_scores():
    """Validate statistical Z-score calculation and risk tier assignments across multi-site cohorts.

    @req:PRD-QLT-004
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Batch KRI evaluation")
    payload = {
        "study_id": "STUDY-RBQM-001",
        "kri_code": "KRI_QUERY_AGE",
        "site_raw_values": {
            "SITE-101": 5.0,
            "SITE-102": 5.0,
            "SITE-103": 5.0,
            "SITE-104": 35.0,  # Extreme outlier
        },
    }

    res = client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch", headers=headers, json=payload
    )
    assert res.status_code == 200, res.text
    evals = res.json()
    assert len(evals) == 4

    site_eval_map = {e["site_id"]: e for e in evals}
    assert site_eval_map["SITE-104"]["standardized_z_score"] > 1.0
    assert site_eval_map["SITE-104"]["risk_tier"] in ("HIGH", "CRITICAL", "MEDIUM")
    assert site_eval_map["SITE-101"]["standardized_z_score"] < 0

    # Query back evaluations
    get_evals = client.get(
        "/api/v1/quality/rbqm/evaluations?study_id=STUDY-RBQM-001", headers=headers
    )
    assert get_evals.status_code == 200
    assert len(get_evals.json()) >= 4


def test_site_risk_profile_computation_and_ranking():
    """Validate aggregated composite Site Risk Index computation and ranking.

    @req:PRD-QLT-004
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="Site risk scoring")

    # Also create an active deviation for SITE-HIGH
    client.post(
        "/api/v1/quality/deviations",
        headers=headers,
        json={
            "study_id": "STUDY-RANKING-001",
            "site_id": "SITE-HIGH",
            "title": "Serious GCP non-compliance",
            "description": "Unreported AE backlog",
            "severity": "CRITICAL",
            "type": "GCP_COMPLIANCE",
        },
    )

    # First evaluate some KRIs with a cohort
    client.post(
        "/api/v1/quality/rbqm/kris/evaluate-batch",
        headers=headers,
        json={
            "study_id": "STUDY-RANKING-001",
            "kri_code": "KRI_QUERY_AGE",
            "site_raw_values": {
                "SITE-LOW-1": 2.0,
                "SITE-LOW-2": 2.0,
                "SITE-LOW-3": 2.0,
                "SITE-HIGH": 50.0,
            },
        },
    )

    # Compute profiles
    compute_res = client.post(
        "/api/v1/quality/rbqm/site-risk-profiles/compute?study_id=STUDY-RANKING-001",
        headers=headers,
    )
    assert compute_res.status_code == 200
    profiles = compute_res.json()
    assert len(profiles) >= 2

    # Rank 1 must have the highest composite risk score
    assert profiles[0]["risk_rank"] == 1
    assert profiles[0]["site_id"] == "SITE-HIGH"
    assert profiles[0]["composite_risk_score"] > profiles[1]["composite_risk_score"]


def test_qtl_tolerance_limit_and_csr_narrative():
    """Validate Quality Tolerance Limit setup, breach evaluation, and automated CSR Section 9.6 narrative synthesis.

    @req:PRD-QLT-005
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="QTL evaluation")

    # 1. Create QTL
    qtl_res = client.post(
        "/api/v1/quality/rbqm/qtls",
        headers=headers,
        json={
            "study_id": "STUDY-PH3-QTL",
            "parameter_name": "Lost to Follow-up Rate",
            "target_value": 3.0,
            "tolerance_limit": 5.0,
            "unit": "%",
        },
    )
    assert qtl_res.status_code == 201
    qtl = qtl_res.json()
    qtl_id = qtl["id"]
    assert qtl["tolerance_limit"] == 5.0

    # 2. Evaluate Non-Breach (Observed 4.2% < 5.0%)
    non_breach_res = client.post(
        f"/api/v1/quality/rbqm/qtls/{qtl_id}/evaluate-breach",
        headers=headers,
        json={
            "observed_value": 4.2,
            "root_cause": "Minor seasonal holiday delay",
            "corrective_action_summary": "Follow-up contact initiated",
        },
    )
    assert non_breach_res.status_code == 200
    assert non_breach_res.json()["status"] == "NO_BREACH"

    # 3. Evaluate Breach (Observed 7.8% > 5.0%)
    breach_res = client.post(
        f"/api/v1/quality/rbqm/qtls/{qtl_id}/evaluate-breach",
        headers=headers,
        json={
            "observed_value": 7.8,
            "root_cause": "Regional transport disruption prevented subject attendance at Month 6 visit",
            "corrective_action_summary": "Deployed home health nursing for in-home sample collection and remote visits",
        },
    )
    assert breach_res.status_code == 200
    breach = breach_res.json()
    assert breach["observed_value"] == 7.8
    assert "CSR Section 9.6 QTL Summary" in breach["csr_narrative"]
    assert "Regional transport disruption" in breach["csr_narrative"]

    # 4. List QTL breaches
    list_breaches = client.get(
        "/api/v1/quality/rbqm/qtls/breaches?study_id=STUDY-PH3-QTL", headers=headers
    )
    assert list_breaches.status_code == 200
    breaches = list_breaches.json()
    assert len(breaches) >= 1
    assert breaches[0]["qtl_id"] == qtl_id
