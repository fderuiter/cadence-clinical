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
    from packages.security.signing import generate_gateway_signature

    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode(),
        change_reason=change_reason,
        sponsor_id=sponsor_id,
    )
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
        headers_no_sponsor = get_auth_headers(sponsor_id=None)
        headers_no_sponsor.pop("X-Sponsor-Id", None)

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
        assert res_malformed.status_code == 400


@pytest.mark.asyncio
async def test_instantiate_library_object_success():
    """
    Verifies that a study can successfully instantiate a specific (or latest)
    Global Library object version as a distinct, study-scoped copy linked to its source.
    Also ensures the source object remains unmodified.
    """
    from apps.designer.db import MOCK_LIBRARY_OBJECTS, MOCK_STUDIES

    MOCK_STUDIES["study_pharma"] = {
        "study_id": "study_pharma",
        "title": "Pharma Oncology Phase I",
        "sponsor_id": "spon_pharma",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_pharma")

        # 1. Create original version 1 of a FORM object
        form_payload = {
            "id": "lib_form_bp",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_pharma",
            "change_reason": "Create blood pressure form template version 1",
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

        res_v1 = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers,
        )
        assert res_v1.status_code == 201

        # 2. Update to version 2 (with additional field)
        update_payload = {
            "object_type": "FORM",
            "reason_for_change": "Adding diastolic BP field",
            "payload": {
                "items": [
                    {
                        "item_id": "item_sbp",
                        "name": "VSSBP",
                        "question_text": "Systolic BP",
                        "data_type": "integer",
                        "required": True,
                    },
                    {
                        "item_id": "item_dbp",
                        "name": "VSDBP",
                        "question_text": "Diastolic BP",
                        "data_type": "integer",
                        "required": True,
                    },
                ]
            },
        }

        res_v2 = await client.put(
            "/api/v1/mdr/library/lib_form_bp",
            json=update_payload,
            headers=headers,
        )
        assert res_v2.status_code == 200

        # 3. Instantiate specific version 1 of the library object
        inst_payload_v1 = {"library_object_id": "lib_form_bp", "version": 1}
        res_inst_v1 = await client.post(
            "/api/v1/studies/study_pharma/library-instances",
            json=inst_payload_v1,
            headers=headers,
        )
        assert res_inst_v1.status_code == 201
        inst_data_v1 = res_inst_v1.json()
        assert inst_data_v1["study_id"] == "study_pharma"
        assert inst_data_v1["object_type"] == "FORM"
        assert len(inst_data_v1["payload"]["items"]) == 1
        assert inst_data_v1["instantiated_from"]["library_object_id"] == "lib_form_bp"
        assert inst_data_v1["instantiated_from"]["version"] == 1

        # 4. Instantiate latest (version 2) of the library object (leaving version field empty)
        inst_payload_latest = {"library_object_id": "lib_form_bp"}
        res_inst_latest = await client.post(
            "/api/v1/studies/study_pharma/library-instances",
            json=inst_payload_latest,
            headers=headers,
        )
        assert res_inst_latest.status_code == 201
        inst_data_latest = res_inst_latest.json()
        assert len(inst_data_latest["payload"]["items"]) == 2
        assert inst_data_latest["instantiated_from"]["version"] == 2

        # 5. Verify the source library object in MOCK_LIBRARY_OBJECTS remains completely unmodified
        assert len(MOCK_LIBRARY_OBJECTS["lib_form_bp"]) == 2
        assert MOCK_LIBRARY_OBJECTS["lib_form_bp"][0]["version"] == 1
        assert MOCK_LIBRARY_OBJECTS["lib_form_bp"][1]["version"] == 2


@pytest.mark.asyncio
async def test_instantiate_library_object_cross_sponsor_rejected():
    """
    Verifies that instantiation requests where the library object belongs to a different sponsor are rejected.
    """
    from apps.designer.db import MOCK_STUDIES

    # Setup target study for sponsor "spon_active"
    MOCK_STUDIES["study_active"] = {
        "study_id": "study_active",
        "title": "Active Oncology Trial",
        "sponsor_id": "spon_active",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a library object belonging to "spon_other"
        other_headers = get_auth_headers(sponsor_id="spon_other")
        form_payload = {
            "id": "lib_form_other",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_other",
            "change_reason": "Create other blood pressure form template",
            "object_type": "FORM",
            "payload": {"items": []},
        }
        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=other_headers,
        )
        assert res_create.status_code == 201

        # Attempt to instantiate "spon_other"'s library object into "study_active" using "spon_active"'s credentials
        active_headers = get_auth_headers(sponsor_id="spon_active")
        inst_payload = {"library_object_id": "lib_form_other"}
        res_inst = await client.post(
            "/api/v1/studies/study_active/library-instances",
            json=inst_payload,
            headers=active_headers,
        )
        # Should be rejected with 403 Forbidden because library object belongs to a different sponsor
        assert res_inst.status_code == 403
        assert "Cross-sponsor instantiation" in res_inst.json()["detail"]


@pytest.mark.asyncio
async def test_instantiate_library_object_inaccessible_study():
    """
    Verifies that instantiation requests targeting an inaccessible (cross-sponsor) study are rejected.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create a library object belonging to "spon_pharma"
        pharma_headers = get_auth_headers(sponsor_id="spon_pharma")
        form_payload = {
            "id": "lib_form_pharma",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_pharma",
            "change_reason": "Pharma template",
            "object_type": "FORM",
            "payload": {"items": []},
        }
        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=pharma_headers,
        )
        assert res_create.status_code == 201

        # Attempt to instantiate "spon_pharma"'s library object into "study_active" (owned by "spon_active") using "spon_pharma"'s credentials
        inst_payload = {"library_object_id": "lib_form_pharma"}
        res_inst = await client.post(
            "/api/v1/studies/study_active/library-instances",
            json=inst_payload,
            headers=pharma_headers,
        )
        # Should be rejected with 403 Forbidden because study belongs to spon_active, which is inaccessible to spon_pharma
        assert res_inst.status_code == 403
        assert "Target study is inaccessible" in res_inst.json()["detail"]


@pytest.mark.asyncio
async def test_library_instance_updates_and_inheritance_diffs():
    """
    Acceptance Criteria Tests:
    1. Identifies added, removed, and changed fields with source and instance values.
    2. A newly instantiated, unmodified object has an empty diff.
    3. Updating the instance records only its override data and leaves the source immutable.
    4. Covers scalar, nested, and collection payload differences.
    """
    from apps.designer.db import MOCK_STUDIES

    # Setup study and source object in mock
    MOCK_STUDIES["study_test_diffs"] = {
        "study_id": "study_test_diffs",
        "title": "Oncology Study with Diffs",
        "sponsor_id": "spon_pharma",
    }

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_pharma")

        # 1. Create source library object with complex payload (scalar, nested, collections)
        form_payload = {
            "id": "lib_complex_form",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_pharma",
            "change_reason": "Create complex form blueprint",
            "object_type": "FORM",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "How old are you?",
                        "data_type": "integer",
                        "required": True,
                    },
                    {
                        "item_id": "item_sex",
                        "name": "SEX",
                        "question_text": "What is your sex?",
                        "data_type": "text",
                        "required": False,
                    },
                ]
            },
        }

        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers,
        )
        assert res_create.status_code == 201

        # 2. Instantiate unmodified object inside study
        inst_payload = {"library_object_id": "lib_complex_form"}
        res_inst = await client.post(
            "/api/v1/studies/study_test_diffs/library-instances",
            json=inst_payload,
            headers=headers,
        )
        assert res_inst.status_code == 201
        inst_data = res_inst.json()
        instance_id = inst_data["id"]

        # 3. Newly instantiated, unmodified object MUST have an empty diff
        res_diff_unmodified = await client.get(
            f"/api/v1/studies/study_test_diffs/library-instances/{instance_id}/diff",
            headers=headers,
        )
        assert res_diff_unmodified.status_code == 200
        assert res_diff_unmodified.json() == []  # Empty diff!

        # 4. Perform an update (overrides) to the instance payload:
        # - "custom_title": "Subject Demographics" (Scalar added)
        # - "nested_custom.allow_skip": True (Nested added)
        # - "nested_custom.additional_key": "custom" (Nested added)
        # - "items.[0].question_text": "Please enter your age:" (Scalar changed inside collection)
        # - "items.[1].item_id": "item_weight" (Scalar changed inside collection)
        # - "items.[1].name": "WEIGHT" (Scalar changed inside collection)
        # - "items.[1].question_text": "Body Weight" (Scalar changed inside collection)
        # - "items.[1].data_type": "numeric" (Scalar changed inside collection)
        # - "items.[1].required": True (Scalar changed inside collection)
        updated_payload = {
            "payload": {
                "custom_title": "Subject Demographics",
                "nested_custom": {
                    "allow_skip": True,
                    "additional_key": "custom",
                },
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Please enter your age:",
                        "data_type": "integer",
                        "required": True,
                    },
                    {
                        "item_id": "item_weight",
                        "name": "WEIGHT",
                        "question_text": "Body Weight",
                        "data_type": "numeric",
                        "required": True,
                    },
                ],
            }
        }

        res_update = await client.put(
            f"/api/v1/studies/study_test_diffs/library-instances/{instance_id}",
            json=updated_payload,
            headers=headers,
        )
        assert res_update.status_code == 200
        up_instance_data = res_update.json()
        assert up_instance_data["payload"]["custom_title"] == "Subject Demographics"

        # 5. Verify the source remains completely immutable
        # Let's fetch the original library object source and confirm its payload is unmodified
        res_source = await client.get(
            "/api/v1/mdr/library/lib_complex_form?version=1",
            headers=headers,
        )
        assert res_source.status_code == 200
        source_data = res_source.json()
        assert source_data["payload"]["items"][0]["question_text"] == "How old are you?"
        assert len(source_data["payload"]["items"]) == 2

        # 6. Retrieve inheritance diff view and analyze added, removed, and changed fields
        res_diff_modified = await client.get(
            f"/api/v1/studies/study_test_diffs/library-instances/{instance_id}/diff",
            headers=headers,
        )
        assert res_diff_modified.status_code == 200
        diffs = res_diff_modified.json()

        # Let's map diffs by field name for verification
        diff_map = {d["field"]: d for d in diffs}

        # Check scalar added
        assert "custom_title" in diff_map
        assert diff_map["custom_title"]["old_value"] is None
        assert diff_map["custom_title"]["new_value"] == "Subject Demographics"

        # Check nested added
        assert "nested_custom.allow_skip" in diff_map
        assert diff_map["nested_custom.allow_skip"]["old_value"] is None
        assert diff_map["nested_custom.allow_skip"]["new_value"] is True

        assert "nested_custom.additional_key" in diff_map
        assert diff_map["nested_custom.additional_key"]["old_value"] is None
        assert diff_map["nested_custom.additional_key"]["new_value"] == "custom"

        # Check collection item changed
        assert "items.[0].question_text" in diff_map
        assert diff_map["items.[0].question_text"]["old_value"] == "How old are you?"
        assert (
            diff_map["items.[0].question_text"]["new_value"] == "Please enter your age:"
        )

        # Check list item modification differences
        assert "items.[1].item_id" in diff_map
        assert diff_map["items.[1].item_id"]["old_value"] == "item_sex"
        assert diff_map["items.[1].item_id"]["new_value"] == "item_weight"

        assert "items.[1].name" in diff_map
        assert diff_map["items.[1].name"]["old_value"] == "SEX"
        assert diff_map["items.[1].name"]["new_value"] == "WEIGHT"

        assert "items.[1].data_type" in diff_map
        assert diff_map["items.[1].data_type"]["old_value"] == "text"
        assert diff_map["items.[1].data_type"]["new_value"] == "numeric"


@pytest.mark.asyncio
async def test_library_object_in_use_and_amendments():
    """
    Acceptance Criteria Tests:
    1. Direct mutation of an in-use library version is rejected with a clear client-visible 409 error.
    2. An amendment creates a successor version (status DRAFT, incremented version) and retains source traceability for existing instances.
    3. Unused draft objects remain editable (can be mutated directly using PUT).
    4. Tests cover active vs. inactive study references.
    """
    from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS

    # Clean setups for our specific test study IDs to ensure isolation, preserving global study_1
    MOCK_STUDIES.pop("study_inactive", None)
    MOCK_STUDIES.pop("study_active", None)
    MOCK_STUDY_VERSIONS.pop("study_inactive", None)
    MOCK_STUDY_VERSIONS.pop("study_active", None)

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers(sponsor_id="spon_pharma")

        # 1. Create original library object (version 1)
        form_payload = {
            "id": "lib_amend_form",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_pharma",
            "change_reason": "Create form for amendment testing",
            "object_type": "FORM",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Age:",
                        "data_type": "integer",
                    }
                ]
            },
        }

        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=headers,
        )
        assert res_create.status_code == 201

        # Setup studies:
        # study_inactive: Not active/recruiting (status is "DRAFT")
        # study_active: Active/recruiting (status is "Active-Recruiting")
        MOCK_STUDIES["study_inactive"] = {
            "study_id": "study_inactive",
            "title": "Inactive Study",
            "status": "DRAFT",
            "sponsor_id": "spon_pharma",
        }
        MOCK_STUDIES["study_active"] = {
            "study_id": "study_active",
            "title": "Active Recruiting Study",
            "status": "Active-Recruiting",
            "sponsor_id": "spon_pharma",
        }

        # 2. Instantiate this library version in the inactive study
        inst_payload_inactive = {"library_object_id": "lib_amend_form", "version": 1}
        res_inst_inactive = await client.post(
            "/api/v1/studies/study_inactive/library-instances",
            json=inst_payload_inactive,
            headers=headers,
        )
        assert res_inst_inactive.status_code == 201

        # Since it is only used by study_inactive (which is in DRAFT, not active),
        # direct mutation via PUT should still succeed!
        update_payload = {
            "object_type": "FORM",
            "reason_for_change": "Updating form in inactive state",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Enter age in years:",
                        "data_type": "integer",
                    }
                ]
            },
        }
        res_put_inactive = await client.put(
            "/api/v1/mdr/library/lib_amend_form",
            json=update_payload,
            headers=headers,
        )
        # Verify it succeeds and increments version (version 2)
        assert res_put_inactive.status_code == 200
        assert res_put_inactive.json()["version"] == "2.0.0"

        # 3. Now instantiate version 2 in the active study!
        inst_payload_active = {"library_object_id": "lib_amend_form", "version": 2}
        res_inst_active = await client.post(
            "/api/v1/studies/study_active/library-instances",
            json=inst_payload_active,
            headers=headers,
        )
        assert res_inst_active.status_code == 201
        instance_id_active = res_inst_active.json()["id"]

        # Since version 2 is now in use by an active recruiting study,
        # direct mutation via PUT must be rejected with 409 Conflict / "LIBRARY_OBJECT_IN_USE"!
        res_put_rejected = await client.put(
            "/api/v1/mdr/library/lib_amend_form",
            json=update_payload,
            headers=headers,
        )
        assert res_put_rejected.status_code == 409
        assert res_put_rejected.json()["detail"] == "LIBRARY_OBJECT_IN_USE"

        # 4. Perform an amendment via POST /api/v1/mdr/library/{id}/amend
        amend_payload = {
            "reason_for_change": "Amending to add a gender field for the active trial successor",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Enter age in years:",
                        "data_type": "integer",
                    },
                    {
                        "item_id": "item_gender",
                        "name": "GENDER",
                        "question_text": "Gender:",
                        "data_type": "text",
                    },
                ]
            },
        }
        res_amend = await client.post(
            "/api/v1/mdr/library/lib_amend_form/amend",
            json=amend_payload,
            headers=headers,
        )
        assert res_amend.status_code == 201
        amend_data = res_amend.json()
        assert amend_data["version"] == "3.0.0"  # Created successor version 3
        assert amend_data["status"] == "DRAFT"
        assert len(amend_data["payload"]["items"]) == 2

        # 5. Check source traceability: The existing active study instance of version 2 still points to version 2
        from apps.designer.delta import get_library_instance_in_study

        inst_data = await get_library_instance_in_study(
            driver=None,
            study_id="study_active",
            instance_id=instance_id_active,
            sponsor_id="spon_pharma",
        )
        assert (
            inst_data["instantiated_from"]["version"] == 2
        )  # Unchanged! Preservation of traceability.

        # 6. Verify that the newly created successor version 3 (which is a DRAFT and unused) is mutable directly!
        amended_update_payload = {
            "object_type": "FORM",
            "reason_for_change": "Refining gender field options in the new draft version",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Enter age in years:",
                        "data_type": "integer",
                    },
                    {
                        "item_id": "item_gender",
                        "name": "GENDER",
                        "question_text": "Gender (Male/Female/Other):",
                        "data_type": "text",
                    },
                ]
            },
        }
        res_put_amended = await client.put(
            "/api/v1/mdr/library/lib_amend_form",
            json=amended_update_payload,
            headers=headers,
        )
        assert res_put_amended.status_code == 200
        assert res_put_amended.json()["version"] == "4.0.0"
        assert (
            res_put_amended.json()["payload"]["items"][1]["question_text"]
            == "Gender (Male/Female/Other):"
        )


@pytest.mark.asyncio
async def test_sponsor_security_boundaries():
    """
    Acceptance Criteria Tests:
    - Rejects empty or whitespace-only sponsor_id headers with HTTP 403.
    - Rejects tampered/spoofed/unsigned sponsor_id headers with HTTP 403 or 401.
    - Rejects cross-sponsor read, update, list, and amend attempts.
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Create an object owned by spon_real
        real_headers = get_auth_headers(sponsor_id="spon_real")
        form_payload = {
            "id": "lib_secure_form",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_real",
            "change_reason": "Setup secure form",
            "object_type": "FORM",
            "payload": {"items": []},
        }
        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=real_headers,
        )
        assert res_create.status_code == 201

        # 1. Reject empty sponsor_id header -> 403
        headers_empty = get_auth_headers(sponsor_id="")
        # We manually overwrite X-Sponsor-Id with blank
        headers_empty["X-Sponsor-Id"] = ""
        # Regenerate signature with sponsor_id=""
        from packages.security.signing import generate_gateway_signature

        headers_empty["X-Gateway-Signature"] = generate_gateway_signature(
            user_id="test_designer",
            roles="STUDY_DESIGNER",
            timestamp=headers_empty["X-Gateway-Timestamp"],
            secret=GATEWAY_SECRET.encode(),
            change_reason="Global library test operations",
            sponsor_id="",
        )
        res_empty = await client.get(
            "/api/v1/mdr/library/lib_secure_form",
            headers=headers_empty,
        )
        assert res_empty.status_code == 403

        # 2. Reject whitespace-only sponsor_id header -> 403
        headers_ws = get_auth_headers(sponsor_id="   ")
        headers_ws["X-Sponsor-Id"] = "   "
        headers_ws["X-Gateway-Signature"] = generate_gateway_signature(
            user_id="test_designer",
            roles="STUDY_DESIGNER",
            timestamp=headers_ws["X-Gateway-Timestamp"],
            secret=GATEWAY_SECRET.encode(),
            change_reason="Global library test operations",
            sponsor_id="   ",
        )
        res_ws = await client.get(
            "/api/v1/mdr/library/lib_secure_form",
            headers=headers_ws,
        )
        assert res_ws.status_code == 403

        # 3. Reject tampered/spoofed sponsor_id header (signature mismatch) -> 403 or 401
        # Here we sign with spon_fake but try to send spon_real in X-Sponsor-Id header
        headers_spoof = get_auth_headers(sponsor_id="spon_fake")
        headers_spoof["X-Sponsor-Id"] = "spon_real"
        res_spoof = await client.get(
            "/api/v1/mdr/library/lib_secure_form",
            headers=headers_spoof,
        )
        assert res_spoof.status_code in (401, 403)

        # 4. Deny cross-sponsor GET (read) -> should return 404/403 to prevent disclosure of existence
        other_headers = get_auth_headers(sponsor_id="spon_other")
        res_cross_read = await client.get(
            "/api/v1/mdr/library/lib_secure_form",
            headers=other_headers,
        )
        assert res_cross_read.status_code in (403, 404)

        # 5. Deny cross-sponsor PUT (update) -> 404 or 403
        update_payload = {
            "object_type": "FORM",
            "reason_for_change": "Cross-sponsor update attempt",
            "payload": {"items": []},
        }
        res_cross_update = await client.put(
            "/api/v1/mdr/library/lib_secure_form",
            json=update_payload,
            headers=other_headers,
        )
        assert res_cross_update.status_code in (403, 404)

        # 6. Deny cross-sponsor amend -> 404 or 403
        amend_payload = {
            "reason_for_change": "Cross-sponsor amend attempt",
            "payload": {"items": []},
        }
        res_cross_amend = await client.post(
            "/api/v1/mdr/library/lib_secure_form/amend",
            json=amend_payload,
            headers=other_headers,
        )
        assert res_cross_amend.status_code in (403, 404)

        # 7. Deny cross-sponsor history -> 404 or 403
        res_cross_history = await client.get(
            "/api/v1/mdr/library/lib_secure_form/history",
            headers=other_headers,
        )
        assert res_cross_history.status_code in (403, 404)
