import contextvars

from sqlalchemy.ext.asyncio import AsyncSession

current_session = contextvars.ContextVar("current_session", default=None)


def get_session() -> AsyncSession:
    """Gets the current active database session in this context."""
    session = current_session.get()
    if session is None:
        raise RuntimeError(
            "No database session found in current context. Are you using @transactional?"
        )
    return session
