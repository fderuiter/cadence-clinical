from fastapi.testclient import TestClient

from apps.designer.main import app

client = TestClient(app)


def get_auth_headers():
    import hashlib
    import hmac
    import json
    import time

    user_id = "test-user"
    roles = "test-role"
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


def test_valid_csv():
    csv_content = "to_name,to_alias\nvalidName,aliasName\nprefix:local,namespace:alias"
    files = {"file": ("mapping.csv", csv_content, "text/csv")}
    response = client.post(
        "/api/v1/mappings/upload", files=files, headers=get_auth_headers()
    )
    assert response.status_code == 200


def test_invalid_leading_number():
    csv_content = "to_name,to_alias\n1invalid,valid\n"
    files = {"file": ("mapping.csv", csv_content, "text/csv")}
    response = client.post(
        "/api/v1/mappings/upload", files=files, headers=get_auth_headers()
    )
    assert response.status_code == 422
    assert "Invalid XML name" in response.text


def test_invalid_spacing():
    csv_content = "to_name,to_alias\nvalid,\nspaced name,valid\n"
    files = {"file": ("mapping.csv", csv_content, "text/csv")}
    response = client.post(
        "/api/v1/mappings/upload", files=files, headers=get_auth_headers()
    )
    assert response.status_code == 422
    assert "Invalid XML name" in response.text


def test_multiple_colons():
    csv_content = "to_name,to_alias\npre:fix:name,valid\n"
    files = {"file": ("mapping.csv", csv_content, "text/csv")}
    response = client.post(
        "/api/v1/mappings/upload", files=files, headers=get_auth_headers()
    )
    assert response.status_code == 422
    assert "Invalid XML name" in response.text
