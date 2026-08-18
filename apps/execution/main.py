import asyncio
import json
import os
import uuid
from collections.abc import AsyncGenerator
from contextlib import asynccontextmanager, suppress
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from fastapi import (
    BackgroundTasks,
    Depends,
    FastAPI,
    HTTPException,
    Query,
    Request,
    Response,
)
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import func, select, text

from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.coding import match_verbatim_term
from apps.execution.database.context import (
    audit_context,
    current_change_reason,
    current_user_id,
)
from apps.execution.database.core import bg_db_manager, db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import (
    AuditLog,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    CodingState,
    DictionaryImportJob,
    FormSubmission,
    FormSubmissionStatus,
    ImportState,
    MigrationRule,
    StudyAuthoredRule,
    SubjectConsent,
    SubjectRandomization,
    TranslationJob,
)
from apps.execution.database.models import (
    DictionaryType as DBDictionaryType,
)
from apps.execution.demographics import (
    decrypt_demographics as decrypt_demographics,
)
from apps.execution.demographics import (
    encrypt_demographics as encrypt_demographics,
)
from apps.execution.demographics import (
    get_safe_demographics as get_safe_demographics,
)
from apps.execution.dependencies import verify_change_justification
from apps.execution.domain.acl.protocol_version_ref_dto import (
    ProtocolVersionRefDTO,
)
from apps.execution.edit_checks import (
    run_asynchronous_edit_checks,
    run_asynchronous_form_edit_checks,
    run_synchronous_edit_checks,
)
from apps.execution.exceptions import (
    ChangeRequestNotFoundError,
    CodingAssignmentNotFoundError,
    DictionaryNotFoundError,
    InvalidChangeRequestActionError,
    InvalidCodingActionError,
    SubjectEligibilityError,
)
from apps.execution.lab_range_cache import get_active_lab_ranges, lab_range_cache
from apps.execution.outliers import recalculate_cohort_outliers
from apps.execution.routers.amendments import router as amendments_router
from apps.execution.routers.anonymization import router as anonymization_router
from apps.execution.routers.auditor import router as auditor_router
from apps.execution.routers.dictionaries import router as dictionaries_router
from apps.execution.routers.doa import router as doa_router
from apps.execution.routers.documents import router as documents_router
from apps.execution.routers.eisf import router as eisf_router
from apps.execution.routers.exports import router as exports_router
from apps.execution.routers.labs import router as labs_router
from apps.execution.routers.locks import router as locks_router
from apps.execution.routers.offline import router as offline_router
from apps.execution.routers.queries import router as queries_router
from apps.execution.routers.randomization import router as randomization_router
from apps.execution.routers.safety import router as safety_router
from apps.execution.routers.sdv import router as sdv_router
from apps.execution.routers.signatures import router as signatures_router
from apps.execution.routers.unblinding import router as unblinding_router
from apps.execution.rtsm_authz import redact_response, verify_site_access
from apps.execution.rtsm_supply import (
    InsufficientStockError,
    SiteInventoryNotFoundError,
    dispense_kit_transaction,
)
from apps.execution.subject_lifecycle import (
    InvalidStateTransitionError,
    LockedFactorMutationError,
)
from apps.execution.translator import process_translation
from apps.execution.trial_lock import TrialLockManager
from apps.execution.ucum import convert_unit, get_normalized_representation
from packages.security import (
    ROLE_CRA,
    ROLE_CRC,
    ROLE_DATA_MANAGER,
    ROLE_INVESTIGATOR,
    ROLE_SITE_INVESTIGATOR,
    ROLE_SPONSOR_ADMIN,
    Principal,
    assert_secure_secrets,
    current_ip_address,
    get_normalized_roles,
    get_principal,
    require_roles,
    validate_branding,
    verify_not_auditor,
)
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import SITE_SCOPED_ROLES, can_access_study
from packages.security.signing import generate_canonical_signature

ProtocolVersionRef = ProtocolVersionRefDTO

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")

assert_secure_secrets(
    "execution",
    {
        "GATEWAY_SECRET": os.getenv("GATEWAY_SECRET"),
        "BIOSTAT_EXPORT_SALT": os.getenv("BIOSTAT_EXPORT_SALT"),
    },
)


async def recover_orphaned_dictionary_imports(session_maker) -> None:
    """Scans and transitions active (PENDING or PROCESSING) dictionary import jobs to FAILED.

    This runs during the application's startup phase inside an isolated, short-lived database transaction
    and logs a GxP FMEA-compliant compliance report.
    """
    import logging

    logger = logging.getLogger("gxp.boot_recovery")

    logger.info(
        "🟢 [GxP Boot Recovery] Starting startup scan for active dictionary import jobs..."
    )

    try:
        with audit_context(
            user_id="background_service",
            change_reason="GxP FMEA-aligned boot recovery: Transitioning stuck active dictionary imports to FAILED on server startup",
        ):
            async with session_maker() as session:
                async with session.begin():  # Isolated database transaction
                    # Fetch all jobs in PENDING or PROCESSING states
                    stmt = select(DictionaryImportJob).where(
                        DictionaryImportJob.status.in_(
                            [ImportState.PENDING, ImportState.PROCESSING]
                        )
                    )
                    res = await session.execute(stmt)
                    orphaned_jobs = res.scalars().all()

                    if not orphaned_jobs:
                        logger.info(
                            "✅ [GxP Boot Recovery] Scan complete: Zero active/orphaned dictionary import jobs found."
                        )
                        return

                    logger.warning(
                        f"⚠️ [GxP Boot Recovery] Detected {len(orphaned_jobs)} orphaned dictionary import jobs. "
                        "Transitioning to FAILED state under dedicated background service audit context..."
                    )

                    for job in orphaned_jobs:
                        old_status = job.status
                        job.status = ImportState.FAILED
                        job.completed_at = datetime.now(UTC).replace(tzinfo=None)
                        job.error_details = (
                            "Job was interrupted by a server reboot or crash. "
                            "Transitioned to FAILED automatically on startup."
                        )
                        session.add(job)

                        # FMEA scores and Risk Priority Number (RPN) calculation
                        severity = 3  # Moderate impact since import failed but clean state is restored
                        occurrence = 1  # Low occurrence as server crashes during active import are rare
                        detectability = 2  # High/moderate detectability since status is corrected and fully audited
                        rpn = severity * occurrence * detectability  # 6 < 20 (low risk)

                        # Structured FMEA-compliant compliance report
                        logger.info(
                            f"🟢 [GxP FMEA Compliance Report] Job ID: {job.id}\n"
                            f"  - Dictionary: {job.dictionary_type.value} v{job.dictionary_version}\n"
                            f"  - Transition: {old_status} -> FAILED\n"
                            f"  - Risk Assessment (Mitigated):\n"
                            f"    * Severity: {severity}/5 (Moderate)\n"
                            f"    * Occurrence: {occurrence}/5 (Rare)\n"
                            f"    * Detectability: {detectability}/5 (High)\n"
                            f"    * Recalculated RPN: {rpn} < 20 (Low Risk, mitigation successful)\n"
                            f"  - Audit Identity: background_service"
                        )

                    # Commit will occur here automatically via begin() context manager
                    await session.flush()

        logger.info(
            "✅ [GxP Boot Recovery] Successfully transitioned orphaned jobs and closed startup recovery transaction."
        )

    except Exception as e:
        logger.error(f"❌ [GxP Boot Recovery] Error during boot recovery scan: {e}")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None]:
    """Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    is_testing = "PYTEST_CURRENT_TEST" in os.environ or "TESTING" in os.environ
    run_workers = os.getenv("RUN_BACKGROUND_WORKERS", "true").lower() == "true"

    if not is_testing:
        # Initialize shared database library
        db_manager.init_db(DATABASE_URL)

        # Run GxP FMEA-Aligned Boot Recovery for dictionary imports
        await recover_orphaned_dictionary_imports(db_manager.get_session_maker())

        # Start the background ledger sealer
        from apps.execution.database.sealer import (
            start_background_sealer,
            stop_background_sealer,
        )
        from apps.execution.queries_escalation import (
            start_background_query_escalation,
            stop_background_query_escalation,
        )
        from apps.execution.workers.consent_subscriber import (
            start_consent_subscriber,
            stop_consent_subscriber,
        )
        from apps.execution.workers.outbox_worker import (
            start_outbox_worker,
            stop_outbox_worker,
        )

        if run_workers:
            # Initialize isolated background database session manager with dedicated pool
            bg_pool_kwargs = {}
            if not DATABASE_URL.startswith("sqlite"):
                bg_pool_kwargs = {
                    "pool_size": 5,
                    "max_overflow": 5,
                    "pool_timeout": 30,
                    "pool_pre_ping": True,
                }
            bg_db_manager.init_db(DATABASE_URL, **bg_pool_kwargs)

            await start_background_sealer(bg_db_manager.get_session_maker())
            await start_background_query_escalation(bg_db_manager.get_session_maker())
            start_outbox_worker(bg_db_manager.get_session_maker())
            start_consent_subscriber(bg_db_manager.get_session_maker())

    yield

    if not is_testing:
        # Stop background ledger sealer
        await stop_background_sealer()
        # Stop background query escalation
        await stop_background_query_escalation()
        # Stop background outbox worker
        stop_outbox_worker()
        # Stop background consent subscriber worker
        stop_consent_subscriber()
        # Cleanup background database connection
        await bg_db_manager.close()
        # Cleanup database connection
        await db_manager.close()


class InvalidParam(BaseModel):
    field: str | None = None
    reason: str | None = None
    value: str | None = None


class ProblemDetails(BaseModel):
    type: str
    title: str
    status: int
    detail: str
    instance: str
    code: str
    invalid_params: list[InvalidParam] | None = None


BRAND_NAME = os.getenv("BRAND_NAME", "Cadence Clinical")


validate_branding("execution")
app = FastAPI(
    title=f"{BRAND_NAME} - EDC Execution Engine", version="0.1.0", lifespan=lifespan
)

app.include_router(locks_router)
app.include_router(signatures_router)
app.include_router(amendments_router)
app.include_router(auditor_router)
app.include_router(safety_router)
app.include_router(eisf_router)
app.include_router(anonymization_router)
app.include_router(doa_router)
app.include_router(offline_router)
app.include_router(documents_router)
app.include_router(sdv_router)
app.include_router(randomization_router)
app.include_router(unblinding_router)
app.include_router(queries_router)
app.include_router(dictionaries_router)
app.include_router(exports_router)
app.include_router(labs_router)


@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    """Convert request validation errors into a standardized HTTP 400 problem-details response.

    Returns:
        JSONResponse: A 400 response containing details for each invalid request parameter.
    """
    validation_errors_list = []
    for err in exc.errors():
        loc_path = err.get("loc", [])
        field_path = (
            " -> ".join(str(item) for item in loc_path) if loc_path else "unknown"
        )
        msg = err.get("msg", "Validation error")
        val = err.get("input")
        val_str = str(val) if val is not None else ""
        validation_errors_list.append(
            InvalidParam(field=field_path, reason=msg, value=val_str)
        )
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/validation-failed",
        title="Request Validation Failed",
        status=400,
        detail="The request body fails to satisfy schema rules. Refer to 'invalid_params' for details.",
        instance=request.url.path,
        code="REQUEST_VALIDATION_ERROR",
        invalid_params=validation_errors_list,
    )
    return JSONResponse(status_code=400, content=problem.model_dump(exclude_none=True))


class AuthorizationDeniedError(Exception):
    """Domain exception raised when an authenticated principal lacks the
    required permission to perform an action.

    This exception is distinct from ``PermissionError`` (which is the built-in
    ``OSError`` subclass for filesystem/OS permission failures) and is used
    exclusively for application-level authorization denials.  Raising this
    exception routes through ``authorization_denied_handler``, which returns a
    generic HTTP 403 response without leaking internal details.
    """


@app.exception_handler(AuthorizationDeniedError)
async def authorization_denied_handler(
    request: Request, exc: AuthorizationDeniedError
) -> JSONResponse:
    """Convert an application-level authorization denial into an HTTP 403 response.

    Returns a static, non-revealing detail string so that neither filesystem
    paths nor internal exception messages are exposed to the caller.

    Args:
        request: The inbound HTTP request that triggered the authorization check.
        exc: The ``AuthorizationDeniedError`` raised by the application layer.

    Returns:
        JSONResponse: A 403 response with a safe ``detail`` field.
    """
    return JSONResponse(
        status_code=403,
        content={
            "detail": "Forbidden: you do not have permission to perform this action."
        },
    )


@app.exception_handler(SubjectEligibilityError)
async def subject_eligibility_error_handler(
    request: Request, exc: SubjectEligibilityError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/eligibility-violation",
        title="Subject Eligibility Violation",
        status=400,
        detail=str(exc),
        instance=request.url.path,
        code="SUBJECT_ELIGIBILITY_ERROR",
    )
    return JSONResponse(status_code=400, content=problem.model_dump(exclude_none=True))


@app.exception_handler(CodingAssignmentNotFoundError)
async def coding_assignment_not_found_handler(
    request: Request, exc: CodingAssignmentNotFoundError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/coding-assignment-not-found",
        title="Coding Assignment Not Found",
        status=404,
        detail=str(exc),
        instance=request.url.path,
        code="CODING_ASSIGNMENT_NOT_FOUND",
    )
    return JSONResponse(status_code=404, content=problem.model_dump(exclude_none=True))


@app.exception_handler(InvalidCodingActionError)
async def invalid_coding_action_handler(
    request: Request, exc: InvalidCodingActionError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/invalid-coding-action",
        title="Invalid Coding Action",
        status=400,
        detail=str(exc),
        instance=request.url.path,
        code="INVALID_CODING_ACTION",
    )
    return JSONResponse(status_code=400, content=problem.model_dump(exclude_none=True))


@app.exception_handler(DictionaryNotFoundError)
async def dictionary_not_found_handler(
    request: Request, exc: DictionaryNotFoundError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/dictionary-not-found",
        title="Dictionary Not Found",
        status=404,
        detail=str(exc),
        instance=request.url.path,
        code="DICTIONARY_NOT_FOUND",
    )
    return JSONResponse(status_code=404, content=problem.model_dump(exclude_none=True))


@app.exception_handler(ChangeRequestNotFoundError)
async def change_request_not_found_handler(
    request: Request, exc: ChangeRequestNotFoundError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/change-request-not-found",
        title="Change Request Not Found",
        status=404,
        detail=str(exc),
        instance=request.url.path,
        code="CHANGE_REQUEST_NOT_FOUND",
    )
    return JSONResponse(status_code=404, content=problem.model_dump(exclude_none=True))


@app.exception_handler(InvalidChangeRequestActionError)
async def invalid_change_request_action_handler(
    request: Request, exc: InvalidChangeRequestActionError
) -> JSONResponse:
    brand_domain = os.getenv("BRAND_DOMAIN", "ccrsoft.com")
    problem = ProblemDetails(
        type=f"https://api.{brand_domain}/errors/invalid-change-request-action",
        title="Invalid Change Request Action",
        status=400,
        detail=str(exc),
        instance=request.url.path,
        code="INVALID_CHANGE_REQUEST_ACTION",
    )
    return JSONResponse(status_code=400, content=problem.model_dump(exclude_none=True))


@app.exception_handler(LockedFactorMutationError)
async def locked_factor_mutation_handler(
    request: Request, exc: LockedFactorMutationError
) -> JSONResponse:
    return JSONResponse(
        status_code=422,
        content={"detail": "LOCKED_FACTOR_MUTATION"},
    )


@app.exception_handler(InvalidStateTransitionError)
async def invalid_state_transition_handler(
    request: Request, exc: InvalidStateTransitionError
) -> JSONResponse:
    return JSONResponse(
        status_code=400,
        content={"detail": "INVALID_STATE_TRANSITION"},
    )


app.add_middleware(ContextResetMiddleware)
app.add_middleware(GatewayAuthMiddleware)


@app.get("/health")
async def health_check() -> dict[str, str]:
    """Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "execution"}


class StudyEvent(BaseModel):
    """Pydantic model representing an incoming study publication event.

    Attributes:
        study_id (str): The unique identifier of the study.
        payload (dict[str, Any]): The raw USDM protocol payload.
    """

    study_id: str
    payload: dict[str, Any]


@app.post("/events/study-published")
async def study_published(
    event: StudyEvent, background_tasks: BackgroundTasks
) -> dict[str, str]:
    """Ingest study publication events and trigger layout generation asynchronously.

    Args:
        event (StudyEvent): The incoming study event payload.
        background_tasks (BackgroundTasks): FastAPI background task manager.

    Returns:
        dict[str, str]: A status message confirming job acceptance.
    """
    user_id = current_user_id.get()
    change_reason = current_change_reason.get()

    payload = event.payload or {}

    # Viewport and layout warning metadata validation to prevent API bypasses
    layout_warnings = payload.get("layout_warnings") or payload.get("layoutWarnings")
    if (
        not layout_warnings
        and "protocol" in payload
        and isinstance(payload["protocol"], dict)
    ):
        layout_warnings = payload["protocol"].get("layout_warnings") or payload[
            "protocol"
        ].get("layoutWarnings")

    has_warnings = False
    if (
        isinstance(layout_warnings, list)
        and len(layout_warnings) > 0
        or isinstance(layout_warnings, bool)
        and layout_warnings
        or isinstance(layout_warnings, (int, float))
        and layout_warnings > 0
    ):
        has_warnings = True

    justification = (
        payload.get("layout_justification")
        or payload.get("layoutJustification")
        or payload.get("justification")
    )
    if (
        not justification
        and "protocol" in payload
        and isinstance(payload["protocol"], dict)
    ):
        justification = (
            payload["protocol"].get("layout_justification")
            or payload["protocol"].get("layoutJustification")
            or payload["protocol"].get("justification")
        )

    if has_warnings:
        if (
            not justification
            or not isinstance(justification, str)
            or not justification.strip()
        ):
            raise HTTPException(
                status_code=400,
                detail="Layout validation failed: unresolved layout warnings exist, but no clinical justification was provided.",
            )

        # Save layout deviation logs, designer identity, and justification to AuditLog
        u_id = user_id or "system"
        with audit_context(u_id, justification):
            async with db_manager.get_session_maker()() as session:
                async with session.begin():
                    deviation_audit = AuditLog(
                        id=str(uuid.uuid4()),
                        table_name="layout_deviation_audit",
                        record_id=event.study_id,
                        action="DEVIATION",
                        user_id=u_id,
                        ip_address=current_ip_address.get() or "127.0.0.1",
                        timestamp=datetime.now(UTC).replace(tzinfo=None),
                        old_values=None,
                        new_values={
                            "status": "APPROVED",
                            "justification": justification,
                            "layout_warnings": layout_warnings,
                            "timestamp": datetime.now(UTC).isoformat(),
                        },
                        change_reason=justification,
                    )
                    session.add(deviation_audit)

    # Extract study-level cross_form_check rules if present
    cross_form_rules = (
        event.payload.get("cross_form_check")
        or event.payload.get("cross_form_checks")
        or []
    )
    if cross_form_rules:
        u_id = user_id or "system"
        reason = change_reason or "Ingest published cross-form rules"
        with audit_context(u_id, reason):
            async with db_manager.get_session_maker()() as session:
                async with session.begin():
                    # Deactivate/supersede the prior active rule set for the study
                    stmt = select(StudyAuthoredRule).where(
                        StudyAuthoredRule.study_id == event.study_id,
                        StudyAuthoredRule.is_active.is_(True),
                        StudyAuthoredRule.is_deleted.is_(False),
                    )
                    res = await session.execute(stmt)
                    prior_rules = res.scalars().all()
                    for r in prior_rules:
                        r.is_active = False
                        r.version += 1
                        session.add(r)

                    # Insert the new rules as active
                    pub_ver = str(event.payload.get("version") or "1.0")
                    for r_data in cross_form_rules:
                        new_rule = StudyAuthoredRule(
                            study_id=event.study_id,
                            rule_id=r_data["id"],
                            rule_type=r_data.get("type") or "cross_form_check",
                            condition=r_data["condition"],
                            query_message=r_data["query_message"],
                            message=r_data["query_message"],
                            publication_version=pub_ver,
                            is_active=True,
                        )
                        session.add(new_rule)

    job_id = str(uuid.uuid4())
    background_tasks.add_task(
        process_translation,
        event.study_id,
        event.payload,
        db_manager.get_session_maker(),
        user_id=user_id,
        change_reason=change_reason,
        job_id=job_id,
    )
    return {
        "status": "accepted",
        "message": "Translation job queued in background.",
        "job_id": job_id,
        "id": job_id,
    }


class TranslationJobResponse(BaseModel):
    """Pydantic schema returning translation job status and metadata."""

    id: str
    study_id: str
    status: str
    odm_payload: str | None = None
    openrosa_payload: str | None = None
    error_message: str | None = None


@app.get(
    "/api/v1/execution/translation/jobs", response_model=list[TranslationJobResponse]
)
async def list_translation_jobs() -> list[TranslationJobResponse]:
    """Retrieve a list of historical translation jobs."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TranslationJob)
        res = await session.execute(stmt)
        jobs = res.scalars().all()
        return [
            TranslationJobResponse(
                id=job.id,
                study_id=job.study_id,
                status=job.status,
                odm_payload=job.odm_payload,
                openrosa_payload=job.openrosa_payload,
                error_message=job.error_message,
            )
            for job in jobs
        ]


@app.get(
    "/api/v1/execution/translation/jobs/{job_id}", response_model=TranslationJobResponse
)
async def get_translation_job(job_id: str) -> TranslationJobResponse:
    """Query the execution status, output metadata, and error messages of a single translation job by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TranslationJob).where(TranslationJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            raise HTTPException(status_code=404, detail="Translation job not found")
        return TranslationJobResponse(
            id=job.id,
            study_id=job.study_id,
            status=job.status,
            odm_payload=job.odm_payload,
            openrosa_payload=job.openrosa_payload,
            error_message=job.error_message,
        )


# ==========================================
# GxP Relational Observation & Subject API
# ==========================================


class Demographics(BaseModel):
    """Pydantic schema representing demographic details."""

    name: str | None = None
    birthdate: str | None = None
    gender: str | None = None
    race: str | None = None


class SubjectCreate(BaseModel):
    """Pydantic schema for creating a clinical subject pseudonymously."""

    subject_id: str
    study_id: str
    demographics: Demographics | None = None


class SubjectResponse(BaseModel):
    """Pydantic schema returning subject details."""

    id: str
    subject_id: str
    study_id: str
    encrypted_demographics: str | None = None


class CriterionLevelResult(BaseModel):
    """Pydantic schema for individual criterion level evaluation result."""

    criterion_id: str
    criterion_type: str
    description: str
    dsl_source: str
    is_met: bool
    is_indeterminate: bool


class SubjectScreeningResponse(BaseModel):
    """Pydantic schema for subject screening evaluation outcome, excluding PHI."""

    eligible: bool | None = None
    failed_criteria: list[str] = Field(default_factory=list)
    indeterminate_criteria: list[str] = Field(default_factory=list)
    criterion_evaluations: list[CriterionLevelResult] = Field(default_factory=list)


class SubjectScreeningRequest(BaseModel):
    """Pydantic schema for requesting subject eligibility screening."""

    study_id: str | None = None


class SubjectConsentRequest(BaseModel):
    """Pydantic schema for recording a subject's consent to a protocol version."""

    protocol_version: ProtocolVersionRef
    icf_signed: bool
    icf_signed_date: datetime | None = None
    requires_reconsent: bool = False
    is_paper_override: bool = False


class SubjectConsentResponse(BaseModel):
    """Pydantic schema returning subject consent details."""

    id: str
    subject_id: str
    study_id: str
    version_tag: str
    version_index: int
    icf_signed: bool
    icf_signed_date: datetime | None = None
    requires_reconsent: bool
    is_paper_override: bool = False
    version: int


class VisitCreate(BaseModel):
    """Pydantic schema for creating a clinical visit."""

    subject_id: str
    visit_name: str
    study_id: str
    visit_date: datetime | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


class VisitResponse(BaseModel):
    """Pydantic schema returning visit details."""

    id: str
    subject_id: str
    visit_name: str
    visit_date: datetime
    study_id: str
    protocol_version_tag: str | None = None
    protocol_version_index: int | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


class ObservationCreate(BaseModel):
    """Pydantic schema for creating a clinical observation."""

    subject_id: str
    study_id: str | None = None
    visit_id: str | None = None
    domain: str
    test_code: str
    test_name: str
    value: float | None = None
    value_string: str | None = None
    unit: str | None = None
    observation_date: datetime | None = None
    lab_source: str | None = None
    lab_site_id: str | None = None


class ObservationResponse(BaseModel):
    """Pydantic schema returning observation details."""

    id: str
    subject_id: str
    study_id: str
    visit_id: str | None = None
    domain: str
    observation_date: datetime
    test_code: str
    test_name: str
    value: float | None = None
    value_string: str | None = None
    unit: str | None = None
    normalized_value: float | None = None
    normalized_unit: str | None = None
    is_outlier: bool
    lab_source: str | None = None
    lab_site_id: str | None = None
    lab_indicator: str | None = None
    lab_out_of_range: bool | None = None
    matched_normal_bounds: str | None = None
    range_indicator: str | None = None
    is_out_of_range: bool | None = None
    reference_range_low: float | None = None
    reference_range_high: float | None = None
    protocol_version_tag: str | None = None
    protocol_version_index: int | None = None

    @model_validator(mode="after")
    def populate_range_fields(self) -> ObservationResponse:
        if self.range_indicator is None and self.lab_indicator is not None:
            self.range_indicator = self.lab_indicator
        if self.is_out_of_range is None and self.lab_out_of_range is not None:
            self.is_out_of_range = self.lab_out_of_range
        if self.reference_range_low is None or self.reference_range_high is None:
            if self.matched_normal_bounds:
                try:
                    bounds = json.loads(self.matched_normal_bounds)
                    if self.reference_range_low is None:
                        self.reference_range_low = bounds.get("low")
                    if self.reference_range_high is None:
                        self.reference_range_high = bounds.get("high")
                except Exception:
                    pass
        return self


class MigrationRuleCreate(BaseModel):
    study_id: str
    source_version: str
    target_version: str
    rule_type: str
    source_field: str | None = None
    target_field: str | None = None
    default_value_string: str | None = None
    default_value_float: float | None = None


class MigrationRuleResponse(BaseModel):
    id: str
    study_id: str
    source_version: str
    target_version: str
    rule_type: str
    source_field: str | None = None
    target_field: str | None = None
    default_value_string: str | None = None
    default_value_float: float | None = None


@app.post("/api/v1/execution/subjects", response_model=SubjectResponse)
async def create_subject(
    payload: SubjectCreate,
    roles: list[str] = Depends(verify_not_auditor),
) -> SubjectResponse:
    """Create a new clinical subject pseudonymously."""
    encrypted_demo = None
    if payload.demographics is not None:
        encrypted_demo = encrypt_demographics(
            payload.demographics.dict(exclude_none=True)
        )

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            # Query max enrollment_index for the study inside the active transaction
            stmt_max = select(func.max(ClinicalSubject.enrollment_index)).where(
                ClinicalSubject.study_id == payload.study_id
            )
            res_max = await session.execute(stmt_max)
            max_idx = res_max.scalar()
            new_idx = 0 if max_idx is None else max_idx + 1

            subj = ClinicalSubject(
                subject_id=payload.subject_id,
                study_id=payload.study_id,
                encrypted_demographics=encrypted_demo,
                enrollment_index=new_idx,
            )
            session.add(subj)

        stmt = select(ClinicalSubject).where(ClinicalSubject.id == subj.id)
        res = await session.execute(stmt)
        subj_db = res.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )


@app.post(
    "/api/v1/execution/subjects/{subject_id}/consent",
    response_model=SubjectConsentResponse,
)
@app.post(
    "/api/v1/execution/subjects/{subject_id}/consents",
    response_model=SubjectConsentResponse,
)
async def record_subject_consent_endpoint(
    subject_id: str,
    payload: SubjectConsentRequest,
    roles: list[str] = Depends(verify_not_auditor),
) -> SubjectConsentResponse:
    """Record/upload a signed informed consent form (ICF) for a subject, clearing any requires_reconsent gate."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            study_id = payload.protocol_version.study_id
            version_tag = payload.protocol_version.version_tag
            version_index = payload.protocol_version.version_index

            stmt = select(SubjectConsent).where(
                SubjectConsent.subject_id == subject_id,
                SubjectConsent.study_id == study_id,
                SubjectConsent.version_index == version_index,
            )
            existing = (await session.execute(stmt)).scalars().first()
            if existing:
                existing.version_tag = version_tag
                existing.icf_signed = payload.icf_signed
                if payload.icf_signed_date:
                    existing.icf_signed_date = payload.icf_signed_date
                elif not existing.icf_signed_date:
                    existing.icf_signed_date = datetime.utcnow()
                existing.requires_reconsent = payload.requires_reconsent
                existing.is_paper_override = payload.is_paper_override
                consent_db = existing
            else:
                consent_db = SubjectConsent(
                    subject_id=subject_id,
                    study_id=study_id,
                    version_tag=version_tag,
                    version_index=version_index,
                    icf_signed=payload.icf_signed,
                    icf_signed_date=payload.icf_signed_date or datetime.utcnow(),
                    requires_reconsent=payload.requires_reconsent,
                    is_paper_override=payload.is_paper_override,
                )
                session.add(consent_db)

            # If this consent is signed and does not require re-consent,
            # clear requires_reconsent for any other/older consents of this subject
            if (
                payload.icf_signed or payload.is_paper_override
            ) and not payload.requires_reconsent:
                stmt_others = select(SubjectConsent).where(
                    SubjectConsent.subject_id == subject_id,
                    SubjectConsent.study_id == study_id,
                )
                others = (await session.execute(stmt_others)).scalars().all()
                for other in others:
                    other.requires_reconsent = False

            await session.flush()

        stmt_ref = select(SubjectConsent).where(SubjectConsent.id == consent_db.id)
        res_ref = await session.execute(stmt_ref)
        saved = res_ref.scalar_one()

        return SubjectConsentResponse(
            id=saved.id,
            subject_id=saved.subject_id,
            study_id=saved.study_id,
            version_tag=saved.version_tag,
            version_index=saved.version_index,
            icf_signed=saved.icf_signed,
            icf_signed_date=saved.icf_signed_date,
            requires_reconsent=saved.requires_reconsent,
            is_paper_override=saved.is_paper_override,
            version=saved.version,
        )


async def check_site_compliance_for_enrollment(
    session, study_id: str, site_id: str | None
) -> bool:
    """Helper to verify that SITE_ACTIVATION milestone is complete in SiteComplianceCache."""
    if not site_id:
        return True
    from apps.execution.database.models.compliance import SiteComplianceCache

    stmt = select(SiteComplianceCache).where(
        SiteComplianceCache.study_id == study_id,
        SiteComplianceCache.site_id == site_id,
        SiteComplianceCache.milestone == "SITE_ACTIVATION",
    )
    result = await session.execute(stmt)
    cache_entry = result.scalars().first()
    return bool(cache_entry and cache_entry.is_complete)


class ETMFCompletenessWebhookPayload(BaseModel):
    study_id: str
    site_id: str | None = None
    milestone: str
    is_complete: bool
    missing_artifacts: list[str] = Field(default_factory=list)


@app.post("/api/v1/execution/webhooks/etmf")
async def etmf_completeness_webhook(
    payload: ETMFCompletenessWebhookPayload,
) -> dict[str, Any]:
    """Receive and process completeness webhook events from eTMF to update local compliance cache."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            from apps.execution.database.models.compliance import SiteComplianceCache

            stmt = select(SiteComplianceCache).where(
                SiteComplianceCache.study_id == payload.study_id,
                SiteComplianceCache.site_id == payload.site_id,
                SiteComplianceCache.milestone == payload.milestone,
            )
            result = await session.execute(stmt)
            cache_entry = result.scalars().first()

            missing_docs_str = (
                ",".join(payload.missing_artifacts)
                if payload.missing_artifacts
                else None
            )

            if cache_entry:
                cache_entry.is_complete = payload.is_complete
                cache_entry.missing_documents = missing_docs_str
                cache_entry.version += 1
            else:
                cache_entry = SiteComplianceCache(
                    study_id=payload.study_id,
                    site_id=payload.site_id,
                    milestone=payload.milestone,
                    is_complete=payload.is_complete,
                    missing_documents=missing_docs_str,
                    version=1,
                )
                session.add(cache_entry)
            await session.commit()

    return {"status": "success", "message": "Compliance cache updated successfully."}


class SiteActivationRequest(BaseModel):
    study_id: str


@app.post("/api/v1/execution/sites/{site_id}/activate")
async def activate_site_endpoint(
    site_id: str,
    payload: SiteActivationRequest,
    principal: Principal = Depends(get_principal),
) -> dict[str, Any]:
    """Attempt site activation in the Execution Engine, validating compliance from the local cache."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            from apps.execution.database.models.compliance import SiteComplianceCache

            stmt = select(SiteComplianceCache).where(
                SiteComplianceCache.study_id == payload.study_id,
                SiteComplianceCache.site_id == site_id,
                SiteComplianceCache.milestone == "SITE_ACTIVATION",
            )
            result = await session.execute(stmt)
            cache_entry = result.scalars().first()

            if not cache_entry or cache_entry.is_complete is False:
                # Log blocked activation attempt to execution audit trail in separate session
                async with db_manager.get_session_maker()() as audit_session:
                    async with audit_session.begin():
                        block_audit = AuditLog(
                            table_name="site_compliance_caches",
                            record_id=site_id,
                            action="BLOCKED_ACTIVATION",
                            user_id=principal.user_id if principal else "system",
                            change_reason=f"Blocked site activation for site {site_id} due to incomplete eTMF compliance status.",
                        )
                        audit_session.add(block_audit)

                raise HTTPException(
                    status_code=400,
                    detail=f"Site activation blocked: Site {site_id} is not compliant in eTMF. Missing expected documents.",
                )

            # Successfully activated: Log success in execution audit trail
            success_audit = AuditLog(
                table_name="site_compliance_caches",
                record_id=site_id,
                action="SITE_ACTIVATION",
                user_id=principal.user_id if principal else "system",
                change_reason=f"Site {site_id} successfully activated after eTMF compliance completeness verification.",
            )
            session.add(success_audit)
            await session.commit()

    return {"status": "success", "message": f"Site {site_id} activated successfully."}


class ComplianceStatusResponse(BaseModel):
    study_id: str
    site_id: str | None
    milestone: str
    is_complete: bool
    missing_documents: list[str]


@app.get(
    "/api/v1/execution/sites/{site_id}/compliance-status",
    response_model=ComplianceStatusResponse,
)
async def get_site_compliance_status(
    site_id: str,
    study_id: str = Query(..., description="The clinical study ID"),
    milestone: str = Query("SITE_ACTIVATION", description="The milestone to check"),
) -> ComplianceStatusResponse:
    """Retrieve site compliance status from the local cache."""
    async with db_manager.get_session_maker()() as session:
        from apps.execution.database.models.compliance import SiteComplianceCache

        stmt = select(SiteComplianceCache).where(
            SiteComplianceCache.study_id == study_id,
            SiteComplianceCache.site_id == site_id,
            SiteComplianceCache.milestone == milestone,
        )
        result = await session.execute(stmt)
        cache_entry = result.scalars().first()

        if not cache_entry:
            return ComplianceStatusResponse(
                study_id=study_id,
                site_id=site_id,
                milestone=milestone,
                is_complete=False,
                missing_documents=[],
            )

        missing_list = []
        if cache_entry.missing_documents:
            missing_list = [
                d.strip() for d in cache_entry.missing_documents.split(",") if d.strip()
            ]

        return ComplianceStatusResponse(
            study_id=cache_entry.study_id,
            site_id=cache_entry.site_id,
            milestone=cache_entry.milestone,
            is_complete=cache_entry.is_complete,
            missing_documents=missing_list,
        )


@app.post(
    "/api/v1/execution/subjects/{subject_id}/screening",
    response_model=SubjectScreeningResponse,
)
async def evaluate_and_transition_screening(
    subject_id: str,
    request: Request,
    payload: SubjectScreeningRequest | None = None,
    roles: list[str] = Depends(
        require_roles(ROLE_SITE_INVESTIGATOR, ROLE_DATA_MANAGER, "investigator")
    ),
    _justification=Depends(verify_change_justification),
) -> SubjectScreeningResponse:
    """Evaluate subject's eligibility criteria and execute the guarded screening lifecycle transition."""
    change_reason = request.headers.get("X-Change-Reason", "")

    async with db_manager.get_session_maker()() as session:
        # Propagate context variables into database session for PostgreSQL triggers
        try:
            user_val = current_user_id.get()
        except LookupError:
            user_val = "system"
        try:
            reason_val = current_change_reason.get()
        except LookupError:
            reason_val = change_reason or "system_operation"

        await session.execute(
            text("SELECT set_config('cadence.current_user_id', :user_id, true);"),
            {"user_id": user_val},
        )
        await session.execute(
            text("SELECT set_config('cadence.current_change_reason', :reason, true);"),
            {"reason": reason_val},
        )
        await session.execute(
            text("SELECT set_config('cadence.app_writing', 'true', true);")
        )

        stmt_subj = select(ClinicalSubject).where(
            (ClinicalSubject.subject_id == subject_id)
            | (ClinicalSubject.id == subject_id)
        )
        res_subj = await session.execute(stmt_subj)
        subject_obj = res_subj.scalars().first()
        if not subject_obj:
            raise HTTPException(status_code=404, detail="Clinical subject not found.")

        study_id = payload.study_id if payload else None
        if not study_id:
            study_id = subject_obj.study_id

        if not study_id:
            raise HTTPException(
                status_code=400,
                detail="Study ID must be provided in the payload or resolved from the ClinicalSubject.",
            )

        from apps.execution.eligibility_service import evaluate_subject_eligibility

        # Evaluate eligibility using our service
        res = await evaluate_subject_eligibility(study_id, subject_obj, session)

        try:
            if res.eligible is False:
                # Log failed criterion IDs in the change reason/justification context
                failed_ids = ", ".join(res.failed_criteria)
                custom_reason = f"Screen failure due to failed criteria: {failed_ids}. Original reason: {change_reason}"
                current_change_reason.set(custom_reason)

                subject_obj.status = "SCREEN_FAILED"
                session.add(subject_obj)
                await session.commit()
            elif res.eligible is True:
                # Check site compliance from local cache
                is_compliant = await check_site_compliance_for_enrollment(
                    session, study_id, subject_obj.site_id
                )
                if not is_compliant:
                    # Log blocked transition attempt to execution audit trail in separate session
                    async with db_manager.get_session_maker()() as audit_session:
                        async with audit_session.begin():
                            blocked_audit = AuditLog(
                                table_name="clinical_subjects",
                                record_id=subject_obj.id,
                                action="BLOCKED_ENROLLMENT",
                                user_id=user_val,
                                change_reason=f"Blocked enrollment of subject {subject_id} due to non-compliant site {subject_obj.site_id} in study {study_id}.",
                            )
                            audit_session.add(blocked_audit)
                    raise HTTPException(
                        status_code=400,
                        detail=f"Subject enrollment blocked: Site {subject_obj.site_id} is not compliant.",
                    )

                custom_reason = f"Subject met all eligibility criteria and transitioned to ENROLLED. Original reason: {change_reason}"
                current_change_reason.set(custom_reason)

                subject_obj.status = "ENROLLED"
                session.add(subject_obj)
                await session.commit()
            else:
                # Indeterminate: no state transition (leave status as SCREENING)
                pass
        except InvalidStateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        eval_list = []
        for cid, cev in res.criteria_evaluations.items():
            eval_list.append(
                CriterionLevelResult(
                    criterion_id=cev.criterion_id,
                    criterion_type=cev.criterion_type,
                    description=cev.description,
                    dsl_source=cev.dsl_source,
                    is_met=cev.is_met,
                    is_indeterminate=cev.is_indeterminate,
                )
            )

        return SubjectScreeningResponse(
            eligible=res.eligible,
            failed_criteria=res.failed_criteria,
            indeterminate_criteria=res.indeterminate_criteria,
            criterion_evaluations=eval_list,
        )


class SubjectStateUpdateRequest(BaseModel):
    status: str | None = None
    state: str | None = None


class SubjectDemographicsUpdateRequest(BaseModel):
    demographics: Demographics | None = None
    strat_factors: dict[str, Any] | None = None


@app.patch(
    "/api/v1/execution/subjects/{id}/state",
    response_model=SubjectResponse,
)
@app.patch(
    "/subjects/{id}/state",
    response_model=SubjectResponse,
)
async def update_subject_state_endpoint(
    id: str,
    payload: SubjectStateUpdateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectResponse:
    # Ensure change justification headers are present and valid
    verify_change_justification(request)

    target_state = payload.status or payload.state
    if not target_state:
        raise HTTPException(
            status_code=400, detail="Either status or state must be provided."
        )

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == id) | (ClinicalSubject.id == id)
            )
            result = await session.execute(stmt)
            subject = result.scalars().first()
            if not subject:
                raise HTTPException(status_code=404, detail="Subject not found")

            # Try to transition state
            try:
                if target_state == "ENROLLED":
                    # Check site compliance from local cache
                    is_compliant = await check_site_compliance_for_enrollment(
                        session, subject.study_id, subject.site_id
                    )
                    if not is_compliant:
                        # Log blocked transition attempt to execution audit trail in separate session
                        async with db_manager.get_session_maker()() as audit_session:
                            async with audit_session.begin():
                                blocked_audit = AuditLog(
                                    table_name="clinical_subjects",
                                    record_id=subject.id,
                                    action="BLOCKED_ENROLLMENT",
                                    user_id=principal.user_id
                                    if principal
                                    else "system",
                                    change_reason=f"Blocked enrollment of subject {id} due to non-compliant site {subject.site_id} in study {subject.study_id}.",
                                )
                                audit_session.add(blocked_audit)
                        raise HTTPException(
                            status_code=400,
                            detail=f"Subject enrollment blocked: Site {subject.site_id} is not compliant.",
                        )

                subject.status = target_state
            except HTTPException:
                raise
            except InvalidStateTransitionError:
                raise HTTPException(status_code=400, detail="INVALID_STATE_TRANSITION")
            except Exception as e:
                raise HTTPException(status_code=400, detail=str(e))

            session.add(subject)

        # Retrieve and return updated subject
        stmt_ref = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        res_ref = await session.execute(stmt_ref)
        subj_db = res_ref.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )


@app.put(
    "/api/v1/execution/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
@app.put(
    "/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
async def update_subject_demographics_endpoint(
    id: str,
    payload: SubjectDemographicsUpdateRequest,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectResponse:
    # Ensure change justification headers are present and valid
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == id) | (ClinicalSubject.id == id)
            )
            result = await session.execute(stmt)
            subject = result.scalars().first()
            if not subject:
                raise HTTPException(status_code=404, detail="Subject not found")

            # Check if post-randomization
            is_post_rand = subject.status in (
                "RANDOMIZED",
                "ACTIVE",
                "COMPLETED",
                "UNBLINDED",
                "WITHDRAWN",
            )

            # Update strat_factors if provided
            if payload.strat_factors is not None:
                if is_post_rand and subject.strat_factors != payload.strat_factors:
                    raise HTTPException(
                        status_code=422, detail="LOCKED_FACTOR_MUTATION"
                    )
                subject.strat_factors = payload.strat_factors

            # Update demographics if provided
            if payload.demographics is not None:
                current_demo = {}
                if subject.encrypted_demographics:
                    with suppress(Exception):
                        current_demo = decrypt_demographics(
                            subject.encrypted_demographics
                        )

                new_demo = payload.demographics.dict(exclude_none=True)
                if is_post_rand and current_demo != new_demo:
                    raise HTTPException(
                        status_code=422, detail="LOCKED_FACTOR_MUTATION"
                    )
                encrypted_demo = encrypt_demographics(new_demo)
                subject.encrypted_demographics = encrypted_demo

            session.add(subject)

        # Retrieve and return
        stmt_ref = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        res_ref = await session.execute(stmt_ref)
        subj_db = res_ref.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )


@app.delete(
    "/api/v1/execution/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
@app.delete(
    "/subjects/{id}/demographics",
    response_model=SubjectResponse,
)
async def delete_subject_demographics_endpoint(
    id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectResponse:
    # Ensure change justification headers are present and valid
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(ClinicalSubject).where(
                (ClinicalSubject.subject_id == id) | (ClinicalSubject.id == id)
            )
            result = await session.execute(stmt)
            subject = result.scalars().first()
            if not subject:
                raise HTTPException(status_code=404, detail="Subject not found")

            # Check if post-randomization
            is_post_rand = subject.status in (
                "RANDOMIZED",
                "ACTIVE",
                "COMPLETED",
                "UNBLINDED",
                "WITHDRAWN",
            )

            if is_post_rand:
                raise HTTPException(status_code=403, detail="SOFT_DELETE_BLOCKED")

            # Pre-randomization: we can clear demographics/factors
            subject.encrypted_demographics = None
            subject.strat_factors = None
            session.add(subject)

        # Retrieve and return
        stmt_ref = select(ClinicalSubject).where(ClinicalSubject.id == subject.id)
        res_ref = await session.execute(stmt_ref)
        subj_db = res_ref.scalar_one()
        return SubjectResponse(
            id=subj_db.id,
            subject_id=subj_db.subject_id,
            study_id=subj_db.study_id,
            encrypted_demographics=subj_db.encrypted_demographics,
        )


@app.post("/api/v1/execution/visits", response_model=VisitResponse)
async def create_visit(
    payload: VisitCreate,
    roles: list[str] = Depends(verify_not_auditor),
) -> VisitResponse:
    """Create a new clinical visit."""
    async with db_manager.get_session_maker()() as session:
        vdate = payload.visit_date or datetime.now()
        visit = ClinicalVisit(
            subject_id=payload.subject_id,
            visit_name=payload.visit_name,
            visit_date=vdate,
            study_id=payload.study_id,
            planned_date=payload.planned_date,
            window_start=payload.window_start,
            window_end=payload.window_end,
            window_status=payload.window_status,
        )
        # Stamping capture-time protocol-version identity
        stmt_consent = (
            select(SubjectConsent)
            .where(
                SubjectConsent.subject_id == payload.subject_id,
                SubjectConsent.study_id == payload.study_id,
                SubjectConsent.icf_signed.is_(True),
            )
            .order_by(SubjectConsent.version_index.desc())
        )
        res_consent = await session.execute(stmt_consent)
        active_consent = res_consent.scalars().first()
        if active_consent:
            visit.protocol_version_tag = active_consent.version_tag
            visit.protocol_version_index = active_consent.version_index

        session.add(visit)
        await session.commit()
        stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit.id)
        res = await session.execute(stmt)
        visit_db = res.scalar_one()
        return VisitResponse(
            id=visit_db.id,
            subject_id=visit_db.subject_id,
            visit_name=visit_db.visit_name,
            visit_date=visit_db.visit_date,
            study_id=visit_db.study_id,
            protocol_version_tag=visit_db.protocol_version_tag,
            protocol_version_index=visit_db.protocol_version_index,
            planned_date=visit_db.planned_date,
            window_start=visit_db.window_start,
            window_end=visit_db.window_end,
            window_status=visit_db.window_status,
        )


class SubjectDetailResponse(BaseModel):
    subject_id: str
    study_id: str
    status: str
    site_id: str | None = None
    treatment_group: str | None = None
    randomization_seed: str | None = None
    investigational_product_id: str | None = None


class VisitDetailResponse(BaseModel):
    id: str
    subject_id: str
    visit_name: str
    visit_date: datetime
    study_id: str
    treatment_group: str | None = None
    randomization_seed: str | None = None
    investigational_product_id: str | None = None
    planned_date: datetime | None = None
    window_start: datetime | None = None
    window_end: datetime | None = None
    window_status: str | None = None


@app.get(
    "/api/v1/execution/subjects/{subject_id}",
    response_model=SubjectDetailResponse,
)
async def get_subject_detail(
    subject_id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> SubjectDetailResponse:
    """Retrieve detailed subject information, applying dynamic blinding redaction & site isolation."""
    async with db_manager.get_session_maker()() as session:
        # 1. Fetch Subject
        stmt = select(ClinicalSubject).where(ClinicalSubject.subject_id == subject_id)
        result = await session.execute(stmt)
        subject = result.scalars().first()
        if not subject:
            raise HTTPException(status_code=404, detail="Subject not found")

        # 2. Enforce site isolation (PRD-SYS-004)
        from packages.security import enforce_site_isolation

        enforce_site_isolation(request, subject.site_id, principal)

        # 3. Retrieve Randomization if available
        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == subject_id
        )
        rand_res = await session.execute(stmt_rand)
        rand = rand_res.scalars().first()

        treatment_group = None
        randomization_seed = None
        investigational_product_id = None

        if rand:
            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            await key_mgr.load_from_db(session)
            try:
                decrypted = key_mgr.decrypt(rand.encrypted_allocation)
                treatment_group = decrypted.get("allocation")
            except Exception:
                treatment_group = "Decryption Failed"

            randomization_seed = "12345"
            investigational_product_id = rand.kit_reference

        response_dict = {
            "subject_id": subject.subject_id,
            "study_id": subject.study_id,
            "status": subject.status,
            "site_id": subject.site_id,
            "treatment_group": treatment_group,
            "randomization_seed": randomization_seed,
            "investigational_product_id": investigational_product_id,
        }

        # 4. Apply dynamic blinding filter
        from apps.execution.field_masking import apply_rtsm_blinded_filter

        roles = getattr(request.state, "roles", None)
        if roles is None:
            roles = principal.roles
        masked = apply_rtsm_blinded_filter(response_dict, roles)
        return SubjectDetailResponse(**masked)


@app.get(
    "/api/v1/execution/visits/{visit_id}",
    response_model=VisitDetailResponse,
)
async def get_visit_detail(
    visit_id: str,
    request: Request,
    principal: Principal = Depends(get_principal),
) -> VisitDetailResponse:
    """Retrieve detailed visit information, applying dynamic blinding redaction & site isolation."""
    async with db_manager.get_session_maker()() as session:
        # 1. Fetch Visit
        stmt = select(ClinicalVisit).where(ClinicalVisit.id == visit_id)
        result = await session.execute(stmt)
        visit = result.scalars().first()
        if not visit:
            raise HTTPException(status_code=404, detail="Visit not found")

        # 2. Fetch corresponding Subject
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == visit.subject_id
        )
        subj_res = await session.execute(stmt_subj)
        subject = subj_res.scalars().first()
        if not subject:
            raise HTTPException(status_code=404, detail="Associated Subject not found")

        # 3. Enforce site isolation (PRD-SYS-004) using Subject's site_id
        from packages.security import enforce_site_isolation

        enforce_site_isolation(request, subject.site_id, principal)

        # 4. Retrieve Randomization if available
        stmt_rand = select(SubjectRandomization).where(
            SubjectRandomization.subject_id == subject.subject_id
        )
        rand_res = await session.execute(stmt_rand)
        rand = rand_res.scalars().first()

        treatment_group = None
        randomization_seed = None
        investigational_product_id = None

        if rand:
            from apps.execution.cryptography import AllocationKeyManager

            key_mgr = AllocationKeyManager()
            await key_mgr.load_from_db(session)
            try:
                decrypted = key_mgr.decrypt(rand.encrypted_allocation)
                treatment_group = decrypted.get("allocation")
            except Exception:
                treatment_group = "Decryption Failed"

            randomization_seed = "12345"
            investigational_product_id = rand.kit_reference

        response_dict = {
            "id": visit.id,
            "subject_id": visit.subject_id,
            "visit_name": visit.visit_name,
            "visit_date": visit.visit_date,
            "study_id": visit.study_id,
            "treatment_group": treatment_group,
            "randomization_seed": randomization_seed,
            "investigational_product_id": investigational_product_id,
            "planned_date": visit.planned_date,
            "window_start": visit.window_start,
            "window_end": visit.window_end,
            "window_status": visit.window_status,
        }

        # 5. Apply dynamic blinding filter
        from apps.execution.field_masking import apply_rtsm_blinded_filter

        roles = getattr(request.state, "roles", None)
        if roles is None:
            roles = principal.roles
        masked = apply_rtsm_blinded_filter(response_dict, roles)
        return VisitDetailResponse(**masked)


@app.post("/api/v1/execution/observations", response_model=ObservationResponse)
async def create_observation(
    payload: ObservationCreate,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
) -> ObservationResponse:
    """CREATE a new clinical observation, performing unit normalization and outlier checks."""
    norm_val, norm_unit = get_normalized_representation(payload.value, payload.unit)

    async with db_manager.get_session_maker()() as session:
        # Query Subject to determine study_id and decrypt demographics
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == payload.subject_id
        )
        res_subj = await session.execute(stmt_subj)
        subj_db = res_subj.scalars().first()

        study_id = payload.study_id
        if not study_id:
            if not subj_db:
                raise HTTPException(
                    status_code=400,
                    detail="Subject not registered; cannot infer study_id",
                )
            study_id = subj_db.study_id

        obs_date = payload.observation_date or datetime.now()

        # Decrypt demographics relative to observation date if subject is registered
        gender = "U"
        age = None
        if subj_db:
            demo = get_safe_demographics(subj_db, obs_date, preserve_custom=True)
            gender = demo.get("gender")
            age = demo.get("age")

        # Fetch active LabReferenceRange definitions using the read-through cache helper
        ranges = await get_active_lab_ranges(
            lab_range_cache, session, study_id, payload.test_code
        )

        from apps.execution.lab_ranges import evaluate_lab_value, select_reference_range

        matched_range = select_reference_range(
            ranges=ranges,
            study_id=study_id,
            test_code=payload.test_code,
            normalized_unit=norm_unit,
            lab_source=payload.lab_source or "CENTRAL",
            sex=gender,
            age=age,
            site_id=payload.lab_site_id,
        )

        indicator, out_of_range, matched_bounds = evaluate_lab_value(
            norm_val, matched_range
        )

        # Stamping capture-time protocol-version identity
        protocol_version_tag = None
        protocol_version_index = None
        stmt_consent = (
            select(SubjectConsent)
            .where(
                SubjectConsent.subject_id == payload.subject_id,
                SubjectConsent.study_id == study_id,
                SubjectConsent.icf_signed.is_(True),
            )
            .order_by(SubjectConsent.version_index.desc())
        )
        res_consent = await session.execute(stmt_consent)
        active_consent = res_consent.scalars().first()
        if active_consent:
            protocol_version_tag = active_consent.version_tag
            protocol_version_index = active_consent.version_index

        obs = ClinicalObservation(
            subject_id=payload.subject_id,
            study_id=study_id,
            visit_id=payload.visit_id,
            domain=payload.domain,
            observation_date=obs_date,
            test_code=payload.test_code,
            test_name=payload.test_name,
            value=payload.value,
            value_string=payload.value_string,
            unit=payload.unit,
            normalized_value=norm_val,
            normalized_unit=norm_unit,
            is_outlier=False,
            lab_source=payload.lab_source or "CENTRAL",
            lab_site_id=payload.lab_site_id,
            lab_indicator=indicator,
            lab_out_of_range=out_of_range,
            matched_normal_bounds=matched_bounds,
            protocol_version_tag=protocol_version_tag,
            protocol_version_index=protocol_version_index,
        )
        session.add(obs)

        # Connect clinical observations to coded-term assignments
        domain_upper = payload.domain.upper()
        if domain_upper in {"AE", "MH", "CM"}:
            verbatim = payload.value_string
            if verbatim and verbatim.strip():
                # Resolve dictionary type
                if domain_upper in {"AE", "MH"}:
                    dict_type = DBDictionaryType.MEDDRA
                    stmt_latest = (
                        select(DictionaryImportJob)
                        .where(
                            DictionaryImportJob.dictionary_type
                            == DBDictionaryType.MEDDRA,
                            DictionaryImportJob.status == ImportState.COMPLETED,
                        )
                        .order_by(DictionaryImportJob.completed_at.desc())
                    )
                    res_latest = await session.execute(stmt_latest)
                    latest_job = res_latest.scalars().first()
                    version = latest_job.dictionary_version if latest_job else "26.0"
                else:
                    dict_type = DBDictionaryType.WHODRUG
                    stmt_latest = (
                        select(DictionaryImportJob)
                        .where(
                            DictionaryImportJob.dictionary_type
                            == DBDictionaryType.WHODRUG,
                            DictionaryImportJob.status == ImportState.COMPLETED,
                        )
                        .order_by(DictionaryImportJob.completed_at.desc())
                    )
                    res_latest = await session.execute(stmt_latest)
                    latest_job = res_latest.scalars().first()
                    version = latest_job.dictionary_version if latest_job else "2024-03"

                try:
                    match_res = await match_verbatim_term(
                        session=session,
                        verbatim=verbatim.strip(),
                        dictionary_type=dict_type.value,
                        version=version,
                    )
                except Exception:
                    match_res = {
                        "status": "UNCODABLE",
                        "match": None,
                        "suggestions": [],
                    }

                status = CodingState.UNCODED
                coded_code = None
                coded_term = None
                score = None
                hierarchy = None
                suggestions = None

                match_status = match_res.get("status")
                if match_status == "AUTO-CODED" and match_res.get("match"):
                    m = match_res["match"]
                    status = CodingState.AUTO_CODED
                    coded_code = m.get("code") or m.get("drug_code")
                    coded_term = m.get("term_name") or m.get("preferred_name")
                    score = m.get("score")
                    if dict_type == DBDictionaryType.MEDDRA:
                        hierarchy = m.get("hierarchies")
                    else:
                        hierarchy = {
                            "atc_context": m.get("atc_context", []),
                            "ingredients": m.get("ingredients", []),
                        }
                elif match_status == "SUGGESTIONS":
                    status = CodingState.SUGGESTED
                    suggestions = match_res.get("suggestions")
                elif match_status == "UNCODABLE":
                    status = CodingState.QUERY_PENDING

                assignment = ClinicalCodingAssignment(
                    verbatim_text=verbatim.strip(),
                    source_field=f"{payload.domain}.{payload.test_code}",
                    observation_id=obs.id,
                    dictionary_type=dict_type,
                    dictionary_version=version,
                    coded_code=coded_code,
                    coded_term=coded_term,
                    status=status,
                    score=score,
                    hierarchy=hierarchy,
                    suggestions=suggestions,
                    domain=domain_upper,
                    assigned_by="system",
                    assigned_at=datetime.utcnow(),
                )
                session.add(assignment)

                if status == CodingState.QUERY_PENDING:
                    # Check if an unresolved query already exists on this coordinate to ensure idempotency
                    stmt_q_exist = select(ClinicalQuery).where(
                        ClinicalQuery.study_id == study_id,
                        ClinicalQuery.subject_id == payload.subject_id,
                        ClinicalQuery.visit_id == payload.visit_id,
                        ClinicalQuery.domain == payload.domain,
                        ClinicalQuery.test_code == payload.test_code,
                        ClinicalQuery.status.in_(
                            ["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]
                        ),
                        ClinicalQuery.is_deleted.is_(False),
                    )
                    res_q_exist = await session.execute(stmt_q_exist)
                    existing_q = res_q_exist.scalars().first()

                    if not existing_q:
                        q_explanation = f"The verbatim term '{verbatim.strip()}' in field {payload.test_code} is uncodable. Please split into individual events or clarify spelling."
                        query = ClinicalQuery(
                            study_id=study_id,
                            subject_id=payload.subject_id,
                            visit_id=payload.visit_id,
                            domain=payload.domain,
                            test_code=payload.test_code,
                            status="OPEN",
                            explanation=q_explanation,
                            message=q_explanation,
                            observation_id=obs.id,
                            origin="SYSTEM_CODING",
                            query_type="SYSTEM_CODING",
                            form_id=f"{payload.domain.upper()}_FORM",
                            field_id=payload.test_code,
                            action_required="RE-ENTER_VERBATIM",
                            created_by="system",
                        )
                        session.add(query)

                if status == CodingState.AUTO_CODED:
                    ledger = ClinicalCodingLedger(
                        assignment_id=assignment.id,
                        verbatim_text=verbatim.strip(),
                        observation_id=obs.id,
                        dictionary_type=dict_type,
                        old_dictionary_version=None,
                        old_coded_code=None,
                        old_coded_term=None,
                        new_dictionary_version=version,
                        new_coded_code=coded_code,
                        new_coded_term=coded_term,
                        recoding_reason="Auto-coded by Medical Coding Engine",
                        decision_by="system",
                        decision_at=datetime.utcnow(),
                    )
                    session.add(ledger)

        await session.commit()

        # Recalculate outliers for this cohort
        await recalculate_cohort_outliers(session, study_id, payload.test_code)

        # Retrieve the latest state of the observation
        stmt_obs = select(ClinicalObservation).where(ClinicalObservation.id == obs.id)
        res_obs = await session.execute(stmt_obs)
        obs_db = res_obs.scalar_one()

        # Check for cascading dependent nullification first
        from apps.execution.edit_checks import handle_cascading_nullification

        await handle_cascading_nullification(session, obs_db)

        # Invoke synchronous field-level same-record edit checks directly in the active session
        await run_synchronous_edit_checks(session, obs_db)
        await session.commit()

        # Propagate audit and user context to background tasks
        user_id = current_user_id.get()
        change_reason = current_change_reason.get()

        # Check for critical lab notification dispatch
        if obs_db.lab_indicator in ("LOW LOW", "HIGH HIGH"):
            from apps.execution.notification_events import (
                dispatch_critical_lab_alerts,
            )

            dispatch_critical_lab_alerts(
                background_tasks,
                obs_db,
                obs_db.lab_indicator,
                user_id,
                change_reason,
            )

        background_tasks.add_task(
            run_asynchronous_edit_checks,
            db_manager.get_session_maker(),
            obs_db.id,
            user_id=user_id,
            change_reason=change_reason,
        )

        return ObservationResponse(
            id=obs_db.id,
            subject_id=obs_db.subject_id,
            study_id=obs_db.study_id,
            visit_id=obs_db.visit_id,
            domain=obs_db.domain,
            observation_date=obs_db.observation_date,
            test_code=obs_db.test_code,
            test_name=obs_db.test_name,
            value=obs_db.value,
            value_string=obs_db.value_string,
            unit=obs_db.unit,
            normalized_value=obs_db.normalized_value,
            normalized_unit=obs_db.normalized_unit,
            is_outlier=obs_db.is_outlier,
            lab_source=obs_db.lab_source,
            lab_site_id=obs_db.lab_site_id,
            lab_indicator=obs_db.lab_indicator,
            lab_out_of_range=obs_db.lab_out_of_range,
            matched_normal_bounds=obs_db.matched_normal_bounds,
            range_indicator=obs_db.lab_indicator,
            is_out_of_range=obs_db.lab_out_of_range,
            reference_range_low=obs_db.reference_range_low,
            reference_range_high=obs_db.reference_range_high,
            protocol_version_tag=obs_db.protocol_version_tag,
            protocol_version_index=obs_db.protocol_version_index,
        )


# ==========================================
# Unit Conversion API (Requirements & Dictionary)
# ==========================================


class UnitConversionRequest(BaseModel):
    """Pydantic schema for unit conversion requests."""

    value: float
    from_unit: str
    to_unit: str


class UnitConversionResponse(BaseModel):
    """Pydantic schema returning converted values."""

    value: float
    from_unit: str
    to_unit: str
    converted_value: float


async def perform_unit_conversion(
    payload: UnitConversionRequest,
) -> UnitConversionResponse:
    """Helper method executing unit conversion logic."""
    try:
        conv = convert_unit(payload.value, payload.from_unit, payload.to_unit)
        return UnitConversionResponse(
            value=payload.value,
            from_unit=payload.from_unit,
            to_unit=payload.to_unit,
            converted_value=conv,
        )
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))


@app.post("/api/v1/execution/unit-conversion", response_model=UnitConversionResponse)
async def post_unit_conversion_execution(
    payload: UnitConversionRequest,
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules (Execution API)."""
    return await perform_unit_conversion(payload)


@app.post("/dictionary/unit-conversion", response_model=UnitConversionResponse)
async def post_unit_conversion_dictionary(
    payload: UnitConversionRequest,
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules (Dictionary API)."""
    return await perform_unit_conversion(payload)


@app.get("/api/v1/execution/unit-conversion", response_model=UnitConversionResponse)
async def get_unit_conversion_execution(
    value: float, from_unit: str, to_unit: str
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules via GET (Execution API)."""
    return await perform_unit_conversion(
        UnitConversionRequest(value=value, from_unit=from_unit, to_unit=to_unit)
    )


@app.get("/dictionary/unit-conversion", response_model=UnitConversionResponse)
async def get_unit_conversion_dictionary(
    value: float, from_unit: str, to_unit: str
) -> UnitConversionResponse:
    """Translate incoming values using UCUM mapping rules via GET (Dictionary API)."""
    return await perform_unit_conversion(
        UnitConversionRequest(value=value, from_unit=from_unit, to_unit=to_unit)
    )


# ==========================================
# Outlier Management API
# ==========================================


class OutlierRecalculateRequest(BaseModel):
    """Pydantic schema for triggering outlier calculations."""

    study_id: str
    test_code: str


class OutlierRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status."""

    status: str
    study_id: str
    test_code: str
    outliers_found: int


@app.post(
    "/api/v1/execution/outliers/recalculate", response_model=OutlierRecalculateResponse
)
async def trigger_outlier_recalculation(
    payload: OutlierRecalculateRequest,
) -> OutlierRecalculateResponse:
    """Trigger cohort-wide outlier recalculation on-demand."""
    async with db_manager.get_session_maker()() as session:
        count = await recalculate_cohort_outliers(
            session, payload.study_id, payload.test_code
        )
        return OutlierRecalculateResponse(
            status="success",
            study_id=payload.study_id,
            test_code=payload.test_code,
            outliers_found=count,
        )


# ==========================================
# Lab Range Management API
# ==========================================


class LabReferenceRangeResponse(BaseModel):
    """Pydantic schema for returning reference range details."""

    id: str
    study_id: str
    test_code: str
    test_name: str
    source: str
    site_id: str | None = None
    unit: str
    normalized_unit: str
    sex_applicability: str
    age_low: float | None = None
    age_high: float | None = None
    low_bound: float | None = None
    high_bound: float | None = None
    critical_low: float | None = None
    critical_high: float | None = None
    version: int
    is_deleted: bool


class LabReferenceRangeCreate(BaseModel):
    """Pydantic schema for creating a reference range."""

    study_id: str
    test_code: str
    test_name: str
    source: str
    site_id: str | None = None
    unit: str
    normalized_unit: str
    sex_applicability: str
    age_low: float | None = None
    age_high: float | None = None
    low_bound: float | None = None
    high_bound: float | None = None
    critical_low: float | None = None
    critical_high: float | None = None


class LabReferenceRangeUpdate(BaseModel):
    """Pydantic schema for updating a reference range."""

    study_id: str | None = None
    test_code: str | None = None
    test_name: str | None = None
    source: str | None = None
    site_id: str | None = None
    unit: str | None = None
    normalized_unit: str | None = None
    sex_applicability: str | None = None
    age_low: float | None = None
    age_high: float | None = None
    low_bound: float | None = None
    high_bound: float | None = None
    critical_low: float | None = None
    critical_high: float | None = None


def validate_lab_range_payload(data: dict) -> None:
    """Enforces all domain/business constraints on a reference range data dictionary."""
    required_strings = [
        "study_id",
        "test_code",
        "test_name",
        "source",
        "unit",
        "normalized_unit",
        "sex_applicability",
    ]
    for field in required_strings:
        val = data.get(field)
        if val is None:
            raise HTTPException(
                status_code=400,
                detail=f"Field '{field}' is required and cannot be blank.",
            )
        if not isinstance(val, str) or not val.strip():
            raise HTTPException(
                status_code=400,
                detail=f"Field '{field}' cannot be blank.",
            )

    if "site_id" in data and data["site_id"] is not None:
        val = data["site_id"]
        if isinstance(val, str) and not val.strip():
            raise HTTPException(
                status_code=400,
                detail="Field 'site_id' cannot be blank.",
            )

    source_upper = str(data["source"]).strip().upper()
    if source_upper not in ("CENTRAL", "LOCAL"):
        raise HTTPException(
            status_code=400,
            detail="Field 'source' must be either 'CENTRAL' or 'LOCAL'.",
        )
    data["source"] = source_upper

    if source_upper == "CENTRAL" and "site_id" in data and data["site_id"] is not None:
        raise HTTPException(
            status_code=400,
            detail="CENTRAL reference ranges are global and must have site_id = None.",
        )

    sex_upper = str(data["sex_applicability"]).strip().upper()
    if sex_upper not in ("M", "F", "ALL", "U"):
        raise HTTPException(
            status_code=400,
            detail="Field 'sex_applicability' must be one of 'M', 'F', 'ALL', or 'U'.",
        )
    data["sex_applicability"] = sex_upper

    age_low = data.get("age_low")
    age_high = data.get("age_high")
    if age_low is not None:
        try:
            age_low_val = float(age_low)
            data["age_low"] = age_low_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' must be a numeric value.",
            )
        if age_low_val < 0:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' cannot be negative.",
            )

    if age_high is not None:
        try:
            age_high_val = float(age_high)
            data["age_high"] = age_high_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'age_high' must be a numeric value.",
            )
        if age_high_val < 0:
            raise HTTPException(
                status_code=400,
                detail="Field 'age_high' cannot be negative.",
            )

    if age_low is not None and age_high is not None:
        if float(age_low) > float(age_high):
            raise HTTPException(
                status_code=400,
                detail="Field 'age_low' must be less than or equal to 'age_high'.",
            )

    low_bound = data.get("low_bound")
    high_bound = data.get("high_bound")
    if low_bound is not None:
        try:
            low_bound_val = float(low_bound)
            data["low_bound"] = low_bound_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'low_bound' must be a numeric value.",
            )
    if high_bound is not None:
        try:
            high_bound_val = float(high_bound)
            data["high_bound"] = high_bound_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'high_bound' must be a numeric value.",
            )

    if low_bound is not None and high_bound is not None:
        if float(low_bound) > float(high_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'low_bound' must be less than or equal to 'high_bound'.",
            )

    critical_low = data.get("critical_low")
    critical_high = data.get("critical_high")
    if critical_low is not None:
        try:
            critical_low_val = float(critical_low)
            data["critical_low"] = critical_low_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be a numeric value.",
            )
    if critical_high is not None:
        try:
            critical_high_val = float(critical_high)
            data["critical_high"] = critical_high_val
        except (ValueError, TypeError):  # fmt: skip
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_high' must be a numeric value.",
            )

    if critical_low is not None and critical_high is not None:
        if float(critical_low) > float(critical_high):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be less than or equal to 'critical_high'.",
            )

    if critical_low is not None and low_bound is not None:
        if float(critical_low) > float(low_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be less than or equal to 'low_bound'.",
            )

    if critical_high is not None and high_bound is not None:
        if float(critical_high) < float(high_bound):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_high' must be greater than or equal to 'high_bound'.",
            )


@app.post(
    "/api/v1/execution/lab-ranges",
    response_model=LabReferenceRangeResponse,
    status_code=201,
)
async def create_lab_range(
    payload: LabReferenceRangeCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    _justification: None = Depends(verify_change_justification),
) -> LabReferenceRangeResponse:
    """CREATE a new lab reference range, validating all range invariants."""
    from apps.execution.database.models import LabReferenceRange

    data = payload.model_dump()
    validate_lab_range_payload(data)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            lab_range = LabReferenceRange(
                study_id=data["study_id"],
                test_code=data["test_code"],
                test_name=data["test_name"],
                source=data["source"],
                site_id=data.get("site_id"),
                unit=data["unit"],
                normalized_unit=data["normalized_unit"],
                sex_applicability=data["sex_applicability"],
                age_low=data.get("age_low"),
                age_high=data.get("age_high"),
                low_bound=data.get("low_bound"),
                high_bound=data.get("high_bound"),
                critical_low=data.get("critical_low"),
                critical_high=data.get("critical_high"),
            )
            session.add(lab_range)
            await session.flush()

        # Invalidate outside the transaction block to avoid race conditions
        lab_range_cache.invalidate(lab_range.study_id, lab_range.test_code)

        return LabReferenceRangeResponse(
            id=lab_range.id,
            study_id=lab_range.study_id,
            test_code=lab_range.test_code,
            test_name=lab_range.test_name,
            source=lab_range.source,
            site_id=lab_range.site_id,
            unit=lab_range.unit,
            normalized_unit=lab_range.normalized_unit,
            sex_applicability=lab_range.sex_applicability,
            age_low=lab_range.age_low,
            age_high=lab_range.age_high,
            low_bound=lab_range.low_bound,
            high_bound=lab_range.high_bound,
            critical_low=lab_range.critical_low,
            critical_high=lab_range.critical_high,
            version=lab_range.version,
            is_deleted=lab_range.is_deleted,
        )


@app.get(
    "/api/v1/execution/lab-ranges",
    response_model=list[LabReferenceRangeResponse],
)
async def list_lab_ranges(
    study_id: str | None = None,
    test_code: str | None = None,
    source: str | None = None,
    lab_source: str | None = None,
    include_deleted: bool = False,
    roles: list[str] = Depends(get_normalized_roles),
) -> list[LabReferenceRangeResponse]:
    """List and filter reference ranges."""
    from apps.execution.database.models import LabReferenceRange

    async with db_manager.get_session_maker()() as session:
        stmt = select(LabReferenceRange)
        if not include_deleted:
            stmt = stmt.where(LabReferenceRange.is_deleted.is_(False))

        if study_id:
            stmt = stmt.where(LabReferenceRange.study_id == study_id)
        if test_code:
            stmt = stmt.where(LabReferenceRange.test_code == test_code)
        if source:
            stmt = stmt.where(LabReferenceRange.source == source)
        if lab_source:
            stmt = stmt.where(LabReferenceRange.source == lab_source)

        res = await session.execute(stmt)
        ranges = res.scalars().all()

        return [
            LabReferenceRangeResponse(
                id=r.id,
                study_id=r.study_id,
                test_code=r.test_code,
                test_name=r.test_name,
                source=r.source,
                site_id=r.site_id,
                unit=r.unit,
                normalized_unit=r.normalized_unit,
                sex_applicability=r.sex_applicability,
                age_low=r.age_low,
                age_high=r.age_high,
                low_bound=r.low_bound,
                high_bound=r.high_bound,
                critical_low=r.critical_low,
                critical_high=r.critical_high,
                version=r.version,
                is_deleted=r.is_deleted,
            )
            for r in ranges
        ]


@app.get(
    "/api/v1/execution/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def get_lab_range(
    range_id: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> LabReferenceRangeResponse:
    """Retrieve a single lab reference range."""
    from apps.execution.database.models import LabReferenceRange

    async with db_manager.get_session_maker()() as session:
        stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
        res = await session.execute(stmt)
        r = res.scalars().first()
        if not r:
            raise HTTPException(status_code=404, detail="LabReferenceRange not found")

        return LabReferenceRangeResponse(
            id=r.id,
            study_id=r.study_id,
            test_code=r.test_code,
            test_name=r.test_name,
            source=r.source,
            site_id=r.site_id,
            unit=r.unit,
            normalized_unit=r.normalized_unit,
            sex_applicability=r.sex_applicability,
            age_low=r.age_low,
            age_high=r.age_high,
            low_bound=r.low_bound,
            high_bound=r.high_bound,
            critical_low=r.critical_low,
            critical_high=r.critical_high,
            version=r.version,
            is_deleted=r.is_deleted,
        )


@app.put(
    "/api/v1/execution/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def update_lab_range(
    range_id: str,
    payload: LabReferenceRangeUpdate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    _justification: None = Depends(verify_change_justification),
) -> LabReferenceRangeResponse:
    """UPDATE an existing lab reference range, validating all range invariants on the merged state."""
    from apps.execution.database.models import LabReferenceRange

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            res = await session.execute(stmt)
            r = res.scalars().first()
            if not r:
                raise HTTPException(
                    status_code=404, detail="LabReferenceRange not found"
                )

            # Capture original study_id and test_code from the loaded row before they are overwritten
            original_study_id = r.study_id
            original_test_code = r.test_code

            update_dict = payload.model_dump(exclude_unset=True)
            merged_data = {
                "study_id": r.study_id,
                "test_code": r.test_code,
                "test_name": r.test_name,
                "source": r.source,
                "site_id": r.site_id,
                "unit": r.unit,
                "normalized_unit": r.normalized_unit,
                "sex_applicability": r.sex_applicability,
                "age_low": r.age_low,
                "age_high": r.age_high,
                "low_bound": r.low_bound,
                "high_bound": r.high_bound,
                "critical_low": r.critical_low,
                "critical_high": r.critical_high,
            }
            for key, val in update_dict.items():
                merged_data[key] = val

            validate_lab_range_payload(merged_data)

            r.study_id = merged_data["study_id"]
            r.test_code = merged_data["test_code"]
            r.test_name = merged_data["test_name"]
            r.source = merged_data["source"]
            r.site_id = merged_data["site_id"]
            r.unit = merged_data["unit"]
            r.normalized_unit = merged_data["normalized_unit"]
            r.sex_applicability = merged_data["sex_applicability"]
            r.age_low = merged_data["age_low"]
            r.age_high = merged_data["age_high"]
            r.low_bound = merged_data["low_bound"]
            r.high_bound = merged_data["high_bound"]
            r.critical_low = merged_data["critical_low"]
            r.critical_high = merged_data["critical_high"]
            await session.flush()

        # Invalidate outside the transaction block to avoid race conditions
        lab_range_cache.invalidate(original_study_id, original_test_code)
        if original_study_id != r.study_id or original_test_code != r.test_code:
            lab_range_cache.invalidate(r.study_id, r.test_code)

        return LabReferenceRangeResponse(
            id=r.id,
            study_id=r.study_id,
            test_code=r.test_code,
            test_name=r.test_name,
            source=r.source,
            site_id=r.site_id,
            unit=r.unit,
            normalized_unit=r.normalized_unit,
            sex_applicability=r.sex_applicability,
            age_low=r.age_low,
            age_high=r.age_high,
            low_bound=r.low_bound,
            high_bound=r.high_bound,
            critical_low=r.critical_low,
            critical_high=r.critical_high,
            version=r.version,
            is_deleted=r.is_deleted,
        )


@app.delete(
    "/api/v1/execution/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def delete_lab_range(
    range_id: str,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    _justification: None = Depends(verify_change_justification),
) -> LabReferenceRangeResponse:
    """Soft-delete a lab reference range by setting is_deleted = True."""
    from apps.execution.database.models import LabReferenceRange

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            res = await session.execute(stmt)
            r = res.scalars().first()
            if not r:
                raise HTTPException(
                    status_code=404, detail="LabReferenceRange not found"
                )

            r.is_deleted = True
            await session.flush()

        # Invalidate outside the transaction block to avoid race conditions
        lab_range_cache.invalidate(r.study_id, r.test_code)

        return LabReferenceRangeResponse(
            id=r.id,
            study_id=r.study_id,
            test_code=r.test_code,
            test_name=r.test_name,
            source=r.source,
            site_id=r.site_id,
            unit=r.unit,
            normalized_unit=r.normalized_unit,
            sex_applicability=r.sex_applicability,
            age_low=r.age_low,
            age_high=r.age_high,
            low_bound=r.low_bound,
            high_bound=r.high_bound,
            critical_low=r.critical_low,
            critical_high=r.critical_high,
            version=r.version,
            is_deleted=r.is_deleted,
        )


class LabRangeRecalculateRequest(BaseModel):
    """Pydantic schema for triggering lab range recalculations."""

    study_id: str
    test_code: str


class LabRangeRecalculateResponse(BaseModel):
    """Pydantic schema returning recalculation status."""

    status: str
    study_id: str
    test_code: str
    updated_count: int


@app.post(
    "/api/v1/execution/lab-ranges/recalculate",
    response_model=LabRangeRecalculateResponse,
)
async def trigger_lab_range_recalculation(
    payload: LabRangeRecalculateRequest,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
    _justification=Depends(verify_change_justification),
) -> LabRangeRecalculateResponse:
    """Trigger cohort-wide reference range evaluation and recalculation on-demand."""
    from apps.execution.lab_ranges import recalculate_range_flags

    # Deliberately omitting audit_context(...) wrapper here since this endpoint
    # executes inside the HTTP request lifecycle. GatewayAuthMiddleware and ContextResetMiddleware
    # automatically capture and bind current_user_id and current_change_reason ContextVars
    # before execution, allowing the before_flush event listener to log attributed updates.
    async with db_manager.get_session_maker()() as session:
        count = await recalculate_range_flags(
            session, payload.study_id, payload.test_code, background_tasks
        )
        # Invalidate after recalculation
        lab_range_cache.invalidate(payload.study_id, payload.test_code)
        return LabRangeRecalculateResponse(
            status="success",
            study_id=payload.study_id,
            test_code=payload.test_code,
            updated_count=count,
        )


# ==========================================
# CDISC XML Export API
# ==========================================


async def generate_cdisc_export_xml(study_id: str) -> str:
    """Query stored active clinical subject observations and generate CDISC compliant XML."""
    async with db_manager.get_session_maker()() as session:
        # Fetch active observations and join visit name
        stmt = (
            select(ClinicalObservation, ClinicalVisit.visit_name)
            .outerjoin(ClinicalVisit, ClinicalObservation.visit_id == ClinicalVisit.id)
            .where(
                ClinicalObservation.study_id == study_id,
                ClinicalObservation.is_deleted.is_(False),
            )
        )
        res = await session.execute(stmt)
        rows = res.all()

        if not rows:
            raise HTTPException(
                status_code=404,
                detail=f"No active observations found for study {study_id}",
            )

        # Unpack observations and visit names
        raw_obs = [row[0] for row in rows]
        visit_names_by_obs_id = {
            row[0].id: row[1] for row in rows if row[0].id is not None
        }

        # Dynamic non-destructive protocol reconciliation
        from apps.execution.migration_rules import reconcile_observations

        stmt_target_version = (
            select(SubjectConsent.version_tag)
            .where(SubjectConsent.study_id == study_id)
            .order_by(SubjectConsent.version_index.desc())
            .limit(1)
        )
        res_target = await session.execute(stmt_target_version)
        target_version = res_target.scalar() or "1.0"
        reconciled_obs = await reconcile_observations(session, raw_obs, target_version)

        subjects = {}
        for obs in reconciled_obs:
            subj_key = obs.subject_id
            vname = visit_names_by_obs_id.get(obs.id) or "Baseline"
            if subj_key not in subjects:
                subjects[subj_key] = {"visits": {}}
            if vname not in subjects[subj_key]["visits"]:
                subjects[subj_key]["visits"][vname] = []
            subjects[subj_key]["visits"][vname].append(obs)

        # Render using Jinja2 templates
        from jinja2 import Environment, FileSystemLoader, select_autoescape

        templates_dir = os.path.join(os.path.dirname(__file__), "templates")
        env = Environment(
            loader=FileSystemLoader(templates_dir),
            autoescape=select_autoescape(["html", "xml"]),
        )
        template = env.get_template("cdisc_export_template.xml.j2")

        xml_content = template.render(
            study_id=study_id,
            creation_datetime=datetime.utcnow().isoformat() + "Z",
            subjects=subjects,
        )

        # Validate structural compliance
        is_valid, msg = validate_cdisc_xml_structure(xml_content)
        if not is_valid:
            raise HTTPException(
                status_code=500,
                detail=f"Generated CDISC XML failed structural schema checks: {msg}",
            )

        return xml_content


@app.get("/api/v1/execution/export")
async def get_cdisc_export_execution(study_id: str) -> Response:
    """Export stored clinical subject observations in CDISC ODM XML format (Execution API)."""
    xml_content = await generate_cdisc_export_xml(study_id)
    return Response(content=xml_content, media_type="application/xml")


@app.get("/dictionary/export")
async def get_cdisc_export_dictionary(study_id: str) -> Response:
    """Export stored clinical subject observations in CDISC ODM XML format (Dictionary API)."""
    xml_content = await generate_cdisc_export_xml(study_id)
    return Response(content=xml_content, media_type="application/xml")


# ==========================================
# Form Submission & PI Sign-off API
# ==========================================

VALID_SIGNING_REASONS = {
    "I attest that this data is accurate and complete.",
    "PI approval and sign-off.",
    "Review and confirmation.",
    "DATA_RECORDING",
    "DATA_ENTRY_COMPLETED",
    "PI_REVIEW",
    "PI_SIGN_OFF",
    "COMPLIANCE_ATTESTATION",
}


class FormSubmissionStatusEnum(StrEnum):
    DRAFT = FormSubmissionStatus.DRAFT.value
    COMPLETED = FormSubmissionStatus.COMPLETED.value
    APPROVED = FormSubmissionStatus.APPROVED.value


class SigningReasonCode(StrEnum):
    DATA_RECORDING = "DATA_RECORDING"
    PI_APPROVAL = "PI_APPROVAL"
    REVIEW_CONFIRMATION = "REVIEW_CONFIRMATION"
    COMPLIANCE_ATTESTATION = "COMPLIANCE_ATTESTATION"


class FormSubmissionCreate(BaseModel):
    study_id: str
    site_id: str
    subject_id: str
    visit_id: str | None = None
    form_id: str
    protocol_version: str | None = None
    payload: dict[str, Any] | None = None


class FormSubmissionResponse(BaseModel):
    id: str
    study_id: str
    site_id: str | None
    subject_id: str
    visit_id: str | None = None
    form_id: str
    status: FormSubmissionStatusEnum
    version: int
    is_deleted: bool
    signature_manifest: dict[str, Any] | None = None
    protocol_version: str | None = None
    payload: dict[str, Any] | None = None
    is_active: bool
    is_readonly: bool
    cloned_from_id: str | None = None


class FormSubmissionApprove(BaseModel):
    signature_manifest: dict[str, Any]
    signing_reason: str


class BatchSignOffRequest(BaseModel):
    study_id: str
    target_type: str  # "FORM", "VISIT", or "SUBJECT"
    target_ids: list[str]
    signing_reason: str

    @model_validator(mode="after")
    def validate_request(self) -> BatchSignOffRequest:
        tt = self.target_type.upper()
        if tt not in ("FORM", "VISIT", "SUBJECT"):
            raise ValueError("target_type must be one of: FORM, VISIT, SUBJECT")
        if self.signing_reason not in VALID_SIGNING_REASONS:
            raise ValueError(
                f"Invalid signing reason. Must be one of: {sorted(list(VALID_SIGNING_REASONS))}"
            )
        return self


class BatchSignOffResponse(BaseModel):
    status: str
    approved_submission_ids: list[str]
    skipped_submission_ids: list[str]
    skipped_targets: list[str]


@app.post(
    "/api/v1/execution/form-submissions",
    response_model=FormSubmissionResponse,
    status_code=201,
)
async def create_form_submission(
    request: Request,
    payload: FormSubmissionCreate,
    roles: list[str] = Depends(verify_not_auditor),
) -> FormSubmissionResponse:
    """Create a new FormSubmission in DRAFT status."""
    async with db_manager.get_session_maker()() as session:
        sub = FormSubmission(
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            form_id=payload.form_id,
            status="DRAFT",
            signature_manifest=None,
            protocol_version=payload.protocol_version,
            payload=payload.payload,
            is_active=True,
            is_readonly=False,
            cloned_from_id=None,
        )
        session.add(sub)
        await session.commit()

        # Query back to get the database values
        stmt = select(FormSubmission).where(FormSubmission.id == sub.id)
        res = await session.execute(stmt)
        sub_db = res.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@app.post(
    "/api/v1/execution/form-submissions/{submission_id}/complete",
    response_model=FormSubmissionResponse,
)
async def complete_form_submission(
    submission_id: str,
    request: Request,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
) -> FormSubmissionResponse:
    """Transition a FormSubmission from DRAFT to COMPLETED."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        if sub.is_readonly or not sub.is_active:
            raise HTTPException(
                status_code=400,
                detail="Cannot modify a read-only or inactive form submission.",
            )

        if sub.status != "DRAFT":
            raise HTTPException(
                status_code=400,
                detail=f"Form submission can only be completed from DRAFT status. Current: {sub.status}",
            )

        sub.status = "COMPLETED"
        await session.commit()

        user_id = current_user_id.get() or "system"
        change_reason = current_change_reason.get() or "Form Completion Edit Checks"

        # Enqueue exactly one background form-level edit check task
        background_tasks.add_task(
            run_asynchronous_form_edit_checks,
            db_manager.get_session_maker(),
            sub.id,
            user_id=user_id,
            change_reason=change_reason,
        )

        # Query back
        stmt_ref = select(FormSubmission).where(FormSubmission.id == submission_id)
        res_ref = await session.execute(stmt_ref)
        sub_db = res_ref.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@app.post(
    "/api/v1/execution/form-submissions/{submission_id}/approve",
    response_model=FormSubmissionResponse,
)
async def approve_form_submission(
    submission_id: str,
    request: Request,
    payload: FormSubmissionApprove,
    roles: list[str] = Depends(require_roles(ROLE_SITE_INVESTIGATOR)),
) -> FormSubmissionResponse:
    """PI Approve/Sign-off a completed FormSubmission."""
    # Validate signing reason
    if payload.signing_reason not in VALID_SIGNING_REASONS:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid signing reason. Must be one of: {sorted(list(VALID_SIGNING_REASONS))}",
        )

    # Validate signature manifest is non-empty
    if not payload.signature_manifest:
        raise HTTPException(
            status_code=400,
            detail="Signature manifest is required for PI sign-off.",
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        if sub.is_readonly or not sub.is_active:
            raise HTTPException(
                status_code=400,
                detail="Cannot modify a read-only or inactive form submission.",
            )

        if sub.status != "COMPLETED":
            raise HTTPException(
                status_code=400,
                detail=f"PI approval must only be possible from COMPLETED status. Current: {sub.status}",
            )

        sub.status = "APPROVED"
        sub.signature_manifest = payload.signature_manifest
        await session.commit()

        # Query back
        stmt_ref = select(FormSubmission).where(FormSubmission.id == submission_id)
        res_ref = await session.execute(stmt_ref)
        sub_db = res_ref.scalar_one()

        return FormSubmissionResponse(
            id=sub_db.id,
            study_id=sub_db.study_id,
            site_id=sub_db.site_id,
            subject_id=sub_db.subject_id,
            visit_id=sub_db.visit_id,
            form_id=sub_db.form_id,
            status=sub_db.status,
            version=sub_db.version,
            is_deleted=sub_db.is_deleted,
            signature_manifest=sub_db.signature_manifest,
            protocol_version=sub_db.protocol_version,
            payload=sub_db.payload,
            is_active=sub_db.is_active,
            is_readonly=sub_db.is_readonly,
            cloned_from_id=sub_db.cloned_from_id,
        )


@app.post(
    "/api/v1/execution/batch-sign-off",
    response_model=BatchSignOffResponse,
)
async def post_batch_sign_off(
    request: Request,
    payload: BatchSignOffRequest,
    roles: list[str] = Depends(
        require_roles(
            ROLE_SITE_INVESTIGATOR,
            detail="Forbidden: Only a Principal Investigator (PI) can perform batch electronic sign-off.",
        )
    ),
) -> BatchSignOffResponse:
    """Perform a PI-only, atomic batch electronic-signature for form-, visit-, and subject-level sign-off."""
    # Secondary safety validation of the signature token batch-binding
    sig_token = request.headers.get("X-Sig-Token")
    user_id = getattr(request.state, "user_id", "")

    from packages.security.sig_token_verifier import verify_and_consume_sig_token

    sig_payload = verify_and_consume_sig_token(sig_token, user_id)

    token_batch_id = sig_payload.get("batch_id")
    if not token_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    # Compute expected batch_id
    norm_study = str(payload.study_id).strip()
    norm_type = str(payload.target_type).strip().upper()
    sorted_ids = sorted([str(tid).strip() for tid in payload.target_ids])
    norm_ids = ",".join(sorted_ids)
    norm_reason = str(payload.signing_reason).strip()

    binding_str = f"{norm_study}:{norm_type}:{norm_ids}:{norm_reason}"
    import hashlib

    computed_batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

    if token_batch_id != computed_batch_id:
        raise HTTPException(
            status_code=401,
            detail="REAUTHENTICATION_REQUIRED",
        )

    target_type_upper = payload.target_type.upper()

    async with db_manager.get_session_maker()() as session:
        if TrialLockManager.is_locked():
            raise PermissionError(
                "Trial is currently locked in a read-only state due to a security violation."
            )

        async with session.begin():
            if target_type_upper == "FORM":
                stmt = select(FormSubmission).where(
                    FormSubmission.id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            elif target_type_upper == "VISIT":
                stmt = select(FormSubmission).where(
                    FormSubmission.visit_id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            elif target_type_upper == "SUBJECT":
                stmt = select(FormSubmission).where(
                    FormSubmission.subject_id.in_(payload.target_ids),
                    FormSubmission.study_id == payload.study_id,
                    FormSubmission.is_deleted.is_(False),
                )
            else:
                raise HTTPException(status_code=400, detail="Invalid target type.")

            res = await session.execute(stmt)
            resolved_subs = list(res.scalars().all())

            approved_submission_ids = []
            skipped_submission_ids = []
            targets_with_approvals = set()

            secret = os.getenv(
                "GATEWAY_SECRET", "internal-gateway-secret-12345"
            ).encode()

            for sub in resolved_subs:
                if sub.is_readonly or not sub.is_active:
                    skipped_submission_ids.append(sub.id)
                    continue

                sub_target_id = None
                if target_type_upper == "FORM":
                    sub_target_id = sub.id
                elif target_type_upper == "VISIT":
                    sub_target_id = sub.visit_id
                elif target_type_upper == "SUBJECT":
                    sub_target_id = sub.subject_id

                if sub.status == "COMPLETED":
                    if sub.site_id and TrialLockManager.is_site_locked(sub.site_id):
                        raise PermissionError(
                            f"Site {sub.site_id} is currently locked in a read-only state."
                        )
                    if sub.visit_id and TrialLockManager.is_visit_locked(sub.visit_id):
                        raise PermissionError(
                            f"Visit {sub.visit_id} is currently locked in a read-only state."
                        )
                    if sub.subject_id and TrialLockManager.is_subject_locked(
                        sub.subject_id
                    ):
                        raise PermissionError(
                            f"Subject {sub.subject_id} is currently locked in a read-only state."
                        )
                    if sub.form_id and TrialLockManager.is_form_locked(sub.form_id):
                        raise PermissionError(
                            f"Form {sub.form_id} is currently locked in a read-only state."
                        )

                    if sub_target_id:
                        targets_with_approvals.add(sub_target_id)

                    form_payload = {
                        "study_id": sub.study_id,
                        "site_id": sub.site_id,
                        "subject_id": sub.subject_id,
                        "visit_id": sub.visit_id,
                        "form_id": sub.form_id,
                    }
                    binding_payload = {
                        "form_payload": form_payload,
                        "pre_approval_version": sub.version,
                    }
                    canonical_hash = generate_canonical_signature(
                        binding_payload, secret
                    )

                    username = request.state.user_id or "unknown"
                    full_name = (
                        f"{username.replace('_', ' ').replace('.', ' ').title()}"
                    )
                    if "pi" in username.lower() or "investigator" in username.lower():
                        full_name += ", MD"

                    signing_timestamp_utc = datetime.utcnow().isoformat() + "Z"

                    reason_mapping = {
                        "I attest that this data is accurate and complete.": (
                            "DATA_RECORDING",
                            "I attest that this data is accurate and complete.",
                        ),
                        "PI approval and sign-off.": (
                            "PI_APPROVAL",
                            "I approve this clinical record and confirm medical responsibility.",
                        ),
                        "Review and confirmation.": (
                            "REVIEW_CONFIRMATION",
                            "Review and confirmation.",
                        ),
                        "DATA_RECORDING": ("DATA_RECORDING", "I author this data"),
                        "DATA_ENTRY_COMPLETED": (
                            "DATA_RECORDING",
                            "I author this data",
                        ),
                        "PI_REVIEW": ("PI_APPROVAL", "I approve this clinical record"),
                        "PI_SIGN_OFF": (
                            "PI_APPROVAL",
                            "I approve this clinical record and confirm medical responsibility.",
                        ),
                        "COMPLIANCE_ATTESTATION": (
                            "COMPLIANCE_ATTESTATION",
                            "I review and confirm this data",
                        ),
                    }

                    reason_key = payload.signing_reason
                    if reason_key in reason_mapping:
                        signing_reason_code, signing_reason_text = reason_mapping[
                            reason_key
                        ]
                    else:
                        signing_reason_code = reason_key.replace(" ", "_").upper()
                        signing_reason_text = reason_key

                    network_ip_address = request.headers.get("x-forwarded-for") or (
                        request.client.host if request.client else "127.0.0.1"
                    )
                    device_user_agent = request.headers.get("user-agent") or "Unknown"

                    manifest = {
                        # Old keys for backward compatibility
                        "signer_id": username,
                        "timestamp": signing_timestamp_utc,
                        "signing_reason": payload.signing_reason,
                        "ip_address": network_ip_address,
                        "user_agent": device_user_agent,
                        "signed_version": sub.version + 1,
                        "canonical_signature_hash": canonical_hash,
                        # New detailed vocabulary matching 05_Security_Compliance_Audit_Spec.md §4.2
                        "signature_manifestation": {
                            "signer_username": username,
                            "signer_full_name": full_name,
                            "signing_timestamp_utc": signing_timestamp_utc,
                            "signing_reason_code": signing_reason_code,
                            "signing_reason_text": signing_reason_text,
                            "network_ip_address": network_ip_address,
                            "device_user_agent": device_user_agent,
                            "record_id": sub.id,
                            "record_version": sub.version + 1,
                            "signature_hash_sha256": canonical_hash,
                        },
                    }

                    sub.status = "APPROVED"
                    sub.signature_manifest = manifest
                    session.add(sub)
                    approved_submission_ids.append(sub.id)
                else:
                    skipped_submission_ids.append(sub.id)

            skipped_targets = []
            for tid in payload.target_ids:
                if tid not in targets_with_approvals:
                    skipped_targets.append(tid)

            return BatchSignOffResponse(
                status="success",
                approved_submission_ids=approved_submission_ids,
                skipped_submission_ids=skipped_submission_ids,
                skipped_targets=skipped_targets,
            )


@app.get(
    "/api/v1/execution/form-submissions",
    response_model=list[FormSubmissionResponse],
)
async def list_form_submissions(
    study_id: str | None = None,
    subject_id: str | None = None,
    visit_id: str | None = None,
    form_id: str | None = None,
    include_inactive: bool = False,
    principal: Principal = Depends(get_principal),
) -> list[FormSubmissionResponse]:
    """List form submissions with filters."""
    if study_id and not can_access_study(principal, study_id):
        return []

    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(FormSubmission.is_deleted.is_(False))
        if not include_inactive:
            stmt = stmt.where(FormSubmission.is_active.is_(True))
        if study_id:
            stmt = stmt.where(FormSubmission.study_id == study_id)
        if subject_id:
            stmt = stmt.where(FormSubmission.subject_id == subject_id)
        if visit_id:
            stmt = stmt.where(FormSubmission.visit_id == visit_id)
        if form_id:
            stmt = stmt.where(FormSubmission.form_id == form_id)

        user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]
        if user_site_roles or principal.assigned_sites:
            stmt = stmt.where(FormSubmission.site_id.in_(principal.assigned_sites))

        if principal.assigned_studies:
            stmt = stmt.where(FormSubmission.study_id.in_(principal.assigned_studies))

        res = await session.execute(stmt)
        subs = res.scalars().all()

        return [
            redact_response(
                FormSubmissionResponse(
                    id=sub.id,
                    study_id=sub.study_id,
                    site_id=sub.site_id,
                    subject_id=sub.subject_id,
                    visit_id=sub.visit_id,
                    form_id=sub.form_id,
                    status=sub.status,
                    version=sub.version,
                    is_deleted=sub.is_deleted,
                    signature_manifest=sub.signature_manifest,
                    protocol_version=sub.protocol_version,
                    payload=sub.payload,
                    is_active=sub.is_active,
                    is_readonly=sub.is_readonly,
                    cloned_from_id=sub.cloned_from_id,
                ),
                principal,
            )
            for sub in subs
        ]


@app.get(
    "/api/v1/execution/form-submissions/{submission_id}",
    response_model=FormSubmissionResponse,
)
async def get_form_submission(
    submission_id: str,
    principal: Principal = Depends(get_principal),
) -> FormSubmissionResponse:
    """Retrieve a single form submission by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        verify_site_access(
            principal, sub.site_id, study_id=sub.study_id, subject_id=sub.subject_id
        )

        return redact_response(
            FormSubmissionResponse(
                id=sub.id,
                study_id=sub.study_id,
                site_id=sub.site_id,
                subject_id=sub.subject_id,
                visit_id=sub.visit_id,
                form_id=sub.form_id,
                status=sub.status,
                version=sub.version,
                is_deleted=sub.is_deleted,
                signature_manifest=sub.signature_manifest,
                protocol_version=sub.protocol_version,
                payload=sub.payload,
                is_active=sub.is_active,
                is_readonly=sub.is_readonly,
                cloned_from_id=sub.cloned_from_id,
            ),
            principal,
        )


class LockStatusResponse(BaseModel):
    """Pydantic model representing the active locking/freezing state of the system."""

    locked_sites: list[str]
    locked_visits: list[str]
    locked_forms: list[str]
    locked_subjects: list[str]
    trial_locked: bool


@app.get(
    "/api/v1/execution/locks",
    response_model=LockStatusResponse,
)
async def get_lock_status(
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_CRA)),
) -> LockStatusResponse:
    """Retrieve the current lock/freeze status of sites, visits, forms, subjects, and study-wide trial."""
    return LockStatusResponse(
        locked_sites=list(TrialLockManager._locked_sites),
        locked_visits=list(TrialLockManager._locked_visits),
        locked_forms=list(TrialLockManager._locked_forms),
        locked_subjects=list(TrialLockManager._locked_subjects),
        trial_locked=TrialLockManager.is_locked(),
    )


@app.post("/api/v1/execution/locks/site/{site_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/site/{site_id}/freeze", status_code=200)
async def lock_site_endpoint(
    site_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Locks or freezes a specific site."""
    TrialLockManager.lock_site(site_id)
    return {"status": "success", "message": f"Site {site_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/site/{site_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/site/{site_id}/unfreeze", status_code=200)
async def unlock_site_endpoint(
    site_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific site."""
    TrialLockManager.unlock_site(site_id)
    return {"status": "success", "message": f"Site {site_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/visit/{visit_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/visit/{visit_id}/freeze", status_code=200)
async def lock_visit_endpoint(
    visit_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Locks or freezes a specific visit."""
    TrialLockManager.lock_visit(visit_id)
    return {"status": "success", "message": f"Visit {visit_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/visit/{visit_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/visit/{visit_id}/unfreeze", status_code=200)
async def unlock_visit_endpoint(
    visit_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific visit."""
    TrialLockManager.unlock_visit(visit_id)
    return {"status": "success", "message": f"Visit {visit_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/form/{form_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/form/{form_id}/freeze", status_code=200)
async def lock_form_endpoint(
    form_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Locks or freezes a specific form."""
    TrialLockManager.lock_form(form_id)
    return {"status": "success", "message": f"Form {form_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/form/{form_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/form/{form_id}/unfreeze", status_code=200)
async def unlock_form_endpoint(
    form_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific form."""
    TrialLockManager.unlock_form(form_id)
    return {"status": "success", "message": f"Form {form_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/subject/{subject_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/subject/{subject_id}/freeze", status_code=200)
async def lock_subject_endpoint(
    subject_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Locks or freezes a specific subject."""
    TrialLockManager.lock_subject(subject_id)
    return {"status": "success", "message": f"Subject {subject_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/subject/{subject_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/subject/{subject_id}/unfreeze", status_code=200)
async def unlock_subject_endpoint(
    subject_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific subject."""
    TrialLockManager.unlock_subject(subject_id)
    return {
        "status": "success",
        "message": f"Subject {subject_id} is unlocked/unfrozen.",
    }


@app.post("/api/v1/execution/locks/trial/lock", status_code=200)
@app.post("/api/v1/execution/locks/trial/freeze", status_code=200)
async def lock_trial_endpoint(
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Locks or freezes the trial/study."""
    reason = request.headers.get("X-Change-Reason", "Sponsor Lock")
    user_id = request.headers.get("X-User-Id", "admin_user")

    # 1. Update in-memory state
    TrialLockManager.lock_trial(reason=reason)

    # 2. Commit status change and write an outbox record inside a single relational transaction
    import uuid

    from apps.execution.database.models import IntegrationOutbox

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            outbox_entry = IntegrationOutbox(
                id=str(uuid.uuid4()),
                event_type="TRIAL_LOCK",
                payload={"trial_locked": True, "reason": reason},
                status="PENDING",
                attempts=0,
                correlation_id=f"lock-{uuid.uuid4().hex[:12]}",
                created_by=user_id,
                reason_for_change=reason,
            )
            session.add(outbox_entry)

    return {"status": "success", "message": "Trial is locked/frozen."}


@app.post("/api/v1/execution/locks/trial/unlock", status_code=200)
@app.post("/api/v1/execution/locks/trial/unfreeze", status_code=200)
async def unlock_trial_endpoint(
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER, ROLE_SPONSOR_ADMIN)),
) -> dict[str, str]:
    """Unlocks or unfreezes the trial/study."""
    TrialLockManager.unlock_trial()
    return {"status": "success", "message": "Trial is unlocked/unfrozen."}


# ==========================================
# RTSM Supply Chain & Inventory Management API
# ==========================================


class DispenseRequest(BaseModel):
    study_id: str
    site_id: str
    subject_id: str
    visit_id: str
    kit_id: str
    quantity: int = Field(default=1, ge=1)


class DispenseResponse(BaseModel):
    status: str
    message: str
    resupply_triggered: bool


@app.post(
    "/api/v1/execution/rtsm/dispense",
    response_model=DispenseResponse,
    status_code=201,
)
async def dispense_kit_endpoint(
    request: Request,
    payload: DispenseRequest,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRC,
            ROLE_INVESTIGATOR,
            ROLE_CRA,
            detail="Forbidden: User role is not authorized for RTSM supply dispensation.",
        )
    ),
) -> DispenseResponse:
    """End-point to dispense investigational product (IP) kits against site inventory.

    Checks site locks early, calls dispense_kit_transaction, and handles commits atomically.
    Launches resupply alerts via fastapi background tasks post-commit if triggered.
    """
    # Proactively check site lock early
    if TrialLockManager.is_site_locked(payload.site_id):
        raise HTTPException(
            status_code=423,
            detail=f"Site {payload.site_id} is currently locked in a read-only state.",
        )

    # Standard async db session maker pattern
    async with db_manager.get_session_maker()() as session:
        try:
            # Execute transactional kit dispensation logic
            resupply_triggered = await dispense_kit_transaction(
                session=session,
                study_id=payload.study_id,
                site_id=payload.site_id,
                subject_id=payload.subject_id,
                visit_id=payload.visit_id,
                kit_id=payload.kit_id,
                quantity=payload.quantity,
            )

            # Atomic commit of the session (saving KitDispensation, SiteInventory update, and ResupplyEvent)
            await session.commit()

        except SiteInventoryNotFoundError as e:
            raise HTTPException(status_code=404, detail=str(e))
        except InsufficientStockError as e:
            raise HTTPException(status_code=400, detail=str(e))
        except PermissionError as e:
            raise HTTPException(status_code=423, detail=str(e))
        except Exception as e:
            await session.rollback()
            raise HTTPException(
                status_code=500, detail=f"Internal database error: {str(e)}"
            )

    # Schedule resupply notification post-commit if triggered
    if resupply_triggered:

        def dispatch_resupply_notification(
            site_id: str, kit_id: str, requested_qty: int
        ):
            from apps.execution.trial_lock import NotificationRouter

            router = NotificationRouter()
            payload_notif = {
                "message": f"Resupply triggered for site {site_id}, kit {kit_id}. Requested quantity: {requested_qty}",
                "site_id": site_id,
                "kit_id": kit_id,
                "requested_qty": requested_qty,
                "related_entity_type": "site-inventory",
                "related_entity_id": f"{site_id}:{kit_id}",
            }
            router.send_dashboard_notification(["supply_manager"], payload_notif)

        background_tasks.add_task(
            dispatch_resupply_notification,
            payload.site_id,
            payload.kit_id,
            20,  # default requested qty
        )

    return DispenseResponse(
        status="success",
        message=f"Successfully dispensed {payload.quantity} of kit {payload.kit_id} to subject {payload.subject_id}.",
        resupply_triggered=resupply_triggered,
    )


@app.post(
    "/api/v1/execution/migration-rules",
    response_model=MigrationRuleResponse,
    status_code=201,
)
async def create_migration_rule(
    payload: MigrationRuleCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> MigrationRuleResponse:
    """Create a new protocol version migration rule."""
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            rule = MigrationRule(
                study_id=payload.study_id,
                source_version=payload.source_version,
                target_version=payload.target_version,
                rule_type=payload.rule_type,
                source_field=payload.source_field,
                target_field=payload.target_field,
                default_value_string=payload.default_value_string,
                default_value_float=payload.default_value_float,
            )
            session.add(rule)

        # Retrieve
        stmt = select(MigrationRule).where(MigrationRule.id == rule.id)
        res = await session.execute(stmt)
        saved = res.scalar_one()

        return MigrationRuleResponse(
            id=saved.id,
            study_id=saved.study_id,
            source_version=saved.source_version,
            target_version=saved.target_version,
            rule_type=saved.rule_type,
            source_field=saved.source_field,
            target_field=saved.target_field,
            default_value_string=saved.default_value_string,
            default_value_float=saved.default_value_float,
        )


@app.get(
    "/api/v1/execution/migration-rules",
    response_model=list[MigrationRuleResponse],
)
async def list_migration_rules(
    study_id: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> list[MigrationRuleResponse]:
    """List migration rules for a clinical study."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(MigrationRule).where(
            MigrationRule.study_id == study_id,
            MigrationRule.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        rules = res.scalars().all()
        return [
            MigrationRuleResponse(
                id=r.id,
                study_id=r.study_id,
                source_version=r.source_version,
                target_version=r.target_version,
                rule_type=r.rule_type,
                source_field=r.source_field,
                target_field=r.target_field,
                default_value_string=r.default_value_string,
                default_value_float=r.default_value_float,
            )
            for r in rules
        ]


_last_verification_status = {
    "verified": True,
    "message": "GxP clinical execution ledger chain fully verified and structurally intact.",
}
_verification_lock = asyncio.Lock()


async def run_verification_task():
    async with _verification_lock:
        from apps.execution.database.sealer import validate_ledger_integrity

        try:
            async with db_manager.get_session_maker()() as session:
                is_valid = await validate_ledger_integrity(session)
                _last_verification_status["verified"] = is_valid
                _last_verification_status["message"] = (
                    "GxP clinical execution ledger chain fully verified and structurally intact."
                    if is_valid
                    else "GxP Core Data Integrity Breach Detected"
                )
        except Exception as e:
            _last_verification_status["verified"] = False
            _last_verification_status["message"] = (
                f"GxP Core Data Integrity Breach Detected: {str(e)}"
            )


@app.get("/api/v1/execution/audit/integrity")
async def get_execution_audit_integrity(
    request: Request,
    background_tasks: BackgroundTasks,
    principal: Principal = Depends(get_principal),
) -> dict:
    """Verify the GxP clinical execution ledger integrity via block-sealing validation.

    Ensures that chronological audit logs, block-level seals, and sequential chaining
    remain structurally unbroken.
    """
    is_auditor = "auditor" in principal.roles or any(
        r
        in {
            "auditor",
            "inspector",
            "regulatory_inspector",
            "tmf_auditor",
            "sponsor_admin",
        }
        for r in principal.raw_roles
    )
    if not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    # Queue the verification task in the background (will run outside request lifecycle)
    background_tasks.add_task(run_verification_task)

    return {
        "verified": _last_verification_status["verified"],
        "message": _last_verification_status["message"],
    }


@app.get("/api/v1/admin/outbox")
async def execution_admin_outbox_endpoint(
    status: str | None = None,
    event_type: str | None = None,
) -> list[dict]:
    from sqlalchemy import select

    from apps.execution.database.models import IntegrationOutbox

    async with db_manager.get_session_maker()() as session:
        stmt = select(IntegrationOutbox)
        if status:
            stmt = stmt.where(IntegrationOutbox.status == status)
        if event_type:
            stmt = stmt.where(IntegrationOutbox.event_type == event_type)
        stmt = stmt.order_by(IntegrationOutbox.created_at.desc())
        res = await session.execute(stmt)
        records = res.scalars().all()

        return [
            {
                "id": r.id,
                "event_type": r.event_type,
                "payload": r.payload,
                "status": r.status,
                "attempts": r.attempts,
                "last_error": r.last_error,
                "next_retry_at": r.next_retry_at.isoformat()
                if r.next_retry_at
                else None,
                "completed_at": r.completed_at.isoformat() if r.completed_at else None,
                "retry_eligible": r.retry_eligible,
                "correlation_id": r.correlation_id,
                "created_at": r.created_at.isoformat(),
                "created_by": r.created_by,
                "reason_for_change": r.reason_for_change,
            }
            for r in records
        ]


class ResupplyEventResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    kit_id: str
    requested_qty: int
    status: str
    triggered_at: datetime


class ResupplyEventApprovalRequest(BaseModel):
    change_justification: str


@app.get(
    "/api/v1/execution/rtsm/resupply-events",
    response_model=list[ResupplyEventResponse],
)
async def list_resupply_events_endpoint(
    request: Request,
    study_id: str | None = None,
    site_id: str | None = None,
    status: str | None = None,
    principal: Principal = Depends(get_principal),
) -> list[ResupplyEventResponse]:
    """Lists all active resupply events, filtered by query params and user's site-level/study-level authorization."""
    from apps.execution.database.models import ResupplyEvent
    from packages.security.rbac import can_access_site, can_access_study

    if not principal.roles:
        raise HTTPException(status_code=403, detail="Forbidden: User has no roles.")

    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(ResupplyEvent.is_deleted.is_(False))
        if study_id:
            stmt = stmt.where(ResupplyEvent.study_id == study_id)
        if site_id:
            stmt = stmt.where(ResupplyEvent.site_id == site_id)
        if status:
            stmt = stmt.where(ResupplyEvent.status == status)

        stmt = stmt.order_by(ResupplyEvent.triggered_at.desc())
        result = await session.execute(stmt)
        events = result.scalars().all()

        filtered = []
        for ev in events:
            if can_access_site(principal, ev.site_id) and can_access_study(
                principal, ev.study_id
            ):
                filtered.append(ev)

        return [
            ResupplyEventResponse(
                id=ev.id,
                study_id=ev.study_id,
                site_id=ev.site_id,
                kit_id=ev.kit_id,
                requested_qty=ev.requested_qty,
                status=ev.status,
                triggered_at=ev.triggered_at,
            )
            for ev in filtered
        ]


@app.post(
    "/api/v1/execution/rtsm/resupply-events/{event_id}/confirm",
    response_model=ResupplyEventResponse,
)
async def approve_resupply_event_endpoint(
    event_id: str,
    request: Request,
    payload: ResupplyEventApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> ResupplyEventResponse:
    """Approve a resupply event to trigger shipment generation."""
    from apps.execution.database.context import current_change_reason, current_user_id
    from apps.execution.database.models import ResupplyEvent
    from apps.execution.trial_lock import TrialLockManager
    from packages.security.rbac import can_access_site, can_access_study

    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not payload.change_justification or not payload.change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(
            ResupplyEvent.id == event_id, ResupplyEvent.is_deleted.is_(False)
        )
        result = await session.execute(stmt)
        event = result.scalars().first()
        if not event:
            raise HTTPException(status_code=404, detail="Resupply event not found")

        if not (
            can_access_site(principal, event.site_id)
            and can_access_study(principal, event.study_id)
        ):
            raise HTTPException(
                status_code=403, detail="Forbidden: Access denied to site or study."
            )

        def is_manager(p: Principal) -> bool:
            manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
            if any(r in manager_roles for r in p.roles):
                return True
            raw_manager_roles = {
                "cra",
                "monitor",
                "clinical_research_associate",
                "clinicalresearchassociate",
                "sponsor_admin",
                "sponsoradmin",
                "admin",
                "sysadmin",
                "system_admin",
                "systemadmin",
            }
            for r in p.raw_roles:
                norm_r = r.strip().lower().replace(" ", "_")
                if norm_r in raw_manager_roles:
                    return True
            return False

        if not is_manager(principal):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: User does not have manager-level permissions.",
            )

        if TrialLockManager.is_site_locked(event.site_id):
            raise HTTPException(
                status_code=423,
                detail=f"Site {event.site_id} is currently locked in a read-only state.",
            )

        event.status = "APPROVED"
        event.version += 1
        current_user_id.set(principal.user_id)
        current_change_reason.set(change_reason)

        session.add(event)
        await session.commit()

        stmt = select(ResupplyEvent).where(ResupplyEvent.id == event_id)
        result = await session.execute(stmt)
        event = result.scalars().first()

        return ResupplyEventResponse(
            id=event.id,
            study_id=event.study_id,
            site_id=event.site_id,
            kit_id=event.kit_id,
            requested_qty=event.requested_qty,
            status=event.status,
            triggered_at=event.triggered_at,
        )


@app.post(
    "/api/v1/execution/rtsm/resupply-events/{event_id}/reject",
    response_model=ResupplyEventResponse,
)
async def reject_resupply_event_endpoint(
    event_id: str,
    request: Request,
    payload: ResupplyEventApprovalRequest,
    principal: Principal = Depends(get_principal),
) -> ResupplyEventResponse:
    """Reject a resupply event."""
    from apps.execution.database.context import current_change_reason, current_user_id
    from apps.execution.database.models import ResupplyEvent
    from apps.execution.trial_lock import TrialLockManager
    from packages.security.rbac import can_access_site, can_access_study

    verify_change_justification(request)
    change_reason = request.headers.get("X-Change-Reason")

    if not payload.change_justification or not payload.change_justification.strip():
        raise HTTPException(
            status_code=400, detail="Change justification must not be empty."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ResupplyEvent).where(
            ResupplyEvent.id == event_id, ResupplyEvent.is_deleted.is_(False)
        )
        result = await session.execute(stmt)
        event = result.scalars().first()
        if not event:
            raise HTTPException(status_code=404, detail="Resupply event not found")

        if not (
            can_access_site(principal, event.site_id)
            and can_access_study(principal, event.study_id)
        ):
            raise HTTPException(
                status_code=403, detail="Forbidden: Access denied to site or study."
            )

        def is_manager(p: Principal) -> bool:
            manager_roles = {"sponsor_dm", "admin", "sysadmin", "cra", "monitor"}
            if any(r in manager_roles for r in p.roles):
                return True
            raw_manager_roles = {
                "cra",
                "monitor",
                "clinical_research_associate",
                "clinicalresearchassociate",
                "sponsor_admin",
                "sponsoradmin",
                "admin",
                "sysadmin",
                "system_admin",
                "systemadmin",
            }
            for r in p.raw_roles:
                norm_r = r.strip().lower().replace(" ", "_")
                if norm_r in raw_manager_roles:
                    return True
            return False

        if not is_manager(principal):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: User does not have manager-level permissions.",
            )

        if TrialLockManager.is_site_locked(event.site_id):
            raise HTTPException(
                status_code=423,
                detail=f"Site {event.site_id} is currently locked in a read-only state.",
            )

        event.status = "REJECTED"
        event.version += 1
        current_user_id.set(principal.user_id)
        current_change_reason.set(change_reason)

        session.add(event)
        await session.commit()

        stmt = select(ResupplyEvent).where(ResupplyEvent.id == event_id)
        result = await session.execute(stmt)
        event = result.scalars().first()

        return ResupplyEventResponse(
            id=event.id,
            study_id=event.study_id,
            site_id=event.site_id,
            kit_id=event.kit_id,
            requested_qty=event.requested_qty,
            status=event.status,
            triggered_at=event.triggered_at,
        )
