import json

import pytest
import yaml
from fastapi.testclient import TestClient

from apps.designer.db import MOCK_STUDIES
from apps.designer.main import app as designer_app

client = TestClient(designer_app)


def get_auth_headers(change_reason="system_operation", roles="sponsor_designer"):
    import hashlib
    import hmac
    import json
    import time

    user_id = "test-user"
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


def test_api_v2_export_default():
    # If format query param is omitted, should return raw dict mappings directly
    response = client.get("/api/v2/studies/study_1/usdm", headers=get_auth_headers())
    assert response.status_code == 200
    assert isinstance(response.json(), dict)
    assert response.json()["id"] == "study_1"


def test_api_v2_export_json_and_yaml():
    # If format is json, should return serialized JSON
    response = client.get(
        "/api/v2/studies/study_1/usdm?format=json", headers=get_auth_headers()
    )
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/json"
    parsed_json = json.loads(response.text)
    assert (
        parsed_json["id"] == "1407067d-bde8-5c1b-9aec-5bf54117ff51"
    )  # proper to_uuid representation of study_1

    # If format is yaml, should return serialized YAML
    response_yaml = client.get(
        "/api/v2/studies/study_1/usdm?format=yaml", headers=get_auth_headers()
    )
    assert response_yaml.status_code == 200
    assert response_yaml.headers["content-type"] == "application/x-yaml"
    parsed_yaml = yaml.safe_load(response_yaml.text)
    assert parsed_yaml["id"] == "1407067d-bde8-5c1b-9aec-5bf54117ff51"


def test_api_v2_export_invalid_format():
    response = client.get(
        "/api/v2/studies/study_1/usdm?format=invalid_format", headers=get_auth_headers()
    )
    assert response.status_code == 400
    assert "Unsupported format type" in response.json()["detail"]


def test_api_v2_import_valid_yaml():
    from apps.designer.db import get_study_projection
    from apps.designer.mapper import map_study_to_usdm
    from apps.designer.serialization import serialize_usdm

    # Map study_1 to get a guaranteed valid schema
    proj = get_study_projection("study_1")
    # Change the ID and name
    target_study_id = "00000000-0000-0000-0000-000000000001"
    proj["study_id"] = target_study_id
    proj["title"] = "YAML Imported Study"

    usdm_payload = map_study_to_usdm(proj)
    # Serialize it as canonical YAML
    valid_payload_yaml = serialize_usdm(
        usdm_payload, format_type="yaml", style="canonical", validate=True
    )

    headers = get_auth_headers(change_reason="Valid YAML Import GxP comment")

    response = client.post(
        f"/api/v2/studies/{target_study_id}/usdm",
        content=valid_payload_yaml,
        headers=headers,
    )
    print("STATUS:", response.status_code)
    print("ERRORS:", response.json() if response.status_code != 201 else "")
    assert response.status_code == 201
    assert response.json()["status"] == "success"

    # Confirm the projection was persisted in the DB
    assert target_study_id in MOCK_STUDIES
    imported = MOCK_STUDIES[target_study_id]
    assert imported["title"] == "YAML Imported Study"
    assert imported["change_reason"] == "Valid YAML Import GxP comment"
    assert imported["created_by"] == "test-user"


def test_api_v2_import_validation_failure():
    # Missing required 'name' field
    invalid_payload = """
id: 00000000-0000-0000-0000-000000000001
versions: []
"""
    headers = get_auth_headers(change_reason="Invalid Import")

    response = client.post(
        "/api/v2/studies/00000000-0000-0000-0000-000000000001/usdm",
        content=invalid_payload,
        headers=headers,
    )
    assert response.status_code == 422
    problem = response.json()
    assert problem["code"] == "USDM_VALIDATION_ERROR"


def test_api_v2_import_missing_change_reason():
    valid_payload = """
id: 00000000-0000-0000-0000-000000000001
name: No Reason Study
instanceType: Study
versions: []
"""
    headers = get_auth_headers(change_reason="")

    response = client.post(
        "/api/v2/studies/00000000-0000-0000-0000-000000000001/usdm",
        content=valid_payload,
        headers=headers,
    )
    assert response.status_code in (400, 403)
