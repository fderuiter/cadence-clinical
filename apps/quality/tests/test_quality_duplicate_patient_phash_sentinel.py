"""Unit & Integration Tests for Duplicate Patient Record & Perceptual Image Hashing Sentinel.

Requirements:
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering (Weekend/Holiday Entry Inconsistencies)
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2199: Sovereign All-in-One eClinical Suite Expansion
- ICH E6(R2)/(R3) Risk-Based Quality Surveillance
"""

import random

import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager, transactional
from apps.quality.adapters.models import Base
from apps.quality.application.services.data_integrity_sentinel_service import (
    DataIntegritySentinelService,
    DuplicatePatientRecordEngine,
    PerceptualImageHashingEngine,
    TemporalBehavioralAnomalyEngine,
)
from apps.quality.domain.models import (
    DataIntegrityFindingStatus,
    DeviationSeverity,
    DeviationType,
    ForensicsAnomalyFlag,
    ImageTransformationType,
    RiskTier,
    SimilarityMatchTier,
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
    change_reason: str = "Centralized data integrity surveillance calibration",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.csm.auditor@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


# ---------------------------------------------------------------------------
# 1. Duplicate Patient Record Sentinel Unit Tests
# ---------------------------------------------------------------------------


def test_duplicate_patient_vector_similarity_calculations():
    """Validate continuous, categorical, and Jaccard vector similarity calculations.

    @req:PRD-CSM-004, ADR-2199
    """
    v1 = {"systolic_bp": 120.0, "diastolic_bp": 80.0, "heart_rate": 72.0}
    v2 = {"systolic_bp": 121.0, "diastolic_bp": 80.5, "heart_rate": 73.0}
    v_diff = {"systolic_bp": 170.0, "diastolic_bp": 110.0, "heart_rate": 115.0}

    sim_close = DuplicatePatientRecordEngine.compute_continuous_vector_similarity(
        v1, v2
    )
    sim_far = DuplicatePatientRecordEngine.compute_continuous_vector_similarity(
        v1, v_diff
    )
    assert sim_close > 0.90
    assert sim_far < 0.30

    d1 = {"sex": "M", "race": "WHITE", "blood_type": "O+"}
    d2 = {"sex": "M", "race": "white", "blood_type": "O+"}
    d_diff = {"sex": "F", "race": "ASIAN", "blood_type": "A-"}
    assert (
        DuplicatePatientRecordEngine.compute_categorical_similarity(
            d1, d2, ["sex", "race", "blood_type"]
        )
        == 1.0
    )
    assert (
        DuplicatePatientRecordEngine.compute_categorical_similarity(
            d1, d_diff, ["sex", "race", "blood_type"]
        )
        == 0.0
    )

    mh1 = ["I10", "E11.9", "K21.9"]
    mh2 = ["i10", "E11.9", "k21.9"]
    mh3 = ["I10", "Z00.0"]
    assert DuplicatePatientRecordEngine.compute_jaccard_similarity(mh1, mh2) == 1.0
    assert (
        0.20
        <= DuplicatePatientRecordEngine.compute_jaccard_similarity(mh1, mh3)
        <= 0.35
    )


def test_subject_fingerprint_deterministic_sha256():
    """Validate deterministic SHA-256 fingerprint generation for identical normalized subject records.

    @req:PRD-CSM-004, PRD-SYS-001, ADR-2199
    """
    s1 = {
        "demographics": {
            "age": 55,
            "sex": "M",
            "race": "WHITE",
            "height_cm": 175.0,
            "weight_kg": 80.0,
        },
        "vital_signs": {"systolic_bp": 125.0, "diastolic_bp": 82.0, "heart_rate": 70.0},
        "ecg_intervals": {
            "pr_interval_ms": 160.0,
            "qrs_duration_ms": 90.0,
            "qt_interval_ms": 400.0,
            "qtc_ms": 415.0,
        },
        "lab_panel": {"creatinine": 1.1, "alt": 25.0},
        "medical_history": ["I10", "E11.9"],
    }
    s2 = {
        "demographics": {
            "age": 55,
            "sex": "m",
            "race": "white",
            "height_cm": 175.0,
            "weight_kg": 80.0,
        },
        "vital_signs": {"systolic_bp": 125.0, "diastolic_bp": 82.0, "heart_rate": 70.0},
        "ecg_intervals": {
            "pr_interval_ms": 160.0,
            "qrs_duration_ms": 90.0,
            "qt_interval_ms": 400.0,
            "qtc_ms": 415.0,
        },
        "lab_panel": {"creatinine": 1.1, "alt": 25.0},
        "medical_history": ["E11.9", "I10"],  # Order permutation
    }
    fp1 = DuplicatePatientRecordEngine.compute_subject_fingerprint(s1)
    fp2 = DuplicatePatientRecordEngine.compute_subject_fingerprint(s2)
    assert fp1 == fp2
    assert len(fp1) == 64

    res = DuplicatePatientRecordEngine.compare_subject_pair(s1, s2)
    assert res["composite_similarity"] == 1.0
    assert res["tier"] == SimilarityMatchTier.EXACT
    assert res["fingerprint_matched"] is True


def test_cross_site_duplicate_patient_scan_detection():
    """Validate pairwise cross-site scan flagging duplicate enrollments and cloned subjects.

    @req:PRD-CSM-004, ADR-2199
    """
    subjects = [
        {
            "subject_id": "SUBJ-SITE1-001",
            "site_id": "SITE-101",
            "demographics": {
                "age": 62,
                "sex": "F",
                "race": "ASIAN",
                "height_cm": 160.0,
                "weight_kg": 58.0,
            },
            "vital_signs": {
                "systolic_bp": 118.0,
                "diastolic_bp": 76.0,
                "heart_rate": 68.0,
            },
            "ecg_intervals": {
                "pr_interval_ms": 155.0,
                "qrs_duration_ms": 86.0,
                "qt_interval_ms": 390.0,
                "qtc_ms": 405.0,
            },
            "lab_panel": {"creatinine": 0.85, "alt": 19.0},
            "medical_history": ["E11.9"],
        },
        {
            "subject_id": "SUBJ-SITE2-042",
            "site_id": "SITE-102",  # Different site!
            "demographics": {
                "age": 62,
                "sex": "F",
                "race": "ASIAN",
                "height_cm": 160.0,
                "weight_kg": 58.2,
            },
            "vital_signs": {
                "systolic_bp": 119.0,
                "diastolic_bp": 76.0,
                "heart_rate": 68.0,
            },
            "ecg_intervals": {
                "pr_interval_ms": 155.0,
                "qrs_duration_ms": 86.0,
                "qt_interval_ms": 390.0,
                "qtc_ms": 405.0,
            },
            "lab_panel": {"creatinine": 0.86, "alt": 19.0},
            "medical_history": ["E11.9"],
        },
        {
            "subject_id": "SUBJ-SITE1-002",
            "site_id": "SITE-101",
            "demographics": {
                "age": 28,
                "sex": "M",
                "race": "BLACK",
                "height_cm": 185.0,
                "weight_kg": 92.0,
            },
            "vital_signs": {
                "systolic_bp": 138.0,
                "diastolic_bp": 88.0,
                "heart_rate": 82.0,
            },
            "ecg_intervals": {
                "pr_interval_ms": 185.0,
                "qrs_duration_ms": 105.0,
                "qt_interval_ms": 430.0,
                "qtc_ms": 445.0,
            },
            "lab_panel": {"creatinine": 1.45, "alt": 48.0},
            "medical_history": ["K21.9"],
        },
    ]

    findings = DuplicatePatientRecordEngine.scan_all_subjects(
        study_id="STUDY-ONC-2026", subjects=subjects, similarity_threshold=0.85
    )
    assert len(findings) == 1
    f = findings[0]
    assert f["primary_subject_id"] == "SUBJ-SITE1-001"
    assert f["matched_subject_id"] == "SUBJ-SITE2-042"
    assert f["is_cross_site"] is True
    assert f["severity"] == DeviationSeverity.CRITICAL
    assert f["anomaly_flag"] == ForensicsAnomalyFlag.CROSS_SITE_CLONING_SUSPICION
    assert f["similarity_score"] > 0.95


# ---------------------------------------------------------------------------
# 2. Temporal & Behavioral Anomaly Scanner Unit Tests
# ---------------------------------------------------------------------------


def test_temporal_off_hours_and_weekend_clustering():
    """Validate detection of off-hours and weekend data entry clusters inconsistent with clinic hours.

    @req:PRD-CSM-003, ADR-2199
    """
    # 10 events submitted on Sunday at 02:00-03:00 AM
    events = [
        {
            "entry_id": f"EV-{i:02d}",
            "subject_id": f"SUBJ-{i:02d}",
            "site_id": "SITE-NIGHT-BURST",
            "timestamp": f"2026-08-16T02:{10 + i * 2:02d}:00Z",  # Sunday (weekday=6)
            "assessment_type": "VISIT_01",
        }
        for i in range(12)
    ]
    res = TemporalBehavioralAnomalyEngine.scan_temporal_anomalies(
        study_id="STUDY-ONC-2026", audit_events=events
    )
    assert res["anomalies_detected"] >= 1
    finding = res["findings"][0]
    assert finding["anomaly_flag"] == ForensicsAnomalyFlag.SUSPICIOUS_TIMESTAMP_CLUSTER
    assert finding["primary_site_id"] == "SITE-NIGHT-BURST"
    assert finding["forensic_metadata"]["off_hours_ratio"] == 1.0
    assert finding["forensic_metadata"]["weekend_ratio"] == 1.0


def test_temporal_rapid_burst_and_identical_elapsed_intervals():
    """Validate detection of superhuman rapid bursts (< 10s) and identical inter-procedure intervals.

    @req:PRD-CSM-003, ADR-2199
    """
    # Rapid burst entries at 3-second intervals
    events = [
        {
            "entry_id": f"EV-BURST-{i:02d}",
            "subject_id": f"SUBJ-{i:02d}",
            "site_id": "SITE-RAPID-BOT",
            "timestamp": f"2026-08-12T14:30:{i * 3:02d}Z",  # 3 seconds apart
            "assessment_type": "SCREENING_CRF",
            "procedure_pair": "EXAM_TO_VITALS",
            "elapsed_time_seconds": 650.0,  # Exact 650 seconds across all subjects!
        }
        for i in range(10)
    ]
    res = TemporalBehavioralAnomalyEngine.scan_temporal_anomalies(
        study_id="STUDY-ONC-2026", audit_events=events
    )
    assert res["anomalies_detected"] >= 2
    flags = [f["anomaly_flag"] for f in res["findings"]]
    assert ForensicsAnomalyFlag.RAPID_BURST_SUBMISSION in flags
    assert ForensicsAnomalyFlag.IDENTICAL_PROCEDURE_INTERVALS in flags


# ---------------------------------------------------------------------------
# 3. Perceptual Image Hashing (pHash / dHash) Unit Tests
# ---------------------------------------------------------------------------


def test_dct_2d_and_phash_deterministic_computation():
    """Validate 2D orthonormal Discrete Cosine Transform and 64-bit hexadecimal pHash computation.

    @req:PRD-CSM-004, ADR-2199
    """
    rng = random.Random(42)
    # 32x32 synthetic gradient image
    grid = [
        [float((r * 8 + c * 8 + rng.randint(-2, 2)) % 256) for c in range(32)]
        for r in range(32)
    ]

    phash1 = PerceptualImageHashingEngine.compute_phash(grid)
    dhash1 = PerceptualImageHashingEngine.compute_dhash(grid)

    assert len(phash1) == 16
    assert len(dhash1) == 16
    assert int(phash1, 16) > 0

    # Same input produces identical hash
    phash2 = PerceptualImageHashingEngine.compute_phash(grid)
    assert phash1 == phash2

    # Hamming distance of identical hash is 0
    assert PerceptualImageHashingEngine.compute_hamming_distance(phash1, phash2) == 0


def test_image_phash_rotation_and_transformation_invariance():
    """Validate pHash detection of duplicate radiological images rotated by 90, 180, 270 degrees or brightness-adjusted.

    @req:PRD-CSM-004, ADR-2199
    """
    # Create distinct non-symmetric pattern
    grid = [[0.0] * 32 for _ in range(32)]
    for r in range(12, 22):
        for c in range(8, 16):
            grid[r][c] = 220.0  # Bright rectangular lesion

    # 1. Rotated 90 degrees CW
    grid_rot90 = [[grid[31 - r][c] for r in range(32)] for c in range(32)]
    res_rot90 = PerceptualImageHashingEngine.compare_images(grid, grid_rot90)
    assert res_rot90["is_match"] is True
    assert res_rot90["hamming_distance"] <= 5
    assert res_rot90["transformation_type"] in (
        ImageTransformationType.ROTATED_90,
        ImageTransformationType.ROTATED_270,
        ImageTransformationType.EXACT,
    )

    # 2. Brightness adjusted (+30 across all pixels)
    grid_bright = [
        [min(255.0, grid[r][c] + 30.0) for c in range(32)] for r in range(32)
    ]
    res_bright = PerceptualImageHashingEngine.compare_images(grid, grid_bright)
    assert res_bright["is_match"] is True
    assert res_bright["hamming_distance"] <= 2
    assert res_bright["similarity_score"] >= 0.95

    # 3. Completely different random image should NOT match
    rng = random.Random(999)
    grid_diff = [[float(rng.randint(0, 255)) for _ in range(32)] for _ in range(32)]
    res_diff = PerceptualImageHashingEngine.compare_images(grid, grid_diff)
    assert res_diff["is_match"] is False
    assert res_diff["hamming_distance"] > 10


def test_cross_subject_image_phash_scan():
    """Validate cross-subject image scanning detecting reused scan attachments.

    @req:PRD-CSM-004, ADR-2199
    """
    grid1 = [[float((r * 7 + c * 3) % 256) for c in range(32)] for r in range(32)]
    grid1_rot90 = [[grid1[31 - r][c] for r in range(32)] for c in range(32)]
    grid_other = [[float((r * 15 - c * 9) % 256) for c in range(32)] for r in range(32)]

    images = [
        {
            "image_id": "IMG-SUBJ1-CT",
            "subject_id": "SUBJ-001",
            "site_id": "SITE-101",
            "modality": "CHEST_CT",
            "pixel_matrix": grid1,
        },
        {
            "image_id": "IMG-SUBJ2-CT",
            "subject_id": "SUBJ-042",
            "site_id": "SITE-104",  # Different site and subject!
            "modality": "CHEST_CT",
            "pixel_matrix": grid1_rot90,
        },
        {
            "image_id": "IMG-SUBJ3-CT",
            "subject_id": "SUBJ-099",
            "site_id": "SITE-102",
            "modality": "CHEST_CT",
            "pixel_matrix": grid_other,
        },
    ]

    findings = PerceptualImageHashingEngine.scan_all_images(
        study_id="STUDY-ONC-2026", image_attachments=images, hamming_threshold=8
    )
    assert len(findings) == 1
    f = findings[0]
    assert f["primary_subject_id"] == "SUBJ-001"
    assert f["matched_subject_id"] == "SUBJ-042"
    assert f["anomaly_flag"] == ForensicsAnomalyFlag.PERCEPTUAL_IMAGE_HASH_MATCH
    assert f["severity"] == DeviationSeverity.CRITICAL
    assert f["hamming_distance"] <= 5


# ---------------------------------------------------------------------------
# 4. DataIntegritySentinelService & Part 11 Investigation Dispatch Integration Tests
# ---------------------------------------------------------------------------


@pytest_asyncio.fixture
def test_client() -> TestClient:
    return TestClient(app)


@pytest_asyncio.fixture
def integrity_service() -> DataIntegritySentinelService:
    from apps.quality.main import _repo_instance

    return DataIntegritySentinelService(_repo_instance)


@transactional
async def test_service_execute_comprehensive_scan_with_investigation_dispatch(
    integrity_service: DataIntegritySentinelService,
):
    """Validate comprehensive scan execution, persistence, and automated Part 11 investigation ticket dispatch.

    @req:PRD-CSM-003, PRD-CSM-004, PRD-SYS-001, ADR-2199
    """
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id="STUDY-ONC-2026",
        num_subjects_per_site=6,
        include_duplicate_patient=True,
        include_temporal_burst=True,
        include_identical_intervals=True,
        include_image_duplication=True,
    )

    run = await integrity_service.execute_comprehensive_scan(
        study_id="STUDY-ONC-2026",
        subjects=sim["subjects"],
        audit_events=sim["audit_events"],
        image_attachments=sim["image_attachments"],
        user_id="lead.csm@cadence.io",
        user_role="quality_manager",
        change_reason="Automated inspection data integrity run",
        auto_dispatch_investigation=True,
    )

    assert run.id is not None
    assert run.study_id == "STUDY-ONC-2026"
    assert run.status == "COMPLETED"
    assert run.anomalies_detected_count >= 3
    assert run.risk_tier in (RiskTier.CRITICAL, RiskTier.HIGH)
    assert len(run.findings) >= 3

    # Verify at least one finding triggered a 21 CFR Part 11 investigation deviation
    investigated_findings = [
        f for f in run.findings if f.investigation_deviation_id is not None
    ]
    assert len(investigated_findings) >= 1
    sample_finding = investigated_findings[0]
    assert sample_finding.status == DataIntegrityFindingStatus.INVESTIGATING
    assert sample_finding.kri_evaluation_id is not None

    # Verify deviation persisted in DB
    dev = await integrity_service.repo.get_deviation_by_id(
        sample_finding.investigation_deviation_id
    )
    assert dev is not None
    assert dev.type == DeviationType.GCP_COMPLIANCE
    assert dev.impact_data is True
    assert dev.impact_compliance is True
    assert dev.source_system == "CSM_DATA_INTEGRITY_SENTINEL"

    # Verify finding lookup by subject
    subj_findings = await integrity_service.repo.get_data_integrity_findings_by_subject(
        sample_finding.primary_subject_id
    )
    assert len(subj_findings) >= 1


# ---------------------------------------------------------------------------
# 5. REST API Endpoints Integration Tests
# ---------------------------------------------------------------------------


def test_rest_duplicate_patient_scan_endpoint(test_client: TestClient):
    """Validate POST /api/v1/quality/sentinels/duplicate-patients/scan endpoint.

    @req:PRD-CSM-004, ADR-2199
    """
    headers = get_auth_headers()
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id="STUDY-API-TEST",
        num_subjects_per_site=8,
        include_duplicate_patient=True,
    )

    response = test_client.post(
        "/api/v1/quality/sentinels/duplicate-patients/scan",
        json={
            "study_id": "STUDY-API-TEST",
            "raw_subjects": sim["subjects"],
            "similarity_threshold": 0.85,
            "auto_dispatch_investigation": True,
        },
        headers=headers,
    )
    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == "STUDY-API-TEST"
    assert data["status"] == "COMPLETED"
    assert data["total_subjects_scanned"] == len(sim["subjects"])
    assert data["anomalies_detected_count"] >= 1
    assert len(data["findings"]) >= 1


def test_rest_temporal_anomaly_scan_endpoint(test_client: TestClient):
    """Validate POST /api/v1/quality/sentinels/temporal-anomalies/scan endpoint.

    @req:PRD-CSM-003, ADR-2199
    """
    headers = get_auth_headers()
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id="STUDY-API-TEST", num_subjects_per_site=8, include_temporal_burst=True
    )

    response = test_client.post(
        "/api/v1/quality/sentinels/temporal-anomalies/scan",
        json={
            "study_id": "STUDY-API-TEST",
            "raw_events": sim["audit_events"],
            "auto_dispatch_investigation": True,
        },
        headers=headers,
    )
    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == "STUDY-API-TEST"
    assert data["anomalies_detected_count"] >= 1


def test_rest_image_phash_scan_endpoint(test_client: TestClient):
    """Validate POST /api/v1/quality/sentinels/image-phash/scan endpoint.

    @req:PRD-CSM-004, ADR-2199
    """
    headers = get_auth_headers()
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id="STUDY-API-TEST",
        num_subjects_per_site=6,
        include_image_duplication=True,
    )

    response = test_client.post(
        "/api/v1/quality/sentinels/image-phash/scan",
        json={
            "study_id": "STUDY-API-TEST",
            "raw_images": sim["image_attachments"],
            "hamming_threshold": 10,
            "auto_dispatch_investigation": True,
        },
        headers=headers,
    )
    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == "STUDY-API-TEST"
    assert data["total_images_scanned"] == len(sim["image_attachments"])
    assert data["anomalies_detected_count"] >= 1


def test_rest_list_runs_and_dispatch_investigation(test_client: TestClient):
    """Validate listing sentinel runs and manual investigation deviation dispatch.

    @req:PRD-CSM-003, PRD-CSM-004, PRD-SYS-001, ADR-2199
    """
    headers = get_auth_headers()
    sim = DataIntegritySentinelService.generate_simulated_sentinel_dataset(
        study_id="STUDY-LIST-RUNS",
        num_subjects_per_site=5,
        include_duplicate_patient=True,
    )
    # Trigger a comprehensive scan
    scan_resp = test_client.post(
        "/api/v1/quality/sentinels/data-integrity/comprehensive-scan",
        json={
            "study_id": "STUDY-LIST-RUNS",
            "subjects": sim["subjects"],
            "audit_events": sim["audit_events"],
            "image_attachments": sim["image_attachments"],
            "auto_dispatch_investigation": False,
        },
        headers=headers,
    )
    assert scan_resp.status_code == 201
    run_data = scan_resp.json()
    run_id = run_data["id"]

    # List runs
    list_resp = test_client.get(
        "/api/v1/quality/sentinels/runs?study_id=STUDY-LIST-RUNS",
        headers=headers,
    )
    assert list_resp.status_code == 200
    runs = list_resp.json()
    assert len(runs) >= 1
    assert any(r["id"] == run_id for r in runs)

    # Get single run
    get_resp = test_client.get(
        f"/api/v1/quality/sentinels/runs/{run_id}",
        headers=headers,
    )
    assert get_resp.status_code == 200
    assert get_resp.json()["id"] == run_id

    # Get findings
    findings_resp = test_client.get(
        f"/api/v1/quality/sentinels/runs/{run_id}/findings",
        headers=headers,
    )
    assert findings_resp.status_code == 200
    findings = findings_resp.json()
    assert len(findings) >= 1
    target_finding_id = findings[0]["id"]

    # Manually dispatch investigation ticket
    dispatch_resp = test_client.post(
        f"/api/v1/quality/sentinels/findings/{target_finding_id}/dispatch-investigation",
        json={
            "finding_id": target_finding_id,
            "rationale": "Auditor confirmed data integrity violation",
        },
        headers=headers,
    )
    assert dispatch_resp.status_code == 200
    updated_finding = dispatch_resp.json()
    assert updated_finding["investigation_deviation_id"] is not None
    assert updated_finding["status"] == "INVESTIGATING"


def test_rest_simulation_endpoint(test_client: TestClient):
    """Validate POST /api/v1/quality/sentinels/simulate dataset generation endpoint.

    @req:PRD-CSM-003, PRD-CSM-004, ADR-2199
    """
    headers = get_auth_headers()
    response = test_client.post(
        "/api/v1/quality/sentinels/simulate",
        json={
            "study_id": "STUDY-SIM-2026",
            "num_subjects_per_site": 10,
            "include_duplicate_patient": True,
            "include_temporal_burst": True,
            "include_identical_intervals": True,
            "include_image_duplication": True,
        },
        headers=headers,
    )
    assert response.status_code == 200
    data = response.json()
    assert data["study_id"] == "STUDY-SIM-2026"
    assert data["subjects_count"] >= 40
    assert data["audit_events_count"] >= 40
    assert data["image_attachments_count"] >= 40
    assert len(data["simulated_scenarios"]) == 4


def test_rest_unauthorized_write_returns_forbidden(test_client: TestClient):
    """Validate RBAC enforcement returns 403 Forbidden for read-only roles attempting to launch scans.

    @req:PRD-SYS-001, ADR-2199
    """
    read_only_headers = build_gateway_headers(
        user_id="read.only.auditor@cadence.io",
        roles="regulatory_inspector_readonly",
        change_reason="Attempting unauthorized scan",
    )
    response = test_client.post(
        "/api/v1/quality/sentinels/duplicate-patients/scan",
        json={"study_id": "STUDY-FORBIDDEN"},
        headers=read_only_headers,
    )
    assert response.status_code == 403
