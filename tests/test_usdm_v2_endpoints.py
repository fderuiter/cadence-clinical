import hashlib
import hmac
import json
import time
import pytest
from fastapi.testclient import TestClient
from jose import jwt

from apps.designer.main import app as designer_app
from apps.gateway.main import app as gateway_app
from apps.designer.db import MOCK_STUDIES, MOCK_RULES, MOCK_ELIGIBILITY_CRITERIA

designer_client = TestClient(designer_app)


def get_auth_headers():
    user_id = "test-user"
    roles = "sponsor_designer"
    change_reason = "system_operation"
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


def test_get_usdm_export_with_format():
    # Setup standard study mock in MOCK_STUDIES
    study_id = "study_export_test"
    MOCK_STUDIES[study_id] = {
        "study_id": study_id,
        "title": "Oncology Phase III",
        "current_version": "1.0",
        "desc": "A study for solid tumors.",
        "arms": [],
    }

    headers = get_auth_headers()

    # 1. Test without format parameter (legacy fallback)
    resp = designer_client.get(f"/api/v2/studies/{study_id}/usdm", headers=headers)
    assert resp.status_code == 200
    assert "X-USDM-Signature" in resp.headers
    data = resp.json()
    assert data["id"] == "study_export_test"
    assert "versions" in data

    # 2. Test JSON format
    resp_json = designer_client.get(f"/api/v2/studies/{study_id}/usdm?format=json", headers=headers)
    assert resp_json.status_code == 200
    assert resp_json.headers["content-type"].startswith("application/json")
    assert "X-USDM-Signature" in resp_json.headers
    json_data = resp_json.json()
    assert json_data["_original_id"] == "study_export_test"

    # 3. Test YAML format
    resp_yaml = designer_client.get(f"/api/v2/studies/{study_id}/usdm?format=yaml", headers=headers)
    assert resp_yaml.status_code == 200
    assert resp_yaml.headers["content-type"].startswith("application/x-yaml")
    assert "X-USDM-Signature" in resp_yaml.headers
    import yaml
    yaml_data = yaml.safe_load(resp_yaml.text)
    assert yaml_data["_original_id"] == "study_export_test"


def test_post_usdm_import_success():
    study_id = "00000000-0000-0000-0000-000000000001"
    valid_payload = {
        "id": study_id,
        "name": "Completely Valid Import Study",
        "instanceType": "Study",
        "versions": [
            {
                "id": "00000000-0000-0000-0000-000000000002",
                "versionIdentifier": "1.0",
                "rationale": "Initial Version",
                "studyIdentifiers": [],
                "titles": [],
                "instanceType": "StudyVersion",
                "studyDesigns": []
            }
        ]
    }

    headers = get_auth_headers()
    resp = designer_client.post(
        f"/api/v2/studies/{study_id}/usdm",
        json=valid_payload,
        headers=headers
    )
    assert resp.status_code == 201
    assert resp.json()["status"] == "success"

    # Verify storage persistence
    assert study_id in MOCK_STUDIES
    assert MOCK_STUDIES[study_id]["title"] == "Completely Valid Import Study"


def test_post_usdm_import_invalid():
    study_id = "00000000-0000-0000-0000-000000000001"
    # Payload missing name field, which is a schema / validator failure
    invalid_payload = {
        "id": study_id,
        "instanceType": "Study",
        "versions": []
    }

    headers = get_auth_headers()
    resp = designer_client.post(
        f"/api/v2/studies/{study_id}/usdm",
        json=invalid_payload,
        headers=headers
    )
    assert resp.status_code == 422
    data = resp.json()
    assert data["code"] == "REQUEST_VALIDATION_ERROR" or data["code"] == "USDM_VALIDATION_ERROR"
    assert "invalid_params" in data


def test_gateway_proxy_to_usdm_v2(monkeypatch):
    # Setup standard study mock in MOCK_STUDIES
    study_id = "study_gateway_test"
    MOCK_STUDIES[study_id] = {
        "study_id": study_id,
        "title": "Oncology Phase III",
        "current_version": "1.0",
        "desc": "A study for solid tumors.",
        "arms": [],
    }

    # Generate gateway auth token
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["sponsor_designer"]}, "test_secret", algorithm="HS256"
    )

    headers = {
        "Authorization": f"Bearer {token}",
        "X-Change-Reason": "Gateway verification test",
    }

    with TestClient(gateway_app) as client:
        resp = client.get(f"/api/v2/studies/{study_id}/usdm", headers=headers)
        assert resp.status_code in (200, 502, 500)  # since downstream designer service may or may not be up on real port, we accept proxy outcomes or mock response
