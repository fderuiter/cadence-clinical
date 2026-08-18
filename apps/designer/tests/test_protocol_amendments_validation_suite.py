import copy
import hashlib
import hmac
import json
import time

import httpx
import pytest
from jose import jwt

# First-party imports
from apps.designer.db import (
    MOCK_STUDIES,
    MOCK_STUDY_VERSIONS,
)
from apps.designer.main import app as designer_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_designer_auth_headers(
    user_id="test_designer",
    roles="STUDY_DESIGNER",
    change_reason="Study versioning operations",
    action_path=None,
    sig_token_custom=None,
):
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
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
    if sig_token_custom:
        headers["X-Sig-Token"] = sig_token_custom
    elif action_path:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action_path,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": f"jti-{time.time()}-{hash(action_path)}-{time.process_time()}",
        }
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")
        headers["X-Sig-Token"] = sig_token
    return headers


# =====================================================================
# 1. DESIGNER VERSION & AMENDMENT VALIDATION (PRD-MDR-002)
# =====================================================================


@pytest.mark.asyncio
async def test_designer_amendment_immutability_and_race_safety():
    """
    Validate that study designs are properly frozen/LOCKED and that
    race safety / concurrency conflicts prevent parallel updates/duplicate creations.

    Requirements: PRD-MDR-002
    """
    study_id = "isolation_race_amend_study"
    MOCK_STUDY_VERSIONS[study_id] = []

    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        res_v1 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_draft",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_v1.status_code == 201

        res_dup = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_draft_dup",
                "version_tag": "1.0",
                "status": "DRAFT",
                "version_index": 1,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_dup.status_code == 409
        assert "CONCURRENT_LOCKING" in str(res_dup.json())

        res_v2 = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_locked",
                "version_tag": "2.0",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_v2.status_code == 201

        rule_payload = {
            "type": "skip_logic",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {"type": "field_ref", "field_ref": {"field_id": "act_1"}},
                    {"type": "constant", "value": "N"},
                ],
            },
            "action": "hide",
            "target_field": "act_2",
        }
        res_fail_rule = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json=rule_payload,
            headers=get_designer_auth_headers(),
        )
        assert res_fail_rule.status_code in (403, 409)
        assert "IMMUTABILITY_VIOLATION" in res_fail_rule.json()["detail"]


@pytest.mark.asyncio
async def test_designer_amendment_signature_validation():
    """
    Validate that loading, amending, or upgrading a study design version enforces
    valid canonical payload signatures and strictly rejects tampered/un-signed records.

    Requirements: PRD-MDR-002
    """
    study_id = "signature_tamper_test_study"
    MOCK_STUDY_VERSIONS[study_id] = []

    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        res_parent = await client.post(
            f"/api/v1/studies/{study_id}/versions",
            json={
                "id": "v_parent_sig",
                "version_tag": "2.1",
                "status": "LOCKED",
                "version_index": 2,
            },
            headers=get_designer_auth_headers(),
        )
        assert res_parent.status_code == 201

        assert len(MOCK_STUDY_VERSIONS[study_id]) > 0
        MOCK_STUDY_VERSIONS[study_id][0]["signature"] = "tampered-signature-invalid-1"

        res_amend = await client.post(
            f"/api/designer/protocols/{study_id}/amend",
            json={"amendment_type": "clinical-amendment"},
            headers=get_designer_auth_headers(),
        )
        assert res_amend.status_code in (400, 409)
        assert any(
            k in str(res_amend.json())
            for k in ("INVALID_SIGNATURE", "INVALID_OR_MISSING_SIGNATURE", "409")
        )
