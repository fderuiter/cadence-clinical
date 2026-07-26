import pytest
from fastapi.testclient import TestClient

from apps.designer.db import MOCK_STUDIES, MOCK_STUDY_VERSIONS
from apps.designer.delta import MOCK_SOA_DATA, _init_mock_soa
from apps.designer.main import app as designer_app
from tests.test_designer_differences import get_auth_headers


@pytest.fixture
def client():
    return TestClient(designer_app)


def test_version_diff_success(client):
    # Setup mock study and version metadata
    study_id = "study_diff_test"
    MOCK_STUDIES[study_id] = {
        "study_id": study_id,
        "title": "Diff Test Study",
        "current_version": "2.0",
    }

    # Setup mock study versions
    v1_id = "ver_old"
    v2_id = "ver_new"
    MOCK_STUDY_VERSIONS[study_id] = [
        {
            "id": v1_id,
            "version_tag": "1.0",
            "status": "PUBLISHED",
            "version_index": 1,
            "created_by": "test_user",
        },
        {
            "id": v2_id,
            "version_tag": "2.0",
            "status": "DRAFT",
            "version_index": 2,
            "created_by": "test_user",
        },
    ]

    # Initialize MOCK_SOA_DATA for old version
    _init_mock_soa(v1_id)
    # Add forms to old version
    # F1 (to be modified), F2 (to be deleted), F3 (to be unchanged)
    MOCK_SOA_DATA[v1_id]["forms"] = {
        "form_1": {
            "id": "form_1",
            "form_key": "F1",
            "xform_definition_xml": "<xml>old_content</xml>",
        },
        "form_2": {
            "id": "form_2",
            "form_key": "F2",
            "xform_definition_xml": "<xml>deleted_content</xml>",
        },
        "form_3": {
            "id": "form_3",
            "form_key": "F3",
            "xform_definition_xml": "<xml>unchanged_content</xml>",
        },
    }

    # Initialize MOCK_SOA_DATA for new version
    _init_mock_soa(v2_id)
    # Add forms to new version
    # F1 (modified), F3 (unchanged), F4 (added)
    MOCK_SOA_DATA[v2_id]["forms"] = {
        "form_1": {
            "id": "form_1",
            "form_key": "F1",
            "xform_definition_xml": "<xml>new_content</xml>",
        },
        "form_3": {
            "id": "form_3",
            "form_key": "F3",
            "xform_definition_xml": "<xml>unchanged_content</xml>",
        },
        "form_4": {
            "id": "form_4",
            "form_key": "F4",
            "xform_definition_xml": "<xml>added_content</xml>",
        },
    }

    # Request the diff
    response = client.get(
        f"/api/v1/studies/{study_id}/versions/diff?version_id1={v1_id}&version_id2={v2_id}",
        headers=get_auth_headers(),
    )
    assert response.status_code == 200
    data = response.json()

    # Validate added_nodes
    added = data["added_nodes"]
    assert len(added) == 1
    assert added[0]["field"] == "F4"
    assert added[0]["old_value"] is None
    assert added[0]["new_value"] == "<xml>added_content</xml>"

    # Validate modified_nodes
    modified = data["modified_nodes"]
    assert len(modified) == 1
    assert modified[0]["field"] == "F1"
    assert modified[0]["old_value"] == "<xml>old_content</xml>"
    assert modified[0]["new_value"] == "<xml>new_content</xml>"

    # Validate deleted_nodes
    deleted = data["deleted_nodes"]
    assert len(deleted) == 1
    assert deleted[0]["field"] == "F2"
    assert deleted[0]["old_value"] == "<xml>deleted_content</xml>"
    assert deleted[0]["new_value"] is None


def test_version_diff_unrelated_or_nonexistent(client):
    study_id = "study_diff_test"

    # Nonexistent version
    response = client.get(
        f"/api/v1/studies/{study_id}/versions/diff?version_id1=nonexistent_1&version_id2=ver_new",
        headers=get_auth_headers(),
    )
    assert response.status_code == 400
    assert "not found" in response.json()["detail"]

    # Nonexistent study
    response = client.get(
        "/api/v1/studies/nonexistent_study/versions/diff?version_id1=ver_old&version_id2=ver_new",
        headers=get_auth_headers(),
    )
    assert response.status_code == 400
    assert "not found" in response.json()["detail"]
