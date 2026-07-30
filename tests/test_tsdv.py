import hashlib
import hmac
import json
import time

import httpx
import pytest
from sqlalchemy import text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import Base, ClinicalSubject, TSDVConfig
from apps.execution.main import app
from apps.execution.trial_lock import TrialLockManager
from apps.execution.tsdv import (
    evaluate_tsdv_requirement,
    is_field_required,
    is_subject_selected_for_sdv,
)

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "test_user",
    roles: str = "CRA",
    change_reason: str = "test operation",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.fixture(autouse=True)
async def setup_test_db():
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()


class MockTSDVConfig:
    """Mock configuration matching apps/execution/tsdv.py attribute expectations."""

    def __init__(
        self,
        initial_full_sdv_subject_count=0,
        random_sample_percentage=0.0,
        full_sdv_domains=None,
        safety_endpoints=None,
        zero_sdv_domains=None,
        trial_random_seed=None,
        sampling_model="SUBJECT_BASED",
    ):
        self.initial_full_sdv_subject_count = initial_full_sdv_subject_count
        self.random_sample_percentage = random_sample_percentage
        self.full_sdv_domains = full_sdv_domains or []
        self.safety_endpoints = safety_endpoints or []
        self.zero_sdv_domains = zero_sdv_domains or []
        self.trial_random_seed = trial_random_seed
        self.sampling_model = sampling_model


# =========================================================================
# Pure Function Tests
# =========================================================================


def test_tsdv_pure_first_n_selection():
    # @req:PRD-QRY-007
    """Verify that all first-N subjects are always selected for full SDV."""
    config = MockTSDVConfig(
        initial_full_sdv_subject_count=5,
        random_sample_percentage=0.0,
        trial_random_seed=123,
    )
    # Check first 5 indices
    for idx in range(5):
        assert is_subject_selected_for_sdv(config, f"SUBJ-{idx}", idx) is True

    # 6th index onwards should be False with 0% random sampling
    assert is_subject_selected_for_sdv(config, "SUBJ-5", 5) is False


def test_tsdv_pure_deterministic_sampling():
    # @req:PRD-QRY-007
    """Verify selection is completely stable/deterministic across repeated evaluations."""
    config = MockTSDVConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=40.0,
        trial_random_seed=999,
    )
    subject_uuid = "e36e52c8-89c5-430c-ab9e-a89278bd1423"
    enrollment_idx = 10

    # Ensure repeatability
    first_run = is_subject_selected_for_sdv(config, subject_uuid, enrollment_idx)
    for _ in range(10):
        assert (
            is_subject_selected_for_sdv(config, subject_uuid, enrollment_idx)
            == first_run
        )


def test_tsdv_pure_different_seeds_produce_different_values():
    # @req:PRD-QRY-007
    """Verify different seeds and subject IDs produce different values while retaining determinism."""
    config_seed1 = MockTSDVConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=50.0,
        trial_random_seed=1111,
    )
    config_seed2 = MockTSDVConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=50.0,
        trial_random_seed=2222,
    )

    # There's a high likelihood different seeds/subjects result in different boolean values
    # Let's search across a few subjects to prove different results can happen (not all True or all False)
    results_seed1 = []
    results_seed2 = []
    for i in range(20):
        sub_id = f"sub-diff-{i}"
        results_seed1.append(is_subject_selected_for_sdv(config_seed1, sub_id, 10))
        results_seed2.append(is_subject_selected_for_sdv(config_seed2, sub_id, 10))

    # Assert they are not completely identical (seeds change hashes)
    assert results_seed1 != results_seed2
    # Ensure both True and False are present across the distribution
    assert True in results_seed1 and False in results_seed1
    assert True in results_seed2 and False in results_seed2


def test_tsdv_pure_percentage_boundaries():
    # @req:PRD-QRY-007
    """Verify correct selection behavior with 0% and 100% random sample percentages."""
    config_0 = MockTSDVConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=0.0,
        trial_random_seed=123,
    )
    config_100 = MockTSDVConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=100.0,
        trial_random_seed=123,
    )

    # 0% should NEVER select subsequent subjects
    for idx in range(10):
        assert is_subject_selected_for_sdv(config_0, f"SUBJ-{idx}", idx) is False

    # 100% should ALWAYS select subsequent subjects
    for idx in range(10):
        assert is_subject_selected_for_sdv(config_100, f"SUBJ-{idx}", idx) is True


def test_tsdv_pure_field_requirement_precedence():
    # @req:PRD-QRY-007
    """Verify full-SDV/safety domains always require SDV, zero-SDV never require, and overrides are respected."""
    # Safety endpoints always override zero-SDV (Conflict management)
    config = MockTSDVConfig(
        full_sdv_domains=["VS", "EG"],
        safety_endpoints=["AE"],
        zero_sdv_domains=[
            "DM",
            "VS",
        ],  # VS is both zero-SDV and full-SDV -> should override to True
    )

    # Safety/full-SDV must return True
    assert is_field_required(config, "AE") is True
    assert is_field_required(config, "EG") is True

    # Conflicting rule: VS is both full-SDV and zero-SDV. VS must return True (high priority overrides)
    assert is_field_required(config, "VS") is True

    # Pure zero-SDV must return False
    assert is_field_required(config, "DM") is False

    # Unconfigured must return None
    assert is_field_required(config, "LB") is None


def test_tsdv_pure_evaluation_sampling_models():
    # @req:PRD-QRY-007
    """Verify combination logic and precedence of COMBINED, SUBJECT_BASED, and FIELD_BASED sampling models."""
    # 1. SUBJECT_BASED model
    cfg_subj = MockTSDVConfig(
        sampling_model="SUBJECT_BASED",
        initial_full_sdv_subject_count=1,
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    # Selected subject (index 0) gets full-SDV on unconfigured LB domain
    req, subj_sel, field_dec, explanation = evaluate_tsdv_requirement(
        cfg_subj, "SUBJ-A", 0, "LB"
    )
    assert req is True
    assert subj_sel is True
    assert field_dec is None
    assert "within the first 1" in explanation

    # Non-selected subject (index 1) gets no SDV on unconfigured LB domain
    req, subj_sel, field_dec, explanation = evaluate_tsdv_requirement(
        cfg_subj, "SUBJ-B", 1, "LB"
    )
    assert req is False
    assert subj_sel is False
    assert "not selected" in explanation

    # Conflict overrides: safety endpoint overrides to True even on non-selected subject
    req, _, _, explanation = evaluate_tsdv_requirement(cfg_subj, "SUBJ-B", 1, "AE")
    assert req is True
    assert "safety/full-SDV domain" in explanation

    # Conflict overrides: zero-SDV overrides to False even on selected subject
    req, _, _, explanation = evaluate_tsdv_requirement(cfg_subj, "SUBJ-A", 0, "DM")
    assert req is False
    assert "zero-SDV domain" in explanation

    # 2. FIELD_BASED model: Only domain rules matter, subject selection is ignored for unconfigured domains
    cfg_field = MockTSDVConfig(
        sampling_model="FIELD_BASED",
        initial_full_sdv_subject_count=5,  # ignored
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    # Selected subject, but unconfigured LB domain -> False
    req, _, _, explanation = evaluate_tsdv_requirement(cfg_field, "SUBJ-A", 0, "LB")
    assert req is False
    assert "Under FIELD_BASED model" in explanation

    # Non-selected subject, safety AE domain -> True
    req, _, _, explanation = evaluate_tsdv_requirement(cfg_field, "SUBJ-B", 10, "AE")
    assert req is True

    # 3. COMBINED model: Combined selection logic
    cfg_comb = MockTSDVConfig(
        sampling_model="COMBINED",
        initial_full_sdv_subject_count=1,
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    # Selected subject, unconfigured LB -> True
    req, _, _, _ = evaluate_tsdv_requirement(cfg_comb, "SUBJ-A", 0, "LB")
    assert req is True

    # Non-selected subject, unconfigured LB -> False
    req, _, _, _ = evaluate_tsdv_requirement(cfg_comb, "SUBJ-B", 1, "LB")
    assert req is False


# =========================================================================
# API Integration Tests
# =========================================================================


@pytest.mark.asyncio
async def test_api_tsdv_configuration_rbac():
    # @req:PRD-QRY-007
    """Verify write operations (creation/updating) are restricted to Allowed Data Manager/CRA roles, and check HTTP 403."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Unauthorized roles (Site Investigator) must get 403
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-RBAC",
                "sampling_model": "SUBJECT_BASED",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403

        # 2. Allowed role (Data Manager) with GxP justification header succeeds
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-RBAC",
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": 5,
            },
            headers=get_v2_auth_headers(roles="Data Manager"),
        )
        assert resp.status_code == 201
        assert resp.json()["study_id"] == "STUDY-RBAC"
        assert resp.json()["initial_full_sdv_subject_count"] == 5

        # 3. CRA role also succeeds (and updates config)
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-RBAC",
                "sampling_model": "COMBINED",
                "initial_full_sdv_subject_count": 10,
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 201
        assert resp.json()["initial_full_sdv_subject_count"] == 10
        assert resp.json()["sampling_model"] == "COMBINED"


@pytest.mark.asyncio
async def test_api_tsdv_config_validation_rules():
    # @req:PRD-QRY-007
    """Verify input validation rejects malformed percentages/counts/lists."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Negative random percentage
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-VAL",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": -10.0,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400

        # 2. Percentage > 100%
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-VAL",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": 110.0,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400

        # 3. Negative subject count
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-VAL",
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": -1,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400

        # 4. Missing trial random seed when percentage > 0.0
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-VAL",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": 10.0,
                "trial_random_seed": None,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400


@pytest.mark.asyncio
async def test_api_tsdv_evaluation_integration_and_context_errors():
    # @req:PRD-QRY-007
    """
    Verify TSDV evaluation API resolves correctly:
    - Missing configuration yields 404.
    - Missing subject context yields 404.
    - Evaluation returns required/not-required decisions matching pure functions and explanation rationale.
    """
    # 1. Seed database with subjects and config
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            sub1 = ClinicalSubject(
                id="SUBJ-UUID-1", subject_id="SUBJ-A1", study_id="STUDY-INTEG"
            )
            sub2 = ClinicalSubject(
                id="SUBJ-UUID-2", subject_id="SUBJ-A2", study_id="STUDY-INTEG"
            )
            session.add_all([sub1, sub2])

            cfg = TSDVConfig(
                id="CFG-INTEG",
                study_id="STUDY-INTEG",
                sampling_model="SUBJECT_BASED",
                initial_full_sdv_subject_count=1,
                random_sample_percentage=0.0,
                full_sdv_domains=["VS"],
                safety_endpoints=["AE"],
                zero_sdv_domains=["DM"],
                trial_random_seed=42,
            )
            session.add(cfg)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 2. Missing configuration yields HTTP 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-MISSING&subject_id=SUBJ-A1",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404
        assert "TSDV configuration not found" in resp.json()["detail"]

        # 3. Missing subject yields HTTP 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-INTEG&subject_id=SUBJ-NONEXIST",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404
        assert "not found in study" in resp.json()["detail"]

        # 4. Correct Evaluation agreeing with pure function:
        # SUBJ-A1 (sorted index 0) -> within first-N count of 1 -> required = True
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-INTEG&subject_id=SUBJ-A1&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert data["subject_selected"] is True
        assert data["enrollment_index"] == 0
        assert "within the first 1" in data["explanation"]

        # SUBJ-A2 (sorted index 1) -> beyond first-N count of 1 -> required = False (domain LB is unconfigured)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-INTEG&subject_id=SUBJ-A2&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert data["subject_selected"] is False
        assert data["enrollment_index"] == 1
        assert "not selected" in data["explanation"]

        # Verification that domain overrides work perfectly on the endpoint:
        # AE is a safety endpoint -> always required = True
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-INTEG&subject_id=SUBJ-A2&domain=AE",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        assert resp.json()["required"] is True
        assert "safety/full-SDV domain" in resp.json()["explanation"]

        # DM is zero-SDV -> always required = False
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-INTEG&subject_id=SUBJ-A1&domain=DM",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        assert resp.json()["required"] is False
        assert "zero-SDV domain" in resp.json()["explanation"]


@pytest.mark.asyncio
async def test_api_tsdv_immutable_enrollment_index_stability():
    # @req:PRD-QRY-007
    """Verify that subject enrollment sequence is stable, immutable, and independent of alphabetical order.

    This ensures that subsequent enrollments and non-lexical/non-sequential subject IDs cannot alter earlier
    first-N decisions. Also checks that the API reports the correct index and rejects conflicting index parameters.

    Requirements: PRD-QRY-007
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        study_id = "STUDY-IMMUTABLE"

        # 1. Create TSDV config with initial_full_sdv_subject_count of 2
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": study_id,
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": 2,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 201

        # 2. Enroll first subject: Z-SUBJ-99 (Lexicographically last, but enrolled first)
        resp = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "Z-SUBJ-99",
                "study_id": study_id,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200

        # Evaluate TSDV for Z-SUBJ-99: enrollment_index must be 0, selected = True
        resp = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=Z-SUBJ-99&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert data["enrollment_index"] == 0
        assert "within the first 2" in data["explanation"]

        # 3. Enroll second subject: A-SUBJ-01 (Lexicographically first, but enrolled second)
        resp = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "A-SUBJ-01",
                "study_id": study_id,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200

        # Evaluate TSDV for A-SUBJ-01: enrollment_index must be 1, selected = True
        resp = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=A-SUBJ-01&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert data["enrollment_index"] == 1
        assert "within the first 2" in data["explanation"]

        # Evaluate TSDV for Z-SUBJ-99 again to prove addition of alphabetically prior subject did not change its index
        resp = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=Z-SUBJ-99&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        assert resp.json()["enrollment_index"] == 0
        assert resp.json()["required"] is True

        # 4. Enroll third subject: M-SUBJ-50
        resp = await client.post(
            "/api/v1/execution/subjects",
            json={
                "subject_id": "M-SUBJ-50",
                "study_id": study_id,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200

        # Evaluate TSDV for M-SUBJ-50: enrollment_index must be 2, selected = False (since count = 2)
        resp = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=M-SUBJ-50&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert data["enrollment_index"] == 2
        assert "not selected" in data["explanation"]

        # Re-verify that earlier decisions/indices for Z-SUBJ-99 (index 0) and A-SUBJ-01 (index 1) are untouched
        resp_z = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=Z-SUBJ-99&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp_z.json()["enrollment_index"] == 0
        assert resp_z.json()["required"] is True

        resp_a = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=A-SUBJ-01&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp_a.json()["enrollment_index"] == 1
        assert resp_a.json()["required"] is True

        # 5. Validate/reject conflicting caller-supplied indexes
        # If the caller supplies index 1 for Z-SUBJ-99, it should raise HTTP 400
        resp_conflict = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=Z-SUBJ-99&domain=LB&enrollment_index=1",
            headers=get_v2_auth_headers(),
        )
        assert resp_conflict.status_code == 400
        assert "Conflicting enrollment_index" in resp_conflict.json()["detail"]

        # If the caller supplies index 0 for Z-SUBJ-99 (which is correct), it should succeed
        resp_correct = await client.get(
            f"/api/v1/execution/tsdv/required?study_id={study_id}&subject_id=Z-SUBJ-99&domain=LB&enrollment_index=0",
            headers=get_v2_auth_headers(),
        )
        assert resp_correct.status_code == 200
        assert resp_correct.json()["enrollment_index"] == 0
