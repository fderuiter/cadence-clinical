"""
Database manager configuration for the Tickets service.
"""

from apps.tickets.infrastructure.database import db_manager, get_db_session

__all__ = ["db_manager", "get_db_session"]
