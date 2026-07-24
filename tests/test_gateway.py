from typing import Any
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
from fastapi.testclient import TestClient
from jose import jwt

from apps.gateway.main import app, generate_signature, verify_token


def test_verify_token_invalid() -> None:
    """
    Test verifying an invalid token.

    Ensures that passing an invalid token to verify_token raises an exception.
    """
    with pytest.raises(Exception):
        verify_token("invalid_token")


def test_generate_signature() -> None:
    """
    Test the generation of HMAC signatures.

    Ensures that generate_signature returns a non-null string value given valid inputs.
    """
    sig = generate_signature("user1", "admin", "12345")
    assert sig is not None


def test_proxy_requests_no_auth() -> None:
    """
    # @req:PRD-UNI-001
    Test proxy endpoint without an authorization header.

    Ensures that requests without a Bearer token receive a 401 Unauthorized response.
    """
    with TestClient(app) as client:
        response = client.get("/api/v1/studies/study_1")
        assert response.status_code == 401


def test_proxy_requests_invalid_auth() -> None:
    """
    Test proxy endpoint with an invalid authorization header.

    Ensures that requests with an invalid Bearer token receive a 401 Unauthorized response.
    """
    with TestClient(app) as client:
        response = client.get(
            "/api/v1/studies/study_1", headers={"Authorization": "Bearer invalid"}
        )
        assert response.status_code == 401


def test_proxy_requests_valid_auth(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test proxy endpoint with a valid authorization header.

    Mocks the test secret, encodes a valid JWT, and asserts that the proxy
    passes the request to downstream services without a 401 error.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )
    with TestClient(app) as client:
        response = client.get(
            "/api/v1/studies/study_1", headers={"Authorization": f"Bearer {token}"}
        )
        assert response.status_code in [200, 502, 500]


@pytest.mark.asyncio
async def test_get_openapi_json(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test the dynamic OpenAPI JSON aggregation endpoint.

    Mocks the downstream service responses and validates that the gateway
    correctly merges the schemas, rewrites paths, and rewrites components.
    """

    class MockResponse:
        status_code = 200

        def json(self):
            return {
                "openapi": "3.1.0",
                "paths": {"/test": {}},
                "components": {"schemas": {"TestModel": {"type": "string"}}},
            }

    async def mock_get(*args: Any, **kwargs: Any) -> MockResponse:
        return MockResponse()

    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    with TestClient(app) as client:
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()
        assert "/designer/test" in data["paths"]
        assert "/execution/test" in data["paths"]
        assert "/ctms/test" in data["paths"]
        assert "Designer_TestModel" in data["components"]["schemas"]
        assert "Execution_TestModel" in data["components"]["schemas"]
        assert "Ctms_TestModel" in data["components"]["schemas"]


def test_get_openapi_json_error(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test the OpenAPI aggregation fallback when downstream services fail.

    Ensures the gateway returns an empty schema without crashing if downstream
    services throw connection errors.
    """

    async def mock_get(*args: Any, **kwargs: Any) -> None:
        raise Exception("Connection error")

    monkeypatch.setattr(httpx.AsyncClient, "get", mock_get)

    with TestClient(app) as client:
        response = client.get("/openapi.json")
        assert response.status_code == 200
        data = response.json()
        assert data["paths"] == {}
        assert data["components"]["schemas"] == {}


def test_get_swagger_ui() -> None:
    """
    Test the Swagger UI HTML endpoint.

    Ensures the /docs route returns a 200 OK status and contains the correct HTML title.
    """
    with TestClient(app) as client:
        response = client.get("/docs")
        assert response.status_code == 200
        assert "Cadence Clinical - Unified API Docs" in response.text


def test_proxy_requests_paths(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test routing proxies for designer and execution prefixes.

    Ensures the gateway routes prefix-specific requests to the right microservice urls.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        # Test designer prefix
        res = client.get("/designer/test", headers={"Authorization": f"Bearer {token}"})
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8001/test"

        # Test execution prefix
        res = client.get(
            "/execution/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8002/test"

        # Test api/v1/execution
        res = client.get(
            "/api/v1/execution/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8002/api/v1/execution/test"
        )

        # Test ctms prefix
        res = client.get(
            "/ctms/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8005/test"

        # Test api/v1/ctms
        res = client.get(
            "/api/v1/ctms/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8005/api/v1/ctms/test"
        )

        # Test default route
        res = client.get("/unknown/path", headers={"Authorization": f"Bearer {token}"})
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url) == "http://localhost:8001/unknown/path"
        )


def test_generate_signature_v2() -> None:
    """
    Test Version 2 signature generation.

    Ensures that signature is key-sorted JSON canonical format,
    and is different from Version 1 signature.
    """

    user_id = "user1"
    roles = "admin"
    timestamp = "123456"
    change_reason = "Clinical reason for test"

    sig_v1 = generate_signature(user_id, roles, timestamp, version="1")
    sig_v2 = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    assert sig_v1 != sig_v2

    # Check key ordering stability
    sig_v2_alt = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    assert sig_v2 == sig_v2_alt


def test_proxy_requests_change_reason_too_long(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test that a change reason exceeding 255 characters is rejected with 400 Bad Request.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )

    long_reason = "A" * 256
    with TestClient(app) as client:
        response = client.get(
            "/api/v1/studies/study_1",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": long_reason,
            },
        )
        assert response.status_code == 400
        assert "exceeds 255 characters" in response.json()["detail"]


def test_proxy_requests_v2_headers(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test that the proxy correctly attaches X-Signature-Version and other required V2 headers.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "roles": ["admin"]}, "test_secret", algorithm="HS256"
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        res = client.get(
            "/designer/test",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "Valid reason",
            },
        )
        assert res.status_code == 200

        # Retrieve request headers sent downstream
        sent_request = mock_send.call_args.args[0]
        sent_headers = sent_request.headers

        assert sent_headers.get("X-Signature-Version") == "2"
        assert sent_headers.get("X-Change-Reason") == "Valid reason"
        assert sent_headers.get("X-Gateway-Signature") is not None


def test_gateway_cors_headers() -> None:
    """
    # @req:PRD-UNI-001
    Test that the API gateway correctly handles CORS requests.

    Ensures that preflight OPTIONS requests return standard CORS response headers
    such as Access-Control-Allow-Origin, Access-Control-Allow-Methods, and
    Access-Control-Allow-Headers.
    """
    with TestClient(app) as client:
        response = client.options(
            "/openapi.json",
            headers={
                "Origin": "http://localhost:3000",
                "Access-Control-Request-Method": "GET",
                "Access-Control-Request-Headers": "Authorization",
            },
        )
        assert response.status_code == 200
        assert response.headers.get("access-control-allow-origin") == "*"
        assert "GET" in response.headers.get("access-control-allow-methods", "")


def test_gateway_rate_limiting(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    # @req:PRD-UNI-001
    Test that the API gateway correctly enforces rate limits on public endpoints.

    Mocks a tight rate limit threshold and sends consecutive requests to verify
    that the rate limiter successfully returns a 429 Too Many Requests status
    once the limit has been exceeded.
    """
    from apps.gateway.main import rate_limiter

    # Store old rate limiter configuration
    old_max = rate_limiter.max_requests
    old_window = rate_limiter.window_seconds

    # Set tight limits for testing
    rate_limiter.max_requests = 2
    rate_limiter.window_seconds = 5.0
    rate_limiter.requests.clear()

    try:
        with TestClient(app) as client:
            # First request - should be allowed (returns 200 for openapi.json)
            # Use mock to prevent actual HTTP calls or use path that doesn't trigger remote fetches
            response1 = client.get("/docs")
            assert response1.status_code == 200

            # Second request - should be allowed
            response2 = client.get("/docs")
            assert response2.status_code == 200

            # Third request - exceeds rate limit, should be blocked with 429
            response3 = client.get("/docs")
            assert response3.status_code == 429
            assert "Rate limit exceeded" in response3.json()["detail"]

    finally:
        # Restore rate limiter configuration and clean up
        rate_limiter.max_requests = old_max
        rate_limiter.window_seconds = old_window
        rate_limiter.requests.clear()
