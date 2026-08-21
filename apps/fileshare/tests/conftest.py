"""Pytest configuration and fixtures for the Fileshare microservice test harness.

Uses packages/testing infrastructure: InMemoryStoragePort, security mocks.
Gated database provisioning per pytest-xdist isolation rules.
"""

from collections.abc import AsyncGenerator

import pytest_asyncio
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.fileshare.adapters.database import db_manager
from apps.fileshare.adapters.storage import set_storage_adapter
from apps.fileshare.infrastructure.models import Base
from packages.testing.fakes import InMemoryStoragePort


@pytest_asyncio.fixture(autouse=True)
def mock_storage() -> InMemoryStoragePort:
    """Provides and registers an isolated in-memory storage adapter for each test."""
    fake_storage = InMemoryStoragePort()
    set_storage_adapter(fake_storage)
    return fake_storage


@pytest_asyncio.fixture
async def db_session() -> AsyncGenerator[AsyncSession]:
    """Provides an isolated in-memory SQLite async session for each test."""
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

