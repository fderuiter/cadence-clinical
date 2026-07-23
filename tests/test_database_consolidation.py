import asyncio
import os
import uuid
import pytest
from sqlalchemy import ForeignKey, Integer, String, select, text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column

from packages.database import DatabaseSessionManager
from packages.security.context import current_change_reason, current_user_id


class TestBase(DeclarativeBase):
    pass


class Parent(TestBase):
    __tablename__ = "consolidation_parents"
    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    name: Mapped[str] = mapped_column(String(50))


class Child(TestBase):
    __tablename__ = "consolidation_children"
    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    parent_id: Mapped[int] = mapped_column(
        Integer, ForeignKey("consolidation_parents.id", ondelete="CASCADE"), nullable=False
    )


@pytest.mark.asyncio
async def test_sqlite_foreign_key_enforcement():
    """
    Verify that relational integrity constraints (foreign keys)
    are globally and consistently enforced on SQLite database connections.
    """
    db = DatabaseSessionManager()
    db.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    try:
        # Create tables
        async with db.engine.begin() as conn:
            await conn.run_sync(TestBase.metadata.create_all)

        # Attempting to insert a child record with a non-existent parent_id
        # must fail with an IntegrityError if foreign key constraints are ON.
        with pytest.raises(IntegrityError):
            async with db.get_session_maker()() as session:
                async with session.begin():
                    child = Child(id=1, parent_id=999)  # Parent 999 does not exist
                    session.add(child)
    finally:
        await db.close()


@pytest.mark.asyncio
async def test_sqlite_postgresql_compatibility_functions():
    """
    Verify that the local SQLite database emulates PostgreSQL specific structures:
    - set_config()
    - current_setting()
    - gen_random_uuid()
    """
    db = DatabaseSessionManager()
    db.init_db("sqlite+aiosqlite:///:memory:", echo=False)

    try:
        async with db.get_session_maker()() as session:
            # 1. Test set_config and current_setting
            await session.execute(
                text("SELECT set_config('test.compat_var', 'compat_value', 1);")
            )
            result = await session.execute(
                text("SELECT current_setting('test.compat_var');")
            )
            val = result.scalar()
            assert val == "compat_value"

            # 2. Test gen_random_uuid
            result = await session.execute(text("SELECT gen_random_uuid();"))
            uuid_str = result.scalar()
            assert isinstance(uuid_str, str)
            assert len(uuid_str) == 36
            assert uuid.UUID(uuid_str)  # Validates UUID structure
    finally:
        await db.close()


@pytest.mark.asyncio
async def test_concurrent_request_context_isolation():
    """
    Verify that transaction-level audit contexts are correctly isolated
    and never leak across concurrent user requests / asynchronous operations.
    """
    db_file = f"test_concurrency_{uuid.uuid4().hex}.db"
    db = DatabaseSessionManager()
    db.init_db(f"sqlite+aiosqlite:///{db_file}", echo=False)

    try:
        async with db.engine.begin() as conn:
            await conn.run_sync(TestBase.metadata.create_all)

        async def run_concurrent_task(task_id: int, user_name: str, reason: str):
            # Set the contextvars representing the unique user making the request
            user_token = current_user_id.set(user_name)
            reason_token = current_change_reason.set(reason)

            try:
                async with db.get_session_maker()() as session:
                    # Let's set a local config key using the connection/coroutine context
                    await session.execute(
                        text("SELECT set_config('test.task_id', :tid, 1);"),
                        {"tid": str(task_id)},
                    )
                    # Propagate transaction-level context variables to connection settings
                    await session.execute(
                        text("SELECT set_config('cadence.current_user_id', :uid, 1);"),
                        {"uid": user_name},
                    )
                    await session.execute(
                        text("SELECT set_config('cadence.current_change_reason', :reason, 1);"),
                        {"reason": reason},
                    )

                    # Simulate asynchronous concurrent processing with a random delay
                    await asyncio.sleep(0.05 * (task_id % 3 + 1))

                    # Query the settings inside SQLite custom functions
                    db_user = await session.execute(
                        text("SELECT current_setting('cadence.current_user_id');")
                    )
                    db_reason = await session.execute(
                        text("SELECT current_setting('cadence.current_change_reason');")
                    )
                    db_task_id = await session.execute(
                        text("SELECT current_setting('test.task_id');")
                    )

                    # Assert that values returned are isolated to this specific coroutine task
                    assert db_user.scalar() == user_name
                    assert db_reason.scalar() == reason
                    assert db_task_id.scalar() == str(task_id)
            finally:
                current_user_id.reset(user_token)
                current_change_reason.reset(reason_token)

        # Run 20 concurrent requests with different identities and reasons
        tasks = []
        for i in range(20):
            tasks.append(
                run_concurrent_task(
                    task_id=i,
                    user_name=f"user_concurrent_{i}",
                    reason=f"compliance_verification_reason_{i}",
                )
            )

        # Run them in parallel
        await asyncio.gather(*tasks)

    finally:
        await db.close()
        if os.path.exists(db_file):
            try:
                os.remove(db_file)
            except Exception:
                pass
