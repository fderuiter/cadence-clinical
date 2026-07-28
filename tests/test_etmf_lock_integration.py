import os
from unittest.mock import patch

import pytest
from fastapi import HTTPException

from apps.etmf.lock_client import (
    trigger_global_trial_lock,
    verify_trial_lock_status,
)
from packages.security.signing import (
    verify_gateway_signature,
)


@pytest.mark.asyncio
async def test_verify_trial_lock_status_unlocked():
    """
    Verify that verify_trial_lock_status returns False when the execution service says trial_locked is False.
    """
    import httpx

    mock_response = httpx.Response(200, json={"trial_locked": False})
    with patch("httpx.AsyncClient.get", return_value=mock_response) as mock_get:
        res = await verify_trial_lock_status(is_testing=False)
        assert res is False
        mock_get.assert_called_once()

        # Check headers passed to mock_get
        args, kwargs = mock_get.call_args
        headers = kwargs.get("headers", {})
        assert headers["X-User-Id"] == "etmf-service"
        assert headers["X-User-Roles"] == "Data Manager"
        assert "X-Gateway-Timestamp" in headers
        assert "X-Gateway-Signature" in headers
        assert headers["X-Signature-Version"] == "2"


@pytest.mark.asyncio
async def test_verify_trial_lock_status_locked():
    """
    Verify that verify_trial_lock_status returns True when the execution service says trial_locked is True.
    """
    import httpx

    mock_response = httpx.Response(200, json={"trial_locked": True})
    with patch("httpx.AsyncClient.get", return_value=mock_response):
        res = await verify_trial_lock_status(is_testing=False)
        assert res is True


@pytest.mark.asyncio
async def test_verify_trial_lock_status_error():
    """
    Verify that verify_trial_lock_status raises an HTTPException (fails closed) if the execution service returns an error.
    """
    import httpx

    mock_response = httpx.Response(500)
    with patch("httpx.AsyncClient.get", return_value=mock_response):
        with pytest.raises(HTTPException) as exc_info:
            await verify_trial_lock_status(is_testing=False)
        assert exc_info.value.status_code == 502


@pytest.mark.asyncio
async def test_trigger_global_trial_lock():
    """
    Verify that trigger_global_trial_lock signs and posts the lock request to the execution service.
    """
    import httpx

    mock_response = httpx.Response(200, json={"status": "success"})
    with patch("httpx.AsyncClient.post", return_value=mock_response) as mock_post:
        reason = "Cryptographic ledger integrity breach detected in eTMF"
        await trigger_global_trial_lock(reason=reason, is_testing=False)
        mock_post.assert_called_once()

        # Check headers passed to mock_post
        args, kwargs = mock_post.call_args
        headers = kwargs.get("headers", {})
        assert headers["X-User-Id"] == "etmf-service"
        assert headers["X-User-Roles"] == "Data Manager"
        assert headers["X-Change-Reason"] == reason
        assert "X-Gateway-Timestamp" in headers
        assert "X-Gateway-Signature" in headers
        assert headers["X-Signature-Version"] == "2"

        # Cryptographically verify the signature
        secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()
        verified = verify_gateway_signature(
            user_id=headers["X-User-Id"],
            roles=headers["X-User-Roles"],
            timestamp=headers["X-Gateway-Timestamp"],
            signature=headers["X-Gateway-Signature"],
            secret=secret,
            change_reason=reason,
        )
        assert verified is True
