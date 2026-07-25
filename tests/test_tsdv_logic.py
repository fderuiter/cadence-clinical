import hashlib
import hmac
import json
import time

import httpx
import pytest
from sqlalchemy import text

from apps.execution.database.core import db_manager
from apps.execution.database.migrate import deploy_database_triggers
from apps.execution.database.models import (
    Base,
    ClinicalSubject,
)
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


class DummyConfig:
    """Mock config object for pure function testing."""

    def __init__(
        self,
        id="MOCK-CFG",
        sampling_model="SUBJECT_BASED",
        initial_full_sdv_subject_count=0,
        random_sample_percentage=0.0,
        full_sdv_domains=None,
        safety_endpoints=None,
        zero_sdv_domains=None,
        trial_random_seed=42,
    ):
        self.id = id
        self.sampling_model = sampling_model
        self.initial_full_sdv_subject_count = initial_full_sdv_subject_count
        self.random_sample_percentage = random_sample_percentage
        self.full_sdv_domains = full_sdv_domains or []
        self.safety_endpoints = safety_endpoints or []
        self.zero_sdv_domains = zero_sdv_domains or []
        self.trial_random_seed = trial_random_seed


@pytest.fixture(autouse=True)
async def setup_test_db():
    TrialLockManager.reset()
    db_manager.init_db(
        "sqlite+aiosqlite:///:memory:",
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    TrialLockManager.reset()


# =============================================================================
# PURE FUNCTIONS UNIT TESTS (PRD-QRY-007)
# =============================================================================


def test_is_subject_selected_for_sdv():
    # @req:PRD-QRY-007
    """Verify that is_subject_selected_for_sdv works deterministically and correctly.

    - First N subjects receive full SDV (returns True).
    - Subsequent selection is deterministic based on seed/subject UUID.
    - Boundary values 0.0 and 100.0 behave correctly.
    """
    # 1. First N subjects are always selected (True)
    config = DummyConfig(initial_full_sdv_subject_count=3, random_sample_percentage=0.0)
    assert is_subject_selected_for_sdv(config, "SUBJ-A", 0) is True
    assert is_subject_selected_for_sdv(config, "SUBJ-B", 1) is True
    assert is_subject_selected_for_sdv(config, "SUBJ-C", 2) is True
    # Subsequent index (3 >= 3) with 0% random sampling must return False
    assert is_subject_selected_for_sdv(config, "SUBJ-D", 3) is False

    # 2. Boundary percentage 0.0 must never select subsequent subjects
    config_zero = DummyConfig(
        initial_full_sdv_subject_count=0, random_sample_percentage=0.0
    )
    for i in range(100):
        assert is_subject_selected_for_sdv(config_zero, f"SUBJ-{i}", i) is False

    # 3. Boundary percentage 100.0 must always select subsequent subjects
    config_hundred = DummyConfig(
        initial_full_sdv_subject_count=0, random_sample_percentage=100.0
    )
    for i in range(100):
        assert is_subject_selected_for_sdv(config_hundred, f"SUBJ-{i}", i) is True

    # 4. Deterministic sampling reproducibility
    config_rand = DummyConfig(
        initial_full_sdv_subject_count=1,
        random_sample_percentage=40.0,
        trial_random_seed=12345,
    )
    # First subject (index 0) is always True
    assert is_subject_selected_for_sdv(config_rand, "SUBJ-X", 0) is True

    # Subsequent runs for the exact same seed and subject UUID must be 100% reproducible
    res1 = is_subject_selected_for_sdv(config_rand, "SUBJ-Y", 1)
    res2 = is_subject_selected_for_sdv(config_rand, "SUBJ-Y", 1)
    assert res1 == res2

    # Different subject should also be reproducible but can differ from SUBJ-Y
    res_z1 = is_subject_selected_for_sdv(config_rand, "SUBJ-Z", 2)
    res_z2 = is_subject_selected_for_sdv(config_rand, "SUBJ-Z", 2)
    assert res_z1 == res_z2

    # Different seeds should change the deterministic outcome pattern
    config_rand_diff_seed = DummyConfig(
        initial_full_sdv_subject_count=0,
        random_sample_percentage=50.0,
        trial_random_seed=99999,
    )
    results_seed1 = [
        is_subject_selected_for_sdv(config_rand, f"SUBJ-{i}", 10) for i in range(50)
    ]
    results_seed2 = [
        is_subject_selected_for_sdv(config_rand_diff_seed, f"SUBJ-{i}", 10)
        for i in range(50)
    ]
    # They should not be identical
    assert results_seed1 != results_seed2


def test_is_field_required():
    # @req:PRD-QRY-007
    """Verify field selection, normalization, and strict precedence rules.

    - Safety/full-SDV always require SDV.
    - Zero-SDV never require SDV.
    - Safety/full-SDV must take precedence to prevent silent bypass.
    """
    config = DummyConfig(
        full_sdv_domains=["VS", "EG"],
        safety_endpoints=["AE", "SAE"],
        zero_sdv_domains=["DM", "AE"],  # Conflict: "AE" is in both safety and zero-SDV
    )

    # 1. Full-SDV domain
    assert is_field_required(config, "VS") is True
    assert is_field_required(config, "EG") is True

    # 2. Safety endpoint domain
    assert is_field_required(config, "SAE") is True

    # 3. Conflict resolution: AE is in both, safety must take precedence!
    assert is_field_required(config, "AE") is True

    # 4. Zero-SDV domain
    assert is_field_required(config, "DM") is False

    # 5. Non-configured domain defaults to False
    assert is_field_required(config, "LB") is False

    # 6. Case insensitivity and whitespace handling
    assert is_field_required(config, "  vs  ") is True
    assert is_field_required(config, "ae") is True
    assert is_field_required(config, "dm") is False


def test_evaluate_tsdv_requirement_pure():
    # @req:PRD-QRY-007
    """Verify evaluation logic across models and error scenarios in pure functions.

    - SUBJECT_BASED model.
    - FIELD_BASED model and domain requirement.
    - COMBINED model rules.
    """
    # 1. SUBJECT_BASED model: only cares about subject selection
    config_subj = DummyConfig(
        sampling_model="SUBJECT_BASED",
        initial_full_sdv_subject_count=1,
        random_sample_percentage=0.0,
        full_sdv_domains=["VS"],
    )
    # Selected subject (index 0)
    res = evaluate_tsdv_requirement(config_subj, "SUBJ-1", 0, domain="DM")
    assert res["required"] is True
    assert "SUBJECT_BASED" in res["explanation"]

    # Not selected subject (index 1) - even if domain is full_sdv_domain, SUBJECT_BASED ignores it
    res = evaluate_tsdv_requirement(config_subj, "SUBJ-2", 1, domain="VS")
    assert res["required"] is False

    # 2. FIELD_BASED model: only cares about field rules, domain required
    config_fld = DummyConfig(
        sampling_model="FIELD_BASED",
        full_sdv_domains=["VS"],
        zero_sdv_domains=["DM"],
    )
    # Missing domain raises ValueError
    with pytest.raises(ValueError, match="Domain parameter is required"):
        evaluate_tsdv_requirement(config_fld, "SUBJ-1", 0)

    # Domain in full_sdv
    res = evaluate_tsdv_requirement(config_fld, "SUBJ-1", 0, domain="VS")
    assert res["required"] is True

    # Domain in zero_sdv
    res = evaluate_tsdv_requirement(config_fld, "SUBJ-1", 0, domain="DM")
    assert res["required"] is False

    # 3. COMBINED model: subject and field rules combined with precedence
    config_comb = DummyConfig(
        sampling_model="COMBINED",
        initial_full_sdv_subject_count=1,
        random_sample_percentage=0.0,
        full_sdv_domains=["VS"],
        zero_sdv_domains=["DM"],
    )
    # Missing domain raises ValueError
    with pytest.raises(ValueError, match="Domain parameter is required"):
        evaluate_tsdv_requirement(config_comb, "SUBJ-1", 0)

    # Case A: Domain is full-SDV -> Always True regardless of subject selection
    res = evaluate_tsdv_requirement(config_comb, "SUBJ-2", 1, domain="VS")
    assert res["required"] is True
    assert "safety/full-SDV domain" in res["explanation"]

    # Case B: Domain is zero-SDV -> Always False regardless of subject selection
    res = evaluate_tsdv_requirement(config_comb, "SUBJ-1", 0, domain="DM")
    assert res["required"] is False
    assert "zero-SDV domain" in res["explanation"]

    # Case C: Domain is normal, subject is selected -> True
    res = evaluate_tsdv_requirement(config_comb, "SUBJ-1", 0, domain="LB")
    assert res["required"] is True
    assert "Subject is selected for full SDV" in res["explanation"]

    # Case D: Domain is normal, subject is not selected -> False
    res = evaluate_tsdv_requirement(config_comb, "SUBJ-2", 1, domain="LB")
    assert res["required"] is False
    assert "Subject is not selected for full SDV" in res["explanation"]


# =============================================================================
# API INTEGRATION TESTS (PRD-QRY-007)
# =============================================================================


@pytest.mark.asyncio
async def test_tsdv_config_api_authorization_and_validation():
    # @req:PRD-QRY-007
    """Verify that configuration API enforces role-based security, GxP change reason, and validation bounds."""
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Missing change reason header -> 403 Forbidden
        headers_no_reason = get_v2_auth_headers(roles="CRA", change_reason="")
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-A",
                "sampling_model": "SUBJECT_BASED",
                "trial_random_seed": 123,
            },
            headers=headers_no_reason,
        )
        assert resp.status_code == 403

        # 2. Unauthorized role (e.g., Investigator) -> 403 Forbidden
        headers_inv = get_v2_auth_headers(roles="Site Investigator")
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-A",
                "sampling_model": "SUBJECT_BASED",
                "trial_random_seed": 123,
            },
            headers=headers_inv,
        )
        assert resp.status_code == 403

        # 3. Invalid payload validations (percentage > 100) -> 422 Unprocessable Entity
        headers_ok = get_v2_auth_headers(roles="Data Manager")
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-A",
                "sampling_model": "SUBJECT_BASED",
                "random_sample_percentage": 105.0,
                "trial_random_seed": 123,
            },
            headers=headers_ok,
        )
        assert resp.status_code == 422

        # 4. Invalid payload validations (negative count) -> 422 Unprocessable Entity
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-A",
                "sampling_model": "SUBJECT_BASED",
                "initial_full_sdv_subject_count": -5,
                "trial_random_seed": 123,
            },
            headers=headers_ok,
        )
        assert resp.status_code == 422

        # 5. Missing study_id -> 422
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "sampling_model": "SUBJECT_BASED",
                "trial_random_seed": 123,
            },
            headers=headers_ok,
        )
        assert resp.status_code == 422

        # 6. Missing trial_random_seed -> 422
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json={
                "study_id": "STUDY-A",
                "sampling_model": "SUBJECT_BASED",
            },
            headers=headers_ok,
        )
        assert resp.status_code == 422


@pytest.mark.asyncio
async def test_tsdv_config_api_upsert_and_retrieval():
    # @req:PRD-QRY-007
    """Verify that configuration API upserts deterministic one-config-per-study and supports retrieve."""
    headers_ok = get_v2_auth_headers(roles="Data Manager")
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a config (Initial POST)
        payload = {
            "study_id": "STUDY-B",
            "sampling_model": "COMBINED",
            "initial_full_sdv_subject_count": 2,
            "random_sample_percentage": 25.0,
            "full_sdv_domains": ["VS"],
            "safety_endpoints": ["AE"],
            "zero_sdv_domains": ["DM"],
            "trial_random_seed": 42,
        }
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json=payload,
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["study_id"] == "STUDY-B"
        assert data["sampling_model"] == "COMBINED"
        assert data["version"] == 1

        # 2. Update the config (Second POST - Upsert)
        payload["random_sample_percentage"] = 35.0
        resp = await client.post(
            "/api/v1/execution/tsdv/config",
            json=payload,
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["random_sample_percentage"] == 35.0
        assert data["version"] == 2

        # 3. Retrieve config GET
        resp_get = await client.get(
            "/api/v1/execution/tsdv/config/STUDY-B",
            headers=headers_ok,
        )
        assert resp_get.status_code == 200
        data_get = resp_get.json()
        assert data_get["study_id"] == "STUDY-B"
        assert data_get["random_sample_percentage"] == 35.0
        assert data_get["version"] == 2

        # 4. Unknown study retrieves 404
        resp_404 = await client.get(
            "/api/v1/execution/tsdv/config/unknown-study",
            headers=headers_ok,
        )
        assert resp_404.status_code == 404


@pytest.mark.asyncio
async def test_tsdv_evaluation_endpoint():
    # @req:PRD-QRY-007
    """Verify the TSDV requirement evaluation endpoint evaluates correctly.

    - Resolves configuration and subject context.
    - Handles missing config/subject scenarios with 404s.
    - Resolves subject enrollment index alphabetically when not passed.
    - Resolves COMBINED model evaluation path correctly.
    """
    headers_ok = get_v2_auth_headers(roles="CRA")

    # Seed subject data
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', 1);")
            )
            # Create three subjects for STUDY-C
            # Alphabetically sorted: SUBJ-101, SUBJ-102, SUBJ-103
            s1 = ClinicalSubject(
                subject_id="SUBJ-103", study_id="STUDY-C", site_id="SITE-1"
            )
            s2 = ClinicalSubject(
                subject_id="SUBJ-101", study_id="STUDY-C", site_id="SITE-1"
            )
            s3 = ClinicalSubject(
                subject_id="SUBJ-102", study_id="STUDY-C", site_id="SITE-1"
            )
            session.add_all([s1, s2, s3])

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Call before config exists -> 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-101&domain=VS",
            headers=headers_ok,
        )
        assert resp.status_code == 404
        assert "configuration not found" in resp.json()["detail"]

        # Create config
        cfg_payload = {
            "study_id": "STUDY-C",
            "sampling_model": "COMBINED",
            "initial_full_sdv_subject_count": 1,  # First 1 subject in alphabetical order gets full SDV
            "random_sample_percentage": 0.0,
            "full_sdv_domains": ["VS"],
            "safety_endpoints": ["AE"],
            "zero_sdv_domains": ["DM"],
            "trial_random_seed": 42,
        }
        await client.post(
            "/api/v1/execution/tsdv/config",
            json=cfg_payload,
            headers=headers_ok,
        )

        # 2. Call with invalid subject ID -> 404
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=INVALID-SUBJ&domain=VS",
            headers=headers_ok,
        )
        assert resp.status_code == 404

        # 3. Alphabetical resolving of enrollment index:
        # Alphabetical order:
        # Index 0: SUBJ-101 (Selected since initial_full_sdv_subject_count = 1)
        # Index 1: SUBJ-102 (Not selected)
        # Index 2: SUBJ-103 (Not selected)

        # Let's verify SUBJ-101 is index 0 and receives full SDV (for non-zero domains)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-101&domain=LB",
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert data["is_subject_selected"] is True
        assert data["details"]["enrollment_index"] == 0

        # Let's verify SUBJ-102 is index 1 and does NOT receive full SDV (for normal domain LB)
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-102&domain=LB",
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert data["is_subject_selected"] is False
        assert data["details"]["enrollment_index"] == 1

        # Let's verify SUBJ-102 STILL requires SDV for full-SDV domain VS
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-102&domain=VS",
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is True
        assert "safety/full-SDV domain" in data["explanation"]

        # Let's verify SUBJ-101 (selected subject) does NOT require SDV for zero-SDV domain DM
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-101&domain=DM",
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert "zero-SDV domain" in data["explanation"]

        # 4. Explicitly passed enrollment_index overrides alphabetical order
        # For SUBJ-101, if we pass enrollment_index=5 (which is >= initial_full_sdv_subject_count),
        # they will NOT be selected for full SDV.
        resp = await client.get(
            "/api/v1/execution/tsdv/required?study_id=STUDY-C&subject_id=SUBJ-101&domain=LB&enrollment_index=5",
            headers=headers_ok,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["required"] is False
        assert data["is_subject_selected"] is False
        assert data["details"]["enrollment_index"] == 5
