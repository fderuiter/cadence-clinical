"""Unit tests for Designer microservice lifespan and Neo4j driver lifecycle.

Requirements: PRD-SYS-001, GxP Reliability Standards
"""

from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI

from apps.designer.main import lifespan


@pytest.mark.asyncio
async def test_designer_lifespan_successful_lifecycle():
    """Verify that Designer lifespan initializes Neo4j driver and closes it on shutdown.

    @req:PRD-SYS-001
    """
    app = FastAPI()
    mock_driver = AsyncMock()
    mock_driver.close = AsyncMock()

    with patch(
        "apps.designer.main.AsyncGraphDatabase.driver", return_value=mock_driver
    ):
        async with lifespan(app):
            assert app.state.driver == mock_driver

        mock_driver.close.assert_awaited_once()
        assert app.state.driver is None


@pytest.mark.asyncio
async def test_designer_lifespan_driver_failure_fallback_to_mock():
    """Verify that if Neo4j driver initialization fails, application operates gracefully in mock mode.

    @req:PRD-SYS-001
    """
    app = FastAPI()

    with patch(
        "apps.designer.main.AsyncGraphDatabase.driver",
        side_effect=Exception("Connection refused to bolt://localhost:7687"),
    ):
        async with lifespan(app):
            assert app.state.driver is None

        assert app.state.driver is None
