import asyncio
import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator

from fastapi import BackgroundTasks, FastAPI, HTTPException, Request
from pydantic import BaseModel

from apps.execution.database.core import db_manager
from apps.execution.database.decorators import transactional
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import Cohort, Subject
from apps.execution.ledger import run_sealing_loop
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

    # Start the ledger sealing background loop
    sealing_task = asyncio.create_task(run_sealing_loop(db_manager.get_session_maker()))

    yield
    # Cleanup database connection
    sealing_task.cancel()
    try:
        await sealing_task
    except asyncio.CancelledError:
        pass
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


class CohortStatusUpdate(BaseModel):
    """Pydantic model representing a request to update a cohort's status.

    Attributes:
        status (str): The new status for the cohort (e.g., 'active', 'closed').
    """

    status: str


@app.put("/cohorts/{cohort_id}/status")
@transactional(lambda: db_manager.get_session_maker()())
async def update_cohort_status(
    cohort_id: str, update: CohortStatusUpdate, request: Request
) -> dict[str, Any]:
    """Updates the status of a specific cohort.

    This endpoint enforces GxP compliance by requiring a text-based justification
    (via the 'X-Change-Reason' header) before modifying the state of a cohort.
    The change is executed within a transaction to ensure automatic logging.

    Args:
        cohort_id (str): The unique identifier of the cohort to update.
        update (CohortStatusUpdate): The new status payload.
        request (Request): The incoming HTTP request.

    Raises:
        HTTPException: If the change justification is missing or if the cohort is not found.

    Returns:
        dict[str, Any]: A success payload with the updated cohort ID and status.
    """
    change_reason = request.headers.get("x-change-reason")
    if not change_reason or not change_reason.strip():
        raise HTTPException(status_code=400, detail="Missing change justification")

    from apps.execution.database.context import get_session

    session = get_session()

    cohort = await session.get(Cohort, cohort_id)
    if not cohort:
        raise HTTPException(status_code=404, detail="Cohort not found")

    cohort.status = update.status
    return {"status": "success", "cohort_id": cohort_id, "new_status": update.status}


class EnrollmentRequest(BaseModel):
    """Pydantic model representing a request to enroll a subject.

    Attributes:
        subject_uid (str): The unique identifier of the subject.
        cohort_id (str): The target cohort for enrollment.
    """

    subject_uid: str
    cohort_id: str


@app.post("/subjects/enroll")
@transactional(lambda: db_manager.get_session_maker()())
async def enroll_subject(request: EnrollmentRequest) -> dict[str, Any]:
    """Enrolls a new subject into a cohort.

    Evaluates the cohort's active status in real-time. If the cohort is not active,
    the enrollment request is rejected. This runs within a single transaction boundary.

    Args:
        request (EnrollmentRequest): The subject enrollment details.

    Raises:
        HTTPException: If the cohort is not found or is no longer active.

    Returns:
        dict[str, Any]: A success payload containing the new subject's database ID.
    """
    from apps.execution.database.context import get_session

    session = get_session()

    cohort = await session.get(Cohort, request.cohort_id)
    if not cohort:
        raise HTTPException(status_code=404, detail="Cohort not found")

    if cohort.status != "active":
        raise HTTPException(status_code=400, detail="Cohort is not active")

    subject = Subject(subject_uid=request.subject_uid, cohort_id=request.cohort_id)
    session.add(subject)

    return {"status": "success", "subject_id": subject.id}
