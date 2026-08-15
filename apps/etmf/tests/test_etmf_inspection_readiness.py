"""Unit and integration tests for eTMF Inspection Readiness Assessment and Quality Scoring.

Validates multi-dimensional readiness scoring, milestone breakdown, zone distribution,
and electronic signature verification.
"""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from jose import jwt

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base
from apps.etmf.main import app
from packages.testing.security import generate_signature

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


@pytest.fixture(autouse=True)
def allow_legacy_signatures_for_this_suite(monkeypatch):
    monkeypatch.setenv("ALLOW_LEGACY_MOCK_SIGNATURES", "true")


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    roles: str = "admin",
    change_reason: str = "",
    action_path: str | None = None,
) -> dict:
    timestamp = str(time.time())
    user_id = "test_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    if action_path:
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


@pytest.mark.asyncio
async def test_inspection_readiness_endpoint_and_scoring():
    """Test retrieving inspection readiness score and detailed quality metrics."""
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Setup inspection readiness",
    )
    inspector_headers = get_auth_headers(roles="regulatory_inspector,auditor")

    study_id = "STUDY-READINESS-1"

    # 1. Seed EDL for study
    seed_resp = client.post(
        f"/api/v1/etmf/studies/{study_id}/seed-edl",
        json={
            "milestones": ["INITIATION", "CONDUCT"],
            "reason_for_change": "Setting up baseline EDL",
        },
        headers=admin_headers,
    )
    assert seed_resp.status_code == 201

    # 2. Query inspection readiness initially (0 documents uploaded)
    resp_init = client.get(
        f"/api/v1/etmf/studies/{study_id}/inspection-readiness",
        headers=inspector_headers,
    )
    assert resp_init.status_code == 200
    data_init = resp_init.json()
    assert data_init["study_id"] == study_id
    assert data_init["total_documents"] == 0
    assert data_init["total_expected"] > 0
    assert data_init["overall_readiness_score"] < 50.0
    assert data_init["readiness_rating"] in ("REQUIRES_ATTENTION", "CRITICAL_GAPS")
    assert len(data_init["zones"]) == 11
    assert len(data_init["milestones"]) >= 2
    assert len(data_init["action_items"]) > 0

    # 3. Ingest Protocol and approve it
    ingest_resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": study_id,
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_v1.pdf",
            "content": "Protocol content for readiness test.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Transition to APPROVED: DRAFT -> TECHNICAL_QC -> CLINICAL_QC -> APPROVED
    t1 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "TECHNICAL_QC",
            "reason_for_change": "Initial technical QC submission",
        },
        headers=admin_headers,
    )
    assert t1.status_code == 200

    clinical_headers = get_auth_headers(
        roles="admin,sponsor_clinical", change_reason="Clinical QC pass"
    )
    t2 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={
            "to_status": "CLINICAL_QC",
            "reason_for_change": "Clinical QC review passed",
        },
        headers=clinical_headers,
    )
    assert t2.status_code == 200

    t3 = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"to_status": "APPROVED", "reason_for_change": "Final QC approval passed"},
        headers=admin_headers,
    )
    assert t3.status_code == 200

    # 4. Check readiness score again (should improve)
    resp_updated = client.get(
        f"/api/v1/etmf/studies/{study_id}/inspection-readiness",
        headers=inspector_headers,
    )
    assert resp_updated.status_code == 200
    data_updated = resp_updated.json()
    assert data_updated["total_documents"] == 1
    assert data_updated["approved_documents_count"] == 1
    assert (
        data_updated["overall_readiness_score"] > data_init["overall_readiness_score"]
    )

    # Zone 1 should show 1 present, 1 approved
    z1 = next(z for z in data_updated["zones"] if z["zone_code"] == 1)
    assert z1["present_count"] == 1
    assert z1["approved_count"] == 1


@pytest.mark.asyncio
async def test_document_signature_verification_endpoint():
    """Test verifying electronic signatures through the dedicated API endpoint."""
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Test e-signature flow",
    )

    # Ingest document
    ingest_resp = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "STUDY-SIG-1",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_signoff.pdf",
            "content": "Protocol signature verification test content.",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )
    assert ingest_resp.status_code == 201
    doc_id = ingest_resp.json()["document_id"]

    # Sign document
    sign_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Protocol Author Approval",
        action_path=f"/api/v1/etmf/documents/{doc_id}/sign-off",
    )
    sign_resp = client.post(
        f"/api/v1/etmf/documents/{doc_id}/sign-off",
        json={"signing_reason": "APPROVAL"},
        headers=sign_headers,
    )
    assert sign_resp.status_code == 200

    # Verify signature
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector,auditor",
        change_reason="Signature verification inspection",
    )
    verify_resp = client.post(
        f"/api/v1/etmf/documents/{doc_id}/verify-signature",
        headers=inspector_headers,
    )
    assert verify_resp.status_code == 200
    v_data = verify_resp.json()
    assert v_data["document_id"] == doc_id
    assert v_data["is_valid"] is True
    assert v_data["signer"] == "test_user"
    assert v_data["signing_reason"] == "APPROVAL"
    assert v_data["content_hash_matched"] is True
