import hashlib
import hmac
import json
import time

import httpx
import pytest

from apps.designer.db import MOCK_STUDY_VERSIONS
from apps.designer.delta import MOCK_SOA_DATA
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


@pytest.fixture(autouse=True)
def clean_mock_data():
    """Clears MOCK_SOA_DATA and MOCK_STUDY_VERSIONS before each test to ensure test isolation."""
    MOCK_SOA_DATA.clear()
    MOCK_STUDY_VERSIONS.clear()


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
            json={"id": "arm_1", "properties": {"name": "Arm A"}},
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
            json={"id": "arm_1", "properties": {"name": "Arm A"}},
        )
        assert res.status_code == 403
