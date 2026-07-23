from typing import Any, Optional

from sqlalchemy import event
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine


class InteropDatabaseManager:
    """
    Manages the lifecycle of the Interop service's database connections and sessions.
    """

    def __init__(self) -> None:
        self.engine: Any = None
        self.session_maker: Optional[async_sessionmaker[AsyncSession]] = None

    def init_db(self, database_url: str, **kwargs: Any) -> None:
        """
        Initialize the async engine and session maker for the Interop database.
        """
        self.engine = create_async_engine(database_url, **kwargs)

        @event.listens_for(self.engine.sync_engine, "connect")
        def set_sqlite_pragma(dbapi_connection, connection_record):
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
        """Close database engine and cleanup sessions."""
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        if not self.session_maker:
            raise Exception("Interop database session manager is not initialized.")
        return self.session_maker


db_manager = InteropDatabaseManager()
