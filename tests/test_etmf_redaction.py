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
    assert resp_inspector_download_redacted.text == "[REDACTED] protocol content."


@pytest.mark.asyncio
async def test_redaction_audit_trail_and_provenance():
    """
    Ensure the REDACT audit entry contains only non-sensitive metadata,
    and that version history and unredacted source content are preserved and queryable.
    """
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
