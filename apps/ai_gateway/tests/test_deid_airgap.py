"""In-flight de-identification air-gap tests for AI Gateway microservice.

Requirements: PRD-SYS-051
"""

import json

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
    """Setup mock AI engine wrapped in deid air-gap adapter for deterministic testing."""
    mock_engine = MockAIEngineAdapter()
    wrapped_engine = DeidentifiedAIEngineAdapter(mock_engine)
    app.dependency_overrides[get_ai_engine] = lambda: wrapped_engine
    yield mock_engine
    app.dependency_overrides.pop(get_ai_engine, None)


@pytest.mark.asyncio
async def test_inflight_prompt_deidentification_and_rehydration(setup_ai_gateway):
    """Verify that patient identifiers in prompts are replaced with surrogate tokens,

    never reach the underlying engine, and are restored in memory upon response return.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway
    mock_engine.set_mock_response(
        prompt_substring="[SURROGATE_",
        response="Patient [SURROGATE_CUSTOM_1] (SSN: [SURROGATE_SSN_NATIONAL_ID_1]) protocol review complete.",
    )

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_crc",
        roles=["site_crc"],
        change_reason="Clinical narrative summarization",
    )

    raw_prompt = "Patient Alice Johnson with SSN 123-45-6789 presented with hypertension on 2026-04-10."
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "prompt": raw_prompt,
            "tier": "tier_2_fast",
            "custom_terms": ["Alice Johnson"],
            "enable_deid": True,
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()

        # Verify underlying engine received 0 raw PHI
        assert len(mock_engine.generation_calls) == 1
        engine_call_prompt = mock_engine.generation_calls[0].prompt
        assert "Alice Johnson" not in engine_call_prompt
        assert "123-45-6789" not in engine_call_prompt
        assert "[SURROGATE_CUSTOM_" in engine_call_prompt
        assert "[SURROGATE_SSN_NATIONAL_ID_" in engine_call_prompt

        # Verify caller received fully re-hydrated completion
        assert "Alice Johnson" in data["content"]
        assert "123-45-6789" in data["content"]
        assert "[SURROGATE_" not in data["content"]
        assert data["deid_applied"] is True
        assert data["deid_tokens_count"] >= 2


@pytest.mark.asyncio
async def test_multiturn_messages_co_reference_airgap(setup_ai_gateway):
    """Verify multi-turn chat messages share consistent surrogate tokens across turns.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_dm",
        roles=["Data Manager"],
        change_reason="Multi-turn chart audit",
    )

    messages = [
        {
            "role": "user",
            "content": "Patient Bob Roberts (MRN: 98765432) underwent baseline ECG.",
        },
        {
            "role": "assistant",
            "content": "Baseline ECG for Bob Roberts recorded.",
        },
        {
            "role": "user",
            "content": "Check adverse events for Bob Roberts with MRN: 98765432.",
        },
    ]

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "messages": messages,
            "tier": "tier_2_fast",
            "custom_terms": ["Bob Roberts"],
            "enable_deid": True,
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["deid_applied"] is True

        # Verify engine received sanitized messages
        assert len(mock_engine.generation_calls) == 1
        call_messages = mock_engine.generation_calls[0].messages
        assert len(call_messages) == 3
        for msg in call_messages:
            assert "Bob Roberts" not in msg.content
            assert "98765432" not in msg.content


@pytest.mark.asyncio
async def test_structured_output_rehydration(setup_ai_gateway):
    """Verify structured output JSON schema response is re-hydrated in memory.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_dm",
        roles=["Data Manager"],
        change_reason="Structured coding extraction",
    )

    schema = {
        "type": "object",
        "properties": {
            "subject_identifier": {"type": "string"},
            "reported_event": {"type": "string"},
            "site_id": {"type": "string"},
        },
        "required": ["subject_identifier", "reported_event"],
    }

    # Canned JSON response containing surrogate tokens
    mock_engine.set_mock_response(
        prompt_substring="[SURROGATE_",
        response=json.dumps(
            {
                "subject_identifier": "[SURROGATE_CUSTOM_1]",
                "reported_event": "Subject [SURROGATE_CUSTOM_1] reported headache",
                "site_id": "SITE-101",
            }
        ),
    )

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "prompt": "Extract adverse event for patient Charlie Brown.",
            "tier": "tier_2_fast",
            "custom_terms": ["Charlie Brown"],
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
        assert data["structured_data"]["subject_identifier"] == "Charlie Brown"
        assert (
            data["structured_data"]["reported_event"]
            == "Subject Charlie Brown reported headache"
        )
        assert data["deid_applied"] is True


@pytest.mark.asyncio
async def test_embedding_batch_sanitization(setup_ai_gateway):
    """Verify embedding inputs are sanitized before being passed to vector model.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_dm",
        roles=["Data Manager"],
        change_reason="Vector embedding indexing",
    )

    input_texts = [
        "Patient Dave Miller (SSN: 444-55-6666) initial triage note.",
        "Followup report for Dave Miller on 2026-03-15.",
    ]

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "input_texts": input_texts,
            "tier": "tier_1_local",
            "custom_terms": ["Dave Miller"],
            "enable_deid": True,
        }
        response = await client.post(
            "/api/v1/ai/embed",
            json=payload,
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["deid_applied"] is True
        assert data["deid_tokens_count"] >= 2
        assert len(data["embeddings"]) == 2

        # Verify underlying engine received only sanitized texts
        assert len(mock_engine.embedding_calls) == 1
        embedded_texts = mock_engine.embedding_calls[0].input_texts
        for t in embedded_texts:
            assert "Dave Miller" not in t
            assert "444-55-6666" not in t


@pytest.mark.asyncio
async def test_enable_deid_false_bypasses_scrubbing(setup_ai_gateway):
    """Verify that explicitly setting enable_deid=False bypasses de-identification.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_admin",
        roles=["super_admin"],
        change_reason="Pre-anonymized synthetic data test",
    )

    raw_prompt = "Synthetic test case: Patient 123-45-6789."
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        payload = {
            "prompt": raw_prompt,
            "tier": "tier_2_fast",
            "enable_deid": False,
        }
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

        assert response.status_code == 200
        data = response.json()
        assert data["deid_applied"] is False

        assert len(mock_engine.generation_calls) == 1
        assert mock_engine.generation_calls[0].prompt == raw_prompt
