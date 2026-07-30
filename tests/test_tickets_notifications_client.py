from unittest.mock import AsyncMock, patch

import httpx
import pytest

from apps.tickets.notifications_client import publish_notification
from packages.security.context import audit_context


@pytest.mark.asyncio
async def test_publish_notification_success():
    """
    Assert that publish_notification returns True on a successful 201 response,
    and that the outgoing request headers and JSON body are correct.
    """
    payload = {"message_content": "Test notification message"}

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 201
        mock_post.return_value = mock_response

        with audit_context(
            user_id="test_actor", change_reason="Test notification reason"
        ):
            result = await publish_notification(payload)

        assert result is True
        mock_post.assert_called_once()
        args, kwargs = mock_post.call_args

        # Verify URL
        assert args[0].endswith("/api/v1/notifications")
        # Verify JSON
        assert kwargs["json"] == payload
        # Verify headers
        headers = kwargs["headers"]
        assert headers["X-User-Id"] == "test_actor"
        assert headers["X-Signature-Version"] == "2"
        assert headers["X-Change-Reason"] == "Test notification reason"
        assert "X-Gateway-Signature" in headers
        assert "X-Gateway-Timestamp" in headers


@pytest.mark.asyncio
async def test_publish_notification_non_2xx_failure():
    """
    Assert that publish_notification returns False on non-201 response.
    """
    payload = {"message_content": "Test failure message"}

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 500
        mock_response.text = "Internal Server Error"
        mock_post.return_value = mock_response

        result = await publish_notification(payload)
        assert result is False


@pytest.mark.asyncio
async def test_publish_notification_transport_exception():
    """
    Assert that publish_notification returns False when an exception is raised (e.g., ConnectTimeout).
    """
    payload = {"message_content": "Test timeout message"}

    with patch(
        "httpx.AsyncClient.post",
        side_effect=httpx.ConnectTimeout("Connection timed out"),
    ):
        result = await publish_notification(payload)
        assert result is False
