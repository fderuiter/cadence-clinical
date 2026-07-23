import time

from fastapi import FastAPI
from fastapi.testclient import TestClient

from apps.gateway.main import generate_signature
from packages.security.middleware import GatewayAuthMiddleware

# Setup a test app wrapped in GatewayAuthMiddleware
test_app = FastAPI()
test_app.add_middleware(GatewayAuthMiddleware)


@test_app.get("/secure-endpoint")
async def secure_endpoint():
    return {"status": "success", "message": "Access Granted"}


@test_app.post("/secure-endpoint")
async def secure_endpoint_post():
    return {"status": "success", "message": "Access Granted"}


@test_app.get("/health")
async def health_endpoint():
    return {"status": "ok"}


def test_middleware_health_bypass() -> None:
    """
    Test that health check endpoints bypass security middleware checks.
    """
    client = TestClient(test_app)
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_middleware_missing_headers() -> None:
    """
    Test that requests with missing gateway auth headers are rejected.
    """
    client = TestClient(test_app)
    response = client.get("/secure-endpoint")
    assert response.status_code == 401
    assert "Missing gateway authentication headers" in response.json()["detail"]


def test_middleware_expired_timestamp() -> None:
    """
    Test that requests with timestamps older than 300 seconds are rejected.
    """
    client = TestClient(test_app)
    expired_timestamp = str(time.time() - 301)
    headers = {
        "X-User-Id": "test_user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": expired_timestamp,
        "X-Gateway-Signature": "some_sig",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Gateway signature expired" in response.json()["detail"]


def test_middleware_invalid_timestamp_format() -> None:
    """
    Test that requests with malformed timestamps are rejected.
    """
    client = TestClient(test_app)
    headers = {
        "X-User-Id": "test_user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": "not-a-number",
        "X-Gateway-Signature": "some_sig",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway timestamp" in response.json()["detail"]


def test_middleware_v1_legacy_fallback_success() -> None:
    """
    Test that missing version header falls back to legacy V1 validation and passes on valid signature.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "legacy_user"
    roles = "user,editor"

    # Generate legacy V1 signature
    sig = generate_signature(user_id, roles, timestamp, version="1")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 200
    assert response.json()["message"] == "Access Granted"


def test_middleware_v1_explicit_success() -> None:
    """
    Test that an explicit V1 version header executes legacy validation and passes on valid signature.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "legacy_user_v1"
    roles = "user"

    sig = generate_signature(user_id, roles, timestamp, version="1")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "1",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 200


def test_middleware_v1_invalid_signature() -> None:
    """
    Test that legacy V1 validation rejects invalid signatures.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": "invalid_signature_hex",
        "X-Signature-Version": "1",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway signature" in response.json()["detail"]


def test_middleware_v2_success() -> None:
    """
    Test that V2 signature verification passes given a valid JSON-canonical signature.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "v2_user"
    roles = "admin"
    change_reason = "Updating medical data for patient X"

    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 200


def test_middleware_v2_missing_reason() -> None:
    """
    Test that V2 signature verification rejects mutation requests with missing or empty change reason.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "v2_user"
    roles = "admin"

    sig = generate_signature(user_id, roles, timestamp, version="2", change_reason="")

    # Missing X-Change-Reason header entirely
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Missing change justification reason" in response.json()["detail"]


def test_middleware_v2_invalid_signature() -> None:
    """
    Test that V2 signature verification rejects invalid signatures.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": "wrong-hmac",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Valid reason",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway signature" in response.json()["detail"]


def test_middleware_v2_mismatched_reason() -> None:
    """
    Test that V2 signature verification rejects if header change reason differs from signed reason.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "v2_user"
    roles = "admin"
    signed_reason = "Original signed reason"
    tampered_reason = "Modified audit justification"

    # Sign with original reason
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=signed_reason
    )

    # Request with modified reason
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": tampered_reason,
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway signature" in response.json()["detail"]


def test_middleware_v2_safe_method_no_reason_success() -> None:
    """
    Test that V2 signature verification permits safe HTTP methods (GET) without X-Change-Reason.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "v2_user"
    roles = "admin"

    sig = generate_signature(user_id, roles, timestamp, version="2", change_reason="")

    # Missing X-Change-Reason entirely, but using GET
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 200
