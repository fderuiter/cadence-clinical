"""FastAPI Router for Safety microservice."""

import copy
import json
import logging
import os
import uuid
from typing import Any

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    HTTPException,
    Request,
    Response,
)
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.safety.domain.as2_models import (
    AS2Authority,
    AS2OutboundTransmissionInput,
    TransmissionStatus,
)
from apps.safety.domain.expedited_rules import (
    ClockAlertLevel,
    ICSRRuleEvaluationInput,
    ICSRRuleEvaluationSummary,
    SignICSRRuleEvaluationRequest,
    SignICSRRuleEvaluationResponse,
)
from apps.safety.domain.narrative_models import (
    NarrativeGenerateRequest,
    NarrativeSignRequest,
    NarrativeSignResponse,
    SafetyNarrativeDTO,
)
from apps.safety.infrastructure.database import db_manager
from apps.safety.infrastructure.models import (
    AS2Transmission,
    DeviceSafetyInvestigation,
    ExportJob,
    PeriodicSafetyReportRecord,
    SAEDiscrepancy,
    SAEReconciliationJob,
    SAEReconciliationRun,
    SafetyAuditLog,
    SafetyCaseICSR,
    SafetySignalAlert,
    SafetySignalDetectionRun,
    write_as2_audit_log,
    write_audit_log,
)
from apps.safety.presentation.dtos import (
    AS2OutboundTransmissionRequestDTO,
    AS2TransmissionAuditLogDTO,
    AS2TransmissionResponseDTO,
    DeviceSafetyInvestigationAdjudicateDTO,
    DeviceSafetyInvestigationCreateDTO,
    DeviceSafetyInvestigationResponseDTO,
    DeviceVigilanceReportResponseDTO,
    ICSRDataExportRequest,
    MDNProcessRequestDTO,
    MDNProcessResponseDTO,
    PeriodicReportCompilationRequest,
    PeriodicReportResponseDTO,
    PeriodicReportSignRequest,
    PeriodicReportSignResponse,
    RegulatoryAckProcessRequestDTO,
    RegulatoryAckProcessResponseDTO,
    RegulatorySnapshotLockRequest,
    RegulatorySnapshotLockResponse,
    RegulatorySnapshotVerifyRequest,
    RegulatorySnapshotVerifyResponse,
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
    SafetySignalAlertDTO,
    SafetySignalAlertReviewRequest,
    SafetySignalAlertSignRequest,
    SafetySignalAlertSignResponse,
    SignalDetectionScanRequest,
    SignalDetectionScanResponse,
    SubjectExposureCalculationRequest,
    SubjectExposureCalculationResponse,
)
from apps.safety.processor import process_sae_reconciliation
from apps.safety.services.as2_gateway_client import AS2GatewayClient
from apps.safety.services.device_safety_service import DeviceSafetyService
from apps.safety.services.expedited_rules_engine import (
    ExpeditedSafetyReportingRulesEngine,
)
from apps.safety.services.narrative_service import SafetyNarrativeService
from apps.safety.services.periodic_report_compiler import (
    PeriodicSafetyReportCompiler,
    SubjectExposureCalculator,
)
from apps.safety.services.regulatory_audit_ledger import (
    RegulatoryAuditLedger,
)
from apps.safety.services.signal_alert_service import SafetySignalAlertService
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


# =========================================================================
# Generative Pharmacovigilance Safety Narratives & Part 11 E-Signature (PRD-SYS-052)
# =========================================================================


@router.post(
    "/api/v1/safety/narratives/generate",
    response_model=SafetyNarrativeDTO,
    status_code=201,
)
async def generate_safety_narrative_endpoint(
    request: Request,
    payload: NarrativeGenerateRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyNarrativeDTO:
    """Draft a new FDA MedWatch 3500A / CIOMS-I Serious Adverse Event narrative via Tier 3 model.

    Requirements: PRD-SYS-051, PRD-SYS-052
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_reason = (
        payload.reason_for_change or change_reason or "Draft SAE Safety Narrative"
    )

    test_client = getattr(request.app.state, "test_httpx_client", None)
    service = SafetyNarrativeService()

    narrative = await service.generate_narrative(
        session=session,
        study_id=payload.study_id,
        subject_id=payload.subject_id,
        sae_event_key=payload.sae_event_key,
        created_by=user_id,
        reason_for_change=effective_reason,
        worldwide_unique_case_id=payload.worldwide_unique_case_id,
        additional_context=payload.additional_context,
        test_client=test_client,
    )
    await session.commit()
    return narrative


@router.get(
    "/api/v1/safety/narratives/{id}",
    response_model=SafetyNarrativeDTO,
)
async def get_safety_narrative_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyNarrativeDTO:
    """Retrieve safety narrative details, grounded claims, and review status.

    Requirements: PRD-SYS-052
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetyNarrativeService()
    narrative = await service.get_narrative(
        session=session,
        narrative_id=id,
        user_id=user_id,
        reason_for_change=change_reason,
    )
    await session.commit()
    return narrative


@router.get(
    "/api/v1/safety/narratives",
    response_model=list[SafetyNarrativeDTO],
)
async def list_safety_narratives_endpoint(
    request: Request,
    study_id: str | None = None,
    subject_id: str | None = None,
    review_status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[SafetyNarrativeDTO]:
    """List safety narratives filtered by study, subject, or review status.

    Requirements: PRD-SYS-052
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetyNarrativeService()
    narratives = await service.list_narratives(
        session=session,
        study_id=study_id,
        subject_id=subject_id,
        review_status=review_status,
    )
    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_NARRATIVE_LIST",
        details=f"Listed safety narratives (study: {study_id}, subject: {subject_id}, status: {review_status}).",
        change_reason=change_reason or "List narratives",
        version_index=1,
    )
    await session.commit()
    return narratives


@router.post(
    "/api/v1/safety/narratives/{id}/sign",
    response_model=NarrativeSignResponse,
)
async def sign_safety_narrative_endpoint(
    request: Request,
    id: str,
    payload: NarrativeSignRequest,
    session: AsyncSession = Depends(get_db_session),
) -> NarrativeSignResponse:
    """Apply a 21 CFR Part 11 cryptographic electronic signature to approve a safety narrative.

    Requirements: PRD-SYS-051, PRD-SYS-052
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_reason = payload.reason_for_change or change_reason
    if not effective_reason or not effective_reason.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is mandatory for 21 CFR Part 11 electronic signatures.",
        )

    service = SafetyNarrativeService()
    sign_response = await service.sign_narrative(
        session=session,
        narrative_id=id,
        user_id=user_id,
        user_roles=user_role,
        reason_for_change=effective_reason,
        signature_secret=payload.signature_secret,
    )
    await session.commit()
    return sign_response


@router.post(
    "/api/v1/safety/narratives/{id}/export-e2b",
)
async def export_safety_narrative_e2b_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """Export an approved safety narrative into ICH E2B(R3) XML format.

    Requires 21 CFR Part 11 approval status.

    Requirements: PRD-SYS-052
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetyNarrativeService()
    xml_content = await service.export_narrative_to_e2b_xml(
        session=session,
        narrative_id=id,
        user_id=user_id,
        reason_for_change=change_reason or "Export to ICH E2B(R3) XML",
    )
    await session.commit()
    return Response(
        content=xml_content,
        media_type="application/xml",
        headers={"Content-Disposition": f"attachment; filename=narrative_{id}.xml"},
    )


# =========================================================================
# Deterministic Multi-Country Expedited Safety Reporting Rules Engine (PRD-PVG-001, PRD-PVG-008)
# =========================================================================


@router.post(
    "/api/v1/safety/rules/evaluate",
    response_model=ICSRRuleEvaluationSummary,
    status_code=201,
)
async def evaluate_expedited_safety_rules_endpoint(
    request: Request,
    payload: ICSRRuleEvaluationInput,
    session: AsyncSession = Depends(get_db_session),
) -> ICSRRuleEvaluationSummary:
    """Evaluate adverse event against global expedited safety reporting rules and persist evaluation.

    Requirements: PRD-PVG-001, PRD-PVG-008, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_reason = (
        payload.reason_for_change or change_reason or "Evaluate Expedited Safety Rules"
    )

    engine = ExpeditedSafetyReportingRulesEngine()
    summary = await engine.evaluate_and_persist(
        session=session,
        input_data=payload,
        user_id=user_id,
        reason_for_change=effective_reason,
    )
    await session.commit()
    return summary


@router.get(
    "/api/v1/safety/rules/evaluations/{id}",
    response_model=ICSRRuleEvaluationSummary,
)
async def get_expedited_safety_evaluation_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ICSRRuleEvaluationSummary:
    """Retrieve safety reporting evaluation with updated real-time Day 0 countdown clocks.

    Requirements: PRD-PVG-001
    """
    user_id, user_role, change_reason = get_user_context(request)
    engine = ExpeditedSafetyReportingRulesEngine()
    summary = await engine.get_evaluation(
        session=session,
        evaluation_id=id,
        user_id=user_id,
        reason_for_change=change_reason,
    )
    await session.commit()
    return summary


@router.get(
    "/api/v1/safety/rules/evaluations",
    response_model=list[ICSRRuleEvaluationSummary],
)
async def list_expedited_safety_evaluations_endpoint(
    request: Request,
    study_id: str | None = None,
    case_id: str | None = None,
    subject_id: str | None = None,
    approval_status: str | None = None,
    any_expedited: bool | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ICSRRuleEvaluationSummary]:
    """List safety evaluations filtered by study, case, or expedited status with live countdowns.

    Requirements: PRD-PVG-001
    """
    user_id, user_role, change_reason = get_user_context(request)
    engine = ExpeditedSafetyReportingRulesEngine()
    evaluations = await engine.list_evaluations(
        session=session,
        study_id=study_id,
        case_id=case_id,
        subject_id=subject_id,
        approval_status=approval_status,
        any_expedited=any_expedited,
    )
    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="EXPEDITED_SAFETY_EVALUATIONS_LIST",
        details=f"Listed expedited safety evaluations (study: {study_id}, case: {case_id}, status: {approval_status}).",
        change_reason=change_reason or "List expedited safety evaluations",
        version_index=1,
    )
    await session.commit()
    return evaluations


@router.post(
    "/api/v1/safety/rules/evaluations/{id}/sign",
    response_model=SignICSRRuleEvaluationResponse,
)
async def sign_expedited_safety_evaluation_endpoint(
    request: Request,
    id: str,
    payload: SignICSRRuleEvaluationRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SignICSRRuleEvaluationResponse:
    """Apply a 21 CFR Part 11 electronic signature by a qualified Safety Physician to approve an ICSR evaluation.

    Requirements: PRD-PVG-008
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_reason = payload.reason_for_change or change_reason
    if not effective_reason or not effective_reason.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is mandatory for 21 CFR Part 11 electronic signatures.",
        )

    engine = ExpeditedSafetyReportingRulesEngine()
    response = await engine.sign_evaluation(
        session=session,
        evaluation_id=id,
        user_id=user_id,
        user_roles=user_role,
        reason_for_change=effective_reason,
        signature_secret=payload.signature_secret,
        meaning_of_signature=payload.meaning_of_signature,
    )
    await session.commit()
    return response


@router.get(
    "/api/v1/safety/rules/countdown-alerts",
    response_model=list[ICSRRuleEvaluationSummary],
)
async def get_safety_countdown_alerts_endpoint(
    request: Request,
    study_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ICSRRuleEvaluationSummary]:
    """Retrieve all active expedited safety evaluations with WARNING (>=75%), CRITICAL (>=90%), or OVERDUE countdown clocks.

    Requirements: PRD-PVG-001
    """
    user_id, user_role, change_reason = get_user_context(request)
    engine = ExpeditedSafetyReportingRulesEngine()
    evaluations = await engine.list_evaluations(
        session=session,
        study_id=study_id,
        any_expedited=True,
    )
    alerting = [
        e
        for e in evaluations
        if e.overall_alert_level
        in {
            ClockAlertLevel.WARNING,
            ClockAlertLevel.CRITICAL,
            ClockAlertLevel.OVERDUE,
        }
    ]
    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="EXPEDITED_SAFETY_COUNTDOWN_ALERTS_QUERY",
        details=f"Queried expedited safety countdown alerts (study: {study_id}, found: {len(alerting)}).",
        change_reason=change_reason or "Query countdown alerts",
        version_index=1,
    )
    await session.commit()
    return alerting


# =========================================================================
# AS2 B2B Transmission Gateway & MDN Parser Endpoints (PRD-PVG-002)
# =========================================================================


def map_transmission_to_response(tx: AS2Transmission) -> AS2TransmissionResponseDTO:
    """Helper to serialize an AS2Transmission ORM entity into a Pydantic DTO."""
    events = []
    if hasattr(tx, "audit_events") and tx.audit_events:
        for ev in tx.audit_events:
            events.append(
                AS2TransmissionAuditLogDTO(
                    id=ev.id,
                    transmission_id=ev.transmission_id,
                    action=ev.action,
                    from_status=ev.from_status,
                    to_status=ev.to_status,
                    details=ev.details,
                    created_at=ev.created_at.isoformat() if ev.created_at else "",
                    created_by=ev.created_by,
                    reason_for_change=ev.reason_for_change,
                    version_index=ev.version_index,
                )
            )

    return AS2TransmissionResponseDTO(
        id=tx.id,
        transmission_id=tx.transmission_id,
        case_id=tx.case_id,
        study_id=tx.study_id,
        worldwide_unique_case_id=tx.worldwide_unique_case_id,
        message_id=tx.message_id,
        as2_from=tx.as2_from,
        as2_to=tx.as2_to,
        target_authority=tx.target_authority,
        environment=tx.environment,
        status=tx.status,
        payload_digest_sha256=tx.payload_digest_sha256,
        mic=tx.mic,
        encryption_algorithm=tx.encryption_algorithm,
        signature_algorithm=tx.signature_algorithm,
        outbound_payload_size=tx.outbound_payload_size,
        outbound_xml=tx.outbound_xml,
        mdn_message_id=tx.mdn_message_id,
        mdn_disposition=tx.mdn_disposition,
        mdn_mic=tx.mdn_mic,
        mdn_status=tx.mdn_status,
        mdn_received_at=tx.mdn_received_at.isoformat() if tx.mdn_received_at else None,
        ack_a_status=tx.ack_a_status,
        ack_a_received_at=tx.ack_a_received_at.isoformat()
        if tx.ack_a_received_at
        else None,
        ack_b_status=tx.ack_b_status,
        ack_b_received_at=tx.ack_b_received_at.isoformat()
        if tx.ack_b_received_at
        else None,
        ack_b_errors=tx.ack_b_errors or [],
        ack_c_status=tx.ack_c_status,
        ack_c_received_at=tx.ack_c_received_at.isoformat()
        if tx.ack_c_received_at
        else None,
        ack_c_details=tx.ack_c_details or {},
        error_message=tx.error_message,
        retry_count=tx.retry_count,
        created_at=tx.created_at.isoformat() if tx.created_at else "",
        created_by=tx.created_by,
        reason_for_change=tx.reason_for_change,
        version_index=tx.version_index,
        audit_events=events,
    )


@router.post(
    "/api/v1/safety/as2/transmissions",
    response_model=AS2TransmissionResponseDTO,
    status_code=201,
)
async def send_as2_transmission_endpoint(
    request: Request,
    payload: AS2OutboundTransmissionRequestDTO,
    session: AsyncSession = Depends(get_db_session),
) -> AS2TransmissionResponseDTO:
    """Dispatch an outbound ICH E2B(R3) XML payload directly to a Regulatory Health Authority AS2 Gateway.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is mandatory for AS2 B2B regulatory transmissions.",
        )

    client = AS2GatewayClient(session=session)
    input_data = AS2OutboundTransmissionInput(
        case_id=payload.case_id,
        study_id=payload.study_id,
        icsr_xml_payload=payload.icsr_xml_payload,
        target_authority=payload.target_authority,
        sender_as2_id=payload.sender_as2_id,
        receiver_as2_id=payload.receiver_as2_id,
        environment=payload.environment,
        custom_headers=payload.custom_headers,
    )

    result = await client.send_e2b_payload(
        input_data=input_data,
        user_id=user_id,
        change_reason=change_reason,
    )

    # Fetch created transmission with audit events
    stmt = select(AS2Transmission).where(
        AS2Transmission.transmission_id == result.transmission_id
    )
    res = await session.execute(stmt)
    tx = res.scalars().first()
    if not tx:
        raise HTTPException(
            status_code=500, detail="Transmission ledger record creation failed"
        )

    await session.commit()
    return map_transmission_to_response(tx)


@router.get(
    "/api/v1/safety/as2/transmissions",
    response_model=list[AS2TransmissionResponseDTO],
)
async def list_as2_transmissions_endpoint(
    request: Request,
    target_authority: str | None = None,
    status: str | None = None,
    study_id: str | None = None,
    case_id: str | None = None,
    limit: int = 50,
    offset: int = 0,
    session: AsyncSession = Depends(get_db_session),
) -> list[AS2TransmissionResponseDTO]:
    """List append-only AS2 B2B transmission ledger records with optional filters.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(AS2Transmission)
    if target_authority:
        stmt = stmt.where(AS2Transmission.target_authority == target_authority)
    if status:
        stmt = stmt.where(AS2Transmission.status == status)
    if study_id:
        stmt = stmt.where(AS2Transmission.study_id == study_id)
    if case_id:
        stmt = stmt.where(AS2Transmission.case_id == case_id)

    stmt = stmt.order_by(AS2Transmission.created_at.desc()).offset(offset).limit(limit)
    res = await session.execute(stmt)
    transmissions = res.scalars().all()

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="AS2_TRANSMISSIONS_QUERY",
        details=f"Queried AS2 transmission ledger (found {len(transmissions)} records).",
        change_reason=change_reason or "Query AS2 transmission ledger",
        version_index=1,
    )
    await session.commit()
    return [map_transmission_to_response(tx) for tx in transmissions]


@router.get(
    "/api/v1/safety/as2/transmissions/{id}",
    response_model=AS2TransmissionResponseDTO,
)
async def get_as2_transmission_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> AS2TransmissionResponseDTO:
    """Retrieve full audit history and status of an AS2 B2B transmission by ID.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(AS2Transmission).where(
        (AS2Transmission.id == id) | (AS2Transmission.transmission_id == id)
    )
    res = await session.execute(stmt)
    tx = res.scalars().first()
    if not tx:
        raise HTTPException(
            status_code=404, detail=f"AS2 Transmission '{id}' not found"
        )

    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="AS2_TRANSMISSION_READ",
        details=f"Retrieved AS2 transmission details for {tx.transmission_id}.",
        change_reason=change_reason or "View AS2 transmission",
        version_index=1,
        record_id=tx.id,
    )
    await session.commit()
    return map_transmission_to_response(tx)


@router.post(
    "/api/v1/safety/as2/mdn",
    response_model=MDNProcessResponseDTO,
)
async def process_inbound_mdn_endpoint(
    request: Request,
    payload: MDNProcessRequestDTO,
    session: AsyncSession = Depends(get_db_session),
) -> MDNProcessResponseDTO:
    """Process an asynchronous Message Disposition Notification (MDN) callback.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    client = AS2GatewayClient(session=session)

    mdn_res = await client.process_inbound_mdn(
        mdn_raw=payload.mdn_payload,
        user_id=user_id,
        change_reason=change_reason or "Inbound Asynchronous MDN Callback",
        authority_cert=payload.authority_cert_pem,
    )
    await session.commit()

    return MDNProcessResponseDTO(
        is_valid_mdn=mdn_res.is_valid_mdn,
        original_message_id=mdn_res.original_message_id,
        disposition_mode=mdn_res.disposition_mode,
        disposition_type=mdn_res.disposition_type.value,
        disposition_raw=mdn_res.disposition_raw,
        received_content_mic=mdn_res.received_content_mic,
        mic_matches=mdn_res.mic_matches,
        is_signed=mdn_res.is_signed,
        signature_valid=mdn_res.signature_valid,
        human_readable_text=mdn_res.human_readable_text,
        error_details=mdn_res.error_details,
    )


@router.post(
    "/api/v1/safety/as2/ack",
    response_model=RegulatoryAckProcessResponseDTO,
)
async def process_regulatory_ack_endpoint(
    request: Request,
    payload: RegulatoryAckProcessRequestDTO,
    session: AsyncSession = Depends(get_db_session),
) -> RegulatoryAckProcessResponseDTO:
    """Process an inbound Regulatory ACK B (Syntactic) or ACK C (Authority Ingestion) receipt.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    client = AS2GatewayClient(session=session)

    ack_res = await client.process_inbound_ack(
        ack_payload=payload.ack_payload,
        ack_type=payload.ack_type,
        user_id=user_id,
        change_reason=change_reason or f"Process Regulatory {payload.ack_type.value}",
        transmission_id=payload.transmission_id,
    )
    await session.commit()

    return RegulatoryAckProcessResponseDTO(
        ack_type=ack_res.ack_type.value,
        ack_status=ack_res.ack_status.value,
        is_accepted=ack_res.is_accepted,
        authority_receipt_id=ack_res.authority_receipt_id,
        worldwide_unique_case_id=ack_res.worldwide_unique_case_id,
        message_id=ack_res.message_id,
        authority_status_code=ack_res.authority_status_code,
        errors=ack_res.errors,
        warnings=ack_res.warnings,
        received_at=ack_res.received_at.isoformat(),
    )


@router.post(
    "/api/v1/safety/as2/transmissions/{id}/retry",
    response_model=AS2TransmissionResponseDTO,
)
async def retry_as2_transmission_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> AS2TransmissionResponseDTO:
    """Retry a failed AS2 transmission.

    Requirements: PRD-PVG-002, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=400,
            detail="Reason for change is mandatory for AS2 transmission retry.",
        )

    stmt = select(AS2Transmission).where(
        (AS2Transmission.id == id) | (AS2Transmission.transmission_id == id)
    )
    res = await session.execute(stmt)
    tx = res.scalars().first()
    if not tx:
        raise HTTPException(
            status_code=404, detail=f"AS2 Transmission '{id}' not found"
        )

    if not tx.outbound_xml:
        raise HTTPException(
            status_code=400,
            detail="Cannot retry transmission without preserved outbound XML.",
        )

    client = AS2GatewayClient(session=session)
    input_data = AS2OutboundTransmissionInput(
        case_id=tx.case_id,
        study_id=tx.study_id,
        icsr_xml_payload=tx.outbound_xml,
        target_authority=AS2Authority(tx.target_authority),
        sender_as2_id=tx.as2_from,
        receiver_as2_id=tx.as2_to,
        environment=tx.environment,
    )

    # Increment retry count on original record
    tx.retry_count += 1
    await write_as2_audit_log(
        session=session,
        transmission_id=tx.id,
        action="AS2_TRANSMISSION_RETRY_TRIGGERED",
        from_status=tx.status,
        to_status=TransmissionStatus.TRANSMITTED.value,
        details=f"Triggered transmission retry attempt #{tx.retry_count}",
        created_by=user_id,
        reason_for_change=change_reason,
    )
    await session.flush()

    retry_res = await client.send_e2b_payload(
        input_data=input_data,
        user_id=user_id,
        change_reason=f"Retry attempt #{tx.retry_count}: {change_reason}",
    )

    # Fetch updated retry transmission
    retry_stmt = select(AS2Transmission).where(
        AS2Transmission.transmission_id == retry_res.transmission_id
    )
    retry_tx_res = await session.execute(retry_stmt)
    retry_tx = retry_tx_res.scalars().first()
    if not retry_tx:
        raise HTTPException(
            status_code=500, detail="Retry transmission record not found"
        )

    await session.commit()
    return map_transmission_to_response(retry_tx)


# =========================================================================
# Disproportionality Signal Detection & Alert Workflow Endpoints (PRD-PVG-003)
# =========================================================================


def map_alert_to_dto(alert: SafetySignalAlert) -> SafetySignalAlertDTO:
    """Serializes a SafetySignalAlert ORM entity to a Pydantic DTO."""
    return SafetySignalAlertDTO(
        id=alert.id,
        detection_run_id=alert.detection_run_id,
        study_id=alert.study_id,
        target_drug=alert.target_drug,
        meddra_term=alert.meddra_term,
        term_level=alert.term_level,
        meddra_soc=alert.meddra_soc,
        case_count_a=alert.case_count_a,
        expected_count_e=alert.expected_count_e,
        prr=alert.prr,
        prr_ci_lower=alert.prr_ci_lower,
        prr_ci_upper=alert.prr_ci_upper,
        chi_square_yates=alert.chi_square_yates,
        ror=alert.ror,
        ror_ci_lower=alert.ror_ci_lower,
        ror_ci_upper=alert.ror_ci_upper,
        ebgm=alert.ebgm,
        eb05=alert.eb05,
        eb95=alert.eb95,
        severity=alert.severity,
        status=alert.status,
        triage_summary=alert.triage_summary,
        assigned_safety_officer=alert.assigned_safety_officer,
        review_decision=alert.review_decision,
        review_justification=alert.review_justification,
        clinical_summary=alert.clinical_summary,
        reviewed_by=alert.reviewed_by,
        reviewed_at=alert.reviewed_at.isoformat() if alert.reviewed_at else None,
        esignature_manifest_id=alert.esignature_manifest_id,
        created_at=alert.created_at.isoformat() if alert.created_at else "",
        created_by=alert.created_by,
        reason_for_change=alert.reason_for_change,
        version_index=alert.version_index,
    )


@router.post(
    "/api/v1/safety/signals/detect",
    response_model=SignalDetectionScanResponse,
    status_code=200,
)
async def execute_signal_detection_scan_endpoint(
    request: Request,
    payload: SignalDetectionScanRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SignalDetectionScanResponse:
    """Execute statistical disproportionality signal detection (PRR, ROR, EBGM/MGPS) across PT and SOC pairs.

    Requirements: PRD-PVG-003, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetySignalAlertService(session=session)
    result = await service.execute_scan(
        request=payload,
        user_id=user_id,
        reason_for_change=change_reason
        or "Automated Disproportionality Signal Detection Scan",
    )
    await session.commit()
    return result


@router.get(
    "/api/v1/safety/signals/runs",
    response_model=list[dict[str, Any]],
)
async def list_signal_detection_runs_endpoint(
    request: Request,
    study_id: str | None = None,
    target_drug: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[dict[str, Any]]:
    """List historical disproportionality signal detection scan runs.

    Requirements: PRD-PVG-003, ADR-2199
    """
    stmt = select(SafetySignalDetectionRun)
    if study_id:
        stmt = stmt.where(SafetySignalDetectionRun.study_id == study_id)
    if target_drug:
        stmt = stmt.where(SafetySignalDetectionRun.target_drug == target_drug)

    stmt = stmt.order_by(SafetySignalDetectionRun.created_at.desc())
    res = await session.execute(stmt)
    runs = res.scalars().all()

    return [
        {
            "id": r.id,
            "study_id": r.study_id,
            "target_drug": r.target_drug,
            "benchmark_dataset": r.benchmark_dataset,
            "total_cases_analyzed": r.total_cases_analyzed,
            "signals_detected_count": r.signals_detected_count,
            "created_at": r.created_at.isoformat() if r.created_at else "",
            "created_by": r.created_by,
        }
        for r in runs
    ]


@router.get(
    "/api/v1/safety/signals/runs/{id}",
    response_model=dict[str, Any],
)
async def get_signal_detection_run_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """Retrieve full metric tables and details for a signal detection scan run.

    Requirements: PRD-PVG-003, ADR-2199
    """
    stmt = select(SafetySignalDetectionRun).where(SafetySignalDetectionRun.id == id)
    res = await session.execute(stmt)
    run = res.scalars().first()
    if not run:
        raise HTTPException(
            status_code=404, detail=f"Signal Detection Run '{id}' not found"
        )

    return {
        "id": run.id,
        "study_id": run.study_id,
        "target_drug": run.target_drug,
        "benchmark_dataset": run.benchmark_dataset,
        "total_cases_analyzed": run.total_cases_analyzed,
        "signals_detected_count": run.signals_detected_count,
        "summary_metrics": run.summary_metrics,
        "created_at": run.created_at.isoformat() if run.created_at else "",
        "created_by": run.created_by,
    }


@router.get(
    "/api/v1/safety/signals/alerts",
    response_model=list[SafetySignalAlertDTO],
)
async def list_signal_alerts_endpoint(
    request: Request,
    study_id: str | None = None,
    target_drug: str | None = None,
    status: str | None = None,
    severity: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[SafetySignalAlertDTO]:
    """List safety signal review alerts with optional filtering by status, severity, or study.

    Requirements: PRD-PVG-003, ADR-2199
    """
    service = SafetySignalAlertService(session=session)
    alerts = await service.list_alerts(
        study_id=study_id,
        target_drug=target_drug,
        status=status,
        severity=severity,
    )
    return [map_alert_to_dto(a) for a in alerts]


@router.get(
    "/api/v1/safety/signals/alerts/{id}",
    response_model=SafetySignalAlertDTO,
)
async def get_signal_alert_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetySignalAlertDTO:
    """Retrieve details of an individual safety signal review alert.

    Requirements: PRD-PVG-003, ADR-2199
    """
    service = SafetySignalAlertService(session=session)
    alert = await service.get_alert(id)
    if not alert:
        raise HTTPException(
            status_code=404, detail=f"Safety Signal Alert '{id}' not found"
        )
    return map_alert_to_dto(alert)


@router.post(
    "/api/v1/safety/signals/alerts/{id}/review",
    response_model=SafetySignalAlertDTO,
)
async def review_signal_alert_endpoint(
    request: Request,
    id: str,
    payload: SafetySignalAlertReviewRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SafetySignalAlertDTO:
    """Review and update the clinical triage disposition of a safety signal alert.

    Requirements: PRD-PVG-003, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetySignalAlertService(session=session)
    try:
        updated = await service.review_alert(
            alert_id=id,
            request=payload,
            user_id=user_id,
            reason_for_change=change_reason
            or f"Signal review assessment: {payload.decision.value}",
        )
        await session.commit()
        return map_alert_to_dto(updated)
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@router.post(
    "/api/v1/safety/signals/alerts/{id}/sign",
    response_model=SafetySignalAlertSignResponse,
)
async def sign_signal_alert_endpoint(
    request: Request,
    id: str,
    payload: SafetySignalAlertSignRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SafetySignalAlertSignResponse:
    """Apply a 21 CFR Part 11 Electronic Signature to approve a safety signal disposition.

    Requirements: PRD-PVG-003, PRD-PVG-008, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_user = payload.user_id or user_id
    service = SafetySignalAlertService(session=session)
    try:
        sign_res = await service.sign_alert_review(
            alert_id=id,
            decision=payload.decision,
            justification=payload.justification,
            clinical_summary=payload.clinical_summary,
            user_id=effective_user,
            user_roles=[user_role] if isinstance(user_role, str) else user_role,
            reason_for_change=change_reason
            or f"Sign-off signal review: {payload.decision.value}",
            signature_secret=payload.signature_secret,
            meaning_of_signature=payload.meaning_of_signature,
        )
        await session.commit()
        return sign_res
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


# =========================================================================
# Periodic Aggregate Safety Reports Endpoints (DSUR, PSUR/PBRER) (PRD-PVG-004)
# =========================================================================


def map_periodic_report_to_dto(
    record: PeriodicSafetyReportRecord,
) -> PeriodicReportResponseDTO:
    """Serializes PeriodicSafetyReportRecord to DTO."""
    payload = record.report_payload or {}
    line_listings = payload.get("line_listings", [])
    tabulations = payload.get("summary_tabulations", [])
    signals = payload.get("signal_summary", [])
    snapshot = payload.get("snapshot") or {}
    snapshot_hash = snapshot.get("snapshot_hash")

    return PeriodicReportResponseDTO(
        id=record.id,
        report_type=record.report_type,
        study_id=record.study_id,
        compound_name=record.compound_name,
        reporting_period_start=record.period_start.isoformat()[:10]
        if record.period_start
        else "",
        reporting_period_end=record.period_end.isoformat()[:10]
        if record.period_end
        else "",
        dibd_anniversary=payload.get("dibd_anniversary"),
        status=record.status,
        total_subject_exposure=record.total_subject_exposure,
        total_saes=record.total_saes,
        total_fatal_cases=payload.get("total_fatal_cases", 0),
        total_susars=payload.get("total_susars", 0),
        total_aesi=payload.get("total_aesi", 0),
        snapshot_hash=snapshot_hash,
        line_listings_count=len(line_listings),
        summary_tabulations_count=len(tabulations),
        signals_included_count=len(signals),
        payload=payload,
        approved_by_user_id=record.approved_by_user_id,
        approved_at=record.approved_at.isoformat() if record.approved_at else None,
        esignature_manifest_id=record.esignature_manifest_id,
        qppv_signoff=payload.get("qppv_signoff"),
        created_at=record.created_at.isoformat() if record.created_at else "",
        created_by=record.created_by,
        reason_for_change=record.reason_for_change,
        version_index=record.version_index,
    )


@router.post(
    "/api/v1/safety/exposure/calculate",
    response_model=SubjectExposureCalculationResponse,
)
async def calculate_subject_exposure_endpoint(
    request: Request,
    payload: SubjectExposureCalculationRequest,
) -> SubjectExposureCalculationResponse:
    """Calculate cumulative and interval patient-years of exposure.

    Requirements: PRD-PVG-004, PRD-AGG-001, ADR-2199
    """
    metrics, _ = SubjectExposureCalculator.calculate_exposure(
        reporting_period_start=payload.reporting_period_start,
        reporting_period_end=payload.reporting_period_end,
        subjects_data=payload.subjects_exposure_data,
        raw_cases=payload.cases,
    )
    return SubjectExposureCalculationResponse(
        study_id=payload.study_id,
        reporting_period_start=payload.reporting_period_start,
        reporting_period_end=payload.reporting_period_end,
        metrics=metrics,
    )


@router.post(
    "/api/v1/safety/snapshots/lock",
    response_model=RegulatorySnapshotLockResponse,
    status_code=201,
)
async def lock_regulatory_snapshot_endpoint(
    request: Request,
    payload: RegulatorySnapshotLockRequest,
    session: AsyncSession = Depends(get_db_session),
) -> RegulatorySnapshotLockResponse:
    """Lock an immutable point-in-time regulatory dataset snapshot with SHA-256 seal.

    Requirements: PRD-AGG-001, 21 CFR Part 11, ADR-2199
    """
    user_id, _, change_reason = get_user_context(request)
    snapshot = RegulatoryAuditLedger.create_snapshot(
        compound_name=payload.compound_name,
        dibd_anniversary=payload.dibd_anniversary,
        lock_reason=payload.lock_reason,
        raw_cases=payload.cases,
        reporting_period_end=payload.reporting_period_end,
        reporting_period_start=payload.reporting_period_start,
        study_id=payload.study_id,
        subjects_data=payload.subjects_data,
        user_id=user_id,
    )

    await write_audit_log(
        session=session,
        created_by=user_id,
        action="REGULATORY_SNAPSHOT_LOCKED",
        details=f"Locked dataset snapshot {snapshot.snapshot_id} for study {snapshot.study_id}. Hash: {snapshot.snapshot_hash}.",
        record_id=snapshot.snapshot_id,
        reason_for_change=change_reason or payload.lock_reason,
        version_index=1,
    )
    await session.commit()

    return RegulatorySnapshotLockResponse(snapshot=snapshot)


@router.post(
    "/api/v1/safety/snapshots/verify",
    response_model=RegulatorySnapshotVerifyResponse,
)
async def verify_regulatory_snapshot_endpoint(
    payload: RegulatorySnapshotVerifyRequest,
) -> RegulatorySnapshotVerifyResponse:
    """Cryptographically verify the bitwise integrity of a locked regulatory snapshot.

    Requirements: PRD-AGG-001, 21 CFR Part 11, ADR-2199
    """
    res = RegulatoryAuditLedger.verify_snapshot_manifest(
        snapshot_id=payload.snapshot_id,
        expected_hash=payload.snapshot_hash,
        dataset_manifest=payload.dataset_manifest or {},
    )

    return RegulatorySnapshotVerifyResponse(
        snapshot_id=res["snapshot_id"],
        is_valid=res["is_valid"],
        computed_hash=res["computed_hash"],
        expected_hash=res["expected_hash"],
        match_status=res["match_status"],
        verified_at=res["verified_at"],
    )


@router.post(
    "/api/v1/safety/periodic-reports/compile",
    response_model=PeriodicReportResponseDTO,
    status_code=201,
)
async def compile_periodic_safety_report_endpoint(
    request: Request,
    payload: PeriodicReportCompilationRequest,
    session: AsyncSession = Depends(get_db_session),
) -> PeriodicReportResponseDTO:
    """Compile an aggregate periodic safety report (ICH E2F DSUR, ICH E2C(R2) PSUR/PBRER).

    Requirements: PRD-PVG-004, PRD-AGG-001, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    service = SafetySignalAlertService(session=session)
    result = await service.compile_periodic_report(
        request=payload,
        user_id=user_id,
        reason_for_change=change_reason
        or f"Compile {payload.report_type.value} Periodic Safety Report",
    )
    await session.commit()
    return result


@router.get(
    "/api/v1/safety/periodic-reports",
    response_model=list[PeriodicReportResponseDTO],
)
async def list_periodic_safety_reports_endpoint(
    request: Request,
    study_id: str | None = None,
    compound_name: str | None = None,
    report_type: str | None = None,
    status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[PeriodicReportResponseDTO]:
    """List compiled periodic regulatory safety reports with optional filters.

    Requirements: PRD-PVG-004, ADR-2199
    """
    stmt = select(PeriodicSafetyReportRecord)
    if study_id:
        stmt = stmt.where(PeriodicSafetyReportRecord.study_id == study_id)
    if compound_name:
        stmt = stmt.where(PeriodicSafetyReportRecord.compound_name == compound_name)
    if report_type:
        stmt = stmt.where(PeriodicSafetyReportRecord.report_type == report_type)
    if status:
        stmt = stmt.where(PeriodicSafetyReportRecord.status == status)

    stmt = stmt.order_by(PeriodicSafetyReportRecord.created_at.desc())
    res = await session.execute(stmt)
    records = res.scalars().all()
    return [map_periodic_report_to_dto(r) for r in records]


@router.get(
    "/api/v1/safety/periodic-reports/{id}",
    response_model=PeriodicReportResponseDTO,
)
async def get_periodic_safety_report_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> PeriodicReportResponseDTO:
    """Retrieve full structured JSON payload of a periodic regulatory safety report by ID.

    Requirements: PRD-PVG-004, ADR-2199
    """
    stmt = select(PeriodicSafetyReportRecord).where(PeriodicSafetyReportRecord.id == id)
    res = await session.execute(stmt)
    record = res.scalars().first()
    if not record:
        raise HTTPException(
            status_code=404, detail=f"Periodic Safety Report '{id}' not found"
        )
    return map_periodic_report_to_dto(record)


@router.post(
    "/api/v1/safety/periodic-reports/{id}/sign",
    response_model=PeriodicReportSignResponse,
)
async def sign_periodic_safety_report_endpoint(
    request: Request,
    id: str,
    payload: PeriodicReportSignRequest,
    session: AsyncSession = Depends(get_db_session),
) -> PeriodicReportSignResponse:
    """Apply a 21 CFR Part 11 Electronic Signature to approve a Periodic Safety Report.

    Requirements: PRD-PVG-004, PRD-PVG-008, PRD-AGG-001, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_user = payload.user_id or user_id
    effective_roles = []
    if payload.role:
        effective_roles.append(payload.role)
    if user_role:
        if isinstance(user_role, (list, set, tuple)):
            effective_roles.extend(user_role)
        else:
            effective_roles.append(str(user_role))
    if not effective_roles:
        effective_roles = ["safety_officer"]
    service = SafetySignalAlertService(session=session)
    try:
        sign_res = await service.sign_periodic_report(
            report_id=id,
            user_id=effective_user,
            user_roles=effective_roles,
            reason_for_change=change_reason
            or "21 CFR Part 11 Approval of Periodic Safety Report",
            signature_secret=payload.signature_secret,
            meaning_of_signature=payload.meaning_of_signature,
        )
        await session.commit()
        return sign_res
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.post(
    "/api/v1/safety/periodic-reports/{id}/qppv-sign",
    response_model=PeriodicReportSignResponse,
)
async def qppv_sign_periodic_safety_report_endpoint(
    request: Request,
    id: str,
    payload: PeriodicReportSignRequest,
    session: AsyncSession = Depends(get_db_session),
) -> PeriodicReportSignResponse:
    """Executive QPPV Sign-off endpoint for aggregate regulatory safety submissions.

    Requirements: PRD-AGG-001, 21 CFR Part 11, ADR-2199
    """
    user_id, user_role, change_reason = get_user_context(request)
    effective_user = payload.user_id or user_id
    roles = [payload.role or "qppv"]
    if user_role and user_role not in roles:
        roles.append(user_role if isinstance(user_role, str) else str(user_role))

    service = SafetySignalAlertService(session=session)
    try:
        sign_res = await service.sign_periodic_report(
            report_id=id,
            user_id=effective_user,
            user_roles=roles,
            reason_for_change=change_reason or "Executive QPPV Approval for Submission",
            signature_secret=payload.signature_secret,
            meaning_of_signature=payload.meaning_of_signature
            or "Executive QPPV Approval of Aggregate Safety Report",
        )
        await session.commit()
        return sign_res
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


@router.get(
    "/api/v1/safety/periodic-reports/{id}/export",
    response_class=Response,
)
async def export_periodic_safety_report_endpoint(
    request: Request,
    id: str,
    format: str = "html",
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """Export a periodic safety report as HTML, XML, or JSON.

    Requirements: PRD-PVG-004, PRD-AGG-001, ADR-2199
    """
    stmt = select(PeriodicSafetyReportRecord).where(PeriodicSafetyReportRecord.id == id)
    res = await session.execute(stmt)
    record = res.scalars().first()
    if not record:
        raise HTTPException(
            status_code=404, detail=f"Periodic Safety Report '{id}' not found"
        )

    from apps.safety.domain.periodic_report_models import PeriodicReportPayload

    payload_obj = PeriodicReportPayload(**record.report_payload)
    fmt_lower = format.lower()

    if fmt_lower == "xml":
        xml_content = PeriodicSafetyReportCompiler.generate_ich_xml(payload_obj)
        return Response(
            content=xml_content,
            media_type="application/xml",
            headers={
                "Content-Disposition": f"attachment; filename={record.report_type}_{record.study_id}_{record.id}.xml"
            },
        )
    if fmt_lower == "json":
        json_content = json.dumps(payload_obj.model_dump(), indent=2, default=str)
        return Response(
            content=json_content,
            media_type="application/json",
            headers={
                "Content-Disposition": f"attachment; filename={record.report_type}_{record.study_id}_{record.id}.json"
            },
        )
    # Default HTML
    html_content = PeriodicSafetyReportCompiler.render_html_report(payload_obj)
    return Response(
        content=html_content,
        media_type="text/html",
        headers={
            "Content-Disposition": f"inline; filename={record.report_type}_{record.study_id}_{record.id}.html"
        },
    )


@router.get(
    "/api/v1/safety/periodic-reports/{id}/xml",
    response_class=Response,
)
async def export_periodic_safety_report_xml_endpoint(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """Export the structured ICH E2F / E2C(R2) XML submission package.

    Requirements: PRD-AGG-001, ICH E2F, ICH E2C(R2)
    """
    return await export_periodic_safety_report_endpoint(
        request=request, id=id, format="xml", session=session
    )


# =========================================================================
# Medical Device & IVD Safety Investigation Suite (ISO 14155 / EU MDR / IVDR)
# Requirements: PRD-SYS-126, PRD-DEV-003, PRD-DEV-004, ADR-2209
# =========================================================================


def map_device_investigation_to_dto(
    inv: DeviceSafetyInvestigation,
) -> DeviceSafetyInvestigationResponseDTO:
    """Serializes a DeviceSafetyInvestigation ORM entity to a Pydantic DTO with countdown clocks."""
    return DeviceSafetyService.map_investigation_to_dto(inv)


@router.post(
    "/api/v1/safety/device-investigations",
    response_model=DeviceSafetyInvestigationResponseDTO,
    status_code=201,
)
async def open_device_safety_investigation_endpoint(
    request: Request,
    payload: DeviceSafetyInvestigationCreateDTO,
    session: AsyncSession = Depends(get_db_session),
) -> DeviceSafetyInvestigationResponseDTO:
    """Opens a new Adverse Device Effect (ADE / SADE / USADE) safety investigation case.

    Calculates vigilance reporting deadlines according to ISO 14155 and EU MDR / IVDR.

    Requirements: PRD-SYS-126, PRD-DEV-003, ISO 14155:2020, ADR-2209
    """
    user_id, change_reason, _ = get_user_context(request)
    return await DeviceSafetyService.open_investigation(
        change_reason=change_reason,
        payload=payload,
        session=session,
        user_id=user_id,
    )


@router.get(
    "/api/v1/safety/device-investigations",
    response_model=list[DeviceSafetyInvestigationResponseDTO],
    status_code=200,
)
async def list_device_safety_investigations_endpoint(
    study_id: str | None = None,
    site_id: str | None = None,
    classification: str | None = None,
    status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[DeviceSafetyInvestigationResponseDTO]:
    """Lists device safety investigation cases matching query parameters.

    Requirements: PRD-SYS-126, PRD-DEV-003, ADR-2209
    """
    return await DeviceSafetyService.list_investigations(
        classification=classification,
        session=session,
        site_id=site_id,
        status=status,
        study_id=study_id,
    )


@router.get(
    "/api/v1/safety/device-investigations/{investigation_id}",
    response_model=DeviceSafetyInvestigationResponseDTO,
    status_code=200,
)
async def get_device_safety_investigation_endpoint(
    investigation_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> DeviceSafetyInvestigationResponseDTO:
    """Retrieves a single device safety investigation case by ID.

    Requirements: PRD-SYS-126, PRD-DEV-003, ADR-2209
    """
    return await DeviceSafetyService.get_investigation(
        investigation_id=investigation_id,
        session=session,
    )


@router.put(
    "/api/v1/safety/device-investigations/{investigation_id}/adjudicate",
    response_model=DeviceSafetyInvestigationResponseDTO,
    status_code=200,
)
async def adjudicate_device_safety_investigation_endpoint(
    request: Request,
    investigation_id: str,
    payload: DeviceSafetyInvestigationAdjudicateDTO,
    session: AsyncSession = Depends(get_db_session),
) -> DeviceSafetyInvestigationResponseDTO:
    """Formally adjudicates Adverse Device Effect causality and SADE determination with Part 11 e-signature.

    Requirements: PRD-SYS-126, PRD-DEV-003, 21 CFR Part 11, ISO 14155:2020, ADR-2209
    """
    user_id, change_reason, _ = get_user_context(request)
    return await DeviceSafetyService.adjudicate_investigation(
        change_reason=change_reason,
        investigation_id=investigation_id,
        payload=payload,
        session=session,
        user_id=user_id,
    )


@router.post(
    "/api/v1/safety/device-investigations/{investigation_id}/regulatory-report",
    response_model=DeviceVigilanceReportResponseDTO,
    status_code=200,
)
async def generate_device_vigilance_report_endpoint(
    request: Request,
    investigation_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> DeviceVigilanceReportResponseDTO:
    """Generates an EU MDR 2017/745 / EU IVDR 2017/746 Manufacturer Incident Report (MIR v7.2 JSON/XML).

    Requirements: PRD-SYS-126, PRD-DEV-004, EU MDR 2017/745, EU IVDR 2017/746, ADR-2209
    """
    return await DeviceSafetyService.generate_vigilance_report(
        investigation_id=investigation_id,
        session=session,
    )
