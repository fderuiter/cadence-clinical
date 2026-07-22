import time
from typing import Any, Dict, List

from fastapi import FastAPI, HTTPException, status
from pydantic import BaseModel

from apps.designer.db import get_study_projection, terminology_cache
from apps.designer.mapper import map_study_to_usdm
from apps.designer.validator import StudyAlignmentReport, generate_alignment_report
from packages.security.middleware import GatewayAuthMiddleware


class DifferenceResult(BaseModel):
    """
    Represents a field-level difference between two versions.

    Attributes:
        field: The name of the field that changed.
        old_value: The previous value of the field.
        new_value: The updated value of the field.
    """

    field: str
    old_value: Any
    new_value: Any


app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")

app.add_middleware(GatewayAuthMiddleware)


@app.on_event("startup")
async def startup() -> None:
    """Initialize resources on designer startup."""
    pass


@app.on_event("shutdown")
async def shutdown() -> None:
    """Clean up resources on designer shutdown."""
    pass


@app.get("/health")
async def health_check() -> Dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        Dict[str, str]: The health status payload.
    """
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
async def get_usdm_study(study_id: str) -> Dict[str, Any]:
    """Dynamically processes the internal projection and returns a compliant USDM structure.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        Dict[str, Any]: The dynamically mapped USDM study data.

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
async def validate_study_alignment(study_id: str) -> StudyAlignmentReport:
    """
    Generate an alignment validation report for a specific clinical study.

    Analyzes trace links dynamically to ensure the
    Study Data Requirements (SDR) align with Metadata Requirements (MDR).

    Args:
        study_id (str): The unique identifier of the study to validate.

    Returns:
        StudyAlignmentReport: The structured validation report.
    """
    return await generate_alignment_report(study_id)


@app.get(
    "/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult]
)
async def study_differences(
    study_id: str, action_id1: str, action_id2: str
) -> List[DifferenceResult]:
    """
    Get human-readable field-level differences between two version actions of a study.

    Args:
        study_id (str): The unique identifier of the study.
        action_id1 (str): The ID of the first action version.
        action_id2 (str): The ID of the second action version.

    Returns:
        List[DifferenceResult]: A list of field-level differences.

    Raises:
        HTTPException: Raises 503 as the direct database connection is disabled in the API-first design.
    """
    raise HTTPException(status_code=503, detail="Database connection not initialized")
