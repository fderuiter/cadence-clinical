import hashlib
import hmac
import json
import time
import pytest
import yaml
import copy
from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.designer.db import get_study_projection
from apps.designer.mapper import to_uuid, map_study_to_usdm

client = TestClient(designer_app)


def get_auth_headers(change_reason: str = "USDM Import/Export test"):
    user_id = "test-user-v2"
    roles = "sponsor_designer"
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        b"internal-gateway-secret-12345", serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


def remove_key_recursive(d, key_to_remove):
    if isinstance(d, dict):
        d.pop(key_to_remove, None)
        for k, v in list(d.items()):
            remove_key_recursive(v, key_to_remove)
    elif isinstance(d, list):
        for item in d:
            remove_key_recursive(item, key_to_remove)


def test_get_usdm_study_v2_fallback():
    # Retrieve study_1 from in-memory mock data
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm", headers=headers)
    assert response.status_code == 200
    data = response.json()
    assert data["id"] == "study_1"
    assert "versions" in data


def test_get_usdm_study_v2_format_json():
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=json", headers=headers)
    assert response.status_code == 200
    assert response.headers["content-type"].startswith("application/json")

    data = json.loads(response.text)
    assert data["id"] == to_uuid("study_1", "study")
    assert data["_original_id"] == "study_1"
    assert data["instanceType"] == "Study"


def test_get_usdm_study_v2_format_yaml():
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=yaml", headers=headers)
    assert response.status_code == 200
    assert "application/yaml" in response.headers["content-type"]

    data = yaml.safe_load(response.text)
    assert data["id"] == to_uuid("study_1", "study")
    assert data["_original_id"] == "study_1"
    assert data["instanceType"] == "Study"


def test_get_usdm_study_v2_invalid_format():
    headers = get_auth_headers()
    response = client.get("/api/v2/studies/study_1/usdm?format=invalid_fmt", headers=headers)
    assert response.status_code == 400
    assert "Unsupported format" in response.json()["detail"]


def test_import_usdm_study_v2_valid_json():
    # Generate a fully valid USDM payload from study_1
    study_data = get_study_projection("study_1")
    usdm_payload = map_study_to_usdm(study_data)

    # Decouple from study_1
    clean_payload = copy.deepcopy(usdm_payload)
    remove_key_recursive(clean_payload, "_original_id")

    # Customize the ID for the imported study
    study_uuid = "00000000-0000-0000-0000-000000000100"
    clean_payload["id"] = study_uuid
    clean_payload["name"] = "Imported Study JSON"

    headers = get_auth_headers("Import test validation")
    response = client.post(
        f"/api/v2/studies/{study_uuid}/usdm",
        json=clean_payload,
        headers=headers
    )
    assert response.status_code == 201

    data = response.json()
    assert data["study_id"] == study_uuid
    assert data["title"] == "Imported Study JSON"
    assert len(data["arms"]) == 1
    assert data["arms"][0]["name"] == "Arm A"

    # Verify that the study has been persisted in MOCK_STUDIES
    persisted = get_study_projection(study_uuid)
    assert persisted is not None
    assert persisted["title"] == "Imported Study JSON"


def test_import_usdm_study_v2_valid_yaml():
    # Generate fully valid USDM payload from study_1
    study_data = get_study_projection("study_1")
    usdm_payload = map_study_to_usdm(study_data)

    # Decouple from study_1
    clean_payload = copy.deepcopy(usdm_payload)
    remove_key_recursive(clean_payload, "_original_id")

    # Customize the ID for the imported study
    study_uuid = "00000000-0000-0000-0000-000000000101"
    clean_payload["id"] = study_uuid
    clean_payload["name"] = "Imported Study YAML"

    valid_yaml_payload = yaml.dump(clean_payload)

    headers = get_auth_headers("Import test YAML validation")
    response = client.post(
        f"/api/v2/studies/{study_uuid}/usdm",
        content=valid_yaml_payload,
        headers=headers
    )
    assert response.status_code == 201

    data = response.json()
    assert data["study_id"] == study_uuid
    assert data["title"] == "Imported Study YAML"

    # Verify database persistence
    persisted = get_study_projection(study_uuid)
    assert persisted is not None
    assert persisted["title"] == "Imported Study YAML"


def test_import_usdm_study_v2_invalid_structure():
    invalid_payload = {
        "id": "00000000-0000-0000-0000-000000000102",
        # Missing required field "name"
        "instanceType": "Study",
        "versions": []
    }

    headers = get_auth_headers()
    response = client.post(
        "/api/v2/studies/00000000-0000-0000-0000-000000000102/usdm",
        json=invalid_payload,
        headers=headers
    )
    assert response.status_code == 422
    data = response.json()
    assert data["code"] == "USDM_VALIDATION_ERROR"
    assert any("non-empty physical name/title" in param["reason"] for param in data["invalid_params"])


def test_import_usdm_study_v2_id_mismatch():
    study_uuid = "00000000-0000-0000-0000-000000000103"
    valid_payload_mismatch = {
        "id": "00000000-0000-0000-0000-000000000104",
        "name": "Mismatched Study",
        "instanceType": "Study",
        "versions": []
    }

    headers = get_auth_headers()
    response = client.post(
        f"/api/v2/studies/{study_uuid}/usdm",
        json=valid_payload_mismatch,
        headers=headers
    )
    assert response.status_code == 400
    assert "does not match payload study_id" in response.json()["detail"]
