import os
from typing import Any, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

import copy
from apps.safety.database import db_manager
from apps.safety.models import Base, SafetyAuditLog, SafetyCaseICSR, SafetyExportJob
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware
from sae_icsr import IndividualCaseSafetyReport


# Pydantic Schemas for Request/Response Validation
class ICSRDataExportRequest(BaseModel):
    job_name: str = Field(..., description="The descriptive name of the export job")
    icsr: IndividualCaseSafetyReport = Field(..., description="The E2B ICSR report data")
class SafetyCaseICSRCreate(BaseModel):
    worldwide_unique_case_id: str = Field(
        ..., description="Worldwide unique identifier for this safety case"
    )
    patient_id: str = Field(..., description="Unique subject/patient identifier")
    case_data: Dict[str, Any] = Field(
        ..., description="The structured ICSR case JSON payload"
    )


class SafetyCaseICSRResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    worldwide_unique_case_id: str
    patient_id: str
    case_data: Dict[str, Any]
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SafetyExportJobCreate(BaseModel):
    job_name: str = Field(..., description="The descriptive name of the export job")


class SafetyExportJobResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    job_name: str
    status: str
    error_message: Optional[str] = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class SafetyAuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    created_at: str
    created_by: str
    reason_for_change: Optional[str] = None
    version_index: int
    action: str
    details: str
    record_id: Optional[str] = None


DATABASE_URL = os.getenv("SAFETY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - Safety & Pharmacovigilance Gateway",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)

# Dependable to obtain database session
get_db_session = DatabaseSessionDependency(db_manager)


def get_user_context(request: Request):
    """
    Helper to extract user identity headers parsed by GatewayAuthMiddleware.
    """
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
    record_id: Optional[str] = None,
    change_reason: Optional[str] = None,
    version_index: int = 1,
) -> None:
    """
    Utility function to write to the immutable Safety audit ledger.
    """
    log_entry = SafetyAuditLog(
        created_by=user_id,
        action=action,
        details=details,
        record_id=record_id,
        reason_for_change=change_reason,
        version_index=version_index,
    )
    session.add(log_entry)
    await session.flush()


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "safety"}


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


def map_job_to_response(job: SafetyExportJob) -> SafetyExportJobResponse:
    return SafetyExportJobResponse(
        id=job.id,
        job_name=job.job_name,
        status=job.status,
        error_message=job.error_message,
        created_at=job.created_at.isoformat(),
        created_by=job.created_by,
        reason_for_change=job.reason_for_change,
        version_index=job.version_index,
    )


# Safety Cases / ICSR Endpoints
@app.post(
    "/api/v1/safety/cases", response_model=SafetyCaseICSRResponse, status_code=201
)
async def create_safety_case(
    request: Request,
    payload: SafetyCaseICSRCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyCaseICSRResponse:
    """
    Create and persist a new Safety Case / ICSR record.
    """
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


@app.get("/api/v1/safety/cases", response_model=List[SafetyCaseICSRResponse])
async def list_safety_cases(
    request: Request,
    patient_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> List[SafetyCaseICSRResponse]:
    """
    List all safety cases, optionally filtered by patient ID.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SafetyCaseICSR)
    if patient_id:
        stmt = stmt.where(SafetyCaseICSR.patient_id == patient_id)

    result = await session.execute(stmt)
    cases = result.scalars().all()

    # Log listing action
    await write_safety_audit_log(
        session=session,
        user_id=user_id,
        action="SAFETY_CASE_LIST",
        details=f"Listed safety cases (patient_id filter: {patient_id}).",
        change_reason=change_reason,
        version_index=1,
    )

    return [map_case_to_response(c) for c in cases]


@app.get("/api/v1/safety/cases/{id}", response_model=SafetyCaseICSRResponse)
async def get_safety_case(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyCaseICSRResponse:
    """
    Retrieve a specific safety case record by its ID.
    """
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


# Safety Export Jobs Endpoints
@app.post(
    "/api/v1/safety/export-jobs",
    response_model=SafetyExportJobResponse,
    status_code=201,
)
async def create_export_job(
    request: Request,
    payload: SafetyExportJobCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    """
    Create a new safety ICSR export job.
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    job = SafetyExportJob(
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


@app.get("/api/v1/safety/export-jobs", response_model=List[SafetyExportJobResponse])
async def list_export_jobs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[SafetyExportJobResponse]:
    """
    List all safety export jobs.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SafetyExportJob)
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


@app.get("/api/v1/safety/export-jobs/{id}", response_model=SafetyExportJobResponse)
async def get_export_job(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    """
    Retrieve details of a specific export job by ID.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(SafetyExportJob).where(SafetyExportJob.id == id)
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


# Audit Logs Retrieval Endpoint
@app.get("/api/v1/safety/audit-logs", response_model=List[SafetyAuditLogResponse])
async def list_safety_audit_logs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[SafetyAuditLogResponse]:
    """
    Retrieve safety audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    # Note: Recording self-auditing list action first so it is included in the query result.
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


@app.post(
    "/api/v1/safety/export",
    response_model=SafetyExportJobResponse,
    status_code=201,
)
async def export_safety_case(
    request: Request,
    payload: ICSRDataExportRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SafetyExportJobResponse:
    """
    Expose validated E2B export and outbound safety/PV transmission.
    Accepts SAE/ICSR data, renders and validates E2B XML, pseudonymizes patient PII
    following the HMAC approach, transmits to configured safety gateway,
    persists a SafetyExportJob, and writes GxP-compliant audit events.
    """
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    # 1. Render initial raw XML to validate structural correctness
    from apps.safety.renderer import render_icsr_to_xml
    from apps.safety.validator import validate_icsr_xml

    try:
        raw_xml = render_icsr_to_xml(payload.icsr)
    except Exception as e:
        raise HTTPException(
            status_code=422,
            detail=f"XML rendering failed: {str(e)}",
        )

    is_valid, msg = validate_icsr_xml(raw_xml)
    if not is_valid:
        raise HTTPException(
            status_code=422,
            detail=f"Structural validation failure: {msg}",
        )

    # 2. Persist the export job as PENDING first
    job = SafetyExportJob(
        job_name=payload.job_name,
        status="PENDING",
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(job)
    await session.flush()

    # 3. Pseudonymize and remove direct patient PII following the HMAC approach
    from packages.deid.transforms import pseudonymize_value

    salt = os.getenv("SAFETY_SALT", "internal-safety-salt-12345")
    icsr_copy = copy.deepcopy(payload.icsr)

    raw_patient_id = icsr_copy.patient.patient_id
    pseudonymized_patient_id = pseudonymize_value(raw_patient_id, salt)

    icsr_copy.patient.patient_id = pseudonymized_patient_id
    icsr_copy.patient.birth_date = None  # Remove direct DOB

    # Render pseudonymized XML
    pseudonymized_xml = render_icsr_to_xml(icsr_copy)

    # 4. Transmit the pseudonymized XML payload using the SafetyDatabaseAdapter
    from apps.safety.adapter import SafetyDatabaseAdapter

    # Allow custom client state injection for testing
    client = getattr(request.app.state, "test_httpx_client", None)
    adapter = SafetyDatabaseAdapter(client=client)

    try:
        response = await adapter.transmit(pseudonymized_xml)
        if 200 <= response.status_code < 300:
            job.status = "COMPLETED"
        else:
            job.status = "FAILED"
            job.error_message = f"Transmission failed with status {response.status_code}: {response.text}"
    except Exception as e:
        job.status = "FAILED"
        job.error_message = f"Transmission exception: {str(e)}"

    await session.flush()

    # 5. Write audit event to SafetyAuditLog, ensuring raw patient PII is absent
    audit_action = "SAFETY_EXPORT_JOB_COMPLETE" if job.status == "COMPLETED" else "SAFETY_EXPORT_JOB_FAIL"
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
