"""
Test configuration for the Knowledge microservice.

Uses packages/testing infrastructure: InMemoryRepository, factories, and
security mocks. Gated database provisioning per pytest-xdist isolation rules.
"""

import pytest_asyncio
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.knowledge.infrastructure.models import Base


@pytest_asyncio.fixture
async def db_session():
    """
    Provides an in-memory SQLite async session for each test.

    Schema is created fresh per test; no cross-test state leakage.
    Rolls back all writes on teardown.
    """
    engine = create_async_engine("sqlite+aiosqlite:///:memory:", echo=False)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_factory = async_sessionmaker(
        engine, class_=AsyncSession, expire_on_commit=False
    )
    async with session_factory() as session:
        yield session
        await session.rollback()

    await engine.dispose()
