"""Tests for Grounded Protocol RAG Router in AI Gateway microservice."""

from __future__ import annotations

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.ai_gateway.adapters.deid_adapter import DeidentifiedAIEngineAdapter
from apps.ai_gateway.adapters.mock_adapter import MockAIEngineAdapter
from apps.ai_gateway.main import app
from apps.ai_gateway.presentation.routers.rag import get_ai_engine
from packages.compliance.services.protocol_rag import ProtocolRAGContextChunk
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
async def test_rag_triage_empty_chunks_fails_closed():
    """Verify that RAG triage with empty context chunks fails closed to human review.

    @req:PRD-TCK-005
    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_data_manager",
        roles=["Data Manager"],
        change_reason="RAG triage empty chunks test",
    )
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "query": "What is the primary endpoint of the study?",
            "study_id": "STUDY-001",
            "context_chunks": [],
        }
        response = await client.post(
            "/api/v1/ai/rag/protocol-triage",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 200
        data = response.json()
        assert data["faithfulness_score"] == 0.0
        assert data["is_grounded"] is False
        assert data["confidence_tier"] == "LOW_FAIL_CLOSED"
        assert len(data["citations"]) == 0


@pytest.mark.asyncio
async def test_rag_triage_grounded_excerpts():
    """Verify that RAG triage with valid protocol chunks generates grounded citations.

    @req:PRD-TCK-005
    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_cra_user",
        roles=["CRA Monitor"],
        change_reason="RAG triage grounded test",
    )
    chunk = ProtocolRAGContextChunk(
        chunk_id="chunk-101",
        protocol_version="v2.1",
        section_number="7.3",
        section_title="SAE Reporting",
        page_number=42,
        chunk_text="Serious adverse events must be reported within 24 hours of site awareness to Pharmacovigilance.",
        is_approved=True,
    )

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "query": "When must serious adverse events be reported?",
            "study_id": "STUDY-001",
            "protocol_version": "v2.1",
            "context_chunks": [chunk.model_dump()],
            "tenant_id": "tenant-cadence",
        }
        response = await client.post(
            "/api/v1/ai/rag/protocol-triage",
            json=payload,
            headers=headers,
        )
        assert response.status_code == 200
        data = response.json()
        assert data["faithfulness_score"] >= 0.85
        assert data["is_grounded"] is True
        assert data["confidence_tier"] == "HIGH"
        assert len(data["citations"]) >= 1
        assert data["citations"][0]["page_number"] == 42
        assert data["citations"][0]["protocol_version"] == "v2.1"
        assert data["citations"][0]["is_verified"] is True


@pytest.mark.asyncio
async def test_rag_triage_unauthenticated_rejected():
    """Verify that unauthenticated requests to the RAG endpoint are rejected.

    @req:PRD-SYS-051
    """
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "query": "What is the washout period?",
            "study_id": "STUDY-001",
            "context_chunks": [],
        }
        response = await client.post(
            "/api/v1/ai/rag/protocol-triage",
            json=payload,
        )
        assert response.status_code == 403
