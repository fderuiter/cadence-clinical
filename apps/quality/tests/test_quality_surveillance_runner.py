"""Unit and Integration Tests for Autonomous Quality Surveillance Runner (ADR-2213).

Validates:
- Autonomous cross-domain data harvesting from execution services
- Sample-gated statistical power validation (ICH E6(R2)/(R3))
- Multi-domain finding coalescing and composite risk scoring
- Idempotent 21 CFR Part 11 Deviation & KRI alert dispatch
- REST endpoint POST /api/v1/quality/surveillance/run

Requirements:
- PRD-CSM-001: Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis
- PRD-CSM-002: Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection
- PRD-CSM-003: Calendar & Timestamp Pattern Clustering
- PRD-CSM-004: Duplicate Patient Record & Anatomical Fabrication Detection
- PRD-SYS-001: 21 CFR Part 11 Immutable Audit Logging
- ADR-2213: Autonomous Cross-Domain Quality Surveillance Runner Architecture
"""

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager, transactional
from apps.quality.adapters.execution_client import (
    QualityExecutionClient,
    mock_audit_events_records,
    mock_image_attachments_records,
    mock_sdtm_domain_records,
)
from apps.quality.adapters.models import Base
from apps.quality.adapters.repositories import SQLQualityRepository
from apps.quality.application.services.data_integrity_sentinel_service import (
    DataIntegritySentinelService,
)
from apps.quality.application.services.quality_surveillance_runner import (
    QualitySurveillanceRunner,
)
from apps.quality.application.services.statistical_forensics_service import (
    StatisticalForensicsService,
)
from apps.quality.domain.surveillance_models import (
    HarvestedSurveillanceBundle,
    SiteSamplePowerStatus,
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
    change_reason: str = "Automated Quality Surveillance Runner test execution",
) -> dict[str, str]:
    return build_gateway_headers(
        user_id="lead.csm.biostatistician@cadence.io",
        roles=roles,
        change_reason=change_reason,
    )


@pytest.mark.asyncio
@transactional
async def test_surveillance_runner_sample_gating_ich_e6_r3():
    """Validate that sites below statistical power (N < 15 and < 50 obs) are gated from

    Benford and Mahalanobis checks, while deterministic sentinels run unconditionally.

    @req:PRD-CSM-001, PRD-CSM-002, PRD-CSM-004, ADR-2213
    """
    repo = SQLQualityRepository()
    forensics_srv = StatisticalForensicsService(repo)
    integrity_srv = DataIntegritySentinelService(repo)
    runner = QualitySurveillanceRunner(
        repo=repo,
        forensics_service=forensics_srv,
        data_integrity_service=integrity_srv,
    )

    # Site 101: 20 subjects (Sufficient Power)
    # Site 102: 5 subjects (Insufficient Power - Sample Gated)
    subjects = []
    for i in range(20):
        subjects.append(
            {
                "subject_id": f"SUBJ-101-{i:03d}",
                "site_id": "SITE-101",
                "demographics": {"age": 45.0 + i, "sex": "M"},
                "vital_signs": {"systolic_bp": 120.0 + i, "diastolic_bp": 80.0},
                "ecg_intervals": {"pr_interval_ms": 160.0},
                "lab_panel": {"creatinine": 0.9},
                "medical_history": ["HTN"],
            }
        )
    for i in range(5):
        subjects.append(
            {
                "subject_id": f"SUBJ-102-{i:03d}",
                "site_id": "SITE-102",
                "demographics": {"age": 50.0 + i, "sex": "F"},
                "vital_signs": {"systolic_bp": 125.0 + i, "diastolic_bp": 82.0},
                "ecg_intervals": {"pr_interval_ms": 165.0},
                "lab_panel": {"creatinine": 1.0},
                "medical_history": ["DM2"],
            }
        )

    bundle = HarvestedSurveillanceBundle(
        study_id="STUDY-POWER-TEST",
        subjects=subjects,
        site_observations={
            "SITE-101": [
                {
                    "systolic_bp": 120.0 + j,
                    "diastolic_bp": 80.0,
                    "heart_rate": 72.0,
                    "lab_creatinine": 0.9,
                }
                for j in range(60)
            ],
            "SITE-102": [
                {
                    "systolic_bp": 125.0 + j,
                    "diastolic_bp": 82.0,
                    "heart_rate": 75.0,
                    "lab_creatinine": 1.0,
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
    assert gating["SITE-101"] == SiteSamplePowerStatus.SUFFICIENT_POWER
    assert gating["SITE-102"] == SiteSamplePowerStatus.SAMPLE_GATED_INSUFFICIENT_POWER

    summary = await runner.execute_surveillance(
        study_id="STUDY-POWER-TEST",
        bundle=bundle,
        min_subjects_power=15,
        min_observations_power=50,
    )

    assert summary.total_sites == 2
    assert summary.gated_sites_count == 1
    assert summary.analyzed_sites_count == 1

    site_102_summary = next(
        s for s in summary.site_summaries if s.site_id == "SITE-102"
    )
    assert site_102_summary.is_sample_gated is True
    assert (
        site_102_summary.power_status
        == SiteSamplePowerStatus.SAMPLE_GATED_INSUFFICIENT_POWER
    )
    assert site_102_summary.details["forensics_analyzed"] is False


@pytest.mark.asyncio
@transactional
async def test_surveillance_runner_e2e_autonomous_harvest():
    """Validate autonomous end-to-end data harvesting from execution client and synthesis.

    @req:PRD-CSM-001, PRD-CSM-002, PRD-CSM-003, PRD-CSM-004, PRD-SYS-001, ADR-2213
    """
    repo = SQLQualityRepository()
    forensics_srv = StatisticalForensicsService(repo)
    integrity_srv = DataIntegritySentinelService(repo)
    exec_client = QualityExecutionClient()
    runner = QualitySurveillanceRunner(
        repo=repo,
        forensics_service=forensics_srv,
        data_integrity_service=integrity_srv,
        execution_client=exec_client,
    )

    study_id = "STUDY-HARVEST-AUTONOMOUS"

    # Set up mock SDTM Dataset-JSON records in ExecutionClient
    mock_sdtm_domain_records[study_id] = {
        "DM": {
            "DM": [
                {
                    "USUBJID": f"SUBJ-A-{i:03d}",
                    "SITEID": "SITE-A",
                    "AGE": 40 + i,
                    "SEX": "M",
                    "RACE": "WHITE",
                    "ETHNIC": "NOT_HISPANIC",
                    "ARMCD": "TRT_A",
                }
                for i in range(25)
            ]
            + [
                {
                    "USUBJID": f"SUBJ-B-{i:03d}",
                    "SITEID": "SITE-B",
                    "AGE": 50 + i,
                    "SEX": "F",
                    "RACE": "ASIAN",
                    "ETHNIC": "NOT_HISPANIC",
                    "ARMCD": "TRT_B",
                }
                for i in range(25)
            ]
        },
        "VS": {
            "VS": [
                {
                    "USUBJID": f"SUBJ-A-{i:03d}",
                    "VSTESTCD": "SYSBP",
                    "VSSTRESN": 120.0 + (i % 10),
                }
                for i in range(25)
            ]
            + [
                {"USUBJID": f"SUBJ-A-{i:03d}", "VSTESTCD": "DIABP", "VSSTRESN": 80.0}
                for i in range(25)
            ]
            + [
                {"USUBJID": f"SUBJ-B-{i:03d}", "VSTESTCD": "SYSBP", "VSSTRESN": 130.0}
                for i in range(25)
            ]
        },
        "LB": {
            "LB": [
                {"USUBJID": f"SUBJ-A-{i:03d}", "LBTESTCD": "CREAT", "LBSTRESN": 0.95}
                for i in range(25)
            ]
        },
        "EG": {
            "EG": [
                {"USUBJID": f"SUBJ-A-{i:03d}", "EGTESTCD": "PR", "EGSTRESN": 160.0}
                for i in range(25)
            ]
        },
    }

    mock_audit_events_records[study_id] = [
        {
            "event_id": f"AUD-{i}",
            "study_id": study_id,
            "site_id": "SITE-A",
            "timestamp": f"2026-08-20T10:0{i:02d}:00Z",
            "user_id": "crc-1",
        }
        for i in range(10)
    ]
    mock_image_attachments_records[study_id] = []

    # Execute surveillance with autonomous harvesting (bundle=None)
    summary = await runner.execute_surveillance(
        study_id=study_id,
        bundle=None,
        min_subjects_power=15,
        min_observations_power=20,
    )

    assert summary.study_id == study_id
    assert summary.total_sites == 2
    assert summary.status == "COMPLETED"
    assert summary.total_subjects == 50
    assert summary.analyzed_sites_count >= 1


@pytest.mark.asyncio
@transactional
async def test_surveillance_runner_idempotent_deviation_logging():
    """Validate that recurring surveillance runs create Part 11 deviations idempotently without duplication.

    @req:PRD-SYS-001, PRD-CSM-004, ADR-2213
    """
    repo = SQLQualityRepository()
    forensics_srv = StatisticalForensicsService(repo)
    integrity_srv = DataIntegritySentinelService(repo)
    runner = QualitySurveillanceRunner(
        repo=repo,
        forensics_service=forensics_srv,
        data_integrity_service=integrity_srv,
    )

    study_id = "STUDY-IDEMPOTENT-DEV"

    # Inject duplicate patient records across Site-101 and Site-104
    subjects = [
        {
            "subject_id": "SUBJ-ORIG-101",
            "site_id": "SITE-101",
            "demographics": {
                "age": 52.0,
                "sex": "M",
                "race": "WHITE",
                "height_cm": 178.0,
                "weight_kg": 82.0,
            },
            "vital_signs": {
                "systolic_bp": 138.0,
                "diastolic_bp": 88.0,
                "heart_rate": 74.0,
            },
            "ecg_intervals": {
                "pr_interval_ms": 164.0,
                "qrs_duration_ms": 92.0,
                "qt_interval_ms": 410.0,
                "qtc_ms": 420.0,
            },
            "lab_panel": {
                "creatinine": 1.15,
                "alt": 28.0,
                "ast": 24.0,
                "fasting_glucose": 98.0,
            },
            "medical_history": ["HTN", "HYPERLIPIDEMIA"],
        },
        {
            "subject_id": "SUBJ-CLONE-104",
            "site_id": "SITE-104",
            "demographics": {
                "age": 52.0,
                "sex": "M",
                "race": "WHITE",
                "height_cm": 178.0,
                "weight_kg": 82.0,
            },
            "vital_signs": {
                "systolic_bp": 138.0,
                "diastolic_bp": 88.0,
                "heart_rate": 74.0,
            },
            "ecg_intervals": {
                "pr_interval_ms": 164.0,
                "qrs_duration_ms": 92.0,
                "qt_interval_ms": 410.0,
                "qtc_ms": 420.0,
            },
            "lab_panel": {
                "creatinine": 1.15,
                "alt": 28.0,
                "ast": 24.0,
                "fasting_glucose": 98.0,
            },
            "medical_history": ["HTN", "HYPERLIPIDEMIA"],
        },
    ]

    bundle = HarvestedSurveillanceBundle(
        study_id=study_id,
        subjects=subjects,
        site_observations={"SITE-101": [], "SITE-104": []},
        audit_events=[],
        image_attachments=[],
    )

    # First Run: should create deviations for the fraud finding
    summary_1 = await runner.execute_surveillance(
        study_id=study_id,
        bundle=bundle,
        auto_dispatch_deviations=True,
    )
    assert summary_1.anomalies_count >= 1
    assert summary_1.deviations_dispatched_count >= 1

    deviations_after_run_1 = await repo.get_deviations()
    count_1 = len(deviations_after_run_1)
    assert count_1 >= 1

    # Second Run: should detect existing open deviation and avoid duplicate creation
    summary_2 = await runner.execute_surveillance(
        study_id=study_id,
        bundle=bundle,
        auto_dispatch_deviations=True,
    )
    assert summary_2.status == "COMPLETED"
    deviations_after_run_2 = await repo.get_deviations()
    count_2 = len(deviations_after_run_2)

    # Deviation count must remain identical due to idempotency deduplication
    assert count_2 == count_1


def test_surveillance_router_endpoint_post_run():
    """Validate POST /api/v1/quality/surveillance/run REST API endpoint.

    @req:PRD-CSM-001, PRD-CSM-004, PRD-SYS-001, ADR-2213
    """
    client = TestClient(app)
    headers = get_auth_headers()

    study_id = "STUDY-REST-API-TEST"
    mock_sdtm_domain_records[study_id] = {
        "DM": {
            "DM": [
                {
                    "USUBJID": f"SUBJ-API-{i:03d}",
                    "SITEID": "SITE-101",
                    "AGE": 45 + i,
                    "SEX": "M",
                }
                for i in range(20)
            ]
        },
        "VS": {
            "VS": [
                {"USUBJID": f"SUBJ-API-{i:03d}", "VSTESTCD": "SYSBP", "VSSTRESN": 122.0}
                for i in range(20)
            ]
        },
    }
    mock_audit_events_records[study_id] = []
    mock_image_attachments_records[study_id] = []

    response = client.post(
        "/api/v1/quality/surveillance/run",
        json={
            "study_id": study_id,
            "run_name": "API-Test-Surveillance-Run",
            "auto_dispatch_deviations": True,
            "min_subjects_power": 15,
            "min_observations_power": 20,
        },
        headers=headers,
    )

    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == study_id
    assert data["run_name"] == "API-Test-Surveillance-Run"
    assert data["status"] == "COMPLETED"
    assert "site_summaries" in data
    assert isinstance(data["site_summaries"], list)
    assert len(data["site_summaries"]) >= 1
