from sqlalchemy.ext.asyncio import create_async_engine, async_sessionmaker, AsyncSession
from typing import Optional

class DatabaseSessionManager:
    def __init__(self):
        self.engine = None
        self.session_maker = None

    def init_db(self, database_url: str, **kwargs):
        self.engine = create_async_engine(database_url, **kwargs)
        self.session_maker = async_sessionmaker(
            bind=self.engine,
            class_=AsyncSession,
            expire_on_commit=False
        )

    async def close(self):
        if self.engine:
            await self.engine.dispose()
            self.engine = None
            self.session_maker = None

    def get_session_maker(self) -> async_sessionmaker[AsyncSession]:
        if not self.session_maker:
            raise Exception("Database session manager is not initialized.")
        return self.session_maker

db_manager = DatabaseSessionManager()
