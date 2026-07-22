import hashlib
import hmac
import time
from unittest.mock import MagicMock, patch

import httpx
import pytest
from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app


def get_auth_headers():
    timestamp = str(time.time())
    user_id = "123"
    roles = "admin"
    secret = "internal-gateway-secret-12345"
    message = f"{user_id}:{roles}:{timestamp}"
    signature = hmac.new(secret.encode(), message.encode(), hashlib.sha256).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
    }


@pytest.fixture
def client():
    return TestClient(designer_app)


def test_study_differences_success(client):
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "versions": [
            {"id": "v1", "name": "Study A", "nested": {"field1": "A"}},
            {"id": "v2", "name": "Study B", "nested": {"field1": "B", "field2": "C"}},
        ]
    }

    with patch("httpx.AsyncClient.get", return_value=mock_response):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 200

        data = response.json()
        assert len(data) == 4

        fields = {d["field"]: d for d in data}
        assert "name" in fields
        assert fields["name"]["old_value"] == "Study A"
        assert fields["name"]["new_value"] == "Study B"

        assert "nested.field1" in fields
        assert fields["nested.field1"]["old_value"] == "A"
        assert fields["nested.field1"]["new_value"] == "B"

        assert "nested.field2" in fields
        assert fields["nested.field2"]["old_value"] is None
        assert fields["nested.field2"]["new_value"] == "C"


def test_study_differences_missing_version(client):
    mock_response = MagicMock()
    mock_response.status_code = 200
    mock_response.json.return_value = {
        "versions": [
            {"id": "v1", "name": "Study A"},
        ]
    }

    with patch("httpx.AsyncClient.get", return_value=mock_response):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 400
        assert (
            "Target version v2 is missing from the registry"
            in response.json()["detail"]
        )


def test_study_differences_registry_timeout(client):
    with patch("httpx.AsyncClient.get", side_effect=httpx.TimeoutException("timeout")):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 504
        assert response.json()["detail"] == "Registry timeout"


def test_study_differences_registry_offline(client):
    with patch("httpx.AsyncClient.get", side_effect=httpx.RequestError("offline")):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 502
        assert response.json()["detail"] == "External registry offline"


def test_study_differences_registry_404(client):
    mock_request = httpx.Request("GET", "http://test")
    mock_response = httpx.Response(404, request=mock_request)
    with patch(
        "httpx.AsyncClient.get",
        side_effect=httpx.HTTPStatusError(
            "404", request=mock_request, response=mock_response
        ),
    ):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 404
        assert response.json()["detail"] == "Study not found in registry"


def test_study_differences_registry_error(client):
    mock_request = httpx.Request("GET", "http://test")
    mock_response = httpx.Response(500, request=mock_request)
    with patch(
        "httpx.AsyncClient.get",
        side_effect=httpx.HTTPStatusError(
            "500", request=mock_request, response=mock_response
        ),
    ):
        response = client.get(
            "/api/v1/studies/study-123/differences?action_id1=v1&action_id2=v2",
            headers=get_auth_headers(),
        )
        assert response.status_code == 500
        assert response.json()["detail"] == "Registry error"
