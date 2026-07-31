import time
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
        assert "/quality/test" in data["paths"]
        assert "/notifications/test" in data["paths"]
        assert "/safety/test" in data["paths"]
        assert "/eisf/test" in data["paths"]
        assert "Designer_TestModel" in data["components"]["schemas"]
        assert "Execution_TestModel" in data["components"]["schemas"]
        assert "Ctms_TestModel" in data["components"]["schemas"]
        assert "Quality_TestModel" in data["components"]["schemas"]
        assert "Notifications_TestModel" in data["components"]["schemas"]
        assert "Safety_TestModel" in data["components"]["schemas"]
        assert "Eisf_TestModel" in data["components"]["schemas"]


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
        res = client.get("/ctms/test", headers={"Authorization": f"Bearer {token}"})
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

        # Test quality prefix
        res = client.get("/quality/test", headers={"Authorization": f"Bearer {token}"})
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8005/test"

        # Test api/v1/quality
        res = client.get(
            "/api/v1/quality/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8005/api/v1/quality/test"
        )

        # Test notifications prefix
        res = client.get(
            "/notifications/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8006/test"

        # Test api/v1/notifications
        res = client.get(
            "/api/v1/notifications/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8006/api/v1/notifications/test"
        )

        # Test safety prefix
        res = client.get("/safety/test", headers={"Authorization": f"Bearer {token}"})
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8008/test"

        # Test api/v1/safety
        res = client.get(
            "/api/v1/safety/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8008/api/v1/safety/test"
        )

        # Test eisf prefix
        res = client.get("/eisf/test", headers={"Authorization": f"Bearer {token}"})
        assert res.status_code == 200
        assert str(mock_send.call_args.args[0].url) == "http://localhost:8010/test"

        # Test api/v1/eisf
        res = client.get(
            "/api/v1/eisf/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8010/api/v1/eisf/test"
        )

        # Test events/publish alias
        res = client.post(
            "/events/publish", headers={"Authorization": f"Bearer {token}"}
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8010/events/publish"
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


def test_gateway_subject_role_routing_restrictions(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test that the gateway restricts users with the 'Subject' role to only
    designated ePRO/eCOA routes, rejecting all others with HTTP 403.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # 1. Subject role token
    token = jwt.encode(
        {"sub": "patient_123", "realm_access": {"roles": ["Subject"]}},
        "test_secret",
        algorithm="HS256",
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        # Allowed routes (should proxy, and because of mock return 200)
        res = client.post(
            "/api/v1/interop/epro/submit",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "submit diary",
            },
            json={"subject_id": "patient_123"},
        )
        assert res.status_code == 200

        res = client.post(
            "/api/v1/interop/epro/sync",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "sync diaries",
            },
            json={"submissions": []},
        )
        assert res.status_code == 200

        # Newly allowed Subject self-service routes (GETs and owned-notification acknowledgement)
        res_assignments = client.get(
            "/api/v1/interop/assignments/subject/patient_123",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_assignments.status_code == 200

        res_instruments = client.get(
            "/api/v1/interop/subjects/patient_123/instruments",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_instruments.status_code == 200

        res_instrument = client.get(
            "/api/v1/interop/instruments/some-instrument-id",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_instrument.status_code == 200

        res_compliance = client.get(
            "/api/v1/interop/subjects/patient_123/compliance",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_compliance.status_code == 200

        res_notifications = client.get(
            "/api/v1/interop/subjects/patient_123/notifications",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_notifications.status_code == 200

        res_acknowledge = client.post(
            "/api/v1/interop/notifications/notif_123/acknowledge",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "ack_reason",
            },
            json={"reason_for_change": "ack_reason"},
        )
        assert res_acknowledge.status_code == 200

        # Cross-subject/Mismatched identity checks -> 403
        res_cross_assignments = client.get(
            "/api/v1/interop/assignments/subject/another_patient",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_cross_assignments.status_code == 403

        res_cross_instruments = client.get(
            "/api/v1/interop/subjects/another_patient/instruments",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_cross_instruments.status_code == 403

        res_cross_compliance = client.get(
            "/api/v1/interop/subjects/another_patient/compliance",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_cross_compliance.status_code == 403

        res_cross_notifications = client.get(
            "/api/v1/interop/subjects/another_patient/notifications",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_cross_notifications.status_code == 403

        # Blocked routes for Subject role -> 403
        res_fhir = client.post(
            "/api/v1/interop/fhir/prefill",
            headers={"Authorization": f"Bearer {token}", "X-Change-Reason": "prefill"},
            json={},
        )
        assert res_fhir.status_code == 403
        assert "Access denied" in res_fhir.json()["detail"]

        res_reminder_compute = client.post(
            "/api/v1/interop/reminders/compute",
            headers={"Authorization": f"Bearer {token}"},
            json={},
        )
        assert res_reminder_compute.status_code == 403

        res_designer = client.get(
            "/designer/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res_designer.status_code == 403

        res_execution = client.get(
            "/execution/test", headers={"Authorization": f"Bearer {token}"}
        )
        assert res_execution.status_code == 403


def test_signature_verification_success(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    # @req:Trace-15
    Test successful re-authentication.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "totp": "123456",
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert "sig_token" in data


def test_signature_verification_invalid_credentials(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test re-authentication with invalid credentials.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        # Invalid password
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "wrong_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 401

        # Invalid TOTP
        response2 = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "totp": "invalid_totp",
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response2.status_code == 401


def test_signature_verification_role_insufficient(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test re-authentication for a user with insufficient roles.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    # Auditor is not in AUTHORIZED_SIGNING_ROLES
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["auditor"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 403
        assert response.json()["detail"] == "ROLE_INSUFFICIENT"


def test_signature_verification_study_designer_role_allowed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test re-authentication for a user with study_designer/sponsor_designer roles.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    for designer_role in ["study_designer", "sponsor_designer"]:
        token = jwt.encode(
            {
                "sub": "user1",
                "preferred_username": "user1",
                "realm_access": {"roles": [designer_role]},
            },
            "test_secret",
            algorithm="HS256",
        )

        with TestClient(app) as client:
            response = client.post(
                "/api/v1/auth/signature-verification",
                headers={"Authorization": f"Bearer {token}"},
                json={
                    "username": "user1",
                    "password": "correct_password",  # pragma: allowlist secret
                    "action": "/api/v1/execution/form-submissions/123/approve",
                },
            )
            assert response.status_code == 200
            assert "sig_token" in response.json()


def test_signature_gated_mutation_enforcement(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    # @req:Trace-15
    Test that signature-gated mutations require a valid sig_token.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        # 1. Muted mutation request without X-Sig-Token -> 401 REAUTHENTICATION_REQUIRED
        response = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response.status_code == 401
        assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_gateway_sponsor_claim_extraction(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test that the gateway successfully extracts custom_attributes.sponsor_id claim,
    handles top-level sponsor_id fallback, and prevents incoming X-Sponsor-Id spoofing.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # Mock send downstream
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    # 1. Custom attributes sponsor_id extraction
    token_nested = jwt.encode(
        {
            "sub": "user_nested",
            "roles": ["sponsor_designer"],
            "custom_attributes": {"sponsor_id": "spon_nested_123"},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        res = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token_nested}"},
        )
        assert res.status_code == 200
        sent_request = mock_send.call_args.args[0]
        assert sent_request.headers.get("X-Sponsor-Id") == "spon_nested_123"

        # 2. Fallback to top-level sponsor_id claim
        token_fallback = jwt.encode(
            {
                "sub": "user_fallback",
                "roles": ["sponsor_designer"],
                "sponsor_id": "spon_fallback_456",
            },
            "test_secret",
            algorithm="HS256",
        )
        res_fb = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token_fallback}"},
        )
        assert res_fb.status_code == 200
        sent_request_fb = mock_send.call_args.args[0]
        assert sent_request_fb.headers.get("X-Sponsor-Id") == "spon_fallback_456"

        # 3. Prevent incoming X-Sponsor-Id spoofing (should be stripped/overridden)
        res_spoof = client.get(
            "/designer/test",
            headers={
                "Authorization": f"Bearer {token_nested}",
                "X-Sponsor-Id": "spon_hacker_789",  # Spoofed client header
            },
        )
        assert res_spoof.status_code == 200
        sent_request_spoof = mock_send.call_args.args[0]
        # Should be overridden by the trusted token's claim!
        assert sent_request_spoof.headers.get("X-Sponsor-Id") == "spon_nested_123"


def test_signature_verification_with_batch_id(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    # @req:Trace-15
    Test successful re-authentication with an optional batch_id.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "totp": "123456",
                "action": "/api/v1/execution/form-submissions/123/approve",
                "batch_id": "batch_abc_123",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert "sig_token" in data

        # Verify batch_id claim inside signature token
        from apps.gateway.main import GATEWAY_SECRET

        sig_payload = jwt.decode(
            data["sig_token"], GATEWAY_SECRET, algorithms=["HS256"]
        )
        assert sig_payload.get("batch_id") == "batch_abc_123"
        assert "jti" in sig_payload


def test_signature_token_altered_signature_rejected(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test that an altered or tampered signature token is rejected.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    # 1. Re-authenticate to get a valid token
    with TestClient(app) as client:
        reauth_resp = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert reauth_resp.status_code == 200
        valid_token = reauth_resp.json()["sig_token"]

        # 2. Tamper with signature by changing characters at the end of the token string
        tampered_token = valid_token[:-4] + "AAAA"

        # 3. Call signature-gated endpoint with tampered token -> 401
        response = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": tampered_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response.status_code == 401
        assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_signature_token_credentials_not_logged_or_returned(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    """
    # @req:Trace-15
    Test that user credentials/passwords are neither logged nor returned.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    sensitive_password = "very_secret_user_password_123!"  # pragma: allowlist secret

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": sensitive_password,  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert response.status_code == 200

        # Ensure password is not in response content
        assert sensitive_password not in response.text

        # Ensure password is not in any logs captured during this execution
        for record in caplog.records:
            assert sensitive_password not in record.message


def test_proxy_requests_terminology_paths(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Test routing proxies for terminology-related paths.

    Ensures that /api/v1/terminology and /terminology prefix paths are correctly routed
    to SERVICES['designer'] with correct signed headers.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {"sub": "user1", "realm_access": {"roles": ["sponsor_designer"]}},
        "test_secret",
        algorithm="HS256",
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        # Test api/v1/terminology prefix
        res = client.get(
            "/api/v1/terminology/search?term=treatment",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8001/api/v1/terminology/search?term=treatment"
        )

        # Verify signed headers are present
        sent_request = mock_send.call_args.args[0]
        sent_headers = sent_request.headers
        assert sent_headers.get("X-User-Id") == "user1"
        assert sent_headers.get("X-User-Roles") == "sponsor_designer"
        assert sent_headers.get("X-Gateway-Signature") is not None
        assert sent_headers.get("X-Signature-Version") == "2"

        # Test terminology/ prefix (which strips prefix and routes to designer)
        res_stripped = client.get(
            "/terminology/validate/C123", headers={"Authorization": f"Bearer {token}"}
        )
        assert res_stripped.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8001/validate/C123"
        )

        # Test existing study-scoped validation path routes to designer as well
        res_study = client.get(
            "/api/v1/studies/study_123/ct-validation",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_study.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8001/api/v1/studies/study_123/ct-validation"
        )

        # Verify signed headers are present on ct-validation path as well
        sent_request_ct = mock_send.call_args.args[0]
        sent_headers_ct = sent_request_ct.headers
        assert sent_headers_ct.get("X-User-Id") == "user1"
        assert sent_headers_ct.get("X-User-Roles") == "sponsor_designer"
        assert sent_headers_ct.get("X-Gateway-Signature") is not None
        assert sent_headers_ct.get("X-Signature-Version") == "2"
        assert sent_headers_ct.get("X-Gateway-Timestamp") is not None

        # Test study-scoped terminology-validation path routes to designer as well
        res_study_term = client.get(
            "/api/v1/studies/study_123/terminology-validation",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res_study_term.status_code == 200
        assert (
            str(mock_send.call_args.args[0].url)
            == "http://localhost:8001/api/v1/studies/study_123/terminology-validation"
        )

        # Verify signed headers are present on terminology-validation path as well
        sent_request_term = mock_send.call_args.args[0]
        sent_headers_term = sent_request_term.headers
        assert sent_headers_term.get("X-User-Id") == "user1"
        assert sent_headers_term.get("X-User-Roles") == "sponsor_designer"
        assert sent_headers_term.get("X-Gateway-Signature") is not None
        assert sent_headers_term.get("X-Signature-Version") == "2"
        assert sent_headers_term.get("X-Gateway-Timestamp") is not None

        # 2. Re-authenticate to get sig_token
        reauth_resp = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert reauth_resp.status_code == 200
        sig_token = reauth_resp.json()["sig_token"]

        # 3. Request with valid X-Sig-Token -> 200 OK
        response2 = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response2.status_code == 200

        # 4. Request with same X-Sig-Token (Replay) -> 401 REAUTHENTICATION_REQUIRED
        response3 = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response3.status_code == 401
        assert response3.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_signature_gated_mutation_expired_token(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test that expired sig_tokens are rejected.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    # Issue an expired token
    expired_time = time.time() - 10.0
    payload = {
        "sub": "user1",
        "username": "user1",
        "action": "/api/v1/execution/form-submissions/123/approve",
        "roles": ["investigator"],
        "iat": expired_time - 60.0,
        "exp": expired_time,
    }
    from apps.gateway.main import GATEWAY_SECRET

    expired_token = jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")

    with TestClient(app) as client:
        response = client.post(
            "/api/v1/execution/form-submissions/123/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": expired_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response.status_code == 401
        assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_signature_gated_mutation_mismatched_action(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-15
    Test that sig_tokens bound to a different action/path are rejected.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        # Get token bound to action A
        reauth_resp = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/execution/form-submissions/123/approve",
            },
        )
        assert reauth_resp.status_code == 200
        sig_token = reauth_resp.json()["sig_token"]

        # Request to action B using token A -> 401 REAUTHENTICATION_REQUIRED
        response = client.post(
            "/api/v1/execution/form-submissions/999/approve",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Valid reason",
            },
        )
        assert response.status_code == 401
        assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_gateway_startup_production_with_test_secret() -> None:
    """
    Test that the gateway terminates with a non-zero exit code if the environment is set to production
    and JWT_TEST_SECRET is present.
    """
    import subprocess
    import sys

    env = {
        "APP_ENV": "production",
        "JWT_TEST_SECRET": "some_test_secret",  # pragma: allowlist secret
    }
    result = subprocess.run(
        [sys.executable, "-c", "import apps.gateway.main"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert result.returncode != 0
    assert "SECURITY ALERT" in result.stderr
    assert "JWT_TEST_SECRET" in result.stderr


def test_gateway_startup_production_with_unverified_jwt() -> None:
    """
    Test that the gateway terminates with a non-zero exit code if the environment is set to production
    and ALLOW_UNVERIFIED_JWT_FOR_TEST is enabled.
    """
    import subprocess
    import sys

    env = {
        "APP_ENV": "production",
        "ALLOW_UNVERIFIED_JWT_FOR_TEST": "true",
    }
    result = subprocess.run(
        [sys.executable, "-c", "import apps.gateway.main"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert result.returncode != 0
    assert "SECURITY ALERT" in result.stderr
    assert "ALLOW_UNVERIFIED_JWT_FOR_TEST" in result.stderr


def test_gateway_startup_production_with_skip_jwks() -> None:
    """
    Test that the gateway terminates with a non-zero exit code if the environment is set to production
    and SKIP_JWKS_FETCH is enabled.
    """
    import subprocess
    import sys

    env = {
        "APP_ENV": "production",
        "SKIP_JWKS_FETCH": "true",
    }
    result = subprocess.run(
        [sys.executable, "-c", "import apps.gateway.main"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert result.returncode != 0
    assert "SECURITY ALERT" in result.stderr
    assert "SKIP_JWKS_FETCH" in result.stderr


def test_gateway_startup_development_with_bypass_configs() -> None:
    """
    Test that the gateway initializes without errors when test bypass configurations are set and the
    environment is explicitly configured as development.
    """
    import subprocess
    import sys

    env = {
        "APP_ENV": "development",
        "JWT_TEST_SECRET": "some_secret",  # pragma: allowlist secret
        "ALLOW_UNVERIFIED_JWT_FOR_TEST": "true",
        "SKIP_JWKS_FETCH": "true",
    }
    result = subprocess.run(
        [sys.executable, "-c", "import apps.gateway.main"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert result.returncode == 0


@pytest.mark.asyncio
async def test_eisf_gateway_site_isolation_propagation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-16
    Contract test: Verify that valid gateway-signed identity/site scope headers
    are propagated to eISF, and that eISF correctly enforces site isolation
    (returning 403 and logging SECURITY_ALERT on mismatch).
    """
    from httpx import ASGITransport, AsyncClient
    from sqlalchemy import select

    from apps.eisf.database import db_manager as eisf_db_manager
    from apps.eisf.main import app as eisf_app
    from apps.eisf.models import Base as EisfBase
    from apps.eisf.models import ISFAuditLog
    from tests.test_eisf_api import get_eisf_auth_headers

    # Initialize in-memory SQLite database for eISF
    eisf_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with eisf_db_manager.engine.begin() as conn:
        await conn.run_sync(EisfBase.metadata.create_all)

    try:
        async with AsyncClient(
            transport=ASGITransport(app=eisf_app), base_url="http://test"
        ) as eisf_client:
            # Create same-site document using admin to allow setup
            setup_headers = get_eisf_auth_headers(
                user_id="admin-user", roles="admin", site_id="site-boston-01"
            )
            doc_payload = {
                "study_id": "study-100",
                "site_id": "site-boston-01",
                "binder_classification": "Investigator CV",
                "filename": "cv.pdf",
                "content": "CV content",
                "mime_type": "application/pdf",
                "reason_for_change": "Admin setup",
            }
            res_setup = await eisf_client.post(
                "/api/v1/eisf/documents", json=doc_payload, headers=setup_headers
            )
            assert res_setup.status_code == 201
            doc_id = res_setup.json()["id"]

            # 1. Same-site access (should succeed)
            pi_boston_headers = get_eisf_auth_headers(
                user_id="pi-boston", roles="site investigator", site_id="site-boston-01"
            )
            res_same = await eisf_client.get(
                f"/api/v1/eisf/documents/{doc_id}", headers=pi_boston_headers
            )
            assert res_same.status_code == 200

            # 2. Cross-site access (should return 403 and write SECURITY_ALERT log)
            pi_london_headers = get_eisf_auth_headers(
                user_id="pi-london", roles="site investigator", site_id="site-london-02"
            )
            res_cross = await eisf_client.get(
                f"/api/v1/eisf/documents/{doc_id}", headers=pi_london_headers
            )
            assert res_cross.status_code == 403

            # Verify SECURITY_ALERT log entry
            async with eisf_db_manager.get_session_maker()() as session:
                stmt = select(ISFAuditLog).where(ISFAuditLog.action == "SECURITY_ALERT")
                res = await session.execute(stmt)
                alerts = res.scalars().all()
                assert len(alerts) > 0

    finally:
        async with eisf_db_manager.engine.begin() as conn:
            await conn.run_sync(EisfBase.metadata.drop_all)
        await eisf_db_manager.close()


def test_gateway_proxy_eisf_headers_propagation(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    # @req:Trace-16
    Verify that when proxying to eISF, the gateway successfully extracts site_id from JWT
    and propagates it along with signature version 2 headers unchanged to the eISF proxy path.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "pi-boston",
            "roles": ["site investigator"],
            "site_id": "site-boston-01",
        },
        "test_secret",
        algorithm="HS256",
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
            "/api/v1/eisf/documents",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "Authorized list documents",
            },
        )
        assert res.status_code == 200

        sent_request = mock_send.call_args.args[0]
        sent_headers = sent_request.headers

        assert sent_headers.get("X-Site-Id") == "site-boston-01"
        assert sent_headers.get("X-User-Id") == "pi-boston"
        assert sent_headers.get("X-Signature-Version") == "2"
        assert sent_headers.get("X-Gateway-Signature") is not None


def test_gateway_scope_extraction_and_verification_integrity(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test that the gateway extracts claims (site_id, sponsor_id, unblinded_access) from JWT,
    normalizes them, generates a valid V2 signature, and forwards them, and that if any forwarded
    header is altered/injected, the downstream verification rejects the signature.
    """
    from packages.security.signing import (
        normalize_scope_values,
        verify_gateway_signature,
    )

    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # Mock send downstream
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    # JWT with site_id, sponsor_id, unblinded_access
    token = jwt.encode(
        {
            "sub": "user_123",
            "roles": ["sponsor_designer"],
            "site_id": ["site_a", "site_b"],  # List test
            "sponsor_id": "sponsor_999",
            "unblinded_access": "yes",  # Coercion test ("yes" -> True)
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        res = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token}"},
        )
        assert res.status_code == 200
        sent_request = mock_send.call_args.args[0]

        # Verify normalization consistent with shared helper (list joined by comma, "yes" coerced to "true")
        assert sent_request.headers.get("X-Site-Id") == "site_a,site_b"
        assert sent_request.headers.get("X-Sponsor-Id") == "sponsor_999"
        assert sent_request.headers.get("X-Unblinded-Access") == "true"

        # Verify that downstream verification succeeds with the unmodified headers and signature
        user_id = sent_request.headers.get("X-User-Id")
        roles = sent_request.headers.get("X-User-Roles")
        timestamp = sent_request.headers.get("X-Gateway-Timestamp")
        signature = sent_request.headers.get("X-Gateway-Signature")
        tenant_id = sent_request.headers.get("X-Tenant-Id") or "tenant_default"

        gateway_secret = b"internal-gateway-secret-12345"

        # Run normalization on the forwarded headers
        site_id, sponsor_id, unblinded_access = normalize_scope_values(
            sent_request.headers.get("X-Site-Id"),
            sent_request.headers.get("X-Sponsor-Id"),
            sent_request.headers.get("X-Unblinded-Access"),
        )

        assert (
            verify_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                signature=signature,
                secret=gateway_secret,
                change_reason="",
                site_id=site_id,
                sponsor_id=sponsor_id,
                unblinded_access=unblinded_access,
                tenant_id=tenant_id,
            )
            is True
        )

        # Now test that downstream verification REJECTS if any scope header is altered/injected
        # 1. Alter site_id
        assert (
            verify_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                signature=signature,
                secret=gateway_secret,
                change_reason="",
                site_id="site_a,site_b,site_c",  # altered
                sponsor_id=sponsor_id,
                unblinded_access=unblinded_access,
                tenant_id=tenant_id,
            )
            is False
        )

        # 2. Alter sponsor_id
        assert (
            verify_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                signature=signature,
                secret=gateway_secret,
                change_reason="",
                site_id=site_id,
                sponsor_id="sponsor_altered",  # altered
                unblinded_access=unblinded_access,
                tenant_id=tenant_id,
            )
            is False
        )

        # 3. Alter unblinded_access
        assert (
            verify_gateway_signature(
                user_id=user_id,
                roles=roles,
                timestamp=timestamp,
                signature=signature,
                secret=gateway_secret,
                change_reason="",
                site_id=site_id,
                sponsor_id=sponsor_id,
                unblinded_access=False,  # altered
                tenant_id=tenant_id,
            )
            is False
        )


def test_gateway_semantic_action_issuance_and_enforcement(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test issuance and gateway body-driven semantic gating enforcement for CAPA close transition.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")
    token = jwt.encode(
        {
            "sub": "user1",
            "preferred_username": "user1",
            "realm_access": {"roles": ["investigator"]},
        },
        "test_secret",
        algorithm="HS256",
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    with TestClient(app) as client:
        # 1. Request sig_token with semantic_action explicitly
        reauth_resp = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/quality/capas/123/transition",
                "semantic_action": "quality.capa.close",
            },
        )
        assert reauth_resp.status_code == 200
        sig_token = reauth_resp.json()["sig_token"]

        # Decode sig_token to verify claims
        from apps.gateway.main import GATEWAY_SECRET

        sig_payload = jwt.decode(sig_token, GATEWAY_SECRET, algorithms=["HS256"])
        assert sig_payload.get("semantic_action") == "quality.capa.close"
        assert sig_payload.get("sig_ver") == "v3"

        # 2. Body-driven regulated transition request (CLOSED) with valid sig_token -> 200
        res_valid = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Closing CAPA",
            },
            json={"to_status": "CLOSED"},
        )
        assert res_valid.status_code == 200

        # 3. Request CLOSED transition with missing sig_token -> 401
        res_missing = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "Closing CAPA",
            },
            json={"to_status": "CLOSED"},
        )
        assert res_missing.status_code == 401

        # 4. Request CLOSED transition with mismatched semantic action -> 401
        # Get token bound to cancel
        reauth_cancel = client.post(
            "/api/v1/auth/signature-verification",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "username": "user1",
                "password": "correct_password",  # pragma: allowlist secret
                "action": "/api/v1/quality/capas/123/transition",
                "semantic_action": "quality.capa.cancel",
            },
        )
        sig_token_cancel = reauth_cancel.json()["sig_token"]
        res_mismatched_semantic = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token_cancel,
                "X-Change-Reason": "Closing CAPA",
            },
            json={"to_status": "CLOSED"},
        )
        assert res_mismatched_semantic.status_code == 401

        # 5. Request CLOSED transition with expired token -> 401
        expired_payload = sig_payload.copy()
        expired_payload["exp"] = time.time() - 10.0
        expired_token = jwt.encode(expired_payload, GATEWAY_SECRET, algorithm="HS256")
        res_expired = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": expired_token,
                "X-Change-Reason": "Closing CAPA",
            },
            json={"to_status": "CLOSED"},
        )
        assert res_expired.status_code == 401

        # 6. Request CLOSED transition with replayed token -> 401
        # Reuse first sig_token, but it is already replayed (already added to cache in gateway in test 2)
        res_replayed = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Sig-Token": sig_token,
                "X-Change-Reason": "Closing CAPA",
            },
            json={"to_status": "CLOSED"},
        )
        assert res_replayed.status_code == 401

        # 7. Non-terminal / un-regulated transition (e.g. to_status="UNDER_REVIEW") should be ungated -> 200
        res_ungated = client.post(
            "/api/v1/quality/capas/123/transition",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Change-Reason": "Reviewing CAPA",
            },
            json={"to_status": "UNDER_REVIEW"},
        )
        assert res_ungated.status_code == 200


def test_gateway_tenant_claim_extraction(monkeypatch: pytest.MonkeyPatch) -> None:
    """Validate that the gateway successfully extracts tenant claims, enforces default fallback migration, and signs them.

    Requirements: PRD-SYS-001
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # Mock send downstream
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    # 1. Custom attributes tenant_id extraction
    token_nested = jwt.encode(
        {
            "sub": "user_nested",
            "roles": ["sponsor_designer"],
            "custom_attributes": {"tenant_id": "tenant_pfizer_123"},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        res = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token_nested}"},
        )
        assert res.status_code == 200
        sent_request = mock_send.call_args.args[0]
        assert sent_request.headers.get("X-Tenant-Id") == "tenant_pfizer_123"

        # 2. Fallback to top-level tenant_id claim
        token_fallback = jwt.encode(
            {
                "sub": "user_fallback",
                "roles": ["sponsor_designer"],
                "tenant_id": "tenant_roche_456",
            },
            "test_secret",
            algorithm="HS256",
        )
        res_fb = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token_fallback}"},
        )
        assert res_fb.status_code == 200
        sent_request_fb = mock_send.call_args.args[0]
        assert sent_request_fb.headers.get("X-Tenant-Id") == "tenant_roche_456"

        # 3. Default fallback (migration policy) when no tenant_id exists
        token_default = jwt.encode(
            {
                "sub": "user_default",
                "roles": ["sponsor_designer"],
            },
            "test_secret",
            algorithm="HS256",
        )
        res_def = client.get(
            "/designer/test",
            headers={"Authorization": f"Bearer {token_default}"},
        )
        assert res_def.status_code == 200
        sent_request_def = mock_send.call_args.args[0]
        assert sent_request_def.headers.get("X-Tenant-Id") == "tenant_default"


def test_gateway_tenant_spoofing_prevention(monkeypatch: pytest.MonkeyPatch) -> None:
    """Validate that the gateway sanitizes/strips incoming X-Tenant-Id and establishes identity from claims.

    Requirements: PRD-SYS-001
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # Mock send downstream
    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    token = jwt.encode(
        {
            "sub": "user_nested",
            "roles": ["sponsor_designer"],
            "custom_attributes": {"tenant_id": "tenant_pfizer_123"},
        },
        "test_secret",
        algorithm="HS256",
    )

    with TestClient(app) as client:
        res = client.get(
            "/designer/test",
            headers={
                "Authorization": f"Bearer {token}",
                "X-Tenant-Id": "spoofed_tenant_id_hacker",
            },
        )
        assert res.status_code == 200
        sent_request = mock_send.call_args.args[0]
        # Should be completely overridden by the claims-derived tenant!
        assert sent_request.headers.get("X-Tenant-Id") == "tenant_pfizer_123"


def test_gateway_bearer_only_subject_routing_and_header_enforcement(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """
    Test that sending a Subject-role JWT with only Authorization: Bearer <token>
    to the portal's actually-consumed routes successfully proxies (returns 200),
    that the gateway correctly injects downstream headers, and that any client-supplied
    identity/signature headers are stripped and overwritten.
    """
    monkeypatch.setenv("JWT_TEST_SECRET", "test_secret")

    # 1. Subject role token
    token = jwt.encode(
        {"sub": "patient_123", "realm_access": {"roles": ["Subject"]}},
        "test_secret",
        algorithm="HS256",
    )

    mock_send = AsyncMock()
    mock_resp = MagicMock()
    mock_resp.status_code = 200
    mock_resp.content = b'{"status": "ok"}'
    mock_resp.headers = {"content-type": "application/json"}
    mock_send.return_value = mock_resp
    monkeypatch.setattr(httpx.AsyncClient, "send", mock_send)

    # Portal's actually-consumed routes
    routes_to_test = [
        ("/api/v1/interop/assignments/subject/patient_123", "GET", None),
        ("/api/v1/interop/subjects/patient_123/notifications", "GET", None),
        (
            "/api/v1/interop/notifications/notif_123/acknowledge",
            "POST",
            {"reason_for_change": "some reason"},
        ),
        ("/api/v1/interop/epro/sync", "POST", {"submissions": []}),
    ]

    with TestClient(app) as client:
        for route, method, payload in routes_to_test:
            mock_send.reset_mock()

            # Attempt to send client-supplied spoofed identity/signature headers
            headers = {
                "Authorization": f"Bearer {token}",
                "X-User-Id": "malicious_user",
                "X-User-Roles": "malicious_role",
                "X-Gateway-Signature": "fake_signature",
                "X-Signature-Version": "fake_version",
                "X-Gateway-Timestamp": "fake_timestamp",
            }

            if method == "GET":
                res = client.get(route, headers=headers)
            else:
                res = client.post(route, headers=headers, json=payload)

            assert res.status_code == 200

            # Verify downstream send was called
            assert mock_send.call_args is not None
            sent_request = mock_send.call_args.args[0]
            sent_headers = sent_request.headers

            # Verify that client-supplied identity/signature headers were stripped and overwritten by the gateway
            assert sent_headers.get("X-User-Id") == "patient_123"
            assert sent_headers.get("X-User-Roles") == "Subject"
            assert sent_headers.get("X-Signature-Version") == "2"
            assert sent_headers.get("X-Gateway-Timestamp") != "fake_timestamp"
            assert sent_headers.get("X-Gateway-Signature") != "fake_signature"
            assert sent_headers.get("X-Gateway-Signature") is not None


def test_gateway_startup_production_no_bypass_configs() -> None:
    """
    Test that the gateway successfully completes initialization in production when no test bypass
    configurations are detected.
    """
    import subprocess
    import sys

    env = {
        "APP_ENV": "production",
    }
    # Ensure bypass env vars are not in the environment
    env_keys = ["JWT_TEST_SECRET", "ALLOW_UNVERIFIED_JWT_FOR_TEST", "SKIP_JWKS_FETCH"]
    for key in env_keys:
        if key in env:
            del env[key]

    result = subprocess.run(
        [sys.executable, "-c", "import apps.gateway.main"],
        env=env,
        capture_output=True,
        text=True,
    )
    assert result.returncode == 0
