"""
Comprehensive unit and integration tests for the Agent Facade Microservice.

Verifies:
- Health check accessibility.
- Mandatory Gateway authentication checks (rejection of unsigned requests).
- Proper downstream proxy routing with cryptographically signed signature headers.
- Protection against spoofed delegation and scope headers at the gateway/facade boundary.
"""

import hashlib
import hmac
import json
import time
from typing import Dict
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
from fastapi.testclient import TestClient
from jose import jwt

from apps.agent_facade.main import app as facade_app
from apps.gateway.main import app as gateway_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_signature_headers(
    user_id: str = "agent_developer",
    roles: str = "agent",
    change_reason: str = "testing agent facade operations",
    site_id: str = None,
    sponsor_id: str = None,
    unblinded_access: bool = False,
) -> Dict[str, str]:
    """Helper to generate valid Gateway version 2 signature headers."""
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
        "site_id": site_id if site_id is not None else "",
        "sponsor_id": sponsor_id if sponsor_id is not None else "",
        "unblinded_access": unblinded_access,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if site_id:
        headers["X-Site-Id"] = site_id
    if sponsor_id:
        headers["X-Sponsor-Id"] = sponsor_id
    if unblinded_access:
        headers["X-Unblinded-Access"] = "true"

    return headers


def test_facade_health_check() -> None:
    """Verify health check is open and functional."""
    with TestClient(facade_app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "ok", "service": "agent-facade"}


def test_facade_rejects_unauthenticated_request() -> None:
    """
    Verify that requests without valid gateway signature headers are rejected.
    """
    with TestClient(facade_app) as client:
        # Non-mutation GET request
        response_get = client.get("/api/v1/agent-facade/queries")
        assert response_get.status_code == 401

        # Mutation POST request
        response_post = client.post(
            "/api/v1/agent-facade/unit-conversion",
            json={"value": 10.0, "from_unit": "m", "to_unit": "cm"},
        )
        assert response_post.status_code == 403


@pytest.mark.asyncio
async def test_facade_unit_conversion_delegation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Verify that the facade forwards the unit conversion request downstream
    attaching a valid, freshly signed gateway cryptographic signature.
    """
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.json.return_value = {
        "value": 5.0,
        "from_unit": "g",
        "to_unit": "kg",
        "converted_value": 0.005,
    }
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "post", mock_send)

    headers = get_v2_signature_headers(user_id="agent_alice", roles="agent")

    with TestClient(facade_app) as client:
        response = client.post(
            "/api/v1/agent-facade/unit-conversion",
            json={"value": 5.0, "from_unit": "g", "to_unit": "kg"},
            headers=headers,
        )
        assert response.status_code == 200
        assert response.json()["converted_value"] == 0.005

        # Check that the facade indeed routed/forwarded the request to the execution service
        # and attached the required signature headers
        assert mock_send.called
        call_args, call_kwargs = mock_send.call_args
        target_url = call_args[0]
        assert "execution" in target_url

        forwarded_headers = call_kwargs.get("headers", {})
        assert forwarded_headers.get("X-User-Id") == "agent_alice"
        assert forwarded_headers.get("X-User-Roles") == "agent"
        assert "X-Gateway-Signature" in forwarded_headers


@pytest.mark.asyncio
async def test_facade_queries_delegation(monkeypatch: pytest.MonkeyPatch) -> None:
    """Verify query creation and retrieval routing through the facade."""
    mock_get = AsyncMock()
    mock_get_resp = MagicMock()
    mock_get_resp.status_code = 200
    mock_get_resp.json.return_value = [{"id": "q1", "query_text": "Is this correct?"}]
    mock_get.return_value = mock_get_resp
    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    headers = get_v2_signature_headers(user_id="agent_bob", roles="agent")

    with TestClient(facade_app) as client:
        response = client.get("/api/v1/agent-facade/queries", headers=headers)
        assert response.status_code == 200
        assert len(response.json()) == 1
        assert response.json()[0]["id"] == "q1"


def test_gateway_spoofed_delegation_rejection_integration(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Verify that if a client attempts to pass spoofed delegation headers
    (like X-Delegator-Site-Id or X-Target-Site-Id) through the Gateway,
    they are successfully stripped/removed.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "forwarded"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    token = jwt.encode(
        {
            "sub": "investigator_user",
            "roles": ["investigator"],
            "site_id": "site_trust_999",  # Trusted claim in the JWT
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(gateway_app) as client:
        response = client.get(
            "/designer/test",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Delegator-Site-Id": "site_spoofed_hacker",  # Spoofed delegator site
                "X-Target-Site-Id": "site_spoofed_hacker",  # Spoofed target site
            },
        )
        assert response.status_code == 200

        # Assert that the outgoing request forwarded downstream had the spoofed headers stripped!
        sent_request = mock_send.call_args.args[0]
        assert sent_request.headers.get("X-Delegator-Site-Id") is None
        assert sent_request.headers.get("X-Target-Site-Id") is None

        # Verify that only the trusted claim X-Site-Id is propagated
        assert sent_request.headers.get("X-Site-Id") == "site_trust_999"
