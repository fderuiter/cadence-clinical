import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator

from fastapi import BackgroundTasks, FastAPI
from pydantic import BaseModel

from apps.execution.database.context import current_change_reason, current_session
from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import TranslationJob
from apps.execution.queue import get_queue_worker, init_queue_worker
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup, launches the background
    transactional queue worker/sweeper loop, and securely cleans up connections
    and workers on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    # Initialize shared database library
    db_manager.init_db(DATABASE_URL)

    # Initialize and start the background queue worker
    session_maker = db_manager.get_session_maker()
    worker = init_queue_worker(session_maker)
    await worker.start()

    yield

    # Clean up / stop the background worker
    try:
        worker = get_queue_worker()
        await worker.stop()
    except Exception:
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

    Inserts a TranslationJob record in PENDING status inside a database transaction,
    then triggers the background QueueWorker to claim and process it.

    Args:
        event (StudyEvent): The incoming study event payload.
        background_tasks (BackgroundTasks): FastAPI background task manager.

    Returns:
        dict[str, str]: A status message confirming job acceptance.
    """
    # Requirement 1: The system must record and persist all incoming translation requests in a database queue with a pending status before returning a response to the client.
    session_maker = db_manager.get_session_maker()

    async with session_maker() as session:
        async with session.begin():
            token = current_session.set(session)
            try:
                current_change_reason.set("queue_translation_job")
                job = TranslationJob(
                    study_id=event.study_id,
                    payload=event.payload,
                    status="PENDING",
                    max_retries=3,
                )
                session.add(job)
            finally:
                current_session.reset(token)

    # Wake up the queue worker or trigger a one-off execution via background_tasks
    try:
        init_queue_worker(session_maker)
    except Exception:
        pass

    try:
        worker = get_queue_worker()
        worker.wakeup()
        # AC 1: We also add a task to execute background processing immediately
        background_tasks.add_task(worker.claim_and_process_one)
    except Exception:
        pass

    return {"status": "accepted", "message": "Translation job queued in background."}
