import time
import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select
from jose import jwt

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFAuditLog, TMFDocument
from apps.etmf.sealer import execute_etmf_audit_sealing_cycle, validate_etmf_ledger_integrity
from apps.gateway.main import generate_signature
from signature import SignatureManifestation

GATEWAY_SECRET = "internal-gateway-secret-12345"


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for signing tests.
    """
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
    user_id: str = "test_user",
    sig_token_custom: str = None,
    action_path: str = None,
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
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

    if sig_token_custom:
        headers["X-Sig-Token"] = sig_token_custom
    elif action_path:
        # Generate a unique jti for each token to bypass replay cache
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
async def test_etmf_signing_happy_path():
    """
    Verify successful signing flow:
    - Persists a valid, verifiable SignatureManifestation.
    - Transitions document to SIGNED and APPROVED.
    - Generates immutable SIGN and APPROVE audit logs.
    - Successfully integrates with the Merkle ledger seal cycle.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest unredacted protocol manual test"
    )

    # 1. Ingest an unsigned document
    payload = {
        "study_id": "study_101",
        "artifact_type": "FORM_1572",
        "filename": "form1572_unsigned.pdf",
        "content": "Statement of Investigator qualification document.",
        "mime_type": "application/pdf",
        "metadata_json": {"requires_signature": False},
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    # 2. Call the signing endpoint with a valid signature token
    action_path = f"/api/v1/etmf/documents/{doc_id}/sign-off"
    sig_headers = get_auth_headers(
        roles="admin",
        change_reason="Sign-off Form 1572",
        action_path=action_path,
    )

    sign_payload = {"signing_reason": "APPROVAL"}
    resp_sign = client.post(action_path, json=sign_payload, headers=sig_headers)
    assert resp_sign.status_code == 200

    data = resp_sign.json()
    assert data["status"] == "SIGNED"
    assert data["approval_status"] == "APPROVED"
    assert data["signer"] == "test_user"
    assert data["signing_timestamp"] is not None

    # Load and verify the signature manifestation
    manifestation_data = data["signature_manifestation"]
    assert manifestation_data is not None
    assert manifestation_data["signer_id"] == "test_user"
    assert manifestation_data["signing_reason"] == "APPROVAL"

    # Re-verify the manifestation cryptographically
    manifest = SignatureManifestation(**manifestation_data)
    assert manifest.verify() is True

    # 3. Verify Database state and Audit logs
    async with db_manager.get_session_maker()() as session:
        # Fetch document
        stmt_doc = select(TMFDocument).where(TMFDocument.id == doc_id)
        db_doc = (await session.execute(stmt_doc)).scalar_one()
        assert db_doc.status == "SIGNED"
        assert db_doc.approval_status == "APPROVED"

        # Check for SIGN and APPROVE audit logs
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.document_id == doc_id)
        logs = (await session.execute(stmt_audit)).scalars().all()

        actions = [log.action for log in logs]
        assert "SIGN" in actions
        assert "APPROVE" in actions

        sign_log = next(log for log in logs if log.action == "SIGN")
        approve_log = next(log for log in logs if log.action == "APPROVE")

        assert "Successfully signed" in sign_log.details
        assert "Successfully approved" in approve_log.details

        # 4. Feed into Merkle sealing cycle and validate chain integrity
        current_block_hash = await execute_etmf_audit_sealing_cycle(session)
        assert current_block_hash is not None

        # Force SQLAlchemy session to expire objects and fetch fresh ones from DB
        session.expire_all()

        # Verify all audit logs are cryptographically sealed
        stmt_sealed = select(TMFAuditLog).where(TMFAuditLog.document_id == doc_id)
        sealed_logs = (await session.execute(stmt_sealed)).scalars().all()
        for log in sealed_logs:
            assert log.cryptographic_seal == current_block_hash

        # Validate whole ledger chain integrity
        is_valid_ledger = await validate_etmf_ledger_integrity(session)
        assert is_valid_ledger is True


@pytest.mark.asyncio
async def test_etmf_signing_reauth_failures():
    """
    Verify rejection of missing, expired, or mismatched re-authentication tokens.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest unredacted protocol manual test"
    )

    # Ingest document
    payload = {
        "study_id": "study_101",
        "artifact_type": "FORM_1572",
        "filename": "form1572_unsigned.pdf",
        "content": "Statement of Investigator qualification document.",
        "mime_type": "application/pdf",
        "metadata_json": {"requires_signature": False},
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    action_path = f"/api/v1/etmf/documents/{doc_id}/sign-off"

    # 1. Missing signature token -> 411 / 401 depending on middleware
    no_token_headers = get_auth_headers(roles="admin", change_reason="No token")
    resp_no_token = client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=no_token_headers)
    assert resp_no_token.status_code == 401
    assert "re-authentication is required" in resp_no_token.json()["message"].lower()

    # 2. Expired signature token
    expired_payload = {
        "sub": "test_user",
        "username": "test_user",
        "action": action_path,
        "roles": ["admin"],
        "iat": time.time() - 600,
        "exp": time.time() - 300,  # Expired 5 mins ago
        "jti": "jti-expired-123",
    }
    expired_token = jwt.encode(expired_payload, GATEWAY_SECRET, algorithm="HS256")
    expired_headers = get_auth_headers(
        roles="admin",
        change_reason="Expired token",
        sig_token_custom=expired_token,
    )
    resp_expired = client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=expired_headers)
    assert resp_expired.status_code == 401
    assert "invalid signature token" in resp_expired.json()["message"].lower()

    # 3. User mismatch signature token
    mismatch_user_payload = {
        "sub": "some_other_user",
        "username": "some_other_user",
        "action": action_path,
        "roles": ["admin"],
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": "jti-mismatch-123",
    }
    mismatch_user_token = jwt.encode(mismatch_user_payload, GATEWAY_SECRET, algorithm="HS256")
    mismatch_headers = get_auth_headers(
        roles="admin",
        change_reason="User mismatch",
        sig_token_custom=mismatch_user_token,
    )
    resp_mismatch = client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=mismatch_headers)
    assert resp_mismatch.status_code == 401
    assert "mismatch" in resp_mismatch.json()["message"].lower()

    # 4. Action mismatch signature token
    mismatch_action_payload = {
        "sub": "test_user",
        "username": "test_user",
        "action": "/api/v1/wrong-endpoint",
        "roles": ["admin"],
        "iat": time.time(),
        "exp": time.time() + 300.0,
        "jti": "jti-mismatch-action-123",
    }
    mismatch_action_token = jwt.encode(mismatch_action_payload, GATEWAY_SECRET, algorithm="HS256")
    mismatch_action_headers = get_auth_headers(
        roles="admin",
        change_reason="Action mismatch",
        sig_token_custom=mismatch_action_token,
    )
    resp_mismatch_action = client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=mismatch_action_headers)
    assert resp_mismatch_action.status_code == 401
    assert "mismatch" in resp_mismatch_action.json()["message"].lower()


@pytest.mark.asyncio
async def test_etmf_post_signature_locking():
    """
    Verify strict immutability locking post-signature:
    - Reject transition, redact, auto-redact, manual-redact, and re-ingest.
    - All return 403 Forbidden with IMMUTABILITY_VIOLATION.
    - Write a MUTATION_REJECTED action to audit logs.
    """
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest unredacted protocol manual test"
    )

    # 1. Ingest document
    payload = {
        "study_id": "study_101",
        "artifact_type": "FORM_1572",
        "filename": "form1572_unsigned.pdf",
        "content": "Statement of Investigator qualification document.",
        "mime_type": "application/pdf",
        "metadata_json": {"requires_signature": False},
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    doc_id = resp.json()["document_id"]

    # 2. Sign the document
    action_path = f"/api/v1/etmf/documents/{doc_id}/sign-off"
    sig_headers = get_auth_headers(
        roles="admin",
        change_reason="Sign-off Form 1572",
        action_path=action_path,
    )
    client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=sig_headers)

    # 3. Assert lock is active across all mutations

    # Attempt A: Re-sign the signed document -> 403 IMMUTABILITY_VIOLATION
    # Use a fresh, unique token headers to bypass replay protection
    fresh_sig_headers = get_auth_headers(
        roles="admin",
        change_reason="Sign-off Form 1572",
        action_path=action_path,
    )
    resp_resign = client.post(action_path, json={"signing_reason": "APPROVAL"}, headers=fresh_sig_headers)
    assert resp_resign.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_resign.json()["detail"]

    # Attempt B: Transition status of the signed document -> 403 IMMUTABILITY_VIOLATION
    trans_headers = get_auth_headers(roles="admin", change_reason="Try to transition status")
    resp_transition = client.post(
        f"/api/v1/etmf/documents/{doc_id}/transition",
        json={"to_status": "TECHNICAL_QC", "reason_for_change": "Forced status edit attempt"},
        headers=trans_headers,
    )
    assert resp_transition.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_transition.json()["detail"]

    # Attempt C: Standard Redact of the signed document -> 403 IMMUTABILITY_VIOLATION
    redact_payload = {
        "redacted_content": "[REDACTED] Form 1572.",
        "redacted_filename": "form1572_redacted.pdf",
        "manifest": {
            "signature": "red-sig-123",
            "redaction_metadata": {"applied_rules": ["MASK"]},
        },
    }
    resp_redact = client.post(
        f"/api/v1/etmf/documents/{doc_id}/redact",
        json=redact_payload,
        headers=get_auth_headers(roles="admin", change_reason="Standard redaction attempt"),
    )
    assert resp_redact.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_redact.json()["detail"]

    # Attempt D: Auto Redact of the signed document -> 403 IMMUTABILITY_VIOLATION
    resp_auto = client.post(
        f"/api/v1/etmf/documents/{doc_id}/auto-redact",
        json={"profile": "HIPAA"},
        headers=get_auth_headers(roles="admin", change_reason="Auto redaction attempt"),
    )
    assert resp_auto.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_auto.json()["detail"]

    # Attempt E: Manual Redact of the signed document -> 403 IMMUTABILITY_VIOLATION
    resp_manual = client.post(
        f"/api/v1/etmf/documents/{doc_id}/manual-redact",
        json={"terms": ["Investigator"]},
        headers=get_auth_headers(roles="admin", change_reason="Manual redaction attempt"),
    )
    assert resp_manual.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_manual.json()["detail"]

    # Attempt F: Ingest a new version of the signed document -> 403 IMMUTABILITY_VIOLATION
    resp_ingest_new_version = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_101",
            "artifact_type": "FORM_1572",
            "filename": "form1572_v2.pdf",
            "content": "Updated Statement of Investigator qualification document.",
            "mime_type": "application/pdf",
            "metadata_json": {"requires_signature": False},
        },
        headers=get_auth_headers(roles="admin", change_reason="Attempt new version ingestion"),
    )
    assert resp_ingest_new_version.status_code == 403
    assert "IMMUTABILITY_VIOLATION" in resp_ingest_new_version.json()["detail"]

    # 4. Verify MUTATION_REJECTED action inside TMFAuditLog
    async with db_manager.get_session_maker()() as session:
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.action == "MUTATION_REJECTED")
        logs = (await session.execute(stmt_audit)).scalars().all()
        # Assert at least one attempt was logged with details of the rejection
        assert len(logs) > 0
        for log in logs:
            assert "IMMUTABILITY_VIOLATION" in log.details
