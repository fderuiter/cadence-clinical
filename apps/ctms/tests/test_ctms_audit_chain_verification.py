"""Cryptographic audit chain verification and tamper-detection integration tests for CTMS.

Requirements: PRD-SYS-001, PRD-SYS-003 | 21 CFR Part 11 Regulated
"""

import pytest
import pytest_asyncio
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.ctms.adapters.repositories.ctms_delegation_repository import (
    SQLAlchemyCTMSDelegationRepository,
)
from apps.ctms.domain.models import CTMSAuditLogEntity
from apps.ctms.models import Base, CTMSAuditLog
from packages.compliance.audit import verify_audit_chain


@pytest_asyncio.fixture
async def ctms_db_session():
    """Create an isolated in-memory SQLite database session for CTMS audit testing."""
    engine = create_async_engine("sqlite+aiosqlite:///:memory:", echo=False)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_factory = async_sessionmaker(
        bind=engine, class_=AsyncSession, expire_on_commit=False
    )
    async with session_factory() as session:
        yield session

    await engine.dispose()


@pytest.mark.asyncio
async def test_ctms_audit_log_sequential_hmac_chaining(ctms_db_session: AsyncSession):
    """Validate that CTMS audit log entries establish a continuous HMAC-SHA-256 hash chain.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLAlchemyCTMSDelegationRepository(session=ctms_db_session)

    # 1. Create three consecutive audit log entries
    log1 = CTMSAuditLogEntity(
        user_id="user_ctms_01",
        user_role="CRA",
        action="SITE_INITIATION_VISIT",
        details="Conducted SIV at Site 101",
        tenant_id="tenant_alpha",
        reason_for_change="Scheduled initiation",
    )
    await repo.save_audit_log(log1)

    log2 = CTMSAuditLogEntity(
        user_id="user_ctms_02",
        user_role="PI",
        action="DOA_LOG_MODIFIED",
        details="Added sub-investigator to delegation log for Site 101",
        tenant_id="tenant_alpha",
        reason_for_change="Staff addition",
    )
    await repo.save_audit_log(log2)

    log3 = CTMSAuditLogEntity(
        user_id="user_ctms_03",
        user_role="Data Manager",
        action="MILESTONE_APPROVED",
        details="Approved FPFV milestone for Site 101",
        tenant_id="tenant_alpha",
        reason_for_change="Milestone verification complete",
    )
    await repo.save_audit_log(log3)

    # 2. Retrieve all saved logs in chronological order
    stmt = select(CTMSAuditLog).order_by(
        CTMSAuditLog.timestamp.asc(), CTMSAuditLog.id.asc()
    )
    res = await ctms_db_session.execute(stmt)
    records = list(res.scalars().all())

    assert len(records) == 3

    # 3. Assert Genesis Block on initial entry
    genesis = (
        "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
    )
    assert records[0].previous_digest == genesis
    assert records[0].sha256_digest is not None
    assert len(records[0].sha256_digest) == 64

    # 4. Assert Sequential Chaining
    assert records[1].previous_digest == records[0].sha256_digest
    assert records[1].sha256_digest is not None
    assert len(records[1].sha256_digest) == 64

    assert records[2].previous_digest == records[1].sha256_digest
    assert records[2].sha256_digest is not None
    assert len(records[2].sha256_digest) == 64

    # 5. Verify cryptographic chain validity
    for r in records:
        setattr(r, "service_name", "apps/ctms")
        setattr(r, "entity_name", "CTMSAuditLog")
        setattr(r, "entity_id", r.id)

    result = verify_audit_chain(records)
    assert result["valid"] is True, f"Chain verification failed: {result.get('error')}"
    assert result["status"] == "INTACT"
    assert result["verified_count"] == 3


@pytest.mark.asyncio
async def test_ctms_audit_log_tamper_detection(ctms_db_session: AsyncSession):
    """Validate that unauthorized modification of historical CTMS audit records is detected.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLAlchemyCTMSDelegationRepository(session=ctms_db_session)

    for i in range(3):
        await repo.save_audit_log(
            CTMSAuditLogEntity(
                user_id=f"user_0{i}",
                user_role="Monitor",
                action="MONITORING_VISIT",
                details=f"Visit report {i}",
                tenant_id="tenant_alpha",
                reason_for_change="Routine monitoring",
            )
        )

    stmt = select(CTMSAuditLog).order_by(
        CTMSAuditLog.timestamp.asc(), CTMSAuditLog.id.asc()
    )
    res = await ctms_db_session.execute(stmt)
    records = list(res.scalars().all())

    for r in records:
        setattr(r, "service_name", "apps/ctms")
        setattr(r, "entity_name", "CTMSAuditLog")
        setattr(r, "entity_id", r.id)

    # Tamper with record 1 reason
    records[1].reason_for_change = "Tampered reason"

    result = verify_audit_chain(records)
    assert result["valid"] is False
    assert result["status"] == "TAMPERED"
    assert result["broken_at_index"] == 1
