"""Test suite for Asynchronous Protocol Digitization Stage DAG with USDM Compilation.

Requirements: PRD-DDF-001, PRD-SYS-001, PRD-MDR-007, PRD-CRF-004, PRD-CRF-005
"""

from __future__ import annotations

import hashlib
import hmac
import json
import time
from unittest.mock import patch

import pytest
from fastapi.testclient import TestClient

from apps.designer.adapters.digitization_job_store import (
    DigitizationJobStore,
    get_digitization_job_store,
)
from apps.designer.application.services.digitization_dag_service import (
    DigitizationDAGRunner,
)
from apps.designer.domain.digitization_dag_models import (
    DigitizationJobStatus,
    DigitizationStage,
    StageGateStatus,
)
from apps.designer.main import app as designer_app
from apps.designer.presentation.routers.digitization import (
    get_digitization_dag_runner,
)


def get_test_auth_headers(
    roles: str = "sponsor_designer",
    change_reason: str = "Automated DAG digitization test",
    user_id: str = "designer_user_001",
) -> dict[str, str]:
    """Generates canonical v2 gateway HMAC signature headers for testing."""
    timestamp = str(time.time())
    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        secret.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.fixture
def client() -> TestClient:
    return TestClient(designer_app)


@pytest.fixture(autouse=True)
async def reset_store():
    """Resets the in-memory DAG job store before each test."""
    store = get_digitization_job_store()
    await store.clear()


# =============================================================================
# UNIT & SERVICE TESTS
# =============================================================================


@pytest.mark.asyncio
async def test_dag_job_initialization_and_store():
    """Validate DAG job creation, persistence in job store, and query operations.

    @req:PRD-DDF-001
    """
    store = DigitizationJobStore()
    runner = DigitizationDAGRunner(job_store=store)

    doc_bytes = (
        b"%PDF-1.4\nProtocol Title: Oncology Phase II Study\n"
        b"Protocol ID: CDNC-ONC-201\nPhase: Phase II\n%%EOF"
    )

    job = await runner.initialize_job(
        file_content=doc_bytes,
        filename="oncology_protocol.pdf",
        study_id="study_onc_201",
        user_id="lead_designer",
    )

    assert job.job_id.startswith("job_dag_")
    assert job.status == DigitizationJobStatus.PENDING
    assert job.current_stage is None
    assert job.study_id == "study_onc_201"
    assert job.created_by == "lead_designer"
    assert "Oncology Phase II Study" in job.raw_text

    # Retrieve from store
    fetched = await store.get_job(job.job_id)
    assert fetched is not None
    assert fetched.job_id == job.job_id
    assert fetched.file_size_bytes == len(doc_bytes)

    # List jobs
    all_jobs = await store.list_jobs(study_id="study_onc_201")
    assert len(all_jobs) == 1
    assert all_jobs[0].job_id == job.job_id


@pytest.mark.asyncio
async def test_end_to_end_dag_pipeline_execution():
    """Validate full 5-stage sequential DAG execution with validation gates and USDM compilation.

    @req:PRD-DDF-001, @req:PRD-SYS-001, @req:PRD-MDR-007
    """
    store = DigitizationJobStore()
    runner = DigitizationDAGRunner(job_store=store)

    doc_text = (
        "Protocol Title: A Phase III Trial in Metastatic Breast Cancer\n"
        "Protocol ID: CDNC-BC-301\n"
        "Phase: Phase III\n"
        "Therapeutic Area: Oncology\n"
        "Section 1: Arms and Design\n"
        "Arm 1 (Active): Drug Alpha 50mg\n"
        "Arm 2 (Control): Standard Placebo\n"
        "Epochs: Screening, Treatment, Follow-up\n"
        "Inclusion Criteria:\n"
        "1. Age >= 18 years.\n"
        "2. Confirmed HER2+ breast cancer.\n"
        "Schedule of Activities:\n"
        "- Vital Signs at all visits.\n"
        "- 12-Lead ECG at Baseline and Week 4.\n"
        "- Safety Laboratory Panel at all visits.\n"
    )
    doc_bytes = b"%PDF-1.4\n" + doc_text.encode("utf-8") + b"\n%%EOF"

    job = await runner.initialize_job(
        file_content=doc_bytes,
        filename="breast_cancer_protocol.pdf",
        study_id="study_bc_301",
        user_id="oncology_pi",
    )

    # Execute full DAG
    completed_job = await runner.run_job(job.job_id)

    assert completed_job.status == DigitizationJobStatus.COMPLETED
    assert completed_job.error_message is None
    assert completed_job.final_usdm_payload is not None
    assert completed_job.final_usdm_payload.study_title != ""
    assert len(completed_job.synthesized_forms) > 0

    # Verify all 5 stage checkpoints were recorded and passed gates
    expected_stages = [
        DigitizationStage.LAYOUT_PARSING.value,
        DigitizationStage.SOA_EXTRACTION.value,
        DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING.value,
        DigitizationStage.ECRF_SYNTHESIS.value,
        DigitizationStage.USDM_COMPILATION.value,
    ]

    for stage_name in expected_stages:
        assert stage_name in completed_job.checkpoints
        ckpt = completed_job.checkpoints[stage_name]
        assert ckpt.gate_status == StageGateStatus.PASSED
        assert ckpt.status == StageGateStatus.PASSED
        assert ckpt.duration_ms >= 0.0
        assert ckpt.completed_at is not None
        assert len(ckpt.data) > 0


@pytest.mark.asyncio
async def test_schema_validation_gate_failure_stops_pipeline():
    """Verify that a schema validation gate failure immediately halts the DAG pipeline.

    @req:PRD-DDF-001
    """
    store = DigitizationJobStore()
    runner = DigitizationDAGRunner(job_store=store)

    job = await runner.initialize_job(
        file_content=b"%PDF-1.4\nCorrupted text\n%%EOF",
        filename="corrupted.pdf",
        study_id="study_fail_001",
    )

    # Mock Stage 1 to produce invalid layout schema (missing protocol_title)
    with patch.object(
        runner,
        "_run_layout_parsing",
        return_value=(
            {
                "protocol_id": "CDNC-001",
                # missing required "protocol_title", "phase", "therapeutic_area"
            },
            0.1,
        ),
    ):
        failed_job = await runner.run_job(job.job_id)

    assert failed_job.status == DigitizationJobStatus.FAILED
    assert failed_job.error_message is not None
    assert (
        "protocol_title" in failed_job.error_message
        or "Field required" in failed_job.error_message
    )

    # Checkpoint for stage 1 exists and is marked FAILED
    ckpt1 = failed_job.checkpoints[DigitizationStage.LAYOUT_PARSING.value]
    assert ckpt1.gate_status == StageGateStatus.FAILED
    assert len(ckpt1.gate_errors) > 0

    # Stage 2 (SOA_EXTRACTION) must NOT have executed
    assert DigitizationStage.SOA_EXTRACTION.value not in failed_job.checkpoints


@pytest.mark.asyncio
async def test_dag_job_resumption_from_stage():
    """Verify resuming a DAG pipeline from a specific intermediate stage.

    @req:PRD-DDF-001
    """
    store = DigitizationJobStore()
    runner = DigitizationDAGRunner(job_store=store)

    doc_text = (
        "Protocol Title: Resumption Test Trial\n"
        "Protocol ID: CDNC-RES-001\n"
        "Phase: Phase I\n"
        "Therapeutic Area: Immunology\n"
    )
    job = await runner.initialize_job(
        file_content=b"%PDF-1.4\n" + doc_text.encode() + b"\n%%EOF",
        filename="resumption_test.pdf",
        study_id="study_res_001",
    )

    # First run only stages 1 & 2
    ckpt1 = await runner._execute_stage(DigitizationStage.LAYOUT_PARSING, job)
    await store.save_checkpoint(job.job_id, ckpt1)
    ckpt2 = await runner._execute_stage(DigitizationStage.SOA_EXTRACTION, job)
    await store.save_checkpoint(job.job_id, ckpt2)

    # Resume from Stage 3 (BIOMEDICAL_CONCEPT_MAPPING)
    resumed_job = await runner.run_job(
        job.job_id,
        start_from_stage=DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING,
    )

    assert resumed_job.status == DigitizationJobStatus.COMPLETED
    assert DigitizationStage.BIOMEDICAL_CONCEPT_MAPPING.value in resumed_job.checkpoints
    assert DigitizationStage.ECRF_SYNTHESIS.value in resumed_job.checkpoints
    assert DigitizationStage.USDM_COMPILATION.value in resumed_job.checkpoints


# =============================================================================
# REST API ENDPOINT INTEGRATION TESTS
# =============================================================================


def test_api_start_dag_job_endpoint(client: TestClient):
    """Validate starting an asynchronous DAG digitization job via POST /dag/jobs.

    @req:PRD-DDF-001, @req:PRD-SYS-001
    """
    pdf_content = (
        b"%PDF-1.4\nProtocol Title: Async DAG Ingestion Test\n"
        b"Protocol ID: CDNC-ASYNC-001\nPhase: Phase II\n%%EOF"
    )
    files = {"file": ("async_protocol.pdf", pdf_content, "application/pdf")}
    data = {"study_id": "study_async_001"}

    response = client.post(
        "/api/v1/designer/digitization/dag/jobs",
        files=files,
        data=data,
        headers=get_test_auth_headers(),
    )

    assert response.status_code == 202
    resp_data = response.json()
    assert "job_id" in resp_data
    assert resp_data["status"] == "PENDING"
    assert "scheduled successfully" in resp_data["message"]


def test_api_get_dag_job_status_endpoint(client: TestClient):
    """Validate querying real-time DAG status, progress %, and checkpoints via GET /dag/jobs/{job_id}.

    @req:PRD-DDF-001
    """
    # Create and run a job directly in store
    _store = get_digitization_job_store()
    runner = get_digitization_dag_runner()

    pdf_content = (
        b"%PDF-1.4\nProtocol Title: Polling Test Study\n"
        b"Protocol ID: CDNC-POLL-001\nPhase: Phase I\n%%EOF"
    )

    import asyncio

    job = asyncio.run(runner.initialize_job(pdf_content, "poll.pdf", "study_poll_001"))
    asyncio.run(runner.run_job(job.job_id))

    response = client.get(
        f"/api/v1/designer/digitization/dag/jobs/{job.job_id}",
        headers=get_test_auth_headers(),
    )

    assert response.status_code == 200
    data = response.json()
    assert data["job_id"] == job.job_id
    assert data["status"] == "COMPLETED"
    assert data["progress_pct"] == 100
    assert data["is_terminal"] is True
    assert len(data["checkpoints"]) == 5
    assert data["final_usdm_payload"] is not None


def test_api_resume_dag_job_endpoint(client: TestClient):
    """Validate resuming a DAG job from a specific checkpoint via POST /dag/jobs/{job_id}/resume.

    @req:PRD-DDF-001
    """
    runner = get_digitization_dag_runner()
    pdf_content = b"%PDF-1.4\nProtocol Title: API Resume Test\nPhase: Phase II\n%%EOF"

    import asyncio

    job = asyncio.run(
        runner.initialize_job(pdf_content, "resume.pdf", "study_api_res_001")
    )

    resume_payload = {
        "from_stage": "BIOMEDICAL_CONCEPT_MAPPING",
        "change_reason": "Operator resumed stage after verification",
    }
    response = client.post(
        f"/api/v1/designer/digitization/dag/jobs/{job.job_id}/resume",
        json=resume_payload,
        headers=get_test_auth_headers(),
    )

    assert response.status_code == 200
    data = response.json()
    assert data["job_id"] == job.job_id
    assert data["status"] == "RUNNING"


def test_api_compile_usdm_from_dag_job_endpoint(client: TestClient):
    """Validate committing USDM model compiled by completed DAG job into Neo4j graph.

    @req:PRD-DDF-001, @req:PRD-SYS-001, @req:PRD-MDR-007
    """
    runner = get_digitization_dag_runner()
    pdf_content = (
        b"%PDF-1.4\nProtocol Title: USDM Compile Test Protocol\n"
        b"Protocol ID: CDNC-USDM-001\nPhase: Phase II\n%%EOF"
    )

    import asyncio

    job = asyncio.run(
        runner.initialize_job(pdf_content, "usdm_test.pdf", "study_usdm_commit_001")
    )
    asyncio.run(runner.run_job(job.job_id))

    compile_payload = {
        "study_id": "study_usdm_commit_001",
        "change_reason": "Validated USDM Graph Ingestion from Protocol DAG",
    }

    response = client.post(
        f"/api/v1/designer/digitization/dag/jobs/{job.job_id}/compile-usdm",
        json=compile_payload,
        headers=get_test_auth_headers(
            change_reason="Validated USDM Graph Ingestion from Protocol DAG"
        ),
    )

    assert response.status_code == 201
    resp_data = response.json()
    assert resp_data["status"] == "COMMITTED"
    assert resp_data["study_id"] == "study_usdm_commit_001"
    assert resp_data["nodes_created"] > 0
    assert len(resp_data["synthesized_forms"]) > 0


def test_api_compile_usdm_requires_change_reason(client: TestClient):
    """Verify 21 CFR Part 11 rejection when change justification is empty.

    @req:PRD-SYS-001
    """
    runner = get_digitization_dag_runner()
    pdf_content = b"%PDF-1.4\nProtocol Title: Rejection Test\n%%EOF"

    import asyncio

    job = asyncio.run(runner.initialize_job(pdf_content, "reject.pdf", "study_rej_001"))
    asyncio.run(runner.run_job(job.job_id))

    compile_payload = {
        "study_id": "study_rej_001",
        "change_reason": "",  # Empty reason
    }

    response = client.post(
        f"/api/v1/designer/digitization/dag/jobs/{job.job_id}/compile-usdm",
        json=compile_payload,
        headers=get_test_auth_headers(change_reason="Valid Gateway Reason"),
    )

    assert response.status_code == 400
    assert "Missing change justification reason" in response.json()["detail"]


def test_api_compile_usdm_incomplete_job_rejected(client: TestClient):
    """Verify rejection when attempting to compile USDM from an incomplete DAG job.

    @req:PRD-DDF-001
    """
    runner = get_digitization_dag_runner()
    pdf_content = b"%PDF-1.4\nProtocol Title: Incomplete Study\n%%EOF"

    import asyncio

    # Initialize but do NOT run
    job = asyncio.run(
        runner.initialize_job(pdf_content, "incomp.pdf", "study_incomp_001")
    )

    compile_payload = {
        "study_id": "study_incomp_001",
        "change_reason": "Attempting commit on uncompleted job",
    }

    response = client.post(
        f"/api/v1/designer/digitization/dag/jobs/{job.job_id}/compile-usdm",
        json=compile_payload,
        headers=get_test_auth_headers(),
    )

    assert response.status_code == 400
    assert "has not completed successfully" in response.json()["detail"]
