from typing import Any, AsyncGenerator, Optional

from sqlalchemy import event
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine


class RelationalDatabaseManager:
    """
    Parameterized relational database manager capable of managing connection pools based on distinct microservice configurations.
    """

    def __init__(self, service_name: str) -> None:
        self.service_name = service_name
        self.engine: Any = None
        self.session_maker: Optional[async_sessionmaker[AsyncSession]] = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        self.engine = create_async_engine(database_url, **kwargs)

        @event.listens_for(self.engine.sync_engine, "connect")
        def set_sqlite_pragma(dbapi_connection, connection_record):
            """Enable SQLite foreign key support on connect event."""
            # If using sqlite, ensure foreign keys are enabled (if dialect is sqlite)
            cursor = dbapi_connection.cursor()
            try:
                cursor.execute("PRAGMA foreign_keys=ON")
            except Exception:
                pass
            finally:
                cursor.close()

        self.session_maker = async_sessionmaker(
            bind=self.engine, class_=AsyncSession, expire_on_commit=False
        )

    async def close(self) -> None:
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        if not self.session_maker:
            raise Exception(
                f"{self.service_name} database session manager is not initialized."
            )
        return self.session_maker


class DatabaseSessionDependency:
    """
    Standardized FastAPI route dependency helper that manages database session lifespans,
    automatically committing on success or rolling back on failure.
    """

    def __init__(self, db_manager: RelationalDatabaseManager) -> None:
        self.db_manager = db_manager

    async def __call__(self) -> AsyncGenerator[AsyncSession, None]:
        session_maker = self.db_manager.get_session_maker()
        async with session_maker() as session:
            try:
                yield session
                await session.commit()
            except Exception:
                await session.rollback()
                raise
