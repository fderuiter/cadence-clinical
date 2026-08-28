"""Integration tests for Protocol Amendment Training Generation and EDC Programmatic Write-Gating.

Requirements: PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004
"""

from collections.abc import AsyncGenerator
from typing import Any

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base as ExecutionBase,
)
from apps.execution.database.models import (
    ClinicalSubject,
    ClinicalVisit,
)
from apps.execution.greenlight_gating import (
    GreenlightVerificationClient,
    set_greenlight_verification_client,
)
from apps.execution.main import app
from apps.execution.site_execution_gatekeeper import (
    SiteExecutionGatekeeper,
    set_site_execution_gatekeeper,
)
from apps.execution.tests.test_lock_router import _make_auth_headers
from apps.execution.training_gating import (
    ITrainingVerificationClient,
    set_training_verification_client,
)

client = TestClient(app)


class FakeTrainingVerificationClient(ITrainingVerificationClient):
    """In-memory mock of training qualification status for execution service tests."""

    def __init__(self) -> None:
        self.qualified_users: set[tuple[str, str, str]] = set()
        self.pending_curricula_map: dict[tuple[str, str], list[dict[str, Any]]] = {}

    def set_curriculum_mandatory(
        self, study_id: str, protocol_version: str, title: str
    ) -> None:
        self.pending_curricula_map[(study_id, protocol_version)] = [
            {"id": "curr_fake_01", "title": title, "is_mandatory": True}
        ]

    def mark_user_qualified(
        self, user_id: str, study_id: str, protocol_version: str
    ) -> None:
        self.qualified_users.add((user_id, study_id, protocol_version))

    async def get_qualification_status(
        self,
        user_id: str,
        study_id: str,
        protocol_version: str,
        site_id: str | None = None,
        user_role: str | None = None,
    ) -> dict[str, Any]:
        pending = self.pending_curricula_map.get((study_id, protocol_version), [])
        if not pending:
            return {"is_qualified": True, "pending_curricula": []}

        is_qual = (user_id, study_id, protocol_version) in self.qualified_users
        return {
            "is_qualified": is_qual,
            "pending_curricula": [] if is_qual else pending,
        }


fake_training_client = FakeTrainingVerificationClient()


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None]:
    """Setup isolated in-memory database for Execution models."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecutionBase.metadata.create_all)

    fake_training_client.qualified_users.clear()
    fake_training_client.pending_curricula_map.clear()
    set_training_verification_client(fake_training_client)
    set_greenlight_verification_client(GreenlightVerificationClient())
    set_site_execution_gatekeeper(
        SiteExecutionGatekeeper(
            training_client=fake_training_client,
            greenlight_client=GreenlightVerificationClient(),
        )
    )

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(ExecutionBase.metadata.drop_all)
    await db_manager.close()
    set_greenlight_verification_client(GreenlightVerificationClient())
    set_site_execution_gatekeeper(SiteExecutionGatekeeper())


@pytest.mark.asyncio
async def test_publish_amendment_triggers_delta_training_generation():
    """Publishing a protocol amendment returns summary of changes and activates delta workflow.

    @req:PRD-TRN-001
    """
    headers = _make_auth_headers(
        user_id="designer_lead_01",
        roles="study_designer",
        change_reason="Publish Protocol Amendment v2.0 with delta training",
    )

    baseline = {
        "study_id": "STUDY-AMEND-01",
        "version": "1.0.0",
        "activities": [{"id": "act_01", "name": "Vital Signs"}],
    }
    amended = {
        "study_id": "STUDY-AMEND-01",
        "version": "2.0.0",
        "activities": [
            {"id": "act_01", "name": "Vital Signs"},
            {
                "id": "act_02",
                "name": "PK Sampling Series",
                "description": "Serial blood draw",
            },
        ],
    }

    resp = client.post(
        "/api/v1/execution/amendments/publish",
        json={
            "study_id": "STUDY-AMEND-01",
            "version_number": "2.0.0",
            "description": "Amended to add PK sampling series",
            "baseline_snapshot": baseline,
            "amended_snapshot": amended,
        },
        headers=headers,
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["version_number"] == "2.0.0"
    assert data["added_activities_count"] == 1


@pytest.mark.asyncio
async def test_edc_write_gating_blocks_unqualified_users():
    """EDC write and signature endpoints block with HTTP 403 TrainingRequiredError when mandatory training is incomplete.

    @req:PRD-TRN-002
    """
    study_id = "STUDY-GATE-01"
    proto_ver = "2.0.0"
    user_id = "site_investigator_gated"

    # 1. Setup subject in execution DB
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="sub_test_001",
            subject_id="SUB-1001",
            study_id=study_id,
            site_id="SITE-01",
            status="SCREENING",
            active_protocol_version=proto_ver,
        )
        visit = ClinicalVisit(
            id="vis_test_001",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="SUB-1001",
            visit_id="VISIT-01",
            visit_name="Baseline Visit",
            status="SCHEDULED",
        )
        session.add(subj)
        session.add(visit)
        await session.commit()

    # 2. Register mandatory curriculum in training client
    fake_training_client.set_curriculum_mandatory(
        study_id=study_id,
        protocol_version=proto_ver,
        title="Cardiac Echo Protocol Delta",
    )

    # 3. Attempt Form Submission while training is incomplete -> HTTP 403 FORBIDDEN
    headers = _make_auth_headers(
        user_id=user_id,
        roles="site_investigator",
        change_reason="Attempt eCRF entry without training",
    )
    resp_form = client.post(
        "/api/v1/execution/form-submissions",
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "subject_id": "SUB-1001",
            "visit_id": "VISIT-01",
            "form_id": "FORM-VS",
            "protocol_version": proto_ver,
            "payload": {"temperature": 37.0},
        },
        headers=headers,
    )
    assert resp_form.status_code == 403
    err_body = resp_form.json()
    assert err_body["detail"]["error"] in (
        "TRAINING_REQUIRED",
        "SITE_NOT_ELIGIBLE_FOR_MUTATION",
    )
    if err_body["detail"]["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION":
        assert "TRAINING_REQUIRED" in err_body["detail"]["rejection_reasons"]
    else:
        assert "TrainingRequiredError" in err_body["detail"]["message"]

    # 4. Attempt Observation creation while training is incomplete -> HTTP 403 FORBIDDEN
    resp_obs = client.post(
        "/api/v1/execution/observations",
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "subject_id": "SUB-1001",
            "domain": "VS",
            "test_code": "SYSBP",
            "test_name": "Systolic Blood Pressure",
            "value": 120.0,
            "unit": "mmHg",
        },
        headers=headers,
    )
    assert resp_obs.status_code == 403
    obs_err = resp_obs.json()["detail"]
    assert obs_err["error"] in (
        "TRAINING_REQUIRED",
        "SITE_NOT_ELIGIBLE_FOR_MUTATION",
    )
    if obs_err["error"] == "SITE_NOT_ELIGIBLE_FOR_MUTATION":
        assert "TRAINING_REQUIRED" in obs_err["rejection_reasons"]


@pytest.mark.asyncio
async def test_edc_write_unlocked_immediately_after_training_completion():
    """EDC write permissions are immediately unlocked in real-time once training is completed and signed.

    @req:PRD-TRN-002
    @req:PRD-TRN-003
    @req:PRD-TRN-004
    """
    user_id = "site_investigator_cleared"
    study_id = "STUDY-UNLOCK-01"
    proto_ver = "2.0.0"

    # 1. Setup execution subject
    async with db_manager.get_session_maker()() as session:
        subj = ClinicalSubject(
            id="sub_test_002",
            subject_id="SUB-2002",
            study_id=study_id,
            site_id="SITE-01",
            status="SCREENING",
            active_protocol_version=proto_ver,
        )
        visit = ClinicalVisit(
            id="vis_test_002",
            study_id=study_id,
            site_id="SITE-01",
            subject_id="SUB-2002",
            visit_id="VISIT-01",
            visit_name="Screening",
            status="SCHEDULED",
        )
        session.add(subj)
        session.add(visit)
        await session.commit()

    # 2. Register mandatory curriculum & mark user as completed / qualified
    fake_training_client.set_curriculum_mandatory(
        study_id=study_id,
        protocol_version=proto_ver,
        title="Lab Panel Protocol Delta",
    )
    fake_training_client.mark_user_qualified(
        user_id=user_id,
        study_id=study_id,
        protocol_version=proto_ver,
    )

    # 3. Now attempt Form Submission -> SUCCESS (201 CREATED)
    headers = _make_auth_headers(
        user_id=user_id,
        roles="site_investigator",
        change_reason="Contemporaneous eCRF data entry under approved protocol",
    )
    resp_form = client.post(
        "/api/v1/execution/form-submissions",
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "subject_id": "SUB-2002",
            "visit_id": "VISIT-01",
            "form_id": "FORM-LABS",
            "protocol_version": proto_ver,
            "payload": {"hemoglobin": 14.2},
        },
        headers=headers,
    )
    assert resp_form.status_code == 201
    assert resp_form.json()["status"] == "DRAFT"

    # 4. Now attempt Observation creation -> SUCCESS (200 OK)
    resp_obs = client.post(
        "/api/v1/execution/observations",
        json={
            "study_id": study_id,
            "site_id": "SITE-01",
            "subject_id": "SUB-2002",
            "domain": "LB",
            "test_code": "HGB",
            "test_name": "Hemoglobin",
            "value": 14.2,
            "unit": "g/dL",
        },
        headers=headers,
    )
    assert resp_obs.status_code == 200
    assert resp_obs.json()["value"] == 14.2


@pytest.mark.asyncio
async def test_execution_verify_training_gate_api_endpoint():
    """Verify programmatic training gating check endpoint /api/v1/execution/amendments/verify-training-gate.

    @req:PRD-TRN-002
    """
    study_id = "STUDY-GATE-CHECK"
    proto_ver = "2.0.0"
    user_id = "user_evaluation_test"

    fake_training_client.set_curriculum_mandatory(
        study_id=study_id,
        protocol_version=proto_ver,
        title="ECG Procedure Delta",
    )

    headers = _make_auth_headers(
        user_id=user_id,
        roles="crc",
        change_reason="Verify qualification status",
    )

    # 2. Check gate when untrained -> HTTP 403 Forbidden
    resp_blocked = client.post(
        "/api/v1/execution/amendments/verify-training-gate",
        json={
            "study_id": study_id,
            "protocol_version": proto_ver,
            "user_id": user_id,
            "site_id": "SITE-01",
        },
        headers=headers,
    )
    assert resp_blocked.status_code == 403
    assert resp_blocked.json()["detail"]["error"] == "TRAINING_REQUIRED"
