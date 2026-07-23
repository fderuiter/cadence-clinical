import contextvars
from contextlib import contextmanager
from typing import Generator

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


@contextmanager
def audit_context(
    user_id: str | None = None, change_reason: str | None = None
) -> Generator[None, None, None]:
    """Context manager to bind user identity and change reason context variables.

    Ensures that background tasks maintain the initiating user's context for audit
    logging, and guarantees cleanup of context variables after task completion or
    error, preventing identity leakage.

    Args:
        user_id (str | None): The unique identifier of the initiating user.
            Defaults to None, which triggers fallback to the system default "system".
        change_reason (str | None): The justification or reason for change.
            Defaults to None, which triggers fallback to "system_operation".

    Yields:
        None
    """
    u = user_id if user_id is not None else "system"
    r = change_reason if change_reason is not None else "system_operation"

    user_token = current_user_id.set(u)
    reason_token = current_change_reason.set(r)
    try:
        yield
    finally:
        current_user_id.reset(user_token)
        current_change_reason.reset(reason_token)
