"""Cryptographic audit chain verification and tamper-detection integration tests for eConsent.

Requirements: PRD-SYS-001, PRD-SYS-003 | 21 CFR Part 11 Regulated
"""

from datetime import UTC, datetime

import pytest
import pytest_asyncio
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.econsent.adapters.models import Base, ConsentAuditLog
from apps.econsent.domain.entities import ConsentAuditLogEntity
from apps.econsent.infrastructure.repositories import SQLConsentAuditRepository
from packages.compliance.audit import verify_audit_chain


@pytest_asyncio.fixture
async def econsent_db_session():
    """Create an isolated in-memory SQLite database session for eConsent audit testing."""
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
async def test_econsent_audit_log_sequential_hmac_chaining(
    econsent_db_session: AsyncSession,
):
    """Validate that eConsent audit log entries establish a continuous HMAC-SHA-256 hash chain.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLConsentAuditRepository(session=econsent_db_session)

    # 1. Create three consecutive audit logs
    await repo.log_action(
        actor_id="patient_001",
        actor_role="Subject",
        action="CLAUSE_ACCEPTED",
        document_id="DOC-991",
        details="Subject accepted Clause 1 (Procedure Details)",
        reason_for_change="Primary electronic consent progression",
    )

    await repo.log_action(
        actor_id="patient_001",
        actor_role="Subject",
        action="CONSENT_SIGNED",
        document_id="DOC-991",
        details="Subject executed digital signature on ICF v2.0",
        reason_for_change="Completed informed consent",
    )

    log3_entity = ConsentAuditLogEntity(
        id=None,
        timestamp=datetime.now(UTC),
        actor_id="pi_user_01",
        actor_role="Principal Investigator",
        action="INVESTIGATOR_COUNTERSIGNED",
        document_id="DOC-991",
        details="PI countersigned ICF v2.0 for patient_001",
        reason_for_change="Investigator verification",
        tenant_id="tenant_beta",
    )
    await repo.save(log3_entity)

    # 2. Retrieve all records in chronological order
    stmt = select(ConsentAuditLog).order_by(
        ConsentAuditLog.timestamp.asc(), ConsentAuditLog.id.asc()
    )
    res = await econsent_db_session.execute(stmt)
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
        setattr(r, "service_name", "apps/econsent")
        setattr(r, "entity_name", "ConsentAuditLog")
        setattr(r, "entity_id", r.document_id or r.id)
        setattr(r, "user_id", r.actor_id)

    result = verify_audit_chain(records)
    assert result["valid"] is True, f"Chain verification failed: {result.get('error')}"
    assert result["status"] == "INTACT"
    assert result["verified_count"] == 3


@pytest.mark.asyncio
async def test_econsent_audit_log_tamper_detection(econsent_db_session: AsyncSession):
    """Validate that unauthorized modification of historical eConsent audit records is detected.

    @req:PRD-SYS-001
    @req:PRD-SYS-003
    """
    repo = SQLConsentAuditRepository(session=econsent_db_session)

    for i in range(3):
        await repo.log_action(
            actor_id=f"actor_0{i}",
            actor_role="Subject",
            action="CLAUSE_VIEWED",
            document_id="DOC-992",
            details=f"Viewed section {i}",
            reason_for_change="Audit review",
        )

    stmt = select(ConsentAuditLog).order_by(
        ConsentAuditLog.timestamp.asc(), ConsentAuditLog.id.asc()
    )
    res = await econsent_db_session.execute(stmt)
    records = list(res.scalars().all())

    for r in records:
        setattr(r, "service_name", "apps/econsent")
        setattr(r, "entity_name", "ConsentAuditLog")
        setattr(r, "entity_id", r.document_id or r.id)
        setattr(r, "user_id", r.actor_id)

    # Tamper with record 1 action type
    records[1].action = "UNAUTHORIZED_MODIFICATION"

    result = verify_audit_chain(records)
    assert result["valid"] is False
    assert result["status"] == "TAMPERED"
    assert result["broken_at_index"] == 1
