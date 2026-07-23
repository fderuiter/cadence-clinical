from unittest.mock import AsyncMock, patch

import pytest

from apps.execution.database.migrate import main, run_migrations


@pytest.mark.asyncio
async def test_run_migrations_success():
    from unittest.mock import MagicMock

    with patch(
        "apps.execution.database.migrate.create_async_engine"
    ) as mock_create_engine:
        mock_engine = AsyncMock()
        mock_create_engine.return_value = mock_engine

        # Mock engine.begin() context manager
        class MockBegin:
            async def __aenter__(self):
                self.conn = AsyncMock()
                return self.conn

            async def __aexit__(self, exc_type, exc, tb):
                pass

        mock_engine.begin = MagicMock(return_value=MockBegin())

        await run_migrations("sqlite+aiosqlite:///:memory:")

        mock_create_engine.assert_called_once()
        mock_engine.begin.assert_called_once()
        mock_engine.dispose.assert_called_once()


@pytest.mark.asyncio
async def test_run_migrations_failure():
    from unittest.mock import MagicMock

    with patch(
        "apps.execution.database.migrate.create_async_engine"
    ) as mock_create_engine:
        mock_engine = AsyncMock()
        mock_create_engine.return_value = mock_engine

        class MockBeginFail:
            async def __aenter__(self):
                raise Exception("DB Error")

            async def __aexit__(self, exc_type, exc, tb):
                pass

        mock_engine.begin = MagicMock(return_value=MockBeginFail())

        with patch("sys.exit") as mock_exit:
            await run_migrations("sqlite+aiosqlite:///:memory:")
            mock_exit.assert_called_once_with(1)


def test_main_cli():
    # To prevent the "coroutine never awaited" runtime warning, we consume the coroutine in the mock.
    def mock_run_impl(coro):
        try:
            coro.close()
        except Exception:
            pass

    with patch(
        "apps.execution.database.migrate.asyncio.run", side_effect=mock_run_impl
    ) as mock_run:
        with patch(
            "sys.argv", ["migrate.py", "--db-url", "sqlite+aiosqlite:///:memory:"]
        ):
            main()
            mock_run.assert_called_once()


@pytest.mark.asyncio
async def test_run_migrations_real_sqlite():
    """
    Test executing run_migrations against a real SQLite database
    to ensure full coverage of SQLite trigger generation branches.
    """
    await run_migrations("sqlite+aiosqlite:///:memory:")
