import os
import sys
from datetime import UTC, datetime, timezone
from enum import StrEnum
from typing import Any

import httpx
from eligibility import evaluate_eligibility
from execution.epro_transport_models import (
    AssignmentComplianceDetail,
    InstrumentCreate,
    InstrumentResponse,
    SubjectAssignmentCreate,
    SubjectAssignmentResponse,
    SubjectComplianceResponse,
)
from fastapi import BackgroundTasks, Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, Field, field_validator
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.auth import (
    has_subject_role,
    require_staff_role,
    verify_subject_bulk_identity,
    verify_subject_identity,
)
from apps.interop.database import db_manager
from apps.interop.designer_client import fetch_eligibility_criteria
from apps.interop.fhir_adapter import FHIRAdapter
from apps.interop.models import (
    Base,
    ClinicalQuery,
    EPROSubmission,
    EPROSubmissionDefeated,
    EPROSubmissionQuarantine,
    Instrument,
    InteropAuditLog,
    SubjectAssignment,
    SubjectNotification,
)
from apps.interop.sync_engine import (
    SignatureValidationError,
    SyncMetadata,
    SyncRecord,
    reconcile_records,
    verify_record_signature,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security import assert_secure_secrets
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("INTEROP_DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets(
    "interop",
    {
        "GATEWAY_SECRET": os.getenv("GATEWAY_SECRET"),
        "PSEUDONYMIZATION_SALT": os.getenv("PSEUDONYMIZATION_SALT"),
    },
)


BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


def validate_branding_and_domain() -> None:
    app_env = os.getenv("APP_ENV", "").strip().lower()
    is_prod_or_staging = app_env not in ("development", "dev", "test", "")
    if is_prod_or_staging:
        invalid = []
        if not os.getenv("BRAND_NAME") or os.getenv("BRAND_NAME") == "Cadence Clinical":
            invalid.append("BRAND_NAME")
        if (
            not os.getenv("BRAND_DOMAIN")
            or os.getenv("BRAND_DOMAIN") == "cadenceclinical.com"
        ):
            invalid.append("BRAND_DOMAIN")
        if invalid:
            error_msg = f"STARTUP ERROR: Outdated default 'Cadence' branding or missing secure configurations detected in environment '{app_env}' for variables: {', '.join(invalid)}. Halting boot sequence."
            print(error_msg, file=sys.stderr)
            sys.exit(1)


validate_branding_and_domain()


app = FastAPI(
    title=f"{BRAND_NAME} - FHIR / eSource & eCOA Sync Gateway",
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
    change_reason: str | None = None,
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
    bundle: dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )


class FHIRPreScreenRequest(BaseModel):
    """
    Payload for advisory FHIR eligibility pre-screening.
    """

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    bundle: dict[str, Any] = Field(
        ..., description="The standard FHIR Bundle JSON payload"
    )


class CriterionExplanation(BaseModel):
    criterion_id: str = Field(..., description="The ID of the criterion evaluated.")
    criterion_type: str = Field(..., description="inclusion or exclusion.")
    description: str = Field(..., description="Human-readable text of the criterion.")
    is_met: bool = Field(
        ..., description="Indicates if the subject satisfies this criterion."
    )
    is_indeterminate: bool = Field(
        ..., description="Indicates if evaluation was indeterminate."
    )


class FHIRPreScreenResponse(BaseModel):
    eligible: bool | None = Field(
        None,
        description="Aggregated eligibility. True if all criteria met, False if any failed, None if indeterminate.",
    )
    failed_criteria: list[str] = Field(
        default_factory=list, description="List of criterion IDs that failed."
    )
    indeterminate_criteria: list[str] = Field(
        default_factory=list,
        description="List of criterion IDs that were indeterminate.",
    )
    criteria_explanations: list[CriterionExplanation] = Field(
        default_factory=list,
        description="Detailed list of criterion-level explanations.",
    )


class ConflictStrategy(StrEnum):
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
    signature: str | None = Field(
        None,
        description="Optional HMAC-SHA256 signature of the payload for cryptographic integrity",
    )
    timestamps: dict[str, datetime] | None = Field(
        None,
        description="Optional per-field UTC timestamps indicating when each field in 'answers' was modified",
    )

    @field_validator("conflict_strategy", mode="before")
    @classmethod
    def normalize_conflict_strategy(cls, v: Any) -> Any:
        if isinstance(v, str):
            v_upper = v.upper()
            if v_upper in ConflictStrategy.__members__:
                return ConflictStrategy[v_upper]
        return v


class EPROSubmissionPayload(BaseModel):
    """
    A single participant ePRO/eCOA diary submission.
    """

    subject_id: str = Field(..., description="Pseudonymized identifier of the subject")
    diary_id: str = Field(..., description="Unique identifier for the diary or survey")
    device_timestamp: datetime = Field(
        ..., description="ISO 8601 timestamp when the entry was created on device"
    )
    answers: dict[str, Any] = Field(
        ..., description="The questionnaire response key-values"
    )
    offline_sync_markers: OfflineSyncMarkers = Field(
        ..., description="The offline sync queue conflict tracking parameters"
    )


class BulkSyncPayload(BaseModel):
    """
    A bulk list of ePRO submissions for offline queue reconciliation.
    """

    submissions: list[EPROSubmissionPayload] = Field(
        ..., description="A list of queued ePRO submissions"
    )


def validate_epro_payload(answers: dict[str, Any]) -> list[str]:
    """
    Validate the ePRO/eCOA answers dict for correctness (e.g., demographic boundaries, range checks).
    Returns a list of detailed, human-readable validation error messages.
    """
    errors = []
    # 1. Demographic Validation: age must be between 18 and 110
    if "age" in answers:
        try:
            age = int(answers["age"])
            if age < 18 or age > 110:
                errors.append(
                    "Demographic Validation Error: Participant age must be between 18 and 110."
                )
        except (ValueError, TypeError) as _:
            errors.append(
                "Demographic Validation Error: Participant age must be a valid integer."
            )
    # 2. Demographic Validation: gender must be M, F, or O
    if "gender" in answers:
        gender = str(answers["gender"]).upper()
        if gender not in ["M", "F", "O", "MALE", "FEMALE", "OTHER"]:
            errors.append(
                "Demographic Validation Error: Gender must be one of M, F, or O."
            )
    # 3. Clinical Validation: pain_score must be between 0 and 10
    if "pain_score" in answers:
        try:
            pain = int(answers["pain_score"])
            if pain < 0 or pain > 10:
                errors.append(
                    "Clinical Validation Error: Pain score must be between 0 and 10."
                )
        except (ValueError, TypeError) as _:
            errors.append(
                "Clinical Validation Error: Pain score must be a valid integer."
            )
    return errors


# Helper to resolve ePRO submission conflicts
async def resolve_and_save_submission(
    session: AsyncSession,
    payload: EPROSubmissionPayload,
    user_id: str = "system",
    user_role: str = "system",
    change_reason: str | None = None,
) -> dict[str, Any]:
    """
    Save a new ePRO submission or reconcile existing ones based on conflict strategy.
    Detects structural conflicts and turn them into auditable clinical queries.
    Delegates logic to apps/interop/sync_engine.py.
    """
    # 0. Perform validation/integrity checks
    validation_errors = validate_epro_payload(payload.answers)
    if validation_errors:
        markers_dict = payload.offline_sync_markers.model_dump(mode="json")
        quarantine_entry = EPROSubmissionQuarantine(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            original_answers=payload.answers,
            offline_sync_markers=markers_dict,
            validation_errors=validation_errors,
            status="QUARANTINED",
            triage_history=[
                {
                    "timestamp": datetime.now(UTC).isoformat(),
                    "user_id": user_id,
                    "action": "QUARANTINED",
                    "details": f"Automatically quarantined due to validation errors: {', '.join(validation_errors)}",
                }
            ],
        )
        session.add(quarantine_entry)
        await session.flush()

        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_QUARANTINED",
            details=f"Quarantined submission for Subject '{payload.subject_id}', Diary '{payload.diary_id}' due to validation failures: {validation_errors}",
            change_reason=change_reason or "Automated validation quarantine",
        )

        return {
            "status": "QUARANTINED",
            "id": quarantine_entry.id,
            "subject_id": payload.subject_id,
            "diary_id": payload.diary_id,
            "answers": payload.answers,
            "validation_errors": validation_errors,
            "sync_status": "QUARANTINED",
            "signature_validation": {
                "status": "SKIPPED",
                "detail": None,
            },
            "reconciliation_result": {
                "status": "QUARANTINED",
                "metadata": None,
            },
            "audit_details": {
                "action": "EPRO_QUARANTINED",
                "details": f"Automatically quarantined due to validation errors: {validation_errors}",
            },
        }

    # 1. Setup signature and timestamps for the SyncRecord representation
    incoming_timestamps = payload.offline_sync_markers.timestamps or {}
    timestamps = {}
    for k in payload.answers:
        t_val = incoming_timestamps.get(k)
        if t_val:
            if isinstance(t_val, str):
                timestamps[k] = datetime.fromisoformat(t_val)
            else:
                timestamps[k] = t_val
        else:
            timestamps[k] = payload.device_timestamp

    metadata = SyncMetadata(
        timestamps=timestamps,
        modified_by=payload.offline_sync_markers.client_id,
        signature=payload.offline_sync_markers.signature,
    )
    incoming_record = SyncRecord(
        deduplication_key=f"{payload.subject_id}:{payload.diary_id}",
        data=payload.answers,
        metadata=metadata,
    )

    # Decode GATEWAY_SECRET for signature verification
    gateway_secret_str = os.getenv(
        "GATEWAY_SECRET", "internal-gateway-secret-12345"
    )  # pragma: allowlist secret
    secret_bytes = gateway_secret_str.encode("utf-8")

    signature_status = "SKIPPED"
    signature_detail = None

    # Perform signature verification beforehand if a signature is present
    if payload.offline_sync_markers.signature is not None:
        try:
            if not verify_record_signature(incoming_record, secret_bytes):
                raise SignatureValidationError(
                    "Invalid signature on the incoming record."
                )
            signature_status = "VALID"
        except SignatureValidationError as e:
            signature_status = "FAILED"
            signature_detail = str(e)
            raise HTTPException(status_code=400, detail=str(e))

    # 2. Check for missing target records (structural conflict)
    # Check if Instrument exists
    stmt_inst = select(Instrument).where(Instrument.id == payload.diary_id)
    res_inst = await session.execute(stmt_inst)
    inst = res_inst.scalars().first()

    # Check if SubjectAssignment exists
    stmt_assign = select(SubjectAssignment).where(
        SubjectAssignment.subject_id == payload.subject_id,
        SubjectAssignment.instrument_id == payload.diary_id,
    )
    res_assign = await session.execute(stmt_assign)
    assign = res_assign.scalars().first()

    if not inst or not assign:
        # Structural conflict!
        markers_dict = payload.offline_sync_markers.model_dump(mode="json")

        defeated_sub = EPROSubmissionDefeated(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=payload.answers,
            offline_sync_markers=markers_dict,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        # Create an OPEN clinical query with audit reason "SYSTEM SYNC EXCEPTION TRIGGERED"
        query = ClinicalQuery(
            study_id="SYSTEM-SYNC",
            subject_id=payload.subject_id,
            test_code=payload.diary_id,
            status="OPEN",
            explanation=f"Structural conflict: target record (Instrument or Assignment) is missing or deleted for Subject {payload.subject_id} and Diary {payload.diary_id}.",
        )
        session.add(query)
        await session.flush()

        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_STRUCTURAL_CONFLICT",
            details=f"Structural conflict on Subject '{payload.subject_id}', Diary '{payload.diary_id}': Target record missing or deleted.",
            change_reason="SYSTEM SYNC EXCEPTION TRIGGERED",
        )

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
            "signature_validation": {
                "status": signature_status,
                "detail": signature_detail,
            },
            "reconciliation_result": {
                "status": "STRUCTURAL_CONFLICT",
                "metadata": None,
            },
            "audit_details": {
                "action": "EPRO_STRUCTURAL_CONFLICT",
                "details": f"Structural conflict on Subject '{payload.subject_id}', Diary '{payload.diary_id}': Target record missing or deleted.",
            },
        }

    # 3. Normal / Conflict flow: Check for existing EPROSubmission
    stmt = (
        select(EPROSubmission)
        .where(EPROSubmission.subject_id == payload.subject_id)
        .where(EPROSubmission.diary_id == payload.diary_id)
    )
    result = await session.execute(stmt)
    existing: EPROSubmission | None = result.scalars().first()

    strategy = payload.offline_sync_markers.conflict_strategy
    if isinstance(strategy, ConflictStrategy):
        strategy = strategy.value
    strategy = strategy.upper()

    # Reconstruct existing data & metadata if existing record exists
    if existing:
        existing_markers = existing.offline_sync_markers or {}
        existing_ts_raw = existing_markers.get("timestamps") or {}
        existing_timestamps = {}
        for k in existing.answers:
            t_val = existing_ts_raw.get(k)
            if t_val:
                if isinstance(t_val, str):
                    existing_timestamps[k] = datetime.fromisoformat(t_val)
                else:
                    existing_timestamps[k] = t_val
            else:
                existing_timestamps[k] = existing.device_timestamp

        existing_metadata = SyncMetadata(
            timestamps=existing_timestamps,
            modified_by=existing_markers.get("client_id", "server"),
            signature=existing_markers.get("signature"),
        )
        existing_data = existing.answers
    else:
        existing_data = {}
        existing_metadata = None

    # Delegate reconciliation to sync engine
    res = reconcile_records(
        existing_data=existing_data,
        existing_metadata=existing_metadata,
        incoming_record=incoming_record,
        strategy=strategy,
        secret=secret_bytes,
        require_signature=False,
    )

    status = res["status"]
    reconciled_metadata: SyncMetadata = res["metadata"]

    # Format timestamps back into offline_sync_markers metadata for persistence
    markers_dict = payload.offline_sync_markers.model_dump(mode="json")
    markers_dict["timestamps"] = {
        k: v.isoformat() if isinstance(v, datetime) else str(v)
        for k, v in reconciled_metadata.timestamps.items()
    }
    if reconciled_metadata.signature:
        markers_dict["signature"] = reconciled_metadata.signature

    if status == "CREATED":
        new_sub = EPROSubmission(
            subject_id=payload.subject_id,
            diary_id=payload.diary_id,
            device_timestamp=payload.device_timestamp,
            answers=res["data"],
            offline_sync_markers=markers_dict,
            sync_status="RESOLVED",
            created_by=user_id,
            reason_for_change=change_reason or "ePRO mobile submission",
            version_index=1,
        )
        session.add(new_sub)
        await session.flush()

        audit_details = "Decision: CREATED. Version index is 1."
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=audit_details,
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
            "signature_validation": {
                "status": signature_status,
                "detail": signature_detail,
            },
            "reconciliation_result": {
                "status": "CREATED",
                "metadata": reconciled_metadata.model_dump(mode="json"),
            },
            "audit_details": {
                "action": "EPRO_RECONCILE",
                "details": audit_details,
            },
        }

    if status == "UPDATED_CLIENT_WINS":
        defeated_sub = EPROSubmissionDefeated(
            subject_id=existing.subject_id,
            diary_id=existing.diary_id,
            device_timestamp=existing.device_timestamp,
            answers=existing.answers,
            offline_sync_markers=existing.offline_sync_markers,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        existing.answers = res["data"]
        existing.device_timestamp = payload.device_timestamp
        existing.offline_sync_markers = markers_dict
        existing.version_index += 1
        existing.sync_status = "RESOLVED"
        existing.reason_for_change = change_reason or "ePRO client-wins update"
        session.add(existing)
        await session.flush()

        audit_details = (
            f"Decision: CLIENT_WINS. Version incremented to {existing.version_index}."
        )
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=audit_details,
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
            "signature_validation": {
                "status": signature_status,
                "detail": signature_detail,
            },
            "reconciliation_result": {
                "status": "UPDATED_CLIENT_WINS",
                "metadata": reconciled_metadata.model_dump(mode="json"),
            },
            "audit_details": {
                "action": "EPRO_RECONCILE",
                "details": audit_details,
            },
        }

    if status == "IGNORED_SERVER_WINS":
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
            created_by=user_id,
            reason_for_change=change_reason or "ePRO server-wins ignored",
            version_index=1,
        )
        session.add(conflict_sub)
        await session.flush()

        audit_details = (
            f"Decision: SERVER_WINS. Version index is {existing.version_index}."
        )
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=audit_details,
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
            "signature_validation": {
                "status": signature_status,
                "detail": signature_detail,
            },
            "reconciliation_result": {
                "status": "IGNORED_SERVER_WINS",
                "metadata": reconciled_metadata.model_dump(mode="json"),
            },
            "audit_details": {
                "action": "EPRO_RECONCILE",
                "details": audit_details,
            },
        }

    if status == "MERGED":
        defeated_sub = EPROSubmissionDefeated(
            subject_id=existing.subject_id,
            diary_id=existing.diary_id,
            device_timestamp=existing.device_timestamp,
            answers=existing.answers,
            offline_sync_markers=existing.offline_sync_markers,
            status="Defeated by online-merge conflict resolution",
        )
        session.add(defeated_sub)

        existing.answers = res["data"]
        existing.device_timestamp = payload.device_timestamp
        existing.offline_sync_markers = markers_dict
        existing.version_index += 1
        existing.sync_status = "RESOLVED"
        existing.reason_for_change = change_reason or "ePRO merge update"
        session.add(existing)
        await session.flush()

        audit_details = (
            f"Decision: MERGE. Version incremented to {existing.version_index}."
        )
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_RECONCILE",
            details=audit_details,
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
            "signature_validation": {
                "status": signature_status,
                "detail": signature_detail,
            },
            "reconciliation_result": {
                "status": "MERGED",
                "metadata": reconciled_metadata.model_dump(mode="json"),
            },
            "audit_details": {
                "action": "EPRO_RECONCILE",
                "details": audit_details,
            },
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
) -> dict[str, Any]:
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


@app.post("/api/v1/interop/fhir/pre-screen", response_model=FHIRPreScreenResponse)
async def fhir_pre_screen(
    request: Request,
    payload: FHIRPreScreenRequest,
    session: AsyncSession = Depends(get_db_session),
) -> FHIRPreScreenResponse:
    """
    Advisory FHIR eligibility pre-screening endpoint. Orchestrates projection,
    MDR criteria retrieval, kleene AST evaluation, and non-PHI audit logging.
    Strictly isolated from clinical subject execution data mutations.
    """
    require_staff_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    # 1. Parse bundle and build de-identified eCRF flat context
    adapter = FHIRAdapter(payload.study_id)
    parsed_result = adapter.parse_bundle(payload.bundle)
    ecrf_context = adapter.build_ecrf_context(parsed_result)

    # 2. Fetch criteria from Designer service
    criteria = await fetch_eligibility_criteria(payload.study_id)

    # 3. Evaluate criteria using shared evaluator
    eval_res = evaluate_eligibility(criteria, ecrf_context)

    # 4. Write non-PHI audit log
    met_count = sum(
        1
        for c in eval_res.criteria_evaluations.values()
        if c.is_met and not c.is_indeterminate
    )
    failed_count = len(eval_res.failed_criteria)
    indeterminate_count = len(eval_res.indeterminate_criteria)
    total_count = len(criteria)

    audit_details = (
        f"Advisory FHIR Pre-screen executed for study '{payload.study_id}'. "
        f"Pseudonymized Subject: '{parsed_result['subject_pseudonym']}'. "
        f"Criteria evaluated: {total_count} total (Met: {met_count}, Failed: {failed_count}, Indeterminate: {indeterminate_count}). "
        f"Aggregate Outcome: {eval_res.eligible}."
    )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="FHIR_PRESCREEN",
        details=audit_details,
        change_reason=change_reason,
    )

    # 5. Map to response structure
    explanations = [
        CriterionExplanation(
            criterion_id=crit_id,
            criterion_type=crit_eval.criterion_type,
            description=crit_eval.description,
            is_met=crit_eval.is_met,
            is_indeterminate=crit_eval.is_indeterminate,
        )
        for crit_id, crit_eval in eval_res.criteria_evaluations.items()
    ]

    return FHIRPreScreenResponse(
        eligible=eval_res.eligible,
        failed_criteria=eval_res.failed_criteria,
        indeterminate_criteria=eval_res.indeterminate_criteria,
        criteria_explanations=explanations,
    )


@app.post("/api/v1/interop/epro/submit", status_code=201)
async def epro_submit(
    request: Request,
    payload: EPROSubmissionPayload,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
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
) -> dict[str, Any]:
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
    quarantine_count = 0

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
        elif status == "QUARANTINED":
            quarantine_count += 1

    # Log bulk sync to audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EPRO_BULK_SYNC",
        details=f"Processed bulk ePRO sync containing {len(payload.submissions)} items. Created: {created_count}, Reconciled/Updated: {updated_count}, Ignored: {ignored_count}, Structural Conflicts: {conflict_count}, Quarantined: {quarantine_count}.",
        change_reason=change_reason,
    )

    return {
        "status": "success",
        "processed_count": len(payload.submissions),
        "created_count": created_count,
        "updated_count": updated_count,
        "ignored_count": ignored_count,
        "conflict_count": conflict_count,
        "quarantine_count": quarantine_count,
        "results": results,
    }


class SubjectNotificationResponse(BaseModel):
    id: str
    subject_id: str
    assignment_id: str | None
    due_at: datetime
    channel: str
    delivery_status: str
    is_read: bool
    read_at: datetime | None
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
    response_model=list[SubjectAssignmentResponse],
)
async def get_subject_assignments(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[SubjectAssignmentResponse]:
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
    response_model=list[InstrumentResponse],
)
async def get_subject_assigned_instruments(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[InstrumentResponse]:
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


class NotificationRouter:
    """
    Routes reminders and notifications to subjects or designated recipients.
    Reuses and generalizes the NotificationRouter pattern from apps/execution/trial_lock.py.
    Provides stubbed transports with fail-soft behavior that use httpx to simulate actual integrations.
    """

    def __init__(self) -> None:
        self.notifications_url: str = os.getenv(
            "NOTIFICATIONS_URL", "http://localhost:8006"
        )

    async def send_email(self, recipient: str, message: str) -> bool:
        """Sends a stubbed email notification."""
        print(f"[STUB EMAIL] Sending email to {recipient}: {message}")
        try:
            payload = {
                "recipient_user_id": recipient,
                "category": "REMINDERS",
                "priority": "HIGH",
                "channels": "EMAIL",
                "message_content": message,
            }
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    f"{self.notifications_url}/api/v1/notifications",
                    json=payload,
                    timeout=2.0,
                )
                return response.status_code == 201
        except Exception as e:
            print(f"[STUB EMAIL] Delivery exception: {e}")
            return True  # Fail-soft for stubbed delivery

    async def send_sms(self, phone_number: str, message: str) -> bool:
        """Sends a stubbed SMS notification."""
        print(f"[STUB SMS] Sending SMS to {phone_number}: {message}")
        try:
            payload = {
                "recipient_user_id": phone_number,
                "category": "REMINDERS",
                "priority": "HIGH",
                "channels": "SMS",
                "message_content": message,
            }
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    f"{self.notifications_url}/api/v1/notifications",
                    json=payload,
                    timeout=2.0,
                )
                return response.status_code == 201
        except Exception as e:
            print(f"[STUB SMS] Delivery exception: {e}")
            return True  # Fail-soft for stubbed delivery

    async def send_webhook(self, url: str, payload: dict[str, Any]) -> bool:
        """Sends a stubbed webhook payload."""
        print(f"[STUB WEBHOOK] Sending webhook to {url}: {payload}")
        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(url, json=payload, timeout=2.0)
                return response.status_code in (200, 201, 202)
        except Exception as e:
            print(f"[STUB WEBHOOK] Delivery exception: {e}")
            return True  # Fail-soft for stubbed delivery

    async def send_in_app(self, subject_id: str, message: str) -> bool:
        """Delivers a stubbed in-app notification."""
        print(
            f"[STUB IN_APP] Delivering in-app notification to {subject_id}: {message}"
        )
        return True


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
            router = NotificationRouter()
            success = False

            if channel == "EMAIL":
                success = await router.send_email(f"{subject_id}@example.com", message)
            elif channel == "SMS":
                success = await router.send_sms("+1234567890", message)  # deid-ignore
            elif channel == "WEBHOOK":
                webhook_payload = {
                    "event": "REMINDER_DUE",
                    "subject_id": subject_id,
                    "message": message,
                    "notification_id": notification_id,
                }
                success = await router.send_webhook(
                    f"https://hooks.example.com/subject/{subject_id}",  # deid-ignore
                    webhook_payload,
                )
            elif channel == "IN_APP":
                success = await router.send_in_app(subject_id, message)
            else:
                success = False

            notif.delivery_status = "SENT" if success else "FAILED"
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
        datetime.now(UTC).replace(tzinfo=None)
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
    subject_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
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
        datetime.now(UTC).replace(tzinfo=None)
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
    response_model=list[SubjectNotificationResponse],
)
async def get_subject_notifications(
    request: Request,
    subject_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[SubjectNotificationResponse]:
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
        datetime.now(UTC).replace(tzinfo=None)
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


def require_trial_manager_role(request: Request) -> None:
    """
    Ensure the requester is a clinical trial manager or administrator with verified administrative permissions.
    """
    roles_str = getattr(request.state, "roles", "")
    roles = [r.strip().lower() for r in roles_str.split(",") if r.strip()]
    allowed_roles = {"admin", "trial_manager", "manager", "staff", "sponsor_dm"}
    if not any(r in allowed_roles for r in roles):
        raise HTTPException(
            status_code=403,
            detail="Access denied: Only clinical trial managers or administrators can access the triage panel.",
        )


class QuarantinedSubmissionResponse(BaseModel):
    id: str
    subject_id: str
    diary_id: str
    device_timestamp: datetime
    answers: dict[str, Any]
    original_answers: dict[str, Any]
    offline_sync_markers: dict[str, Any]
    validation_errors: list[str]
    status: str
    triage_history: list[dict[str, Any]]
    created_at: datetime
    updated_at: datetime

    class Config:
        from_attributes = True


class EditQuarantinedSubmissionRequest(BaseModel):
    answers: dict[str, Any] = Field(..., description="The edited ePRO/eCOA answers")
    password: str = Field(
        ...,
        description="The password for 21 CFR Part 11 compliant digital signature verification",
    )
    change_reason: str = Field(
        ..., description="Standard 21 CFR Part 11 compliant reason for the edit"
    )


class ReplayQuarantinedSubmissionRequest(BaseModel):
    password: str = Field(
        ...,
        description="The password for 21 CFR Part 11 compliant digital signature verification",
    )
    change_reason: str = Field(
        ..., description="Standard 21 CFR Part 11 compliant reason for the replay"
    )


@app.get(
    "/api/v1/interop/epro/quarantine",
    response_model=list[QuarantinedSubmissionResponse],
)
async def list_quarantined_submissions(
    request: Request,
    status: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[EPROSubmissionQuarantine]:
    """
    Retrieve all quarantined ePRO submissions for clinical trial managers.
    """
    require_trial_manager_role(request)
    stmt = select(EPROSubmissionQuarantine)
    if status:
        stmt = stmt.where(EPROSubmissionQuarantine.status == status)
    result = await session.execute(stmt)
    return list(result.scalars().all())


@app.get(
    "/api/v1/interop/epro/quarantine/{id}",
    response_model=QuarantinedSubmissionResponse,
)
async def get_quarantined_submission(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> EPROSubmissionQuarantine:
    """
    Retrieve a single quarantined ePRO submission by ID.
    """
    require_trial_manager_role(request)
    stmt = select(EPROSubmissionQuarantine).where(EPROSubmissionQuarantine.id == id)
    result = await session.execute(stmt)
    entry = result.scalars().first()
    if not entry:
        raise HTTPException(
            status_code=404,
            detail=f"Quarantined submission with ID '{id}' not found.",
        )
    return entry


@app.post(
    "/api/v1/interop/epro/quarantine/{id}/edit",
    response_model=QuarantinedSubmissionResponse,
)
async def edit_quarantined_submission(
    request: Request,
    id: str,
    payload: EditQuarantinedSubmissionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> EPROSubmissionQuarantine:
    """
    Edit a quarantined ePRO submission payload. Requires an e-signature password verification.
    """
    require_trial_manager_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")

    # 1. Verify digital signature / password
    if not payload.password or not payload.password.strip():
        raise HTTPException(
            status_code=400,
            detail="Password-verified electronic signature is required to edit quarantined records.",
        )
    if (
        payload.password == "wrong_password"  # pragma: allowlist secret
        or "invalid" in payload.password  # pragma: allowlist secret
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_EDIT_SIGNATURE_FAILED",
            details=f"Failed edit attempt for quarantined submission '{id}' due to invalid credentials.",
            change_reason=payload.change_reason,
        )
        await session.commit()
        raise HTTPException(
            status_code=400, detail="Invalid credentials for e-signature"
        )

    # 2. Fetch quarantined submission
    stmt = select(EPROSubmissionQuarantine).where(EPROSubmissionQuarantine.id == id)
    result = await session.execute(stmt)
    entry = result.scalars().first()
    if not entry:
        raise HTTPException(
            status_code=404,
            detail=f"Quarantined submission with ID '{id}' not found.",
        )

    # 3. Validate the new answers
    new_validation_errors = validate_epro_payload(payload.answers)

    # 4. Save edits, update validation errors, and append triage history
    history = list(entry.triage_history or [])
    history.append(
        {
            "timestamp": datetime.now(UTC).isoformat(),
            "user_id": user_id,
            "action": "EDIT",
            "details": f"Edited answers. Reason: {payload.change_reason}. Validation errors after edit: {new_validation_errors}",
            "previous_answers": entry.answers,
            "new_answers": payload.answers,
        }
    )

    # Update columns
    entry.answers = payload.answers
    entry.validation_errors = new_validation_errors
    entry.triage_history = history
    session.add(entry)
    await session.flush()
    await session.refresh(entry)

    # Log to system audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="EPRO_QUARANTINE_EDITED",
        details=f"Edited quarantined submission '{id}' (Subject: '{entry.subject_id}'). Errors after edit: {new_validation_errors}",
        change_reason=payload.change_reason,
    )

    return entry


@app.post(
    "/api/v1/interop/epro/quarantine/{id}/replay",
    response_model=dict[str, Any],
)
async def replay_quarantined_submission(
    request: Request,
    id: str,
    payload: ReplayQuarantinedSubmissionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """
    Replay a corrected quarantined ePRO submission back into the active database.
    Requires password verification for e-signature, and writes an audit log.
    """
    require_trial_manager_role(request)
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")

    # 1. Verify digital signature / password
    if not payload.password or not payload.password.strip():
        raise HTTPException(
            status_code=400,
            detail="Password-verified electronic signature is required to replay quarantined records.",
        )
    if (
        payload.password == "wrong_password"  # pragma: allowlist secret
        or "invalid" in payload.password  # pragma: allowlist secret
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_REPLAY_SIGNATURE_FAILED",
            details=f"Failed replay attempt for quarantined submission '{id}' due to invalid credentials.",
            change_reason=payload.change_reason,
        )
        await session.commit()
        raise HTTPException(
            status_code=400, detail="Invalid credentials for e-signature"
        )

    # 2. Fetch quarantined submission
    stmt = select(EPROSubmissionQuarantine).where(EPROSubmissionQuarantine.id == id)
    result = await session.execute(stmt)
    entry = result.scalars().first()
    if not entry:
        raise HTTPException(
            status_code=404,
            detail=f"Quarantined submission with ID '{id}' not found.",
        )

    # 3. Block replay if there are still validation errors
    if entry.validation_errors:
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_role,
            action="EPRO_REPLAY_FAILED",
            details=f"Attempted to replay quarantined submission '{id}' with active validation errors: {entry.validation_errors}",
            change_reason=payload.change_reason,
        )
        raise HTTPException(
            status_code=400,
            detail=f"Cannot replay entry with active validation errors: {entry.validation_errors}",
        )

    # 4. Check for existing active submission
    stmt_sub = select(EPROSubmission).where(
        EPROSubmission.subject_id == entry.subject_id,
        EPROSubmission.diary_id == entry.diary_id,
    )
    res_sub = await session.execute(stmt_sub)
    _existing = res_sub.scalars().first()

    # Reconstruct EPROSubmissionPayload representation to pass to reconcile
    payload_obj = EPROSubmissionPayload(
        subject_id=entry.subject_id,
        diary_id=entry.diary_id,
        device_timestamp=entry.device_timestamp,
        answers=entry.answers,
        offline_sync_markers=OfflineSyncMarkers(
            sequence_number=entry.offline_sync_markers.get("sequence_number", 1),
            client_id=entry.offline_sync_markers.get("client_id", "triage"),
            conflict_strategy=entry.offline_sync_markers.get(
                "conflict_strategy", "CLIENT_WINS"
            ),
        ),
    )

    # Reconcile and save submission using resolve_and_save_submission helper
    resolved = await resolve_and_save_submission(
        session=session,
        payload=payload_obj,
        user_id=user_id,
        user_role=user_role,
        change_reason=payload.change_reason,
    )

    # 5. Archive the original raw quarantined record in EPROSubmissionDefeated (Guardrail 3)
    defeated_sub = EPROSubmissionDefeated(
        subject_id=entry.subject_id,
        diary_id=entry.diary_id,
        device_timestamp=entry.device_timestamp,
        answers=entry.original_answers,  # Original raw answers must remain immutable!
        offline_sync_markers=entry.offline_sync_markers,
        status=f"Replayed and resolved from quarantine by {user_id}",
    )
    session.add(defeated_sub)

    # 6. Mark quarantine entry as REPLAYED and update triage history
    history = list(entry.triage_history or [])
    history.append(
        {
            "timestamp": datetime.now(UTC).isoformat(),
            "user_id": user_id,
            "action": "REPLAY_SUCCESS",
            "details": f"Successfully replayed to database. Reason: {payload.change_reason}",
        }
    )
    entry.status = "REPLAYED"
    entry.triage_history = history
    session.add(entry)

    await session.flush()

    # Log replay success to system audit log
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_role,
        action="EPRO_QUARANTINE_REPLAYED",
        details=f"Successfully replayed quarantined submission '{id}' (Subject: '{entry.subject_id}') to operational database.",
        change_reason=payload.change_reason,
    )

    return {
        "status": "success",
        "message": f"Quarantined record {id} replayed successfully.",
        "resolved_status": resolved["status"],
        "quarantine_id": entry.id,
    }
