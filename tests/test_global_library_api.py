import hashlib
import hmac
import json
import time

import httpx
import pytest

from apps.designer.db import MOCK_LIBRARY_OBJECTS
from apps.designer.main import app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Global library test operations",
    sponsor_id="spon_pharma",
    tenant_id="tenant_001",
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
        "X-Sponsor-Id": sponsor_id,
        "X-Tenant-Id": tenant_id,
    }


@pytest.fixture(autouse=True)
def clean_mock_library():
    """Clears MOCK_LIBRARY_OBJECTS before each test."""
    MOCK_LIBRARY_OBJECTS.clear()
    yield
    MOCK_LIBRARY_OBJECTS.clear()


@pytest.mark.asyncio
async def test_create_and_retrieve_library_objects():
    """
    Verifies creation and retrieval endpoints for Global Library Objects.
    Enforces that sponsor scope from request state is used, override payload-supplied sponsor,
    and handles validation/errors properly.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_pharma")

        # 1. Successful creation of a FORM object (override caller-supplied sponsor_id="spon_fake")
        form_payload = {
            "id": "lib_form_bp",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_fake",  # Should be overridden by spon_pharma
            "change_reason": "Create blood pressure form template",
            "object_type": "FORM",
            "payload": {
                "items": [
                    {
                        "item_id": "item_sbp",
                        "name": "VSSBP",
                        "question_text": "Systolic BP",
                        "data_type": "integer",
                        "required": True,
                    }
                ]
            },
        }

        res = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers,
        )
        assert res.status_code == 201
        data = res.json()
        assert data["id"] == "lib_form_bp"
        assert data["object_type"] == "FORM"
        assert (
            data["sponsor_id"] == "spon_pharma"
        )  # Verified: overridden by authenticated sponsor!
        assert data["version"] == "1.0.0"

        # 2. Duplicate ID creation attempt -> should fail with 409 Conflict
        res_dup = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers,
        )
        assert res_dup.status_code == 409
        assert "already exists" in res_dup.json()["detail"]

        # 3. Successful retrieval of the created object
        res_get = await client.get(
            "/api/v1/mdr/library/lib_form_bp",
            headers=headers,
        )
        assert res_get.status_code == 200
        get_data = res_get.json()
        assert get_data["id"] == "lib_form_bp"
        assert get_data["sponsor_id"] == "spon_pharma"
        assert get_data["payload"]["items"][0]["item_id"] == "item_sbp"

        # 4. Attempt retrieval from other sponsor scope -> should fail with 404
        other_headers = get_auth_headers(sponsor_id="spon_other")
        res_get_other = await client.get(
            "/api/v1/mdr/library/lib_form_bp",
            headers=other_headers,
        )
        assert res_get_other.status_code == 404


@pytest.mark.asyncio
async def test_update_and_history_versioning():
    """
    Verifies PUT updates create a new version instead of overwriting,
    checks immutability violation constraints, and ensures correct history retrieval.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_cardiology")

        # 1. Create original version 1
        de_payload = {
            "id": "lib_de_hr",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_cardiology",
            "change_reason": "Create heart rate concept",
            "object_type": "DATA_ELEMENT",
            "payload": {
                "data_type": "numeric",
                "allowable_units": ["bpm"],
                "default_unit": "bpm",
            },
        }

        res_create = await client.post(
            "/api/v1/mdr/library",
            json=de_payload,
            headers=headers,
        )
        assert res_create.status_code == 201

        # 2. Update as new version (version 2)
        update_payload = {
            "object_type": "DATA_ELEMENT",
            "reason_for_change": "Adding more descriptive text or bounds",
            "payload": {
                "data_type": "numeric",
                "allowable_units": ["bpm", "hz"],
                "default_unit": "bpm",
            },
        }

        res_update = await client.put(
            "/api/v1/mdr/library/lib_de_hr",
            json=update_payload,
            headers=headers,
        )
        assert res_update.status_code == 200
        up_data = res_update.json()
        assert up_data["version"] == "2.0.0"  # Successfully versioned up!
        assert "hz" in up_data["payload"]["allowable_units"]

        # 3. Retrieve version 1 explicitly
        res_v1 = await client.get(
            "/api/v1/mdr/library/lib_de_hr?version=1",
            headers=headers,
        )
        assert res_v1.status_code == 200
        v1_data = res_v1.json()
        assert v1_data["version"] == "1.0.0"
        assert v1_data["payload"]["allowable_units"] == ["bpm"]

        # 4. Retrieve version history
        res_hist = await client.get(
            "/api/v1/mdr/library/lib_de_hr/history",
            headers=headers,
        )
        assert res_hist.status_code == 200
        history = res_hist.json()
        assert len(history) == 2
        assert history[0]["version"] == "1.0.0"
        assert history[1]["version"] == "2.0.0"

        # 5. Lock/Archive the latest version to verify immutability
        # Set status of latest version in mock database directly to simulate a published/archived state
        MOCK_LIBRARY_OBJECTS["lib_de_hr"][-1]["status"] = "ARCHIVED"

        # Attempting another update after ARCHIVED status -> should return 403 Forbidden
        res_fail_update = await client.put(
            "/api/v1/mdr/library/lib_de_hr",
            json=update_payload,
            headers=headers,
        )
        assert res_fail_update.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail_update.json()["detail"]


@pytest.mark.asyncio
async def test_stripe_style_pagination_and_filtering():
    """
    Verifies listing global library objects with Stripe-style pagination, limits,
    starting_after cursors, and filtering by object_type.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_pharma")

        # Create multiple items of different types
        items = [
            (
                "lib_arm_01",
                "ARM",
                {
                    "attributes": {
                        "arm_type": "TREATMENT",
                        "target_sample_size": 100,
                        "randomization_ratio": "1:1",
                    }
                },
            ),
            (
                "lib_arm_02",
                "ARM",
                {
                    "attributes": {
                        "arm_type": "PLACEBO",
                        "target_sample_size": 100,
                        "randomization_ratio": "1:1",
                    }
                },
            ),
            ("lib_form_01", "FORM", {"items": []}),
            ("lib_form_02", "FORM", {"items": []}),
            (
                "lib_visit_01",
                "VISIT",
                {
                    "attributes": {
                        "visit_type": "SCREENING",
                        "planned_day": 0,
                        "window_days": 2,
                    }
                },
            ),
        ]

        for oid, otype, payload in items:
            creation_body = {
                "id": oid,
                "version": "1.0.0",
                "status": "APPROVED",
                "sponsor_id": "spon_pharma",
                "change_reason": f"Initial creation of {oid}",
                "object_type": otype,
                "payload": payload,
            }
            res = await client.post(
                "/api/v1/mdr/library",
                json=creation_body,
                headers=headers,
            )
            assert res.status_code == 201

        # 1. List all with no filters
        res_all = await client.get(
            "/api/v1/mdr/library",
            headers=headers,
        )
        assert res_all.status_code == 200
        all_data = res_all.json()
        assert all_data["object"] == "list"
        assert len(all_data["data"]) == 5
        assert all_data["has_more"] is False
        assert [x["id"] for x in all_data["data"]] == [
            "lib_arm_01",
            "lib_arm_02",
            "lib_form_01",
            "lib_form_02",
            "lib_visit_01",
        ]

        # 2. List with object_type filtering
        res_arms = await client.get(
            "/api/v1/mdr/library?object_type=ARM",
            headers=headers,
        )
        assert res_arms.status_code == 200
        arms_data = res_arms.json()
        assert len(arms_data["data"]) == 2
        assert all(x["object_type"] == "ARM" for x in arms_data["data"])

        # 3. Limit-based pagination and Stripe-style next_cursor
        res_p1 = await client.get(
            "/api/v1/mdr/library?limit=2",
            headers=headers,
        )
        assert res_p1.status_code == 200
        p1_data = res_p1.json()
        assert len(p1_data["data"]) == 2
        assert p1_data["has_more"] is True
        assert p1_data["next_cursor"] == "lib_arm_02"

        # Fetch page 2 using starting_after cursor
        res_p2 = await client.get(
            f"/api/v1/mdr/library?limit=2&starting_after={p1_data['next_cursor']}",
            headers=headers,
        )
        assert res_p2.status_code == 200
        p2_data = res_p2.json()
        assert len(p2_data["data"]) == 2
        assert p2_data["has_more"] is True
        assert [x["id"] for x in p2_data["data"]] == ["lib_form_01", "lib_form_02"]

        # Fetch page 3
        res_p3 = await client.get(
            f"/api/v1/mdr/library?limit=2&starting_after={p2_data['next_cursor']}",
            headers=headers,
        )
        assert res_p3.status_code == 200
        p3_data = res_p3.json()
        assert len(p3_data["data"]) == 1
        assert p3_data["has_more"] is False
        assert p3_data["data"][0]["id"] == "lib_visit_01"


@pytest.mark.asyncio
async def test_auth_and_malformed_requests():
    """
    Verifies missing sponsor headers, missing change justification, and malformed bodies.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Missing sponsor scope -> should return 403 Forbidden
        headers_no_sponsor = get_auth_headers()
        headers_no_sponsor.pop("X-Sponsor-Id")

        form_payload = {
            "id": "lib_form_test",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_pharma",
            "change_reason": "Creating a blood pressure form",
            "object_type": "FORM",
            "payload": {"items": []},
        }

        res = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers_no_sponsor,
        )
        assert res.status_code == 403
        assert "authenticated sponsor scope" in res.json()["detail"]

        # 2. Missing change justification reason -> should return 400 Bad Request
        headers_no_reason = get_auth_headers(change_reason=" ")
        # Note: middleware checks for presence, but if whitespace we want 400
        res_no_reason = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers_no_reason,
        )
        assert res_no_reason.status_code == 400
        assert "change justification" in res_no_reason.json()["detail"]

        # 3. Malformed payload combination (mismatched object_type and payload structure) -> 422 Unprocessable Entity
        malformed_payload = {
            "id": "lib_form_malformed",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_pharma",
            "change_reason": "Creating mismatched payload",
            "object_type": "FORM",
            "payload": {
                # FORM expects 'items' list, but we give it ARM payload format
                "attributes": {
                    "arm_type": "TREATMENT",
                    "target_sample_size": 50,
                    "randomization_ratio": "1:1",
                }
            },
        }

        res_malformed = await client.post(
            "/api/v1/mdr/library",
            json=malformed_payload,
            headers=get_auth_headers(sponsor_id="spon_pharma"),
        )
        assert res_malformed.status_code == 422
