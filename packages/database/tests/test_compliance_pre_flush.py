"""Tests for GxP pre-flush compliance verification, trial/site freeze enforcement, and UTC datetime column constraints.

@req:PRD-SYS-001
@req:PRD-SYS-002
@req:PRD-SYS-003
@req:PRD-SYS-004
@req:PRD-SYS-005
"""

import datetime

import pytest
from sqlalchemy import Column, Integer, String, select
from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine
from sqlalchemy.orm import declarative_base, sessionmaker

from packages.database import (
    AuditJustificationError,
    ComplianceError,
    UTCDateTime,
    map_database_exceptions,
)
from packages.hexagonal import (
    DatabaseError,
    EntityAlreadyExistsError,
    EntityNotFoundError,
)
from packages.security.context import current_change_reason

Base = declarative_base()


class ComplianceTestClinicalRecord(Base):
    __tablename__ = "compliance_test_clinical_records"
    __module__ = "apps.econsent.test_dummy"  # Bypass external execution audit listener

    id = Column(Integer, primary_key=True, autoincrement=True)
    site_id = Column(String, nullable=True)
    data = Column(String, nullable=True)
    reason_for_change = Column(String, nullable=True)
    created_at = Column(UTCDateTime, nullable=True)


@pytest.mark.asyncio
async def test_utc_datetime_type_decorator_enforcement():
    """Verify UTCDateTime type decorator rejects naive datetimes and normalizes aware datetimes.

    @req:PRD-SYS-004
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    # Test rejecting naive datetime
    async with async_session() as session:
        naive_dt = datetime.datetime.now()
        record = ComplianceTestClinicalRecord(
            data="naive test", created_at=naive_dt, reason_for_change="Initial creation"
        )
        session.add(record)
        import sqlalchemy.exc

        with pytest.raises(
            sqlalchemy.exc.StatementError, match="Naive datetimes are not allowed."
        ):
            await session.flush()

    # Test accepting aware datetime (normalize to UTC)
    async with async_session() as session:
        aware_dt = datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=2)))
        record = ComplianceTestClinicalRecord(
            data="aware test", created_at=aware_dt, reason_for_change="Initial creation"
        )
        session.add(record)
        await session.commit()

    async with async_session() as session:
        # Query and assert timezone-aware UTC datetime
        stmt = select(ComplianceTestClinicalRecord).where(
            ComplianceTestClinicalRecord.data == "aware test"
        )
        res = await session.execute(stmt)
        queried_record = res.scalars().one()
        assert queried_record.created_at.tzinfo == datetime.UTC
        # Difference should be minimal (due to floating point/seconds)
        assert abs((queried_record.created_at - aware_dt).total_seconds()) < 1.0

    await engine.dispose()


@pytest.mark.asyncio
async def test_pre_flush_justification_rejection():
    """Verify that records representing audited clinical data lacking justifications are rejected on flush.

    @req:PRD-SYS-001
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    # Test with empty justification on model
    async with async_session() as session:
        record = ComplianceTestClinicalRecord(
            data="clinical value 1",
            reason_for_change="   ",  # only whitespace
        )
        session.add(record)
        with pytest.raises(
            AuditJustificationError, match="Reason for change cannot be empty"
        ):
            await session.flush()

    # Test with no justification on model and no context variable
    async with async_session() as session:
        record = ComplianceTestClinicalRecord(
            data="clinical value 2",
            reason_for_change=None,
        )
        # Clear/ensure context variable isn't set to a non-blank custom reason
        token = current_change_reason.set("")
        try:
            session.add(record)
            with pytest.raises(
                AuditJustificationError, match="Reason for change cannot be empty"
            ):
                await session.flush()
        finally:
            current_change_reason.reset(token)

    # Test with valid justification on model
    async with async_session() as session:
        record = ComplianceTestClinicalRecord(
            data="clinical value 3",
            reason_for_change="Valid justification reason",
        )
        session.add(record)
        await session.flush()  # should succeed without exception

    # Test with justification set via context variable
    async with async_session() as session:
        record = ComplianceTestClinicalRecord(
            data="clinical value 4",
            reason_for_change=None,
        )
        token = current_change_reason.set("Justified via context var")
        try:
            session.add(record)
            await session.flush()  # should succeed
        finally:
            current_change_reason.reset(token)

    await engine.dispose()


@pytest.mark.asyncio
async def test_trial_freeze_blocking_writes():
    """Verify that an active trial freeze blocks all writes before hitting the database.

    @req:PRD-SYS-002
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    from packages.database import _TRIAL_LOCK_CHECKERS, register_trial_lock_checker

    # Register mock lock checker
    lock_active = True

    def mock_checker():
        return lock_active

    register_trial_lock_checker(mock_checker)

    try:
        async with async_session() as session:
            record = ComplianceTestClinicalRecord(
                data="frozen write", reason_for_change="Valid reason"
            )
            session.add(record)
            with pytest.raises(ComplianceError, match="Trial is currently locked"):
                await session.flush()

        # Disable lock and try again
        lock_active = False
        async with async_session() as session:
            record = ComplianceTestClinicalRecord(
                data="unfrozen write", reason_for_change="Valid reason"
            )
            session.add(record)
            await session.flush()  # should succeed

    finally:
        _TRIAL_LOCK_CHECKERS.clear()
        await engine.dispose()


@pytest.mark.asyncio
async def test_site_freeze_blocking_writes():
    """Verify that an active site freeze blocks write operations for that specific site.

    @req:PRD-SYS-002
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:")
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async_session = sessionmaker(engine, class_=AsyncSession, expire_on_commit=False)

    from packages.database import _SITE_LOCK_CHECKERS, register_site_lock_checker

    # Lock site-101 via mock callback
    locked_sites = {"site-101"}
    register_site_lock_checker(lambda s: s in locked_sites)

    try:
        # Write to locked site should fail
        async with async_session() as session:
            record = ComplianceTestClinicalRecord(
                site_id="site-101",
                data="locked site write",
                reason_for_change="Valid reason",
            )
            session.add(record)
            with pytest.raises(
                ComplianceError, match="Site site-101 is currently locked"
            ):
                await session.flush()

        # Write to unlocked site should succeed
        async with async_session() as session:
            record = ComplianceTestClinicalRecord(
                site_id="site-202",
                data="unlocked site write",
                reason_for_change="Valid reason",
            )
            session.add(record)
            await session.flush()

    finally:
        _SITE_LOCK_CHECKERS.clear()
        await engine.dispose()


def test_sync_and_async_map_database_exceptions():
    """Verify map_database_exceptions decorator wraps both sync and async functions correctly.

    @req:PRD-SYS-005
    """
    from sqlalchemy.exc import IntegrityError, NoResultFound, SQLAlchemyError

    # 1. Async mapping
    @map_database_exceptions
    async def async_fail(err_type):
        if err_type == "no_result":
            raise NoResultFound("not found")
        if err_type == "integrity":
            raise IntegrityError("stmt", {}, Exception("dup"))
        raise SQLAlchemyError("generic")

    # 2. Sync mapping
    @map_database_exceptions
    def sync_fail(err_type):
        if err_type == "no_result":
            raise NoResultFound("not found")
        if err_type == "integrity":
            raise IntegrityError("stmt", {}, Exception("dup"))
        raise SQLAlchemyError("generic")

    # Verify Async
    import asyncio

    with pytest.raises(EntityNotFoundError):
        asyncio.run(async_fail("no_result"))
    with pytest.raises(EntityAlreadyExistsError):
        asyncio.run(async_fail("integrity"))
    with pytest.raises(DatabaseError):
        asyncio.run(async_fail("generic"))

    # Verify Sync
    with pytest.raises(EntityNotFoundError):
        sync_fail("no_result")
    with pytest.raises(EntityAlreadyExistsError):
        sync_fail("integrity")
    with pytest.raises(DatabaseError):
        sync_fail("generic")
