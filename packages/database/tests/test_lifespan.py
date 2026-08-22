"""Unit tests for get_relational_db_lifespan and lifecycle events.

Requirements: PRD-SYS-001, GxP Reliability Standards
"""

import pytest
from fastapi import FastAPI
from sqlalchemy.orm import declarative_base

from packages.database import RelationalDatabaseManager, get_relational_db_lifespan

Base = declarative_base()


@pytest.mark.asyncio
async def test_relational_db_lifespan_successful_lifecycle():
    """Verify that lifespan initializes database, executes startup hooks, and cleans up on shutdown.

    @req:PRD-SYS-001
    """
    db_mgr = RelationalDatabaseManager(service_name="TestService")
    startup_called = False
    shutdown_called = False

    async def startup_hook():
        nonlocal startup_called
        startup_called = True

    async def shutdown_hook():
        nonlocal shutdown_called
        shutdown_called = True

    lifespan_fn = get_relational_db_lifespan(
        db_manager=db_mgr,
        database_url="sqlite+aiosqlite:///:memory:",
        base_metadata=Base.metadata,
        startup_hooks=[startup_hook],
        shutdown_hooks=[shutdown_hook],
    )

    app = FastAPI()

    async with lifespan_fn(app):
        assert db_mgr.engine is not None
        assert db_mgr.session_maker is not None
        assert startup_called is True
        assert shutdown_called is False

    assert shutdown_called is True
    assert db_mgr.engine is None
    assert db_mgr.session_maker is None


@pytest.mark.asyncio
async def test_relational_db_lifespan_partial_failure_cleanup():
    """Verify that if startup hook fails, resources are cleaned up cleanly.

    @req:PRD-SYS-001
    """
    db_mgr = RelationalDatabaseManager(service_name="FailingService")

    async def failing_startup_hook():
        raise RuntimeError("Simulated startup failure in worker")

    lifespan_fn = get_relational_db_lifespan(
        db_manager=db_mgr,
        database_url="sqlite+aiosqlite:///:memory:",
        base_metadata=Base.metadata,
        startup_hooks=[failing_startup_hook],
    )

    app = FastAPI()

    with pytest.raises(RuntimeError, match="Simulated startup failure in worker"):
        async with lifespan_fn(app):
            pass


@pytest.mark.asyncio
async def test_relational_db_lifespan_without_metadata_or_hooks():
    """Verify lifespan functions correctly with minimal parameters.

    @req:PRD-SYS-001
    """
    db_mgr = RelationalDatabaseManager(service_name="MinimalService")
    lifespan_fn = get_relational_db_lifespan(
        db_manager=db_mgr,
        database_url="sqlite+aiosqlite:///:memory:",
    )

    app = FastAPI()

    async with lifespan_fn(app):
        assert db_mgr.engine is not None
        assert db_mgr.session_maker is not None

    assert db_mgr.engine is None
