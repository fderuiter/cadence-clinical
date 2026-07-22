import os
import time
from typing import Any, Dict, List, Tuple

import httpx
from fastapi import FastAPI, File, HTTPException, UploadFile, status
from pydantic import BaseModel

from apps.designer.db import get_study_projection, terminology_cache
from apps.designer.mapper import map_study_to_usdm
from apps.designer.validator import StudyAlignmentReport, generate_alignment_report
from apps.designer.xml_mapping import validate_mapping_csv
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

    This endpoint uses a decoupled, API-first in-memory diffing architecture. Instead of
    relying on a direct database connection (which led to 503 errors and tight coupling),
    it fetches full study payloads from an external registry. The comparison logic runs
    entirely in-memory by flattening nested dictionary structures to dynamically identify
    added, modified, and deleted fields. This ensures high availability and fast execution
    without maintaining direct database connections.

    Args:
        study_id (str): The unique identifier of the study.
        action_id1 (str): The ID of the first action version.
        action_id2 (str): The ID of the second action version.

    Returns:
        List[DifferenceResult]: A list of field-level differences.
    """
    base_url = os.getenv("STUDY_REGISTRY_URL", "http://localhost:8000")

    try:
        async with httpx.AsyncClient() as client:
            response = await client.get(
                f"{base_url}/usdm/v4/studies/{study_id}", timeout=5.0
            )
            response.raise_for_status()
            data = response.json()
    except httpx.TimeoutException:
        raise HTTPException(status_code=504, detail="Registry timeout")
    except httpx.RequestError:
        raise HTTPException(status_code=502, detail="External registry offline")
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 404:
            raise HTTPException(status_code=404, detail="Study not found in registry")
        raise HTTPException(status_code=e.response.status_code, detail="Registry error")

    versions = data.get("versions", [])

    v1_data = None
    v2_data = None
    for v in versions:
        if v.get("id") == action_id1:
            v1_data = v
        if v.get("id") == action_id2:
            v2_data = v

    if not v1_data:
        raise HTTPException(
            status_code=400,
            detail=f"Target version {action_id1} is missing from the registry",
        )
    if not v2_data:
        raise HTTPException(
            status_code=400,
            detail=f"Target version {action_id2} is missing from the registry",
        )

    def flatten_dict(d: Any, parent_key: str = "", sep: str = ".") -> Dict[str, Any]:
        """
        Recursively flatten a nested dictionary or list into a flat dictionary.

        This enables efficient 1D in-memory comparison of complex nested JSON
        payloads (like USDM) by generating unique dot-notated paths for every node.

        Args:
            d (Any): The dictionary, list, or primitive to flatten.
            parent_key (str): The accumulated path key.
            sep (str): The separator used for nested keys.

        Returns:
            Dict[str, Any]: A flattened dictionary mapping paths to values.
        """
        items: List[Tuple[str, Any]] = []
        if isinstance(d, dict):
            for k, v in d.items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif isinstance(d, list):
            for i, v in enumerate(d):
                new_key = f"{parent_key}{sep}[{i}]" if parent_key else f"[{i}]"
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        else:
            items.append((parent_key, d))
        return dict(items)

    flat_v1 = flatten_dict(v1_data)
    flat_v2 = flatten_dict(v2_data)

    all_keys = set(flat_v1.keys()).union(set(flat_v2.keys()))
    differences = []

    for key in sorted(all_keys):
        val1 = flat_v1.get(key)
        val2 = flat_v2.get(key)
        if val1 != val2:
            differences.append(
                DifferenceResult(field=key, old_value=val1, new_value=val2)
            )

    return differences


@app.post("/api/v1/mappings/upload", status_code=status.HTTP_200_OK)
async def upload_mapping_csv(file: UploadFile = File(...)):
    """
    Validates a CSV mapping configuration to ensure target names meet standard W3C XML naming specifications.

    Raises:
        HTTPException: If the CSV format is invalid or if target XML names violate naming rules.
    """
    try:
        content = (await file.read()).decode("utf-8")
        rows = validate_mapping_csv(content)
        return {"status": "success", "rows_processed": len(rows)}
    except ValueError as e:
        raise HTTPException(status_code=422, detail=f"Validation Error: {str(e)}")
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Processing Error: {str(e)}")
