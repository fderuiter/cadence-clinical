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
    """Validate that a valid signature token is successfully verified and consumed.

    @req:Trace-17
    """
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
    """Validate that an expired signature token is rejected.

    @req:Trace-17
    """
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
    """Validate that a signature token for a different user is rejected.

    @req:Trace-17
    """
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
    """Validate that trying to verify the same signature token twice is blocked (single-use).

    @req:Trace-17
    """
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


def test_redis_consumption_success() -> None:
    """Validate that when REDIS_URL is set, signature token consumption succeeds using Redis.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    mock_redis.set.return_value = True

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis) as mock_from_url,
    ):
        token_consumption_cache.reset()
        assert sig_token_verifier.get_redis_client() is mock_redis
        mock_from_url.assert_called_once_with(
            "redis://mocked-host:6379", decode_responses=True
        )

        user_id = "test_user_123"
        now = time.time()
        payload = {
            "sub": user_id,
            "username": "test_user_123",
            "action": "/api/v1/execution/batch-sign-off",
            "roles": ["investigator"],
            "iat": now,
            "exp": now + 60.0,
            "jti": "jti-unique-redis-success",
            "acr": "high-assurance-step-up",
            "auth_time": now,
        }
        sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

        # First consumption using Redis
        decoded_payload = verify_and_consume_sig_token(sig_token, user_id)
        assert decoded_payload["sub"] == user_id

        # Verify set was called with correct key and nx=True
        mock_redis.set.assert_called_once()
        args, kwargs = mock_redis.set.call_args
        assert args[0] == "esign_replay:jti-unique-redis-success"
        assert args[1] == "1"
        assert kwargs.get("nx") is True
        assert kwargs.get("ex") > 0

    # Cleanup global state
    sig_token_verifier._redis_client = None


def test_redis_consumption_replay_blocked() -> None:
    """Validate that when REDIS_URL is set, duplicate signature token consumption is blocked using Redis.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    # First call succeeds (True), second fails (False) because key already exists
    mock_redis.set.side_effect = [True, False]

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis),
    ):
        user_id = "test_user_123"
        now = time.time()
        payload = {
            "sub": user_id,
            "username": "test_user_123",
            "action": "/api/v1/execution/batch-sign-off",
            "roles": ["investigator"],
            "iat": now,
            "exp": now + 60.0,
            "jti": "jti-unique-redis-replay",
            "acr": "high-assurance-step-up",
            "auth_time": now,
        }
        sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

        # First consumption succeeds
        verify_and_consume_sig_token(sig_token, user_id)

        # Second consumption fails
        with pytest.raises(HTTPException) as exc_info:
            verify_and_consume_sig_token(sig_token, user_id)
        assert exc_info.value.status_code == 401
        assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"

        # Verify set was called twice
        assert mock_redis.set.call_count == 2

    # Cleanup global state
    sig_token_verifier._redis_client = None


def test_redis_consumption_fallback_on_exception() -> None:
    """Validate that if Redis raises an exception, signature token consumption falls back to in-memory dict cache.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    # set raises an error
    mock_redis.set.side_effect = Exception("Redis connection lost")

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis),
    ):
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
            "jti": "jti-unique-redis-fallback",
            "acr": "high-assurance-step-up",
            "auth_time": now,
        }
        sig_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

        # First consumption succeeds (via in-memory fallback because Redis set failed)
        decoded_payload = verify_and_consume_sig_token(sig_token, user_id)
        assert decoded_payload["sub"] == user_id

        # Second consumption fails (via in-memory fallback, even though Redis set throws exception again)
        with pytest.raises(HTTPException) as exc_info:
            verify_and_consume_sig_token(sig_token, user_id)
        assert exc_info.value.status_code == 401
        assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"

        # Verify set was called twice
        assert mock_redis.set.call_count == 2

    # Cleanup global state
    sig_token_verifier._redis_client = None


def test_redis_reset() -> None:
    """Validate that token_consumption_cache.reset() deletes signature token keys in Redis.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    mock_redis.keys.return_value = ["esign_replay:key1", "esign_replay:key2"]

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis),
    ):
        token_consumption_cache.reset()

        mock_redis.keys.assert_called_once_with("esign_replay:*")
        mock_redis.delete.assert_called_once_with(
            "esign_replay:key1", "esign_replay:key2"
        )

    # Cleanup global state
    sig_token_verifier._redis_client = None


def test_downstream_replay_cache_redis_success() -> None:
    """Validate that DownstreamReplayCache uses Redis when REDIS_URL is configured.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier
    from packages.security.middleware import downstream_replay_cache

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    mock_redis.set.return_value = True

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis),
    ):
        downstream_replay_cache.reset()

        # Test first check
        now = time.time()
        res1 = downstream_replay_cache.is_replayed("my-token", now + 60.0, "jti-123")
        assert res1 is False  # not replayed

        mock_redis.set.assert_called_with(
            "esign_replay:jti-123", "1", ex=pytest.approx(60, abs=5), nx=True
        )

        # Test second check (mocking set to return False)
        mock_redis.set.return_value = False
        res2 = downstream_replay_cache.is_replayed("my-token", now + 60.0, "jti-123")
        assert res2 is True  # replayed!

    # Cleanup global state
    sig_token_verifier._redis_client = None


def test_downstream_replay_cache_redis_reset() -> None:
    """Validate that resetting downstream_replay_cache clears Redis keys.

    @req:Trace-17
    """
    import os
    from unittest.mock import MagicMock, patch

    import packages.security.sig_token_verifier as sig_token_verifier
    from packages.security.middleware import downstream_replay_cache

    # Clear any active client
    sig_token_verifier._redis_client = None

    mock_redis = MagicMock()
    mock_redis.keys.return_value = ["esign_replay:key1"]

    with (
        patch.dict(os.environ, {"REDIS_URL": "redis://mocked-host:6379"}),
        patch("redis.Redis.from_url", return_value=mock_redis),
    ):
        downstream_replay_cache.reset()
        mock_redis.keys.assert_called_once_with("esign_replay:*")
        mock_redis.delete.assert_called_once_with("esign_replay:key1")

    # Cleanup global state
    sig_token_verifier._redis_client = None
