"""
Centralized notifications client wrapper for etmf service.
"""

from packages.security import publish_expiration_notification

__all__ = [
    "publish_expiration_notification",
]
