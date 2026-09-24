"""Cryptographic audit chain verification and tamper-detection integration tests for eISF.

Requirements: PRD-SYS-001, PRD-SYS-003 | 21 CFR Part 11 Regulated
"""

import pytest
import pytest_asyncio
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.eisf.adapters.models import Base, ISFAuditLog
from apps.eisf.adapters.repositories import SQLEISFRepository
from packages.compliance.audit import verify_audit_chain


@pytest_asyncio.fixture
async def eisf_db_session():
    """Create an isolated in-memory SQLite database session for eISF audit testing."""
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
async def test_eisf_audit_log_sequential_hmac_chaining(eisf_db_session: AsyncSession):
    """Validate that eISF audit log entries establish a continuous HMAC-SHA-256 hash chain.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLEISFRepository(session=eisf_db_session)

    # 1. Create three consecutive audit logs
    log1 = ISFAuditLog(
        actor_id="user_crc_01",
        actor_role="Site Coordinator",
        action="DOCUMENT_UPLOADED",
        document_id="ISF-DOC-101",
        details="Uploaded FDA Form 1572 v3.0 to Section 01",
        reason_for_change="Initial regulatory submission",
        tenant_id="tenant_gamma",
    )
    await repo.save_audit_log(log1)

    log2 = ISFAuditLog(
        actor_id="user_pi_01",
        actor_role="Principal Investigator",
        action="DOCUMENT_SIGNED",
        document_id="ISF-DOC-101",
        details="PI signed Form 1572 v3.0 with Part 11 digital signature",
        reason_for_change="Investigator sign-off",
        tenant_id="tenant_gamma",
    )
    await repo.save_audit_log(log2)

    log3 = ISFAuditLog(
        actor_id="user_cra_01",
        actor_role="CRA",
        action="DOCUMENT_VERIFIED",
        document_id="ISF-DOC-101",
        details="CRA verified completeness of Section 01 filing",
        reason_for_change="Remote monitoring review",
        tenant_id="tenant_gamma",
    )
    await repo.save_audit_log(log3)

    # 2. Retrieve all records in chronological order
    stmt = select(ISFAuditLog).order_by(
        ISFAuditLog.timestamp.asc(), ISFAuditLog.id.asc()
    )
    res = await eisf_db_session.execute(stmt)
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
        setattr(r, "service_name", "apps/eisf")
        setattr(r, "entity_name", "ISFAuditLog")
        setattr(r, "entity_id", r.document_id or r.id)
        setattr(r, "user_id", r.actor_id)

    result = verify_audit_chain(records)
    assert result["valid"] is True, f"Chain verification failed: {result.get('error')}"
    assert result["status"] == "INTACT"
    assert result["verified_count"] == 3


@pytest.mark.asyncio
async def test_eisf_audit_log_tamper_detection(eisf_db_session: AsyncSession):
    """Validate that unauthorized modification of historical eISF audit records is detected.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLEISFRepository(session=eisf_db_session)

    for i in range(3):
        await repo.save_audit_log(
            ISFAuditLog(
                actor_id=f"user_0{i}",
                actor_role="CRC",
                action="DOCUMENT_ACCESSED",
                document_id="ISF-DOC-202",
                details=f"Viewed binder section {i}",
                reason_for_change="Routine access",
                tenant_id="tenant_gamma",
            )
        )

    stmt = select(ISFAuditLog).order_by(
        ISFAuditLog.timestamp.asc(), ISFAuditLog.id.asc()
    )
    res = await eisf_db_session.execute(stmt)
    records = list(res.scalars().all())

    for r in records:
        setattr(r, "service_name", "apps/eisf")
        setattr(r, "entity_name", "ISFAuditLog")
        setattr(r, "entity_id", r.document_id or r.id)
        setattr(r, "user_id", r.actor_id)

    # Tamper with record 2 previous_digest
    records[2].previous_digest = "0" * 64

    result = verify_audit_chain(records)
    assert result["valid"] is False
    assert result["status"] == "TAMPERED"
    assert result["broken_at_index"] == 2
