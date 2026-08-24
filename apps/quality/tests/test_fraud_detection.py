"""Comprehensive Verification Suite for Statistical Fraud & CSM Sentinels.

Requirements:
- PRD-SYS-174: Centralized Statistical Monitoring & Forensic Fraud Detection Engine
- PRD-SYS-175: Forensic Data Integrity Sentinels & Perceptual Image Hashing Tracer
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering (Weekend/Holiday Entry Inconsistencies)
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

import math
import random

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager, transactional
from apps.quality.adapters.models import Base
from apps.quality.adapters.repositories import SQLQualityRepository
from apps.quality.application.services.data_integrity_sentinel_service import (
    DataIntegritySentinelService,
    DuplicatePatientRecordEngine,
    PerceptualImageHashingEngine,
    TemporalBehavioralAnomalyEngine,
)
from apps.quality.application.services.quality_surveillance_runner import (
    HarvestedSurveillanceBundle,
    QualitySurveillanceRunner,
)
from apps.quality.application.services.statistical_forensics_service import (
    BenfordAnalysisEngine,
    MultivariateMahalanobisEngine,
    StatisticalForensicsService,
    TerminalDigitRoundingEngine,
    VarianceShrinkageKLScanner,
)
from apps.quality.domain.models import (
    BenfordConformity,
    DeviationSeverity,
    ForensicsAnomalyFlag,
    ImageTransformationType,
    RoundingBiasTier,
    SimilarityMatchTier,
)
from apps.quality.domain.surveillance_models import SiteSamplePowerStatus
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
    change_reason: str = "Forensic statistical fraud surveillance verification",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="csm.forensic.lead@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


# ---------------------------------------------------------------------------
# Section 1: PRD-SYS-174 / PRD-CSM-001 / PRD-CSM-002: Statistical Fraud & CSM
# ---------------------------------------------------------------------------


def test_benford_law_first_digit_goodness_of_fit():
    """Validate first digit distribution against theoretical Benford's Law and Nigrini conformity tiers.

    @req:PRD-SYS-174
    """
    rng = random.Random(42)

    # 1. Benford-compliant distribution (log-normal spanning multiple orders of magnitude)
    compliant_values = [math.exp(rng.gauss(3.5, 1.8)) for _ in range(600)]
    res_compliant = BenfordAnalysisEngine.analyze_digits(compliant_values)

    assert res_compliant["sample_size"] == 600
    assert res_compliant["mad"] < 0.015
    assert res_compliant["conformity"] in (
        BenfordConformity.CLOSE_CONFORMITY,
        BenfordConformity.ACCEPTABLE_CONFORMITY,
        BenfordConformity.MARGINAL_CONFORMITY,
    )
    assert res_compliant["p_value"] > 0.001

    # 2. Fabricated dataset with uniform leading digit distribution (11% each digit 1-9)
    fabricated_values = []
    for d in range(1, 10):
        fabricated_values.extend(
            [float(d * 100 + rng.randint(1, 99)) for _ in range(50)]
        )

    res_fabricated = BenfordAnalysisEngine.analyze_digits(fabricated_values)
    assert res_fabricated["sample_size"] == 450
    assert res_fabricated["mad"] > 0.015
    assert res_fabricated["conformity"] == BenfordConformity.NON_CONFORMING
    assert res_fabricated["p_value"] < 0.001
    assert res_fabricated["chi_square"] > 26.124


def test_terminal_digit_and_repeated_values_rounding_bias():
    """Validate terminal digit analysis detecting 0 and 5 clustering and repeated clinical values.

    @req:PRD-SYS-174
    """
    rng = random.Random(99)

    # 1. Natural physiological variation (unrounded blood pressure)
    natural_values = [round(rng.gauss(122.5, 9.5), 1) for _ in range(500)]
    res_natural = TerminalDigitRoundingEngine.analyze_terminal_digits(natural_values)

    assert res_natural["sample_size"] == 500
    assert res_natural["rounding_bias_tier"] == RoundingBiasTier.NORMAL
    assert 0.15 <= res_natural["zero_five_index"] <= 0.28
    assert res_natural["p_value"] > 0.001

    # 2. Heavy human rounding / estimated data (75% of measurements rounded to ending 0 or 5)
    rounded_values = []
    for _ in range(400):
        val = rng.gauss(120.0, 10.0)
        if rng.random() < 0.75:
            val = round(val / 5.0) * 5.0
        rounded_values.append(float(val))

    res_rounded = TerminalDigitRoundingEngine.analyze_terminal_digits(rounded_values)
    assert res_rounded["sample_size"] == 400
    assert res_rounded["zero_five_index"] >= 0.50
    assert res_rounded["rounding_bias_tier"] == RoundingBiasTier.SEVERE_ROUNDING
    assert res_rounded["p_value"] < 0.001


def test_cross_site_variance_shrinkage_and_kl_divergence():
    """Validate detection of artificially low variance (variance shrinkage) and high KL divergence.

    @req:PRD-SYS-174
    """
    rng = random.Random(777)
    pooled_means = {"systolic_bp": 120.0, "diastolic_bp": 80.0}
    pooled_vars = {"systolic_bp": 100.0, "diastolic_bp": 36.0}

    # 1. Compliant site with expected physiological standard deviations (~10.0 and ~6.0)
    normal_site = {
        "systolic_bp": [rng.gauss(120.2, 9.7) for _ in range(50)],
        "diastolic_bp": [rng.gauss(80.1, 5.8) for _ in range(50)],
    }
    res_normal = VarianceShrinkageKLScanner.analyze_variance_and_kl(
        normal_site, pooled_means, pooled_vars
    )
    assert not res_normal["is_variance_shrunk"]
    assert res_normal["variance_ratio"] > 0.60
    assert res_normal["p_value"] > 0.01
    assert res_normal["kl_divergence"] < 0.60

    # 2. Fraudulent site with artificially deflated variance (variance shrinkage ratio <= 0.30, P < 0.001)
    shrunk_site = {
        "systolic_bp": [rng.gauss(120.0, 0.35) for _ in range(50)],
        "diastolic_bp": [rng.gauss(80.0, 0.25) for _ in range(50)],
    }
    res_shrunk = VarianceShrinkageKLScanner.analyze_variance_and_kl(
        shrunk_site, pooled_means, pooled_vars
    )
    assert res_shrunk["is_variance_shrunk"] is True
    assert res_shrunk["variance_ratio"] <= 0.20
    assert res_shrunk["p_value"] < 0.001
    assert res_shrunk["kl_divergence"] > 2.0


def test_multivariate_mahalanobis_and_correlation_distance():
    """Validate multivariate centroid estimation, correlation distortion, and Mahalanobis distance.

    @req:PRD-SYS-174
    """
    rng = random.Random(321)

    # Generate pooled population data (SBP, DBP, Heart Rate)
    pooled_rows = []
    for _ in range(250):
        sbp = rng.gauss(120.0, 10.0)
        dbp = 0.65 * sbp + rng.gauss(0.0, 4.0)
        hr = rng.gauss(72.0, 8.0)
        pooled_rows.append([sbp, dbp, hr])

    pooled_means, pooled_cov, pooled_corr = (
        MultivariateMahalanobisEngine.compute_centroid_and_cov(pooled_rows)
    )
    pooled_cov_inv = MultivariateMahalanobisEngine._invert_matrix(pooled_cov)

    assert len(pooled_means) == 3
    assert pooled_corr[0][1] > 0.60  # Positive physiological correlation

    # Site with distorted inverted correlation and outlier centroid
    anomalous_rows = []
    for _ in range(50):
        sbp = rng.gauss(160.0, 10.0)
        dbp = -0.6 * sbp + rng.gauss(170.0, 4.0)
        hr = rng.gauss(110.0, 10.0)
        anomalous_rows.append([sbp, dbp, hr])

    site_means, _, site_corr = MultivariateMahalanobisEngine.compute_centroid_and_cov(
        anomalous_rows
    )
    d_m, p_val = MultivariateMahalanobisEngine.compute_mahalanobis_distance(
        site_means, pooled_means, pooled_cov_inv, sample_size=50
    )
    corr_dist = MultivariateMahalanobisEngine.compute_correlation_matrix_distance(
        site_corr, pooled_corr
    )

    assert d_m > 4.0
    assert p_val < 0.001
    assert corr_dist > 1.0


@pytest.mark.asyncio
@transactional
async def test_sample_gating_under_ich_e6_r3():
    """Validate sample-gated surveillance: sites with N < 15 or < 50 obs are gated from Benford/Mahalanobis.

    @req:PRD-SYS-174
    """
    repo = SQLQualityRepository()
    forensics_srv = StatisticalForensicsService(repo)
    integrity_srv = DataIntegritySentinelService(repo)
    runner = QualitySurveillanceRunner(
        repo=repo,
        forensics_service=forensics_srv,
        data_integrity_service=integrity_srv,
    )

    # Site 1: 20 subjects, 60 obs (Sufficient statistical power -> Gating PASS)
    # Site 2: 4 subjects, 10 obs (Underpowered -> Sample-gated from Benford/Mahalanobis)
    subjects = []
    for i in range(20):
        subjects.append(
            {
                "subject_id": f"SUBJ-PWR-A-{i:02d}",
                "site_id": "SITE-POWER-A",
                "demographics": {"age": 40 + i, "sex": "M"},
                "vital_signs": {"systolic_bp": 120.0 + i, "diastolic_bp": 80.0},
                "ecg_intervals": {"pr_interval_ms": 160.0},
                "lab_panel": {"creatinine": 1.0},
                "medical_history": ["HTN"],
            }
        )
    for i in range(4):
        subjects.append(
            {
                "subject_id": f"SUBJ-PWR-B-{i:02d}",
                "site_id": "SITE-POWER-B",
                "demographics": {"age": 50 + i, "sex": "F"},
                "vital_signs": {"systolic_bp": 125.0 + i, "diastolic_bp": 82.0},
                "ecg_intervals": {"pr_interval_ms": 165.0},
                "lab_panel": {"creatinine": 1.1},
                "medical_history": ["DM2"],
            }
        )

    bundle = HarvestedSurveillanceBundle(
        study_id="STUDY-SAMPLE-GATE-01",
        subjects=subjects,
        site_observations={
            "SITE-POWER-A": [
                {
                    "systolic_bp": 120.0 + j,
                    "diastolic_bp": 80.0,
                    "heart_rate": 72.0,
                    "lab_creatinine": 1.0,
                }
                for j in range(60)
            ],
            "SITE-POWER-B": [
                {
                    "systolic_bp": 125.0 + j,
                    "diastolic_bp": 82.0,
                    "heart_rate": 75.0,
                    "lab_creatinine": 1.1,
                }
                for j in range(10)
            ],
        },
        audit_events=[],
        image_attachments=[],
    )

    gating = runner.evaluate_sample_gating(
        bundle, min_subjects_power=15, min_observations_power=50
    )
    assert gating["SITE-POWER-A"] == SiteSamplePowerStatus.SUFFICIENT_POWER
    assert (
        gating["SITE-POWER-B"] == SiteSamplePowerStatus.SAMPLE_GATED_INSUFFICIENT_POWER
    )

    summary = await runner.execute_surveillance(
        study_id="STUDY-SAMPLE-GATE-01",
        bundle=bundle,
        min_subjects_power=15,
        min_observations_power=50,
    )

    assert summary.total_sites == 2
    assert summary.gated_sites_count == 1
    assert summary.analyzed_sites_count == 1

    site_b_summary = next(
        s for s in summary.site_summaries if s.site_id == "SITE-POWER-B"
    )
    assert site_b_summary.is_sample_gated is True


# ---------------------------------------------------------------------------
# Section 2: PRD-SYS-175 / PRD-CSM-004 / PRD-CSM-003: Forensic Data Integrity Sentinels
# ---------------------------------------------------------------------------


def test_perceptual_image_hashing_phash_and_transformations():
    """Validate perceptual image hashing (pHash) detecting duplicate images across rotations and brightness shifts.

    @req:PRD-SYS-175
    """
    # Create non-symmetric synthetic medical image (32x32)
    grid = [[0.0] * 32 for _ in range(32)]
    for r in range(10, 20):
        for c in range(5, 15):
            grid[r][c] = 200.0  # Hyper-intense lesion

    # 1. 90-degree rotated copy
    grid_rot90 = [[grid[31 - r][c] for r in range(32)] for c in range(32)]
    res_rot = PerceptualImageHashingEngine.compare_images(grid, grid_rot90)
    assert res_rot["is_match"] is True
    assert res_rot["hamming_distance"] <= 5
    assert res_rot["transformation_type"] in (
        ImageTransformationType.ROTATED_90,
        ImageTransformationType.ROTATED_270,
        ImageTransformationType.EXACT,
    )

    # 2. Brightness-adjusted copy (+25)
    grid_bright = [
        [min(255.0, grid[r][c] + 25.0) for c in range(32)] for r in range(32)
    ]
    res_bright = PerceptualImageHashingEngine.compare_images(grid, grid_bright)
    assert res_bright["is_match"] is True
    assert res_bright["hamming_distance"] <= 2
    assert res_bright["similarity_score"] >= 0.95

    # 3. Unrelated image
    rng = random.Random(888)
    grid_unrelated = [
        [float(rng.randint(0, 255)) for _ in range(32)] for _ in range(32)
    ]
    res_unrelated = PerceptualImageHashingEngine.compare_images(grid, grid_unrelated)
    assert res_unrelated["is_match"] is False
    assert res_unrelated["hamming_distance"] > 10


def test_cross_site_patient_record_cloning_detection():
    """Validate cross-site clinical feature vector matching and deterministic SHA-256 fingerprinting.

    @req:PRD-SYS-175
    """
    s1 = {
        "subject_id": "SUBJ-SITE-A-001",
        "site_id": "SITE-A",
        "demographics": {
            "age": 58,
            "sex": "M",
            "race": "WHITE",
            "height_cm": 178.0,
            "weight_kg": 82.0,
        },
        "vital_signs": {"systolic_bp": 126.0, "diastolic_bp": 82.0, "heart_rate": 68.0},
        "ecg_intervals": {
            "pr_interval_ms": 158.0,
            "qrs_duration_ms": 88.0,
            "qt_interval_ms": 395.0,
            "qtc_ms": 410.0,
        },
        "lab_panel": {"creatinine": 1.05, "alt": 22.0},
        "medical_history": ["I10", "E11.9"],
    }
    s2 = {
        "subject_id": "SUBJ-SITE-B-042",
        "site_id": "SITE-B",  # Cross-site duplicate!
        "demographics": {
            "age": 58,
            "sex": "m",
            "race": "white",
            "height_cm": 178.0,
            "weight_kg": 82.2,
        },
        "vital_signs": {"systolic_bp": 126.0, "diastolic_bp": 82.0, "heart_rate": 68.0},
        "ecg_intervals": {
            "pr_interval_ms": 158.0,
            "qrs_duration_ms": 88.0,
            "qt_interval_ms": 395.0,
            "qtc_ms": 410.0,
        },
        "lab_panel": {"creatinine": 1.05, "alt": 22.0},
        "medical_history": ["E11.9", "I10"],
    }

    res = DuplicatePatientRecordEngine.compare_subject_pair(s1, s2)
    assert res["composite_similarity"] >= 0.95
    assert res["is_match"] is True
    assert res["tier"] in (
        SimilarityMatchTier.EXACT,
        SimilarityMatchTier.HIGH_CONFIDENCE,
    )

    # Full cohort scan
    findings = DuplicatePatientRecordEngine.scan_all_subjects(
        study_id="STUDY-FRAUD-001",
        subjects=[s1, s2],
        similarity_threshold=0.85,
    )
    assert len(findings) == 1
    assert findings[0]["is_cross_site"] is True
    assert (
        findings[0]["anomaly_flag"] == ForensicsAnomalyFlag.CROSS_SITE_CLONING_SUSPICION
    )
    assert findings[0]["severity"] == DeviationSeverity.CRITICAL


def test_temporal_clustering_and_superhuman_burst_detection():
    """Validate detection of off-hours clustering, superhuman burst submissions (< 10s), and identical procedure intervals.

    @req:PRD-SYS-175
    """
    # 1. Midnight / weekend burst entries
    events_burst = [
        {
            "entry_id": f"EV-BOT-{i:02d}",
            "subject_id": f"SUBJ-{i:02d}",
            "site_id": "SITE-BOT-99",
            "timestamp": f"2026-08-16T02:15:{i * 2:02d}Z",  # Sunday 2 AM, 2s apart
            "assessment_type": "VITALS",
            "procedure_pair": "ECG_TO_BLOOD_DRAW",
            "elapsed_time_seconds": 450.0,  # Exact 450.0s elapsed across all entries
        }
        for i in range(10)
    ]

    res = TemporalBehavioralAnomalyEngine.scan_temporal_anomalies(
        study_id="STUDY-TEMPORAL-001",
        audit_events=events_burst,
    )

    assert res["anomalies_detected"] >= 2
    flags = [f["anomaly_flag"] for f in res["findings"]]
    assert ForensicsAnomalyFlag.SUSPICIOUS_TIMESTAMP_CLUSTER in flags
    assert ForensicsAnomalyFlag.RAPID_BURST_SUBMISSION in flags
    assert ForensicsAnomalyFlag.IDENTICAL_PROCEDURE_INTERVALS in flags


def test_full_rest_api_forensics_surveillance_pipeline():
    """Validate end-to-end REST API execution of statistical forensics surveillance with KRI breach generation.

    @req:PRD-SYS-174, PRD-SYS-175
    """
    client = TestClient(app)
    headers = get_auth_headers(
        change_reason="Execute full CSM statistical forensics surveillance"
    )

    sim_data = StatisticalForensicsService.generate_simulated_clinical_dataset(
        study_id="STUDY-E2E-FRAUD-01",
        num_normal_sites=3,
        include_benford_anomaly=True,
        include_rounding_bias=True,
        include_variance_shrinkage=True,
        include_multivariate_outlier=True,
        samples_per_site=50,
    )

    res = client.post(
        "/api/v1/quality/forensics/analyze",
        headers=headers,
        json={
            "study_id": "STUDY-E2E-FRAUD-01",
            "run_name": "CSM-SENTINEL-TRACER-01",
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
    assert res.status_code == 201, res.text
    data = res.json()
    assert data["study_id"] == "STUDY-E2E-FRAUD-01"
    assert data["total_sites"] == 7
    assert data["anomalous_sites_count"] >= 3
    assert len(data["site_results"]) == 7
