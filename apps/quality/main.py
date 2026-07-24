import os
from contextlib import asynccontextmanager
from typing import AsyncGenerator

from fastapi import FastAPI
from sqlalchemy.ext.asyncio import AsyncSession

from apps.quality.database import db_manager
from apps.quality.models import Base, QualityAuditLog
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("QUALITY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the Quality & CAPA application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown. Creates all tables if sqlite is used.
    """
    db_manager.init_db(DATABASE_URL)

    # Automatically create tables for sqlite in-memory/file databases
    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - Quality & CAPA",
    version="0.1.0",
    lifespan=lifespan,
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


# Dependable to obtain database session
async def get_db_session() -> AsyncGenerator[AsyncSession, None]:
    """
    Dependency to yield an asynchronous database session.
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise


async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
) -> None:
    """
    Utility helper to write to the append-only QualityAuditLog.
    """
    log_entry = QualityAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
    )
    session.add(log_entry)
    await session.flush()


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "quality"}
