"""Security and gateway authentication tests for AI Gateway microservice."""

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.ai_gateway.adapters.mock_adapter import MockAIEngineAdapter
from apps.ai_gateway.main import app
from apps.ai_gateway.presentation.routers.inference import get_ai_engine
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
def setup_ai_gateway():
    """Setup mock AI engine for deterministic security testing via dependency overrides."""
    mock_engine = MockAIEngineAdapter()
    app.dependency_overrides[get_ai_engine] = lambda: mock_engine
    yield mock_engine
    app.dependency_overrides.pop(get_ai_engine, None)


@pytest.mark.asyncio
async def test_unauthenticated_request_rejected():
    """Verify that requests without gateway HMAC authentication are rejected with 403.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/ai/generate",
            json={"prompt": "Hello", "tier": "tier_2_fast"},
        )
        assert response.status_code == 403


@pytest.mark.asyncio
async def test_invalid_signature_rejected():
    """Verify that forged or corrupted signatures are rejected with 403.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = {
        "X-User-Id": "attacker",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": "1724000000.0",
        "X-Gateway-Signature": "forged_signature_hex_value",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Unauthorized access attempt",
    }
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/ai/generate",
            json={"prompt": "Hello", "tier": "tier_2_fast"},
            headers=headers,
        )
        assert response.status_code == 403


@pytest.mark.asyncio
async def test_valid_signature_accepted():
    """Verify that authentic gateway HMAC signatures pass middleware validation.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="service_execution",
        roles=["admin"],
        change_reason="Valid inter-service inference request",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/api/v1/ai/tiers", headers=headers)
        assert response.status_code == 200
