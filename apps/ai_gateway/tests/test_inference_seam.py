"""Inference seam tests for AI Gateway microservice."""

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.ai_gateway.adapters.deid_adapter import DeidentifiedAIEngineAdapter
from apps.ai_gateway.adapters.mock_adapter import MockAIEngineAdapter
from apps.ai_gateway.main import app
from apps.ai_gateway.presentation.routers.inference import get_ai_engine
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
def setup_ai_gateway():
    """Setup mock AI engine for deterministic testing via FastAPI dependency overrides."""
    mock_engine = MockAIEngineAdapter()
    wrapped = DeidentifiedAIEngineAdapter(mock_engine)
    app.dependency_overrides[get_ai_engine] = lambda: wrapped
    yield mock_engine
    app.dependency_overrides.pop(get_ai_engine, None)


@pytest.mark.asyncio
async def test_health_check():
    """Verify service health check endpoint responds with ok status.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/health")
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "ok"
        assert data["service"] == "ai-gateway"


@pytest.mark.asyncio
async def test_text_generation_seam():
    """Verify single-turn text generation executes through public REST seam.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="AI inference test execution",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "prompt": "Summarize protocol inclusion criteria.",
            "tier": "tier_2_fast",
            "temperature": 0.0,
            "max_tokens": 100,
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 200
        data = response.json()
        assert "content" in data
        assert data["tier"] == "tier_2_fast"
        assert data["usage"]["total_tokens"] > 0
        assert data["latency_ms"] >= 0.0


@pytest.mark.asyncio
async def test_structured_output_generation_seam():
    """Verify structured output extraction enforces JSON schema.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="Structured output test execution",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        schema = {
            "type": "object",
            "properties": {
                "meddra_term": {"type": "string"},
                "confidence": {"type": "number"},
                "is_approved": {"type": "boolean"},
            },
            "required": ["meddra_term", "confidence"],
        }
        payload = {
            "prompt": "Code verbatim: Patient reported severe nausea.",
            "tier": "tier_1_local",
            "response_schema": schema,
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 200
        data = response.json()
        assert data["structured_data"] is not None
        assert "meddra_term" in data["structured_data"]
        assert "confidence" in data["structured_data"]


@pytest.mark.asyncio
async def test_embeddings_generation_seam():
    """Verify dense vector embeddings generation for text batches.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="Embeddings test execution",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "input_texts": [
                "Adverse event: acute headache",
                "Concomitant medication: Acetaminophen 500mg",
            ],
            "tier": "tier_1_local",
        }
        response = await client.post(
            "/api/v1/ai/embed",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 200
        data = response.json()
        assert len(data["embeddings"]) == 2
        assert len(data["embeddings"][0]) == 384
        assert data["tier"] == "tier_1_local"


@pytest.mark.asyncio
async def test_tier_listing_seam():
    """Verify tier configuration endpoint lists all active execution tiers.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="List tiers test execution",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get("/api/v1/ai/tiers", headers=headers)
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 3
        tiers = [item["tier"] for item in data]
        assert "tier_1_local" in tiers
        assert "tier_2_fast" in tiers
        assert "tier_3_frontier" in tiers


@pytest.mark.asyncio
async def test_invalid_generation_payload():
    """Verify 422 error when neither prompt nor messages is supplied.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="Invalid payload test",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "tier": "tier_2_fast",
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 422
