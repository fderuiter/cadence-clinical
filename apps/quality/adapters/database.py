"""Database adapter module for Quality service."""

from packages.database import ServiceDatabaseAdapter

_adapter = ServiceDatabaseAdapter("Quality")

db_manager = _adapter.db_manager
current_session = _adapter.current_session
get_session = _adapter.get_session
transactional = _adapter.transactional
get_db_session = _adapter.get_db_session

__all__ = [
    "db_manager",
    "current_session",
    "get_session",
    "transactional",
    "get_db_session",
]
