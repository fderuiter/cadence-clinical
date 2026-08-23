"""Unit and Integration Tests for Cross-Site Digit Preference (Benford's Law), Rounding Bias,

Multivariate Mahalanobis Distances, and Variance Shrinkage Sentinels.

Requirements:
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

import math
import random

import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base
from apps.quality.application.services.statistical_forensics_service import (
    BenfordAnalysisEngine,
    ChiSquareDistribution,
    MultivariateMahalanobisEngine,
    StatisticalForensicsService,
    TerminalDigitRoundingEngine,
    VarianceShrinkageKLScanner,
)
from apps.quality.domain.models import (
    BenfordConformity,
    ForensicsAnomalyFlag,
    RiskTier,
    RoundingBiasTier,
)
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
    change_reason: str = "Centralized statistical monitoring calibration",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.csm.biostatistician@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


def test_chi_square_distribution_accuracy():
    """Validate analytical Chi-Square CDF and survival function calculations.

    @req:PRD-CSM-001, ADR-2199
    """
    # df = 8 critical values: 15.507 -> ~0.05, 26.124 -> ~0.001
    p_8_05 = ChiSquareDistribution.survival(8.0, 15.507)
    p_8_001 = ChiSquareDistribution.survival(8.0, 26.124)
    assert 0.049 <= p_8_05 <= 0.051
    assert 0.0009 <= p_8_001 <= 0.0011

    # df = 9 critical values: 16.919 -> ~0.05, 27.877 -> ~0.001
    p_9_05 = ChiSquareDistribution.survival(9.0, 16.919)
    p_9_001 = ChiSquareDistribution.survival(9.0, 27.877)
    assert 0.049 <= p_9_05 <= 0.051
    assert 0.0009 <= p_9_001 <= 0.0011

    # Edge cases
    assert ChiSquareDistribution.survival(8.0, 0.0) == 1.0
    assert ChiSquareDistribution.survival(8.0, -5.0) == 1.0
    assert ChiSquareDistribution.cdf(8.0, 0.0) == 0.0
    assert ChiSquareDistribution.cdf(8.0, 100.0) > 0.999


def test_benford_law_goodness_of_fit_compliant_and_non_compliant():
    """Validate Benford's Law leading digit analysis on compliant vs fabricated datasets.

    @req:PRD-CSM-001, ADR-2199
    """
    rng = random.Random(123)

    # 1. Compliant lognormal clinical laboratory distribution (e.g. Viral Load / Biomarkers)
    compliant_values = [math.exp(rng.gauss(3.0, 1.8)) for _ in range(500)]
    res_compliant = BenfordAnalysisEngine.analyze_digits(compliant_values)

    assert res_compliant["sample_size"] == 500
    assert res_compliant["mad"] < 0.015
    assert res_compliant["conformity"] in (
        BenfordConformity.CLOSE_CONFORMITY,
        BenfordConformity.ACCEPTABLE_CONFORMITY,
        BenfordConformity.MARGINAL_CONFORMITY,
    )
    assert res_compliant["p_value"] > 0.001

    # 2. Fabricated dataset with unnatural uniform leading digits (digits 1-9 equally represented)
    fabricated_values = []
    for d in range(1, 10):
        # 50 values per digit
        fabricated_values.extend(
            [float(d * 100 + rng.randint(1, 90)) for _ in range(50)]
        )

    res_fabricated = BenfordAnalysisEngine.analyze_digits(fabricated_values)
    assert res_fabricated["sample_size"] == 450
    assert res_fabricated["mad"] > 0.015
    assert res_fabricated["conformity"] == BenfordConformity.NON_CONFORMING
    assert res_fabricated["p_value"] < 0.001
    assert res_fabricated["chi_square"] > 26.124


def test_terminal_digit_uniform_distribution_and_rounding_bias():
    """Validate terminal digit uniform distribution testing for rounding bias detection.

    @req:PRD-CSM-001, ADR-2199
    """
    rng = random.Random(456)

    # 1. True continuous unrounded measurements (Blood pressure with natural variation)
    unrounded_values = [round(rng.gauss(123.4, 11.2), 1) for _ in range(600)]
    res_unrounded = TerminalDigitRoundingEngine.analyze_terminal_digits(
        unrounded_values
    )

    assert res_unrounded["sample_size"] == 600
    assert res_unrounded["rounding_bias_tier"] == RoundingBiasTier.NORMAL
    assert 0.15 <= res_unrounded["zero_five_index"] <= 0.28
    assert res_unrounded["p_value"] > 0.001

    # 2. Heavy human rounding bias (e.g. Clinician rounding 70% of readings to ending 0 or 5)
    rounded_values = []
    for _ in range(400):
        val = rng.gauss(120.0, 10.0)
        if rng.random() < 0.70:
            val = round(val / 5.0) * 5.0
        rounded_values.append(float(val))

    res_rounded = TerminalDigitRoundingEngine.analyze_terminal_digits(rounded_values)
    assert res_rounded["sample_size"] == 400
    assert res_rounded["zero_five_index"] >= 0.45
    assert res_rounded["rounding_bias_tier"] == RoundingBiasTier.SEVERE_ROUNDING
    assert res_rounded["p_value"] < 0.001
    assert res_rounded["chi_square"] > 27.877


def test_multivariate_mahalanobis_distance_and_correlation_matrices():
    """Validate multivariate centroid estimation, correlation matrix distance, and Mahalanobis distance.

    @req:PRD-CSM-002, ADR-2199
    """
    rng = random.Random(789)

    # Generate pooled reference observations (SBP, DBP, Heart Rate)
    pooled_rows = []
    for _ in range(300):
        sbp = rng.gauss(120.0, 10.0)
        dbp = 0.7 * sbp + rng.gauss(0.0, 4.0)
        hr = rng.gauss(72.0, 8.0)
        pooled_rows.append([sbp, dbp, hr])

    pooled_means, pooled_cov, pooled_corr = (
        MultivariateMahalanobisEngine.compute_centroid_and_cov(pooled_rows)
    )
    pooled_cov_inv = MultivariateMahalanobisEngine._invert_matrix(pooled_cov)

    assert len(pooled_means) == 3
    assert 118.0 <= pooled_means[0] <= 122.0
    # Positive correlation between SBP and DBP
    assert pooled_corr[0][1] > 0.7

    # 1. Normal site (centroid close to pooled mean)
    site_normal_rows = []
    for _ in range(50):
        sbp = rng.gauss(121.0, 10.0)
        dbp = 0.7 * sbp + rng.gauss(0.0, 4.0)
        hr = rng.gauss(71.5, 8.0)
        site_normal_rows.append([sbp, dbp, hr])

    site_normal_means, _, site_normal_corr = (
        MultivariateMahalanobisEngine.compute_centroid_and_cov(site_normal_rows)
    )
    d_m_norm, p_m_norm = MultivariateMahalanobisEngine.compute_mahalanobis_distance(
        site_normal_means, pooled_means, pooled_cov_inv
    )
    corr_dist_norm = MultivariateMahalanobisEngine.compute_correlation_matrix_distance(
        site_normal_corr, pooled_corr
    )

    assert d_m_norm < 2.5
    assert p_m_norm > 0.05
    assert corr_dist_norm < 0.5

    # 2. Outlier site (extreme centroid shift and corrupted negative correlation)
    site_outlier_rows = []
    for _ in range(50):
        sbp = rng.gauss(165.0, 10.0)
        dbp = -0.6 * sbp + rng.gauss(180.0, 5.0)  # Inverted physiology
        hr = rng.gauss(110.0, 10.0)
        site_outlier_rows.append([sbp, dbp, hr])

    site_outlier_means, _, site_outlier_corr = (
        MultivariateMahalanobisEngine.compute_centroid_and_cov(site_outlier_rows)
    )
    d_m_out, p_m_out = MultivariateMahalanobisEngine.compute_mahalanobis_distance(
        site_outlier_means, pooled_means, pooled_cov_inv
    )
    corr_dist_out = MultivariateMahalanobisEngine.compute_correlation_matrix_distance(
        site_outlier_corr, pooled_corr
    )

    assert d_m_out > 5.0
    assert p_m_out < 0.001
    assert corr_dist_out > 1.0


def test_variance_shrinkage_and_kullback_leibler_divergence():
    """Validate variance shrinkage detection (fabricated low-variance data) and KL divergence.

    @req:PRD-CSM-002, ADR-2199
    """
    pooled_means = {"systolic_bp": 120.0, "diastolic_bp": 80.0}
    pooled_vars = {"systolic_bp": 100.0, "diastolic_bp": 36.0}  # std: 10.0 and 6.0

    # 1. Normal site with physiological variance (std ~10.0 and ~6.0)
    rng = random.Random(111)
    normal_site = {
        "systolic_bp": [rng.gauss(120.0, 9.8) for _ in range(50)],
        "diastolic_bp": [rng.gauss(80.0, 5.9) for _ in range(50)],
    }
    res_normal = VarianceShrinkageKLScanner.analyze_variance_and_kl(
        normal_site, pooled_means, pooled_vars
    )
    assert not res_normal["is_variance_shrunk"]
    assert res_normal["variance_ratio"] > 0.70
    assert res_normal["p_value"] > 0.05
    assert res_normal["kl_divergence"] < 0.50

    # 2. Fabricated site with artificially shrunk variance ("too perfect" data, std 0.5 vs 10.0)
    shrunk_site = {
        "systolic_bp": [rng.gauss(120.0, 0.4) for _ in range(50)],
        "diastolic_bp": [rng.gauss(80.0, 0.3) for _ in range(50)],
    }
    res_shrunk = VarianceShrinkageKLScanner.analyze_variance_and_kl(
        shrunk_site, pooled_means, pooled_vars
    )
    assert res_shrunk["is_variance_shrunk"]
    assert res_shrunk["variance_ratio"] < 0.10
    assert res_shrunk["p_value"] < 0.001
    assert res_shrunk["kl_divergence"] > 2.0


def test_simulation_dataset_generator():
    """Validate synthetic clinical dataset simulation for multi-site calibration.

    @req:PRD-CSM-001, PRD-CSM-002, ADR-2199
    """
    data = StatisticalForensicsService.generate_simulated_clinical_dataset(
        study_id="STUDY-ONC-2026",
        num_normal_sites=4,
        include_benford_anomaly=True,
        include_rounding_bias=True,
        include_variance_shrinkage=True,
        include_multivariate_outlier=True,
        samples_per_site=50,
    )
    assert len(data) == 8  # 4 normal + 4 anomalous
    assert "SITE-101" in data
    assert "SITE-BENFORD-ANOMALY" in data
    assert "SITE-ROUNDING-BIAS" in data
    assert "SITE-VARIANCE-SHRINKAGE" in data
    assert "SITE-MULTIVARIATE-OUTLIER" in data

    for site_id, obs in data.items():
        assert len(obs) == 50
        assert "systolic_bp" in obs[0]
        assert "diastolic_bp" in obs[0]
        assert "heart_rate" in obs[0]
        assert "lab_creatinine" in obs[0]


def test_forensics_rest_api_full_surveillance_and_kri_breach_alerts():
    """Validate full FastAPI REST API surveillance execution, anomaly flagging, and KRI breach alerts.

    @req:PRD-CSM-001, PRD-CSM-002, PRD-SYS-001, ADR-2199
    """
    client = TestClient(app)
    headers = get_auth_headers(change_reason="ICH E6(R3) Statistical Surveillance Scan")

    # 1. Execute Forensics Analysis
    sim_data = StatisticalForensicsService.generate_simulated_clinical_dataset(
        study_id="STUDY-CSM-2026",
        num_normal_sites=3,
        include_benford_anomaly=True,
        include_rounding_bias=True,
        include_variance_shrinkage=True,
        include_multivariate_outlier=True,
        samples_per_site=60,
    )

    analyze_res = client.post(
        "/api/v1/quality/forensics/analyze",
        headers=headers,
        json={
            "study_id": "STUDY-CSM-2026",
            "run_name": "CSM-SURVEILLANCE-Q3",
            "target_variables": [
                "systolic_bp",
                "diastolic_bp",
                "heart_rate",
                "lab_creatinine",
            ],
            "site_observations": sim_data,
            "auto_trigger_kri": True,
        },
    )
    assert analyze_res.status_code == 201, analyze_res.text
    run_data = analyze_res.json()

    assert run_data["study_id"] == "STUDY-CSM-2026"
    assert run_data["run_name"] == "CSM-SURVEILLANCE-Q3"
    assert run_data["total_sites"] == 7
    assert run_data["anomalous_sites_count"] >= 3
    run_id = run_data["id"]

    # Verify site results
    site_map = {res["site_id"]: res for res in run_data["site_results"]}

    # A. Benford Anomaly Site
    benford_site = site_map["SITE-BENFORD-ANOMALY"]
    assert benford_site["is_anomalous_outlier"] is True
    assert (
        ForensicsAnomalyFlag.BENFORD_NON_CONFORMANCE
        in benford_site["flagged_anomaly_reasons"]
    )
    assert benford_site["kri_evaluation_id"] is not None

    # B. Rounding Bias Site
    rounding_site = site_map["SITE-ROUNDING-BIAS"]
    assert (
        ForensicsAnomalyFlag.TERMINAL_DIGIT_ROUNDING_BIAS
        in rounding_site["flagged_anomaly_reasons"]
    )
    assert rounding_site["terminal_digit"]["zero_five_index"] >= 0.40

    # C. Variance Shrinkage Site
    shrunk_site = site_map["SITE-VARIANCE-SHRINKAGE"]
    assert (
        ForensicsAnomalyFlag.ANOMALOUS_VARIANCE_SHRINKAGE
        in shrunk_site["flagged_anomaly_reasons"]
    )
    assert shrunk_site["variance_shrinkage"]["variance_ratio"] < 0.25

    # D. Normal Compliant Site
    normal_site = site_map["SITE-101"]
    assert normal_site["risk_tier"] in (RiskTier.LOW, RiskTier.MEDIUM)

    # 2. List Runs
    list_runs_res = client.get(
        "/api/v1/quality/forensics/runs?study_id=STUDY-CSM-2026", headers=headers
    )
    assert list_runs_res.status_code == 200
    runs = list_runs_res.json()
    assert len(runs) >= 1
    assert runs[0]["id"] == run_id

    # 3. Get Run Details
    get_run_res = client.get(
        f"/api/v1/quality/forensics/runs/{run_id}", headers=headers
    )
    assert get_run_res.status_code == 200
    assert get_run_res.json()["id"] == run_id

    # 4. Get Site History
    site_hist_res = client.get(
        "/api/v1/quality/forensics/sites/SITE-BENFORD-ANOMALY", headers=headers
    )
    assert site_hist_res.status_code == 200
    history = site_hist_res.json()
    assert len(history) >= 1
    assert history[0]["site_id"] == "SITE-BENFORD-ANOMALY"

    # 5. Trigger KRI Alerts for Run
    trigger_res = client.post(
        f"/api/v1/quality/forensics/runs/{run_id}/trigger-kri-alerts",
        headers=headers,
    )
    assert trigger_res.status_code == 200

    # 6. Simulate Dataset Endpoint
    sim_res = client.post(
        "/api/v1/quality/forensics/simulate",
        headers=headers,
        json={
            "study_id": "STUDY-SIM-TEST",
            "num_normal_sites": 2,
            "samples_per_site": 25,
        },
    )
    assert sim_res.status_code == 200
    sim_payload = sim_res.json()
    assert sim_payload["study_id"] == "STUDY-SIM-TEST"
    assert sim_payload["total_sites"] == 6
