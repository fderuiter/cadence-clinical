import contextvars
import functools
from collections.abc import Callable
from typing import Any

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession

from packages.database import RelationalDatabaseManager

db_manager = RelationalDatabaseManager(service_name="Quality")

current_session = contextvars.ContextVar("current_session", default=None)


def get_session() -> AsyncSession:
    """Gets the current active database session in this context."""
    session = current_session.get()
    if session is None:
        raise RuntimeError(
            "No database session found in current context. Are you using @transactional?"
        )
    return session


def transactional(func: Callable) -> Callable:
    """
    A decorator that automatically opens an async database session and manages
    the transaction boundaries. If the decorated function completes successfully,
    the transaction is committed. If an exception occurs, it is rolled back.
    """

    @functools.wraps(func)
    async def wrapper(*args, **kwargs) -> Any:
        session_maker = db_manager.get_session_maker()
        async with session_maker() as session:
            async with session.begin():
                token = current_session.set(session)
                try:
                    # Propagate context variables into database session if context variables exist
                    try:
                        from packages.security.context import (
                            current_change_reason,
                            current_user_id,
                        )

                        user_id = current_user_id.get()
                        reason = current_change_reason.get()
                        if user_id:
                            await session.execute(
                                text(
                                    "SELECT set_config('cadence.current_user_id', :user_id, true);"
                                ),
                                {"user_id": user_id},
                            )
                        if reason:
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
