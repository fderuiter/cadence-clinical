import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app
from apps.etmf.models import Base, TMFAuditLog, TMFDocument
from apps.gateway.main import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eTMF database for redaction and auth testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
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
    return headers


@pytest.mark.asyncio
async def test_redaction_authorization_gates():
    """
    Ensure only appropriate privileged roles can perform redaction and raw-original retrieval,
    and auditor/inspector roles are strictly blocked.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingesting initial protocol"
    )

    # Ingest source document
    payload = {
        "study_id": "study_001",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_original.pdf",
        "content": "This contains highly sensitive PII for patient Alice Smith born on 1990-01-01.",
        "mime_type": "application/pdf",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # Attempt to redact using inspector role -> should fail with 403
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Attempting redaction"
    )
    redact_payload = {
        "redacted_content": "[REDACTED] protocol content.",
        "redacted_filename": "protocol_redacted.pdf",
        "manifest": {
            "signature": "mock-redaction-signature-xyz",
            "redaction_metadata": {"rules_applied": ["PII_RED_01"]},
        },
    }
    resp_inspector_redact = client.post(
        f"/api/v1/etmf/documents/{source_id}/redact",
        json=redact_payload,
        headers=inspector_headers,
    )
    assert resp_inspector_redact.status_code == 403
    assert "Forbidden" in resp_inspector_redact.json()["detail"]

    # Redact using admin role -> should succeed
    admin_redact_headers = get_auth_headers(
        roles="admin", change_reason="Redacting Alice Smith PII"
    )
    resp_admin_redact = client.post(
        f"/api/v1/etmf/documents/{source_id}/redact",
        json=redact_payload,
        headers=admin_redact_headers,
    )
    assert resp_admin_redact.status_code == 201
    redacted_data = resp_admin_redact.json()
    assert redacted_data["is_redacted"] is True
    assert redacted_data["redaction_source_id"] == source_id
    assert (
        redacted_data["redaction_manifest_json"]["signature"]
        == "mock-redaction-signature-xyz"
    )
    assert redacted_data["version_index"] == 2
    assert redacted_data["filename"] == "protocol_redacted.pdf"

    # Enforce original retrieval restriction on viewing metadata
    # Inspector view original -> should fail with 403
    inspector_headers_view = get_auth_headers(roles="regulatory_inspector")
    resp_inspector_view = client.get(
        f"/api/v1/etmf/documents/{source_id}",
        headers=inspector_headers_view,
    )
    assert resp_inspector_view.status_code == 403
    assert (
        "Raw-original retrieval is restricted" in resp_inspector_view.json()["detail"]
    )

    # Admin view original -> should succeed
    admin_headers_view = get_auth_headers(roles="admin")
    resp_admin_view = client.get(
        f"/api/v1/etmf/documents/{source_id}",
        headers=admin_headers_view,
    )
    assert resp_admin_view.status_code == 200
    assert resp_admin_view.json()["filename"] == "protocol_original.pdf"

    # Enforce original retrieval restriction on downloading content
    # Inspector download original -> should fail with 403
    resp_inspector_download = client.get(
        f"/api/v1/etmf/documents/{source_id}/download",
        headers=inspector_headers_view,
    )
    assert resp_inspector_download.status_code == 403
    assert (
        "Raw-original retrieval is restricted"
        in resp_inspector_download.json()["detail"]
    )

    # Admin download original -> should succeed
    resp_admin_download = client.get(
        f"/api/v1/etmf/documents/{source_id}/download",
        headers=admin_headers_view,
    )
    assert resp_admin_download.status_code == 200
    assert "Alice Smith" in resp_admin_download.text

    # Auditor/Inspector should be able to view and download redacted version
    redacted_id = redacted_data["id"]
    resp_inspector_view_redacted = client.get(
        f"/api/v1/etmf/documents/{redacted_id}",
        headers=inspector_headers_view,
    )
    assert resp_inspector_view_redacted.status_code == 200
    assert resp_inspector_view_redacted.json()["is_redacted"] is True

    resp_inspector_download_redacted = client.get(
        f"/api/v1/etmf/documents/{redacted_id}/download",
        headers=inspector_headers_view,
    )
    assert resp_inspector_download_redacted.status_code == 200
    assert resp_inspector_download_redacted.text.startswith(
        "[REDACTED] protocol content."
    )


@pytest.mark.asyncio
async def test_redaction_audit_trail_and_provenance():
    """
    Ensure the REDACT audit entry contains only non-sensitive metadata,
    and that version history and unredacted source content are preserved and queryable.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Ingest protocol"
    )

    # 1. Ingest original unredacted document (Version 1)
    payload = {
        "study_id": "study_002",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_v1.pdf",
        "content": "Secret patient data: Bob Jones is unblinded.",
        "mime_type": "application/pdf",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # 2. Ingest a redacted version as Version 2
    redact_headers = get_auth_headers(
        roles="admin,sponsor_dm", change_reason="Redaction for GDPR compliance"
    )
    redact_payload = {
        "redacted_content": "Secret patient data: [REDACTED] is unblinded.",
        "redacted_filename": "protocol_v2_redacted.pdf",
        "manifest": {
            "signature": "manifest-signature-abc-123",
            "redaction_metadata": {
                "algorithm": "masking",
                "target_fields": ["patient_name"],
            },
        },
    }
    resp_redact = client.post(
        f"/api/v1/etmf/documents/{source_id}/redact",
        json=redact_payload,
        headers=redact_headers,
    )
    assert resp_redact.status_code == 201
    redacted_data = resp_redact.json()
    redacted_id = redacted_data["id"]

    # 3. Verify that the original document and redacted document both exist in DB intact
    async with db_manager.get_session_maker()() as session:
        # Query TMFDocument to verify version history
        stmt = (
            select(TMFDocument)
            .where(TMFDocument.study_id == "study_002")
            .order_by(TMFDocument.version_index)
        )
        result = await session.execute(stmt)
        docs = result.scalars().all()
        assert len(docs) == 2

        # Original Version 1
        assert docs[0].id == source_id
        assert docs[0].version_index == 1
        assert docs[0].content == "Secret patient data: Bob Jones is unblinded."
        assert docs[0].is_redacted is False

        # Redacted Version 2
        assert docs[1].id == redacted_id
        assert docs[1].version_index == 2
        assert docs[1].content == "Secret patient data: [REDACTED] is unblinded."
        assert docs[1].is_redacted is True
        assert docs[1].redaction_source_id == source_id
        assert (
            docs[1].redaction_manifest_json["signature"] == "manifest-signature-abc-123"
        )
        assert docs[1].metadata_json["change_reason"] == "Redaction for GDPR compliance"

        # Check TMFAuditLog for REDACT action
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.action == "REDACT")
        result_audit = await session.execute(stmt_audit)
        audit_logs = result_audit.scalars().all()
        assert len(audit_logs) == 1

        audit_record = audit_logs[0]
        assert audit_record.user_id == "test_user"
        # Role matches what was passed in
        assert "admin" in audit_record.user_role
        assert "sponsor_dm" in audit_record.user_role
        assert audit_record.document_id == redacted_id

        # Verify details contain non-sensitive metadata only and NO sensitive content
        details = audit_record.details
        assert "Bob Jones" not in details
        assert "REDACT action executed" in details
        assert f"Source Document Reference ID: {source_id}" in details
        assert f"Redacted Document Reference ID: {redacted_id}" in details
        assert "manifest-signature-abc-123" in details


@pytest.mark.asyncio
async def test_automated_redaction_basic():
    """
    Test that a valid automated redact request succeeds:
    - Creates a new version without modifying the source.
    - Response never contains any detected raw identifiers (like Bob or bob@gmail.com).
    - Verifies categories and counts in response.
    - Manifest is signed and valid.
    - Non-sensitive REDACT audit record is created.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingesting unredacted source"
    )

    payload = {
        "study_id": "study_003",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_with_pii.txt",
        "content": "Contact Bob Jones at bob@gmail.com on 2026-05-15 or call 555-1234.",
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # Call automated redact endpoint
    redact_headers = get_auth_headers(
        roles="admin", change_reason="Automated HIPAA Redaction"
    )
    auto_payload = {
        "profile": "HIPAA",
        "custom_terms": ["Bob Jones"],
    }
    resp_auto = client.post(
        f"/api/v1/etmf/documents/{source_id}/auto-redact",
        json=auto_payload,
        headers=redact_headers,
    )
    assert resp_auto.status_code == 201
    data = resp_auto.json()

    assert data["status"] == "success"
    assert data["document_id"] != source_id
    assert data["version_index"] == 2
    assert data["filename"] == "protocol_with_pii_redacted.txt"

    # Category counts must show correct redacted types
    counts = data["categories_counts"]
    assert counts["email"] == 1
    assert counts["dates"] == 1
    assert counts["telephone_fax"] == 1
    assert counts["custom"] == 1

    # Verify that the response NEVER contains raw identifiers
    raw_response_str = resp_auto.text
    assert "Bob Jones" not in raw_response_str
    assert "bob@gmail.com" not in raw_response_str
    assert "555-1234" not in raw_response_str
    assert "2026-05-15" not in raw_response_str

    # Verify the redacted content is actually stored and redacted
    redacted_id = data["document_id"]
    resp_dl = client.get(
        f"/api/v1/etmf/documents/{redacted_id}/download",
        headers=get_auth_headers(roles="admin"),
    )
    assert resp_dl.status_code == 200
    redacted_text = resp_dl.text
    assert "Bob Jones" not in redacted_text
    assert "bob@gmail.com" not in redacted_text
    assert (
        "[CUSTOM]" in redacted_text
        or "[CUSTOM_REDACTED]" in redacted_text
        or redacted_text != payload["content"]
    )
    assert "[EMAIL]" in redacted_text
    assert "[DATES]" in redacted_text

    # Verify audit trail contains REDACT entry with no raw matches
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFAuditLog).where(
            TMFAuditLog.document_id == redacted_id, TMFAuditLog.action == "REDACT"
        )
        result = await session.execute(stmt)
        audit_log = result.scalars().first()
        assert audit_log is not None
        assert "Bob Jones" not in audit_log.details
        assert "bob@gmail.com" not in audit_log.details


@pytest.mark.asyncio
async def test_automated_redaction_profile_scopes():
    """
    Verify profile selection affects the active categories:
    EU_CTR profile should NOT detect IP/MAC/URLs but should detect Email/Dates.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Ingest")

    payload = {
        "study_id": "study_004",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_with_mixed_pii.txt",
        "content": "Email is doctor@clinic.org, IP is 192.168.1.1, date is 2026-01-01.",
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # Redact using EU_CTR (IP should NOT be redacted, Email/Dates should be)
    redact_headers = get_auth_headers(roles="admin", change_reason="CTR Redaction")
    resp_ctr = client.post(
        f"/api/v1/etmf/documents/{source_id}/auto-redact",
        json={"profile": "EU_CTR"},
        headers=redact_headers,
    )
    assert resp_ctr.status_code == 201
    data_ctr = resp_ctr.json()
    assert "ip_mac_addresses" not in data_ctr["categories_counts"]
    assert data_ctr["categories_counts"]["email"] == 1
    assert data_ctr["categories_counts"]["dates"] == 1

    # Download redacted text and verify IP is unchanged, email and date are masked
    resp_dl = client.get(
        f"/api/v1/etmf/documents/{data_ctr['document_id']}/download",
        headers=get_auth_headers(roles="admin"),
    )
    text_dl = resp_dl.text
    assert "doctor@clinic.org" not in text_dl
    assert "2026-01-01" not in text_dl
    assert "192.168.1.1" in text_dl


@pytest.mark.asyncio
async def test_automated_redaction_errors():
    """
    Test error situations:
    - Absent document (404)
    - Invalid profile (422)
    - Missing X-Change-Reason (400)
    - Unauthorized caller (403)
    """
    # @req:PRD-TMF-005
    client = TestClient(app)

    # 1. Absent Document -> 404
    headers = get_auth_headers(roles="admin", change_reason="Testing absent")
    resp = client.post(
        "/api/v1/etmf/documents/non_existent_id/auto-redact",
        json={"profile": "HIPAA"},
        headers=headers,
    )
    assert resp.status_code == 404
    assert "not found" in resp.json()["detail"]

    # 2. Ingest document to test further errors
    resp_ing = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_005",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_err.txt",
            "content": "Patient Alice",
            "mime_type": "text/plain",
        },
        headers=headers,
    )
    doc_id = resp_ing.json()["document_id"]

    # 3. Invalid Profile -> 422
    resp_inv_prof = client.post(
        f"/api/v1/etmf/documents/{doc_id}/auto-redact",
        json={"profile": "INVALID_PROFILE_NAME"},
        headers=headers,
    )
    assert resp_inv_prof.status_code == 422

    # 4. Missing X-Change-Reason -> 403 (blocked by GatewayAuthMiddleware)
    no_reason_headers = get_auth_headers(roles="admin")
    resp_no_reason = client.post(
        f"/api/v1/etmf/documents/{doc_id}/auto-redact",
        json={"profile": "HIPAA"},
        headers=no_reason_headers,
    )
    assert resp_no_reason.status_code == 403
    assert "Missing change justification reason" in resp_no_reason.json()["detail"]

    # 5. Unauthorized Caller (Auditor) -> 403
    auditor_headers = get_auth_headers(roles="auditor", change_reason="Auditor trying")
    resp_auditor = client.post(
        f"/api/v1/etmf/documents/{doc_id}/auto-redact",
        json={"profile": "HIPAA"},
        headers=auditor_headers,
    )
    assert resp_auditor.status_code == 403


@pytest.mark.asyncio
async def test_automated_redaction_trial_locked():
    """
    Test that trial lock blocks automated redactions (403)
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Ingest")

    # Ingest document
    resp_ing = client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "study_006",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_lock.txt",
            "content": "Patient Bob",
            "mime_type": "text/plain",
        },
        headers=admin_headers,
    )
    doc_id = resp_ing.json()["document_id"]

    # Lock trial
    from apps.execution.trial_lock import TrialLockManager

    TrialLockManager.lock_trial()
    try:
        resp_locked = client.post(
            f"/api/v1/etmf/documents/{doc_id}/auto-redact",
            json={"profile": "HIPAA"},
            headers=get_auth_headers(roles="admin", change_reason="Redact locked"),
        )
        assert resp_locked.status_code == 403
        assert "Trial is currently locked" in resp_locked.json()["detail"]
    finally:
        # Unlock trial for other tests
        TrialLockManager.unlock_trial()


@pytest.mark.asyncio
async def test_manual_redaction_success():
    """
    Test successful manual redaction using both character spans and literal terms.
    Verify:
    - Successor document created (Version 2) with source doc preserved.
    - Content is redacted correctly.
    - Manifest and response never leak raw PII/PHI.
    - Immutable audit trail logs REDACT entry with non-sensitive details only.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest unredacted protocol manual test"
    )

    # 1. Ingest original unredacted document
    content = (
        "We must protect Bob Jones whose telephone is 555-4321 and born on 1990-05-12."
    )
    payload = {
        "study_id": "study_007",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_original_manual.txt",
        "content": content,
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # 2. Trigger manual redaction
    # "Bob Jones" is at index 16 to 25. Let's verify: content[16:25] == "Bob Jones"
    assert content[16:25] == "Bob Jones"

    redact_headers = get_auth_headers(
        roles="admin", change_reason="Executing manual redaction justification"
    )
    redact_payload = {
        "spans": [{"start": 16, "end": 25, "label": "manual_name"}],
        "terms": ["555-4321"],
        "redacted_filename": "manual_redacted.txt",
    }

    resp_redact = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=redact_payload,
        headers=redact_headers,
    )
    assert resp_redact.status_code == 201
    data = resp_redact.json()

    assert data["status"] == "success"
    assert data["document_id"] != source_id
    assert data["version_index"] == 2
    assert data["filename"] == "manual_redacted.txt"

    # Verify response/manifest NEVER leaks raw PII/PHI (Bob Jones or 555-4321)
    raw_response_str = resp_redact.text
    assert "Bob Jones" not in raw_response_str
    assert "555-4321" not in raw_response_str

    # 3. Download redacted document and check the content
    redacted_id = data["document_id"]
    resp_dl = client.get(
        f"/api/v1/etmf/documents/{redacted_id}/download",
        headers=get_auth_headers(roles="admin"),
    )
    assert resp_dl.status_code == 200
    redacted_text = resp_dl.text
    assert "Bob Jones" not in redacted_text
    assert "555-4321" not in redacted_text
    assert "[MANUAL_NAME]" in redacted_text
    assert "[CUSTOM]" in redacted_text
    # Pre-existing date is untouched since we didn't target it
    assert "1990-05-12" in redacted_text

    # 4. Verify database state
    async with db_manager.get_session_maker()() as session:
        # Check source is preserved and unredacted
        stmt_src = select(TMFDocument).where(TMFDocument.id == source_id)
        res_src = await session.execute(stmt_src)
        src_doc = res_src.scalar_one()
        assert src_doc.content == content
        assert src_doc.is_redacted is False

        # Check redacted document is stored correctly
        stmt_red = select(TMFDocument).where(TMFDocument.id == redacted_id)
        res_red = await session.execute(stmt_red)
        red_doc = res_red.scalar_one()
        assert red_doc.is_redacted is True
        assert red_doc.redaction_source_id == source_id
        assert red_doc.version_index == 2
        assert "change_reason" in red_doc.metadata_json
        assert (
            red_doc.metadata_json["change_reason"]
            == "Executing manual redaction justification"
        )

        # Check audit trail has REDACT action with no leak of raw values
        stmt_audit = select(TMFAuditLog).where(
            TMFAuditLog.document_id == redacted_id, TMFAuditLog.action == "REDACT"
        )
        res_audit = await session.execute(stmt_audit)
        audit_log = res_audit.scalars().first()
        assert audit_log is not None
        assert "Bob Jones" not in audit_log.details
        assert "555-4321" not in audit_log.details
        assert f"Source Document Reference ID: {source_id}" in audit_log.details


@pytest.mark.asyncio
async def test_manual_redaction_span_validation():
    """
    Verify character span validation:
    - Out-of-range offsets should be rejected.
    - Invalid offsets (start >= end) should be rejected.
    - Overlapping/conflicting span inputs should be rejected.
    All should return HTTP 422.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest unredacted manual check"
    )

    # Ingest document (length 29)
    content = "Confidential document content."
    payload = {
        "study_id": "study_008",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_span_check.txt",
        "content": content,
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # 1. Test out-of-range: negative start
    payload_err = {"spans": [{"start": -5, "end": 10}]}
    resp_err = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=payload_err,
        headers=get_auth_headers(roles="admin", change_reason="Neg start"),
    )
    assert resp_err.status_code == 422
    assert "Invalid span offsets" in resp_err.json()["detail"]

    # 2. Test out-of-range: end exceeding document length
    payload_err = {"spans": [{"start": 10, "end": 45}]}
    resp_err = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=payload_err,
        headers=get_auth_headers(roles="admin", change_reason="Out of bounds"),
    )
    assert resp_err.status_code == 422
    assert "Invalid span offsets" in resp_err.json()["detail"]

    # 3. Test invalid: start >= end
    payload_err = {"spans": [{"start": 10, "end": 10}]}
    resp_err = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=payload_err,
        headers=get_auth_headers(roles="admin", change_reason="Empty span"),
    )
    assert resp_err.status_code == 422
    assert "Invalid span offsets" in resp_err.json()["detail"]

    # 4. Test overlapping/conflicting spans passed in input
    payload_err = {"spans": [{"start": 0, "end": 10}, {"start": 5, "end": 15}]}
    resp_err = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=payload_err,
        headers=get_auth_headers(roles="admin", change_reason="Overlapping spans"),
    )
    assert resp_err.status_code == 422
    assert "Overlapping or conflicting span inputs" in resp_err.json()["detail"]

    # 5. Test nested overlapping spans
    payload_err = {"spans": [{"start": 5, "end": 20}, {"start": 10, "end": 15}]}
    resp_err = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=payload_err,
        headers=get_auth_headers(roles="admin", change_reason="Nested spans"),
    )
    assert resp_err.status_code == 422
    assert "Overlapping or conflicting span inputs" in resp_err.json()["detail"]


@pytest.mark.asyncio
async def test_manual_redaction_literal_escaping():
    """
    Ensure literal term matching handles terms with special regex characters safely.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Ingest special chars"
    )

    payload = {
        "study_id": "study_009",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_special.txt",
        "content": "Check $100 price and special+char.",
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # Redact using special regex character terms
    redact_payload = {"terms": ["$100", "special+char"]}
    resp_redact = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json=redact_payload,
        headers=get_auth_headers(roles="admin", change_reason="Special chars redact"),
    )
    assert resp_redact.status_code == 201
    redacted_id = resp_redact.json()["document_id"]

    # Download and verify they are successfully redacted
    resp_dl = client.get(
        f"/api/v1/etmf/documents/{redacted_id}/download",
        headers=get_auth_headers(roles="admin"),
    )
    assert resp_dl.status_code == 200
    assert "$100" not in resp_dl.text
    assert "special+char" not in resp_dl.text
    assert "[CUSTOM]" in resp_dl.text


@pytest.mark.asyncio
async def test_manual_redaction_authorization_and_lock():
    """
    Verify read-only roles are blocked from executing manual redaction,
    and a locked trial blocks manual redactions.
    """
    # @req:PRD-TMF-005
    client = TestClient(app)
    admin_headers = get_auth_headers(roles="admin", change_reason="Ingest auth")

    payload = {
        "study_id": "study_010",
        "artifact_type": "Clinical Trial Protocol",
        "filename": "protocol_auth.txt",
        "content": "Sensitive patient Bob Smith.",
        "mime_type": "text/plain",
    }
    resp = client.post("/api/v1/etmf/ingest", json=payload, headers=admin_headers)
    assert resp.status_code == 201
    source_id = resp.json()["document_id"]

    # 1. Auditor trying manual redact -> should fail with 403
    auditor_headers = get_auth_headers(roles="auditor", change_reason="Auditor hack")
    resp_aud = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json={"terms": ["Bob Smith"]},
        headers=auditor_headers,
    )
    assert resp_aud.status_code == 403

    # 2. Inspector trying manual redact -> should fail with 403
    inspector_headers = get_auth_headers(
        roles="regulatory_inspector", change_reason="Inspector hack"
    )
    resp_ins = client.post(
        f"/api/v1/etmf/documents/{source_id}/manual-redact",
        json={"terms": ["Bob Smith"]},
        headers=inspector_headers,
    )
    assert resp_ins.status_code == 403

    # 3. Locked trial trying manual redact -> should fail with 403
    from apps.execution.trial_lock import TrialLockManager

    TrialLockManager.lock_trial()
    try:
        resp_locked = client.post(
            f"/api/v1/etmf/documents/{source_id}/manual-redact",
            json={"terms": ["Bob Smith"]},
            headers=get_auth_headers(
                roles="admin", change_reason="Admin manual redact locked"
            ),
        )
        assert resp_locked.status_code == 403
        assert "Trial is currently locked" in resp_locked.json()["detail"]
    finally:
        TrialLockManager.unlock_trial()
