from collections.abc import AsyncGenerator
from contextlib import asynccontextmanager
from typing import Any

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
        self.session_maker: async_sessionmaker[AsyncSession] | None = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        self.engine = create_async_engine(database_url, **kwargs)

        @event.listens_for(self.engine.sync_engine, "connect")
        def set_sqlite_pragma(dbapi_connection, connection_record):
            """Enable SQLite foreign key support on connect event."""
            cursor = dbapi_connection.cursor()
            try:
                cursor.execute("PRAGMA foreign_keys=ON")
                cursor.execute("PRAGMA busy_timeout=30000")
            except Exception:
                pass
            finally:
                cursor.close()

            # Set raw sqlite3 connection isolation level to IMMEDIATE
            try:
                if hasattr(dbapi_connection, "isolation_level"):
                    dbapi_connection.isolation_level = "IMMEDIATE"
                elif hasattr(getattr(dbapi_connection, "dbapi_connection", None), "isolation_level"):
                    dbapi_connection.dbapi_connection.isolation_level = "IMMEDIATE"
            except Exception:
                pass

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

    async def __call__(self) -> AsyncGenerator[AsyncSession]:
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
    base_metadata: Any | None = None,
    startup_hooks: list | None = None,
    shutdown_hooks: list | None = None,
    **kwargs: Any,
) -> Any:
    """
    Unified application lifecycle wrapper that automatically handles database connection
    setup and local migrations (on SQLite), and supports parameterized callback hooks for
    executing service-specific startup and shutdown tasks.
    """
    import os

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncGenerator[None]:
        # Dynamically check if there's an environment variable override for this service's DB
        env_key = f"{db_manager.service_name.upper()}_DATABASE_URL"
        url = os.getenv(env_key) or database_url

        # Initialize database engine and session maker
        db_manager.init_db(url, **kwargs)

        # Run local migrations if using sqlite
        if url.startswith("sqlite") and base_metadata is not None:
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
