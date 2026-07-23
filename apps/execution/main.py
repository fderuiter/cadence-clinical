import asyncio
import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator

from fastapi import BackgroundTasks, FastAPI
from pydantic import BaseModel

from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import TranslationJob
from apps.execution.translator import (
    poll_and_process_jobs,
    polling_daemon_loop,
    recover_active_jobs,
)
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup, recovers active jobs,
    starts the background polling daemon, and securely cleans up connections
    on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    # Initialize shared database library
    db_manager.init_db(DATABASE_URL)

    # 1. Recover any jobs left in "PROCESSING" status back to "PENDING"
    await recover_active_jobs()

    # 2. Start background polling daemon
    daemon_task = asyncio.create_task(polling_daemon_loop())
    app.state.polling_daemon = daemon_task

    yield

    # 3. Cancel the background polling daemon on shutdown
    daemon_task.cancel()
    try:
        await daemon_task
    except asyncio.CancelledError:
        pass

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
    # Requirement 1: Save persistent pending task entry immediately
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            job = TranslationJob(
                study_id=event.study_id,
                status="PENDING",
                payload=event.payload,
            )
            session.add(job)

    # Requirement 2: Background polling process picks it up; we trigger immediate check for lower latency.
    background_tasks.add_task(poll_and_process_jobs)
    return {"status": "accepted", "message": "Translation job queued in background."}
