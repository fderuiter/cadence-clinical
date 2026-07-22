import pytest
from fastapi.testclient import TestClient
from jose import jwt

from apps.gateway.main import app, generate_signature, verify_token


def test_verify_token_invalid():
    with pytest.raises(Exception):
        verify_token("invalid_token")


def test_generate_signature():
    sig = generate_signature("user1", "admin", "12345")
    assert sig is not None


def test_proxy_requests_no_auth():
    with TestClient(app) as client:
        response = client.get("/api/v1/studies/study_1")
        assert response.status_code == 401


def test_proxy_requests_invalid_auth():
    with TestClient(app) as client:
        response = client.get(
            "/api/v1/studies/study_1", headers={"Authorization": "Bearer invalid"}
        )
        assert response.status_code == 401


def test_proxy_requests_valid_auth(monkeypatch):
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )
    with TestClient(app) as client:
        response = client.get(
            "/api/v1/studies/study_1", headers={"Authorization": f"Bearer {token}"}
        )
        assert response.status_code in [200, 502, 500]
