import os
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Dict, List, Optional

from fastapi import BackgroundTasks, Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.auth import (
    has_subject_role,
    require_staff_role,
    verify_subject_bulk_identity,
    verify_subject_identity,
)
from apps.interop.database import db_manager
from apps.interop.fhir_adapter import FHIRAdapter
from apps.interop.models import (
    Base,
    ClinicalQuery,
    EPROSubmission,
    EPROSubmissionDefeated,
    Instrument,
    InteropAuditLog,
    SubjectAssignment,
    SubjectNotification,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("INTEROP_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - FHIR / eSource & eCOA Sync Gateway",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


get_db_session = DatabaseSessionDependency(db_manager)


# Helper to secure and log actions to the audit ledger
async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str,
    action: str,
    details: str,
    change_reason: Optional[str] = None,
) -> None:
    """
    Utility function to write to the immutable interop audit ledger.
    """
    log_entry = InteropAuditLog(
        user_id=user_id,
        user_role=user_role,
        action=action,
        details=details,
        change_reason=change_reason,
    )
    session.add(log_entry)
    await session.flush()


# Pydantic models for FHIR & ePRO
class FHIRPrefillRequest(BaseModel):
    """
    Payload for pre-filling CDASH fields using a FHIR bundle.
    """

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    bundle: Dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )


class ConflictStrategy(str, Enum):
    """
    Explicit validated conflict resolution strategies.
    """

    CLIENT_WINS = "CLIENT_WINS"
    SERVER_WINS = "SERVER_WINS"
    MERGE = "MERGE"


class OfflineSyncMarkers(BaseModel):
    """
    Offline queue reconciliation and conflict resolution parameters.
    """

    sequence_number: int = Field(
        ..., description="The queue order sequence from device"
    )
    client_id: str = Field(..., description="Unique identifier for the mobile device")
    conflict_strategy: ConflictStrategy = Field(
        ConflictStrategy.CLIENT_WINS,
        description="Conflict strategy to resolve duplicate submissions. Supported: CLIENT_WINS, SERVER_WINS, MERGE",
    )


class EPROSubmissionPayload(BaseModel):
    """
    A single participant ePRO/eCOA diary submission.
    """

    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: Dict[str, Any] = Field(
        ..., description="The questionnaire response key-values"
    )
    offline_sync_markers: OfflineSyncMarkers = Field(
        ..., description="The offline sync queue conflict tracking parameters"
    )


class BulkSyncPayload(BaseModel):
    """
    A bulk list of ePRO submissions for offline queue reconciliation.
    """

    submissions: List[EPROSubmissionPayload] = Field(
        ..., description="A list of queued ePRO submissions"
    )


# Helper to resolve ePRO submission conflicts
async def resolve_and_save_submission(
    session: AsyncSession,
    payload: EPROSubmissionPayload,
    user_id: str = "system",
    user_role: str = "system",
    change_reason: Optional[str] = None,
) -> Dict[str, Any]:
    """
    Save a new ePRO submission or reconcile existing ones based on conflict strategy.
    Detects structural conflicts and turn them into auditable clinical queries.
    """
    # Check for missing target records (structural conflict)
    # 1. Check if Instrument exists
    stmt_inst = select(Instrument).where(Instrument.id == payload.diary_id)
    res_inst = await session.execute(stmt_inst)
    inst = res_inst.scalars().first()

    # 2. Check if SubjectAssignment exists
    stmt_assign = select(SubjectAssignment).where(
        SubjectAssignment.subject_id == payload.subject_id,
        SubjectAssignment.instrument_id == payload.diary_id,
    )
    res_assign = await session.execute(stmt_assign)
    assign = res_assign.scalars().first()

    if not inst or not assign:
        # We have a structural conflict!
        markers_dict = payload.offline_sync_markers.model_dump(mode="json")

        # 1. Reject direct structural-conflict updates (so we do not write to EPROSubmission)
        # 2. Retain reviewable state: persist incoming payload in EPROSubmissionDefeated
        defeated_sub = EPROSubmissionDefeated(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            offline_sync_markers=markers_dict,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        # 3. Create an OPEN clinical query with audit reason "SYSTEM SYNC EXCEPTION TRIGGERED"
        query = ClinicalQuery(
            study_id="SYSTEM-SYNC",  # Sensible default
            subject_id=payload.subject_id,
            test_code=payload.diary_id,
            status="OPEN",
            explanation=f"Structural conflict: target record (Instrument or Assignment) is missing or deleted for Subject {payload.subject_id} and Diary {payload.diary_id}.",
        )
        session.add(query)
        await session.flush()

        # 4. Create an InteropAuditLog record via write_audit_log
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_STRUCTURAL_CONFLICT",
            details=f"Structural conflict on Subject '{payload.subject_id}', Diary '{payload.diary_id}': Target record missing or deleted.",
            change_reason="SYSTEM SYNC EXCEPTION TRIGGERED",
        )

        # Return the generated query in the sync result
        return {
            "status": "STRUCTURAL_CONFLICT",
            "query": {
                "id": query.id,
                "study_id": query.study_id,
                "subject_id": query.subject_id,
                "test_code": query.test_code,
                "status": query.status,
                "explanation": query.explanation,
            },
        }

    # Normal sync flow:
    # Look for an existing submission with same subject_id and diary_id
    stmt = (
        select(EPROSubmission)
        .where(EPROSubmission.subject_id == payload.subject_id)
        .where(EPROSubmission.diary_id == payload.diary_id)
    )
    result = await session.execute(stmt)
    existing: Optional[EPROSubmission] = result.scalars().first()

    strategy = payload.offline_sync_markers.conflict_strategy
    if isinstance(strategy, ConflictStrategy):
        strategy = strategy.value
    strategy = strategy.upper()
    if strategy not in ("CLIENT_WINS", "SERVER_WINS", "MERGE"):
        strategy = "CLIENT_WINS"

    markers_dict = payload.offline_sync_markers.model_dump(mode="json")

    if not existing:
        # Easy case, no conflict
        new_sub = EPROSubmission(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            offline_sync_markers=markers_dict,
            sync_status="RESOLVED",
            version_index=1,
        )
        session.add(new_sub)
        await session.flush()

        # Ensure each reconciliation outcome creates an InteropAuditLog record via write_audit_log,
        # including decision and version increment.
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details="Decision: CREATED. Version index is 1.",
            change_reason=change_reason,
        )

        return {
            "status": "CREATED",
            "id": new_sub.id,
            "subject_id": new_sub.subject_id,
            "diary_id": new_sub.diary_id,
            "answers": new_sub.answers,
            "sync_status": new_sub.sync_status,
            "version_index": new_sub.version_index,
        }

    # Conflict scenario!
    if strategy == "CLIENT_WINS":
        # Overwrite with incoming.
        # Persist both winning and defeated inputs and record the status Defeated by online-merge conflict resolution.
        # Winning is incoming (payload.answers). Defeated is existing (existing.answers).
        defeated_sub = EPROSubmissionDefeated(
            subject_id=existing.subject_id,
            diary_id=existing.diary_id,
            device_timestamp=existing.device_timestamp,
            answers=existing.answers,
            offline_sync_markers=existing.offline_sync_markers,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        existing.answers = payload.answers
        existing.device_timestamp = payload.device_timestamp
        existing.offline_sync_markers = markers_dict
        existing.version_index += 1
        existing.sync_status = "RESOLVED"
        session.add(existing)
        await session.flush()

        # Ensure each reconciliation outcome creates an InteropAuditLog record via write_audit_log,
        # including decision and version increment.
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=f"Decision: CLIENT_WINS. Version incremented to {existing.version_index}.",
            change_reason=change_reason,
        )

        return {
            "status": "UPDATED_CLIENT_WINS",
            "id": existing.id,
            "subject_id": existing.subject_id,
            "diary_id": existing.diary_id,
            "answers": existing.answers,
            "sync_status": existing.sync_status,
            "version_index": existing.version_index,
        }

    elif strategy == "SERVER_WINS":
        # Keep existing, store incoming as ignored/archived under conflict status
        # Winning is existing. Defeated is incoming (payload.answers).
        defeated_sub = EPROSubmissionDefeated(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            offline_sync_markers=markers_dict,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        conflict_sub = EPROSubmission(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            offline_sync_markers=markers_dict,
            sync_status="CONFLICT_IGNORED",
            version_index=1,
        )
        session.add(conflict_sub)
        await session.flush()

        # Ensure each reconciliation outcome creates an InteropAuditLog record via write_audit_log,
        # including decision and version increment.
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=f"Decision: SERVER_WINS. Version index is {existing.version_index}.",
            change_reason=change_reason,
        )

        return {
            "status": "IGNORED_SERVER_WINS",
            "id": existing.id,
            "subject_id": existing.subject_id,
            "diary_id": existing.diary_id,
            "answers": existing.answers,
            "sync_status": "RESOLVED",
            "version_index": existing.version_index,
        }

    elif strategy == "MERGE":
        # Merge dictionaries (client overrides server for identical keys)
        # Winning: the merged dictionary. Defeated: the existing dictionary (as it gets overwritten/modified)
        defeated_sub = EPROSubmissionDefeated(
            subject_id=existing.subject_id,
            diary_id=existing.diary_id,
            device_timestamp=existing.device_timestamp,
            answers=existing.answers,
            offline_sync_markers=existing.offline_sync_markers,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        merged_answers = existing.answers.copy()
        merged_answers.update(payload.answers)

        existing.answers = merged_answers
        existing.device_timestamp = payload.device_timestamp
        existing.offline_sync_markers = markers_dict
        existing.version_index += 1
        existing.sync_status = "RESOLVED"
        session.add(existing)
        await session.flush()

        # Ensure each reconciliation outcome creates an InteropAuditLog record via write_audit_log,
        # including decision and version increment.
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=f"Decision: MERGE. Version incremented to {existing.version_index}.",
            change_reason=change_reason,
        )

        return {
            "status": "MERGED",
            "id": existing.id,
            "subject_id": existing.subject_id,
            "diary_id": existing.diary_id,
            "answers": existing.answers,
            "sync_status": existing.sync_status,
            "version_index": existing.version_index,
        }

    return {"status": "ERROR", "detail": "Unhandled conflict resolution state"}


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "interop"}


@app.post("/api/v1/interop/fhir/prefill")
async def fhir_prefill(
    request: Request,
    payload: FHIRPrefillRequest,
    session: AsyncSession = Depends(get_db_session),
) -> Dict[str, Any]:
    """
    Ingest a standard FHIR Bundle payload, pseudonymize Patient ID,
    strip all Direct Identifiers (PII), and return mapped CDASH eCRF fields.
    """
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # Map FHIR using adapter
    adapter = FHIRAdapter(payload.study_id)
    result = adapter.parse_bundle(payload.bundle)

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="FHIR_PREFILL",
        details=f"Parsed FHIR Bundle for study '{payload.study_id}'. Pseudonymized Subject: '{result['subject_pseudonym']}'.",
        change_reason=change_reason,
    )

    return result


@app.post("/api/v1/interop/epro/submit", status_code=201)
async def epro_submit(
    request: Request,
    payload: EPROSubmissionPayload,
    session: AsyncSession = Depends(get_db_session),
) -> Dict[str, Any]:
    """
    Secure REST endpoint for mobile apps to submit a single participant diary/survey entry.
    Handles offline queue reconciliation & conflict resolution on duplicate sync requests.
    """
    verify_subject_identity(request, payload.subject_id)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # Process and resolve conflict
    resolved = await resolve_and_save_submission(
        session,
        payload,
        user_id=user_id,
        user_role=user_roles,
        change_reason=change_reason,
    )

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EPRO_SUBMIT",
        details=f"Processed ePRO submission for Subject '{payload.subject_id}', Diary '{payload.diary_id}'. Result: {resolved['status']}.",
        change_reason=change_reason,
    )

    return resolved


@app.post("/api/v1/interop/epro/sync", status_code=200)
async def epro_sync(
    request: Request,
    payload: BulkSyncPayload,
    session: AsyncSession = Depends(get_db_session),
) -> Dict[str, Any]:
    """
    Secure bulk sync endpoint for offline queues. Performs reconciliation
    and conflict resolution across multiple participant submissions.
    """
    verify_subject_bulk_identity(
        request, [sub.subject_id for sub in payload.submissions]
    )
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    results = []
    created_count = 0
    updated_count = 0
    ignored_count = 0
    conflict_count = 0

    for sub_payload in payload.submissions:
        resolved = await resolve_and_save_submission(
            session,
            sub_payload,
            user_id=user_id,
            user_role=user_roles,
            change_reason=change_reason,
        )
        results.append(resolved)
        status = resolved["status"]
        if status == "CREATED":
            created_count += 1
        elif status in ("UPDATED_CLIENT_WINS", "MERGED"):
            updated_count += 1
        elif status == "IGNORED_SERVER_WINS":
            ignored_count += 1
        elif status == "STRUCTURAL_CONFLICT":
            conflict_count += 1

    # Log bulk sync to audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EPRO_BULK_SYNC",
        details=f"Processed bulk ePRO sync containing {len(payload.submissions)} items. Created: {created_count}, Reconciled/Updated: {updated_count}, Ignored: {ignored_count}, Structural Conflicts: {conflict_count}.",
        change_reason=change_reason,
    )

    return {
        "status": "success",
        "processed_count": len(payload.submissions),
        "created_count": created_count,
        "updated_count": updated_count,
        "ignored_count": ignored_count,
        "conflict_count": conflict_count,
        "results": results,
    }


# Instrument and SubjectAssignment Pydantic Schemas
class InstrumentCreate(BaseModel):
    name: str = Field(..., description="The name of the questionnaire/diary")
    description: Optional[str] = Field(None, description="Optional description")
    items: Dict[str, Any] = Field(..., description="Items/questions")
    response_types: Dict[str, Any] = Field(
        ..., description="Response types and options"
    )
    scoring_metadata: Dict[str, Any] = Field(..., description="Scoring metadata")
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class InstrumentResponse(BaseModel):
    id: str
    name: str
    description: Optional[str]
    items: Dict[str, Any]
    response_types: Dict[str, Any]
    scoring_metadata: Dict[str, Any]
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class SubjectAssignmentCreate(BaseModel):
    subject_id: str = Field(..., description="Unique subject identifier")
    instrument_id: str = Field(..., description="ID of the Instrument to assign")
    start_date: datetime = Field(..., description="Start of the due/recurrence window")
    end_date: datetime = Field(..., description="End of the due/recurrence window")
    recurrence_pattern: Optional[str] = Field(None, description="E.g., DAILY, WEEKLY")
    due_at: Optional[datetime] = Field(
        None, description="Optional specific due date/time"
    )
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


class SubjectAssignmentResponse(BaseModel):
    id: str
    subject_id: str
    instrument_id: str
    start_date: datetime
    end_date: datetime
    recurrence_pattern: Optional[str]
    due_at: Optional[datetime]
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int


class AssignmentComplianceDetail(BaseModel):
    assignment_id: str
    instrument_id: str
    instrument_name: str
    status: str  # "COMPLETED", "PENDING", "OVERDUE"
    due_at: Optional[datetime]
    end_date: datetime
    submitted_at: Optional[datetime] = None


class SubjectComplianceResponse(BaseModel):
    subject_id: str
    compliance_rate: float  # completed / total assignments * 100.0
    completed_count: int
    pending_count: int
    overdue_count: int
    assignments: List[AssignmentComplianceDetail]


class SubjectNotificationResponse(BaseModel):
    id: str
    subject_id: str
    assignment_id: Optional[str]
    due_at: datetime
    channel: str
    delivery_status: str
    is_read: bool
    read_at: Optional[datetime]
    created_at: datetime
    created_by: str
    reason_for_change: str
    version_index: int

    class Config:
        from_attributes = True


class AcknowledgeNotificationRequest(BaseModel):
    reason_for_change: str = Field(
        ..., description="21 CFR Part 11 compliant reason for change"
    )


@app.post(
    "/api/v1/interop/instruments", response_model=InstrumentResponse, status_code=201
)
async def create_instrument(
    request: Request,
    payload: InstrumentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> InstrumentResponse:
    """
    Author a new eCOA questionnaire/diary definition.
    Enforces staff role authentication and Part 11 auditing.
    """
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    instrument = Instrument(
        name=payload.name,
        description=payload.description,
        items=payload.items,
        response_types=payload.response_types,
        scoring_metadata=payload.scoring_metadata,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(instrument)
    await session.flush()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_INSTRUMENT",
        details=f"Created instrument '{payload.name}' with ID '{instrument.id}'.",
        change_reason=change_reason,
    )

    return instrument


@app.post(
    "/api/v1/interop/assignments",
    response_model=SubjectAssignmentResponse,
    status_code=201,
)
async def create_subject_assignment(
    request: Request,
    payload: SubjectAssignmentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> SubjectAssignmentResponse:
    """
    Assign an eCOA instrument to a subject with due/recurrence window data.
    Enforces staff role authorization, validates instrument reference, and logs audit fields.
    """
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Validate that instrument exists
    stmt = select(Instrument).where(Instrument.id == payload.instrument_id)
    result = await session.execute(stmt)
    instrument = result.scalars().first()
    if not instrument:
        raise HTTPException(
            status_code=404,
            detail=f"Instrument with ID '{payload.instrument_id}' not found.",
        )

    # Validate start_date <= end_date
    if payload.start_date > payload.end_date:
        raise HTTPException(
            status_code=400,
            detail="Assignment start_date cannot be after end_date.",
        )

    assignment = SubjectAssignment(
        subject_id=payload.subject_id,
        instrument_id=payload.instrument_id,
        start_date=payload.start_date,
        end_date=payload.end_date,
        recurrence_pattern=payload.recurrence_pattern,
        due_at=payload.due_at,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=1,
    )
    session.add(assignment)
    await session.flush()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="CREATE_ASSIGNMENT",
        details=f"Assigned instrument '{instrument.name}' (ID: '{instrument.id}') to subject '{payload.subject_id}'.",
        change_reason=change_reason,
    )

    return assignment


@app.get("/api/v1/interop/instruments/{id}", response_model=InstrumentResponse)
async def get_instrument(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> InstrumentResponse:
    """
    Retrieve an Instrument definition by ID.
    Enforces subject assignment checks for Subject role.
    """
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        # Subject must be assigned this instrument to retrieve it
        stmt_assign = select(SubjectAssignment).where(
            SubjectAssignment.subject_id == user_id,
            SubjectAssignment.instrument_id == id,
        )
        res_assign = await session.execute(stmt_assign)
        if not res_assign.scalars().first():
            raise HTTPException(
                status_code=403,
                detail="Access denied: Subject is not assigned this instrument.",
            )

    stmt = select(Instrument).where(Instrument.id == id)
    result = await session.execute(stmt)
    instrument = result.scalars().first()
    if not instrument:
        raise HTTPException(
            status_code=404,
            detail=f"Instrument with ID '{id}' not found.",
        )
    return instrument


@app.get(
    "/api/v1/interop/assignments/subject/{subject_id}",
    response_model=List[SubjectAssignmentResponse],
)
async def get_subject_assignments(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[SubjectAssignmentResponse]:
    """
    Retrieve all assignments for a given subject.
    Enforces subject-scoped identity boundary (Subject can only view their own assignments).
    """
    verify_subject_identity(request, subject_id)
    stmt = select(SubjectAssignment).where(SubjectAssignment.subject_id == subject_id)
    result = await session.execute(stmt)
    assignments = result.scalars().all()
    return list(assignments)


@app.get(
    "/api/v1/interop/subjects/{subject_id}/instruments",
    response_model=List[InstrumentResponse],
)
async def get_subject_assigned_instruments(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[InstrumentResponse]:
    """
    Retrieve all unique assigned instruments for a given subject.
    Enforces subject-scoped identity boundary (Subject can only view their own instruments).
    """
    verify_subject_identity(request, subject_id)

    stmt = select(SubjectAssignment).where(SubjectAssignment.subject_id == subject_id)
    result = await session.execute(stmt)
    assignments = result.scalars().all()

    if not assignments:
        return []

    instrument_ids = {a.instrument_id for a in assignments}

    inst_stmt = select(Instrument).where(Instrument.id.in_(list(instrument_ids)))
    inst_result = await session.execute(inst_stmt)
    return list(inst_result.scalars().all())


async def deliver_notification_task(
    notification_id: str, channel: str, subject_id: str
) -> None:
    """
    Asynchronous background task to simulate notification delivery.
    Updates delivery_status to SENT or FAILED.
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        try:
            # Fetch notification
            stmt = select(SubjectNotification).where(
                SubjectNotification.id == notification_id
            )
            res = await session.execute(stmt)
            notif = res.scalars().first()
            if not notif:
                return

            message = "Reminder: eCOA assignment is due! Please complete your survey."
            if channel == "EMAIL":
                # Simulated email sending
                print(
                    f"[STUB EMAIL] Sending email to {subject_id}@example.com: {message}"
                )
            elif channel == "SMS":
                # Simulated SMS sending
                print(f"[STUB SMS] Sending SMS to +1234567890: {message}")
            elif channel == "WEBHOOK":
                # Simulated webhook delivery
                print(
                    f"[STUB WEBHOOK] Sending webhook to https://hooks.example.com/subject/{subject_id}"
                )
            elif channel == "IN_APP":
                # Delivered in-app
                print(f"[STUB IN_APP] Delivering in-app notification to {subject_id}")

            notif.delivery_status = "SENT"
            session.add(notif)
            await session.commit()
        except Exception as e:
            print(f"Error delivering notification {notification_id}: {e}")
            try:
                async with session_maker() as fail_session:
                    stmt = select(SubjectNotification).where(
                        SubjectNotification.id == notification_id
                    )
                    res = await fail_session.execute(stmt)
                    notif = res.scalars().first()
                    if notif:
                        notif.delivery_status = "FAILED"
                        fail_session.add(notif)
                        await fail_session.commit()
            except Exception as fail_err:
                print(f"Failed to record delivery failure: {fail_err}")


@app.get(
    "/api/v1/interop/subjects/{subject_id}/compliance",
    response_model=SubjectComplianceResponse,
)
async def get_subject_compliance(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> SubjectComplianceResponse:
    """
    Retrieve/compute eCOA compliance status metrics for a subject.
    """
    verify_subject_identity(request, subject_id)

    from sqlalchemy.orm import selectinload

    # Fetch all assignments and their instruments
    stmt = (
        select(SubjectAssignment)
        .where(SubjectAssignment.subject_id == subject_id)
        .options(selectinload(SubjectAssignment.instrument))
    )
    res_assigns = await session.execute(stmt)
    assignments = res_assigns.scalars().all()

    # Fetch all submissions for the subject
    stmt_subs = select(EPROSubmission).where(EPROSubmission.subject_id == subject_id)
    res_subs = await session.execute(stmt_subs)
    submissions = res_subs.scalars().all()

    # Reconcile using chronological order per instrument
    assignments_by_inst = {}
    for a in assignments:
        assignments_by_inst.setdefault(a.instrument_id, []).append(a)
    for inst_id in assignments_by_inst:
        assignments_by_inst[inst_id].sort(key=lambda x: x.start_date)

    subs_by_inst = {}
    for s in submissions:
        subs_by_inst.setdefault(s.diary_id, []).append(s)
    for inst_id in subs_by_inst:
        subs_by_inst[inst_id].sort(key=lambda x: x.device_timestamp)

    assignment_submission_map = {}
    for inst_id, inst_assigns in assignments_by_inst.items():
        inst_subs = subs_by_inst.get(inst_id, [])
        sub_idx = 0
        for assign in inst_assigns:
            if sub_idx < len(inst_subs):
                assignment_submission_map[assign.id] = inst_subs[sub_idx]
                sub_idx += 1

    # Determine status and build details list
    now = (
        datetime.now(timezone.utc).replace(tzinfo=None)
        if hasattr(timezone, "utc")
        else datetime.utcnow()
    )
    details = []
    completed_cnt = 0
    pending_cnt = 0
    overdue_cnt = 0

    for assign in assignments:
        matched_sub = assignment_submission_map.get(assign.id)
        inst_name = assign.instrument.name if assign.instrument else "Unknown"

        if matched_sub:
            status = "COMPLETED"
            submitted_at = matched_sub.device_timestamp
            completed_cnt += 1
        else:
            submitted_at = None
            threshold = assign.due_at if assign.due_at else assign.end_date
            if now > threshold:
                status = "OVERDUE"
                overdue_cnt += 1
            else:
                status = "PENDING"
                pending_cnt += 1

        details.append(
            AssignmentComplianceDetail(
                assignment_id=assign.id,
                instrument_id=assign.instrument_id,
                instrument_name=inst_name,
                status=status,
                due_at=assign.due_at,
                end_date=assign.end_date,
                submitted_at=submitted_at,
            )
        )

    total = len(assignments)
    compliance_rate = (completed_cnt / total * 100.0) if total > 0 else 100.0

    return SubjectComplianceResponse(
        subject_id=subject_id,
        compliance_rate=round(compliance_rate, 2),
        completed_count=completed_cnt,
        pending_count=pending_cnt,
        overdue_count=overdue_cnt,
        assignments=details,
    )


@app.post("/api/v1/interop/reminders/compute", status_code=200)
async def compute_reminders(
    request: Request,
    background_tasks: BackgroundTasks,
    subject_id: Optional[str] = None,
    session: AsyncSession = Depends(get_db_session),
) -> Dict[str, Any]:
    """
    Compute due reminders from assignment schedules on demand.
    Generates notifications for uncompleted assignments whose due window is reached.
    """
    if has_subject_role(request):
        user_id = getattr(request.state, "user_id", "")
        if subject_id is None:
            subject_id = user_id
        else:
            verify_subject_identity(request, subject_id)

    # Retrieve assignments
    stmt = select(SubjectAssignment)
    if subject_id:
        stmt = stmt.where(SubjectAssignment.subject_id == subject_id)
    res_assigns = await session.execute(stmt)
    assignments = res_assigns.scalars().all()

    # Retrieve submissions
    stmt_subs = select(EPROSubmission)
    if subject_id:
        stmt_subs = stmt_subs.where(EPROSubmission.subject_id == subject_id)
    res_subs = await session.execute(stmt_subs)
    submissions = res_subs.scalars().all()

    # Reconcile compliance
    assignments_by_sub_inst = {}
    for a in assignments:
        key = (a.subject_id, a.instrument_id)
        assignments_by_sub_inst.setdefault(key, []).append(a)
    for key in assignments_by_sub_inst:
        assignments_by_sub_inst[key].sort(key=lambda x: x.start_date)

    subs_by_sub_inst = {}
    for s in submissions:
        key = (s.subject_id, s.diary_id)
        subs_by_sub_inst.setdefault(key, []).append(s)
    for key in subs_by_sub_inst:
        subs_by_sub_inst[key].sort(key=lambda x: x.device_timestamp)

    completed_assignment_ids = set()
    for key, inst_assigns in assignments_by_sub_inst.items():
        inst_subs = subs_by_sub_inst.get(key, [])
        sub_idx = 0
        for assign in inst_assigns:
            if sub_idx < len(inst_subs):
                completed_assignment_ids.add(assign.id)
                sub_idx += 1

    # Check due assignments
    now = (
        datetime.now(timezone.utc).replace(tzinfo=None)
        if hasattr(timezone, "utc")
        else datetime.utcnow()
    )
    created_count = 0

    stmt_notifs = select(SubjectNotification)
    if subject_id:
        stmt_notifs = stmt_notifs.where(SubjectNotification.subject_id == subject_id)
    res_notifs = await session.execute(stmt_notifs)
    existing_notifs = res_notifs.scalars().all()
    existing_keys = {
        (n.assignment_id, n.channel) for n in existing_notifs if n.assignment_id
    }

    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(
        request.state, "change_reason", "Compute due reminders on demand"
    )

    channels = ["EMAIL", "SMS", "WEBHOOK", "IN_APP"]

    for assign in assignments:
        if assign.id in completed_assignment_ids:
            continue

        threshold = assign.due_at if assign.due_at else assign.end_date
        if now >= threshold:
            for channel in channels:
                if (assign.id, channel) in existing_keys:
                    continue

                new_notif = SubjectNotification(
                    subject_id=assign.subject_id,
                    assignment_id=assign.id,
                    due_at=threshold,
                    channel=channel,
                    delivery_status="PENDING",
                    is_read=False,
                    created_by=user_id,
                    reason_for_change="Automated due reminder generation",
                    version_index=1,
                )
                session.add(new_notif)
                await session.flush()

                background_tasks.add_task(
                    deliver_notification_task,
                    new_notif.id,
                    channel,
                    assign.subject_id,
                )
                created_count += 1

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="COMPUTE_REMINDERS",
        details=f"Computed reminders for subject_id='{subject_id}'. Created {created_count} new notifications.",
        change_reason=change_reason,
    )

    return {
        "status": "success",
        "created_count": created_count,
        "detail": f"Generated {created_count} new reminders.",
    }


@app.get(
    "/api/v1/interop/subjects/{subject_id}/notifications",
    response_model=List[SubjectNotificationResponse],
)
async def get_subject_notifications(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[SubjectNotificationResponse]:
    """
    Retrieve all notifications for a given subject.
    Enforces subject-scoped identity boundary (Subject can only view their own notifications).
    """
    verify_subject_identity(request, subject_id)

    stmt = select(SubjectNotification).where(
        SubjectNotification.subject_id == subject_id
    )
    result = await session.execute(stmt)
    notifications = result.scalars().all()
    return list(notifications)


@app.post(
    "/api/v1/interop/notifications/{notification_id}/acknowledge",
    response_model=SubjectNotificationResponse,
)
async def acknowledge_notification(
    request: Request,
    notification_id: str,
    payload: AcknowledgeNotificationRequest,
    session: AsyncSession = Depends(get_db_session),
) -> SubjectNotificationResponse:
    """
    Acknowledge/read a notification.
    Enforces subject-scoped authorization and records an audit log.
    """
    stmt = select(SubjectNotification).where(SubjectNotification.id == notification_id)
    result = await session.execute(stmt)
    notification = result.scalars().first()

    if not notification:
        raise HTTPException(
            status_code=404,
            detail=f"Notification with ID '{notification_id}' not found.",
        )

    verify_subject_identity(request, notification.subject_id)

    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = payload.reason_for_change or getattr(
        request.state, "change_reason", "system_operation"
    )

    now = (
        datetime.now(timezone.utc).replace(tzinfo=None)
        if hasattr(timezone, "utc")
        else datetime.utcnow()
    )
    notification.is_read = True
    notification.read_at = now
    notification.version_index += 1
    notification.reason_for_change = change_reason

    session.add(notification)
    await session.flush()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="ACKNOWLEDGE_NOTIFICATION",
        details=f"Subject '{notification.subject_id}' acknowledged notification '{notification.id}' (channel: '{notification.channel}').",
        change_reason=change_reason,
    )

    return notification
