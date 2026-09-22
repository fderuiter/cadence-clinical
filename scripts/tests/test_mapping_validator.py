from apps.designer.main import app
from fastapi.testclient import TestClient

from packages.testing.security import create_test_auth_headers

client = TestClient(app)


def get_auth_headers():
    return create_test_auth_headers(
        user_id="test-user",
        roles=["sponsor_designer"],
        change_reason="system_operation",
    )


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
