"""Pytest fixtures and test environment setup for Site Payments microservice."""

import os
import time
from collections.abc import AsyncGenerator

import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from jose import jwt
from sqlalchemy.ext.asyncio import AsyncSession

os.environ["BRAND_NAME"] = "Cadence Clinical"
os.environ["BRAND_DOMAIN"] = "cadenceclinical.com"
os.environ["GATEWAY_SECRET"] = "internal-gateway-secret-12345"
os.environ["AUDIT_LOG_SECRET_KEY"] = "test-payments-audit-secret-key-999"
os.environ["ALLOW_MOCK_SIGNATURES"] = "1"

from apps.payments.adapters.database import db_manager
from apps.payments.infrastructure.models import Base
from apps.payments.main import app
from packages.testing.security import generate_signature


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """Setup isolated in-memory SQLite database for Site Payments testing."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()
    app.dependency_overrides.clear()


@pytest_asyncio.fixture
async def db_session() -> AsyncGenerator[AsyncSession]:
    """Yields an active database session for tests."""
    session_factory = db_manager.get_session_maker()
    async with session_factory() as session:
        yield session


def get_auth_headers(
    roles: str = "Grants Manager",
    change_reason: str = "Financial operations",
    action: str | None = None,
    user_id: str = "grants_lead_01",
) -> dict[str, str]:
    """Generates valid HMAC gateway authentication headers for testing."""
    timestamp = str(time.time())
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    if action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action,
            "roles": [roles],
            "iat": time.time(),
            "exp": time.time() + 300.0,
        }
        headers["X-Sig-Token"] = jwt.encode(
            sig_payload, "internal-gateway-secret-12345", algorithm="HS256"
        )
    return headers


@pytest_asyncio.fixture
async def client() -> AsyncGenerator[AsyncClient]:
    """Yields an asynchronous test client bound to the FastAPI application."""
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac
