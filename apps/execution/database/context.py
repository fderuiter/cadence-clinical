import contextvars

from sqlalchemy.ext.asyncio import AsyncSession

from packages.security.context import (
    audit_context,
    current_change_reason,
    current_ip_address,
    current_timestamp,
    current_user_id,
)

# Keep current_session here as it's bound to database session handling
current_session = contextvars.ContextVar("current_session", default=None)


def get_session() -> AsyncSession:
    """Gets the current active database session in this context.

    Returns:
        AsyncSession: The active SQLAlchemy session.

    Raises:
        RuntimeError: If no session is found in the current context.
    """
    session = current_session.get()
    if session is None:
        raise RuntimeError(
            "No database session found in current context. Are you using @transactional?"
        )
    return session


__all__ = [
    "current_session",
    "get_session",
    "current_user_id",
    "current_change_reason",
    "current_ip_address",
    "current_timestamp",
    "audit_context",
]
