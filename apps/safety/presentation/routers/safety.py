"""FastAPI Router for Safety microservice."""

import copy
import logging
import os
import uuid
from typing import Any

from fastapi import APIRouter, BackgroundTasks, Depends, HTTPException, Request
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.infrastructure.database import db_manager
from apps.safety.infrastructure.models import (
    ExportJob,
    SAEDiscrepancy,
    SAEReconciliationJob,
    SAEReconciliationRun,
    SafetyAuditLog,
    SafetyCaseICSR,
    write_audit_log,
)
from apps.safety.presentation.dtos import (
    ICSRDataExportRequest,
    SAEDiscrepancyResponse,
    SAEReconciliationJobRequest,
    SAEReconciliationJobResponse,
    SAEReconciliationRunRequest,
    SAEReconciliationRunResponse,
    SafetyAuditLogResponse,
    SafetyCaseICSRCreate,
    SafetyCaseICSRResponse,
    SafetyExportJobCreate,
    SafetyExportJobResponse,
)
from apps.safety.processor import process_sae_reconciliation
from packages.database import DatabaseSessionDependency

router = APIRouter()
get_db_session = DatabaseSessionDependency(db_manager)
logger = logging.getLogger("safety-router")


async def send_medical_monitor_alert(
    job_id: str,
    run_id: str,
    study_id: str,
    discrepancy_count: int,
    test_client: Any | None,
    session: AsyncSession,
    user_id: str,
    change_reason: str,
) -> None:
    import os
    import time

    import httpx

    from packages.security.signing import generate_gateway_signature

    gateway_secret_env = os.getenv("GATEWAY_SECRET")
    if not gateway_secret_env:
        raise RuntimeError(
            "GATEWAY_SECRET environment variable is not set. "
            "Refusing to sign internal requests with a default/empty secret."
        )
    gateway_secret = gateway_secret_env.encode("utf-8")

    caller_user_id = "safety-service"
    roles = "sponsor_statistician"
    timestamp = str(time.time())

    signature = generate_gateway_signature(
        user_id=caller_user_id,
        roles=roles,
        timestamp=timestamp,
        secret=gateway_secret,
        change_reason=change_reason,
    )

    headers = {
        "X-User-Id": caller_user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    payload = {
        "recipient_role": "sponsor_mm",
        "category": "ALERTS",
        "priority": "HIGH",
        "channels": "IN_APP",
        "message_content": f"SAE reconciliation run {run_id} identified {discrepancy_count} discrepancies for study {study_id}.",
        "related_entity_id": run_id,
        "related_entity_type": "SAEReconciliationRun",
    }

    notifications_url = os.getenv("NOTIFICATIONS_URL") or "http://localhost:8006"
    url = f"{notifications_url.rstrip('/')}/api/v1/notifications"

    try:
        if test_client is not None:
            response = await test_client.post(
                url, json=payload, headers=headers, timeout=10.0
            )
        else:
            async with httpx.AsyncClient(timeout=10.0) as cli:
                response = await cli.post(url, json=payload, headers=headers)

        if response.status_code == 201:
            logger.info("Successfully dispatched alert to Sponsor Medical Monitor.")
            await write_safety_audit_log(
                session=session,
                user_id=user_id,
                action="RECONCILIATION_ALERT_SENT",
                details=f"Sponsor Medical Monitor alert successfully dispatched for run {run_id}. Identified {discrepancy_count} discrepancies.",
                record_id=job_id,
                change_reason=change_reason,
            )
        else:
            logger.error(
                f"Notifications service returned error {response.status_code}: {response.text}"
            )
            await write_safety_audit_log(
                session=session,
                user_id=user_id,
                action="RECONCILIATION_ALERT_FAILED",
                details=f"Sponsor Medical Monitor alert dispatch failed with status {response.status_code}.",
                record_id=job_id,
                change_reason=change_reason,
            )
    except Exception as e:
        logger.exception("Failed to dispatch Sponsor Medical Monitor alert")
        await write_safety_audit_log(
            session=session,
            user_id=user_id,
            action="RECONCILIATION_ALERT_FAILED",
            details=f"Sponsor Medical Monitor alert dispatch exception: {str(e)[:200]}.",
            record_id=job_id,
            change_reason=change_reason,
        )


async def reconciliation_worker(
    job_id: str,
    study_id: str,
    user_id: str,
    change_reason: str,
    test_client: Any | None = None,
) -> None:
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        try:
            stmt = select(SAEReconciliationJob).where(SAEReconciliationJob.id == job_id)
            result = await session.execute(stmt)
            job = result.scalars().first()
            if not job:
                logger.error(f"Reconciliation job {job_id} not found in database.")
                return

            job.status = "PROCESSING"
            await session.flush()

            await write_safety_audit_log(
                session=session,
                user_id=user_id,
                action="RECONCILIATION_JOB_PROCESSING",
                details=f"SAE reconciliation job {job_id} status changed to PROCESSING.",
                record_id=job_id,
                change_reason=change_reason,
            )
            await session.commit()

            from apps.safety.reconciliation import run_reconciliation

            results = await run_reconciliation(
                study_id=study_id,
                session=session,
                created_by=user_id,
                reason_for_change=change_reason,
                client=test_client,
            )

            run = results["run"]
            discrepancies = results["discrepancies"]

            stmt = select(SAEReconciliationJob).where(SAEReconciliationJob.id == job_id)
            result = await session.execute(stmt)
            job = result.scalars().first()
            if job:
                job.status = "COMPLETED"
                job.run_id = run.id
                await session.flush()

                await write_safety_audit_log(
                    session=session,
                    user_id=user_id,
                    action="RECONCILIATION_JOB_COMPLETED",
                    details=f"SAE reconciliation job {job_id} status changed to COMPLETED. Created run {run.id}.",
                    record_id=job_id,
                    change_reason=change_reason,
                )
                await session.commit()

                if len(discrepancies) > 0:
                    await send_medical_monitor_alert(
                        job_id=job_id,
                        run_id=run.id,
                        study_id=study_id,
                        discrepancy_count=len(discrepancies),
                        test_client=test_client,
                        session=session,
                        user_id=user_id,
                        change_reason=change_reason,
                    )
                    await session.commit()

        except Exception as e:
            logger.exception(f"Error processing reconciliation job {job_id}")
            try:
                await session.rollback()
                stmt = select(SAEReconciliationJob).where(
                    SAEReconciliationJob.id == job_id
                )
                result = await session.execute(stmt)
                job = result.scalars().first()
                if job:
                    job.status = "FAILED"
                    err_msg = str(type(e).__name__)
                    await session.flush()

                    await write_safety_audit_log(
                        session=session,
                        user_id=user_id,
                        action="RECONCILIATION_JOB_FAILED",
                        details=f"SAE reconciliation job {job_id} status changed to FAILED. Error type: {err_msg}",
                        record_id=job_id,
                        change_reason=change_reason,
                    )
                    await session.commit()
            except Exception as inner_e:
                logger.error(
                    f"Failed to record FAILED status for job {job_id}: {inner_e}"
                )


def get_user_context(request: Request):
    """Helper to extract user identity headers parsed by GatewayAuthMiddleware."""
    user_id = getattr(request.state, "user_id", "system")
    user_role = request.headers.get("X-User-Roles", "system")
    change_reason = getattr(
        request.state, "change_reason", None
    ) or request.headers.get("X-Change-Reason")
    return user_id, user_role, change_reason


async def write_safety_audit_log(
    session: AsyncSession,
    user_id: str,
    action: str,
    details: str,
    record_id: str | None = None,
    change_reason: str | None = None,
    version_index: int = 1,
) -> None:
    """Utility function to write to the immutable Safety audit ledger."""
    await write_audit_log(
        session=session,
        created_by=user_id,
        action=action,
        details=details,
        reason_for_change=change_reason,
        version_index=version_index,
        record_id=record_id,
    )


def map_case_to_response(case: SafetyCaseICSR) -> SafetyCaseICSRResponse:
    return SafetyCaseICSRResponse(
        id=case.id,
        worldwide_unique_case_id=case.worldwide_unique_case_id,
        patient_id=case.patient_id,
        case_data=case.case_data,
        created_at=case.created_at.isoformat(),
        created_by=case.created_by,
        reason_for_change=case.reason_for_change,
        version_index=case.version_index,
    )


def map_job_to_response(job: ExportJob) -> SafetyExportJobResponse:
    return SafetyExportJobResponse(
        id=job.id,
        job_name=job.job_name,
        status=job.status,
        output=job.output,
        error=job.error,
        error_message=job.error,
        created_at=job.created_at.isoformat(),
        created_by=job.created_by,
        reason_for_change=job.reason_for_change,
        version_index=job.version_index,
    )


def map_run_to_response(
    run: SAEReconciliationRun, discrepancies: list[SAEDiscrepancy]
) -> SAEReconciliationRunResponse:
    return SAEReconciliationRunResponse(
        id=run.id,
        study_id=run.study_id,
        run_date=run.run_date.isoformat(),
        created_at=run.created_at.isoformat(),
        created_by=run.created_by,
        reason_for_change=run.reason_for_change,
        version_index=run.version_index,
        discrepancies=[
            SAEDiscrepancyResponse(
                id=d.id,
                run_id=d.run_id,
                source=d.source,
                case_event_key=d.case_event_key,
                field_name=d.field_name,
                expected_value=d.expected_value,
                actual_value=d.actual_value,
                meddra_version=d.meddra_version,
                created_at=d.created_at.isoformat(),
                created_by=d.created_by,
                reason_for_change=d.reason_for_change,
                version_index=d.version_index,
            )
            for d in discrepancies
        ],
    )


def map_job_to_reconciliation_response(
    job: SAEReconciliationJob, result_summary: dict[str, Any] | None = None
) -> SAEReconciliationJobResponse:
    return SAEReconciliationJobResponse(
        id=job.id,
        study_id=job.study_id,
        status=job.status,
        error_message=job.error_message,
        run_id=job.run_id,
        result_summary=result_summary,
        created_at=job.created_at.isoformat(),
        created_by=job.created_by,
        reason_for_change=job.reason_for_change,
        version_index=job.version_index,
    )


async def get_job_result_summary(
    session: AsyncSession, run_id: str | None
) -> dict[str, Any] | None:
    if not run_id:
        return None
    try:
        stmt_run = select(SAEReconciliationRun).where(SAEReconciliationRun.id == run_id)
        res_run = await session.execute(stmt_run)
        run = res_run.scalars().first()
        if not run:
            return None

        stmt_disc_count = select(func.count(SAEDiscrepancy.id)).where(
            SAEDiscrepancy.run_id == run_id
        )
        res_disc_count = await session.execute(stmt_disc_count)
        count = res_disc_count.scalar() or 0

        return {
            "discrepancy_count": count,
            "run_id": run_id,
            "study_id": run.study_id,
            "run_date": run.run_date.isoformat(),
        }
    except Exception as e:
        logger.error(f"Failed to build result summary for run {run_id}: {e}")
        return None


@router.post(
    "/api/v1/safety/cases", response_model=SafetyCaseICSRResponse, status_code=201
)
async def create_safety_case(
    request: Request,
    payload: SafetyCaseICSRCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyCaseICSRResponse:
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    case = SafetyCaseICSR(
        worldwide_unique_case_id=payload.worldwide_unique_case_id,
        patient_id=payload.patient_id,
        case_data=payload.case_data,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(case)
    await session.flush()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_CASE_CREATE",
        details=f"Created safety case '{payload.worldwide_unique_case_id}' for patient '{payload.patient_id}'.",
        record_id=case.id,
        change_reason=change_reason,
        version_index=1,
    )

    return map_case_to_response(case)


@router.get("/api/v1/safety/cases", response_model=list[SafetyCaseICSRResponse])
async def list_safety_cases(
    request: Request,
    patient_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[SafetyCaseICSRResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SafetyCaseICSR)
    if patient_id:
        stmt = stmt.where(SafetyCaseICSR.patient_id == patient_id)

    result = await session.execute(stmt)
    cases = result.scalars().all()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_CASE_LIST",
        details=f"Listed safety cases (patient_id filter: {patient_id}).",
        change_reason=change_reason,
        version_index=1,
    )

    return [map_case_to_response(c) for c in cases]


@router.get("/api/v1/safety/cases/{id}", response_model=SafetyCaseICSRResponse)
async def get_safety_case(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyCaseICSRResponse:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SafetyCaseICSR).where(SafetyCaseICSR.id == id)
    result = await session.execute(stmt)
    case = result.scalars().first()

    if not case:
        raise HTTPException(
            status_code=404, detail=f"Safety case with ID '{id}' not found."
        )

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_CASE_VIEW",
        details=f"Viewed safety case ID: {id}.",
        record_id=id,
        change_reason=change_reason,
        version_index=case.version_index,
    )

    return map_case_to_response(case)


@router.post(
    "/api/v1/safety/export-jobs",
    response_model=SafetyExportJobResponse,
    status_code=201,
)
async def create_export_job(
    request: Request,
    payload: SafetyExportJobCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    job = ExportJob(
        job_name=payload.job_name,
        status="PENDING",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(job)
    await session.flush()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_EXPORT_JOB_CREATE",
        details=f"Created export job '{payload.job_name}' with status PENDING.",
        record_id=job.id,
        change_reason=change_reason,
        version_index=1,
    )

    return map_job_to_response(job)


@router.get("/api/v1/safety/export-jobs", response_model=list[SafetyExportJobResponse])
async def list_export_jobs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> list[SafetyExportJobResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(ExportJob)
    result = await session.execute(stmt)
    jobs = result.scalars().all()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_EXPORT_JOB_LIST",
        details="Listed all safety export jobs.",
        change_reason=change_reason,
        version_index=1,
    )

    return [map_job_to_response(j) for j in jobs]


@router.get("/api/v1/safety/export-jobs/{id}", response_model=SafetyExportJobResponse)
async def get_export_job(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(ExportJob).where(ExportJob.id == id)
    result = await session.execute(stmt)
    job = result.scalars().first()

    if not job:
        raise HTTPException(
            status_code=404, detail=f"Safety export job with ID '{id}' not found."
        )

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_EXPORT_JOB_VIEW",
        details=f"Viewed safety export job ID: {id}.",
        record_id=id,
        change_reason=change_reason,
        version_index=job.version_index,
    )

    return map_job_to_response(job)


@router.get("/api/v1/safety/audit-logs", response_model=list[SafetyAuditLogResponse])
async def list_safety_audit_logs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> list[SafetyAuditLogResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_AUDIT_LOG_LIST",
        details="Listed safety audit logs.",
        change_reason=change_reason,
        version_index=1,
    )

    stmt = select(SafetyAuditLog).order_by(SafetyAuditLog.created_at.desc())
    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
        SafetyAuditLogResponse(
            id=log.id,
            created_at=log.created_at.isoformat(),
            created_by=log.created_by,
            reason_for_change=log.reason_for_change,
            version_index=log.version_index,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
        )
        for log in logs
    ]


@router.post(
    "/api/v1/safety/export",
    response_model=SafetyExportJobResponse,
    status_code=201,
)
async def export_safety_case(
    request: Request,
    payload: ICSRDataExportRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    from apps.safety.renderer import generate_e2b_xml

    try:
        _ = generate_e2b_xml(payload.icsr)
    except ValueError as e:
        raise HTTPException(
            status_code=422,
            detail=f"Structural validation failure: {str(e)}",
        )
    except Exception as e:
        raise HTTPException(
            status_code=422,
            detail=f"XML rendering failed: {str(e)}",
        )

    job = ExportJob(
        job_name=payload.job_name,
        status="PENDING",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(job)
    await session.flush()

    from packages.deid.transforms import pseudonymize_value

    salt = os.getenv("SAFETY_SALT", default="internal-safety-salt-12345")
    icsr_copy = copy.deepcopy(payload.icsr)

    raw_patient_id = icsr_copy.patient.patient_id
    pseudonymized_patient_id = pseudonymize_value(raw_patient_id, salt)

    icsr_copy.patient.patient_id = pseudonymized_patient_id
    icsr_copy.patient.birth_date = None

    pseudonymized_xml = generate_e2b_xml(icsr_copy)

    from apps.safety.adapters import SafetyDatabaseAdapter

    client = getattr(request.app.state, "test_httpx_client", None)
    adapter = SafetyDatabaseAdapter(client=client)

    try:
        response = await adapter.transmit(pseudonymized_xml)
        if 200 <= response.status_code < 300:
            job.status = "COMPLETED"
            job.output = pseudonymized_xml
        else:
            job.status = "FAILED"
            job.error = f"Transmission failed with status {response.status_code}: {response.text}"
    except Exception as e:
        job.status = "FAILED"
        job.error = f"Transmission exception: {str(e)}"

    await session.flush()

    audit_action = (
        "SAFETY_EXPORT_JOB_COMPLETE"
        if job.status == "COMPLETED"
        else "SAFETY_EXPORT_JOB_FAIL"
    )
    audit_details = (
        f"Export job '{payload.job_name}' completed. Patient pseudonymized: {pseudonymized_patient_id}."
        if job.status == "COMPLETED"
        else f"Export job '{payload.job_name}' failed. Patient pseudonymized: {pseudonymized_patient_id}. Error: {job.error_message}"
    )

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action=audit_action,
        details=audit_details,
        record_id=job.id,
        change_reason=change_reason,
        version_index=1,
    )

    return map_job_to_response(job)


@router.post(
    "/api/v1/safety/reconciliation/runs",
    response_model=SAEReconciliationRunResponse,
    status_code=201,
)
async def trigger_sae_reconciliation(
    request: Request,
    payload: SAEReconciliationRunRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SAEReconciliationRunResponse:
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    from apps.safety.reconciliation import run_reconciliation

    test_client = getattr(request.app.state, "test_httpx_client", None)

    try:
        results = await run_reconciliation(
            study_id=payload.study_id,
            session=session,
            created_by=user_id,
            reason_for_change=change_reason,
            client=test_client,
        )
    except Exception as e:
        logger.exception("SAE Reconciliation orchestrator failed: %s", e)
        raise HTTPException(
            status_code=502,
            detail=f"SAE Reconciliation execution failure: {str(e)}",
        )

    run = results["run"]
    discrepancies = results["discrepancies"]

    await session.commit()

    audit_details = f"Executed SAE reconciliation run ID '{run.id}' for study '{payload.study_id}'. Identified {len(discrepancies)} discrepancies."
    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAE_RECONCILIATION_RUN",
        details=audit_details,
        record_id=run.id,
        change_reason=change_reason,
        version_index=1,
    )
    await session.commit()

    return map_run_to_response(run, discrepancies)


@router.post(
    "/api/v1/safety/reconciliation/jobs",
    response_model=SAEReconciliationJobResponse,
    status_code=202,
)
async def trigger_sae_reconciliation_job(
    request: Request,
    payload: SAEReconciliationJobRequest,
    background_tasks: BackgroundTasks,
    session: AsyncSession = Depends(get_db_session),
) -> SAEReconciliationJobResponse:
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    allowed_roles = {"sponsor_medical_monitor", "safety_reviewer"}
    roles_present = {r.strip() for r in user_role.split(",") if r.strip()}
    if not roles_present.intersection(allowed_roles):
        raise HTTPException(
            status_code=403,
            detail="Insufficient role: sponsor_medical_monitor or safety_reviewer required",
        )

    job_id = str(uuid.uuid4())

    job = SAEReconciliationJob(
        id=job_id,
        study_id=payload.study_id,
        status="PENDING",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(job)
    await session.flush()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="RECONCILIATION_JOB_CREATE",
        details=f"Created SAE reconciliation job {job_id} with status PENDING.",
        record_id=job_id,
        change_reason=change_reason,
        version_index=1,
    )
    await session.commit()

    test_client = getattr(request.app.state, "test_httpx_client", None)
    background_tasks.add_task(
        process_sae_reconciliation,
        job_id=job_id,
        study_id=payload.study_id,
        user_id=user_id,
        change_reason=change_reason,
        test_client=test_client,
    )

    return map_job_to_reconciliation_response(job, None)


@router.get(
    "/api/v1/safety/reconciliation/jobs",
    response_model=list[SAEReconciliationJobResponse],
)
async def list_reconciliation_jobs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
    limit: int = 50,
    offset: int = 0,
) -> list[SAEReconciliationJobResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = (
        select(SAEReconciliationJob)
        .order_by(SAEReconciliationJob.created_at.desc())
        .limit(limit)
        .offset(offset)
    )
    result = await session.execute(stmt)
    jobs = result.scalars().all()

    response_jobs = []
    for j in jobs:
        summary = await get_job_result_summary(session, j.run_id)
        response_jobs.append(map_job_to_reconciliation_response(j, summary))

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="RECONCILIATION_JOB_LIST",
        details="Listed SAE reconciliation jobs.",
        change_reason=change_reason,
        version_index=1,
    )
    await session.commit()

    return response_jobs


@router.get(
    "/api/v1/safety/reconciliation/jobs/{job_id}",
    response_model=SAEReconciliationJobResponse,
)
async def get_reconciliation_job(
    request: Request,
    job_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SAEReconciliationJobResponse:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SAEReconciliationJob).where(SAEReconciliationJob.id == job_id)
    result = await session.execute(stmt)
    job = result.scalars().first()

    if not job:
        raise HTTPException(
            status_code=404, detail=f"Reconciliation job with ID '{job_id}' not found."
        )

    summary = await get_job_result_summary(session, job.run_id)

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="RECONCILIATION_JOB_VIEW",
        details=f"Viewed reconciliation job ID: {job_id}.",
        record_id=job_id,
        change_reason=change_reason,
        version_index=job.version_index,
    )
    await session.commit()

    return map_job_to_reconciliation_response(job, summary)


@router.get(
    "/api/v1/safety/reconciliation/runs",
    response_model=list[SAEReconciliationRunResponse],
)
async def list_reconciliation_runs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> list[SAEReconciliationRunResponse]:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SAEReconciliationRun)
    result = await session.execute(stmt)
    runs = result.scalars().all()

    response_runs = []
    for r in runs:
        stmt_disc = select(SAEDiscrepancy).where(SAEDiscrepancy.run_id == r.id)
        res_disc = await session.execute(stmt_disc)
        discrepancies = list(res_disc.scalars().all())
        response_runs.append(map_run_to_response(r, discrepancies))

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAE_RECONCILIATION_RUN_LIST",
        details="Listed SAE reconciliation runs.",
        change_reason=change_reason,
        version_index=1,
    )
    await session.commit()

    return response_runs


@router.get(
    "/api/v1/safety/reconciliation/runs/{id}",
    response_model=SAEReconciliationRunResponse,
)
async def get_reconciliation_run(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SAEReconciliationRunResponse:
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SAEReconciliationRun).where(SAEReconciliationRun.id == id)
    result = await session.execute(stmt)
    run = result.scalars().first()

    if not run:
        raise HTTPException(
            status_code=404, detail=f"Reconciliation run with ID '{id}' not found."
        )

    stmt_disc = select(SAEDiscrepancy).where(SAEDiscrepancy.run_id == id)
    res_disc = await session.execute(stmt_disc)
    discrepancies = list(res_disc.scalars().all())

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAE_RECONCILIATION_RUN_VIEW",
        details=f"Viewed reconciliation run ID: {id}.",
        record_id=id,
        change_reason=change_reason,
        version_index=run.version_index,
    )
    await session.commit()

    return map_run_to_response(run, discrepancies)
