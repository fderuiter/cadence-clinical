"""
Unified Notification Event Dispatcher module for the Cadence Clinical Platform.

Provides a deep domain module encapsulating event serialization, composite
idempotency key generation, HMAC-SHA256 gateway signing, and multi-channel
dispatch to apps/notifications/.

Requirements: PRD-SYS-001, PRD-KNB-002, ADR-2188
"""

import logging
import os
from abc import ABC, abstractmethod
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field

from packages.security.context import current_change_reason, current_user_id
from packages.security.gateway_client import GatewayBaseClient

logger = logging.getLogger("packages.security.notifications")


class NotificationCategory(StrEnum):
    """Categories for platform notifications."""

    ALERTS = "ALERTS"
    SYSTEM = "SYSTEM"
    ACTION_ITEMS = "ACTION_ITEMS"


class NotificationPriority(StrEnum):
    """Priority levels for platform notifications."""

    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"
    CRITICAL = "CRITICAL"


class NotificationEvent(BaseModel):
    """
    Pydantic v2 schema representing a typed clinical or operational notification event.

    Encapsulates recipient targeting, classification, priority, message text, and
    composite idempotency metadata per GxP standards.
    """

    recipient_user_id: str | None = Field(
        default=None, description="Optional recipient Keycloak user ID"
    )
    recipient_role: str | None = Field(
        default=None, description="Optional target role (e.g., site_crc, sponsor_mm)"
    )
    category: NotificationCategory = Field(
        default=NotificationCategory.SYSTEM,
        description="Notification category: ALERTS, SYSTEM, or ACTION_ITEMS",
    )
    priority: NotificationPriority = Field(
        default=NotificationPriority.MEDIUM,
        description="Notification priority: LOW, MEDIUM, HIGH, or CRITICAL",
    )
    channels: str = Field(
        default="IN_APP",
        description="Comma-separated delivery channels (e.g. 'IN_APP', 'IN_APP,EMAIL')",
    )
    message_content: str = Field(
        ..., min_length=1, description="Human-readable notification message body"
    )
    related_entity_id: str | None = Field(
        default=None, description="Identifier of the entity related to this event"
    )
    related_entity_type: str | None = Field(
        default=None, description="Type of the related entity (e.g., article, ticket)"
    )

    @classmethod
    def compute_idempotency_key(
        cls,
        entity_type: str,
        entity_id: str,
        event_type: str,
        version_index: int | str = 1,
    ) -> str:
        """
        Derives a deterministic composite idempotency key for notification deduplication.

        Format: '{entity_type}:{entity_id}:{event_type}:{version_index}'
        """
        return f"{entity_type}:{entity_id}:{event_type}:{version_index}"

    def to_payload_dict(self) -> dict[str, Any]:
        """Converts event to dictionary format expected by apps/notifications REST API."""
        return {
            "recipient_user_id": self.recipient_user_id,
            "recipient_role": self.recipient_role,
            "category": self.category.value
            if isinstance(self.category, NotificationCategory)
            else str(self.category),
            "priority": self.priority.value
            if isinstance(self.priority, NotificationPriority)
            else str(self.priority),
            "channels": self.channels,
            "message_content": self.message_content,
            "related_entity_id": self.related_entity_id,
            "related_entity_type": self.related_entity_type,
        }


class NotificationDispatcherPort(ABC):
    """Port for publishing clinical and system notification events."""

    @abstractmethod
    async def publish(
        self,
        event: NotificationEvent | dict[str, Any],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> bool:
        """
        Publishes a single notification event.

        Args:
            event: NotificationEvent model or payload dictionary.
            actor_user_id: Optional actor user ID override.
            change_reason: Optional change justification string.
            service_name: Optional calling service identifier.

        Returns:
            True if notification was accepted, False otherwise.
        """
        pass

    @abstractmethod
    async def publish_batch(
        self,
        events: list[NotificationEvent | dict[str, Any]],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> list[bool]:
        """
        Publishes a batch of notification events.

        Args:
            events: List of NotificationEvent models or dictionaries.
            actor_user_id: Optional actor user ID override.
            change_reason: Optional change justification string.
            service_name: Optional calling service identifier.

        Returns:
            List of boolean results indicating success for each event.
        """
        pass


class GatewayNotificationDispatcher(NotificationDispatcherPort):
    """
    Production adapter dispatching events via authenticated HTTP gateway requests
    to the Notifications microservice (POST /api/v1/notifications).

    Non-blocking: catches and logs errors without raising exceptions to protect
    originating business transactions.
    """

    def __init__(
        self,
        base_url: str | None = None,
        timeout: float = 2.0,
        default_service_name: str = "gateway-client",
    ) -> None:
        self._base_url = (
            base_url or os.getenv("NOTIFICATIONS_URL", "http://localhost:8006")
        ).rstrip("/")
        self._timeout = timeout
        self._default_service_name = default_service_name

    async def publish(
        self,
        event: NotificationEvent | dict[str, Any],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> bool:
        """Publishes a single notification event via HTTP gateway call."""
        payload = (
            event.to_payload_dict()
            if isinstance(event, NotificationEvent)
            else dict(event)
        )

        try:
            client = GatewayBaseClient(base_url=self._base_url, timeout=self._timeout)

            # Resolve user_id: explicit param -> contextvar -> service_name fallback
            user_id = actor_user_id or current_user_id.get()
            if not user_id or user_id == "system":
                user_id = service_name or self._default_service_name

            # Resolve change_reason: explicit param -> contextvar -> service_name fallback
            reason = change_reason or current_change_reason.get()
            if not reason or reason == "system_operation":
                reason = (
                    f"{service_name} notification event dispatch"
                    if service_name
                    else "Clinical workflow notification event dispatch"
                )

            roles = "admin"

            response = await client.request(
                method="POST",
                path="/api/v1/notifications",
                user_id=user_id,
                roles=roles,
                change_reason=reason,
                json=payload,
            )

            if response.status_code != 201:
                logger.error(
                    "Failed to publish notification, status=%s body=%s",
                    response.status_code,
                    response.text,
                )
                return False
            return True
        except Exception as exc:
            logger.error(
                "Exception occurred during notification publication: %s",
                exc,
                exc_info=True,
            )
            return False

    async def publish_batch(
        self,
        events: list[NotificationEvent | dict[str, Any]],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> list[bool]:
        """Publishes a list of notification events sequentially."""
        results = []
        for evt in events:
            res = await self.publish(
                evt,
                actor_user_id=actor_user_id,
                change_reason=change_reason,
                service_name=service_name,
            )
            results.append(res)
        return results


class InMemoryNotificationDispatcher(NotificationDispatcherPort):
    """
    Test fake adapter recording dispatched events in memory for assertions.
    """

    def __init__(self, should_succeed: bool = True) -> None:
        self.dispatched_events: list[dict[str, Any]] = []
        self.should_succeed = should_succeed

    async def publish(
        self,
        event: NotificationEvent | dict[str, Any],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> bool:
        """Records event in memory."""
        payload = (
            event.to_payload_dict()
            if isinstance(event, NotificationEvent)
            else dict(event)
        )
        self.dispatched_events.append(
            {
                "payload": payload,
                "actor_user_id": actor_user_id,
                "change_reason": change_reason,
                "service_name": service_name,
            }
        )
        return self.should_succeed

    async def publish_batch(
        self,
        events: list[NotificationEvent | dict[str, Any]],
        *,
        actor_user_id: str | None = None,
        change_reason: str | None = None,
        service_name: str | None = None,
    ) -> list[bool]:
        """Records batch of events in memory."""
        results = []
        for evt in events:
            res = await self.publish(
                evt,
                actor_user_id=actor_user_id,
                change_reason=change_reason,
                service_name=service_name,
            )
            results.append(res)
        return results

    def clear(self) -> None:
        """Clears all recorded events."""
        self.dispatched_events.clear()


_default_dispatcher: GatewayNotificationDispatcher | None = None


def get_notification_dispatcher() -> GatewayNotificationDispatcher:
    """Returns the singleton GatewayNotificationDispatcher instance."""
    global _default_dispatcher
    if _default_dispatcher is None:
        _default_dispatcher = GatewayNotificationDispatcher()
    return _default_dispatcher


async def publish_notification(
    payload: NotificationEvent | dict[str, Any],
    *,
    base_url: str | None = None,
    actor_user_id: str | None = None,
    change_reason: str | None = None,
    service_name: str | None = None,
) -> bool:
    """
    Convenience function to publish a notification event using the gateway client.

    Maintains backwards compatibility with existing microservice helper signatures.
    """
    if base_url:
        dispatcher = GatewayNotificationDispatcher(base_url=base_url)
    else:
        dispatcher = get_notification_dispatcher()

    return await dispatcher.publish(
        payload,
        actor_user_id=actor_user_id,
        change_reason=change_reason,
        service_name=service_name,
    )


__all__ = [
    "GatewayNotificationDispatcher",
    "InMemoryNotificationDispatcher",
    "NotificationCategory",
    "NotificationDispatcherPort",
    "NotificationEvent",
    "NotificationPriority",
    "get_notification_dispatcher",
    "publish_notification",
]
