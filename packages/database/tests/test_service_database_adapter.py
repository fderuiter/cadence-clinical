"""Unit tests for ServiceDatabaseAdapter and create_service_db_adapter factory."""

import asyncio
import importlib
from unittest.mock import AsyncMock, MagicMock

import pytest
from sqlalchemy.ext.asyncio import AsyncSession

from packages.database import (
    DatabaseSessionDependency,
    RelationalDatabaseManager,
    ServiceDatabaseAdapter,
    create_service_db_adapter,
)


def test_service_database_adapter_initialization():
    """Verify ServiceDatabaseAdapter initializes manager, contextvar, transactional, and dependency."""
    adapter = ServiceDatabaseAdapter("Quality")

    assert isinstance(adapter.db_manager, RelationalDatabaseManager)
    assert adapter.db_manager.service_name == "Quality"
    assert adapter.current_session is not None
    assert isinstance(adapter.get_db_session, DatabaseSessionDependency)


def test_service_database_adapter_get_session_raises_when_unset():
    """Verify get_session raises RuntimeError when no session is active in context."""
    adapter = ServiceDatabaseAdapter("eISF")

    with pytest.raises(
        RuntimeError,
        match="No database session found in current context. Are you using @transactional?",
    ):
        adapter.get_session()


def test_service_database_adapter_get_session_returns_active_session():
    """Verify get_session returns active AsyncSession when set in context."""
    adapter = ServiceDatabaseAdapter("eTMF")
    mock_session = AsyncMock(spec=AsyncSession)

    token = adapter.current_session.set(mock_session)
    try:
        assert adapter.get_session() is mock_session
    finally:
        adapter.current_session.reset(token)


@pytest.mark.asyncio
async def test_service_database_adapter_transactional_decorator():
    """Verify transactional decorator sets session context during execution."""
    adapter = ServiceDatabaseAdapter("Tickets")

    # Mock the session maker behavior on db_manager
    mock_session = AsyncMock(spec=AsyncSession)
    mock_session_maker = MagicMock()

    class MockAsyncContext:
        async def __aenter__(self):
            return mock_session

        async def __aexit__(self, exc_type, exc_val, exc_tb):
            pass

    mock_session_maker.return_value = MockAsyncContext()
    adapter.db_manager.get_session_maker = MagicMock(return_value=mock_session_maker)

    captured_session = None

    @adapter.transactional
    async def sample_handler():
        nonlocal captured_session
        captured_session = adapter.get_session()
        return "success"

    result = await sample_handler()
    assert result == "success"
    assert captured_session is mock_session
    # Context should be cleared after transaction finishes
    with pytest.raises(RuntimeError):
        adapter.get_session()


def test_factory_function_returns_configured_adapter():
    """Verify create_service_db_adapter factory function constructs valid adapter."""
    adapter = create_service_db_adapter("Payments")
    assert isinstance(adapter, ServiceDatabaseAdapter)
    assert adapter.service_name == "Payments"


def test_session_contextvar_isolation():
    """Verify distinct adapters maintain isolated session ContextVars."""
    adapter_a = ServiceDatabaseAdapter("ServiceA")
    adapter_b = ServiceDatabaseAdapter("ServiceB")

    mock_session_a = AsyncMock(spec=AsyncSession)
    mock_session_b = AsyncMock(spec=AsyncSession)

    token_a = adapter_a.current_session.set(mock_session_a)
    token_b = adapter_b.current_session.set(mock_session_b)

    try:
        assert adapter_a.get_session() is mock_session_a
        assert adapter_b.get_session() is mock_session_b
        assert adapter_a.get_session() is not adapter_b.get_session()
    finally:
        adapter_a.current_session.reset(token_a)
        adapter_b.current_session.reset(token_b)


@pytest.mark.parametrize(
    "module_path,service_name",
    [
        ("apps.quality.adapters.database", "Quality"),
        ("apps.eisf.adapters.database", "eISF"),
        ("apps.etmf.adapters.database", "eTMF"),
        ("apps.tickets.adapters.database", "Tickets"),
        ("apps.payments.adapters.database", "Payments"),
        ("apps.org.adapters.database", "Org"),
        ("apps.notifications.adapters.database", "Notifications"),
        ("apps.interop.adapters.database", "Interop"),
        ("apps.safety.adapters.database", "Safety"),
        ("apps.ctms.adapters.database", "CTMS"),
        ("apps.econsent.adapters.database", "eConsent"),
        ("apps.imaging.adapters.database", "Imaging"),
        ("apps.knowledge.adapters.database", "Knowledge"),
        ("apps.fileshare.adapters.database", "FileShare"),
        ("apps.execution.database.context", "Execution"),
        ("apps.execution.adapters.database", "Execution"),
    ],
)
def test_microservice_adapter_backwards_compatibility(module_path, service_name):
    """Verify each microservice database adapter exports required interface variables."""
    mod = importlib.import_module(module_path)

    assert hasattr(mod, "db_manager")
    assert isinstance(mod.db_manager, RelationalDatabaseManager)
    assert mod.db_manager.service_name == service_name

    assert hasattr(mod, "current_session")
    assert hasattr(mod, "get_session")
    assert hasattr(mod, "transactional")
    assert hasattr(mod, "get_db_session")
    assert isinstance(mod.get_db_session, DatabaseSessionDependency)


def test_resolve_current_session_with_service_adapters():
    """Verify security audit logger resolve_current_session correctly finds session from service adapters."""
    from packages.security.audit_logger import resolve_current_session

    quality_db = importlib.import_module("apps.quality.adapters.database")
    mock_session = AsyncMock(spec=AsyncSession)

    token = quality_db.current_session.set(mock_session)
    try:
        resolved = resolve_current_session()
        assert resolved is mock_session
    finally:
        quality_db.current_session.reset(token)
