"""Typed tool calling, adapter pass-through, and recursive JSON payload de-identification tests.

Requirements: PRD-SYS-051
"""

from unittest.mock import AsyncMock, patch

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient

from apps.ai_gateway.adapters.deid_adapter import DeidentifiedAIEngineAdapter
from apps.ai_gateway.adapters.litellm_adapter import LiteLLMAdapter
from apps.ai_gateway.adapters.mock_adapter import MockAIEngineAdapter
from apps.ai_gateway.domain.models import (
    FunctionDefinition,
    GenerationRequest,
    ModelTier,
    ToolCall,
    ToolCallFunction,
    ToolDefinition,
    ToolResponse,
)
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
async def test_generation_request_dto_accepts_tools_and_tool_responses(
    setup_ai_gateway,
):
    """Verify that generation endpoint DTOs accept typed tool definitions and tool execution responses.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_doctor",
        roles=["principal_investigator"],
        change_reason="Clinical tool invocation",
    )

    payload = {
        "prompt": "Evaluate patient vitals for protocol eligibility.",
        "tier": "tier_2_fast",
        "tools": [
            {
                "type": "function",
                "function": {
                    "name": "fetch_patient_vitals",
                    "description": "Fetch vital signs for a clinical participant.",
                    "parameters": {
                        "type": "object",
                        "properties": {
                            "subject_id": {"type": "string"},
                            "limit": {"type": "integer"},
                        },
                        "required": ["subject_id"],
                    },
                },
            }
        ],
        "tool_choice": "auto",
        "tool_responses": [
            {
                "tool_call_id": "call_vitals_99",
                "content": {"bp_systolic": 120, "bp_diastolic": 80},
                "name": "fetch_patient_vitals",
            }
        ],
        "enable_deid": False,
    }

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

    assert response.status_code == 200
    data = response.json()
    assert "tool_calls" in data
    assert data["tool_calls"] is not None
    assert len(data["tool_calls"]) == 1
    assert data["tool_calls"][0]["function"]["name"] == "fetch_patient_vitals"
    assert (
        data["tool_calls"][0]["function"]["arguments"]["subject_id"]
        == "mock_subject_id"
    )

    # Verify mock engine recorded tools and tool_responses in domain request
    assert len(mock_engine.generation_calls) == 1
    rec_req = mock_engine.generation_calls[0]
    assert rec_req.tools is not None
    assert len(rec_req.tools) == 1
    assert rec_req.tools[0].function.name == "fetch_patient_vitals"
    assert rec_req.tool_responses is not None
    assert len(rec_req.tool_responses) == 1
    assert rec_req.tool_responses[0].tool_call_id == "call_vitals_99"


@pytest.mark.asyncio
async def test_mock_adapter_custom_tool_call_payload(setup_ai_gateway):
    """Verify mock test adapter returns configured structured tool call payloads during automated test execution.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway
    custom_tc = [
        ToolCall(
            id="call_custom_101",
            type="function",
            function=ToolCallFunction(
                name="order_lab_test",
                arguments={"lab_code": "CBC_DIFF", "urgency": "STAT"},
            ),
        )
    ]
    mock_engine.set_mock_tool_calls(
        prompt_substring="Order lab test for participant",
        tool_calls=custom_tc,
    )

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_crc",
        roles=["site_crc"],
        change_reason="Order lab test",
    )

    payload = {
        "prompt": "Order lab test for participant SUBJ-404.",
        "tier": "tier_2_fast",
        "enable_deid": False,
    }

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

    assert response.status_code == 200
    data = response.json()
    assert data["tool_calls"] is not None
    assert len(data["tool_calls"]) == 1
    assert data["tool_calls"][0]["id"] == "call_custom_101"
    assert data["tool_calls"][0]["function"]["name"] == "order_lab_test"
    assert data["tool_calls"][0]["function"]["arguments"] == {
        "lab_code": "CBC_DIFF",
        "urgency": "STAT",
    }


@pytest.mark.asyncio
async def test_litellm_adapter_tool_calling_pass_through():
    """Verify LiteLLM adapter passes tool definitions, arguments, and identifiers without dropping properties.

    @req:PRD-SYS-051
    """
    adapter = LiteLLMAdapter()

    tool_def = ToolDefinition(
        type="function",
        function=FunctionDefinition(
            name="query_ecg_trace",
            description="Fetch raw ECG trace.",
            parameters={"type": "object", "properties": {"lead": {"type": "string"}}},
        ),
    )

    domain_req = GenerationRequest(
        prompt="Fetch lead II trace.",
        tier=ModelTier.TIER_2_FAST,
        tools=[tool_def],
        tool_choice={"type": "function", "function": {"name": "query_ecg_trace"}},
        tool_responses=[
            ToolResponse(
                tool_call_id="call_ecg_1",
                content={"lead_ii": [0.1, 0.4, 0.2]},
                name="query_ecg_trace",
            )
        ],
    )

    mock_choice = AsyncMock()
    mock_choice.message.content = "Analyzed trace."
    mock_choice.message.tool_calls = [
        {
            "id": "call_ecg_resp_1",
            "type": "function",
            "function": {
                "name": "query_ecg_trace",
                "arguments": '{"lead": "V1"}',
            },
        }
    ]

    mock_completion_response = AsyncMock()
    mock_completion_response.choices = [mock_choice]
    mock_completion_response.usage = AsyncMock(
        prompt_tokens=15, completion_tokens=10, total_tokens=25
    )

    with patch(
        "litellm.acompletion", return_value=mock_completion_response
    ) as mock_acompletion:
        res = await adapter.generate(domain_req)

    assert mock_acompletion.called
    kwargs = mock_acompletion.call_args.kwargs
    assert "tools" in kwargs
    assert len(kwargs["tools"]) == 1
    assert kwargs["tools"][0]["function"]["name"] == "query_ecg_trace"
    assert kwargs["tool_choice"] == {
        "type": "function",
        "function": {"name": "query_ecg_trace"},
    }
    assert any(
        m.get("role") == "tool" and m.get("tool_call_id") == "call_ecg_1"
        for m in kwargs["messages"]
    )

    assert res.tool_calls is not None
    assert len(res.tool_calls) == 1
    assert res.tool_calls[0].id == "call_ecg_resp_1"
    assert res.tool_calls[0].function.name == "query_ecg_trace"
    assert res.tool_calls[0].function.arguments == {"lead": "V1"}


@pytest.mark.asyncio
async def test_recursive_json_tool_payload_deidentification_and_rehydration(
    setup_ai_gateway,
):
    """Verify recursive de-identification redacts PHI in nested JSON tool call arguments and re-hydrates responses.

    @req:PRD-SYS-051
    """
    mock_engine: MockAIEngineAdapter = setup_ai_gateway

    # Setup mock engine to return tool call containing surrogate tokens in JSON args
    mock_engine.set_mock_tool_calls(
        prompt_substring="Evaluate subject record",
        tool_calls=[
            ToolCall(
                id="call_deid_1",
                type="function",
                function=ToolCallFunction(
                    name="update_subject_demographics",
                    arguments={
                        "full_name": "[SURROGATE_CUSTOM_1]",
                        "ssn": "[SURROGATE_SSN_NATIONAL_ID_1]",
                        "status": "ACTIVE",
                    },
                ),
            )
        ],
    )

    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="test_doctor",
        roles=["principal_investigator"],
        change_reason="Demographic update tool",
    )

    sensitive_name = "Jane Doe"
    sensitive_ssn = "987-65-4321"

    payload = {
        "messages": [
            {
                "role": "user",
                "content": f"Evaluate subject record for {sensitive_name} SSN {sensitive_ssn}.",
            },
            {
                "role": "assistant",
                "content": None,
                "tool_calls": [
                    {
                        "id": "call_deid_1",
                        "type": "function",
                        "function": {
                            "name": "update_subject_demographics",
                            "arguments": {
                                "full_name": sensitive_name,
                                "ssn": sensitive_ssn,
                            },
                        },
                    }
                ],
            },
        ],
        "tool_responses": [
            {
                "tool_call_id": "call_deid_1",
                "content": {
                    "confirmation": f"Updated record for {sensitive_name}",
                    "ssn_verified": sensitive_ssn,
                },
                "name": "update_subject_demographics",
            }
        ],
        "tier": "tier_2_fast",
        "custom_terms": [sensitive_name],
        "enable_deid": True,
    }

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.post(
            "/api/v1/ai/generate",
            json=payload,
            headers=headers,
        )

    assert response.status_code == 200
    data = response.json()

    # Verify that mock engine received de-identified surrogate tokens in recorded requests
    assert len(mock_engine.generation_calls) == 1
    received_req = mock_engine.generation_calls[0]
    rec_tool_call_args = received_req.messages[1].tool_calls[0].function.arguments
    assert sensitive_name not in str(rec_tool_call_args)
    assert sensitive_ssn not in str(rec_tool_call_args)
    assert "[SURROGATE_" in str(rec_tool_call_args)

    rec_tool_resp_content = received_req.tool_responses[0].content
    assert sensitive_name not in str(rec_tool_resp_content)
    assert sensitive_ssn not in str(rec_tool_resp_content)
    assert "[SURROGATE_" in str(rec_tool_resp_content)

    # Verify that returned API response has re-hydrated PHI back to raw values
    assert data["deid_applied"] is True
    assert data["deid_tokens_count"] > 0
    assert data["tool_calls"] is not None
    assert len(data["tool_calls"]) == 1
    returned_args = data["tool_calls"][0]["function"]["arguments"]
    assert returned_args["full_name"] == sensitive_name
    assert returned_args["ssn"] == sensitive_ssn
