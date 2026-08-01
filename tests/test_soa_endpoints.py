import hashlib
import hmac
import json
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS
from apps.designer.delta import (
    MOCK_SOA_DATA,
    ConcurrentLockingError,
    InvalidSignatureError,
    _init_mock_soa,
)
from apps.designer.main import app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="SoA CRUD test operations",
):
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


def register_valid_mock_study_version(study_id, version_id, status="DRAFT"):
    from packages.security.signing import generate_canonical_signature

    secret = b"designer-amendment-secure-key-12345"
    payload = {
        "id": version_id,
        "version_tag": "1.0",
        "status": status,
        "version_index": 1,
        "created_by": "designer",
    }
    sig = generate_canonical_signature(payload, secret)
    payload["signature"] = sig
    MOCK_STUDY_VERSIONS[study_id] = [payload]
    MOCK_STUDIES[study_id] = {"id": study_id, "arms": []}


@pytest.fixture(autouse=True)
def clean_mock_data():
    """Clears MOCK_SOA_DATA, MOCK_STUDY_VERSIONS, and custom keys in MOCK_STUDIES before each test to ensure test isolation."""
    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()
    # Preserves the core template study "study_1" required by other test suites
    custom_keys = [k for k in MOCK_STUDIES if k != "study_1"]
    for k in custom_keys:
        del MOCK_STUDIES[k]


@pytest.mark.asyncio
async def test_api_soa_crud_lifecycle_endpoints():
    """
    Verifies that the arms, epochs, visits, procedures, and timing windows
    can be created, read, and updated via their corresponding REST endpoints.
    """
    study_id = "study_1"
    version_id = "v_draft"

    # Register mock study version as DRAFT
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Create Arm
        res_create_arm = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={"id": "arm_1", "properties": {"name": "Arm A", "type": "Active"}},
            headers=headers,
        )
        assert res_create_arm.status_code == 201
        assert res_create_arm.json() == {"status": "success", "id": "arm_1"}

        # Read Arm
        res_get_arm = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms/arm_1",
            headers=headers,
        )
        assert res_get_arm.status_code == 200
        data_arm = res_get_arm.json()
        assert data_arm["id"] == "arm_1"
        assert data_arm["name"] == "Arm A"

        # List Arms
        res_list_arms = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            headers=headers,
        )
        assert res_list_arms.status_code == 200
        assert len(res_list_arms.json()) == 1
        assert res_list_arms.json()[0]["id"] == "arm_1"

        # Update Arm
        res_update_arm = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms/arm_1",
            json={"properties": {"name": "Arm A (Modified)", "type": "Active"}},
            headers=headers,
        )
        assert res_update_arm.status_code == 200
        assert res_update_arm.json() == {"status": "success", "id": "arm_1"}

        # Verify updated Arm
        res_get_arm_v2 = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms/arm_1",
            headers=headers,
        )
        assert res_get_arm_v2.status_code == 200
        assert res_get_arm_v2.json()["name"] == "Arm A (Modified)"

        # 2. Create Epoch
        res_create_epoch = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/epochs",
            json={"id": "epoch_tx", "properties": {"name": "Treatment", "sequence": 1}},
            headers=headers,
        )
        assert res_create_epoch.status_code == 201

        # Read Epoch
        res_get_epoch = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/epochs/epoch_tx",
            headers=headers,
        )
        assert res_get_epoch.status_code == 200
        assert res_get_epoch.json()["id"] == "epoch_tx"

        # Update Epoch
        res_update_epoch = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/epochs/epoch_tx",
            json={"properties": {"name": "Treatment Phase", "sequence": 1}},
            headers=headers,
        )
        assert res_update_epoch.status_code == 200

        # 3. Create Visit
        res_create_visit = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits",
            json={"id": "visit_v1", "properties": {"name": "Week 1", "sequence": 1}},
            headers=headers,
        )
        assert res_create_visit.status_code == 201

        # Read Visit
        res_get_visit = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits/visit_v1",
            headers=headers,
        )
        assert res_get_visit.status_code == 200

        # Update Visit
        res_update_visit = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits/visit_v1",
            json={"properties": {"name": "Week 1 Visit", "sequence": 1}},
            headers=headers,
        )
        assert res_update_visit.status_code == 200

        # 4. Create Procedure
        res_create_proc = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/procedures",
            json={"id": "proc_vitals", "properties": {"name": "Vitals"}},
            headers=headers,
        )
        assert res_create_proc.status_code == 201

        # Read Procedure
        res_get_proc = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/procedures/proc_vitals",
            headers=headers,
        )
        assert res_get_proc.status_code == 200

        # Update Procedure
        res_update_proc = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/procedures/proc_vitals",
            json={"properties": {"name": "Vital Signs Checks"}},
            headers=headers,
        )
        assert res_update_proc.status_code == 200

        # 5. Create Timing Window
        res_create_timing = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={
                "id": "timing_w1",
                "properties": {"name": "Standard collection window"},
            },
            headers=headers,
        )
        assert res_create_timing.status_code == 201

        # Read Timing Window
        res_get_timing = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/timing_w1",
            headers=headers,
        )
        assert res_get_timing.status_code == 200

        # Update Timing Window
        res_update_timing = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/timing_w1",
            json={"properties": {"name": "Standard collection window +/- 2 days"}},
            headers=headers,
        )
        assert res_update_timing.status_code == 200


@pytest.mark.asyncio
async def test_api_soa_linking_and_matrix_projection():
    """
    Verifies that linking endpoints correctly associate the structural elements,
    and the soa-projection matrix maps arms, epochs, visits, procedures correctly.
    """
    study_id = "study_1"
    version_id = "v_draft"

    # Register mock study version as DRAFT
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # Seed structural components
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={"id": "arm_1", "properties": {"name": "Arm A"}},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/epochs",
            json={"id": "epoch_tx", "properties": {"name": "Treatment", "sequence": 1}},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits",
            json={"id": "visit_v1", "properties": {"name": "Week 1", "sequence": 1}},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/procedures",
            json={"id": "proc_vitals", "properties": {"name": "Vitals"}},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={
                "id": "timing_w1",
                "properties": {"name": "timing_w1"},
            },  # match details = 'timing_w1'
            headers=headers,
        )

        # 1. Link Epoch to Visit
        res_link_ep = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit",
            json={"epoch_id": "epoch_tx", "visit_id": "visit_v1"},
            headers=headers,
        )
        assert res_link_ep.status_code == 200

        # 2. Link Visit to Procedure
        res_link_vp = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure",
            json={"visit_id": "visit_v1", "procedure_id": "proc_vitals"},
            headers=headers,
        )
        assert res_link_vp.status_code == 200

        # 3. Link Procedure to Timing Window
        res_link_timing = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/timing",
            json={
                "source_id": "proc_vitals",
                "timing_id": "timing_w1",
                "source_type": "procedure",
            },
            headers=headers,
        )
        assert res_link_timing.status_code == 200

        # 4. Link Arm Applicability
        res_link_arm = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability",
            json={"arm_id": "arm_1", "target_id": "visit_v1", "target_type": "visit"},
            headers=headers,
        )
        assert res_link_arm.status_code == 200

        # 5. Query SoA Matrix Projection
        res_projection = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
            headers=headers,
        )
        assert res_projection.status_code == 200
        matrix = res_projection.json()

        # Validate structured Schedule of Activities (SoA) presentation matrix format
        assert "epochs" in matrix
        assert len(matrix["epochs"]) == 1
        assert matrix["epochs"][0]["epoch_id"] == "epoch_tx"
        assert matrix["epochs"][0]["epoch_name"] == "Treatment"

        assert "encounters" in matrix
        assert len(matrix["encounters"]) == 1
        assert matrix["encounters"][0]["encounter_id"] == "visit_v1"

        assert "rows" in matrix
        assert len(matrix["rows"]) == 1
        row = matrix["rows"][0]
        assert row["activity_id"] == "proc_vitals"
        assert row["activity_name"] == "Vitals"
        assert len(row["cells"]) == 1
        assert row["cells"][0]["encounter_id"] == "visit_v1"
        assert row["cells"][0]["is_applicable"] is True
        assert row["cells"][0]["details"] == "timing_w1"


@pytest.mark.asyncio
async def test_api_soa_immutability_guards():
    """
    Verifies that mutating an locked/published version returns a 403 Forbidden
    IMMUTABILITY_VIOLATION.
    """
    study_id = "study_1"
    version_id = "v_locked"

    # Register mock study version as LOCKED
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "LOCKED",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # Attempt to create arm under LOCKED version -> should fail with 403
        res_create_arm = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={"id": "arm_1", "properties": {"name": "Arm A", "type": "Active"}},
            headers=headers,
        )
        assert res_create_arm.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_create_arm.json()["detail"]


@pytest.mark.asyncio
async def test_api_unauthorized_requests():
    """
    Verifies that request gets rejected if gateway headers or change justification are missing.
    """
    study_id = "study_1"
    version_id = "v_draft"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Request with missing headers -> should return 403 (for POST/PUT)
        res = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={"id": "arm_1", "properties": {"name": "Arm A", "type": "Active"}},
        )
        assert res.status_code == 403


# --- New API Endpoint / Integration Tests ---


@pytest.mark.asyncio
async def test_api_with_mocked_neo4j_driver():
    """
    Sets app.state.driver to a mock, invokes an endpoint, and verifies that
    the query is processed via the driver mock.
    """
    # 1. Setup mock driver and transaction
    driver_mock = MagicMock()
    session_mock = AsyncMock()
    session_ctx = AsyncMock()
    session_ctx.__aenter__.return_value = session_mock
    driver_mock.session.return_value = session_ctx

    tx_mock = AsyncMock()
    tx_mock.__aenter__.return_value = tx_mock
    session_mock.begin_transaction.return_value = tx_mock

    # Setup database query mock results for create_study_arm
    lock_res = AsyncMock()
    duplicate_res = AsyncMock()
    duplicate_res.single.return_value = None

    create_record_mock = MagicMock()
    create_record_mock.__getitem__.return_value = "arm_mocked"
    create_res = AsyncMock()
    create_res.single.return_value = create_record_mock

    tx_mock.run.side_effect = [lock_res, duplicate_res, create_res]

    # Save original driver and inject mock driver
    original_driver = getattr(app.state, "driver", None)
    app.state.driver = driver_mock

    try:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_auth_headers()
            res = await client.post(
                "/api/v1/studies/study_1/versions/v_draft/arms",
                json={
                    "id": "arm_mocked",
                    "properties": {"name": "Arm Mocked", "type": "Active"},
                },
                headers=headers,
            )
            assert res.status_code == 201
            assert res.json() == {"status": "success", "id": "arm_mocked"}
            assert tx_mock.run.call_count == 3
    finally:
        app.state.driver = original_driver


@pytest.mark.asyncio
async def test_api_concurrent_locking_conflict_exception_translation():
    """
    Verifies that a ConcurrentLockingError raised inside a delta function
    is translated to HTTP 409 CONCURRENT_LOCKING_CONFLICT.
    """
    study_id = "study_1"
    version_id = "v_draft"

    # Register mock study version as DRAFT
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    with patch(
        "apps.designer.main.create_study_arm",
        side_effect=ConcurrentLockingError("Locked by other session"),
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_auth_headers()
            res = await client.post(
                f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
                json={"id": "arm_1", "properties": {"name": "Arm A", "type": "Active"}},
                headers=headers,
            )
            assert res.status_code == 409
            assert res.json()["detail"] == "CONCURRENT_LOCKING_CONFLICT"


@pytest.mark.asyncio
async def test_api_invalid_signature_exception_translation():
    """
    Verifies that an InvalidSignatureError is translated to HTTP 400 INVALID_OR_MISSING_SIGNATURE.
    """
    study_id = "study_1"
    version_id = "v_draft"

    with patch(
        "apps.designer.main.create_study_arm",
        side_effect=InvalidSignatureError("Missing signature"),
    ):
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_auth_headers()
            res = await client.post(
                f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
                json={"id": "arm_1", "properties": {"name": "Arm A", "type": "Active"}},
                headers=headers,
            )
            assert res.status_code == 400
            assert res.json()["detail"] == "INVALID_OR_MISSING_SIGNATURE"


@pytest.mark.asyncio
async def test_api_audit_reason_enforcement():
    """
    Verifies that the API Gateway middleware rejects requests missing X-Change-Reason,
    or requests where X-Change-Reason exceeds 255 characters.
    """
    study_id = "study_1"
    version_id = "v_draft"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Missing X-Change-Reason header (should return 403)
        headers = get_auth_headers()
        del headers["X-Change-Reason"]

        res = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={
                "id": "arm_no_reason",
                "properties": {"name": "Arm A", "type": "Active"},
            },
            headers=headers,
        )
        assert res.status_code == 403
        assert "Missing change justification reason" in res.json()["detail"]

        # 2. Excessively long X-Change-Reason header (> 255 characters) (should return 400)
        long_reason = "A" * 256
        headers_long = get_auth_headers(change_reason=long_reason)
        res_long = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={
                "id": "arm_long_reason",
                "properties": {"name": "Arm A", "type": "Active"},
            },
            headers=headers_long,
        )
        assert res_long.status_code == 400
        assert "Change reason exceeds 255 characters" in res_long.json()["detail"]


@pytest.mark.asyncio
async def test_api_validation_failures():
    """
    Verifies that invalid payloads are rejected with 422 Unprocessable Entity.
    """
    study_id = "study_1"
    version_id = "v_draft"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # Missing properties
        res = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms",
            json={"id": "arm_invalid"},
            headers=headers,
        )
        assert res.status_code == 400

        # Invalid linking payloads (missing target_type or fields)
        res_link = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit",
            json={"epoch_id": ""},  # missing visit_id
            headers=headers,
        )
        assert res_link.status_code == 400


@pytest.mark.asyncio
async def test_api_rule_soft_delete():
    """
    Verifies that deleting a rule soft-deletes it correctly.
    """
    study_id = "study_rule_delete"
    version_id = "v_rule_delete"

    # Register valid version with signature
    register_valid_mock_study_version(study_id, version_id)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Create a Rule
        res_create = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json={
                "type": "skip_logic",
                "condition": {"type": "constant", "value": True},
                "action": "show",
                "target_field": "field_a",
            },
            headers=headers,
        )
        assert res_create.status_code == 201
        rule_data = res_create.json()
        rule_id = rule_data["id"]

        # 2. Get the rule to ensure it's there
        res_get = await client.get(
            f"/api/v1/studies/{study_id}/rules/{rule_id}",
            headers=headers,
        )
        assert res_get.status_code == 200

        # 3. Soft-delete the rule
        res_delete = await client.delete(
            f"/api/v1/studies/{study_id}/rules/{rule_id}",
            headers=headers,
        )
        assert res_delete.status_code == 200
        assert res_delete.json() == {
            "status": "success",
            "message": "Rule successfully deleted",
        }

        # 4. Try getting it again -> should return 404 (or be excluded from list)
        res_get_deleted = await client.get(
            f"/api/v1/studies/{study_id}/rules/{rule_id}",
            headers=headers,
        )
        assert res_get_deleted.status_code == 404


@pytest.mark.asyncio
async def test_api_soa_immutability_guards_updates():
    """
    Verifies that updating (PUT) an entity under a LOCKED version returns 403 Forbidden IMMUTABILITY_VIOLATION.
    """
    study_id = "study_1"
    version_id = "v_locked"

    # Register locked study version
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "LOCKED",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    # Pre-seed arm_1 under locked version so update can be called
    _init_mock_soa(version_id)
    MOCK_SOA_DATA[version_id]["arms"]["arm_1"] = {
        "id": "arm_1",
        "version_index": 1,
        "created_by": "designer",
        "created_at": "2026-08-01T00:00:00",
        "name": "Arm A",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # Attempt to update arm under LOCKED version -> should fail with 403
        res_update_arm = await client.put(
            f"/api/v1/studies/{study_id}/versions/{version_id}/arms/arm_1",
            json={"properties": {"name": "Arm A (Modified)", "type": "Active"}},
            headers=headers,
        )
        assert res_update_arm.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_update_arm.json()["detail"]


@pytest.mark.asyncio
async def test_api_soa_typed_validation_and_timing_rejection():
    """
    Verifies that invalid input data types and cross-field conditional timing constraints
    are deterministically rejected with HTTP 400 Bad Request.
    """
    study_id = "study_1"
    version_id = "v_draft"

    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Invalid Timing Window: conditional is True, but reason is missing or empty
        res_invalid_timing = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={
                "id": "tw_cond_invalid",
                "properties": {
                    "name": "Fasting Timing",
                    "conditional": True,
                    "reason": "",  # empty reason is invalid
                },
            },
            headers=headers,
        )
        assert res_invalid_timing.status_code == 400
        assert (
            "String should have at least 1 character" in res_invalid_timing.text
            or "A non-empty 'reason' must be provided" in res_invalid_timing.text
        )

        # 1b. Invalid Timing Window: conditional is True, reason is omitted entirely
        res_missing_reason = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={
                "id": "tw_cond_missing",
                "properties": {"name": "Fasting Timing", "conditional": True},
            },
            headers=headers,
        )
        assert res_missing_reason.status_code == 400
        assert "A non-empty 'reason' must be provided" in res_missing_reason.text

        # 2. Valid Timing Window: conditional is True, reason is provided
        res_valid_timing = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={
                "id": "tw_cond_valid",
                "properties": {
                    "name": "Fasting Timing",
                    "conditional": True,
                    "reason": "Only required for diabetic cohort",
                },
            },
            headers=headers,
        )
        assert res_valid_timing.status_code == 201


@pytest.mark.asyncio
async def test_api_soa_retirement_and_projection_exclusion():
    """
    Verifies that soft-retiring of entities (arms, epochs, visits, procedures, timing windows)
    and links is non-destructive, increments the version_index, and excludes them
    from future normal matrix projections.
    """
    study_id = "study_1"
    version_id = "v_draft"

    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": version_id,
            "version_tag": "1.0",
            "status": "DRAFT",
            "version_index": 1,
            "created_by": "designer",
        }
    ]

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Create a Visit and Procedure, and a Timing Window
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits",
            json={
                "id": "visit_ret",
                "properties": {"encounter_name": "Week 1", "sequence": 1},
            },
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/procedures",
            json={"id": "proc_ret", "properties": {"activity_name": "Vitals"}},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
            json={"id": "timing_ret", "properties": {"name": "timing_ret"}},
            headers=headers,
        )

        # Link them
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure",
            json={"visit_id": "visit_ret", "procedure_id": "proc_ret"},
            headers=headers,
        )
        await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/links/timing",
            json={
                "source_id": "proc_ret",
                "timing_id": "timing_ret",
                "source_type": "procedure",
            },
            headers=headers,
        )

        # Check projection before retirement
        res_proj_1 = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
            headers=headers,
        )
        assert res_proj_1.status_code == 200
        proj_1 = res_proj_1.json()
        assert len(proj_1["encounters"]) == 1
        assert len(proj_1["rows"]) == 1
        assert proj_1["rows"][0]["cells"][0]["details"] == "timing_ret"

        # 2. Retire the Timing Window
        res_retire_timing = await client.delete(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/timing_ret",
            headers=headers,
        )
        assert res_retire_timing.status_code == 200

        # Check projection: timing window is excluded/details is None (as link target is retired)
        res_proj_2 = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
            headers=headers,
        )
        assert res_proj_2.json()["rows"][0]["cells"][0]["details"] is None

        # Verify Timing Window node still exists, but version_index is incremented and is_retired is True
        res_get_tw = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/timing_ret",
            headers=headers,
        )
        assert res_get_tw.status_code == 200
        tw_detail = res_get_tw.json()
        assert tw_detail["version_index"] == 2
        assert tw_detail["is_retired"] is True

        # 3. Retire the Visit
        res_retire_visit = await client.delete(
            f"/api/v1/studies/{study_id}/versions/{version_id}/visits/visit_ret",
            headers=headers,
        )
        assert res_retire_visit.status_code == 200

        # Check projection: encounters list is now empty as the visit is retired
        res_proj_3 = await client.get(
            f"/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
            headers=headers,
        )
        assert len(res_proj_3.json()["encounters"]) == 0
