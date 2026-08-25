"""
FastAPI presentation router for Autonomous Agentic Multi-Criteria Cohort Reasoning & Synthetic Feasibility Engine.

Provides endpoints for asynchronous feasibility job execution, job status polling,
and synthetic patient population simulation under missing data conditions.

Requirements: PRD-SYS-051, PRD-SYS-001
"""

from __future__ import annotations

import logging
import uuid

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, status

from apps.designer.application.services.cohort_feasibility_engine import (
    CohortFeasibilityEngine,
)
from apps.designer.domain.cohort_feasibility_models import (
    CohortFeasibilityJob,
    CohortFeasibilityReport,
    CohortFeasibilityRequest,
)
from apps.designer.workers.cohort_feasibility_worker import (
    CohortFeasibilityWorker,
)
from packages.security.rbac import (
    Principal,
    get_principal,
    mask_payload,
    require_permission,
)

logger = logging.getLogger("cohort-feasibility-router")

router = APIRouter(
    prefix="/api/v1/designer/cohort-feasibility",
    tags=["Cohort Feasibility Engine"],
)


async def run_feasibility_background_task(
    job_id: str, request: CohortFeasibilityRequest
) -> None:
    """Background task runner for asynchronous cohort feasibility analysis.

    Args:
        job_id: Registered job ID.
        request: Inbound CohortFeasibilityRequest.
    """
    CohortFeasibilityWorker.update_job_status(job_id=job_id, status="RUNNING")
    engine = CohortFeasibilityEngine()

    try:
        report = await engine.execute_feasibility_analysis(request)
        status_to_set = (
            "COMPLETED"
            if report.status in ("COMPLETED", "DUPLICATE_LOCKED")
            else "FAILED"
        )
        if report.status == "DUPLICATE_LOCKED":
            status_to_set = "DUPLICATE_LOCKED"
        CohortFeasibilityWorker.update_job_status(
            job_id=job_id, status=status_to_set, report=report
        )
    except Exception as exc:
        logger.error(
            "Error executing background cohort feasibility job %s: %s",
            job_id,
            exc,
            exc_info=True,
        )
        err_report = CohortFeasibilityReport(
            job_id=job_id,
            study_id=request.study_id,
            cohort_id=request.cohort_id,
            status="FAILED",
            starting_cohort_size=0,
            eligible_count=0,
            ineligible_count=0,
            indeterminate_count=0,
            overall_eligibility_rate=0.0,
            estimated_fit_percentage=0.0,
            error_message=str(exc),
        )
        CohortFeasibilityWorker.update_job_status(
            job_id=job_id, status="FAILED", report=err_report
        )


@router.post(
    "/evaluate",
    response_model=CohortFeasibilityReport | CohortFeasibilityJob,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("study_design:read"))],
    summary="Trigger multi-criteria cohort reasoning and synthetic feasibility simulation",
)
async def evaluate_cohort_feasibility_endpoint(
    payload: CohortFeasibilityRequest,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
) -> CohortFeasibilityReport | CohortFeasibilityJob:
    """Executes multi-criteria cohort reasoning and synthetic subject profile feasibility simulation.

    Supports:
    - Asynchronous background worker execution guarded by PostgreSQL advisory locks (non-blocking)
    - De-identification scrubbing on evaluation context and prompts (packages/deid)
    - Grounded RAG protocol criteria matching (packages/compliance)
    - Three-valued logic (3VL) evaluation under complete, partial, and missing data scenarios
    - Granular node-level fit trace explanations

    Args:
        payload: Inbound CohortFeasibilityRequest payload.
        background_tasks: FastAPI BackgroundTasks queue.
        principal: Authenticated principal security context.

    Returns:
        CohortFeasibilityJob when asynchronous background processing is requested,
        or CohortFeasibilityReport when executed synchronously.
    """
    job_id = payload.analysis_id or f"feas_{uuid.uuid4().hex[:12]}"
    payload.analysis_id = job_id

    if payload.is_async:
        # Register job and dispatch to background task without blocking API call
        job = CohortFeasibilityWorker.register_job(
            job_id=job_id, study_id=payload.study_id, cohort_id=payload.cohort_id
        )
        background_tasks.add_task(run_feasibility_background_task, job_id, payload)
        return mask_payload(job, principal)

    # Synchronous execution
    engine = CohortFeasibilityEngine()
    report = await engine.execute_feasibility_analysis(payload)
    return mask_payload(report, principal)


@router.get(
    "/jobs/{job_id}",
    response_model=CohortFeasibilityJob,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("study_design:read"))],
    summary="Poll status and results of an asynchronous cohort feasibility job",
)
async def get_feasibility_job_status_endpoint(
    job_id: str,
    principal: Principal = Depends(get_principal),
) -> CohortFeasibilityJob:
    """Retrieves asynchronous cohort feasibility job status and report.

    Args:
        job_id: Unique job identifier to query.
        principal: Authenticated principal security context.

    Returns:
        CohortFeasibilityJob status wrapper and report.

    Raises:
        HTTPException: If the job with job_id is not found (404).
    """
    job = CohortFeasibilityWorker.get_job(job_id)
    if not job:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Feasibility job '{job_id}' not found.",
        )
    return mask_payload(job, principal)


@router.post(
    "/synthetic-simulate",
    response_model=CohortFeasibilityReport,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("study_design:read"))],
    summary="Simulate cohort feasibility under missing data scenarios using 3-valued logic",
)
async def simulate_synthetic_feasibility_endpoint(
    payload: CohortFeasibilityRequest,
    principal: Principal = Depends(get_principal),
) -> CohortFeasibilityReport:
    """Directly executes synthetic patient population simulation under missing data conditions.

    Args:
        payload: Inbound CohortFeasibilityRequest parameters.
        principal: Authenticated principal security context.

    Returns:
        CohortFeasibilityReport containing simulation results.
    """
    payload.is_async = False
    engine = CohortFeasibilityEngine()
    report = await engine.execute_feasibility_analysis(payload)
    return mask_payload(report, principal)


__all__ = [
    "evaluate_cohort_feasibility_endpoint",
    "get_feasibility_job_status_endpoint",
    "router",
    "run_feasibility_background_task",
    "simulate_synthetic_feasibility_endpoint",
]
