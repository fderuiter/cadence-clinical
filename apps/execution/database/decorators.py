import functools
from typing import Any, Callable

from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from .context import current_change_reason, current_session, current_user_id


def transactional(session_factory: async_sessionmaker[AsyncSession]):
    """
    A decorator that automatically opens an async database session and manages
    the transaction boundaries. If the decorated function completes successfully,
    the transaction is committed. If an exception occurs, it is rolled back.
    """

    def decorator(func: Callable) -> Callable:
        @functools.wraps(func)
        async def wrapper(*args, **kwargs) -> Any:
            # Create a new session and begin a transaction
            async with session_factory() as session:
                async with session.begin():
                    # Set the session in the context var so other functions can access it
                    token = current_session.set(session)
                    try:
                        # Propagate context variables into database session
                        user_id = current_user_id.get()
                        reason = current_change_reason.get()
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

                        result = await func(*args, **kwargs)
                        # Explicit commit is not strictly necessary here because
                        # 'async with session.begin()' will commit automatically
                        # upon successful exit of the block, and rollback on exception.
                        return result
                    finally:
                        current_session.reset(token)

        return wrapper

    return decorator
