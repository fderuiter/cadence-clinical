"""FastAPI router for central and local laboratory batch data ingestion.

Requirements:
- PRD-LAB-001 (Laboratory Batch Ingestion & Range Evaluation)
- PRD-MDR-001 (Metadata Repository & Catalog Normalization)
- PRD-QRY-001 (Automated Discrepancy Query Escalation)
- Trace-1 (Audit Trail & 21 CFR Part 11)
- Trace-15 (Laboratory Data Flow & Reference Ranges)
"""

from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
    Query,
    Request,
)
from sqlalchemy.ext.asyncio import AsyncSession

from apps.execution.adapters.repositories import get_execution_db_session
from apps.execution.database.context import (
    current_change_reason,
    current_user_id,
)
from apps.execution.services.lab_ingestion_service import (
    LabBatchIngestResult,
    LabIngestionService,
)
from packages.security import (
    verify_not_auditor,
)

router = APIRouter(prefix="/api/v1/execution/labs", tags=["Laboratory Ingestion"])


@router.post("/ingest", response_model=LabBatchIngestResult, status_code=200)
async def ingest_lab_batch_endpoint(
    request: Request,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_execution_db_session),
) -> LabBatchIngestResult:
    """Ingest central or local laboratory batch data (CSV, HL7 v2.x, or FHIR JSON).

    Supports JSON payloads containing CSV/HL7 strings or FHIR Observation objects/bundles,
    as well as multipart/form-data file uploads.

    Args:
        request: FastAPI HTTP request instance.
        background_tasks: BackgroundTasks context for dispatching critical alerts.
        payload_body: Optional parsed JSON body.
        roles: Verified principal roles ensuring write permissions.

    Returns:
        LabBatchIngestResult: Summary of batch processing statistics and auto-queries.
    """
    content_type = request.headers.get("content-type", "").lower()

    study_id: str | None = None
    site_id: str | None = None
    lab_source: str = "CENTRAL"
    format_type: str = "csv"
    reason_for_change: str = "Batch laboratory data ingestion"
    raw_payload: Any = None

    if (
        "multipart/form-data" in content_type
        or "application/x-www-form-urlencoded" in content_type
    ):
        form = await request.form()
        format_type = str(form.get("format", "csv"))
        study_id = str(form.get("study_id")) if form.get("study_id") else None
        site_id = str(form.get("site_id")) if form.get("site_id") else None
        lab_source = str(form.get("lab_source", "CENTRAL"))
        reason_for_change = str(
            form.get("reason_for_change", "Batch laboratory data ingestion")
        )

        upload_file = form.get("file")
        if upload_file is not None and hasattr(upload_file, "read"):
            file_bytes = await upload_file.read()
            raw_payload = file_bytes.decode("utf-8-sig", errors="replace")
        elif "payload" in form:
            raw_payload = str(form.get("payload"))
    else:
        # JSON body handling
        try:
            body_json = await request.json()
        except Exception:
            body_json = {}

        if isinstance(body_json, dict):
            format_type = str(body_json.get("format", "csv"))
            study_id = body_json.get("study_id")
            site_id = body_json.get("site_id")
            lab_source = body_json.get("lab_source", "CENTRAL")
            reason_for_change = body_json.get(
                "reason_for_change", "Batch laboratory data ingestion"
            )
            raw_payload = body_json.get("payload")
            if raw_payload is None and "resource" in body_json:
                raw_payload = body_json["resource"]
            elif raw_payload is None and "resources" in body_json:
                raw_payload = body_json["resources"]
            elif raw_payload is None and body_json.get("resourceType"):
                # Direct FHIR Observation or Bundle payload
                raw_payload = body_json
                format_type = "fhir"
        elif isinstance(body_json, list):
            # Direct JSON list of FHIR Observations
            raw_payload = body_json
            format_type = "fhir"

    if raw_payload is None or (
        isinstance(raw_payload, str) and not raw_payload.strip()
    ):
        raise HTTPException(
            status_code=400,
            detail="Batch ingestion requires non-empty 'payload', 'file', or 'resource'.",
        )

    user_id = current_user_id.get() or "system_lab_ingestion"
    gxp_reason = current_change_reason.get() or reason_for_change

    return await LabIngestionService.ingest_batch(
        session=session,
        payload=raw_payload,
        format=format_type,
        study_id=study_id,
        site_id=site_id,
        lab_source=lab_source,
        user_id=user_id,
        change_reason=gxp_reason,
        background_tasks=background_tasks,
    )


@router.get(
    "/batch-status", response_model=list[LabBatchIngestResult] | LabBatchIngestResult
)
async def get_batch_status_endpoint(
    batch_id: str | None = Query(None, description="Optional batch ID to query"),
    study_id: str | None = Query(None, description="Optional study ID filter"),
    roles: list[str] = Depends(verify_not_auditor),
) -> list[LabBatchIngestResult] | LabBatchIngestResult:
    """Retrieve status and metrics for previous lab batch ingestion jobs."""
    if batch_id:
        batch = LabIngestionService.get_batch_status(batch_id)
        if not batch:
            raise HTTPException(
                status_code=404,
                detail=f"Laboratory ingestion batch '{batch_id}' not found.",
            )
        return batch

    return LabIngestionService.list_batch_statuses(study_id=study_id)


@router.get("/batch-status/{batch_id}", response_model=LabBatchIngestResult)
async def get_batch_status_by_id_endpoint(
    batch_id: str,
    roles: list[str] = Depends(verify_not_auditor),
) -> LabBatchIngestResult:
    """Query execution status and statistics of a specific laboratory batch by ID."""
    batch = LabIngestionService.get_batch_status(batch_id)
    if not batch:
        raise HTTPException(
            status_code=404,
            detail=f"Laboratory ingestion batch '{batch_id}' not found.",
        )
    return batch
