"""
Notification client for Tickets service.
"""

from apps.tickets.adapters.notifications_client import publish_notification

__all__ = ["publish_notification"]
