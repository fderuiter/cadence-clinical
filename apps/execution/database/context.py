"""Database context module for Execution service using standardized ServiceDatabaseAdapter."""

from packages.database import ServiceDatabaseAdapter
from packages.security.context import (
    audit_context,
    current_change_reason,
    current_ip_address,
    current_timestamp,
    current_user_id,
)

_adapter = ServiceDatabaseAdapter("Execution")

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
    "current_user_id",
    "current_change_reason",
    "current_ip_address",
    "current_timestamp",
    "audit_context",
]
