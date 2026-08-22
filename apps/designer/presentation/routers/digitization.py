"""FastAPI router for AI-Native USDM Protocol Digitization and automated synthesis.

Exposes endpoints for extracting structured CDISC USDM entities from raw protocol
files, executing asynchronous multi-stage DAG pipelines, and committing them into
the Neo4j knowledge graph and eCRF runtime.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    File,
    Form,
    HTTPException,
    Request,
    UploadFile,
    status,
)

from apps.designer.adapters.digitization_job_store import (
    DigitizationJobStore,
    get_digitization_job_store,
)
from apps.designer.application.services.digitization_dag_service import (
    STAGE_PROGRESS_MAP,
    DigitizationDAGRunner,
)
from apps.designer.application.services.digitization_service import (
    _heuristic_protocol_extraction,
    extract_usdm_from_protocol_document,
    synthesize_ecrf_forms,
)
from apps.designer.dependencies import get_neo4j_driver
from apps.designer.domain.digitization_dag_models import (
    CompileUSDMFromJobRequest,
    DAGJobStatusResponse,
    DigitizationJobStatus,
    ResumeDAGJobRequest,
    StartDAGJobResponse,
)
from apps.designer.domain.digitization_models import (
    CommitUSDMRequest,
    CommitUSDMResponse,
    USDMProtocolExtractionResponse,
)
from apps.designer.infrastructure.neo4j_usdm_writer import commit_usdm_graph
from packages.security.rbac import require_permission

logger = logging.getLogger(__name__)

router = APIRouter(tags=["protocol_digitization"])


def get_digitization_dag_runner(
    job_store: DigitizationJobStore | None = Depends(get_digitization_job_store),
) -> DigitizationDAGRunner:
    """Provides dependency injection for DigitizationDAGRunner initialized with DigitizationJobStore."""
    store = (
        job_store
        if isinstance(job_store, DigitizationJobStore)
        else get_digitization_job_store()
    )
    return DigitizationDAGRunner(job_store=store)


# =============================================================================
# SYNCHRONOUS PROTOCOL EXTRACTION ENDPOINTS (Backward Compatibility)
# =============================================================================


@router.post(
    "/api/v1/designer/digitization/extract",
    response_model=USDMProtocolExtractionResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("protocol_ingestion:upload"))],
)
async def extract_protocol_digitization(
    file: UploadFile = File(...),
) -> USDMProtocolExtractionResponse:
    """Extracts structured CDISC USDM v4.0 parameters from an uploaded clinical protocol file.

    Accepts PDF, DOCX, or text files.
    """
    filename = file.filename or "unknown.pdf"
    content = await file.read()

    if not content or len(content) == 0:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Uploaded protocol file is empty or corrupted.",
        )

    try:
        return await extract_usdm_from_protocol_document(content, filename)
    except Exception as exc:
        logger.error("Protocol extraction failed: %s", exc)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Protocol entity extraction failed: {str(exc)}",
        ) from exc


@router.get(
    "/api/v1/designer/digitization/sample",
    response_model=USDMProtocolExtractionResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("protocol_ingestion:read"))],
)
async def get_sample_protocol_extraction() -> USDMProtocolExtractionResponse:
    """Returns a canonical pre-extracted USDM v4.0 study structure for instant review and sandboxing."""
    sample_text = (
        "Protocol Title: A Phase II Randomized Study of Novel Therapeutic vs Control in Advanced Solid Tumors\n"
        "Protocol ID: CDNC-2026-001\n"
        "Phase: Phase II\n"
        "Therapeutic Area: Oncology\n"
    )
    return _heuristic_protocol_extraction(sample_text, "sample_protocol.pdf")


@router.post(
    "/api/v1/designer/studies/{study_id}/commit-usdm",
    response_model=CommitUSDMResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_permission("study_design:create"))],
)
async def commit_usdm_to_study(
    study_id: str,
    payload: CommitUSDMRequest,
    request: Request,
    driver: Any = Depends(get_neo4j_driver),
) -> CommitUSDMResponse:
    """Commits extracted USDM entities to the Neo4j graph and synthesizes CDASH eCRF forms.

    Guarantees 21 CFR Part 11 audit attribution and GxP change justification.
    """
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "system"
    )

    if not payload.change_reason or not payload.change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason.",
        )

    # 1. Commit to Neo4j Graph
    graph_res = await commit_usdm_graph(driver, study_id, payload.data, user_id)

    # 2. Synthesize CDASH eCRFs & Rules
    synthesized_forms = synthesize_ecrf_forms(payload.data)

    version_id = f"{study_id}_v1"

    return CommitUSDMResponse(
        study_id=study_id,
        version_id=version_id,
        status="COMMITTED",
        nodes_created=graph_res.get("nodes_created", 0),
        relationships_created=graph_res.get("relationships_created", 0),
        synthesized_forms=synthesized_forms,
        message=f"Successfully synthesized {len(synthesized_forms)} eCRF forms and committed USDM graph.",
    )


# =============================================================================
# ASYNCHRONOUS PROTOCOL DIGITIZATION STAGE DAG ENDPOINTS
# =============================================================================


@router.post(
    "/api/v1/designer/digitization/dag/jobs",
    response_model=StartDAGJobResponse,
    status_code=status.HTTP_202_ACCEPTED,
    dependencies=[Depends(require_permission("protocol_ingestion:upload"))],
)
async def start_digitization_dag_job(
    background_tasks: BackgroundTasks,
    request: Request,
    file: UploadFile = File(...),
    study_id: str | None = Form(None),
    dag_runner: DigitizationDAGRunner = Depends(get_digitization_dag_runner),
) -> StartDAGJobResponse:
    """Starts an asynchronous multi-stage DAG protocol digitization job.

    Decomposes ingestion into checkpointed transformations (layout parsing, SoA extraction,
    biomedical concept mapping, eCRF synthesis, and USDM compilation) with schema validation gates.
    """
    filename = file.filename or "protocol.pdf"
    content = await file.read()

    if not content or len(content) == 0:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Uploaded protocol document is empty or corrupted.",
        )

    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "system"
    )

    try:
        job = await dag_runner.initialize_job(
            file_content=content,
            filename=filename,
            study_id=study_id,
            user_id=user_id,
        )
        background_tasks.add_task(dag_runner.run_job, job.job_id)
        return StartDAGJobResponse(
            job_id=job.job_id,
            status=job.status,
            current_stage=job.current_stage,
            message="Protocol digitization DAG job scheduled successfully.",
        )
    except Exception as exc:
        logger.error("Failed to initialize DAG digitization job: %s", exc)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Job initialization failed: {str(exc)}",
        ) from exc


@router.get(
    "/api/v1/designer/digitization/dag/jobs/{job_id}",
    response_model=DAGJobStatusResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("protocol_ingestion:read"))],
)
async def get_digitization_dag_job_status(
    job_id: str,
    job_store: DigitizationJobStore = Depends(get_digitization_job_store),
) -> DAGJobStatusResponse:
    """Retrieves real-time execution status, stage checkpoints, and progress for a DAG job."""
    job = await job_store.get_job(job_id)
    if not job:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Digitization job '{job_id}' not found.",
        )

    progress = 0
    if job.status == DigitizationJobStatus.COMPLETED:
        progress = 100
    elif job.current_stage:
        progress = STAGE_PROGRESS_MAP.get(job.current_stage, 0)

    is_term = job.status in (
        DigitizationJobStatus.COMPLETED,
        DigitizationJobStatus.FAILED,
        DigitizationJobStatus.CANCELLED,
    )

    return DAGJobStatusResponse(
        job_id=job.job_id,
        study_id=job.study_id,
        status=job.status,
        current_stage=job.current_stage,
        progress_pct=progress,
        created_at=job.created_at,
        updated_at=job.updated_at,
        checkpoints=job.checkpoints,
        error_message=job.error_message,
        is_terminal=is_term,
        final_usdm_payload=job.final_usdm_payload,
        synthesized_forms=job.synthesized_forms,
    )


@router.post(
    "/api/v1/designer/digitization/dag/jobs/{job_id}/resume",
    response_model=DAGJobStatusResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("protocol_ingestion:upload"))],
)
async def resume_digitization_dag_job(
    job_id: str,
    background_tasks: BackgroundTasks,
    payload: ResumeDAGJobRequest | None = None,
    job_store: DigitizationJobStore = Depends(get_digitization_job_store),
    dag_runner: DigitizationDAGRunner = Depends(get_digitization_dag_runner),
) -> DAGJobStatusResponse:
    """Resumes execution of a paused or failed DAG digitization job from a specified checkpoint."""
    job = await job_store.get_job(job_id)
    if not job:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Digitization job '{job_id}' not found.",
        )

    start_stage = payload.from_stage if payload else None
    background_tasks.add_task(dag_runner.run_job, job_id, start_stage)

    job.status = DigitizationJobStatus.RUNNING
    await job_store.update_job(job)

    progress = STAGE_PROGRESS_MAP.get(job.current_stage, 0) if job.current_stage else 0
    return DAGJobStatusResponse(
        job_id=job.job_id,
        study_id=job.study_id,
        status=job.status,
        current_stage=job.current_stage,
        progress_pct=progress,
        created_at=job.created_at,
        updated_at=job.updated_at,
        checkpoints=job.checkpoints,
        error_message=job.error_message,
        is_terminal=False,
        final_usdm_payload=job.final_usdm_payload,
        synthesized_forms=job.synthesized_forms,
    )


@router.post(
    "/api/v1/designer/digitization/dag/jobs/{job_id}/compile-usdm",
    response_model=CommitUSDMResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_permission("study_design:create"))],
)
async def compile_usdm_from_dag_job(
    job_id: str,
    payload: CompileUSDMFromJobRequest,
    request: Request,
    job_store: DigitizationJobStore = Depends(get_digitization_job_store),
    driver: Any = Depends(get_neo4j_driver),
) -> CommitUSDMResponse:
    """Commits the finalized USDM model generated by a completed DAG job into Neo4j with Part 11 audit attribution."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "system"
    )

    if not payload.change_reason or not payload.change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason.",
        )

    job = await job_store.get_job(job_id)
    if not job:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Digitization job '{job_id}' not found.",
        )

    if job.status != DigitizationJobStatus.COMPLETED or not job.final_usdm_payload:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Cannot compile USDM from job '{job_id}' because it has not completed successfully (current status: {job.status.value}).",
        )

    # 1. Commit to Neo4j Graph
    graph_res = await commit_usdm_graph(
        driver, payload.study_id, job.final_usdm_payload, user_id
    )

    # 2. Synthesize CDASH eCRFs & Rules
    synthesized_forms = synthesize_ecrf_forms(job.final_usdm_payload)
    version_id = f"{payload.study_id}_v1"

    return CommitUSDMResponse(
        study_id=payload.study_id,
        version_id=version_id,
        status="COMMITTED",
        nodes_created=graph_res.get("nodes_created", 0),
        relationships_created=graph_res.get("relationships_created", 0),
        synthesized_forms=synthesized_forms,
        message=f"Successfully compiled and committed USDM graph for study '{payload.study_id}' with {len(synthesized_forms)} eCRF forms.",
    )
