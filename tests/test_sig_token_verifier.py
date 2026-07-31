import time

import pytest
from fastapi import HTTPException
from jose import jwt

from packages.security.sig_token_verifier import (
    token_consumption_cache,
    verify_and_consume_sig_token,
)

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def test_verify_and_consume_sig_token_success() -> None:
    """Validate that a valid signature token is successfully verified and consumed."""
    token_consumption_cache.reset()
    user_id = "test_user_123"
    now = time.time()
    payload = {
        "sub": user_id,
        "username": "test_user_123",
        "action": "/api/v1/execution/batch-sign-off",
        "roles": ["investigator"],
        "iat": now,
        "exp": now + 60.0,
        "jti": "jti-unique-success-1",
        "acr": "high-assurance-step-up",
        "auth_time": now,
    }
    sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    decoded_payload = verify_and_consume_sig_token(sig_token, user_id)
    assert decoded_payload["sub"] == user_id
    assert decoded_payload["acr"] == "high-assurance-step-up"
    assert decoded_payload["auth_time"] == now


def test_verify_and_consume_sig_token_expired() -> None:
    """Validate that an expired signature token is rejected."""
    token_consumption_cache.reset()
    user_id = "test_user_123"
    now = time.time()
    payload = {
        "sub": user_id,
        "username": "test_user_123",
        "action": "/api/v1/execution/batch-sign-off",
        "roles": ["investigator"],
        "iat": now - 120.0,
        "exp": now - 60.0,
        "jti": "jti-unique-expired",
        "acr": "high-assurance-step-up",
        "auth_time": now - 120.0,
    }
    sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(sig_token, user_id)
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_verify_and_consume_sig_token_mismatched_user() -> None:
    """Validate that a signature token for a different user is rejected."""
    token_consumption_cache.reset()
    user_id = "test_user_123"
    wrong_user = "other_user_456"
    now = time.time()
    payload = {
        "sub": user_id,
        "username": "test_user_123",
        "action": "/api/v1/execution/batch-sign-off",
        "roles": ["investigator"],
        "iat": now,
        "exp": now + 60.0,
        "jti": "jti-unique-mismatched",
        "acr": "high-assurance-step-up",
        "auth_time": now,
    }
    sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(sig_token, wrong_user)
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_verify_and_consume_sig_token_replay_blocked() -> None:
    """Validate that trying to verify the same signature token twice is blocked (single-use)."""
    token_consumption_cache.reset()
    user_id = "test_user_123"
    now = time.time()
    payload = {
        "sub": user_id,
        "username": "test_user_123",
        "action": "/api/v1/execution/batch-sign-off",
        "roles": ["investigator"],
        "iat": now,
        "exp": now + 60.0,
        "jti": "jti-unique-replay-1",
        "acr": "high-assurance-step-up",
        "auth_time": now,
    }
    sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    # First consumption succeeds
    verify_and_consume_sig_token(sig_token, user_id)

    # Second consumption must fail
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(sig_token, user_id)
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"
