import functools
from typing import Any, Callable

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from .context import current_session


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
                        result = await func(*args, **kwargs)
                        # Explicit commit is not strictly necessary here because
                        # 'async with session.begin()' will commit automatically
                        # upon successful exit of the block, and rollback on exception.
                        return result
                    finally:
                        current_session.reset(token)

        return wrapper

    return decorator
