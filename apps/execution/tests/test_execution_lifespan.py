"""Unit tests for Execution microservice lifespan and background worker lifecycle.

Requirements: PRD-SYS-001, GxP Reliability Standards
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from fastapi import FastAPI

import apps.execution.main as execution_main
from apps.execution.main import lifespan


@pytest.mark.asyncio
async def test_execution_lifespan_test_mode_bypass():
    """Verify that under test mode (PYTEST_CURRENT_TEST set), lifespan cleanly yields and exits without dangling workers.

    @req:PRD-SYS-001
    """
    app = FastAPI()
    async with lifespan(app):
        pass


@pytest.mark.asyncio
async def test_execution_lifespan_production_mode_lifecycle():
    """Verify that in production mode, lifespan initializes recovery, starts workers, and performs graceful shutdown.

    @req:PRD-SYS-001
    """
    app = FastAPI()

    with (
        patch.dict("os.environ", {"RUN_BACKGROUND_WORKERS": "true"}, clear=False),
        patch.object(
            execution_main,
            "recover_orphaned_dictionary_imports",
            new_callable=AsyncMock,
        ) as mock_recovery,
        patch(
            "apps.execution.database.sealer.start_background_sealer",
            new_callable=AsyncMock,
        ) as mock_start_sealer,
        patch(
            "apps.execution.database.sealer.stop_background_sealer",
            new_callable=AsyncMock,
        ) as mock_stop_sealer,
        patch(
            "apps.execution.queries_escalation.start_background_query_escalation",
            new_callable=AsyncMock,
        ) as mock_start_escalation,
        patch(
            "apps.execution.queries_escalation.stop_background_query_escalation",
            new_callable=AsyncMock,
        ) as mock_stop_escalation,
        patch(
            "apps.execution.workers.outbox_worker.start_outbox_worker"
        ) as mock_start_outbox,
        patch(
            "apps.execution.workers.outbox_worker.stop_outbox_worker"
        ) as mock_stop_outbox,
        patch(
            "apps.execution.workers.consent_subscriber.start_consent_subscriber"
        ) as mock_start_consent,
        patch(
            "apps.execution.workers.consent_subscriber.stop_consent_subscriber"
        ) as mock_stop_consent,
        patch(
            "apps.execution.workers.anomaly_worker.start_anomaly_worker"
        ) as mock_start_anomaly,
        patch(
            "apps.execution.workers.anomaly_worker.stop_anomaly_worker"
        ) as mock_stop_anomaly,
        patch.object(execution_main.db_manager, "init_db") as mock_db_init,
        patch.object(
            execution_main.db_manager, "get_session_maker", return_value=MagicMock()
        ),
        patch.object(
            execution_main.db_manager, "close", new_callable=AsyncMock
        ) as mock_db_close,
        patch.object(execution_main.bg_db_manager, "init_db") as mock_bg_db_init,
        patch.object(
            execution_main.bg_db_manager, "get_session_maker", return_value=MagicMock()
        ),
        patch.object(
            execution_main.bg_db_manager, "close", new_callable=AsyncMock
        ) as mock_bg_db_close,
    ):
        with patch.dict("os.environ", {}, clear=True):
            async with lifespan(app):
                mock_db_init.assert_called_once()
                mock_recovery.assert_awaited_once()
                mock_bg_db_init.assert_called_once()
                mock_start_sealer.assert_awaited_once()
                mock_start_escalation.assert_awaited_once()
                mock_start_outbox.assert_called_once()
                mock_start_consent.assert_called_once()
                mock_start_anomaly.assert_called_once()

            mock_stop_sealer.assert_awaited_once()
            mock_stop_escalation.assert_awaited_once()
            mock_stop_outbox.assert_called_once()
            mock_stop_consent.assert_called_once()
            mock_stop_anomaly.assert_called_once()
            mock_bg_db_close.assert_awaited_once()
            mock_db_close.assert_awaited_once()
