"""Pytest fixtures and test environment setup for Medical Imaging microservice."""

import os
import shutil
import tempfile
from collections.abc import AsyncGenerator

import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

# Set test environment variables before importing app
os.environ["BRAND_NAME"] = "Cadence Clinical"
os.environ["BRAND_DOMAIN"] = "cadenceclinical.com"
os.environ["GATEWAY_SECRET"] = "test-gateway-secret-12345"
os.environ["AUDIT_LOG_SECRET_KEY"] = "test-imaging-audit-secret-key-999"

from apps.imaging.adapters.database import db_manager
from apps.imaging.adapters.storage import set_storage_port
from apps.imaging.infrastructure.models import Base
from apps.imaging.main import app
from packages.testing.fakes import InMemoryStoragePort
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture
async def test_env():
    """Provisions an isolated in-memory test environment for SQLite database and storage."""
    temp_dir = tempfile.mkdtemp(prefix="cadence_imaging_test_")
    db_file = os.path.join(temp_dir, "imaging_test.db")
    db_url = f"sqlite+aiosqlite:///{db_file}"

    # Initialize db_manager with SQLite database
    db_manager.init_db(db_url)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    # Set isolated in-memory storage adapter
    storage_provider = InMemoryStoragePort()
    set_storage_port(storage_provider)

    yield {"temp_dir": temp_dir, "db_url": db_url, "storage": storage_provider}

    await db_manager.close()
    set_storage_port(None)
    app.dependency_overrides.clear()
    shutil.rmtree(temp_dir, ignore_errors=True)


@pytest_asyncio.fixture
async def db_session(test_env) -> AsyncGenerator[AsyncSession]:
    """Yields a database session for test operations."""
    session_factory = db_manager.get_session_maker()
    async with session_factory() as session:
        yield session


@pytest_asyncio.fixture
async def client(test_env) -> AsyncGenerator[AsyncClient]:
    """Yields an authenticated AsyncClient bound to the imaging FastAPI app."""
    transport = ASGITransport(app=app)
    headers = create_test_auth_headers(
        user_id="imaging.crc.user",
        roles=["Site Investigator / CRC"],
        tenant_id="tenant_clinical_test",
        change_reason="Imaging test upload and review",
        secret=b"test-gateway-secret-12345",
    )
    async with AsyncClient(
        transport=transport, base_url="http://test", headers=headers
    ) as ac:
        yield ac
