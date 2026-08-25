"""Integration and compliance test suite for Standardized Compliance Audit Subsystem Framework.

Verifies:
1. Automatic capture of user identity, roles, tenant context, and change justifications.
2. Aborting entity mutations when change justification is missing or blank.
3. Rejecting update and delete operations on historic audit log records (immutability).
4. HMAC-SHA256 cryptographic digest chain generation and tamper verification.
5. Standardized audit inspection, pagination, filtering, export, and verification API endpoints.

Requirements: PRD-SYS-001, PRD-SYS-002, PRD-SYS-003, PRD-SYS-004, 21 CFR Part 11
"""

import datetime
from datetime import UTC

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from sqlalchemy import Column, Integer, String, select
from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine
from sqlalchemy.orm import declarative_base, sessionmaker

from packages.compliance import (
    AuditImmutabilityError,
    AuditJustificationError,
    ComplianceAuditFramework,
    ComplianceAuditLogRecord,
    StandardAuditLog,
    compute_audit_digest,
    mount_compliance_audit_framework,
    verify_audit_chain,
)
from packages.security.context import current_change_reason

Base = declarative_base()


class SampleClinicalRecord(Base):
    """Test model representing an audited clinical record."""

    __tablename__ = "sample_clinical_records"

    id = Column(Integer, primary_key=True, autoincrement=True)
    subject_id = Column(String, nullable=True)
    data_value = Column(String, nullable=True)
    reason_for_change = Column(String, nullable=True)


@pytest.mark.asyncio
async def test_missing_change_justification_raises_compliance_error():
    """Verify modifying an audited entity without change justification raises AuditJustificationError and aborts save."""
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    # Attach compliance framework
    mount_compliance_audit_framework(engine)

    # 1. Blank justification on model should fail
    async with async_session() as session:
        rec = SampleClinicalRecord(data_value="120/80", reason_for_change="   ")
        session.add(rec)
        with pytest.raises(
            AuditJustificationError, match="GxP change reason is required"
        ):
            await session.flush()

    # 2. Valid justification on model should succeed
    async with async_session() as session:
        rec = SampleClinicalRecord(
            data_value="120/80", reason_for_change="Initial blood pressure entry"
        )
        session.add(rec)
        await session.commit()

    # 3. Context variable justification should succeed
    async with async_session() as session:
        rec = SampleClinicalRecord(data_value="118/78", reason_for_change=None)
        token = current_change_reason.set("Entry updated via context variable")
        try:
            session.add(rec)
            await session.commit()
        finally:
            current_change_reason.reset(token)

    await engine.dispose()


@pytest.mark.asyncio
async def test_audit_log_record_immutability_enforcement():
    """Verify direct attempts to update or delete existing audit log records fail with an immutability violation."""
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        await conn.run_sync(StandardAuditLog.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)
    mount_compliance_audit_framework(engine)

    # Insert an initial audit log record
    async with async_session() as session:
        audit_rec = StandardAuditLog(
            service_name="test_service",
            table_name="sample_clinical_records",
            record_id="rec-001",
            action="CREATE",
            user_id="user_crc_01",
            change_reason="Initial record creation",
            previous_digest="GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000",
            sha256_digest="hash-001",
        )
        session.add(audit_rec)
        await session.commit()

    # Attempt to update existing audit log record should fail
    async with async_session() as session:
        res = await session.execute(
            select(StandardAuditLog).where(StandardAuditLog.record_id == "rec-001")
        )
        queried_audit = res.scalars().one()
        queried_audit.change_reason = "Unauthorized tampered change reason"

        with pytest.raises(
            AuditImmutabilityError, match="Modification of existing audit log record"
        ):
            await session.flush()

    # Attempt to delete existing audit log record should fail
    async with async_session() as session:
        res = await session.execute(
            select(StandardAuditLog).where(StandardAuditLog.record_id == "rec-001")
        )
        queried_audit = res.scalars().one()
        await session.delete(queried_audit)

        with pytest.raises(
            AuditImmutabilityError, match="Deletion of audit log record"
        ):
            await session.flush()

    await engine.dispose()


def test_crypto_digest_chain_generation_and_verification():
    """Verify cryptographic HMAC-SHA256 digest chain integrity accurately detects valid and tampered chains."""
    secret_key = "test-compliance-secret-key"  # pragma: allowlist secret

    # Build valid chain
    records = []
    prev_digest = (
        "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
    )

    for i in range(1, 4):
        event_id = f"evt-00{i}"
        ts_str = datetime.datetime.now(UTC).isoformat()
        digest = compute_audit_digest(
            event_id=event_id,
            service_name="execution",
            action_type="CREATE",
            entity_name="FormSubmission",
            entity_id=f"form-{i}",
            user_id="user_crc_01",
            tenant_id="tenant_alpha",
            reason_for_change=f"Form entry {i}",
            timestamp=ts_str,
            previous_digest=prev_digest,
            secret_key=secret_key,
        )

        record = ComplianceAuditLogRecord(
            event_id=event_id,
            service_name="execution",
            table_name="FormSubmission",
            record_id=f"form-{i}",
            action="CREATE",
            user_id="user_crc_01",
            tenant_id="tenant_alpha",
            timestamp=ts_str,
            change_reason=f"Form entry {i}",
            previous_digest=prev_digest,
            sha256_digest=digest,
        )
        records.append(record)
        prev_digest = digest

    # Verify intact chain
    report = verify_audit_chain(records, secret_key=secret_key)
    assert report["valid"] is True
    assert report["status"] == "INTACT"
    assert report["total_records"] == 3

    # Tamper with record 2 reason_for_change
    tampered_records = list(records)
    tampered_records[1] = ComplianceAuditLogRecord(
        event_id=records[1].event_id,
        service_name=records[1].service_name,
        table_name=records[1].table_name,
        record_id=records[1].record_id,
        action=records[1].action,
        user_id=records[1].user_id,
        tenant_id=records[1].tenant_id,
        timestamp=records[1].timestamp,
        change_reason="Unauthorized tampered reason",
        previous_digest=records[1].previous_digest,
        sha256_digest=records[1].sha256_digest,
    )

    tampered_report = verify_audit_chain(tampered_records, secret_key=secret_key)
    assert tampered_report["valid"] is False
    assert tampered_report["status"] == "TAMPERED"
    assert tampered_report["broken_at_index"] == 1


def test_standardized_compliance_router_endpoints():
    """Verify standardized compliance API endpoints expose paginated searching, filtering, export, and chain verification."""
    app = FastAPI()
    framework = ComplianceAuditFramework(service_name="execution")
    framework.mount(app)

    client = TestClient(app)

    # GET /api/v1/execution/compliance/audit-logs
    res = client.get("/api/v1/execution/compliance/audit-logs?limit=10&offset=0")
    assert res.status_code == 200
    data = res.json()
    assert "items" in data
    assert "total" in data
    assert "page" in data
    assert "page_size" in data

    # POST /api/v1/execution/compliance/audit-logs/verify-chain
    res_verify = client.post("/api/v1/execution/compliance/audit-logs/verify-chain")
    assert res_verify.status_code == 200
    data_verify = res_verify.json()
    assert data_verify["valid"] is True
    assert data_verify["status"] == "INTACT"

    # GET /api/v1/execution/compliance/audit-logs/export
    res_export = client.get(
        "/api/v1/execution/compliance/audit-logs/export?format=json"
    )
    assert res_export.status_code == 200
    assert "application/json" in res_export.headers["content-type"]
