import os
import time
from typing import Any, Dict, List

from fastapi import FastAPI, HTTPException, status
from neo4j import AsyncGraphDatabase
from pydantic import BaseModel

from apps.designer.db import get_study_projection, terminology_cache
from apps.designer.delta import get_study_differences
from apps.designer.mapper import map_study_to_usdm
from apps.designer.validator import StudyAlignmentReport, generate_alignment_report
from packages.core_models.usdm import StudyDefinition


class DifferenceResult(BaseModel):
    field: str
    old_value: Any
    new_value: Any


app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")

driver = None


@app.on_event("startup")
async def startup():
    global driver
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "cadence_password")
    try:
        driver = AsyncGraphDatabase.driver(uri, auth=(user, password))
    except Exception as e:
        print(f"Failed to connect to Neo4j: {e}")


@app.on_event("shutdown")
async def shutdown():
    global driver
    if driver:
        await driver.close()


@app.get("/health")
async def health_check():
    return {"status": "ok", "service": "designer"}


@app.get("/api/v1/studies/{study_id}")
async def get_legacy_study(study_id: str) -> Dict[str, Any]:
    """Returns the legacy internal projection with no USDM formatting.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        Dict[str, Any]: The legacy study projection data.

    Raises:
        HTTPException: If the study is not found.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")
    return study_data


@app.get("/api/v2/studies/{study_id}/usdm")
async def get_usdm_study(study_id: str) -> StudyDefinition:
    """Dynamically processes the internal projection and returns a compliant USDM structure.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        StudyDefinition: The dynamically mapped USDM study data.

    Raises:
        HTTPException: If the study is not found or validation fails.
    """
    start_time = time.perf_counter()
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    try:
        usdm_study = map_study_to_usdm(study_data)
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Validation Error mapping USDM: {str(e)}"
        )

    duration = (time.perf_counter() - start_time) * 1000
    # Simulate processing overhead check - we want this under 200ms
    if duration > 200:
        pass  # In a real app we might log a warning

    return usdm_study


@app.post("/api/admin/cache/clear", status_code=status.HTTP_200_OK)
async def clear_cache() -> Dict[str, str]:
    """Flushes the controlled terminology cache.

    Returns:
        Dict[str, str]: A success message indicating the cache was cleared.
    """
    terminology_cache.clear()
    return {"status": "success", "message": "Cache cleared successfully"}


@app.get("/api/admin/cache/status")
async def cache_status() -> Dict[str, int]:
    """Returns the current size and status of the terminology cache.

    Returns:
        Dict[str, int]: The status dictionary containing size and max_size.
    """
    return terminology_cache.get_status()


@app.get(
    "/api/v1/studies/{study_id}/alignment-validation",
    response_model=StudyAlignmentReport,
)
async def validate_study_alignment(study_id: str):
    if not driver:
        raise HTTPException(
            status_code=503, detail="Database connection not initialized"
        )
    return await generate_alignment_report(driver, study_id)


@app.get(
    "/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult]
)
async def study_differences(study_id: str, action_id1: str, action_id2: str):
    if not driver:
        raise HTTPException(
            status_code=503, detail="Database connection not initialized"
        )

    diffs = await get_study_differences(driver, study_id, action_id1, action_id2)
    return diffs
