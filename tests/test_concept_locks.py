import httpx
import pytest

from apps.designer.db import (
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
    check_dict_for_value,
    is_concept_referenced_by_active_recruiting_study,
)
from apps.designer.main import app
from tests.test_soa_endpoints import get_auth_headers


def test_check_dict_for_value():
    """Verify recursive check_dict_for_value logic."""
    data = {
        "id": "study_test_val",
        "arms": [
            {"type_concept_id": "C999", "visits": [{"visit_type_concept_id": "C111"}]}
        ],
    }
    assert check_dict_for_value(data, "C999") is True
    assert check_dict_for_value(data, "C111") is True
    assert check_dict_for_value(data, "C000") is False


@pytest.mark.asyncio
async def test_is_concept_referenced_by_active_recruiting_study():
    """Verify is_concept_referenced_by_active_recruiting_study identifies references under Active-Recruiting."""
    # Setup mock studies & versions
    MOCK_STUDIES["study_recruiting_test"] = {
        "study_id": "study_recruiting_test",
        "status": "Active-Recruiting",
        "arms": [{"arm_id": "arm_rec_1", "type_concept_id": "C_REC_123"}],
    }
    MOCK_STUDY_VERSIONS["study_recruiting_test"] = [{"status": "Active-Recruiting"}]

    MOCK_STUDIES["study_draft_test"] = {
        "study_id": "study_draft_test",
        "status": "DRAFT",
        "arms": [{"arm_id": "arm_draft_1", "type_concept_id": "C_DRAFT_123"}],
    }
    MOCK_STUDY_VERSIONS["study_draft_test"] = [{"status": "DRAFT"}]

    try:
        # C_REC_123 is active recruiting and should return True
        res_rec = await is_concept_referenced_by_active_recruiting_study("C_REC_123")
        assert res_rec is True

        # C_DRAFT_123 is DRAFT (not Active-Recruiting) and should return False
        res_draft = await is_concept_referenced_by_active_recruiting_study(
            "C_DRAFT_123"
        )
        assert res_draft is False

        # C_UNREF is unreferenced and should return False
        res_unref = await is_concept_referenced_by_active_recruiting_study("C_UNREF")
        assert res_unref is False

    finally:
        # Cleanup
        MOCK_STUDIES.pop("study_recruiting_test", None)
        MOCK_STUDY_VERSIONS.pop("study_recruiting_test", None)
        MOCK_STUDIES.pop("study_draft_test", None)
        MOCK_STUDY_VERSIONS.pop("study_draft_test", None)


@pytest.mark.asyncio
async def test_concept_mutations_unreferenced():
    """Verify that unreferenced concepts can be updated, renamed, and deleted successfully."""
    # Ensure no active-recruiting references
    assert "C_UNREF" not in MOCK_STUDIES

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        headers = get_auth_headers()

        # 1. Update Concept (PUT)
        update_payload = {
            "display_name": "Unreferenced Heart Rate",
            "definition": "Standard heart rate measurements",
            "reason_for_change": "Regular metadata update",
        }
        res_put = await client.put(
            "/api/v1/mdr/concepts/C_UNREF", json=update_payload, headers=headers
        )
        assert res_put.status_code == 200
        assert res_put.json()["display_name"] == "Unreferenced Heart Rate"

        # 2. Rename Concept (POST)
        rename_payload = {
            "display_name": "Renamed Unreferenced Heart Rate",
            "reason_for_change": "Direct rename operation",
        }
        res_rename = await client.post(
            "/api/v1/mdr/concepts/C_UNREF/rename", json=rename_payload, headers=headers
        )
        assert res_rename.status_code == 200
        assert res_rename.json()["display_name"] == "Renamed Unreferenced Heart Rate"

        # 3. Delete Concept (DELETE)
        res_delete = await client.delete(
            "/api/v1/mdr/concepts/C_UNREF", headers=headers
        )
        assert res_delete.status_code == 200
        assert res_delete.json()["status"] == "success"
        assert "deleted successfully" in res_delete.json()["message"]


@pytest.mark.asyncio
async def test_concept_mutations_locked_active_recruiting():
    """Verify that mutations are rejected with 409 Conflict when a concept is referenced by an Active-Recruiting study."""
    # Inject active-recruiting study referencing C_LOCKED
    MOCK_STUDIES["study_recruiting_lock"] = {
        "study_id": "study_recruiting_lock",
        "status": "Active-Recruiting",
        "arms": [{"arm_id": "arm_locked_1", "type_concept_id": "C_LOCKED"}],
    }
    MOCK_STUDY_VERSIONS["study_recruiting_lock"] = [{"status": "Active-Recruiting"}]

    try:
        async with httpx.AsyncClient(
            transport=httpx.ASGITransport(app=app), base_url="http://test"
        ) as client:
            headers = get_auth_headers()

            # 1. Attempt Update (PUT)
            update_payload = {
                "display_name": "Locked Heart Rate",
                "definition": "This shouldn't be allowed",
                "reason_for_change": "Illegal modification request",
            }
            res_put = await client.put(
                "/api/v1/mdr/concepts/C_LOCKED", json=update_payload, headers=headers
            )
            assert res_put.status_code == 409
            data_put = res_put.json()
            assert data_put["detail"] == "CONCEPT_LOCKED_ACTIVE_STUDY"
            assert "protocol amendment workflow" in data_put["message"].lower()
            assert data_put["concept_id"] == "C_LOCKED"
            assert "/amend" in data_put["workflow_suggestion"]

            # 2. Attempt Rename (POST)
            rename_payload = {
                "display_name": "Locked Name Change",
                "reason_for_change": "Illegal rename request",
            }
            res_rename = await client.post(
                "/api/v1/mdr/concepts/C_LOCKED/rename",
                json=rename_payload,
                headers=headers,
            )
            assert res_rename.status_code == 409
            data_rename = res_rename.json()
            assert data_rename["detail"] == "CONCEPT_LOCKED_ACTIVE_STUDY"
            assert "protocol amendment workflow" in data_rename["message"].lower()
            assert data_rename["concept_id"] == "C_LOCKED"
            assert "/amend" in data_rename["workflow_suggestion"]

            # 3. Attempt Delete (DELETE)
            res_delete = await client.delete(
                "/api/v1/mdr/concepts/C_LOCKED", headers=headers
            )
            assert res_delete.status_code == 409
            data_delete = res_delete.json()
            assert data_delete["detail"] == "CONCEPT_LOCKED_ACTIVE_STUDY"
            assert "protocol amendment workflow" in data_delete["message"].lower()
            assert data_delete["concept_id"] == "C_LOCKED"
            assert "/amend" in data_delete["workflow_suggestion"]

    finally:
        # Cleanup
        MOCK_STUDIES.pop("study_recruiting_lock", None)
        MOCK_STUDY_VERSIONS.pop("study_recruiting_lock", None)
