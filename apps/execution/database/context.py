import contextvars

from sqlalchemy.ext.asyncio import AsyncSession

# Context variables for the current execution context
current_session = contextvars.ContextVar("current_session", default=None)
current_user_id = contextvars.ContextVar("current_user_id", default="system")
current_change_reason = contextvars.ContextVar(
    "current_change_reason", default="system_operation"
)


def get_session() -> AsyncSession:
    session = current_session.get()
    if session is None:
        raise RuntimeError(
            "No database session found in current context. Are you using @transactional?"
        )
    return session
