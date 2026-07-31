"""Integration test suite for Principal Investigator batch eSignature REST API.

Requirements: PRD-SYS-001
"""

import hashlib
import os
import time
from typing import Optional

from fastapi.testclient import TestClient
from jose import jwt

import packages  # noqa: F401
from apps.execution.main import app
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode(
    "utf-8"
)


def _make_auth_headers(
    user_id: str = "pi_user_101",
    roles: str = "principal_investigator",
    change_reason: str = "PI Casebook Approval",
    action: str = "/api/v1/execution/signatures/batch-sign-off",
    payload: Optional[dict] = None,
) -> dict:
    """Generate signed Gateway authentication headers with X-Sig-Token and batch binding for testing."""
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
        "jti": f"jti_{time.time()}_{user_id}",
    }

    if payload and payload.get("target_ids") is not None:
        norm_study = str(payload.get("study_id", "")).strip()
        norm_type = str(payload.get("target_type", "FORM")).strip().upper()
        target_ids = payload.get("target_ids", [])
        sorted_ids = sorted([str(tid).strip() for tid in target_ids])
        norm_ids = ",".join(sorted_ids)
        norm_reason = str(payload.get("signing_reason", "")).strip()
        binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
        batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
        sig_payload["batch_id"] = batch_id

    sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
    headers["X-Sig-Token"] = sig_token
    return headers


def test_batch_signature_sign_off_success() -> None:
    """Validate POST /api/v1/execution/signatures/batch-sign-off executes PI casebook sign-off.

    Requirements: PRD-SYS-001
    """
    req_body = {
        "study_id": "study_sig_001",
        "subject_id": "sub_sig_101",
        "target_type": "FORM",
        "target_ids": ["form_vs_01", "form_lb_01", "form_ae_01"],
        "target_form_ids": ["form_vs_01", "form_lb_01", "form_ae_01"],
        "signing_reason": "I approve the accuracy and completeness of this casebook",
        "password": "SecretPassword123!",  # pragma: allowlist secret
        "printed_name": "Dr. Alice Smith, MD",
    }

    headers = _make_auth_headers(
        user_id="pi_user_101",
        roles="principal_investigator",
        change_reason="PI Casebook Approval",
        payload=req_body,
    )

    response = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )

    assert response.status_code == 201
    data = response.json()
    assert data["study_id"] == "study_sig_001"
    assert data["subject_id"] == "sub_sig_101"
    assert data["signed_forms_count"] == 3
    assert "signature_id" in data
    assert "content_digest" in data
    assert "audit_tx" in data


def test_batch_signature_missing_password_returns_400() -> None:
    """Validate sign-off without password re-authentication returns 400 Bad Request.

    Requirements: PRD-SYS-001
    """
    req_body = {
        "study_id": "study_sig_002",
        "subject_id": "sub_sig_102",
        "target_type": "FORM",
        "target_ids": ["form_vs_01"],
        "target_form_ids": ["form_vs_01"],
        "signing_reason": "Sign-off",
        "password": "",
        "printed_name": "Dr. Bob",
    }

    headers = _make_auth_headers(user_id="pi_user_102", payload=req_body)

    response = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )

    assert response.status_code == 400
    assert "Re-authentication password is required" in response.json()["detail"]


def test_batch_signature_empty_target_forms_returns_400() -> None:
    """Validate sign-off without target form IDs returns 400 Bad Request.

    Requirements: PRD-SYS-001
    """
    req_body = {
        "study_id": "study_sig_003",
        "subject_id": "sub_sig_103",
        "target_type": "FORM",
        "target_ids": [],
        "target_form_ids": [],
        "signing_reason": "Sign-off",
        "password": "Password123!",  # pragma: allowlist secret
        "printed_name": "Dr. Bob",
    }

    headers = _make_auth_headers(user_id="pi_user_103", payload=req_body)

    response = client.post(
        "/api/v1/execution/signatures/batch-sign-off",
        json=req_body,
        headers=headers,
    )

    assert response.status_code == 400
    assert (
        "At least one target eCRF form ID must be provided" in response.json()["detail"]
    )
