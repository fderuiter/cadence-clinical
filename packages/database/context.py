import contextvars

from sqlalchemy.ext.asyncio import AsyncSession

current_session: contextvars.ContextVar[AsyncSession | None] = contextvars.ContextVar(
    "current_session", default=None
)


def get_session() -> AsyncSession:
    """Gets the current active database session in this context.

    Returns:
        AsyncSession: The active SQLAlchemy session.

    Raises:
        RuntimeError: If no session is found in the current context.
    """
    session = current_session.get()
    if session is None:
        raise RuntimeError("No active database session found in current context.")
    return session
