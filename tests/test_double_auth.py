import time
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from jose import jwt

from apps.gateway.main import GATEWAY_SECRET, app, generate_signature
from packages.security.middleware import (
    GatewayAuthMiddleware,
)


def test_signature_verification_keycloak_token_secret(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test signature verification route when KEYCLOAK_CLIENT_SECRET is configured.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    monkeypatch.setenv("KEYCLOAK_CLIENT_SECRET", "super_secret_client_key")

    token = jwt.encode(
        {
            "sub": "user_dm_123",
            "preferred_username": "user_dm_123",
            "realm_access": {"roles": ["data_manager"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user_dm_123",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert "sig_token" in data


def test_signature_verification_role_insufficient_auth(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test signature verification route when user lacks permitted signing role.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    token = jwt.encode(
        {
            "sub": "unauthorized_user",
            "preferred_username": "unauthorized_user",
            "realm_access": {"roles": ["patient_or_unauthorized"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "unauthorized_user",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 403
        assert response.json()["detail"] == "ROLE_INSUFFICIENT"


def test_signature_verification_token_expiration(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @Req:PRD-QRY-005
    # @req:PRD-QRY-005
    Test that signature verification tokens expire and are rejected after 60s.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    jwt.encode(
        {
            "sub": "cra_user_1",
            "preferred_username": "cra_user_1",
            "realm_access": {"roles": ["cra"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    # 1. Generate expired sig_token manually
    now = time.time()
    payload = {
        "sub": "cra_user_1",
        "username": "cra_user_1",
        "action": "/api/v1/execution/form-submissions/123/approve",
        "roles": ["cra"],
        "iat": now - 120.0,
        "exp": now - 60.0,
    }
    expired_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    # Use a dummy app with GatewayAuthMiddleware to simulate the downstream verification behavior
    test_app = FastAPI()
    test_app.add_middleware(GatewayAuthMiddleware)

    @test_app.post("/api/v1/execution/form-submissions/{submission_id}/approve")
    async def approve_endpoint():
        return {"status": "success"}

    client = TestClient(test_app)
    timestamp = str(time.time())
    sig = generate_signature(
        "cra_user_1",
        "cra",
        timestamp,
        version="2",
        change_reason="PI Sign-off",
        sig_token=expired_token,
        tenant_id="tenant_default",
    )

    headers = {
        "X-User-Id": "cra_user_1",
        "X-User-Roles": "cra",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": "PI Sign-off",
        "X-Sig-Token": expired_token,
    }

    # 2. Downstream validation should reject expired token
    response = client.post(
        "/api/v1/execution/form-submissions/123/approve",
        headers=headers,
    )
    assert response.status_code == 401
    assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_signature_verification_replay_attack_prevention(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @Req:PRD-QRY-005
    # @req:PRD-QRY-005
    Test replay prevention of sig_token.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # Initialize a new TestClient with the gateway app
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    token = jwt.encode(
        {
            "sub": "cra_user_2",
            "preferred_username": "cra_user_2",
            "realm_access": {"roles": ["cra"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        # Mint token
        reauth_resp = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "cra_user_2",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert reauth_resp.status_code == 200
        sig_token = reauth_resp.json()["sig_token"]

        # First request should pass
        res_first = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert res_first.status_code == 200

        # Second request using the same sig_token should be rejected (replay blocked)
        res_second = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert res_second.status_code == 401
        assert res_second.json()["detail"] == "REAUTHENTICATION_REQUIRED"
