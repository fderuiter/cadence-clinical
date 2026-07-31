import hashlib
import os
import time
from typing import Optional

import pytest
from fastapi import HTTPException
from fastapi.testclient import TestClient
from jose import jwt

import packages  # noqa: F401
from apps.execution.main import app
from packages.security.sig_token_verifier import (
    token_consumption_cache,
    verify_and_consume_sig_token,
)

GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)
client = TestClient(app)


@pytest.fixture(autouse=True)
def clear_consumption_cache():
    """Clear the token consumption cache before/after each test."""
    token_consumption_cache.reset()
    yield
    token_consumption_cache.reset()


def _make_jwt(
    sub: str = "user1",
    username: str = "user1",
    action: str = "/api/v1/execution/signatures/batch-sign-off",
    roles: list = None,
    iat: Optional[float] = None,
    exp: Optional[float] = None,
    jti: str = "unique_jti_123",
    batch_id: Optional[str] = None,
) -> str:
    now = time.time()
    payload = {
        "sub": sub,
        "username": username,
        "action": action,
        "roles": roles or ["principal_investigator"],
        "iat": iat or now,
        "exp": exp or (now + 60.0),
        "jti": jti,
    }
    if batch_id:
        payload["batch_id"] = batch_id
    return jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")


def test_helper_happy_path():
    """Verify that a valid token is successfully verified and consumed."""
    token = _make_jwt()
    payload = verify_and_consume_sig_token(
        sig_token=token,
        user_id="user1",
        request_path="/api/v1/execution/signatures/batch-sign-off",
    )
    assert payload["sub"] == "user1"
    assert payload["jti"] == "unique_jti_123"


def test_helper_replay_prevention():
    """Verify that a token can be used exactly once and a replay is blocked."""
    token = _make_jwt(jti="replay_jti")

    # First consumption succeeds
    verify_and_consume_sig_token(
        sig_token=token,
        user_id="user1",
        request_path="/api/v1/execution/signatures/batch-sign-off",
    )

    # Second consumption raises HTTPException 401
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(
            sig_token=token,
            user_id="user1",
            request_path="/api/v1/execution/signatures/batch-sign-off",
        )
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_helper_mismatched_user():
    """Verify that a token for another user is rejected."""
    token = _make_jwt(sub="user2")
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(
            sig_token=token,
            user_id="user1",
            request_path="/api/v1/execution/signatures/batch-sign-off",
        )
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_helper_mismatched_action_path():
    """Verify that a token bound to a different action path is rejected."""
    token = _make_jwt(action="/api/v1/execution/other-action")
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(
            sig_token=token,
            user_id="user1",
            request_path="/api/v1/execution/signatures/batch-sign-off",
        )
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_helper_expired_token():
    """Verify that an expired token is rejected."""
    now = time.time()
    token = _make_jwt(iat=now - 120.0, exp=now - 60.0)
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(
            sig_token=token,
            user_id="user1",
            request_path="/api/v1/execution/signatures/batch-sign-off",
        )
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def test_helper_batch_id_binding_success():
    """Verify that correct batch_id binding is successfully verified."""
    payload_dict = {
        "study_id": "STUDY-1",
        "target_type": "FORM",
        "target_form_ids": ["F1", "F2"],
        "signing_reason": "PI approval.",
    }
    binding_str = "STUDY-1:FORM:F1,F2:PI approval."
    batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

    token = _make_jwt(batch_id=batch_id)
    payload = verify_and_consume_sig_token(
        sig_token=token,
        user_id="user1",
        request_path="/api/v1/execution/signatures/batch-sign-off",
        payload_dict=payload_dict,
    )
    assert payload["batch_id"] == batch_id


def test_helper_batch_id_binding_mismatch():
    """Verify that mismatched batch_id binding is rejected."""
    payload_dict = {
        "study_id": "STUDY-1",
        "target_type": "FORM",
        "target_form_ids": ["F1", "F2"],
        "signing_reason": "PI approval.",
    }
    binding_str = "STUDY-1:FORM:F1,F2:PI approval."
    batch_id_correct = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

    # Token with mismatched/wrong batch_id
    token = _make_jwt(batch_id="wrong_batch_id_hash_1234567890")
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(
            sig_token=token,
            user_id="user1",
            request_path="/api/v1/execution/signatures/batch-sign-off",
            payload_dict=payload_dict,
        )
    assert exc_info.value.status_code == 401
    assert exc_info.value.detail == "REAUTHENTICATION_REQUIRED"


def _make_auth_headers(
    user_id: str = "pi_user_101",
    roles: str = "principal_investigator",
    change_reason: str = "PI Casebook Approval",
    action: str = "/api/v1/execution/signatures/batch-sign-off",
    payload: dict = None,
    jti: str = "unique_jti_endpoint",
) -> dict:
    from packages.security.signing import generate_gateway_signature

    timestamp = str(time.time())
    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id="tenant_default",
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }

    sig_payload = {
        "sub": user_id,
        "username": user_id,
        "action": action,
        "semantic_action": "execution.form.signoff",
        "roles": [roles],
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": jti,
    }

    if payload:
        norm_study = str(payload.get("study_id", "")).strip()
        norm_type = str(payload.get("target_type", "FORM")).strip().upper()
        target_ids = payload.get("target_ids") or payload.get("target_form_ids") or []
        sorted_ids = sorted([str(tid).strip() for tid in target_ids])
        norm_ids = ",".join(sorted_ids)
        norm_reason = str(payload.get("signing_reason", "")).strip()
        binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
        batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
        sig_payload["batch_id"] = batch_id

    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    headers["X-Sig-Token"] = sig_token
    return headers


def test_endpoint_missing_token():
    """Verify that calling the endpoint without X-Sig-Token header is rejected with 401."""
    req_body = {
        "study_id": "study_sig_001",
        "subject_id": "sub_sig_101",
        "target_type": "FORM",
        "target_ids": ["form_vs_01"],
        "target_form_ids": ["form_vs_01"],
        "signing_reason": "I approve the accuracy.",
        "password": "Password123!",  # pragma: allowlist secret
        "printed_name": "Dr. Smith",
    }
    headers = _make_auth_headers(payload=req_body)
    headers.pop("X-Sig-Token", None)

    response = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )
    assert response.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in response.json()["detail"]


def test_endpoint_replay_protection():
    """Verify that calling the endpoint with a replayed X-Sig-Token is rejected with 401."""
    req_body = {
        "study_id": "study_sig_001",
        "subject_id": "sub_sig_101",
        "target_type": "FORM",
        "target_ids": ["form_vs_01"],
        "target_form_ids": ["form_vs_01"],
        "signing_reason": "I approve the accuracy.",
        "password": "Password123!",  # pragma: allowlist secret
        "printed_name": "Dr. Smith",
    }

    # First request using headers
    headers = _make_auth_headers(payload=req_body, jti="endpoint_replay_jti")
    response1 = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )
    assert response1.status_code == 201

    # Second request with identical headers/token should fail
    response2 = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )
    assert response2.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in response2.json()["detail"]
