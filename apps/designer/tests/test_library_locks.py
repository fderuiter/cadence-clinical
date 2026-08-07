import httpx
import pytest

from apps.designer.db import (
    MOCK_LIBRARY_OBJECTS,
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.delta import (
    MOCK_LIBRARY_INSTANCES,
)
from apps.designer.main import app
from apps.designer.tests.test_global_library_api import get_auth_headers


@pytest.fixture(autouse=True)
def clean_mock_stores():
    """Clears and restores mock store baselines to prevent test pollution."""
    import copy

    # Backup
    orig_studies = copy.deepcopy(MOCK_STUDIES)
    orig_versions = copy.deepcopy(MOCK_STUDY_VERSIONS)
    orig_lib_objs = copy.deepcopy(MOCK_LIBRARY_OBJECTS)
    orig_lib_insts = copy.deepcopy(MOCK_LIBRARY_INSTANCES)

    yield

    # Restore
    MOCK_STUDIES.clear()
    MOCK_STUDIES.update(orig_studies)

    MOCK_STUDY_VERSIONS.clear()
    MOCK_STUDY_VERSIONS.update(orig_versions)

    MOCK_LIBRARY_OBJECTS.clear()
    MOCK_LIBRARY_OBJECTS.update(orig_lib_objs)

    MOCK_LIBRARY_INSTANCES.clear()
    MOCK_LIBRARY_INSTANCES.update(orig_lib_insts)


@pytest.mark.asyncio
async def test_library_object_active_study_lock():
    """Verify that attempting to update a library object in-use by an active study returns a 409.
    # @req:PRD-SYS-001
    """
    # 1. Setup - Create a DRAFT library object
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        designer_headers = get_auth_headers(
            user_id="designer_user", roles="sponsor_designer", sponsor_id="spon_pharma"
        )
        form_payload = {
            "id": "lib_locked_form",
            "version": "1.0.0",
            "status": "APPROVED",
            "sponsor_id": "spon_pharma",
            "change_reason": "Setup lock test form",
            "object_type": "FORM",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Age:",
                        "data_type": "integer",
                        "required": True,
                    }
                ]
            },
        }
        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=designer_headers,
        )
        assert res_create.status_code == 201

        # 2. Setup active study and reference this library object version 1
        MOCK_STUDIES["study_recruiting_lock"] = {
            "study_id": "study_recruiting_lock",
            "status": "Active-Recruiting",
            "sponsor_id": "spon_pharma",
        }
        MOCK_STUDY_VERSIONS["study_recruiting_lock"] = [{"status": "Active-Recruiting"}]
        MOCK_LIBRARY_INSTANCES["study_recruiting_lock"] = [
            {
                "id": "inst_1",
                "study_id": "study_recruiting_lock",
                "object_type": "FORM",
                "payload": {},
                "instantiated_from": {
                    "library_object_id": "lib_locked_form",
                    "version": 1,
                    "sponsor_id": "spon_pharma",
                },
            }
        ]

        # 3. Attempt update (PUT) should fail with 409 pointing to amendment workflow
        update_payload = {
            "object_type": "FORM",
            "reason_for_change": "Trying to update in-use object directly",
            "payload": {
                "items": [
                    {
                        "item_id": "item_age",
                        "name": "AGE",
                        "question_text": "Age in years:",
                        "data_type": "integer",
                        "required": True,
                    }
                ]
            },
        }
        res_put = await client.put(
            "/api/v1/mdr/library/lib_locked_form",
            json=update_payload,
            headers=designer_headers,
        )
        assert res_put.status_code == 409
        data_put = res_put.json()
        assert data_put["detail"] == "LIBRARY_OBJECT_LOCKED_ACTIVE_STUDY"
        assert "amendment workflow" in data_put["message"].lower()
        assert data_put["object_id"] == "lib_locked_form"
        assert "/amend" in data_put["workflow_suggestion"]


@pytest.mark.asyncio
async def test_library_object_author_self_approval_block():
    """Verify that the author of a library object draft/version cannot self-approve it.
    # @req:PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Create a DRAFT library object with creator "designer_author"
        author_headers = get_auth_headers(
            user_id="designer_author",
            roles="sponsor_designer",
            sponsor_id="spon_pharma",
        )
        form_payload = {
            "id": "lib_self_app_form",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_pharma",
            "change_reason": "Setup author test form",
            "object_type": "FORM",
            "payload": {"items": []},
        }
        res_create = await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=author_headers,
        )
        assert res_create.status_code == 201

        # 2. Transition DRAFT -> IN_REVIEW
        res_review = await client.post(
            "/api/v1/mdr/library/lib_self_app_form/transition",
            json={"status": "IN_REVIEW", "change_reason": "Ready for review"},
            headers=author_headers,
        )
        assert res_review.status_code == 200

        # 3. Attempt self-approval using same author "designer_author" with role "sponsor_dm" -> 403 Forbidden
        author_dm_headers = get_auth_headers(
            user_id="designer_author", roles="sponsor_dm", sponsor_id="spon_pharma"
        )
        res_self_approve = await client.post(
            "/api/v1/mdr/library/lib_self_app_form/transition",
            json={"status": "APPROVED", "change_reason": "Trying to self approve"},
            headers=author_dm_headers,
        )
        assert res_self_approve.status_code == 403
        assert "Author cannot self-approve" in res_self_approve.json()["detail"]

        # 4. Successful approval by a different reviewer "reviewer_dm" with role "sponsor_dm"
        reviewer_headers = get_auth_headers(
            user_id="reviewer_dm", roles="sponsor_dm", sponsor_id="spon_pharma"
        )
        res_approve = await client.post(
            "/api/v1/mdr/library/lib_self_app_form/transition",
            json={"status": "APPROVED", "change_reason": "Approved by non-author"},
            headers=reviewer_headers,
        )
        assert res_approve.status_code == 200
        assert res_approve.json()["status"] == "APPROVED"


@pytest.mark.asyncio
async def test_library_object_rbac_permissions():
    """Verify fine-grained permission gating over global library status transitions.
    # @req:PRD-SYS-001
    """
    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # Setup form
        creator_headers = get_auth_headers(
            user_id="creator", roles="sponsor_designer", sponsor_id="spon_pharma"
        )
        form_payload = {
            "id": "lib_perm_form",
            "version": "1.0.0",
            "status": "DRAFT",
            "sponsor_id": "spon_pharma",
            "change_reason": "Setup permission test form",
            "object_type": "FORM",
            "payload": {"items": []},
        }
        await client.post(
            "/api/v1/mdr/library",
            json=form_payload,
            headers=creator_headers,
        )

        # Move to IN_REVIEW
        await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "IN_REVIEW", "change_reason": "Review time"},
            headers=creator_headers,
        )

        # 1. sponsor_designer does NOT have "library_object:approve" permission -> APPROVED transition fails with 403
        res_designer_approve = await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "APPROVED", "change_reason": "Designer approving"},
            headers=creator_headers,
        )
        assert res_designer_approve.status_code == 403

        # 2. sponsor_dm has "library_object:approve" -> APPROVED succeeds
        dm_headers = get_auth_headers(
            user_id="dm_user", roles="sponsor_dm", sponsor_id="spon_pharma"
        )
        res_dm_approve = await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "APPROVED", "change_reason": "DM approving"},
            headers=dm_headers,
        )
        assert res_dm_approve.status_code == 200

        # 3. sponsor_dm has "library_object:publish" -> PUBLISHED succeeds and metadata is sealed
        res_publish = await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "PUBLISHED", "change_reason": "DM publishing"},
            headers=dm_headers,
        )
        assert res_publish.status_code == 200
        published_data = res_publish.json()
        assert published_data["status"] == "PUBLISHED"

        # Verify cryptographic seal / signature exists on the published object version
        assert "signature" in MOCK_LIBRARY_OBJECTS["lib_perm_form"][-1]
        seal = MOCK_LIBRARY_OBJECTS["lib_perm_form"][-1]["signature"]
        assert seal is not None
        assert len(seal) > 0

        # 4. Try to archive using sponsor_dm -> fails with 403 because they lack "library_object:release"
        res_dm_archive = await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "ARCHIVED", "change_reason": "DM archiving"},
            headers=dm_headers,
        )
        assert res_dm_archive.status_code == 403

        # 5. admin / sponsor_admin has "library_object:release" -> ARCHIVED succeeds
        admin_headers = get_auth_headers(
            user_id="admin_user", roles="sponsor_admin", sponsor_id="spon_pharma"
        )
        res_admin_archive = await client.post(
            "/api/v1/mdr/library/lib_perm_form/transition",
            json={"status": "ARCHIVED", "change_reason": "Admin archiving"},
            headers=admin_headers,
        )
        assert res_admin_archive.status_code == 200
