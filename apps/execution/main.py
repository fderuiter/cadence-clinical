import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator

import usdm_model
from fastapi import BackgroundTasks, FastAPI, HTTPException, status
from pydantic import BaseModel, ValidationError
from sqlalchemy import func, select

from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import TranslationJob
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


@app.post("/events/study-published", status_code=202)
async def study_published(
    event: StudyEvent, background_tasks: BackgroundTasks
) -> dict[str, str]:
    """Ingest study publication events, validate and sequence them synchronously,
    and trigger layout generation asynchronously in the background.

    Args:
        event (StudyEvent): The incoming study event payload.
        background_tasks (BackgroundTasks): FastAPI background task manager.

    Returns:
        dict[str, str]: A status message confirming job acceptance.
    """
    payload = event.payload

    # 1. USDM Validation
    try:
        # Validate payload against the formal USDM schema using local model definitions
        usdm_model.Study(**payload)
    except (ValidationError, ValueError) as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Validation Failed: {str(e)}",
        )

    # 2. Extract Version Number
    version_num = 1
    if "version_index" in payload and payload["version_index"] is not None:
        try:
            version_num = int(payload["version_index"])
        except (ValueError, TypeError):
            pass
    elif "version" in payload and payload["version"] is not None:
        try:
            version_num = int(float(payload["version"]))
        except (ValueError, TypeError):
            pass
    elif "version_number" in payload and payload["version_number"] is not None:
        try:
            version_num = int(float(payload["version_number"]))
        except (ValueError, TypeError):
            pass
    elif (
        "versions" in payload
        and isinstance(payload["versions"], list)
        and len(payload["versions"]) > 0
    ):
        first_ver = payload["versions"][0]
        if isinstance(first_ver, dict):
            v_id = first_ver.get("versionIdentifier")
            if v_id is not None:
                try:
                    version_num = int(float(v_id))
                except (ValueError, TypeError):
                    pass

    # 3. Sequencing Check
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        stmt = select(func.max(TranslationJob.version)).where(
            TranslationJob.study_id == event.study_id
        )
        res = await session.execute(stmt)
        max_version = res.scalar()

        if max_version is not None and version_num <= max_version:
            raise HTTPException(
                status_code=status.HTTP_409_CONFLICT,
                detail=f"Conflict: Payload version {version_num} is equal to or lower than the existing stored study version {max_version}.",
            )

    # 4. Trigger asynchronous translation process
    background_tasks.add_task(
        process_translation,
        event.study_id,
        event.payload,
        db_manager.get_session_maker(),
        version_num,
    )
    return {"status": "accepted", "message": "Translation job queued in background."}
