from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator, Optional

from fastapi import FastAPI
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


def get_relational_db_lifespan(
    db_manager: RelationalDatabaseManager,
    database_url: str,
    base_metadata: Optional[Any] = None,
    startup_hooks: Optional[list] = None,
    shutdown_hooks: Optional[list] = None,
    **kwargs: Any,
) -> Any:
    """
    Unified application lifecycle wrapper that automatically handles database connection
    setup and local migrations (on SQLite), and supports parameterized callback hooks for
    executing service-specific startup and shutdown tasks.
    """

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
        # Initialize database engine and session maker
        db_manager.init_db(database_url, **kwargs)

        # Run local migrations if using sqlite
        if database_url.startswith("sqlite") and base_metadata is not None:
            async with db_manager.engine.begin() as conn:
                await conn.run_sync(base_metadata.create_all)

        # Run service-specific asynchronous startup tasks
        if startup_hooks:
            for hook in startup_hooks:
                await hook()

        try:
            yield
        finally:
            # Run service-specific asynchronous shutdown tasks
            if shutdown_hooks:
                for hook in shutdown_hooks:
                    await hook()
            # Clean up database engine
            await db_manager.close()

    return lifespan
