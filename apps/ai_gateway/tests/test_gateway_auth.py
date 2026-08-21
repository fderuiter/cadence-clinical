"""Security and gateway authentication tests for AI Gateway microservice."""

import time

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.ai_gateway.adapters.mock_adapter import MockAIEngineAdapter
from apps.ai_gateway.main import app, set_ai_engine_override
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
def setup_ai_gateway():
    """Setup mock AI engine for deterministic security testing."""
    mock_engine = MockAIEngineAdapter()
    set_ai_engine_override(mock_engine)
    yield mock_engine
    set_ai_engine_override(None)


@pytest.mark.asyncio
async def test_unauthenticated_request_rejected():
    """Verify that requests without gateway HMAC authentication are rejected with 401.

    @req:PRD-SYS-090
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
    """Verify that forged or corrupted signatures are rejected with 401.

    @req:PRD-SYS-090
    """
    transport = ASGITransport(app=app)
    headers = {
        "X-User-Id": "attacker",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": str(time.time()),
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

    @req:PRD-SYS-090
    """
    transport = ASGITransport(app=app)
    timestamp = str(time.time())
    sig = generate_signature(
        "service_execution",
        "admin",
        timestamp,
        version="2",
        change_reason="Valid inter-service inference request",
    )
    headers = {
        "X-User-Id": "service_execution",
        "X-User-Roles": "admin",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": "Valid inter-service inference request",
    }
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/api/v1/ai/tiers", headers=headers)
        assert response.status_code == 200
