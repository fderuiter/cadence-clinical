from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.execution.main import app as execution_app
from apps.gateway.main import app as gateway_app


def test_designer_health():
    with TestClient(designer_app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "designer"}


def test_designer_gateway_auth_missing_headers():
    """Test that requests missing authentication headers receive a 401 response."""
    with TestClient(designer_app) as client:
        response = client.get("/differences")
        assert response.status_code == 401
        assert response.json()["detail"] == "Missing gateway authentication headers"


def test_designer_gateway_auth_invalid_timestamp():
    """Test that requests with non-float timestamps receive a 401 response."""
    with TestClient(designer_app) as client:
        headers = {
            "X-User-Id": "123",
            "X-User-Roles": "admin",
            "X-Gateway-Timestamp": "not-a-float",
            "X-Gateway-Signature": "sig",
        }
        response = client.get("/differences", headers=headers)
        assert response.status_code == 401
        assert response.json()["detail"] == "Invalid gateway timestamp"


def test_designer_gateway_auth_expired_timestamp():
    """Test that requests with expired timestamps receive a 401 response."""
    with TestClient(designer_app) as client:
        headers = {
            "X-User-Id": "123",
            "X-User-Roles": "admin",
            "X-Gateway-Timestamp": "0",  # 1970
            "X-Gateway-Signature": "sig",
        }
        response = client.get("/differences", headers=headers)
        assert response.status_code == 401
        assert response.json()["detail"] == "Gateway signature expired"


def test_designer_gateway_auth_invalid_signature():
    """Test that requests with an invalid cryptographic signature receive a 401 response."""
    import time

    with TestClient(designer_app) as client:
        headers = {
            "X-User-Id": "123",
            "X-User-Roles": "admin",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "invalid-sig",
            "X-Signature-Version": "2",
        }
        response = client.get("/differences", headers=headers)
        assert response.status_code == 401
        assert response.json()["detail"] == "Invalid gateway signature"


def test_execution_health():
    with TestClient(execution_app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "execution"}


def test_gateway_health():
    with TestClient(gateway_app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "gateway"}
