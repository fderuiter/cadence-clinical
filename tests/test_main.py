from fastapi.testclient import TestClient

from apps.designer.main import app as designer_app
from apps.execution.main import app as execution_app
from apps.gateway.main import app as gateway_app


def test_designer_health():
    with TestClient(designer_app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "designer"}


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
