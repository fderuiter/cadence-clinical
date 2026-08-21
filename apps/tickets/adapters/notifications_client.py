"""
HTTP client adapter for dispatching notification events to Notifications service.
"""

from apps.tickets.infrastructure.notifications_client import publish_notification

__all__ = ["publish_notification"]
