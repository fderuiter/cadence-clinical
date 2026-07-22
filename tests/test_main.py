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
    with TestClient(designer_app) as client:
        response = client.get("/differences")
        assert response.status_code == 401
        assert response.json()["detail"] == "Missing gateway authentication headers"


def test_designer_gateway_auth_invalid_timestamp():
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
    import time
    with TestClient(designer_app) as client:
        headers = {
            "X-User-Id": "123",
            "X-User-Roles": "admin",
            "X-Gateway-Timestamp": str(time.time()),
            "X-Gateway-Signature": "invalid-sig",
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
