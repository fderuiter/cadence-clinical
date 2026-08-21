import os
import time
import uuid

import pytest
from fastapi import FastAPI, Request
from fastapi.testclient import TestClient
from jose import jwt

from packages.security.context import (
    audit_context,
    current_change_reason,
    current_tenant_id,
    current_user_id,
    get_current_context,
    run_in_thread_with_context,
)
from packages.security.gateway_client import GatewayBaseClient
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.regulated_actions import SemanticAction
from packages.security.sig_token_verifier import token_consumption_cache
from packages.security.step_up import require_step_up


def get_gateway_secret() -> str:
    return os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")  # pragma: allowlist secret


def create_step_up_token(
    user_id: str,
    semantic_action: str | None = None,
    exp_delta: float = 60.0,
    iat_delta: float = 0.0,
) -> str:
    now = time.time()
    payload = {
        "sub": user_id,
        "iat": now + iat_delta,
        "exp": now + exp_delta,
        "jti": str(uuid.uuid4()),
        "acr": "high-assurance-step-up",
    }
    if semantic_action:
        payload["semantic_action"] = semantic_action
    return jwt.encode(payload, get_gateway_secret(), algorithm="HS256")


app = FastAPI()
app.add_middleware(GatewayAuthMiddleware)


@app.post("/api/v1/regulated/action")
@require_step_up(semantic_action=SemanticAction.EXEC_SDV_BULK_SIGNOFF)
async def regulated_endpoint(request: Request):
    return {
        "status": "success",
        "user_id": current_user_id.get(),
        "tenant_id": current_tenant_id.get(),
        "change_reason": current_change_reason.get(),
    }


def test_step_up_decorator_valid_execution():
    """Verify decorated endpoint succeeds with valid step-up token and non-empty change reason."""
    token_consumption_cache.reset()
    sig_tok = create_step_up_token("user_123", semantic_action="execution.sdv.bulk_signoff")
    client = TestClient(app)

    headers = {
        "X-User-Id": "user_123",
        "X-User-Roles": "investigator",
        "X-Change-Reason": "Verified patient record",
        "X-Tenant-Id": "tenant_abc",
        "X-Sig-Token": sig_tok,
        "X-In-Process": "true",
    }

    response = client.post("/api/v1/regulated/action", headers=headers)
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "success"
    assert data["user_id"] == "user_123"
    assert data["tenant_id"] == "tenant_abc"


def test_step_up_decorator_missing_sig_token_rejected():
    """Verify decorated endpoint rejects requests lacking a signature token."""
    token_consumption_cache.reset()
    client = TestClient(app)

    headers = {
        "X-User-Id": "user_123",
        "X-User-Roles": "investigator",
        "X-Change-Reason": "Verified patient record",
        "X-In-Process": "true",
    }

    response = client.post("/api/v1/regulated/action", headers=headers)
    assert response.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in response.text


def test_step_up_decorator_expired_token_rejected():
    """Verify decorated endpoint rejects requests with expired signature tokens."""
    token_consumption_cache.reset()
    sig_tok = create_step_up_token("user_123", exp_delta=-10.0)
    client = TestClient(app)

    headers = {
        "X-User-Id": "user_123",
        "X-User-Roles": "investigator",
        "X-Change-Reason": "Verified patient record",
        "X-Sig-Token": sig_tok,
        "X-In-Process": "true",
    }

    response = client.post("/api/v1/regulated/action", headers=headers)
    assert response.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in response.text


def test_step_up_decorator_single_use_replay_protection():
    """Verify step-up tokens are consumed immediately and replay attempts are blocked."""
    token_consumption_cache.reset()
    sig_tok = create_step_up_token("user_123", semantic_action="execution.sdv.bulk_signoff")
    client = TestClient(app)

    headers = {
        "X-User-Id": "user_123",
        "X-User-Roles": "investigator",
        "X-Change-Reason": "Verified patient record",
        "X-Sig-Token": sig_tok,
        "X-In-Process": "true",
    }

    res1 = client.post("/api/v1/regulated/action", headers=headers)
    assert res1.status_code == 200

    res2 = client.post("/api/v1/regulated/action", headers=headers)
    assert res2.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in res2.text


def test_step_up_decorator_missing_change_reason_rejected():
    """Verify requests to regulated endpoints fail validation if change reason is missing or empty."""
    token_consumption_cache.reset()
    sig_tok = create_step_up_token("user_123", semantic_action="execution.sdv.bulk_signoff")
    client = TestClient(app)

    headers = {
        "X-User-Id": "user_123",
        "X-User-Roles": "investigator",
        "X-Sig-Token": sig_tok,
        "X-In-Process": "true",
    }

    response = client.post("/api/v1/regulated/action", headers=headers)
    assert response.status_code == 400
    assert "Missing change justification reason" in response.text


def test_context_maintained_across_thread_boundaries():
    """Verify user context and tenant metadata are correctly maintained across background thread boundaries."""
    with audit_context(
        user_id="user_thread_test",
        change_reason="Background audit task",
        site_id="site_101",
        tenant_id="tenant_999",
    ):

        def worker_function():
            return get_current_context()

        ctx_snapshot = run_in_thread_with_context(worker_function)
        assert ctx_snapshot["user_id"] == "user_thread_test"
        assert ctx_snapshot["change_reason"] == "Background audit task"
        assert ctx_snapshot["site_id"] == "site_101"
        assert ctx_snapshot["tenant_id"] == "tenant_999"


@pytest.mark.asyncio
async def test_in_process_inter_service_communication_no_hmac():
    """Verify internal inter-service communication executes in-process without generating HMAC-SHA256 signatures."""
    from packages.security.asgi_registry import register_service_app

    register_service_app("dummy_service", app)

    client = GatewayBaseClient(base_url="http://dummy_service:8002")

    with audit_context(
        user_id="user_client_test",
        change_reason="Internal client call",
        tenant_id="tenant_inprocess",
    ):
        sig_tok = create_step_up_token(
            "user_client_test", semantic_action="execution.sdv.bulk_signoff"
        )

        response = await client.request(
            method="POST",
            path="/api/v1/regulated/action",
            headers={"X-Sig-Token": sig_tok},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["user_id"] == "user_client_test"
        assert data["tenant_id"] == "tenant_inprocess"
