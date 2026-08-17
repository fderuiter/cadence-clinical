"""Integration test suite verifying connection state eviction and pool hooks.

@req:PRD-SYS-001
"""

import contextlib
import os

import pytest
from sqlalchemy import text

from apps.execution.database.core import db_manager


@pytest.mark.asyncio
async def test_pool_connection_state_eviction() -> None:
    """Validate SQLite session context keys are evicted and reset properly on connect, checkout, and close.

    @req:PRD-SYS-001
    """
    db_file = "test_eviction.db"
    if os.path.exists(db_file):
        with contextlib.suppress(Exception):
            os.remove(db_file)

    # 1. Initialize test database
    await db_manager.close()
    db_manager.init_db(f"sqlite+aiosqlite:///{db_file}")

    # Verify manager settings dict is empty initially
    assert len(db_manager._sqlite_settings) == 0

    # 2. Open a connection and set custom config
    async with db_manager.engine.connect() as conn:
        # Check defaults are configured
        res_user = await conn.execute(
            text("SELECT current_setting('cadence.current_user_id', true)")
        )
        assert res_user.scalar() == "system"

        # Update the context config
        await conn.execute(
            text("SELECT set_config('cadence.current_user_id', 'test_user_abc', true)")
        )

        # Verify it has changed
        res_user_updated = await conn.execute(
            text("SELECT current_setting('cadence.current_user_id', true)")
        )
        assert res_user_updated.scalar() == "test_user_abc"

        # Verify the manager tracked this connection and has updated settings
        assert len(db_manager._sqlite_settings) == 1
        conn_id = list(db_manager._sqlite_settings.keys())[0]
        assert (
            db_manager._sqlite_settings[conn_id]["cadence.current_user_id"]
            == "test_user_abc"
        )

    # At this point, the connection was checked in (returned to pool).
    # Checkin event should reset it to default values.
    assert conn_id in db_manager._sqlite_settings
    assert db_manager._sqlite_settings[conn_id]["cadence.current_user_id"] == "system"

    # 3. Simulate another checkout of the same connection and check that it's reset upon checkout
    async with db_manager.engine.connect() as conn2:
        res_user_new = await conn2.execute(
            text("SELECT current_setting('cadence.current_user_id', true)")
        )
        # Must be system default
        assert res_user_new.scalar() == "system"

    # 4. Now close/dispose the engine, which closes all connections
    await db_manager.close()

    # The settings for all closed connections must be completely evicted from our tracking dict
    assert len(db_manager._sqlite_settings) == 0

    if os.path.exists(db_file):
        with contextlib.suppress(Exception):
            os.remove(db_file)


@pytest.mark.asyncio
async def test_concurrent_connection_isolation_and_no_weakref_errors() -> None:
    """Verify concurrent connection settings maintain separate isolation and avoid weakref TypeErrors.

    @req:PRD-SYS-001
    """
    db_file = "test_isolation.db"
    if os.path.exists(db_file):
        with contextlib.suppress(Exception):
            os.remove(db_file)

    await db_manager.close()
    db_manager.init_db(f"sqlite+aiosqlite:///{db_file}")

    # We will open two connections concurrently
    conn1 = await db_manager.engine.connect()
    conn2 = await db_manager.engine.connect()

    try:
        # Set distinct contexts
        await conn1.execute(
            text("SELECT set_config('cadence.current_user_id', 'user_1', true)")
        )
        await conn2.execute(
            text("SELECT set_config('cadence.current_user_id', 'user_2', true)")
        )

        # Verify they are isolated
        res1 = await conn1.execute(
            text("SELECT current_setting('cadence.current_user_id', true)")
        )
        res2 = await conn2.execute(
            text("SELECT current_setting('cadence.current_user_id', true)")
        )

        assert res1.scalar() == "user_1"
        assert res2.scalar() == "user_2"

        # Verify no weakref issues on connection tracking keys (which are standard integers, not objects)
        for key in db_manager._sqlite_settings:
            assert isinstance(key, int)

    finally:
        await conn1.close()
        await conn2.close()
        await db_manager.close()

        if os.path.exists(db_file):
            with contextlib.suppress(Exception):
                os.remove(db_file)
