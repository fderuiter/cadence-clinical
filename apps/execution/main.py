import os
from typing import AsyncGenerator
from contextlib import asynccontextmanager
from fastapi import FastAPI
from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")

@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    # Initialize shared database library
    db_manager.init_db(DATABASE_URL)
    yield
    # Cleanup database connection
    await db_manager.close()

app = FastAPI(
    title="Cadence Clinical - EDC Execution Engine",
    version="0.1.0",
    lifespan=lifespan
)

app.add_middleware(ContextResetMiddleware)

@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Health check endpoint for the execution service.

    Returns:
        dict[str, str]: The health status and service name.
    """
    return {"status": "ok", "service": "execution"}