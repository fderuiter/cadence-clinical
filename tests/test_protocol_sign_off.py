import pytest
import httpx
import time
from unittest.mock import AsyncMock, MagicMock, patch
from jose import jwt
from apps.designer.main import app
from tests.test_study_versions import get_auth_headers
from apps.designer.db import MOCK_STUDY_VERSIONS, MOCK_STUDIES
from apps.designer.delta import ImmutabilityViolationError

@pytest.mark.asyncio
async def test_protocol_sign_off_requires_sig_token():
    """
    Verify that ordinary sessions without an X-Sig-Token get rejected with REAUTHENTICATION_REQUIRED.
    Verify that valid signature tokens allow approval, write manifestation to Actions, and lock the protocol.
    """
    study_id = "study_signoff_test"
    version_id = "v_signoff_1"

    # Setup mock study and version
    import copy
    MOCK_STUDY_VERSIONS[study_id] = []
    MOCK_STUDIES[study_id] = copy.deepcopy(MOCK_STUDIES["study_1"])
    MOCK_STUDIES[study_id]["study_id"] = study_id

    from apps.designer.db import create_mock_study_version
    create_mock_study_version(study_id, {
        "id": version_id,
        "version_tag": "1.0",
        "status": "DRAFT",
        "version_index": 1,
        "created_by": "designer_user"
    })

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Try without X-Sig-Token -> fails with 401 and REAUTHENTICATION_REQUIRED
        headers = get_auth_headers()
        res = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/approve",
            json={"signing_reason": "APPROVAL"},
            headers=headers,
        )
        assert res.status_code == 401
        assert res.json()["detail"] == "REAUTHENTICATION_REQUIRED"
        assert res.json()["error"] == "REAUTHENTICATION_REQUIRED"

        # 2. Try with a valid signature token
        now = time.time()
        sig_token_payload = {
            "sub": "test_designer",
            "username": "test_designer",
            "action": f"/api/v1/studies/{study_id}/versions/{version_id}/approve",
            "roles": ["study_designer"],
            "iat": now,
            "exp": now + 60,
            "jti": "some-uuid"
        }
        sig_token = jwt.encode(sig_token_payload, "internal-gateway-secret-12345", algorithm="HS256")

        headers_with_token = get_auth_headers()
        headers_with_token["X-Sig-Token"] = sig_token

        res_approved = await client.post(
            f"/api/v1/studies/{study_id}/versions/{version_id}/approve",
            json={"signing_reason": "APPROVAL"},
            headers=headers_with_token,
        )
        assert res_approved.status_code == 200
        data = res_approved.json()
        assert data["status"] == "success"
        assert data["protocol_status"] == "APPROVED"
        assert "signature_manifestation" in data
        assert data["signature_manifestation"]["signer_id"] == "test_designer"

        # 3. Check that it is traceable in action history
        assert len(MOCK_STUDIES[study_id]["actions"]) > 0
        signoff_action = MOCK_STUDIES[study_id]["actions"][-1]
        assert signoff_action["type"] == "SIGN_OFF"
        assert "signature_manifestation" in signoff_action

        # 4. Check that post-approval modifications are strictly rejected with 403 IMMUTABILITY_VIOLATION
        res_fail = await client.post(
            f"/api/v1/studies/{study_id}/rules",
            json={
                "type": "skip_logic",
                "condition": {
                    "type": "comparison",
                    "operator": "==",
                    "operands": [
                        {"type": "constant", "value": "A"},
                        {"type": "constant", "value": "B"},
                    ]
                },
                "action": "hide",
                "target_field": "act_2",
            },
            headers=get_auth_headers(),
        )
        assert res_fail.status_code == 403
        assert "IMMUTABILITY_VIOLATION" in res_fail.json()["detail"]


@pytest.mark.asyncio
async def test_protocol_sign_off_archival_to_etmf():
    """
    Tests that the background task attempts to archive the protocol sign-off artifact to eTMF.
    """
    from apps.designer.main import archive_protocol_to_etmf

    mock_manifest = {
        "signer_id": "test_designer",
        "signing_reason": "APPROVAL",
        "ip_address": "127.0.0.1",
        "user_agent": "Mozilla",
        "sha256_hash": "dummy_hash"
    }

    # Mock response from eTMF service
    with patch("httpx.AsyncClient.post") as mock_post:
        mock_resp = MagicMock()
        mock_resp.status_code = 201
        mock_resp.json.return_value = {"status": "success", "document_id": "doc_etmf_1"}
        mock_post.return_value = mock_resp

        success = await archive_protocol_to_etmf("study_signoff_test", "1.0", mock_manifest)
        assert success is True
        mock_post.assert_called_once()
        called_args, called_kwargs = mock_post.call_args
        assert "PROTOCOL_SIGNOFF" in called_kwargs["json"]["artifact_type"]
        assert called_kwargs["json"]["study_id"] == "study_signoff_test"
