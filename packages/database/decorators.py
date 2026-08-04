import functools
from collections.abc import Callable
from typing import Any

from sqlalchemy import text

from packages.security.context import current_change_reason, current_user_id

from .context import current_session


def transactional(session_factory: Any):
    """
    A decorator that automatically opens an async database session and manages
    the transaction boundaries. If a transaction is already active in the current
    ContextVar context, it reuses the session and creates a nested transaction/savepoint.
    If no session is active, it initiates a new session and begins a root transaction.
    """

    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        async def wrapper(*args, **kwargs) -> Any:
            session = current_session.get()
            if session is not None:
                # Active session exists (Nested Transaction)
                # Wrap the nested execution inside a savepoint boundary using SQLAlchemy's begin_nested()
                async with session.begin_nested():
                    return await func(*args, **kwargs)
            else:
                # No active session (Root Transaction)
                async with session_factory() as session:
                    async with session.begin():
                        token = current_session.set(session)
                        try:
                            # Propagate context variables into database session if Postgres/supported
                            try:
                                user_id = current_user_id.get()
                                reason = current_change_reason.get()
                            except Exception:
                                user_id = "system"
                                reason = "system_operation"

                            try:
                                await session.execute(
                                    text(
                                        "SELECT set_config('cadence.current_user_id', :user_id, true);"
                                    ),
                                    {"user_id": user_id},
                                )
                                await session.execute(
                                    text(
                                        "SELECT set_config('cadence.current_change_reason', :reason, true);"
                                    ),
                                    {"reason": reason},
                                )
                                await session.execute(
                                    text(
                                        "SELECT set_config('cadence.app_writing', 'true', true);"
                                    )
                                )
                            except Exception:
                                pass

                            return await func(*args, **kwargs)
                        finally:
                            current_session.reset(token)

        return wrapper

    return decorator
