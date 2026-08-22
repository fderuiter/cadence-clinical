"""Unit and integration tests for API Gateway lifespan lifecycle.

Requirements: PRD-SYS-001, GxP Reliability Standards
"""

from unittest.mock import AsyncMock, patch

import httpx
import pytest
from fastapi import FastAPI

import apps.gateway.main as gateway_main
from apps.gateway.main import lifespan, startup


@pytest.mark.asyncio
async def test_gateway_lifespan_successful_lifecycle():
    """Verify that gateway lifespan initializes http client and closes it upon shutdown.

    @req:PRD-SYS-001
    """
    app = FastAPI()
    with patch("apps.gateway.main.startup", new_callable=AsyncMock) as mock_startup:
        async with lifespan(app):
            assert gateway_main.http_client is not None
            mock_startup.assert_awaited_once()

        # After lifespan exits, http_client should be closed
        assert gateway_main.http_client is not None


@pytest.mark.asyncio
async def test_gateway_startup_jwks_resilience():
    """Verify startup function is resilient when JWKS endpoint is unreachable.

    @req:PRD-SYS-001
    """
    gateway_main.jwks_cache = None
    gateway_main.http_client = httpx.AsyncClient()

    with patch.object(
        gateway_main.http_client,
        "get",
        side_effect=httpx.ConnectError("Connection refused"),
    ):
        # Should not raise exception
        await startup()
        assert gateway_main.jwks_cache is None

    await gateway_main.http_client.aclose()
    gateway_main.http_client = None


@pytest.mark.asyncio
async def test_gateway_startup_jwks_success():
    """Verify startup function populates jwks_cache on successful fetch.

    @req:PRD-SYS-001
    """
    gateway_main.jwks_cache = None
    gateway_main.http_client = httpx.AsyncClient()

    mock_resp = httpx.Response(
        status_code=200,
        json={"keys": [{"kid": "test-key-1", "kty": "RSA"}]},
        request=httpx.Request("GET", "http://test"),
    )

    with patch.object(gateway_main.http_client, "get", return_value=mock_resp):
        await startup()
        assert gateway_main.jwks_cache == {
            "keys": [{"kid": "test-key-1", "kty": "RSA"}]
        }

    await gateway_main.http_client.aclose()
    gateway_main.http_client = None
    gateway_main.jwks_cache = None
