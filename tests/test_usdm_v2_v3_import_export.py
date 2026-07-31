"""Unit and integration test suite for the USDM v2/v3 Import and Export API endpoints.

# @Req:PRD-MDR-007
# @req:PRD-MDR-007
"""

import copy
import json

import pytest
import yaml
from fastapi.testclient import TestClient

from apps.designer.db import MOCK_STUDIES
from apps.designer.main import app as designer_app
from apps.designer.mapper import to_uuid
from apps.designer.serialization import serialize_usdm
from tests.test_designer_differences import get_auth_headers


@pytest.fixture
def client():
    return TestClient(designer_app)


def test_get_usdm_study_raw_dict(client):
    """Validate that GET /api/v2/studies/{study_id}/usdm returns raw dict when format is omitted.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm", headers=headers)
    assert response.status_code == 200
    data = response.json()
    assert isinstance(data, dict)
    assert data["id"] == "study_1"
    assert data["name"] == "Oncology Phase II"
    assert "versions" in data


def test_get_usdm_study_serialized_json(client):
    """Validate that GET /api/v2/studies/{study_id}/usdm returns serialized JSON when format=json.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=json", headers=headers)
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")

    # Verify it is valid JSON and parses correctly
    parsed = json.loads(response.text)
    assert parsed["instanceType"] == "Study"
    assert "versions" in parsed


def test_get_usdm_study_serialized_yaml(client):
    """Validate that GET /api/v2/studies/{study_id}/usdm returns serialized YAML when format=yaml.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=yaml", headers=headers)
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/x-yaml")

    # Verify it is valid YAML
    parsed = yaml.safe_load(response.text)
    assert parsed["instanceType"] == "Study"


def test_get_usdm_study_invalid_format(client):
    """Validate that GET /api/v2/studies/{study_id}/usdm returns 400 for unsupported format.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=xml", headers=headers)
    assert response.status_code == 400
    assert "Unsupported format" in response.json()["detail"]


def test_get_usdm_study_not_found(client):
    """Validate that GET /api/v2/studies/{study_id}/usdm returns 404 if study does not exist.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_nonexistent/usdm", headers=headers)
    assert response.status_code == 404


def test_post_usdm_import_success_json(client):
    """Validate that POST /api/v2/studies/{study_id}/usdm successfully imports valid USDM JSON.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()

    # Fetch a valid USDM JSON payload first
    resp_get = client.get("/api/v2/studies/study_1/usdm?format=json", headers=headers)
    assert resp_get.status_code == 200
    payload = resp_get.json()

    # Prepare unique import ID and valid UUID
    import_id = "study_import_json"
    import_uuid = to_uuid(import_id, "study")

    payload_copy = copy.deepcopy(payload)
    payload_copy["id"] = import_uuid
    payload_copy["_original_id"] = import_id
    payload_copy["name"] = "JSON Import Test Study"

    response = client.post(
        f"/api/v2/studies/{import_id}/usdm",
        json=payload_copy,
        headers=headers,
    )
    assert response.status_code == 200
    assert response.json()["status"] == "success"

    # Assert study is persisted in database and get_study_projection fetches it correctly
    assert import_id in MOCK_STUDIES
    study_data = MOCK_STUDIES[import_id]
    assert study_data["title"] == "JSON Import Test Study"
    assert study_data["current_version"] == "2.1"
    assert len(study_data["arms"]) == 1
    assert study_data["arms"][0]["arm_id"] == "arm_1"


def test_post_usdm_import_success_yaml(client):
    """Validate that POST /api/v2/studies/{study_id}/usdm successfully imports valid USDM YAML.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()

    # Fetch a valid USDM JSON payload first
    resp_get = client.get("/api/v2/studies/study_1/usdm?format=json", headers=headers)
    assert resp_get.status_code == 200
    payload = resp_get.json()

    # Prepare unique import ID and valid UUID
    import_id = "study_import_yaml"
    import_uuid = to_uuid(import_id, "study")

    payload_copy = copy.deepcopy(payload)
    payload_copy["id"] = import_uuid
    payload_copy["_original_id"] = import_id
    payload_copy["name"] = "YAML Import Test Study"

    # Convert payload_copy to a serialized YAML string
    yaml_payload = serialize_usdm(
        payload_copy, format_type="yaml", style="canonical", validate=True
    )

    headers_yaml = get_auth_headers()
    headers_yaml["content-type"] = "application/x-yaml"

    response = client.post(
        f"/api/v2/studies/{import_id}/usdm",
        content=yaml_payload,
        headers=headers_yaml,
    )
    assert response.status_code == 200
    assert response.json()["status"] == "success"

    # Assert study is persisted
    assert import_id in MOCK_STUDIES
    study_data = MOCK_STUDIES[import_id]
    assert study_data["title"] == "YAML Import Test Study"


def test_post_usdm_import_validation_failure(client):
    """Validate that POST /api/v2/studies/{study_id}/usdm returns 422 ProblemDetails on validation failure.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    # Missing root 'name' field
    invalid_payload = {
        "id": to_uuid("study_import_invalid", "study"),
        "instanceType": "Study",
        "versions": [],
    }

    response = client.post(
        "/api/v2/studies/study_import_invalid/usdm",
        json=invalid_payload,
        headers=headers,
    )
    assert response.status_code == 422
    data = response.json()
    assert "invalid_params" in data
    assert data["code"] == "USDM_VALIDATION_ERROR"


def test_post_usdm_import_path_mismatch(client):
    """Validate that POST /api/v2/studies/{study_id}/usdm returns 400 when path ID does not match payload ID.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()
    payload = {
        "id": to_uuid("study_some_other_id", "study"),
        "name": "Some Study",
        "instanceType": "Study",
        "versions": [],
    }

    response = client.post(
        "/api/v2/studies/study_different_path/usdm",
        json=payload,
        headers=headers,
    )
    assert response.status_code == 400
    assert "does not match payload study ID" in response.json()["detail"]


def test_usdm_round_trip_full_api_flow(client):
    """Validate complete end-to-end GET -> POST -> GET round trip flow.

    # @Req:PRD-MDR-007
    # @req:PRD-MDR-007
    """
    headers = get_auth_headers()

    # 1. Fetch study_1 as raw dict
    resp_get = client.get("/api/v2/studies/study_1/usdm", headers=headers)
    assert resp_get.status_code == 200
    raw_study_dict = resp_get.json()

    # Set unique ID to test importing it
    import_id = "study_roundtrip_flow"
    import_uuid = to_uuid(import_id, "study")

    raw_study_dict["id"] = import_uuid
    raw_study_dict["_original_id"] = import_id
    if "versions" in raw_study_dict:
        raw_study_dict["versions"][0]["id"] = to_uuid(f"ver_{import_id}", "version")
        raw_study_dict["versions"][0]["_original_id"] = f"ver_{import_id}"

    # 2. Import it under the new ID
    resp_post = client.post(
        f"/api/v2/studies/{import_id}/usdm",
        json=raw_study_dict,
        headers=headers,
    )
    assert resp_post.status_code == 200

    # 3. Fetch the imported study under the new ID and assert exact match
    resp_get_new = client.get(f"/api/v2/studies/{import_id}/usdm", headers=headers)
    assert resp_get_new.status_code == 200
    new_study_dict = resp_get_new.json()

    assert new_study_dict["id"] == import_id
    assert new_study_dict["name"] == raw_study_dict["name"]
    assert len(new_study_dict["arms"]) == len(raw_study_dict["arms"])
