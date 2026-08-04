import asyncio

import pytest
import pytest_asyncio
from sqlalchemy import Integer, String, select
from sqlalchemy.orm import Mapped, declarative_base, mapped_column

from packages.database import (
    RelationalDatabaseManager,
    get_session,
    transactional,
)

Base = declarative_base()


class TestDoc(Base):
    __tablename__ = "isf_documents"
    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    title: Mapped[str] = mapped_column(String(255), nullable=False)


@pytest_asyncio.fixture
async def db_manager():
    mgr = RelationalDatabaseManager(service_name="TestService")
    mgr.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with mgr.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield mgr
    await mgr.close()


@pytest.mark.asyncio
async def test_nested_transaction_savepoint_rollback(db_manager):
    """
    Test scenario: Handling Nested Failures with Audit Logs.
    An outer transaction succeeds, but a nested transaction fails and rolls back to a savepoint.
    The outer transaction can still commit successfully.
    """
    session_maker = db_manager.get_session_maker()

    @transactional(session_maker)
    async def nested_failing_operation():
        session = get_session()
        doc = TestDoc(title="Nested Doc (Should be rolled back)")
        session.add(doc)
        await session.flush()
        raise ValueError("Simulated nested failure")

    @transactional(session_maker)
    async def outer_operation():
        session = get_session()
        doc = TestDoc(title="Outer Doc (Should be committed)")
        session.add(doc)
        await session.flush()

        import contextlib

        with contextlib.suppress(ValueError):
            await nested_failing_operation()

        # Let's add an audit/rejection log
        audit_doc = TestDoc(title="Outer Audit Log (Should be committed)")
        session.add(audit_doc)
        await session.flush()

    await outer_operation()

    # Query the database to verify that:
    # 1. Outer Doc and Outer Audit Log are committed.
    # 2. Nested Doc is rolled back.
    async with session_maker() as session:
        res = await session.execute(select(TestDoc))
        docs = res.scalars().all()
        titles = {doc.title for doc in docs}
        assert "Outer Doc (Should be committed)" in titles
        assert "Outer Audit Log (Should be committed)" in titles
        assert "Nested Doc (Should be rolled back)" not in titles
        assert len(docs) == 2


@pytest.mark.asyncio
async def test_implicit_sharing_of_session(db_manager):
    """
    Verify that nested service calls share the exact same active database session.
    """
    session_maker = db_manager.get_session_maker()

    @transactional(session_maker)
    async def nested_op(parent_session):
        session = get_session()
        assert session is parent_session
        doc = TestDoc(title="Nested shared doc")
        session.add(doc)

    @transactional(session_maker)
    async def root_op():
        session = get_session()
        await nested_op(session)

    await root_op()

    async with session_maker() as s:
        res = await s.execute(select(TestDoc))
        docs = res.scalars().all()
        assert len(docs) == 1
        assert docs[0].title == "Nested shared doc"


@pytest.mark.asyncio
async def test_concurrent_async_sessions_isolated(db_manager):
    """
    Verify that concurrent async executions run in isolated sessions without leakage.
    """
    session_maker = db_manager.get_session_maker()
    sessions_seen = set()

    @transactional(session_maker)
    async def run_in_parallel(delay: float):
        session = get_session()
        sessions_seen.add(session)
        await asyncio.sleep(delay)
        # Verify the session is still the correct one in context
        assert get_session() is session

    await asyncio.gather(
        run_in_parallel(0.05),
        run_in_parallel(0.05),
        run_in_parallel(0.05),
    )

    # We ran three in parallel, they should all have distinct, unique sessions
    assert len(sessions_seen) == 3


@pytest.mark.asyncio
async def test_root_transaction_failure_cleans_up_and_rolls_back(db_manager):
    """
    Verify that root transaction failure rolls back everything and cleans up the context.
    """
    session_maker = db_manager.get_session_maker()

    @transactional(session_maker)
    async def failing_root():
        session = get_session()
        doc = TestDoc(title="Failed doc")
        session.add(doc)
        await session.flush()
        raise RuntimeError("Fail root")

    with pytest.raises(RuntimeError, match="Fail root"):
        await failing_root()

    # The current_session ContextVar should have been reset to None
    with pytest.raises(RuntimeError, match="No active database session found"):
        get_session()

    async with session_maker() as s:
        res = await s.execute(select(TestDoc))
        docs = res.scalars().all()
        assert len(docs) == 0
