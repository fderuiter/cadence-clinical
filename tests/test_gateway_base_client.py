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
        mock_response.status_code = 400
        mock_response.text = "Bad Request"
        mock_post.return_value = mock_response

        response = await client.request(
            method="POST",
            path="/error-endpoint",
            user_id="test-user",
            roles="admin",
            change_reason="Error log test",
        )

        assert response.status_code == 400
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


@pytest.mark.asyncio
async def test_gateway_base_client_retries_on_5xx():
    """Verify that failed requests due to 5xx responses execute up to three retries with tenacity."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_response = AsyncMock()
        mock_response.status_code = 502
        mock_response.text = "Bad Gateway"
        mock_get.return_value = mock_response

        # We mock asyncio.sleep to bypass the real backoff delays and count retry sleeps
        with patch("asyncio.sleep", new_callable=AsyncMock) as mock_sleep:
            response = await client.request(
                method="GET",
                path="/test-5xx",
                user_id="test-user",
                roles="admin",
                change_reason="5xx retry test",
            )

            # Up to 3 retries, so 4 total attempts
            assert mock_get.call_count == 4
            assert response.status_code == 502
            assert mock_sleep.call_count == 3


@pytest.mark.asyncio
async def test_gateway_base_client_retries_on_timeout():
    """Verify that failed requests due to connection timeouts execute up to three retries with tenacity."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    # Side effect raises ConnectTimeout on every call
    with patch("httpx.AsyncClient.get", side_effect=httpx.ConnectTimeout("Connect timed out")):
        with patch("asyncio.sleep", new_callable=AsyncMock) as mock_sleep:
            with pytest.raises(httpx.ConnectTimeout):
                await client.request(
                    method="GET",
                    path="/test-timeout",
                    user_id="test-user",
                    roles="admin",
                    change_reason="Timeout retry test",
                )
            # 1 initial attempt + 3 retries = 4 total attempts, so mock_sleep called 3 times
            assert mock_sleep.call_count == 3


@pytest.mark.asyncio
async def test_gateway_base_client_signature_regenerated_on_retry():
    """Verify that retried requests regenerate signature headers dynamically with a new timestamp on each retry."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    timestamps = []
    signatures = []

    # Intercept build_headers to record the generated timestamp and signature
    original_build_headers = client.build_headers
    def mock_build_headers(*args, **kwargs):
        headers = original_build_headers(*args, **kwargs)
        timestamps.append(headers["X-Gateway-Timestamp"])
        signatures.append(headers["X-Gateway-Signature"])
        return headers

    client.build_headers = mock_build_headers

    class IncrementingTime:
        def __init__(self):
            self.val = 1000.0
        def __call__(self):
            self.val += 1.0
            return self.val

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_response = AsyncMock()
        mock_response.status_code = 500
        mock_response.text = "Internal Server Error"
        mock_get.return_value = mock_response

        with (
            patch("asyncio.sleep", new_callable=AsyncMock),
            patch("time.time", side_effect=IncrementingTime())
        ):
            await client.request(
                method="GET",
                path="/test-regen",
                user_id="test-user",
                roles="admin",
                change_reason="Signature regeneration test",
            )

            # 4 attempts total
            assert len(timestamps) == 4
            # Every timestamp should be unique due to dynamic time mock
            assert len(set(timestamps)) == 4
            # Every signature should be unique as it incorporates the timestamp
            assert len(set(signatures)) == 4


def test_gateway_base_client_request_sync_success():
    """Verify that request_sync supports safe synchronous execution of asynchronous communication methods."""
    client = GatewayBaseClient(base_url="http://localhost:1234")

    with patch("httpx.AsyncClient.get", new_callable=AsyncMock) as mock_get:
        mock_response = AsyncMock()
        mock_response.status_code = 200
        mock_response.json.return_value = {"status": "ok"}
        mock_get.return_value = mock_response

        response = client.request_sync(
            method="GET",
            path="/sync-test-endpoint",
            user_id="test-user",
            roles="admin",
            change_reason="Sync request test",
        )

        assert response.status_code == 200
        mock_get.assert_called_once()
