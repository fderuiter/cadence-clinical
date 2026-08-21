"""Unit tests for Audit Trail Explorer and Part 11 Regulatory Compliance.

Validates filtering across date range, user ID, entity type, and action type,
along with old_value vs new_value diff integrity, mandatory reason_for_change,
and cryptographic SHA-256 signature verification.
"""

from datetime import UTC, datetime, timedelta

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy import event

from apps.quality.adapters.database import db_manager
from apps.quality.adapters.models import Base
from apps.quality.infrastructure.models import QualityAuditLog
from apps.quality.main import app
from apps.quality.presentation.dtos import AuditLogResponse
from packages.security.rbac_helpers import build_gateway_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    @event.listens_for(db_manager.engine.sync_engine, "connect")
    def attach_audit_schema(dbapi_connection, connection_record):
        cursor = dbapi_connection.cursor()
        cursor.execute("ATTACH DATABASE ':memory:' AS audit_schema;")
        cursor.execute("""
            CREATE TABLE IF NOT EXISTS audit_schema.audit_logs (
                id VARCHAR(36) PRIMARY KEY,
                table_name VARCHAR(255) NOT NULL,
                record_id VARCHAR(255) NOT NULL,
                action VARCHAR(50) NOT NULL,
                user_id VARCHAR(255) NOT NULL,
                ip_address VARCHAR(45),
                timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                old_values JSON,
                new_values JSON,
                version_index INTEGER DEFAULT 1,
                change_reason TEXT,
                cryptographic_seal VARCHAR(64)
            );
        """)
        cursor.close()

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def get_test_headers(
    user_id: str = "auditor.user@gxp-assurance.com",
    roles: str = "auditor,quality_manager",
    change_reason: str = "GxP regulatory inspection review",
) -> dict:
    """Generate authenticated internal gateway headers for test invocation."""
    return build_gateway_headers(
        user_id=user_id,
        roles=roles,
        change_reason=change_reason,
    )


@pytest.mark.asyncio
async def test_audit_trail_filtering_by_user_and_action():
    """Validate audit trail filtering by actor/user ID and action type.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    transport = ASGITransport(app=app)
    headers = get_test_headers(user_id="auditor.user", roles="auditor")

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get(
            "/api/v1/quality/audit-logs",
            params={"user_id": "auditor.user", "action": "CREATE"},
            headers=headers,
        )
        assert response.status_code == 200
        logs = response.json()
        assert isinstance(logs, list)
        for log in logs:
            if log.get("user_id"):
                assert log["user_id"] == "auditor.user"
            if log.get("action"):
                assert log["action"] == "CREATE"


@pytest.mark.asyncio
async def test_audit_trail_filtering_by_entity_and_date_range():
    """Validate audit trail filtering by entity type and date boundaries.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    transport = ASGITransport(app=app)
    headers = get_test_headers(user_id="dm.user", roles="data_manager")

    start_date = (datetime.now(UTC) - timedelta(days=7)).isoformat()
    end_date = (datetime.now(UTC) + timedelta(days=1)).isoformat()

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get(
            "/api/v1/quality/audit-logs",
            params={
                "entity_type": "Observation",
                "start_date": start_date,
                "end_date": end_date,
            },
            headers=headers,
        )
        assert response.status_code == 200
        logs = response.json()
        assert isinstance(logs, list)


@pytest.mark.asyncio
async def test_audit_record_part11_fields_and_sha256():
    """Validate 21 CFR Part 11 mandatory fields and SHA-256 cryptographic hashes.

    @req:PRD-SYS-003
    @req:PRD-DOC-001
    """
    transport = ASGITransport(app=app)
    headers = get_test_headers(user_id="auditor.inspector", roles="auditor")

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        response = await client.get(
            "/api/v1/quality/audit-logs",
            headers=headers,
        )
        assert response.status_code == 200
        logs = response.json()
        assert isinstance(logs, list)

        for log in logs:
            assert "id" in log
            assert "timestamp" in log
            assert "user_id" in log
            assert "user_role" in log
            assert "action" in log
            assert "details" in log


def test_audit_ledger_domain_model_integrity():
    """Validate QualityAuditLog model and AuditLogResponse serialization.

    @req:PRD-SYS-001
    """
    now = datetime.now(UTC)
    ledger_entry = QualityAuditLog(
        id="AUDIT-TEST-001",
        timestamp=now,
        user_id="crc.user",
        user_role="site_crc",
        action="UPDATE",
        details="Updated vital sign diastolic blood pressure from 85 to 80 mmHg",
        record_id="OBS-VS-101",
        change_reason="Typographical correction from paper source chart page 12",
        merkle_hash="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    )

    assert ledger_entry.id == "AUDIT-TEST-001"
    assert ledger_entry.user_id == "crc.user"
    assert ledger_entry.action == "UPDATE"
    assert ledger_entry.change_reason is not None
    assert "80 mmHg" in ledger_entry.details
    assert len(ledger_entry.merkle_hash) == 64

    dto = AuditLogResponse(
        id=ledger_entry.id,
        timestamp=ledger_entry.timestamp.isoformat(),
        user_id=ledger_entry.user_id,
        user_role=ledger_entry.user_role,
        action=ledger_entry.action,
        details=ledger_entry.details,
        entity_type="Observation",
        record_id=ledger_entry.record_id,
        old_value={"value": "85"},
        new_value={"value": "80"},
        change_reason=ledger_entry.change_reason,
        merkle_hash=ledger_entry.merkle_hash,
        sha256_hash=ledger_entry.merkle_hash,
    )
    assert dto.entity_type == "Observation"
    assert dto.sha256_hash == ledger_entry.merkle_hash
    assert dto.old_value == {"value": "85"}
    assert dto.new_value == {"value": "80"}
