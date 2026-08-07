import hashlib
import hmac
import json
import time

import httpx
import pytest

from apps.designer.db import (
    MOCK_ELIGIBILITY_CRITERIA,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Adding eligibility criteria",
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


@pytest.mark.asyncio
async def test_eligibility_criteria_crud_endpoints():
    # @req:PRD-MDR-007
    # Clear mock storage first
    MOCK_ELIGIBILITY_CRITERIA.clear()
    MOCK_STUDY_VERSIONS.clear()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a valid criterion
        create_payload = {
            "criterion_id": "INC_01",
            "criterion_type": "inclusion",
            "description": "Subject must be 18 years of age or older.",
            "dsl_source": "eCRF.DM.AGE >= 18",
            "expected_outcome": True,
            "change_reason": "Adding minimum age requirement",
        }

        res = await client.post(
            "/api/v1/studies/study_1/eligibility-criteria",
            json=create_payload,
            headers=get_auth_headers(),
        )
        assert res.status_code == 201
        data = res.json()
        assert data["criterion_id"] == "INC_01"
        assert data["criterion_type"] == "inclusion"
        assert data["dsl_source"] == "eCRF.DM.AGE >= 18"
        assert data["expected_outcome"] is True
        assert data["version_index"] == 1
        assert "condition" in data
        assert data["condition"]["type"] == "comparison"

        # 2. Get list of criteria
        res_list = await client.get(
            "/api/v1/studies/study_1/eligibility-criteria",
            headers=get_auth_headers(),
        )
        assert res_list.status_code == 200
        criteria_list = res_list.json()
        assert len(criteria_list) == 1
        assert criteria_list[0]["criterion_id"] == "INC_01"

        # 3. Get criterion details
        res_get = await client.get(
            "/api/v1/studies/study_1/eligibility-criteria/INC_01",
            headers=get_auth_headers(),
        )
        assert res_get.status_code == 200
        assert res_get.json()["criterion_id"] == "INC_01"

        # 4. Update the criterion with a valid update and verify version index increments
        update_payload = {
            "criterion_type": "inclusion",
            "description": "Subject must be 21 years of age or older.",
            "dsl_source": "eCRF.DM.AGE >= 21",
            "expected_outcome": True,
            "change_reason": "Updating minimum age requirement to 21",
        }

        res_put = await client.put(
            "/api/v1/studies/study_1/eligibility-criteria/INC_01",
            json=update_payload,
            headers=get_auth_headers(),
        )
        assert res_put.status_code == 200
        updated_data = res_put.json()
        assert updated_data["criterion_id"] == "INC_01"
        assert updated_data["dsl_source"] == "eCRF.DM.AGE >= 21"
        assert updated_data["version_index"] == 2
        assert (
            updated_data["reason_for_change"]
            == "Updating minimum age requirement to 21"
        )


@pytest.mark.asyncio
async def test_eligibility_criteria_validation_failures():
    # @req:PRD-MDR-007
    MOCK_ELIGIBILITY_CRITERIA.clear()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Invalid DSL syntax
        invalid_payload = {
            "criterion_id": "INC_02",
            "criterion_type": "inclusion",
            "description": "Invalid age requirement",
            "dsl_source": "eCRF.DM.AGE => 18",  # invalid operator =>
            "expected_outcome": True,
            "change_reason": "Adding invalid age",
        }

        res = await client.post(
            "/api/v1/studies/study_1/eligibility-criteria",
            json=invalid_payload,
            headers=get_auth_headers(),
        )
        assert res.status_code == 422
        assert "Invalid DSL expression or reference" in res.json()["detail"]


@pytest.mark.asyncio
async def test_eligibility_criteria_immutability():
    # @req:PRD-MDR-007
    MOCK_ELIGIBILITY_CRITERIA.clear()
    MOCK_STUDY_VERSIONS.clear()

    # Freeze the study version
    from apps.designer.db import create_mock_study_version

    create_mock_study_version(
        "study_1",
        {
            "id": "v1_frozen",
            "version_tag": "1.0",
            "status": "LOCKED",
            "version_index": 1,
            "created_by": "test_designer",
        },
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Attempt to create criterion on a frozen/locked version study should fail with 403 Forbidden
        payload = {
            "criterion_id": "INC_01",
            "criterion_type": "inclusion",
            "description": "Subject must be 18 years of age or older.",
            "dsl_source": "eCRF.DM.AGE >= 18",
            "expected_outcome": True,
            "change_reason": "Adding age requirement",
        }

        res = await client.post(
            "/api/v1/studies/study_1/eligibility-criteria",
            json=payload,
            headers=get_auth_headers(),
        )
        assert res.status_code == 403
        assert res.json()["detail"] == "IMMUTABILITY_VIOLATION"


@pytest.mark.asyncio
async def test_eligibility_criteria_usdm_projection():
    # @req:PRD-MDR-007
    MOCK_ELIGIBILITY_CRITERIA.clear()
    MOCK_STUDY_VERSIONS.clear()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a criterion
        create_payload = {
            "criterion_id": "INC_01",
            "criterion_type": "inclusion",
            "description": "Subject must be 18 years of age or older.",
            "dsl_source": "eCRF.DM.AGE >= 18",
            "expected_outcome": True,
            "change_reason": "Adding minimum age requirement",
        }

        await client.post(
            "/api/v1/studies/study_1/eligibility-criteria",
            json=create_payload,
            headers=get_auth_headers(),
        )

        # Retrieve USDM projection and verify eligibility criteria are in the stable structure
        res_usdm = await client.get(
            "/api/v2/studies/study_1/usdm",
            headers=get_auth_headers(),
        )
        assert res_usdm.status_code == 200
        usdm_data = res_usdm.json()
        assert "eligibility_criteria" in usdm_data
        assert len(usdm_data["eligibility_criteria"]) == 1
        crit_mapped = usdm_data["eligibility_criteria"][0]
        assert crit_mapped["id"] == "INC_01"
        assert crit_mapped["criterion_id"] == "INC_01"
        assert crit_mapped["type"] == "inclusion"
        assert crit_mapped["text"] == "Subject must be 18 years of age or older."
        assert crit_mapped["expression"] == "eCRF.DM.AGE >= 18"
