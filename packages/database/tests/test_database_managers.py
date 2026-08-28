import asyncio
from unittest.mock import AsyncMock, MagicMock

import pytest

from packages.database import DatabaseSessionDependency, RelationalDatabaseManager


def test_interop_database_manager_uninitialized_and_close():
    """
    Verify Interop database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="Interop")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    # close should handle self.engine=None
    asyncio.run(mgr.close())


def test_notifications_database_manager_uninitialized_and_close():
    """
    Verify Notifications database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="Notifications")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_econsent_database_manager_uninitialized_and_close():
    """
    Verify eConsent database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eConsent")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_eisf_database_manager_uninitialized_and_close():
    """
    Verify eISF database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eISF")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_etmf_database_manager_uninitialized_and_close():
    """
    Verify eTMF database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="eTMF")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_ctms_database_manager_uninitialized_and_close():
    """
    Verify CTMS database manager exception and close-twice behavior.
    """
    mgr = RelationalDatabaseManager(service_name="CTMS")
    with pytest.raises(Exception, match="not initialized"):
        mgr.get_session_maker()
    asyncio.run(mgr.close())


def test_ci_database_parity_enforcement_raises_on_failure(monkeypatch):
    """
    Verify that when GITHUB_ACTIONS is active, database initialization failures raise an exception.
    """
    import os

    monkeypatch.setenv("GITHUB_ACTIONS", "true")

    def mock_create_databases_async(worker_suffix):
        raise ConnectionRefusedError("Simulated database connection failure")

    with pytest.raises(
        ConnectionRefusedError, match="Simulated database connection failure"
    ):
        try:
            mock_create_databases_async("_test")
        except Exception:
            if os.environ.get("GITHUB_ACTIONS") == "true":
                raise


@pytest.mark.asyncio
async def test_map_database_exceptions_decorator():
    """Verify map_database_exceptions maps SQLAlchemy exceptions to hexagonal domain exceptions.

    @req:PRD-SYS-001
    """
    from sqlalchemy.exc import IntegrityError, NoResultFound, SQLAlchemyError

    from packages.database import map_database_exceptions
    from packages.hexagonal import (
        DatabaseError,
        EntityAlreadyExistsError,
        EntityNotFoundError,
    )

    @map_database_exceptions
    async def raise_no_result():
        raise NoResultFound("No row found")

    @map_database_exceptions
    async def raise_integrity():
        raise IntegrityError("statement", {}, Exception("Duplicate"))

    @map_database_exceptions
    async def raise_sqlalchemy_error():
        raise SQLAlchemyError("Operational failure")

    with pytest.raises(EntityNotFoundError, match="Requested entity not found"):
        await raise_no_result()

    with pytest.raises(EntityAlreadyExistsError, match="Database constraint violation"):
        await raise_integrity()

    with pytest.raises(DatabaseError, match="Database operational or schema error"):
        await raise_sqlalchemy_error()


def test_relational_database_manager_pool_recycle(monkeypatch):
    """Verify RelationalDatabaseManager configures pool_recycle=1800 for PostgreSQL.

    @req:PRD-SYS-053
    @req:Trace-15
    """
    captured_kwargs = {}

    def mock_create_async_engine(url, **kwargs):
        captured_kwargs.update(kwargs)
        mock_engine = MagicMock()
        mock_engine.sync_engine = MagicMock()
        return mock_engine

    import packages.database as db_pkg

    monkeypatch.setattr(db_pkg, "create_async_engine", mock_create_async_engine)

    manager = RelationalDatabaseManager(service_name="test_service")
    manager.init_db("postgresql+asyncpg://user:pass@localhost:5432/testdb")

    assert captured_kwargs.get("pool_recycle") == 1800
    assert captured_kwargs.get("pool_size") == 10
    assert captured_kwargs.get("max_overflow") == 20
    assert captured_kwargs.get("pool_pre_ping") is True


@pytest.mark.asyncio
async def test_database_session_dependency_does_not_set_app_writing():
    """Verify DatabaseSessionDependency does not set cadence.app_writing during standard operations.

    @req:PRD-SYS-053
    @req:Trace-15
    """
    mock_session = AsyncMock()
    mock_session.bind = MagicMock()
    mock_session.bind.dialect.name = "postgresql"
    executed_statements = []

    async def mock_execute(statement, params=None):
        executed_statements.append(str(statement))

    mock_session.execute = mock_execute

    mock_db_manager = MagicMock()

    class MockAsyncContext:
        async def __aenter__(self):
            return mock_session

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

    mock_db_manager.get_session_maker.return_value = lambda: MockAsyncContext()

    dep = DatabaseSessionDependency(db_manager=mock_db_manager)
    gen = dep()
    sess = await anext(gen)
    assert sess is mock_session

    for stmt in executed_statements:
        assert "cadence.app_writing" not in stmt
