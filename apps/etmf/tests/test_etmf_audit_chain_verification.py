"""Unit and integration tests for cryptographic audit ledger verification and tamper detection."""

import time

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import text

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base
from apps.etmf.adapters.sealer import execute_etmf_audit_sealing_cycle
from apps.etmf.main import app
from packages.testing.security import generate_signature


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


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
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
async def test_audit_chain_verification_endpoint():
    """Verify cryptographic audit ledger chain verification endpoint."""
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Generate audit logs for verification",
    )

    # 1. Ingest documents to create audit logs
    for i in range(3):
        client.post(
            "/api/v1/etmf/ingest",
            json={
                "study_id": "STUDY-SEAL-1",
                "artifact_type": "Clinical Trial Protocol",
                "filename": f"protocol_{i}.pdf",
                "content": f"Protocol content {i}",
                "mime_type": "application/pdf",
            },
            headers=admin_headers,
        )

    # 2. Execute audit sealing cycle
    async with db_manager.get_session_maker()() as session:
        block_hash = await execute_etmf_audit_sealing_cycle(session)
        assert block_hash is not None

    # 3. Call verify-chain endpoint
    auditor_headers = get_auth_headers(
        roles="regulatory_inspector,auditor",
        change_reason="Audit ledger verification inspection",
    )
    resp = client.post(
        "/api/v1/etmf/audit-logs/verify-chain",
        headers=auditor_headers,
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["is_valid"] is True
    assert data["total_sealed_blocks"] == 1
    assert data["total_sealed_records"] >= 3
    assert data["tamper_detected"] is False
    assert data["latest_block_hash"] == block_hash


@pytest.mark.asyncio
async def test_audit_chain_tamper_detection():
    """Verify tamper detection when an audit log is modified."""
    client = TestClient(app)
    admin_headers = get_auth_headers(
        roles="sysadmin,sponsor_designer",
        change_reason="Create initial logs",
    )

    # 1. Ingest document
    client.post(
        "/api/v1/etmf/ingest",
        json={
            "study_id": "STUDY-TAMPER-1",
            "artifact_type": "Clinical Trial Protocol",
            "filename": "protocol_tamper.pdf",
            "content": "Original untampered content",
            "mime_type": "application/pdf",
        },
        headers=admin_headers,
    )

    # 2. Seal block
    async with db_manager.get_session_maker()() as session:
        await execute_etmf_audit_sealing_cycle(session)

    # 3. Tamper with sealed audit log record details in DB
    async with db_manager.get_session_maker()() as session:
        await session.execute(
            text(
                "UPDATE tmf_audit_logs SET details = 'TAMPERED DETAILS' WHERE action = 'INGEST';"
            )
        )
        await session.commit()

    # 4. Verify chain detects tampering
    auditor_headers = get_auth_headers(
        roles="regulatory_inspector,auditor",
        change_reason="Audit ledger verification inspection",
    )
    resp = client.post(
        "/api/v1/etmf/audit-logs/verify-chain",
        headers=auditor_headers,
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data["is_valid"] is False
    assert data["tamper_detected"] is True
    assert (
        "invalid" in data["details"].lower()
        or "mismatch" in data["details"].lower()
        or "broken" in data["details"].lower()
    )
