"""
Tests for the Metadata-Driven Path-to-Schema Registry and validation middleware.

Requirements: Trace 15
"""

import hashlib
import hmac
import time
from typing import Any, Dict

from fastapi import FastAPI
from fastapi.testclient import TestClient
from jose import jwt

from packages.security.middleware import PATH_SCHEMA_REGISTRY, GatewayAuthMiddleware

GATEWAY_SECRET = "internal-gateway-secret-12345"

# Setup a test app wrapped in GatewayAuthMiddleware
test_app = FastAPI()
test_app.add_middleware(GatewayAuthMiddleware)


@test_app.post("/api/v1/execution/batch-sign-off")
async def execution_batch_sign_off_mock() -> Dict[str, str]:
    return {"status": "success", "endpoint": "edc"}


@test_app.post("/api/v1/etmf/batch-sign-off")
async def etmf_batch_sign_off_mock() -> Dict[str, str]:
    return {"status": "success", "endpoint": "etmf"}


@test_app.post("/some/unmapped/batch-sign-off/route")
async def unmapped_batch_sign_off_mock() -> Dict[str, str]:
    return {"status": "success", "endpoint": "unmapped"}


def get_auth_headers_for_test(
    user_id: str = "test_user",
    roles: str = "admin",
    change_reason: str = "system_operation",
    path: str = "/api/v1/execution/batch-sign-off",
    payload: Any = None,
) -> Dict[str, str]:
    """Helper to generate Gateway signature-compliant headers for testing."""
    timestamp = str(time.time())
    header_payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    import json

    serialized = json.dumps(header_payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    if path:
        sig_payload = {
            "sub": user_id,
            "username": "test_user",
            "action": path,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }

        # Resolve keys from registry
        schema_keys = None
        for reg_path, keys in PATH_SCHEMA_REGISTRY.items():
            if reg_path.lower() in path.lower():
                schema_keys = keys
                break

        if schema_keys is None and "batch-sign-off" in path.lower():
            schema_keys = ["study_id", "target_type", "target_ids", "signing_reason"]

        if schema_keys is not None and payload:
            norm_vals = []
            for key in schema_keys:
                val = payload.get(key)
                if key == "target_type":
                    norm_val = str(val).strip().upper() if val is not None else ""
                elif isinstance(val, list):
                    sorted_items = sorted([str(item).strip() for item in val])
                    norm_val = ",".join(sorted_items)
                else:
                    norm_val = str(val).strip() if val is not None else ""
                norm_vals.append(norm_val)

            binding_str = ":".join(norm_vals)
            batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()
            sig_payload["batch_id"] = batch_id

        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token

    return headers


def test_edc_batch_sign_off_happy_path() -> None:
    """Validate EDC batch signing processes successfully when payload contains EDC schema keys.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/api/v1/execution/batch-sign-off"
    payload = {
        "study_id": "STUDY-001",
        "target_type": "FORM",
        "target_ids": ["id-1", "id-2"],
        "signing_reason": "Clinical trial data approval",
    }
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 200
    assert response.json() == {"status": "success", "endpoint": "edc"}


def test_etmf_batch_sign_off_happy_path() -> None:
    """Validate eTMF batch signing processes successfully when payload contains eTMF schema keys.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/api/v1/etmf/batch-sign-off"
    payload = {
        "document_ids": ["doc-abc", "doc-def"],
        "signing_reason": "eTMF archival validation",
    }
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 200
    assert response.json() == {"status": "success", "endpoint": "etmf"}


def test_edc_batch_sign_off_missing_fields_rejected() -> None:
    """Validate EDC batch signing is rejected with HTTP 400 when missing clinical fields.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/api/v1/execution/batch-sign-off"
    payload = {
        "study_id": "STUDY-001",
        "target_type": "FORM",
        # missing target_ids and signing_reason
    }
    # Create valid headers for whatever the token binding resolves, but middleware will reject payload first
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 400
    assert (
        "Missing batch sign-off fields for validation: target_ids, signing_reason"
        in response.json()["message"]
    )


def test_etmf_batch_sign_off_missing_fields_rejected() -> None:
    """Validate eTMF batch signing is rejected with HTTP 400 when missing eTMF fields.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/api/v1/etmf/batch-sign-off"
    payload = {
        "signing_reason": "eTMF archival validation",
        # missing document_ids
    }
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 400
    assert (
        "Missing batch sign-off fields for validation: document_ids"
        in response.json()["message"]
    )


def test_unmapped_batch_sign_off_fallback() -> None:
    """Validate unmapped batch-sign-off paths fallback to the default EDC clinical trial schema.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/some/unmapped/batch-sign-off/route"
    payload = {
        "study_id": "STUDY-001",
        "target_type": "FORM",
        "target_ids": ["id-1"],
        "signing_reason": "Fallback testing",
    }
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 200
    assert response.json() == {"status": "success", "endpoint": "unmapped"}


def test_unmapped_batch_sign_off_fallback_rejected_on_missing_fields() -> None:
    """Validate unmapped batch-sign-off paths rejecting payloads missing clinical fields.

    Requirements: Trace 15
    """
    client = TestClient(test_app)
    path = "/some/unmapped/batch-sign-off/route"
    payload = {
        "study_id": "STUDY-001",
        # missing target_type, target_ids, and signing_reason
    }
    headers = get_auth_headers_for_test(path=path, payload=payload)
    response = client.post(path, json=payload, headers=headers)
    assert response.status_code == 400
    assert (
        "Missing batch sign-off fields for validation: target_type, target_ids, signing_reason"
        in response.json()["message"]
    )
