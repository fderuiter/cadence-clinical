import contextlib
import os
import tempfile

import pytest
from sqlalchemy import select, text
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlmodel import Field as SQLField
from sqlmodel import SQLModel, create_engine

from packages.security.audit_logger import (
    AuditLoggerEngine,
    AuditLogPayload,
    DbAuditLogRecord,
    SQLModelAuditStore,
)


@pytest.fixture
def sync_engine():
    # Setup unique temporary SQLite database file
    fd, path = tempfile.mkstemp(suffix="-sync-audit.db")
    os.close(fd)
    db_url = f"sqlite:///{path}"
    engine = create_engine(db_url)
    yield engine
    # Clean up file
    with contextlib.suppress(OSError):
        os.remove(path)


@pytest.mark.asyncio
async def test_pluggable_sqlmodel_store_sync(sync_engine):
    """Verify that SQLModelAuditStore registers a sync database,
    saves events, blocks updates/deletes via triggers, and resumes chain.

    @req:PRD-SYS-001
    """
    # Create the store
    store = SQLModelAuditStore(sync_engine)
    store.initialize_db()

    # Initialize engine with the store
    engine = AuditLoggerEngine(secret_key="my-secret-key", store=store)

    # Log some events
    payload1 = AuditLogPayload(
        service_name="clinical-service",
        action_type="CREATE",
        entity_name="ConsentFormRecord",
        entity_id="consent-123",
        user_id="user-001",
        reason_for_change="initial enrollment",
    )
    rec1 = engine.log_event(payload1)

    payload2 = AuditLogPayload(
        service_name="clinical-service",
        action_type="SIGN",
        entity_name="ConsentFormRecord",
        entity_id="consent-123",
        user_id="user-001",
        reason_for_change="patient signed form",
    )
    rec2 = engine.log_event(payload2)

    # Check that it is saved in the database
    all_recs = store.fetch_all()
    assert len(all_recs) == 2
    assert all_recs[0].event_id == rec1.event_id
    assert all_recs[1].event_id == rec2.event_id
    assert all_recs[1].previous_digest == rec1.sha256_digest

    # Verify chain integrity
    assert engine.verify_chain_integrity() is True

    # Attempting to delete or alter a row in the audit database tables triggers an error (Requirement 4)
    with sync_engine.begin() as conn:
        with pytest.raises(
            Exception,
            match="Modification or deletion of audit logs is strictly prohibited",
        ):
            conn.execute(
                text(
                    "UPDATE security_audit_logs SET reason_for_change = 'hacked' WHERE event_id = :eid"
                ),
                {"eid": rec1.event_id},
            )

        with pytest.raises(
            Exception,
            match="Modification or deletion of audit logs is strictly prohibited",
        ):
            conn.execute(
                text("DELETE FROM security_audit_logs WHERE event_id = :eid"),
                {"eid": rec1.event_id},
            )

    # Test Startup resumption (Requirement 3): On microservice restart, the next generated audit record
    # correctly links to the digest of the last record saved before restart.
    new_engine_instance = AuditLoggerEngine(secret_key="my-secret-key", store=store)
    assert new_engine_instance.last_digest == rec2.sha256_digest

    payload3 = AuditLogPayload(
        service_name="clinical-service",
        action_type="LOCK",
        entity_name="ConsentFormRecord",
        entity_id="consent-123",
        user_id="user-crc",
        reason_for_change="lock applied",
    )
    rec3 = new_engine_instance.log_event(payload3)
    assert rec3.previous_digest == rec2.sha256_digest
    assert new_engine_instance.verify_chain_integrity() is True


@pytest.mark.asyncio
async def test_pluggable_sqlmodel_store_async():
    """Verify that SQLModelAuditStore registers an async database engine,
    handles asynchronous logging, blocks updates/deletes, and resumes chain.

    @req:PRD-SYS-001
    """
    # Setup asynchronous unique temporary SQLite database file
    fd, path = tempfile.mkstemp(suffix="-async-audit.db")
    os.close(fd)
    db_url = f"sqlite+aiosqlite:///{path}"
    async_engine = create_async_engine(db_url)

    try:
        # Create the store
        store = SQLModelAuditStore(async_engine)
        await store.initialize_db_async()

        # Initialize engine with the store
        engine = AuditLoggerEngine(secret_key="my-secret-key", store=store)

        # Log some events asynchronously
        payload1 = AuditLogPayload(
            service_name="clinical-service",
            action_type="CREATE",
            entity_name="ConsentFormRecord",
            entity_id="consent-123",
            user_id="user-001",
            reason_for_change="initial enrollment",
        )
        rec1 = await engine.log_event_async(payload1)

        payload2 = AuditLogPayload(
            service_name="clinical-service",
            action_type="SIGN",
            entity_name="ConsentFormRecord",
            entity_id="consent-123",
            user_id="user-001",
            reason_for_change="patient signed form",
        )
        rec2 = await engine.log_event_async(payload2)

        # Check that it is saved in the database
        all_recs = await store.fetch_all_async()
        assert len(all_recs) == 2
        assert all_recs[0].event_id == rec1.event_id
        assert all_recs[1].event_id == rec2.event_id
        assert all_recs[1].previous_digest == rec1.sha256_digest

        # Verify chain integrity
        assert await engine.verify_chain_integrity_async() is True

        # Attempting to delete or alter a row triggers error
        async with async_engine.begin() as conn:
            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await conn.execute(
                    text(
                        "UPDATE security_audit_logs SET reason_for_change = 'hacked' WHERE event_id = :eid"
                    ),
                    {"eid": rec1.event_id},
                )

            with pytest.raises(
                Exception,
                match="Modification or deletion of audit logs is strictly prohibited",
            ):
                await conn.execute(
                    text("DELETE FROM security_audit_logs WHERE event_id = :eid"),
                    {"eid": rec1.event_id},
                )

        # Test resumption
        new_engine_instance = AuditLoggerEngine(secret_key="my-secret-key", store=store)
        assert await new_engine_instance.get_last_digest_async() == rec2.sha256_digest

        payload3 = AuditLogPayload(
            service_name="clinical-service",
            action_type="LOCK",
            entity_name="ConsentFormRecord",
            entity_id="consent-123",
            user_id="user-crc",
            reason_for_change="lock applied",
        )
        rec3 = await new_engine_instance.log_event_async(payload3)
        assert rec3.previous_digest == rec2.sha256_digest
        assert await new_engine_instance.verify_chain_integrity_async() is True

    finally:
        await async_engine.dispose()
        with contextlib.suppress(OSError):
            os.remove(path)


@pytest.mark.asyncio
async def test_commit_within_active_transaction():
    """Verify that logging wrapper can commit the audit record within the active transaction.

    @req:PRD-SYS-001
    """
    # Create an async unique temporary SQLite database file
    fd, path = tempfile.mkstemp(suffix="-tx-audit.db")
    os.close(fd)
    db_url = f"sqlite+aiosqlite:///{path}"
    async_engine = create_async_engine(db_url)

    try:
        # Register the store
        store = SQLModelAuditStore(async_engine)
        await store.initialize_db_async()

        engine = AuditLoggerEngine(secret_key="my-secret-key", store=store)

        # Create custom tables
        class TestUserRecord(SQLModel, table=True):
            __tablename__ = "test_user_records"
            id: str = SQLField(primary_key=True)
            name: str

        async with async_engine.begin() as conn:
            await conn.run_sync(SQLModel.metadata.create_all)

        async_session_maker = async_sessionmaker(async_engine, expire_on_commit=False)

        # Case 1: Transaction rolls back, so audit log should also roll back
        async with async_session_maker() as session:
            async with session.begin():
                # Add clinical record
                user = TestUserRecord(id="u-1", name="Alice")
                session.add(user)

                # Log audit event passing the session
                payload = AuditLogPayload(
                    service_name="user-service",
                    action_type="CREATE",
                    entity_name="TestUserRecord",
                    entity_id="u-1",
                    user_id="admin",
                    reason_for_change="adding test user",
                )
                # Log inside transaction
                await engine.log_event_async(payload, session=session)

                # Intentionally rollback
                await session.rollback()

        # Verify no test_user_records and no security_audit_logs are created!
        async with async_session_maker() as session:
            users = (await session.execute(select(TestUserRecord))).scalars().all()
            logs = (await session.execute(select(DbAuditLogRecord))).scalars().all()
            assert len(users) == 0
            assert len(logs) == 0

        # Case 2: Transaction commits successfully, so both should exist
        async with async_session_maker() as session:
            async with session.begin():
                user = TestUserRecord(id="u-1", name="Alice")
                session.add(user)

                payload = AuditLogPayload(
                    service_name="user-service",
                    action_type="CREATE",
                    entity_name="TestUserRecord",
                    entity_id="u-1",
                    user_id="admin",
                    reason_for_change="adding test user",
                )
                await engine.log_event_async(payload, session=session)

        # Verify both records exist in DB now!
        async with async_session_maker() as session:
            users = (await session.execute(select(TestUserRecord))).scalars().all()
            logs = (await session.execute(select(DbAuditLogRecord))).scalars().all()
            assert len(users) == 1
            assert len(logs) == 1
            assert users[0].id == "u-1"
            assert logs[0].entity_id == "u-1"

    finally:
        await async_engine.dispose()
        with contextlib.suppress(OSError):
            os.remove(path)
