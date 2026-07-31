import asyncio
from unittest.mock import AsyncMock, patch

import httpx
import pytest

from packages.security import GatewayBaseClient, run_async


def test_run_async_basic():
    """Verify that run_async synchronously returns the result of an async coroutine."""

    async def sample_coro():
        return "hello world"

    res = run_async(sample_coro())
    assert res == "hello world"


@pytest.mark.asyncio
async def test_run_async_in_running_loop():
    """Verify that run_async works when called from inside a running event loop (thread safety/delegation)."""

    async def sample_coro():
        await asyncio.sleep(0.01)
        return 42

    res = run_async(sample_coro())
    assert res == 42


def test_gateway_base_client_headers():
    """Verify that GatewayBaseClient constructs standard gateway headers correctly."""
    client = GatewayBaseClient(base_url="http://localhost:1234", timeout=3.0)

    headers = client.build_headers(
        user_id="test-user",
        roles="admin",
        change_reason="Testing headers",
    )

    assert headers["X-User-Id"] == "test-user"
    assert headers["X-User-Roles"] == "admin"
    assert headers["X-Signature-Version"] == "2"
    assert headers["X-Change-Reason"] == "Testing headers"
    assert "X-Gateway-Timestamp" in headers
    assert "X-Gateway-Signature" in headers


@pytest.mark.asyncio
async def test_gateway_base_client_request_success():
    """Verify that GatewayBaseClient sends a successful request and returns 200."""
    client = GatewayBaseClient(base_url="http://localhost:1234", timeout=3.0)

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_response = AsyncMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"status": "ok"}
        mock_get.return_value = mock_response

        response = await client.request(
            method="GET",
            path="/test-endpoint",
            user_id="test-user",
            roles="admin",
            change_reason="Success test",
        )

        assert response.status_code == 200
        mock_get.assert_called_once()


@pytest.mark.asyncio
async def test_gateway_base_client_request_failure_logging():
    """Verify that GatewayBaseClient logs descriptive errors on non-2xx status codes."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    with (
        patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post,
        patch("packages.security.gateway_client.logger.error") as mock_logger,
    ):
        mock_response = AsyncMock()
        mock_response.status_code = 500
        mock_response.text = "Internal Server Error"
        mock_post.return_value = mock_response

        response = await client.request(
            method="POST",
            path="/error-endpoint",
            user_id="test-user",
            roles="admin",
            change_reason="Error log test",
        )

        assert response.status_code == 500
        mock_logger.assert_called()
        log_arg = mock_logger.call_args[0][0]
        assert "Failed request" in log_arg


@pytest.mark.asyncio
async def test_gateway_base_client_request_exception_logging():
    """Verify that GatewayBaseClient logs descriptive errors when exceptions are thrown."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    with (
        patch("httpx.AsyncClient.get", side_effect=httpx.ConnectTimeout("Timed out")),
        patch("packages.security.gateway_client.logger.error") as mock_logger,
    ):
        with pytest.raises(httpx.ConnectTimeout):
            await client.request(
                method="GET",
                path="/timeout-endpoint",
                user_id="test-user",
                roles="admin",
                change_reason="Timeout test",
            )

        mock_logger.assert_called()
        log_arg = mock_logger.call_args[0][0]
        assert "Exception occurred" in log_arg
