"""
Notification payload generator for Tickets service.
"""

from apps.tickets.application.notification_events import (
    generate_ticket_notification_payloads,
)

__all__ = ["generate_ticket_notification_payloads"]
