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
    await db_manager.close()


# Mock Config class for unit testing pure sampling functions
class MockConfig:
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
# Unit Tests for apps/execution/tsdv.py
# =========================================================================


def test_tsdv_subject_selection_first_n():
    # @req:PRD-QRY-007
    """Verify that the first N subjects always receive full SDV."""
    config = MockConfig(
        initial_full_sdv_subject_count=3,
        random_sample_percentage=0.0,
        trial_random_seed=42,
    )
    # The first 3 subjects (indices 0, 1, 2) must be selected
    assert is_subject_selected_for_sdv(config, "SUBJ-A", 0) is True
    assert is_subject_selected_for_sdv(config, "SUBJ-B", 1) is True
    assert is_subject_selected_for_sdv(config, "SUBJ-C", 2) is True
    # The 4th subject (index 3) is not selected since random percentage is 0.0
    assert is_subject_selected_for_sdv(config, "SUBJ-D", 3) is False


def test_tsdv_subject_selection_boundaries():
    # @req:PRD-QRY-007
    """Verify correct behavior for random percentage bounds (0% and 100%)."""
    cfg_0 = MockConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=0.0,
        trial_random_seed=123,
    )
    cfg_100 = MockConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=100.0,
        trial_random_seed=123,
    )

    # With 0%, subjects should never be selected
    assert is_subject_selected_for_sdv(cfg_0, "SUBJ-X", 0) is False
    assert is_subject_selected_for_sdv(cfg_0, "SUBJ-Y", 10) is False

    # With 100%, subjects should always be selected
    assert is_subject_selected_for_sdv(cfg_100, "SUBJ-X", 0) is True
    assert is_subject_selected_for_sdv(cfg_100, "SUBJ-Y", 10) is True


def test_tsdv_subject_selection_deterministic():
    # @req:PRD-QRY-007
    """Verify deterministic and reproducible behavior across processes for same seed and subject UUID."""
    config = MockConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=45.5,
        trial_random_seed=99,
    )
    sub_1 = "11111111-2222-3333-4444-555555555555"
    sub_2 = "99999999-8888-7777-6666-555555555555"

    res_1a = is_subject_selected_for_sdv(config, sub_1, 0)
    res_1b = is_subject_selected_for_sdv(config, sub_1, 0)
    assert res_1a == res_1b  # Must be perfectly reproducible

    res_2a = is_subject_selected_for_sdv(config, sub_2, 1)
    res_2b = is_subject_selected_for_sdv(config, sub_2, 1)
    assert res_2a == res_2b


def test_tsdv_field_required_precedence():
    # @req:PRD-QRY-007
    """Verify safety/full-SDV and zero-SDV precedence and conflict handling."""
    config = MockConfig(
        full_sdv_domains=["VS", "EG"],
        safety_endpoints=["AE"],
        zero_sdv_domains=[
            "DM",
            "VS",
        ],  # Conflict: "VS" is in both full-SDV and zero-SDV
    )

    # 1. Safety endpoints/full-SDV domains must always be True
    assert is_field_required(config, "AE") is True
    assert is_field_required(config, "EG") is True

    # 2. Safety/full-SDV must override zero-SDV (no silent bypass)
    assert is_field_required(config, "VS") is True

    # 3. Zero-SDV domains with no conflict must be False
    assert is_field_required(config, "DM") is False

    # 4. Non-configured domains return None
    assert is_field_required(config, "LB") is None


def test_tsdv_evaluation_models():
    # @req:PRD-QRY-007
    """Verify COMBINED, SUBJECT_BASED, and FIELD_BASED logic combinations."""
    # 1. SUBJECT_BASED Model:
    # Selected subject gets full-SDV, unselected subject gets no SDV (with domain overrides)
    config_subj = MockConfig(
        sampling_model="SUBJECT_BASED",
        initial_full_sdv_subject_count=1,
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    # Subject index 0 is selected (first-N)
    req, sub_sel, field_dec, exp = evaluate_tsdv_requirement(
        config_subj, "SUB-1", 0, "LB"
    )
    assert req is True
    assert sub_sel is True
    assert field_dec is None

    # Subject index 1 is not selected
    req, sub_sel, field_dec, exp = evaluate_tsdv_requirement(
        config_subj, "SUB-2", 1, "LB"
    )
    assert req is False
    assert sub_sel is False

    # Overrides still apply if domains are matched
    req, _, _, _ = evaluate_tsdv_requirement(config_subj, "SUB-2", 1, "AE")
    assert req is True  # AE is safety/full-SDV
    req, _, _, _ = evaluate_tsdv_requirement(config_subj, "SUB-1", 0, "DM")
    assert req is False  # DM is zero-SDV (overrides subject selection)

    # 2. FIELD_BASED Model:
    # Only domain rules matter
    config_field = MockConfig(
        sampling_model="FIELD_BASED",
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    req, _, _, _ = evaluate_tsdv_requirement(config_field, "SUB-1", 0, "AE")
    assert req is True
    req, _, _, _ = evaluate_tsdv_requirement(config_field, "SUB-1", 0, "DM")
    assert req is False
    req, _, _, _ = evaluate_tsdv_requirement(config_field, "SUB-1", 0, "LB")
    assert req is False  # Unconfigured defaults to False in field-based

    # 3. COMBINED Model:
    # Combines both; safety overrides to True, zero overrides to False, others depend on subject selection
    config_comb = MockConfig(
        sampling_model="COMBINED",
        initial_full_sdv_subject_count=1,
        full_sdv_domains=["AE"],
        zero_sdv_domains=["DM"],
    )
    # Selected subject, unconfigured domain LB -> True
    req, _, _, _ = evaluate_tsdv_requirement(config_comb, "SUB-1", 0, "LB")
    assert req is True
    # Unselected subject, unconfigured domain LB -> False
    req, _, _, _ = evaluate_tsdv_requirement(config_comb, "SUB-2", 1, "LB")
    assert req is False
    # Unselected subject, but AE is safety -> True
    req, _, _, _ = evaluate_tsdv_requirement(config_comb, "SUB-2", 1, "AE")
    assert req is True
    # Selected subject, but DM is zero-SDV -> False
    req, _, _, _ = evaluate_tsdv_requirement(config_comb, "SUB-1", 0, "DM")
    assert req is False


# =========================================================================
# Integration / API Tests
# =========================================================================


@pytest.mark.asyncio
async def test_api_tsdv_config_validation():
    # @req:PRD-QRY-007
    """Verify Pydantic validation for configuration bounds and conditional requirements."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Invalid random_sample_percentage (> 100)
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-X",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": 105.0,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400

        # 2. Invalid initial_full_sdv_subject_count (negative)
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-X",
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": -5,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400

        # 3. Missing trial_random_seed when random_sample_percentage > 0
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-X",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": 25.0,
                "trial_random_seed": None,
            },
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 400


@pytest.mark.asyncio
async def test_api_tsdv_config_authorization_and_upsert():
    # @req:PRD-QRY-007
    """Verify that only Data Managers and CRAs can write configurations with GxP justifications."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Reject writes from Site Investigator
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-AUTH",
                "sampling_model": "SUBJECT_BASED",
            },
            headers=get_v2_auth_headers(roles="Site Investigator"),
        )
        assert resp.status_code == 403

        # 2. Reject writes with missing GxP change justifications
        headers_no_reason = get_v2_auth_headers()
        headers_no_reason.pop("X-Change-Reason")
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-AUTH",
                "sampling_model": "SUBJECT_BASED",
            },
            headers=headers_no_reason,
        )
        assert resp.status_code == 403

        # 3. Allow CRA write
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-AUTH",
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": 5,
            },
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["initial_full_sdv_subject_count"] == 5
        assert data["version"] == 1

        # 4. Upsert check (updates existing configuration)
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-AUTH",
                "sampling_model": "COMBINED",
                "initial_full_sdv_subject_count": 10,
            },
            headers=get_v2_auth_headers(roles="Data Manager"),
        )
        assert resp.status_code == 201
        data = resp.json()
        assert data["initial_full_sdv_subject_count"] == 10
        assert data["sampling_model"] == "COMBINED"
        assert data["version"] == 2  # Increments audit version successfully

        # 5. Retrieve configuration
        resp = await client.get(
            "/api/v1/execution/tsdv/config/STUDY-AUTH",
            headers=get_v2_auth_headers(roles="CRA"),
        )
        assert resp.status_code == 200
        assert resp.json()["initial_full_sdv_subject_count"] == 10


@pytest.mark.asyncio
async def test_api_tsdv_evaluation_endpoint():
    # @req:PRD-QRY-007
    """Verify evaluation API resolves configs and missing subject enrollment index alphabetically."""
    # Seed subjects in database
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Set GxP audit parameters
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            # Add subjects (unordered alphabetically by subject_id)
            sub_c = ClinicalSubject(
                id="SUB-UUID-C", subject_id="SUBJ-003", study_id="STUDY-EVAL"
            )
            sub_a = ClinicalSubject(
                id="SUB-UUID-A", subject_id="SUBJ-001", study_id="STUDY-EVAL"
            )
            sub_b = ClinicalSubject(
                id="SUB-UUID-B", subject_id="SUBJ-002", study_id="STUDY-EVAL"
            )
            session.add_all([sub_c, sub_a, sub_b])

            # Add TSDV Config
            cfg = TSDVConfig(
                id="CFG-EVAL",
                study_id="STUDY-EVAL",
                sampling_model="SUBJECT_BASED",
                initial_full_sdv_subject_count=2,
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
        # 1. Missing study config -> HTTP 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-MISSING&subject_id=SUBJ-001",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404

        # 2. Missing subject -> HTTP 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-EVAL&subject_id=SUBJ-UNKNOWN",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 404

        # 3. Resolve enrollment index alphabetically:
        # Sorted order: SUBJ-001 (idx 0), SUBJ-002 (idx 1), SUBJ-003 (idx 2)
        # initial_full_sdv_subject_count is 2, so index 0 and 1 receive full SDV, index 2 does not.

        # Eval SUBJ-001 (resolved index 0 -> within first 2 -> required = True)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-EVAL&subject_id=SUBJ-001&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert data["enrollment_index"] == 0
        assert "within the first 2" in data["explanation"]

        # Eval SUBJ-003 (resolved index 2 -> beyond first 2 -> required = False for unconfigured domain LB)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-EVAL&subject_id=SUBJ-003&domain=LB",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert data["enrollment_index"] == 2
        assert "not selected" in data["explanation"]

        # 4. Verify domain overrides in evaluation:
        # AE is safety -> always True even on unselected subject SUBJ-003 (idx 2)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-EVAL&subject_id=SUBJ-003&domain=AE",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert "safety/full-SDV" in data["explanation"]

        # DM is zero-SDV -> always False even on selected subject SUBJ-001 (idx 0)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-EVAL&subject_id=SUBJ-001&domain=DM",
            headers=get_v2_auth_headers(),
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert "zero-SDV" in data["explanation"]
