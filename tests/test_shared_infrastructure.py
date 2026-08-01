import os

import httpx
import pytest

from packages.security.signing import verify_gateway_signature


def test_signed_headers_generation(signed_headers):
    """
    Test Task 1: Add a shared signed-header factory fixture.
    Verify that signed_headers factory generates a correct v2 signature
    and emits both mandatory and conditional headers.
    """
    # 1. Test basic header generation with mandatory fields
    user_id = "user_abc"
    roles = "CRA,investigator"
    change_reason = "Initial setup of trial"
    tenant_id = "tenant_123"

    headers = signed_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        tenant_id=tenant_id,
    )

    assert headers["X-User-Id"] == user_id
    assert headers["X-User-Roles"] == roles
    assert headers["X-Change-Reason"] == change_reason
    assert headers["X-Tenant-Id"] == tenant_id
    assert headers["X-Signature-Version"] == "2"
    assert "X-Gateway-Timestamp" in headers
    assert "X-Gateway-Signature" in headers

    # These conditional headers must NOT be present
    assert "X-Site-Id" not in headers
    assert "X-Sponsor-Id" not in headers
    assert "X-Sig-Token" not in headers
    assert "X-Study-Id" not in headers
    assert "X-Unblinded-Access" not in headers

    # Verify that the generated signature is valid
    secret_env = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    secret_bytes = (
        secret_env.encode("utf-8") if isinstance(secret_env, str) else secret_env
    )

    is_valid = verify_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=headers["X-Gateway-Timestamp"],
        signature=headers["X-Gateway-Signature"],
        secret=secret_bytes,
        change_reason=change_reason,
        tenant_id=tenant_id,
    )
    assert is_valid is True

    # 2. Test conditional scope and token headers
    headers_all_options = signed_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        tenant_id=tenant_id,
        site_id="site_boston",
        sponsor_id="sponsor_acme",
        unblinded_access=True,
        sig_token="some-jwt-sig-token",
        study_id="study_999",
    )

    assert headers_all_options["X-Site-Id"] == "site_boston"
    assert headers_all_options["X-Sponsor-Id"] == "sponsor_acme"
    assert headers_all_options["X-Sig-Token"] == "some-jwt-sig-token"
    assert headers_all_options["X-Study-Id"] == "study_999"
    assert headers_all_options["X-Unblinded-Access"] == "true"

    is_valid_complex = verify_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=headers_all_options["X-Gateway-Timestamp"],
        signature=headers_all_options["X-Gateway-Signature"],
        secret=secret_bytes,
        change_reason=change_reason,
        site_id="site_boston",
        sponsor_id="sponsor_acme",
        unblinded_access=True,
        tenant_id=tenant_id,
        sig_token="some-jwt-sig-token",
    )
    assert is_valid_complex is True

    # 3. Test tamper mode
    headers_tampered = signed_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
        tenant_id="tenant_actual",
        tamper_tenant_id="tenant_different",
    )

    # Sent header should be the tenant_id ("tenant_actual")
    assert headers_tampered["X-Tenant-Id"] == "tenant_actual"

    # Signature must fail verification against the sent tenant_id ("tenant_actual")
    is_valid_actual = verify_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=headers_tampered["X-Gateway-Timestamp"],
        signature=headers_tampered["X-Gateway-Signature"],
        secret=secret_bytes,
        change_reason=change_reason,
        tenant_id="tenant_actual",
    )
    assert is_valid_actual is False

    # Signature must succeed when verified against the tampered tenant_id ("tenant_different")
    is_valid_different = verify_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=headers_tampered["X-Gateway-Timestamp"],
        signature=headers_tampered["X-Gateway-Signature"],
        secret=secret_bytes,
        change_reason=change_reason,
        tenant_id="tenant_different",
    )
    assert is_valid_different is True


@pytest.mark.asyncio
async def test_cross_service_interception_and_replay(
    capture_cross_service_calls, execution_client
):
    """
    Test Task 3: Add the cross-service interception fixture.
    Verify that our capture_cross_service_calls fixture successfully intercepts
    outbound requests, populates details (method, path, headers, body, json),
    and replays them correctly.
    """
    # 1. Trigger an outbound request using httpx.AsyncClient
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8001/api/v1/studies/study_123/eligibility-criteria",
            json={"foo": "bar"},
            headers={"X-Custom-Header": "hello"},
        )

    # The intercepted response should be our default mock response
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}

    # Verify captured details
    assert len(capture_cross_service_calls.calls) == 1
    captured = capture_cross_service_calls.calls[0]

    assert captured["method"] == "POST"
    assert (
        captured["url"]
        == "http://localhost:8001/api/v1/studies/study_123/eligibility-criteria"
    )
    assert captured["path"] == "/api/v1/studies/study_123/eligibility-criteria"
    assert captured["headers"]["X-Custom-Header"] == "hello"
    assert captured["json"] == {"foo": "bar"}

    # 2. Test replay helper against a downstream ASGI client (e.g. execution_client)
    # Clear capture calls so we can trace the replay request cleanly
    capture_cross_service_calls.clear()

    # We will configure passthrough=True for the replay request so it hits execution_client's real router,
    # or we can test execution_client's health endpoint (which is open).
    # Health endpoint doesn't require signature.
    health_captured_call = {
        "method": "GET",
        "path": "/health",
        "headers": {},
        "json": None,
        "body": None,
    }

    # Ensure passthrough is True so the call is actually forwarded to the target client
    capture_cross_service_calls.passthrough = True

    response = await capture_cross_service_calls.replay(
        execution_client, health_captured_call
    )
    assert response.status_code == 200
    assert response.json()["status"] in ("ok", "healthy", "UP")


@pytest.mark.asyncio
async def test_service_client_fixtures_isolation(
    shared_sqlite_dbs, execution_client, etmf_client, designer_client, signed_headers
):
    """
    Test Task 2: Add in-process ASGI client fixtures for each service.
    Verify that all ASGI clients (execution, etmf, designer) are accessible,
    respond to health checks, and validate signed headers correctly.
    """
    # Each client's /health endpoint should respond successfully
    for client in (execution_client, etmf_client, designer_client):
        resp = await client.get("/health")
        assert resp.status_code == 200

    # Ensure that gated endpoints on each microservice return 401/403 when missing signatures
    resp_exec_gated = await execution_client.post("/api/v1/execution/sdv/signoff")
    assert resp_exec_gated.status_code in (401, 403)

    resp_etmf_gated = await etmf_client.get("/api/v1/etmf/documents")
    assert resp_etmf_gated.status_code in (401, 403)

    resp_designer_gated = await designer_client.get("/api/v1/studies")
    assert resp_designer_gated.status_code in (401, 403)

    # Verify that correct signed headers allow access through the GatewayAuthMiddleware
    headers_exec = signed_headers(
        user_id="user_cra", roles="CRA", change_reason="CRA operation"
    )
    resp_exec_gated_signed = await execution_client.post(
        "/api/v1/execution/sdv/signoff",
        json={
            "scope": "FIELD",
            "target_id": "OBS_NONEXISTENT",
            "subject_id": "SUBJ_NONEXISTENT",
            "study_id": "STUDY_NONEXISTENT",
        },
        headers=headers_exec,
    )
    # Signature is valid, but the target subject/observation doesn't exist, so it should return 404 (not 401/403)
    assert resp_exec_gated_signed.status_code == 404
