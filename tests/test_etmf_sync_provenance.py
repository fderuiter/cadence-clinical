import hashlib

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select

from apps.etmf.database import db_manager
from apps.etmf.main import app as etmf_app
from apps.etmf.models import Base, TMFAuditLog, TMFDocument
from apps.etmf.sealer import (
    execute_etmf_audit_sealing_cycle,
    validate_etmf_ledger_integrity,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_test_etmf_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers_with_user(
    user_id: str, roles: str = "admin", change_reason: str = ""
) -> dict:
    import time

    from apps.gateway.main import generate_signature

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
    return headers


@pytest.mark.asyncio
async def test_eisf_to_etmf_e2e_boundaries() -> None:
    client = TestClient(etmf_app)
    headers = get_auth_headers_with_user(
        user_id="eisf_sync_service",
        roles="system",
        change_reason="Synchronized site document propagation",
    )

    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "artifact_type": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr. Smith CV content",
        "mime_type": "application/pdf",
        "correlation_key": "corr:study-100:site-boston-01:investigator-cv:smith",
        "content_checksum": hashlib.sha256(b"Dr. Smith CV content").hexdigest(),
        "source_system": "eISF",
    }

    # 1. First Ingestion - Creates a version
    resp1 = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp1.status_code == 201
    data1 = resp1.json()
    assert data1["result"] == "created"
    assert data1["version_index"] == 1
    assert data1["correlation_key"] == payload["correlation_key"]
    assert data1["content_checksum"] == payload["content_checksum"]
    assert data1["source_system"] == "eISF"
    assert data1["sync_status"] == "SYNCED"

    # Verify document is in eTMF database
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFDocument).where(TMFDocument.id == data1["id"])
        res = await session.execute(stmt)
        doc = res.scalars().one()
        assert doc.correlation_key == payload["correlation_key"]
        assert doc.content_checksum == payload["content_checksum"]
        assert doc.source_system == "eISF"
        assert doc.sync_status == "SYNCED"

        # Verify audit log includes structured reason_for_change
        stmt_audit = select(TMFAuditLog).where(TMFAuditLog.document_id == doc.id)
        res_audit = await session.execute(stmt_audit)
        audit_logs = res_audit.scalars().all()
        assert len(audit_logs) >= 1
        assert (
            audit_logs[0].reason_for_change == "Synchronized site document propagation"
        )

    # 2. Replayed Identical Payload - Durable no-op
    resp2 = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp2.status_code == 201
    data2 = resp2.json()
    assert data2["result"] == "ignored"
    assert data2["version_index"] == 1
    assert data2["id"] == data1["id"]

    # Verify no new versions are created
    async with db_manager.get_session_maker()() as session:
        stmt = select(TMFDocument).where(
            TMFDocument.correlation_key == payload["correlation_key"]
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 1

        # Verify INGEST_NOOP action was recorded
        stmt_noop = select(TMFAuditLog).where(TMFAuditLog.action == "INGEST_NOOP")
        res_noop = await session.execute(stmt_noop)
        noop_logs = res_noop.scalars().all()
        assert len(noop_logs) >= 1
        assert "Durable no-op" in noop_logs[0].details
        assert (
            noop_logs[0].reason_for_change == "Synchronized site document propagation"
        )

    # 3. Changed Content for the Same correlation_key - Creates exactly one new version
    changed_payload = dict(payload)
    changed_payload["content"] = "Dr. Smith CV content revised V2"
    changed_payload["content_checksum"] = hashlib.sha256(
        changed_payload["content"].encode("utf-8")
    ).hexdigest()

    resp3 = client.post("/api/v1/etmf/ingest", json=changed_payload, headers=headers)
    assert resp3.status_code == 201
    data3 = resp3.json()
    assert data3["result"] == "created"
    assert data3["version_index"] == 2
    assert data3["correlation_key"] == payload["correlation_key"]
    assert data3["content_checksum"] == changed_payload["content_checksum"]

    # Verify database has exactly two versions
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(TMFDocument)
            .where(TMFDocument.correlation_key == payload["correlation_key"])
            .order_by(TMFDocument.version_index.asc())
        )
        res = await session.execute(stmt)
        docs = res.scalars().all()
        assert len(docs) == 2
        assert docs[0].version_index == 1
        assert docs[1].version_index == 2
        content_v2 = docs[1].content
        try:
            import base64

            content_v2 = base64.b64decode(content_v2).decode("utf-8")
        except Exception:
            pass
        assert content_v2 == "Dr. Smith CV content revised V2"


@pytest.mark.asyncio
async def test_redaction_derivative_safety() -> None:
    """
    Test that if a redacted derivative is already present in a correlation chain,
    subsequent raw-content sync for that correlation key is treated as a no-op.
    """
    client = TestClient(etmf_app)
    headers = get_auth_headers_with_user(
        user_id="eisf_sync_service",
        roles="system",
        change_reason="Redaction safety verification",
    )

    payload = {
        "study_id": "study-100",
        "site_id": "site-boston-01",
        "artifact_type": "Investigator CV",
        "filename": "cv_smith.pdf",
        "content": "Dr. Smith CV content with PII",
        "mime_type": "application/pdf",
        "correlation_key": "corr:study-100:site-boston-01:investigator-cv:smith",
        "source_system": "eISF",
    }

    # 1. Ingest raw document
    resp1 = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp1.status_code == 201
    raw_doc_id = resp1.json()["id"]

    # 2. Redact the raw document
    redact_headers = get_auth_headers_with_user(
        user_id="sponsor_dm",
        roles="sponsor_dm",
        change_reason="PII sanitization",
    )
    redact_payload = {
        "redacted_content": "Dr. Smith CV content with [REDACTED]",
        "redacted_filename": "cv_smith_redacted.pdf",
        "manifest": {"signature": "symm-sig-123", "categories_counts": {"PII": 1}},
    }
    resp_redact = client.post(
        f"/api/v1/etmf/documents/{raw_doc_id}/redact",
        json=redact_payload,
        headers=redact_headers,
    )
    assert resp_redact.status_code == 201
    redacted_data = resp_redact.json()
    assert redacted_data["is_redacted"] is True
    assert redacted_data["redaction_source_id"] == raw_doc_id

    # Add the correlation key to the redacted document (re-fetch and update since manual redaction does not inherit sync columns automatically, or let's verify if the redaction endpoint preserves it!)
    # Actually, let's verify if manual redaction copies correlation_key. Wait, manual redaction builds `redacted_doc` using fields from `source_doc`!
    # But wait, did we add correlation_key to the manual redaction build?
    # Let's check main.py manual/auto-redact endpoints.
    # Ah! In main.py manual/auto-redact, `redacted_doc` is instantiated with specific fields. Let's make sure `correlation_key` and sync/provenance fields are copied too!
    # Yes, we should check if they are copied. If not, let's fix that.
    # Let's write this test and then run it to verify. If it fails due to missing correlation_key on redacted_doc, we will add it to the redaction endpoint.

    # 3. Attempt raw sync for that same correlation key
    resp_raw_sync = client.post("/api/v1/etmf/ingest", json=payload, headers=headers)
    assert resp_raw_sync.status_code == 201
    assert resp_raw_sync.json()["result"] == "ignored"


@pytest.mark.asyncio
async def test_sealer_retains_and_validates_reason_for_change(monkeypatch) -> None:
    """
    Asserts that audit sealing correctly computes ledger hashes with reason_for_change,
    detects tampering on reason_for_change, and validates legacy rows where reason_for_change is NULL.
    """

    # Prevent trial locking attempt to hit actual network
    async def mock_lock(reason, is_testing=None):
        pass

    monkeypatch.setattr("apps.etmf.sealer.trigger_global_trial_lock", mock_lock)

    async with db_manager.get_session_maker()() as session:
        # Create a legacy unsealed row with reason_for_change=None
        legacy_log = TMFAuditLog(
            user_id="legacy-user",
            user_role="admin",
            action="VIEW",
            details="Legacy audit details",
            reason_for_change=None,
        )
        # Create a new unsealed row with reason_for_change populated
        new_log = TMFAuditLog(
            user_id="new-user",
            user_role="system",
            action="SIGN",
            details="New audit details",
            reason_for_change="Part 11 compliance reason",
        )
        session.add_all([legacy_log, new_log])
        await session.flush()

        # Seal them
        block_hash = await execute_etmf_audit_sealing_cycle(session, limit=10)
        assert block_hash is not None

        # Validate ledger integrity
        assert await validate_etmf_ledger_integrity(session) is True

        # Tamper with the new_log's reason_for_change and assert integrity check detects it
        new_log.reason_for_change = "Tampered reason"
        session.add(new_log)
        await session.flush()

        with pytest.raises(ValueError, match="Integrity violation"):
            await validate_etmf_ledger_integrity(session)
