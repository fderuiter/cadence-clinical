"""
Unit tests for the centralized Notification Dispatcher module.

Requirements: PRD-SYS-001, PRD-KNB-002, ADR-2188
"""

from unittest.mock import AsyncMock, patch

import httpx
import pytest

from packages.security.context import audit_context
from packages.security.notifications import (
    GatewayNotificationDispatcher,
    InMemoryNotificationDispatcher,
    NotificationCategory,
    NotificationEvent,
    NotificationPriority,
    publish_notification,
)


def test_notification_event_creation_and_defaults():
    """
    Validate NotificationEvent default properties and serialization.

    @req:PRD-SYS-001
    """
    event = NotificationEvent(
        message_content="Test system alert",
        recipient_role="site_crc",
    )

    assert event.message_content == "Test system alert"
    assert event.recipient_role == "site_crc"
    assert event.recipient_user_id is None
    assert event.category == NotificationCategory.SYSTEM
    assert event.priority == NotificationPriority.MEDIUM
    assert event.channels == "IN_APP"

    payload = event.to_payload_dict()
    assert payload["category"] == "SYSTEM"
    assert payload["priority"] == "MEDIUM"
    assert payload["message_content"] == "Test system alert"
    assert payload["recipient_role"] == "site_crc"


def test_notification_event_idempotency_key_derivation():
    """
    Validate deterministic composite idempotency key derivation.

    @req:PRD-SYS-001
    """
    key = NotificationEvent.compute_idempotency_key(
        entity_type="article",
        entity_id="art-101",
        event_type="published",
        version_index=2,
    )
    assert key == "article:art-101:published:2"


@pytest.mark.asyncio
async def test_gateway_notification_dispatcher_success():
    """
    Validate GatewayNotificationDispatcher successful publication with HMAC gateway headers.

    @req:PRD-SYS-001
    """
    event = NotificationEvent(
        recipient_user_id="user_crc_01",
        category=NotificationCategory.ACTION_ITEMS,
        priority=NotificationPriority.HIGH,
        message_content="Action required on visit 3",
        related_entity_id="visit-003",
        related_entity_type="encounter",
    )

    dispatcher = GatewayNotificationDispatcher(
        base_url="http://mock-notifications:8006"
    )

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 201
        mock_post.return_value = mock_response

        with audit_context(
            user_id="test_coordinator", change_reason="Visit action required"
        ):
            result = await dispatcher.publish(event)

        assert result is True
        mock_post.assert_called_once()
        args, kwargs = mock_post.call_args

        assert args[0] == "http://mock-notifications:8006/api/v1/notifications"
        assert kwargs["json"]["recipient_user_id"] == "user_crc_01"
        assert kwargs["json"]["priority"] == "HIGH"
        assert kwargs["json"]["category"] == "ACTION_ITEMS"

        headers = kwargs["headers"]
        assert headers["X-User-Id"] == "test_coordinator"
        assert headers["X-Signature-Version"] == "2"
        assert headers["X-Change-Reason"] == "Visit action required"
        assert "X-Gateway-Signature" in headers
        assert "X-Gateway-Timestamp" in headers


@pytest.mark.asyncio
async def test_gateway_notification_dispatcher_non_2xx_failure():
    """
    Validate GatewayNotificationDispatcher gracefully handles non-2xx responses without raising.

    @req:PRD-SYS-001
    """
    dispatcher = GatewayNotificationDispatcher()

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 500
        mock_response.text = "Internal Server Error"
        mock_post.return_value = mock_response

        result = await dispatcher.publish({"message_content": "Failing message"})
        assert result is False


@pytest.mark.asyncio
async def test_gateway_notification_dispatcher_network_exception():
    """
    Validate GatewayNotificationDispatcher swallows network exceptions and returns False.

    @req:PRD-SYS-001
    """
    dispatcher = GatewayNotificationDispatcher()

    with patch(
        "httpx.AsyncClient.post",
        side_effect=httpx.ConnectTimeout("Connection timed out"),
    ):
        result = await dispatcher.publish({"message_content": "Timeout message"})
        assert result is False


@pytest.mark.asyncio
async def test_gateway_notification_dispatcher_batch():
    """
    Validate GatewayNotificationDispatcher batch publishing.

    @req:PRD-SYS-001
    """
    dispatcher = GatewayNotificationDispatcher()

    events = [
        NotificationEvent(message_content="Event 1"),
        NotificationEvent(message_content="Event 2"),
    ]

    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 201
        mock_post.return_value = mock_response

        results = await dispatcher.publish_batch(events)
        assert results == [True, True]
        assert mock_post.call_count == 2


@pytest.mark.asyncio
async def test_in_memory_notification_dispatcher():
    """
    Validate InMemoryNotificationDispatcher fake operations for unit test isolation.

    @req:PRD-SYS-001
    """
    fake = InMemoryNotificationDispatcher()

    event = NotificationEvent(
        recipient_role="cra_monitor",
        message_content="Query discrepancy raised",
        related_entity_id="qry-99",
        related_entity_type="query",
    )

    success = await fake.publish(
        event,
        actor_user_id="cra.user",
        change_reason="Query review",
        service_name="execution-service",
    )

    assert success is True
    assert len(fake.dispatched_events) == 1
    recorded = fake.dispatched_events[0]
    assert recorded["payload"]["recipient_role"] == "cra_monitor"
    assert recorded["payload"]["message_content"] == "Query discrepancy raised"
    assert recorded["actor_user_id"] == "cra.user"
    assert recorded["change_reason"] == "Query review"
    assert recorded["service_name"] == "execution-service"

    fake.clear()
    assert len(fake.dispatched_events) == 0


@pytest.mark.asyncio
async def test_publish_notification_convenience_function():
    """
    Validate top-level publish_notification convenience function.

    @req:PRD-SYS-001
    """
    with patch("httpx.AsyncClient.post", new_callable=AsyncMock) as mock_post:
        mock_response = AsyncMock()
        mock_response.status_code = 201
        mock_post.return_value = mock_response

        result = await publish_notification(
            {"message_content": "Quick notification"},
            actor_user_id="admin.user",
            change_reason="Admin alert",
        )
        assert result is True
        mock_post.assert_called_once()
