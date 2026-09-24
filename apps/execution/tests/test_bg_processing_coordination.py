from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from apps.execution.database.core import bg_db_manager, db_manager
from apps.execution.database.sealer import execute_audit_sealing_cycle
from apps.execution.main import run_verification_task
from apps.execution.queries_escalation import execute_query_escalation_cycle
from apps.execution.workers.outbox_worker import poll_and_dispatch


@pytest.fixture(autouse=True)
def setup_test_db_state():
    """Fixture to ensure clean db state for background processing coordination tests."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    bg_db_manager.init_db("sqlite+aiosqlite:///:memory:")
    yield
    # Cleanup


@pytest.mark.asyncio
async def test_advisory_locking_pg_sealer():
    """Validate that execute_audit_sealing_cycle attempts to acquire advisory lock on PG dialect.

    @req:PRD-SYS-102
    """
    mock_db = AsyncMock()
    mock_db.bind.dialect.name = "postgresql"

    # Mock execute to return acquired lock status
    mock_execute_res = MagicMock()
    mock_execute_res.scalar.return_value = False
    mock_db.execute.return_value = mock_execute_res

    res = await execute_audit_sealing_cycle(mock_db)

    # Since lock was not acquired, it should return None
    assert res is None
    # Verify that pg_try_advisory_xact_lock was called with sealer key (42001)
    mock_db.execute.assert_called_once()
    assert "pg_try_advisory_xact_lock(42001)" in str(mock_db.execute.call_args[0][0])


@pytest.mark.asyncio
async def test_advisory_locking_pg_queries_escalation():
    """Validate that execute_query_escalation_cycle attempts to acquire advisory lock on PG dialect.

    @req:PRD-SYS-102
    """
    mock_db = AsyncMock()
    mock_db.bind.dialect.name = "postgresql"

    # Mock execute to return acquired lock status
    mock_execute_res = MagicMock()
    mock_execute_res.scalar.return_value = False
    mock_db.execute.return_value = mock_execute_res

    session_maker = MagicMock()
    session_maker.return_value.__aenter__.return_value = mock_db

    await execute_query_escalation_cycle(session_maker)

    # Verify that pg_try_advisory_xact_lock was called with queries key (42002)
    assert any(
        "pg_try_advisory_xact_lock(42002)" in str(call[0][0])
        for call in mock_db.execute.call_args_list
    )


@pytest.mark.asyncio
async def test_advisory_locking_pg_outbox_worker():
    """Validate that poll_and_dispatch attempts to acquire advisory lock on PG dialect.

    @req:PRD-SYS-102
    """
    mock_db = AsyncMock()
    mock_db.bind.dialect.name = "postgresql"

    # Mock execute to return acquired lock status
    mock_execute_res = MagicMock()
    mock_execute_res.scalar.return_value = False
    mock_db.execute.return_value = mock_execute_res

    # Use custom session maker
    session_maker = MagicMock()
    session_maker.return_value.__aenter__.return_value = mock_db

    # Temporarily set the _session_maker inside outbox_worker
    with patch("apps.execution.workers.outbox_worker._session_maker", session_maker):
        await poll_and_dispatch()

    # Verify that pg_try_advisory_xact_lock was called with outbox key (42003)
    assert any(
        "pg_try_advisory_xact_lock(42003)" in str(call[0][0])
        for call in mock_db.execute.call_args_list
    )


@pytest.mark.asyncio
async def test_integrity_verification_runs_in_background():
    """Verify that ledger integrity verification runs asynchronously in a background task.

    @req:PRD-SYS-103
    """
    from apps.execution.main import _last_verification_status

    # Reset state
    _last_verification_status["verified"] = True
    _last_verification_status["message"] = "Initial Status"

    with patch(
        "apps.execution.database.sealer.validate_ledger_integrity",
        AsyncMock(return_value=True),
    ) as mock_validate:
        # Run verification task
        await run_verification_task()

        assert mock_validate.called
        assert _last_verification_status["verified"] is True
        assert "fully verified" in _last_verification_status["message"]


@pytest.mark.asyncio
async def test_background_verification_failure_resilience():
    """Verify that background verification task handles errors gracefully and sets verified to False.

    @req:PRD-SYS-103
    """
    from apps.execution.main import _last_verification_status

    # Reset state
    _last_verification_status["verified"] = True

    with patch(
        "apps.execution.database.sealer.validate_ledger_integrity",
        AsyncMock(side_effect=ValueError("Tamper detected")),
    ):
        # Run verification task
        await run_verification_task()

        assert _last_verification_status["verified"] is False
        assert "Breach Detected" in _last_verification_status["message"]
