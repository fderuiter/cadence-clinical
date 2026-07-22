import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator

from fastapi import BackgroundTasks, FastAPI
from pydantic import BaseModel

from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.translator import process_translation
from packages.security.middleware import GatewayAuthMiddleware

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
    title="Cadence Clinical - EDC Execution Engine", version="0.1.0", lifespan=lifespan
)

app.add_middleware(ContextResetMiddleware)

app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "execution"}


class StudyEvent(BaseModel):
    """Pydantic model representing an incoming study publication event.

    Attributes:
        study_id (str): The unique identifier of the study.
        payload (dict[str, Any]): The raw USDM protocol payload.
    """

    study_id: str
    payload: dict[str, Any]


@app.post("/events/study-published")
async def study_published(
    event: StudyEvent, background_tasks: BackgroundTasks
) -> dict[str, str]:
    """Ingest study publication events and trigger layout generation asynchronously.

    Args:
        event (StudyEvent): The incoming study event payload.
        background_tasks (BackgroundTasks): FastAPI background task manager.

    Returns:
        dict[str, str]: A status message confirming job acceptance.
    """
    # Requirement 1: Listen for study publication events and trigger translation processes in the background.
    background_tasks.add_task(
        process_translation,
        event.study_id,
        event.payload,
        db_manager.get_session_maker(),
    )
    return {"status": "accepted", "message": "Translation job queued in background."}
