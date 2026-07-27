import os
from datetime import datetime
from typing import List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from apps.quality.database import db_manager
from apps.quality.models import (
    Base,
    CAPARecord,
    CAPAStatus,
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    QualityAuditLog,
    RootCauseAnalysis,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import get_normalized_roles


# Pydantic Schemas for Request/Response Validation
class DeviationCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: Optional[str] = Field(None, description="Optional clinical site ID")
    title: str = Field(
        ..., max_length=255, description="A short summary of the deviation"
    )
    description: str = Field(..., description="Detailed explanation of the deviation")
    severity: DeviationSeverity = Field(
        ..., description="Severity level: MINOR, MAJOR, CRITICAL"
    )
    type: DeviationType = Field(
        ..., description="Type of deviation, e.g., INFORMED_CONSENT"
    )
    is_protocol_violation: bool = Field(
        False, description="Whether this constitutes a protocol violation"
    )


class DeviationResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: Optional[str] = None
    title: str
    description: str
    severity: DeviationSeverity
    status: DeviationStatus
    type: DeviationType
    is_protocol_violation: bool
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class RCACreateOrUpdate(BaseModel):
    methodology: str = Field(
        ..., max_length=255, description="RCA methodology used, e.g., 5 Whys, Fishbone"
    )
    investigation_details: str = Field(
        ..., description="Full details of the investigation"
    )
    root_cause_summary: str = Field(
        ..., description="Summary of the determined root cause"
    )
    version_index: Optional[int] = Field(
        None, description="Current expected version index for optimistic locking"
    )


class RCAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    methodology: str
    investigation_details: str
    root_cause_summary: str
    study_id: str
    site_id: Optional[str] = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


class CAPACreate(BaseModel):
    deviation_id: str = Field(..., description="Reference to the parent deviation ID")
    rca_id: Optional[str] = Field(
        None, description="Optional reference to the Root Cause Analysis ID"
    )
    capa_type: str = Field(..., description="Type of CAPA: CORRECTIVE or PREVENTIVE")
    action_plan: str = Field(
        ..., description="The planned corrective/preventive action steps"
    )
    preventive_measures: Optional[str] = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: Optional[datetime] = Field(
        None, description="Optional expected completion timestamp"
    )


class CAPATransitionRequest(BaseModel):
    to_status: CAPAStatus = Field(
        ..., description="Target CAPA Status to transition to"
    )
    version_index: Optional[int] = Field(
        None, description="Expected version index for optimistic locking"
    )


class CAPAUpdate(BaseModel):
    action_plan: Optional[str] = Field(
        None, description="The planned corrective/preventive action steps"
    )
    preventive_measures: Optional[str] = Field(
        None, description="Specific measures to prevent recurrence"
    )
    target_completion_date: Optional[datetime] = Field(
        None, description="Optional expected completion timestamp"
    )
    version_index: Optional[int] = Field(
        None, description="Current expected version index for optimistic locking"
    )


class CAPAResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    deviation_id: str
    rca_id: Optional[str] = None
    capa_type: str
    action_plan: str
    status: CAPAStatus
    preventive_measures: Optional[str] = None
    target_completion_date: Optional[str] = None
    study_id: str
    site_id: Optional[str] = None
    created_at: str
    created_by: str
    version_index: int
    reason_for_change: str


DATABASE_URL = os.getenv("QUALITY_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - Quality & CAPA",
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


async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
    record_id: Optional[str] = None,
    change_reason: Optional[str] = None,
) -> None:
    """
    Utility helper to write to the append-only QualityAuditLog.
    """
    log_entry = QualityAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
        record_id=record_id,
        change_reason=change_reason,
    )
    session.add(log_entry)
    await session.flush()


def get_request_roles(request: Request) -> list[str]:
    return get_normalized_roles(request)


def authorize_quality_write(request: Request) -> list[str]:
    roles = get_request_roles(request)
    read_only_roles = {
        "auditor",
        "inspector",
        "regulatory_inspector",
        "viewer",
        "read_only",
    }
    if not roles or any(r in read_only_roles for r in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Read-only roles are restricted to read-only access.",
        )
    return roles


def authorize_quality_oversight(request: Request) -> list[str]:
    roles = authorize_quality_write(request)
    oversight_roles = {"quality_manager", "qa_lead", "quality_oversight", "admin"}
    if not any(r in oversight_roles for r in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Quality oversight role required for CAPA approval or closure.",
        )
    return roles


def get_user_context(request: Request):
    user_id = getattr(request.state, "user_id", "system")
    user_role = request.headers.get("X-User-Roles", "system")
    change_reason = getattr(
        request.state, "change_reason", None
    ) or request.headers.get("X-Change-Reason")
    return user_id, user_role, change_reason


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "quality"}


# CAPA explicit transition map
CAPA_TRANSITIONS = {
    CAPAStatus.INITIATED: {CAPAStatus.UNDER_REVIEW, CAPAStatus.CANCELLED},
    CAPAStatus.UNDER_REVIEW: {
        CAPAStatus.IMPLEMENTATION,
        CAPAStatus.INITIATED,
        CAPAStatus.CANCELLED,
    },
    CAPAStatus.IMPLEMENTATION: {CAPAStatus.EFFECTIVENESS_CHECK, CAPAStatus.CANCELLED},
    CAPAStatus.EFFECTIVENESS_CHECK: {CAPAStatus.CLOSED, CAPAStatus.CANCELLED},
    CAPAStatus.CLOSED: set(),
    CAPAStatus.CANCELLED: set(),
}


def map_deviation_to_response(dev: Deviation) -> DeviationResponse:
    return DeviationResponse(
        id=dev.id,
        study_id=dev.study_id,
        site_id=dev.site_id,
        title=dev.title,
        description=dev.description,
        severity=dev.severity,
        status=dev.status,
        type=dev.type,
        is_protocol_violation=dev.is_protocol_violation,
        created_at=dev.created_at.isoformat(),
        created_by=dev.created_by,
        version_index=dev.version_index,
        reason_for_change=dev.reason_for_change,
    )


def map_rca_to_response(rca: RootCauseAnalysis) -> RCAResponse:
    return RCAResponse(
        id=rca.id,
        deviation_id=rca.deviation_id,
        methodology=rca.methodology,
        investigation_details=rca.investigation_details,
        root_cause_summary=rca.root_cause_summary,
        study_id=rca.study_id,
        site_id=rca.site_id,
        created_at=rca.created_at.isoformat(),
        created_by=rca.created_by,
        version_index=rca.version_index,
        reason_for_change=rca.reason_for_change,
    )


def map_capa_to_response(capa: CAPARecord) -> CAPAResponse:
    return CAPAResponse(
        id=capa.id,
        deviation_id=capa.deviation_id,
        rca_id=capa.rca_id,
        capa_type=capa.capa_type,
        action_plan=capa.action_plan,
        status=capa.status,
        preventive_measures=capa.preventive_measures,
        target_completion_date=(
            capa.target_completion_date.isoformat()
            if capa.target_completion_date
            else None
        ),
        study_id=capa.study_id,
        site_id=capa.site_id,
        created_at=capa.created_at.isoformat(),
        created_by=capa.created_by,
        version_index=capa.version_index,
        reason_for_change=capa.reason_for_change,
    )


@app.post(
    "/api/v1/quality/deviations", response_model=DeviationResponse, status_code=201
)
async def create_deviation(
    request: Request,
    payload: DeviationCreate,
    session: AsyncSession = Depends(get_db_session),
) -> DeviationResponse:
    """
    Create a new clinical protocol deviation or quality deviation event.
    """
    authorize_quality_write(request)
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    dev = Deviation(
        study_id=payload.study_id,
        site_id=payload.site_id,
        title=payload.title,
        description=payload.description,
        severity=payload.severity,
        status=DeviationStatus.REPORTED,
        type=payload.type,
        is_protocol_violation=payload.is_protocol_violation,
        created_by=user_id,
        version_index=1,
        reason_for_change=change_reason,
    )
    session.add(dev)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="DEVIATION_CREATE",
        details=f"Created deviation '{payload.title}' for study '{payload.study_id}' with status REPORTED.",
        record_id=dev.id,
        change_reason=change_reason,
    )

    return map_deviation_to_response(dev)


@app.get("/api/v1/quality/deviations", response_model=List[DeviationResponse])
async def list_deviations(
    request: Request,
    study_id: Optional[str] = Query(None, description="Filter by study ID"),
    site_id: Optional[str] = Query(None, description="Filter by site ID"),
    status: Optional[DeviationStatus] = Query(None, description="Filter by status"),
    session: AsyncSession = Depends(get_db_session),
) -> List[DeviationResponse]:
    """
    Retrieve clinical deviation records with optional filtering.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Deviation)
    if study_id:
        stmt = stmt.where(Deviation.study_id == study_id)
    if site_id:
        stmt = stmt.where(Deviation.site_id == site_id)
    if status:
        stmt = stmt.where(Deviation.status == status)

    result = await session.execute(stmt)
    deviations = result.scalars().all()

    # Log listing action
    filters = f"study_id={study_id}, site_id={site_id}, status={status}"
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="DEVIATION_LIST",
        details=f"Listed deviations matching criteria: {filters}.",
    )

    return [map_deviation_to_response(dev) for dev in deviations]


@app.get("/api/v1/quality/deviations/{id}", response_model=DeviationResponse)
async def view_deviation(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> DeviationResponse:
    """
    Retrieve a specific clinical deviation by ID.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(Deviation).where(Deviation.id == id)
    result = await session.execute(stmt)
    dev = result.scalars().first()

    if not dev:
        raise HTTPException(status_code=404, detail="Deviation not found")

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="DEVIATION_VIEW",
        details=f"Viewed deviation ID: {id}.",
        record_id=id,
    )

    return map_deviation_to_response(dev)


@app.post("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
@app.put("/api/v1/quality/deviations/{id}/rca", response_model=RCAResponse)
async def create_or_update_rca(
    request: Request,
    id: str,
    payload: RCACreateOrUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> RCAResponse:
    """
    Create or update Root Cause Analysis (RCA) linked to a specific deviation.
    Transitions the deviation status to RCA_COMPLETE.
    """
    authorize_quality_write(request)
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    # Verify parent deviation exists
    stmt_dev = select(Deviation).where(Deviation.id == id)
    result_dev = await session.execute(stmt_dev)
    dev = result_dev.scalars().first()

    if not dev:
        raise HTTPException(status_code=404, detail="Parent deviation not found")

    # Check if RCA already exists
    stmt_rca = select(RootCauseAnalysis).where(RootCauseAnalysis.deviation_id == id)
    result_rca = await session.execute(stmt_rca)
    rca = result_rca.scalars().first()

    action = "RCA_CREATE"
    if rca:
        action = "RCA_UPDATE"
        # Validate version mismatch for optimistic concurrency
        if (
            payload.version_index is not None
            and rca.version_index != payload.version_index
        ):
            raise HTTPException(
                status_code=409,
                detail=f"Version conflict: The RCA has been modified by another process. Current version: {rca.version_index}.",
            )
        rca.methodology = payload.methodology
        rca.investigation_details = payload.investigation_details
        rca.root_cause_summary = payload.root_cause_summary
        rca.version_index += 1
        rca.reason_for_change = change_reason
    else:
        rca = RootCauseAnalysis(
            deviation_id=id,
            methodology=payload.methodology,
            investigation_details=payload.investigation_details,
            root_cause_summary=payload.root_cause_summary,
            study_id=dev.study_id,
            site_id=dev.site_id,
            created_by=user_id,
            version_index=1,
            reason_for_change=change_reason,
        )
        session.add(rca)

    # Automatically progress parent deviation to RCA_COMPLETE
    if dev.status != DeviationStatus.RCA_COMPLETE:
        dev.status = DeviationStatus.RCA_COMPLETE
        dev.version_index += 1
        dev.reason_for_change = f"Progressed status to RCA_COMPLETE via {action}"
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_UPDATE",
            details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to RCA_COMPLETE.",
            record_id=dev.id,
            change_reason=change_reason,
        )

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=f"Performed {action} for deviation ID: {id}.",
        record_id=rca.id,
        change_reason=change_reason,
    )

    return map_rca_to_response(rca)


@app.post("/api/v1/quality/capas", response_model=CAPAResponse, status_code=201)
async def create_capa(
    request: Request,
    payload: CAPACreate,
    session: AsyncSession = Depends(get_db_session),
) -> CAPAResponse:
    """
    Create a new Corrective and Preventive Action (CAPA) record linked to a deviation.
    """
    authorize_quality_write(request)
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    # 1. Validate parent deviation exists
    stmt_dev = select(Deviation).where(Deviation.id == payload.deviation_id)
    result_dev = await session.execute(stmt_dev)
    dev = result_dev.scalars().first()

    if not dev:
        raise HTTPException(
            status_code=422,
            detail=f"Parent deviation with ID '{payload.deviation_id}' not found.",
        )

    # Compatibility: Ensure deviation is not in a terminal/closed state
    if dev.status in (DeviationStatus.CLOSED, DeviationStatus.RESOLVED):
        raise HTTPException(
            status_code=422,
            detail=f"Cannot create CAPA for a settled or closed deviation (current status: {dev.status}).",
        )

    # 2. Validate optional RCA if specified
    if payload.rca_id:
        stmt_rca = select(RootCauseAnalysis).where(
            RootCauseAnalysis.id == payload.rca_id
        )
        result_rca = await session.execute(stmt_rca)
        rca = result_rca.scalars().first()

        if not rca:
            raise HTTPException(
                status_code=422,
                detail=f"RCA with ID '{payload.rca_id}' not found.",
            )

        if rca.deviation_id != payload.deviation_id:
            raise HTTPException(
                status_code=422,
                detail=f"RCA ID '{payload.rca_id}' is not linked to deviation ID '{payload.deviation_id}'.",
            )

    # 3. Create CAPA
    capa = CAPARecord(
        deviation_id=payload.deviation_id,
        rca_id=payload.rca_id,
        capa_type=payload.capa_type,
        action_plan=payload.action_plan,
        status=CAPAStatus.INITIATED,
        preventive_measures=payload.preventive_measures,
        target_completion_date=payload.target_completion_date,
        study_id=dev.study_id,
        site_id=dev.site_id,
        created_by=user_id,
        version_index=1,
        reason_for_change=change_reason,
    )
    session.add(capa)

    # 4. Progress parent deviation status to CAPA_INITIATED
    if dev.status != DeviationStatus.CAPA_INITIATED:
        dev.status = DeviationStatus.CAPA_INITIATED
        dev.version_index += 1
        dev.reason_for_change = "Progressed status to CAPA_INITIATED via CAPA creation"
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="DEVIATION_UPDATE",
            details=f"Updated deviation '{dev.title}' (ID: {dev.id}) status to CAPA_INITIATED.",
            record_id=dev.id,
            change_reason=change_reason,
        )

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="CAPA_CREATE",
        details=f"Created CAPA (ID: {capa.id}) linked to deviation ID '{payload.deviation_id}' with status INITIATED.",
        record_id=capa.id,
        change_reason=change_reason,
    )

    return map_capa_to_response(capa)


@app.post("/api/v1/quality/capas/{id}/transition", response_model=CAPAResponse)
async def transition_capa(
    request: Request,
    id: str,
    payload: CAPATransitionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> CAPAResponse:
    """
    Perform a secure, 21 CFR Part 11 compliant status transition on a CAPA record.
    """
    if payload.to_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
        authorize_quality_oversight(request)
    else:
        authorize_quality_write(request)

    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    # Fetch CAPA and lock/load parent deviation
    stmt_capa = (
        select(CAPARecord)
        .where(CAPARecord.id == id)
        .options(selectinload(CAPARecord.deviation))
    )
    result_capa = await session.execute(stmt_capa)
    capa = result_capa.scalars().first()

    if not capa:
        raise HTTPException(
            status_code=404, detail=f"CAPA record with ID '{id}' not found."
        )

    current_status = capa.status
    target_status = payload.to_status

    # Validate version mismatch for optimistic concurrency
    if (
        payload.version_index is not None
        and capa.version_index != payload.version_index
    ):
        raise HTTPException(
            status_code=409,
            detail=f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
        )

    # 1. Reject transition from terminal states
    if current_status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
        raise HTTPException(
            status_code=422,
            detail=f"Transitions out of terminal state '{current_status}' are irreversible and strictly prohibited.",
        )

    # 2. Validate against explicit transitions map
    allowed_targets = CAPA_TRANSITIONS.get(current_status, set())
    if target_status not in allowed_targets:
        raise HTTPException(
            status_code=422,
            detail=f"Invalid transition from state '{current_status}' to '{target_status}'. Allowed targets: {[s.value for s in allowed_targets]}.",
        )

    # 3. Transition status
    capa.status = target_status
    capa.version_index += 1
    capa.reason_for_change = change_reason

    # 4. Keep linked deviation state consistent (Settlement)
    if target_status == CAPAStatus.CLOSED:
        # Progress parent deviation to CLOSED
        dev = capa.deviation
        if dev and dev.status != DeviationStatus.CLOSED:
            dev.status = DeviationStatus.CLOSED
            dev.version_index += 1
            dev.reason_for_change = (
                "Settled and closed parent deviation because linked CAPA was closed."
            )
            await write_audit_log(
                session=session,
                user_id=user_id,
                user_role=user_role,
                action="DEVIATION_UPDATE",
                details=f"Settled and closed parent deviation (ID: {dev.id}) following CAPA closure.",
                record_id=dev.id,
                change_reason=change_reason,
            )

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="CAPA_TRANSITION",
        details=f"Transitioned CAPA (ID: {capa.id}) status from '{current_status}' to '{target_status}'.",
        record_id=capa.id,
        change_reason=change_reason,
    )

    return map_capa_to_response(capa)


@app.put("/api/v1/quality/capas/{id}", response_model=CAPAResponse)
async def update_capa(
    request: Request,
    id: str,
    payload: CAPAUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> CAPAResponse:
    """
    Update non-status attributes of a CAPA record. Disallowed once terminal (CLOSED/CANCELLED).
    """
    authorize_quality_write(request)
    user_id, user_role, change_reason = get_user_context(request)
    if not change_reason:
        raise HTTPException(
            status_code=403, detail="Missing change justification reason"
        )

    stmt_capa = select(CAPARecord).where(CAPARecord.id == id)
    result_capa = await session.execute(stmt_capa)
    capa = result_capa.scalars().first()

    if not capa:
        raise HTTPException(
            status_code=404, detail=f"CAPA record with ID '{id}' not found."
        )

    # 1. Validate version mismatch for optimistic concurrency
    if (
        payload.version_index is not None
        and capa.version_index != payload.version_index
    ):
        raise HTTPException(
            status_code=409,
            detail=f"Version conflict: The CAPA has been modified by another process. Current version: {capa.version_index}.",
        )

    # 2. Reject modifications in terminal states
    if capa.status in (CAPAStatus.CLOSED, CAPAStatus.CANCELLED):
        raise HTTPException(
            status_code=422,
            detail=f"Cannot update CAPA record because it is in terminal state '{capa.status}'.",
        )

    # 3. Apply updates
    if payload.action_plan is not None:
        capa.action_plan = payload.action_plan
    if payload.preventive_measures is not None:
        capa.preventive_measures = payload.preventive_measures
    if payload.target_completion_date is not None:
        capa.target_completion_date = payload.target_completion_date

    capa.version_index += 1
    capa.reason_for_change = change_reason

    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="CAPA_UPDATE",
        details=f"Updated CAPA record details (ID: {capa.id}).",
        record_id=capa.id,
        change_reason=change_reason,
    )

    return map_capa_to_response(capa)


class AuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    details: str
    record_id: Optional[str] = None
    change_reason: Optional[str] = None


@app.get("/api/v1/quality/audit-logs", response_model=List[AuditLogResponse])
async def list_audit_logs(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> List[AuditLogResponse]:
    """
    Retrieve quality audit logs in descending chronological order.
    """
    user_id, user_role, change_reason = get_user_context(request)

    stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
    result = await session.execute(stmt)
    logs = result.scalars().all()

    # Log listing action
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="AUDIT_LOG_LIST",
        details="Listed quality audit logs.",
    )

    return [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat() if log.timestamp else None,
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            details=log.details,
            record_id=log.record_id,
            change_reason=log.change_reason,
        )
        for log in logs
    ]
