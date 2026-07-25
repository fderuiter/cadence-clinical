"""
FastAPI application entrypoint for the Organization Directory microservice.

Configures async database lifecycle management, security context middleware, and
health check endpoint.
"""

import os
from contextlib import asynccontextmanager
from typing import AsyncGenerator

from fastapi import FastAPI

from apps.org.database import db_manager
from apps.org.models import Base
from packages.database import DatabaseSessionDependency
from packages.security.middleware import GatewayAuthMiddleware

# Retrieve database URL from environment or default to in-memory SQLite
DATABASE_URL = os.getenv("ORG_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handles startup and shutdown lifespan events for the microservice.

    Initializes the async SQLAlchemy engine/sessionmaker and automatically
    creates the schema/tables when configured to use SQLite.
    """
    db_manager.init_db(DATABASE_URL)

    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - Organization Directory",
    version="0.1.0",
    lifespan=lifespan,
)

# Register internal gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Standardized DB session dependency
get_db_session = DatabaseSessionDependency(db_manager)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Health check endpoint to verify microservice availability.

    Bypasses standard API Gateway headers validation.
    """
    return {"status": "ok", "service": "org"}
