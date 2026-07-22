from typing import Any, Optional

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine


class DatabaseSessionManager:
    """
    Manages the lifecycle of database connections and sessions.

    This unified manager simplifies initialization and teardown of the
    asynchronous SQLAlchemy engine and session makers, facilitating
    both application runtime execution and test configurations.
    """

    def __init__(self) -> None:
        """Initialize the DatabaseSessionManager with empty state."""
        self.engine: Any = None
        self.session_maker: Optional[async_sessionmaker[AsyncSession]] = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        """
        Initialize the database engine and session maker.

        Args:
            database_url (str): The connection string for the database.
            **kwargs (Any): Additional arguments to pass to the async engine.
        """
        self.engine = create_async_engine(database_url, **kwargs)
        self.session_maker = async_sessionmaker(
            bind=self.engine, class_=AsyncSession, expire_on_commit=False
        )

    async def close(self) -> None:
        """Close the database engine and clear the session maker."""
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        """
        Retrieve the configured async session maker.

        Returns:
            async_sessionmaker[AsyncSession]: The active session factory.

        Raises:
            Exception: If the database has not been initialized.
        """
        if not self.session_maker:
            raise Exception("Database session manager is not initialized.")
        return self.session_maker


db_manager: DatabaseSessionManager = DatabaseSessionManager()
