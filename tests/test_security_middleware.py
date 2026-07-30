import time
from typing import Optional

from fastapi import FastAPI
from fastapi.testclient import TestClient
from jose import jwt

from apps.gateway.main import generate_signature
from packages.security.context import (
    audit_context,
    audit_context_decorator,
    current_change_reason,
    current_ip_address,
    current_timestamp,
    current_user_id,
)
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.signing import (
    generate_canonical_signature,
    verify_canonical_signature,
)

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


@test_app.post("/api/v1/execution/form-submissions/{submission_id}/approve")
async def form_approve_endpoint(submission_id: str):
    return {"status": "success", "message": "Form Approved"}


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


def test_middleware_missing_signature_version_rejected() -> None:
    """
    Test that requests omitting the version header are immediately rejected.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "user,editor"

    # Signature is generated (which is V2 now, but if they omit the version header, we reject)
    sig = generate_signature(user_id, roles, timestamp)

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
    }
    # GET request omitting version header should get 401
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Missing or obsolete signature format" in response.json()["detail"]

    # POST request omitting version header should get 403
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 403
    assert "Missing or obsolete signature format" in response.json()["detail"]


def test_middleware_explicit_legacy_version_accepted() -> None:
    """
    Test that explicitly specifying legacy Version 1 signature header is accepted when valid.
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


def test_middleware_explicit_legacy_version_invalid_rejected() -> None:
    """
    Test that explicitly specifying legacy Version 1 signature header with an invalid signature is rejected.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "legacy_user_v1"
    roles = "user"

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": "invalid_sig",
        "X-Signature-Version": "1",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway signature" in response.json()["detail"]


def test_middleware_unsupported_version_rejected() -> None:
    """
    Test that invalid or unsupported version headers are rejected.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": "some-signature",
        "X-Signature-Version": "3",
    }
    response = client.get("/secure-endpoint", headers=headers)
    assert response.status_code == 401
    assert "Missing or obsolete signature format" in response.json()["detail"]


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
    Test that V2 signature verification rejects mutation requests with missing or empty change reason with HTTP 403.
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
    assert response.status_code == 403
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


def test_mutation_unsigned_and_non_compliant_rejections() -> None:
    """
    Test that unsigned or non-compliant mutation attempts are rejected with HTTP 400/403 responses.
    """
    client = TestClient(test_app)

    # 1. POST (mutation) with missing headers -> HTTP 403
    response = client.post("/secure-endpoint")
    assert response.status_code == 403
    assert "Missing gateway authentication" in response.json()["detail"]

    # 2. POST (mutation) with invalid signature -> HTTP 403
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": str(time.time()),
        "X-Gateway-Signature": "invalid-sig",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Valid reason",
    }
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 403
    assert "Invalid gateway signature" in response.json()["detail"]

    # 3. POST (mutation) with expired signature -> HTTP 403
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": str(time.time() - 301),
        "X-Gateway-Signature": "sig",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Valid reason",
    }
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 403
    assert "Gateway signature expired" in response.json()["detail"]

    # 4. POST (mutation) with malformed timestamp -> HTTP 400
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": "not-a-number",
        "X-Gateway-Signature": "sig",
        "X-Signature-Version": "2",
        "X-Change-Reason": "Valid reason",
    }
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 400
    assert "Invalid gateway timestamp" in response.json()["detail"]

    # 5. POST (mutation) with change reason > 255 chars -> HTTP 400
    headers = {
        "X-User-Id": "user",
        "X-User-Roles": "user",
        "X-Gateway-Timestamp": str(time.time()),
        "X-Gateway-Signature": "sig",
        "X-Signature-Version": "2",
        "X-Change-Reason": "A" * 256,
    }
    response = client.post("/secure-endpoint", headers=headers)
    assert response.status_code == 400
    assert "Change reason exceeds 255 characters" in response.json()["detail"]


def test_canonical_json_signing_and_verification() -> None:
    """
    Test canonical JSON serialization and signing/verification of payloads (e.g., study versions).
    """
    secret = b"test-secret-key-for-study-signing-and-protocol-locks"
    payload = {
        "study_id": "study_100",
        "version_index": 2,
        "is_locked": True,
        "meta": {"author": "Dr. John Doe", "approved": True},
    }

    # Generate canonical signature
    signature = generate_canonical_signature(payload, secret)
    assert len(signature) == 64  # SHA-256 hex signature is 64 characters

    # Verify valid signature
    assert verify_canonical_signature(payload, signature, secret) is True

    # Tampering with payload fails verification
    tampered_payload = payload.copy()
    tampered_payload["is_locked"] = False
    assert verify_canonical_signature(tampered_payload, signature, secret) is False


def test_audit_context_variables_and_decorator() -> None:
    """
    Test context variable binding, retrieval, and decorator functionality.
    """
    # 1. Verify defaults
    assert current_user_id.get() == "system"
    assert current_change_reason.get() == "system_operation"
    assert current_ip_address.get() == "127.0.0.1"
    assert current_timestamp.get() is None

    # 2. Test context manager
    with audit_context(
        user_id="user_abc", change_reason="updating drug design", ip_address="10.0.0.5"
    ):
        assert current_user_id.get() == "user_abc"
        assert current_change_reason.get() == "updating drug design"
        assert current_ip_address.get() == "10.0.0.5"
        assert current_timestamp.get() is not None

    # Verify reset
    assert current_user_id.get() == "system"
    assert current_ip_address.get() == "127.0.0.1"

    # 3. Test decorator on sync/async functions
    @audit_context_decorator(
        user_id_getter=lambda *args, **kwargs: kwargs.get("user"),
        change_reason_getter=lambda *args, **kwargs: kwargs.get("reason"),
        ip_address_getter=lambda *args, **kwargs: kwargs.get("ip"),
    )
    async def decorated_async_fn(*args, **kwargs):
        return {
            "user": current_user_id.get(),
            "reason": current_change_reason.get(),
            "ip": current_ip_address.get(),
        }

    @audit_context_decorator(
        user_id_getter=lambda *args, **kwargs: kwargs.get("user"),
        change_reason_getter=lambda *args, **kwargs: kwargs.get("reason"),
        ip_address_getter=lambda *args, **kwargs: kwargs.get("ip"),
    )
    def decorated_sync_fn(*args, **kwargs):
        return {
            "user": current_user_id.get(),
            "reason": current_change_reason.get(),
            "ip": current_ip_address.get(),
        }

    # Run async decorated fn
    import asyncio

    res_async = asyncio.run(
        decorated_async_fn(user="admin_user", reason="locking study", ip="192.168.1.1")
    )
    assert res_async == {
        "user": "admin_user",
        "reason": "locking study",
        "ip": "192.168.1.1",
    }

    # Run sync decorated fn
    res_sync = decorated_sync_fn(
        user="pi_investigator", reason="signoff", ip="172.16.0.2"
    )
    assert res_sync == {
        "user": "pi_investigator",
        "reason": "signoff",
        "ip": "172.16.0.2",
    }


def test_downstream_signature_gated_endpoint_requires_sig_token() -> None:
    """
    Test that signature-gated endpoints in downstream microservices reject requests without X-Sig-Token.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "investigator"
    change_reason = "PI Sign-off"

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

    # Request without X-Sig-Token should fail with REAUTHENTICATION_REQUIRED
    response = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response.status_code == 401
    assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_downstream_signature_gated_endpoint_valid_sig_token() -> None:
    """
    Test that a valid, bound, and unexpired X-Sig-Token permits access.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "investigator"
    change_reason = "PI Sign-off"

    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    # Generate valid sig_token
    payload = {
        "sub": user_id,
        "username": "test_user",
        "action": "/api/v1/execution/form-submissions/123/approve",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    sig_token = jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Sig-Token": sig_token,
    }

    response = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response.status_code == 200
    assert response.json()["status"] == "success"


def test_downstream_signature_gated_endpoint_expired_token() -> None:
    """
    Test that an expired X-Sig-Token is rejected by downstream middleware.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "investigator"
    change_reason = "PI Sign-off"

    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    # Expired token
    payload = {
        "sub": user_id,
        "username": "test_user",
        "action": "/api/v1/execution/form-submissions/123/approve",
        "roles": [roles],
        "iat": time.time() - 70.0,
        "exp": time.time() - 10.0,
    }
    sig_token = jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Sig-Token": sig_token,
    }

    response = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response.status_code == 401
    assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_downstream_signature_gated_endpoint_mismatched_action() -> None:
    """
    Test that a token bound to a different action/path is rejected by downstream middleware.
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "investigator"
    change_reason = "PI Sign-off"

    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    # Token bound to different action
    payload = {
        "sub": user_id,
        "username": "test_user",
        "action": "/api/v1/execution/form-submissions/999/approve",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    sig_token = jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Sig-Token": sig_token,
    }

    response = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response.status_code == 401
    assert response.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_downstream_signature_gated_endpoint_replay_blocked() -> None:
    """
    Test that a token cannot be used twice (replay attack is blocked).
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "test_user"
    roles = "investigator"
    change_reason = "PI Sign-off"

    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )

    # Generate token
    payload = {
        "sub": user_id,
        "username": "test_user",
        "action": "/api/v1/execution/form-submissions/123/approve",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 60.0,
    }
    sig_token = jwt.encode(payload, "internal-gateway-secret-12345", algorithm="HS256")

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Sig-Token": sig_token,
    }

    # First request should pass
    response = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response.status_code == 200

    # Second request should fail with REAUTHENTICATION_REQUIRED
    response2 = client.post(
        "/api/v1/execution/form-submissions/123/approve", headers=headers
    )
    assert response2.status_code == 401
    assert response2.json()["detail"] == "REAUTHENTICATION_REQUIRED"


def test_verify_gateway_signature_scope_fallback_restrictions() -> None:
    """
    Test that legacy/no-scope signature fallback is only allowed when
    no scope variables (site_id, sponsor_id, unblinded_access) are present.
    """
    from packages.security.signing import (
        generate_gateway_signature,
        verify_gateway_signature,
    )

    secret = b"test-secret-key-12345"
    user_id = "user_001"
    roles = "investigator"
    timestamp = "1234567890"
    change_reason = "gxp signoff"

    # 1. Generate a legacy 4-key v2 signature (which does not sign scope parameters)
    import hashlib
    import hmac
    import json

    legacy_payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    legacy_serialized = json.dumps(
        legacy_payload, sort_keys=True, separators=(",", ":")
    )
    legacy_sig = hmac.new(
        secret, legacy_serialized.encode("utf-8"), hashlib.sha256
    ).hexdigest()

    # 2. Case A: Scopes are completely absent/falsy.
    # Legacy fallback should be allowed.
    assert (
        verify_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            signature=legacy_sig,
            secret=secret,
            change_reason=change_reason,
            site_id=None,
            sponsor_id=None,
            unblinded_access=False,
        )
        is True
    )

    # 3. Case B: A scope is present in the request.
    # Legacy fallback should be strictly rejected.
    assert (
        verify_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            signature=legacy_sig,
            secret=secret,
            change_reason=change_reason,
            site_id="site_active_01",
            sponsor_id=None,
            unblinded_access=False,
        )
        is False
    )

    assert (
        verify_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            signature=legacy_sig,
            secret=secret,
            change_reason=change_reason,
            site_id=None,
            sponsor_id="sponsor_active_01",
            unblinded_access=False,
        )
        is False
    )

    assert (
        verify_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            signature=legacy_sig,
            secret=secret,
            change_reason=change_reason,
            site_id=None,
            sponsor_id=None,
            unblinded_access=True,
        )
        is False
    )

    # 4. Case C: A correct 7-field scope-signed signature passes verification.
    scope_sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
        site_id="site_active_01",
        sponsor_id="sponsor_active_01",
        unblinded_access=True,
    )
    assert (
        verify_gateway_signature(
            user_id=user_id,
            roles=roles,
            timestamp=timestamp,
            signature=scope_sig,
            secret=secret,
            change_reason=change_reason,
            site_id="site_active_01",
            sponsor_id="sponsor_active_01",
            unblinded_access=True,
        )
        is True
    )


# Add endpoint to verify context propagation under test_app
@test_app.get("/verify-context-tenant")
async def verify_context_tenant():
    from packages.security.context import current_tenant_id

    return {
        "context_tenant_id": current_tenant_id.get(),
    }


def test_middleware_tenant_context_and_state() -> None:
    """Validate that the security middleware correctly extracts, verifies, binds to contextvar, and attaches tenant_id.

    Requirements: PRD-SYS-001
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "tenant_user_01"
    roles = "sponsor_designer"
    change_reason = "Design custom validation rule"
    tenant_id = "tenant_biotech_99"

    # Generate V2 signature with tenant scope included
    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        tenant_id=tenant_id,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": tenant_id,
    }

    response = client.get("/verify-context-tenant", headers=headers)
    assert response.status_code == 200
    assert response.json() == {"context_tenant_id": "tenant_biotech_99"}

    # Outside the request scope, the context variable should be reset to default (None)
    from packages.security.context import current_tenant_id

    assert current_tenant_id.get() is None


def test_middleware_tenant_missing_fallback() -> None:
    """Validate that when X-Tenant-Id is missing or empty, the middleware defaults it to 'tenant_default' and validates.

    Requirements: PRD-SYS-001
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "tenant_user_02"
    roles = "sponsor_designer"
    change_reason = "Design test case"
    # Gateway generates signature with tenant_id="tenant_default" when missing from claim
    expected_tenant = "tenant_default"

    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        tenant_id=expected_tenant,
    )

    # Do not include X-Tenant-Id header to simulate legacy or missing claim requests
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    response = client.get("/verify-context-tenant", headers=headers)
    assert response.status_code == 200
    assert response.json() == {"context_tenant_id": "tenant_default"}


def test_middleware_tenant_signature_tampering_rejected() -> None:
    """Validate that a request with tampered X-Tenant-Id header is rejected due to signature verification failure.

    Requirements: PRD-SYS-001
    """
    client = TestClient(test_app)
    timestamp = str(time.time())
    user_id = "tenant_user_03"
    roles = "sponsor_designer"
    change_reason = "Form submission"
    tenant_id = "tenant_legit_101"

    # Sign for tenant_legit_101
    sig = generate_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        version="2",
        change_reason=change_reason,
        tenant_id=tenant_id,
    )

    # Maliciously change X-Tenant-Id to another tenant ID (spoofing attempt)
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_target_202",
    }

    response = client.get("/verify-context-tenant", headers=headers)
    assert response.status_code == 401
    assert "Invalid gateway signature" in response.json()["detail"]


def test_verify_sig_token_helper_scenarios() -> None:
    """
    Test various verification outcomes directly using the extracted verify_sig_token helper.
    """
    from packages.security.middleware import downstream_replay_cache, verify_sig_token

    secret = b"test-secret-12345"
    user_id = "user_test_99"
    path = "/api/v1/quality/capas/123/transition"

    # 1. Helper to construct tokens easily
    def make_token(
        semantic_action: Optional[str] = None,
        expired: bool = False,
        wrong_action: bool = False,
        wrong_user: bool = False,
    ) -> str:
        now = time.time()
        payload = {
            "sub": "wrong_user" if wrong_user else user_id,
            "username": "test_username",
            "action": "/api/v1/wrong_path" if wrong_action else path,
            "roles": ["investigator"],
            "iat": now - 100 if expired else now,
            "exp": now - 40 if expired else now + 60,
            "jti": f"jti_test_{user_id}_{now}",
        }
        if semantic_action:
            payload["semantic_action"] = semantic_action
            payload["sig_ver"] = "v3"
        return jwt.encode(payload, secret, algorithm="HS256")

    # 2. Valid token with semantic action matching expected
    t_valid = make_token(semantic_action="quality.capa.close")
    success, res = verify_sig_token(
        sig_token=t_valid,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is True
    assert isinstance(res, dict)

    # 3. Missing token -> False
    success, err = verify_sig_token(
        sig_token=None,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
    )
    assert success is False
    assert "Re-authentication is required" in err

    # 4. Mismatched semantic action -> False
    t_mismatched = make_token(semantic_action="quality.capa.cancel")
    success, err = verify_sig_token(
        sig_token=t_mismatched,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is False
    assert "semantic action mismatch" in err

    # 5. Expired token -> False
    t_expired = make_token(semantic_action="quality.capa.close", expired=True)
    success, err = verify_sig_token(
        sig_token=t_expired,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is False
    assert "invalid" in err.lower()

    # 6. Mismatched user -> False
    t_user_mismatch = make_token(semantic_action="quality.capa.close", wrong_user=True)
    success, err = verify_sig_token(
        sig_token=t_user_mismatch,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is False
    assert "user mismatch" in err.lower()

    # 7. Replayed token -> False
    # Clear the replay cache first to be independent
    downstream_replay_cache.used_tokens.clear()
    t_replay = make_token(semantic_action="quality.capa.close")
    # First verify passes
    success, _ = verify_sig_token(
        sig_token=t_replay,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is True
    # Second verify fails (replayed)
    success, err = verify_sig_token(
        sig_token=t_replay,
        user_id=user_id,
        request_path=path,
        secret=secret,
        replay_cache=downstream_replay_cache,
        expected_semantic_action="quality.capa.close",
    )
    assert success is False
    assert "already been used" in err.lower()
