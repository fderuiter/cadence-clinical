import json
import os
import shutil
import tempfile
import uuid
import zipfile
from contextlib import asynccontextmanager
from datetime import datetime
from enum import Enum
from typing import Any, AsyncGenerator, List, Optional

from fastapi import (
    BackgroundTasks,
    Depends,
    FastAPI,
    File,
    Form,
    HTTPException,
    Query,
    Request,
    Response,
    UploadFile,
)
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select, text

from apps.execution.biostat import (
    DatasetJSONValidationError,
    derive_adae,
    derive_adsl,
    derive_advs,
    extract_ae,
    extract_dm,
    extract_lb,
    extract_mh,
    extract_vs,
    serialize_to_dataset_json,
    validate_dataset_json,
)
from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.coding import match_verbatim_term
from apps.execution.coding.importer import process_dictionary_import
from apps.execution.coding.parsers import MedDRAParser, WHODrugParser
from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import (
    AuditLog,
    BiostatExport,
    ClinicalCodingAssignment,
    ClinicalCodingLedger,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    CodingState,
    DictionaryImportJob,
    FormSubmission,
    ImportState,
    SDVSignOff,
    TranslationJob,
    TSDVConfig,
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
from apps.execution.edit_checks import (
    run_asynchronous_edit_checks,
    run_synchronous_edit_checks,
)
from apps.execution.outliers import recalculate_cohort_outliers
from apps.execution.query_service import QueryService, StateTransitionError
from apps.execution.translator import process_translation
from apps.execution.trial_lock import TrialLockManager
from apps.execution.tsdv import evaluate_tsdv_requirement
from apps.execution.ucum import convert_unit, get_normalized_representation
from packages.security import (
    ROLE_CRA,
    ROLE_DATA_MANAGER,
    ROLE_SITE_INVESTIGATOR,
    get_normalized_roles,
    require_roles,
    verify_not_auditor,
)
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Handle the lifespan events for the FastAPI application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown.

    Args:
        app (FastAPI): The FastAPI application instance.

    Yields:
        None
    """
    # Initialize shared database library
    db_manager.init_db(DATABASE_URL)

    # Start the background ledger sealer
    from apps.execution.database.sealer import (
        start_background_sealer,
        stop_background_sealer,
    )
    from apps.execution.queries_escalation import (
        start_background_query_escalation,
        stop_background_query_escalation,
    )

    await start_background_sealer(db_manager.get_session_maker())
    await start_background_query_escalation(db_manager.get_session_maker())

    yield

    # Stop background ledger sealer
    await stop_background_sealer()
    # Stop background query escalation
    await stop_background_query_escalation()
    # Cleanup database connection
    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - EDC Execution Engine", version="0.1.0", lifespan=lifespan
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
    odm_payload: Optional[str] = None
    openrosa_payload: Optional[str] = None
    error_message: Optional[str] = None


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

    name: Optional[str] = None
    birthdate: Optional[str] = None
    gender: Optional[str] = None
    race: Optional[str] = None


class SubjectCreate(BaseModel):
    """Pydantic schema for creating a clinical subject pseudonymously."""

    subject_id: str
    study_id: str
    demographics: Optional[Demographics] = None


class SubjectResponse(BaseModel):
    """Pydantic schema returning subject details."""

    id: str
    subject_id: str
    study_id: str
    encrypted_demographics: Optional[str] = None


class VisitCreate(BaseModel):
    """Pydantic schema for creating a clinical visit."""

    subject_id: str
    visit_name: str
    study_id: str
    visit_date: Optional[datetime] = None


class VisitResponse(BaseModel):
    """Pydantic schema returning visit details."""

    id: str
    subject_id: str
    visit_name: str
    visit_date: datetime
    study_id: str


class ObservationCreate(BaseModel):
    """Pydantic schema for creating a clinical observation."""

    subject_id: str
    study_id: Optional[str] = None
    visit_id: Optional[str] = None
    domain: str
    test_code: str
    test_name: str
    value: Optional[float] = None
    value_string: Optional[str] = None
    unit: Optional[str] = None
    observation_date: Optional[datetime] = None
    lab_source: Optional[str] = None
    lab_site_id: Optional[str] = None


class ObservationResponse(BaseModel):
    """Pydantic schema returning observation details."""

    id: str
    subject_id: str
    study_id: str
    visit_id: Optional[str] = None
    domain: str
    observation_date: datetime
    test_code: str
    test_name: str
    value: Optional[float] = None
    value_string: Optional[str] = None
    unit: Optional[str] = None
    normalized_value: Optional[float] = None
    normalized_unit: Optional[str] = None
    is_outlier: bool
    lab_source: Optional[str] = None
    lab_site_id: Optional[str] = None
    lab_indicator: Optional[str] = None
    lab_out_of_range: Optional[bool] = None
    matched_normal_bounds: Optional[str] = None


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
        subj = ClinicalSubject(
            subject_id=payload.subject_id,
            study_id=payload.study_id,
            encrypted_demographics=encrypted_demo,
        )
        session.add(subj)
        await session.commit()
        stmt = select(ClinicalSubject).where(ClinicalSubject.id == subj.id)
        res = await session.execute(stmt)
        subj_db = res.scalar_one()
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
        )
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
        )


@app.post("/api/v1/execution/observations", response_model=ObservationResponse)
async def create_observation(
    payload: ObservationCreate,
    background_tasks: BackgroundTasks,
    roles: list[str] = Depends(verify_not_auditor),
) -> ObservationResponse:
    """Create a new clinical observation, performing unit normalization and outlier checks."""
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
            demo = get_safe_demographics(subj_db, obs_date)
            gender = demo.get("gender")
            age = demo.get("age")

        # Fetch active LabReferenceRange definitions for the study and test code
        from apps.execution.database.models import LabReferenceRange

        stmt_ranges = select(LabReferenceRange).where(
            LabReferenceRange.study_id == study_id,
            LabReferenceRange.test_code == payload.test_code,
            LabReferenceRange.is_deleted.is_(False),
        )
        res_ranges = await session.execute(stmt_ranges)
        ranges = res_ranges.scalars().all()

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

        # Invoke synchronous field-level same-record edit checks directly in the active session
        await run_synchronous_edit_checks(session, obs_db)
        await session.commit()

        # Propagate audit and user context to background tasks
        user_id = current_user_id.get()
        change_reason = current_change_reason.get()
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
    site_id: Optional[str] = None
    unit: str
    normalized_unit: str
    sex_applicability: str
    age_low: Optional[float] = None
    age_high: Optional[float] = None
    low_bound: Optional[float] = None
    high_bound: Optional[float] = None
    critical_low: Optional[float] = None
    critical_high: Optional[float] = None
    version: int
    is_deleted: bool


class LabReferenceRangeCreate(BaseModel):
    """Pydantic schema for creating a reference range."""

    study_id: str
    test_code: str
    test_name: str
    source: str
    site_id: Optional[str] = None
    unit: str
    normalized_unit: str
    sex_applicability: str
    age_low: Optional[float] = None
    age_high: Optional[float] = None
    low_bound: Optional[float] = None
    high_bound: Optional[float] = None
    critical_low: Optional[float] = None
    critical_high: Optional[float] = None


class LabReferenceRangeUpdate(BaseModel):
    """Pydantic schema for updating a reference range."""

    study_id: Optional[str] = None
    test_code: Optional[str] = None
    test_name: Optional[str] = None
    source: Optional[str] = None
    site_id: Optional[str] = None
    unit: Optional[str] = None
    normalized_unit: Optional[str] = None
    sex_applicability: Optional[str] = None
    age_low: Optional[float] = None
    age_high: Optional[float] = None
    low_bound: Optional[float] = None
    high_bound: Optional[float] = None
    critical_low: Optional[float] = None
    critical_high: Optional[float] = None


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
        except (ValueError, TypeError):
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
        except (ValueError, TypeError):
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
        except (ValueError, TypeError):
            raise HTTPException(
                status_code=400,
                detail="Field 'low_bound' must be a numeric value.",
            )
    if high_bound is not None:
        try:
            high_bound_val = float(high_bound)
            data["high_bound"] = high_bound_val
        except (ValueError, TypeError):
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
        except (ValueError, TypeError):
            raise HTTPException(
                status_code=400,
                detail="Field 'critical_low' must be a numeric value.",
            )
    if critical_high is not None:
        try:
            critical_high_val = float(critical_high)
            data["critical_high"] = critical_high_val
        except (ValueError, TypeError):
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
) -> LabReferenceRangeResponse:
    """Create a new lab reference range, validating all range invariants."""
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

        # Retrieve the latest committed state
        async with session.begin():
            stmt = select(LabReferenceRange).where(LabReferenceRange.id == lab_range.id)
            res = await session.execute(stmt)
            saved_range = res.scalar_one()

        return LabReferenceRangeResponse(
            id=saved_range.id,
            study_id=saved_range.study_id,
            test_code=saved_range.test_code,
            test_name=saved_range.test_name,
            source=saved_range.source,
            site_id=saved_range.site_id,
            unit=saved_range.unit,
            normalized_unit=saved_range.normalized_unit,
            sex_applicability=saved_range.sex_applicability,
            age_low=saved_range.age_low,
            age_high=saved_range.age_high,
            low_bound=saved_range.low_bound,
            high_bound=saved_range.high_bound,
            critical_low=saved_range.critical_low,
            critical_high=saved_range.critical_high,
            version=saved_range.version,
            is_deleted=saved_range.is_deleted,
        )


@app.get(
    "/api/v1/execution/lab-ranges",
    response_model=List[LabReferenceRangeResponse],
)
async def list_lab_ranges(
    study_id: Optional[str] = None,
    test_code: Optional[str] = None,
    source: Optional[str] = None,
    include_deleted: bool = False,
    roles: list[str] = Depends(get_normalized_roles),
) -> List[LabReferenceRangeResponse]:
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
) -> LabReferenceRangeResponse:
    """Update an existing lab reference range, validating all range invariants on the merged state."""
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

            merged_data = {
                "study_id": payload.study_id
                if payload.study_id is not None
                else r.study_id,
                "test_code": payload.test_code
                if payload.test_code is not None
                else r.test_code,
                "test_name": payload.test_name
                if payload.test_name is not None
                else r.test_name,
                "source": payload.source if payload.source is not None else r.source,
                "site_id": payload.site_id
                if payload.site_id is not None
                else r.site_id,
                "unit": payload.unit if payload.unit is not None else r.unit,
                "normalized_unit": payload.normalized_unit
                if payload.normalized_unit is not None
                else r.normalized_unit,
                "sex_applicability": payload.sex_applicability
                if payload.sex_applicability is not None
                else r.sex_applicability,
                "age_low": payload.age_low
                if payload.age_low is not None
                else r.age_low,
                "age_high": payload.age_high
                if payload.age_high is not None
                else r.age_high,
                "low_bound": payload.low_bound
                if payload.low_bound is not None
                else r.low_bound,
                "high_bound": payload.high_bound
                if payload.high_bound is not None
                else r.high_bound,
                "critical_low": payload.critical_low
                if payload.critical_low is not None
                else r.critical_low,
                "critical_high": payload.critical_high
                if payload.critical_high is not None
                else r.critical_high,
            }

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

        async with session.begin():
            stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            res = await session.execute(stmt)
            updated_range = res.scalar_one()

        return LabReferenceRangeResponse(
            id=updated_range.id,
            study_id=updated_range.study_id,
            test_code=updated_range.test_code,
            test_name=updated_range.test_name,
            source=updated_range.source,
            site_id=updated_range.site_id,
            unit=updated_range.unit,
            normalized_unit=updated_range.normalized_unit,
            sex_applicability=updated_range.sex_applicability,
            age_low=updated_range.age_low,
            age_high=updated_range.age_high,
            low_bound=updated_range.low_bound,
            high_bound=updated_range.high_bound,
            critical_low=updated_range.critical_low,
            critical_high=updated_range.critical_high,
            version=updated_range.version,
            is_deleted=updated_range.is_deleted,
        )


@app.delete(
    "/api/v1/execution/lab-ranges/{range_id}",
    response_model=LabReferenceRangeResponse,
)
async def delete_lab_range(
    range_id: str,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
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

        async with session.begin():
            stmt = select(LabReferenceRange).where(LabReferenceRange.id == range_id)
            res = await session.execute(stmt)
            deleted_range = res.scalar_one()

        return LabReferenceRangeResponse(
            id=deleted_range.id,
            study_id=deleted_range.study_id,
            test_code=deleted_range.test_code,
            test_name=deleted_range.test_name,
            source=deleted_range.source,
            site_id=deleted_range.site_id,
            unit=deleted_range.unit,
            normalized_unit=deleted_range.normalized_unit,
            sex_applicability=deleted_range.sex_applicability,
            age_low=deleted_range.age_low,
            age_high=deleted_range.age_high,
            low_bound=deleted_range.low_bound,
            high_bound=deleted_range.high_bound,
            critical_low=deleted_range.critical_low,
            critical_high=deleted_range.critical_high,
            version=deleted_range.version,
            is_deleted=deleted_range.is_deleted,
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
) -> LabRangeRecalculateResponse:
    """Trigger cohort-wide reference range evaluation and recalculation on-demand."""
    from apps.execution.lab_ranges import recalculate_range_flags

    async with db_manager.get_session_maker()() as session:
        count = await recalculate_range_flags(
            session, payload.study_id, payload.test_code
        )
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

        subjects = {}
        for obs, visit_name in rows:
            subj_key = obs.subject_id
            vname = visit_name or "Baseline"
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
# Medical Dictionary & UCUM Standardization API Contracts
# ==========================================


class DictTypeEnum(str, Enum):
    MEDDRA = "MEDDRA"
    WHODRUG = "WHODRUG"
    LOINC = "LOINC"
    SNOMED = "SNOMED"


class JobStatusEnum(str, Enum):
    PENDING = "PENDING"
    PROCESSING = "PROCESSING"
    COMPLETED = "COMPLETED"
    FAILED = "FAILED"


class JobStatusResponse(BaseModel):
    job_id: str
    dictionary_type: str
    version: str
    status: JobStatusEnum
    started_at: datetime
    completed_at: Optional[datetime] = None
    progress_percentage: Optional[int] = None
    records_imported: Optional[int] = None
    errors_encountered: Optional[int] = None


class PrimarySocFlagEnum(str, Enum):
    Y = "Y"
    N = "N"


class MedDRACodeMatch(BaseModel):
    llt_code: str
    llt_name: str
    pt_code: str
    pt_name: str
    hlt_code: str
    hlt_name: str
    hlgt_code: str
    hlgt_name: str
    soc_code: str
    soc_name: str
    primary_soc_flag: Optional[PrimarySocFlagEnum] = None
    score: float


class MedDRACodingResult(BaseModel):
    status: str  # e.g., "AUTO-CODED", "SUGGESTIONS", "UNCODABLE"
    matches: List[MedDRACodeMatch]


class WHODrugATCContext(BaseModel):
    atc_code: str
    description: str


class WHODrugIngredientItem(BaseModel):
    ingredient_code: str
    ingredient_name: str


class WHODrugCodeMatch(BaseModel):
    drug_code: str
    preferred_name: str
    drug_name: Optional[str] = None
    score: float
    atc_context: List[WHODrugATCContext] = []
    ingredients: List[WHODrugIngredientItem] = []


class WHODrugCodingResult(BaseModel):
    status: str  # e.g., "AUTO-CODED", "SUGGESTIONS", "UNCODABLE"
    matches: List[WHODrugCodeMatch]


class UCUMConvertRequest(BaseModel):
    value: float
    source_unit: str
    target_unit: str


class UCUMUnitValue(BaseModel):
    value: float
    unit: str


class UCUMConvertResponse(BaseModel):
    source: UCUMUnitValue
    target: UCUMUnitValue
    is_compatible: bool
    scale_factor: float
    offset: Optional[float] = None


def validate_archive_layout(temp_zip_path: str, dictionary_type: DictTypeEnum) -> None:
    if not zipfile.is_zipfile(temp_zip_path):
        raise HTTPException(
            status_code=400,
            detail="The uploaded file is not a valid zip archive.",
        )

    with zipfile.ZipFile(temp_zip_path) as z:
        names = z.namelist()
        if dictionary_type == DictTypeEnum.MEDDRA:
            parser = MedDRAParser(dictionary_version="temp")
            valid_found = False
            for name in names:
                if name.lower().endswith(".asc"):
                    try:
                        parser.detect_file_type(name)
                        valid_found = True
                        break
                    except ValueError:
                        continue
            if not valid_found:
                raise HTTPException(
                    status_code=400,
                    detail="Invalid MedDRA archive layout. Expected at least one of llt.asc, pt.asc, hlt.asc, hlgt.asc, soc.asc, or mdhier.asc.",
                )
        elif dictionary_type == DictTypeEnum.WHODRUG:
            parser = WHODrugParser(dictionary_version="temp")
            valid_found = False
            for name in names:
                if name.lower().endswith((".txt", ".asc", ".csv")):
                    try:
                        parser.detect_file_type(name)
                        valid_found = True
                        break
                    except ValueError:
                        continue
            if not valid_found:
                raise HTTPException(
                    status_code=400,
                    detail="Invalid WHODrug archive layout. Expected at least one of drugs, ingredients, atc, drug_atc, or drug_ingredients files.",
                )


@app.post(
    "/api/v1/dictionaries/import", response_model=JobStatusResponse, status_code=202
)
async def import_dictionary(
    background_tasks: BackgroundTasks,
    dictionary_type: DictTypeEnum = Form(...),
    version: str = Form(...),
    files: UploadFile = File(...),
    parse_multilingual: bool = Form(True),
    roles: list[str] = Depends(require_roles("TERMINOLOGY_MANAGER", "SYSTEM_ADMIN")),
) -> JobStatusResponse:
    """Imports raw dictionary files and schedules a background parsing task."""
    if dictionary_type not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
        raise HTTPException(
            status_code=400,
            detail=f"Import not supported for dictionary type: {dictionary_type.value}",
        )

    if not version or not version.strip():
        raise HTTPException(
            status_code=400,
            detail="Version must be a non-empty string.",
        )

    # 1. Persist the uploaded file to secure temporary storage
    # Create temporary file
    temp_dir = tempfile.gettempdir()
    temp_file_path = os.path.join(temp_dir, f"dict_import_{uuid.uuid4().hex}.zip")

    try:
        with open(temp_file_path, "wb") as buffer:
            shutil.copyfileobj(files.file, buffer)

        # 2. Perform layout validation synchronously
        validate_archive_layout(temp_file_path, dictionary_type)
    except HTTPException:
        # Clean up temporary file on layout validation failure
        if os.path.exists(temp_file_path):
            os.remove(temp_file_path)
        raise
    except Exception as e:
        if os.path.exists(temp_file_path):
            os.remove(temp_file_path)
        raise HTTPException(
            status_code=400,
            detail=f"Failed to process file upload: {str(e)}",
        )

    # Convert parameter enum to database model enum
    db_type = DBDictionaryType[dictionary_type.value]

    # 3. Create the initial DictionaryImportJob record in PENDING status
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            job = DictionaryImportJob(
                dictionary_type=db_type,
                dictionary_version=version,
                status=ImportState.PENDING,
                started_at=datetime.utcnow(),
                progress_percentage=0,
                records_imported=0,
                errors_encountered=0,
            )
            session.add(job)
            await session.flush()
            job_id = job.id
            started_at = job.started_at

    # 4. Schedule the background parsing task
    user_id = current_user_id.get()
    change_reason = current_change_reason.get()

    background_tasks.add_task(
        process_dictionary_import,
        job_id=job_id,
        dictionary_type=dictionary_type.value,
        version=version,
        temp_zip_path=temp_file_path,
        session_maker=db_manager.get_session_maker(),
        user_id=user_id,
        change_reason=change_reason,
    )

    return JobStatusResponse(
        job_id=job_id,
        dictionary_type=dictionary_type.value,
        version=version,
        status=JobStatusEnum.PENDING,
        started_at=started_at,
        progress_percentage=0,
        records_imported=0,
        errors_encountered=0,
    )


@app.get("/api/v1/dictionaries/jobs/{job_id}", response_model=JobStatusResponse)
async def get_dictionary_import_job(
    job_id: str,
) -> JobStatusResponse:
    """Query the execution status, progress, and import counts of a dictionary import job by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(DictionaryImportJob).where(DictionaryImportJob.id == job_id)
        res = await session.execute(stmt)
        job = res.scalars().first()
        if not job:
            raise HTTPException(
                status_code=404, detail="Dictionary import job not found"
            )

        return JobStatusResponse(
            job_id=job.id,
            dictionary_type=job.dictionary_type.value,
            version=job.dictionary_version,
            status=JobStatusEnum(job.status.value),
            started_at=job.started_at,
            completed_at=job.completed_at,
            progress_percentage=job.progress_percentage,
            records_imported=job.records_imported,
            errors_encountered=job.errors_encountered,
        )


class MedDRATargetLevelEnum(str, Enum):
    LLT = "LLT"
    PT = "PT"


@app.get("/api/v1/dictionaries/meddra/code", response_model=MedDRACodingResult)
async def get_meddra_code(
    term: str,
    version: Optional[str] = Query("26.0"),
    target_level: Optional[MedDRATargetLevelEnum] = Query(MedDRATargetLevelEnum.LLT),
    roles: list[str] = Depends(get_normalized_roles),
) -> MedDRACodingResult:
    """Performs coding or interactive auto-complete lookup on adverse events using version-aware matcher."""
    if not term or not term.strip():
        raise HTTPException(
            status_code=400,
            detail="Term must be a non-empty string.",
        )
    if not version or not version.strip():
        raise HTTPException(
            status_code=400,
            detail="Version must be a non-empty string.",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            res = await match_verbatim_term(
                session=session,
                verbatim=term.strip(),
                dictionary_type="MEDDRA",
                version=version.strip(),
                target_level=target_level.value if target_level else None,
            )
        except Exception as e:
            raise HTTPException(
                status_code=500, detail=f"Database or matcher error: {str(e)}"
            )

        matches = []
        if res.get("match"):
            parent_match = res["match"]
            score = parent_match.get("score", 0.0)
            if parent_match.get("hierarchies"):
                for h in parent_match.get("hierarchies", []):
                    matches.append(
                        MedDRACodeMatch(
                            llt_code=h.get("llt_code") or "",
                            llt_name=h.get("llt_name") or "",
                            pt_code=h.get("pt_code") or "",
                            pt_name=h.get("pt_name") or "",
                            hlt_code=h.get("hlt_code") or "",
                            hlt_name=h.get("hlt_name") or "",
                            hlgt_code=h.get("hlgt_code") or "",
                            hlgt_name=h.get("hlgt_name") or "",
                            soc_code=h.get("soc_code") or "",
                            soc_name=h.get("soc_name") or "",
                            primary_soc_flag=h.get("primary_soc_flag"),
                            score=score,
                        )
                    )
            else:
                is_llt = parent_match.get("level") == "LLT"
                matches.append(
                    MedDRACodeMatch(
                        llt_code=parent_match.get("code") if is_llt else "",
                        llt_name=parent_match.get("term_name") if is_llt else "",
                        pt_code=parent_match.get("code") if not is_llt else "",
                        pt_name=parent_match.get("term_name") if not is_llt else "",
                        hlt_code="",
                        hlt_name="",
                        hlgt_code="",
                        hlgt_name="",
                        soc_code="",
                        soc_name="",
                        primary_soc_flag=None,
                        score=score,
                    )
                )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                score = sug.get("score", 0.0)
                if sug.get("hierarchies"):
                    for h in sug.get("hierarchies", []):
                        matches.append(
                            MedDRACodeMatch(
                                llt_code=h.get("llt_code") or "",
                                llt_name=h.get("llt_name") or "",
                                pt_code=h.get("pt_code") or "",
                                pt_name=h.get("pt_name") or "",
                                hlt_code=h.get("hlt_code") or "",
                                hlt_name=h.get("hlt_name") or "",
                                hlgt_code=h.get("hlgt_code") or "",
                                hlgt_name=h.get("hlgt_name") or "",
                                soc_code=h.get("soc_code") or "",
                                soc_name=h.get("soc_name") or "",
                                primary_soc_flag=h.get("primary_soc_flag"),
                                score=score,
                            )
                        )
                else:
                    is_llt = sug.get("level") == "LLT"
                    matches.append(
                        MedDRACodeMatch(
                            llt_code=sug.get("code") if is_llt else "",
                            llt_name=sug.get("term_name") if is_llt else "",
                            pt_code=sug.get("code") if not is_llt else "",
                            pt_name=sug.get("term_name") if not is_llt else "",
                            hlt_code="",
                            hlt_name="",
                            hlgt_code="",
                            hlgt_name="",
                            soc_code="",
                            soc_name="",
                            primary_soc_flag=None,
                            score=score,
                        )
                    )

        return MedDRACodingResult(
            status=res.get("status", "UNCODABLE"),
            matches=matches,
        )


@app.get("/api/v1/dictionaries/whodrug/code", response_model=WHODrugCodingResult)
async def get_whodrug_code(
    term: str,
    version: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> WHODrugCodingResult:
    """Performs coding or interactive lookup on WHODrug database using version-aware matcher."""
    if not term or not term.strip():
        raise HTTPException(
            status_code=400,
            detail="Term must be a non-empty string.",
        )
    if not version or not version.strip():
        raise HTTPException(
            status_code=400,
            detail="Version must be a non-empty string.",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            res = await match_verbatim_term(
                session=session,
                verbatim=term.strip(),
                dictionary_type="WHODRUG",
                version=version.strip(),
            )
        except Exception as e:
            raise HTTPException(
                status_code=500, detail=f"Database or matcher error: {str(e)}"
            )

        matches = []
        if res.get("match"):
            m = res["match"]
            matches.append(
                WHODrugCodeMatch(
                    drug_code=m.get("drug_code") or "",
                    preferred_name=m.get("preferred_name") or "",
                    drug_name=m.get("drug_name"),
                    score=m.get("score", 0.0),
                    atc_context=[
                        WHODrugATCContext(
                            atc_code=a.get("atc_code") or "",
                            description=a.get("description") or "",
                        )
                        for a in m.get("atc_context", [])
                    ],
                    ingredients=[
                        WHODrugIngredientItem(
                            ingredient_code=i.get("ingredient_code") or "",
                            ingredient_name=i.get("ingredient_name") or "",
                        )
                        for i in m.get("ingredients", [])
                    ],
                )
            )
        elif res.get("suggestions"):
            for sug in res["suggestions"]:
                matches.append(
                    WHODrugCodeMatch(
                        drug_code=sug.get("drug_code") or "",
                        preferred_name=sug.get("preferred_name") or "",
                        drug_name=sug.get("drug_name"),
                        score=sug.get("score", 0.0),
                        atc_context=[
                            WHODrugATCContext(
                                atc_code=a.get("atc_code") or "",
                                description=a.get("description") or "",
                            )
                            for a in sug.get("atc_context", [])
                        ],
                        ingredients=[
                            WHODrugIngredientItem(
                                ingredient_code=i.get("ingredient_code") or "",
                                ingredient_name=i.get("ingredient_name") or "",
                            )
                            for i in sug.get("ingredients", [])
                        ],
                    )
                )

        return WHODrugCodingResult(
            status=res.get("status", "UNCODABLE"),
            matches=matches,
        )


@app.post("/api/v1/dictionaries/ucum/convert", response_model=UCUMConvertResponse)
async def post_ucum_convert(payload: UCUMConvertRequest) -> UCUMConvertResponse:
    """Standardizes numeric values and verifies scale compatibility between source and target codes."""
    return UCUMConvertResponse(
        source=UCUMUnitValue(value=payload.value, unit=payload.source_unit),
        target=UCUMUnitValue(
            value=payload.value * 0.5555555555555556, unit=payload.target_unit
        ),
        is_compatible=True,
        scale_factor=0.5555555555555556,
        offset=-17.77777777777778,
    )


# ==========================================
# Clinical Query Management API
# ==========================================


class QueryHistoryItem(BaseModel):
    """Pydantic schema representing a single audited event in query history."""

    action: str
    user_id: Optional[str] = None
    timestamp: datetime
    old_values: Optional[dict[str, Any]] = None
    new_values: Optional[dict[str, Any]] = None
    change_reason: Optional[str] = None
    version_index: int


class ClinicalQueryResponse(BaseModel):
    """Pydantic schema returning query details and full audit history."""

    id: str
    study_id: str
    subject_id: str
    visit_id: Optional[str] = None
    domain: Optional[str] = None
    test_code: str
    status: str
    explanation: Optional[str] = None
    response: Optional[str] = None
    created_at: datetime
    updated_at: datetime
    history: List[QueryHistoryItem] = []

    observation_id: Optional[str] = None
    field_link: Optional[str] = None
    message: Optional[str] = None
    origin: Optional[str] = None
    priority: Optional[str] = None
    rule_id: Optional[str] = None
    created_by: Optional[str] = None
    responder: Optional[str] = None
    resolver: Optional[str] = None
    resolved_at: Optional[datetime] = None
    cancellation_reason: Optional[str] = None
    escalated_at: Optional[datetime] = None

    form_id: Optional[str] = None
    field_id: Optional[str] = None
    query_type: Optional[str] = None
    action_required: Optional[str] = None


class QueryCreate(BaseModel):
    """Pydantic schema for raising a new query."""

    study_id: str
    subject_id: str
    visit_id: Optional[str] = None
    domain: Optional[str] = None
    test_code: str
    explanation: str
    status: Optional[str] = "OPEN"

    observation_id: Optional[str] = None
    field_link: Optional[str] = None
    message: Optional[str] = None
    origin: Optional[str] = None
    priority: Optional[str] = None
    rule_id: Optional[str] = None
    created_by: Optional[str] = None

    form_id: Optional[str] = None
    field_id: Optional[str] = None
    query_type: Optional[str] = None
    action_required: Optional[str] = None


class QueryReopen(BaseModel):
    """Pydantic schema for reopening a query with a reason."""

    reason: Optional[str] = None


class QueryCancel(BaseModel):
    """Pydantic schema for cancelling a query with a reason."""

    reason: str


class QueryRespond(BaseModel):
    """Pydantic schema for responding to an open query."""

    response: str
    responder: Optional[str] = None


class QueryUpdate(BaseModel):
    """Pydantic schema for general state transitions."""

    status: str
    explanation: Optional[str] = None
    response: Optional[str] = None

    observation_id: Optional[str] = None
    field_link: Optional[str] = None
    message: Optional[str] = None
    origin: Optional[str] = None
    priority: Optional[str] = None
    rule_id: Optional[str] = None
    created_by: Optional[str] = None
    responder: Optional[str] = None
    resolver: Optional[str] = None
    resolved_at: Optional[datetime] = None
    cancellation_reason: Optional[str] = None
    escalated_at: Optional[datetime] = None

    form_id: Optional[str] = None
    field_id: Optional[str] = None
    query_type: Optional[str] = None
    action_required: Optional[str] = None


class SyncBlockQuery(BaseModel):
    """Pydantic schema representing the query details in a local ledger block."""

    status: str
    message: Optional[str] = None
    createdBy: Optional[str] = None
    createdAt: Optional[str] = None
    response: Optional[str] = None
    respondedBy: Optional[str] = None
    respondedAt: Optional[str] = None
    closedBy: Optional[str] = None
    closedAt: Optional[str] = None


class SyncBlockDetails(BaseModel):
    """Pydantic schema representing block-specific metadata and clinical coordinates."""

    fieldId: str
    studyId: Optional[str] = None
    subjectId: Optional[str] = None
    visitId: Optional[str] = None
    domain: Optional[str] = None
    testCode: Optional[str] = None
    query: Optional[SyncBlockQuery] = None
    label: Optional[str] = None
    cdash: Optional[str] = None
    oldValue: Optional[str] = None
    newValue: Optional[str] = None


class LocalLedgerBlock(BaseModel):
    """Pydantic schema representing a cryptographically chained offline ledger block."""

    index: int
    timestamp: datetime
    action: str
    details: SyncBlockDetails
    reason: str
    prevHash: str
    hash: str


class SyncRequest(BaseModel):
    """Pydantic schema for bulk-synchronizing local client-side ledger updates."""

    blocks: list[LocalLedgerBlock]


def _is_data_manager(roles_str: Any) -> bool:
    """Check if the roles include Data Manager role variations."""
    if isinstance(roles_str, str):
        roles = [r.strip().lower() for r in roles_str.split(",")]
    else:
        roles = [str(r).strip().lower() for r in roles_str]
    dm_roles = {
        "data manager",
        "data_manager",
        "data-manager",
        "sponsor_dm",
        "dm",
        "admin",
    }
    return any(r in dm_roles for r in roles)


def _is_investigator(roles_str: Any) -> bool:
    """Check if the roles include Investigator role variations."""
    if isinstance(roles_str, str):
        roles = [r.strip().lower() for r in roles_str.split(",")]
    else:
        roles = [str(r).strip().lower() for r in roles_str]
    inv_roles = {
        "investigator",
        "site_investigator",
        "site-investigator",
        "investigator_user",
    }
    return any(r in inv_roles for r in roles)


ALLOWED_TRANSITIONS = {
    "NONE": ["OPEN"],
    "OPEN": ["ANSWERED"],
    "ANSWERED": ["CLOSED", "REOPENED"],
    "REOPENED": ["ANSWERED"],
    "CLOSED": ["REOPENED"],
}


def validate_transition(current_status: str, new_status: str) -> None:
    """Validate transition according to strict sequence rules.

    Args:
        current_status (str): The current query status.
        new_status (str): The requested target status.

    Raises:
        StateTransitionError: If the transition is not allowed.
    """
    if current_status == new_status:
        return
    allowed = ALLOWED_TRANSITIONS.get(current_status, [])
    if new_status not in allowed:
        raise StateTransitionError(
            f"Invalid transition from {current_status} to {new_status}. Allowed transitions are: {allowed}"
        )


def verify_change_justification(request: Request) -> None:
    """Enforce presence of change justification header (version 1 or 2)."""
    version = request.headers.get("X-Signature-Version")
    change_reason = request.headers.get("X-Change-Reason")
    if version not in ("1", "v1", "2", "v2") or not change_reason:
        raise HTTPException(
            status_code=403,
            detail="API rejects any state modifications that do not contain a verified, gateway-signed change justification header.",
        )


def verify_roles(request: Request, allowed_roles: List[str]) -> None:
    """Verify that the user possesses at least one of the allowed roles."""
    roles_str = getattr(request.state, "roles", None) or request.headers.get(
        "X-User-Roles", ""
    )
    if not roles_str:
        raise HTTPException(status_code=403, detail="Missing role credentials.")

    if "data_manager" in allowed_roles:
        if _is_data_manager(roles_str):
            return
    if "investigator" in allowed_roles:
        if _is_investigator(roles_str):
            return

    raise HTTPException(
        status_code=403, detail="User role is not authorized for this action."
    )


async def fetch_history(session: Any, query_id: str) -> List[QueryHistoryItem]:
    """Fetch and parse audit logs for a specific query."""
    stmt_history = (
        select(AuditLog)
        .where(
            AuditLog.table_name == "clinical_queries",
            AuditLog.record_id == query_id,
        )
        .order_by(AuditLog.timestamp.asc())
    )
    res_history = await session.execute(stmt_history)
    logs = res_history.scalars().all()
    history = []
    for log in logs:
        old_val = log.old_values
        new_val = log.new_values
        if isinstance(old_val, str):
            try:
                old_val = json.loads(old_val)
            except Exception:
                pass
        if isinstance(new_val, str):
            try:
                new_val = json.loads(new_val)
            except Exception:
                pass
        history.append(
            QueryHistoryItem(
                action=log.action,
                user_id=log.user_id,
                timestamp=log.timestamp,
                old_values=old_val,
                new_values=new_val,
                change_reason=log.change_reason,
                version_index=log.version_index,
            )
        )
    return history


@app.get("/api/v1/execution/queries", response_model=List[ClinicalQueryResponse])
async def list_queries(
    study_id: Optional[str] = None,
    subject_id: Optional[str] = None,
    visit_id: Optional[str] = None,
    status: Optional[str] = None,
) -> List[ClinicalQueryResponse]:
    """Retrieve a list of clinical queries with optional filtering.

    Args:
        study_id (Optional[str]): Filter by study identifier.
        subject_id (Optional[str]): Filter by subject identifier.
        visit_id (Optional[str]): Filter by visit identifier.
        status (Optional[str]): Filter by query status.

    Returns:
        List[ClinicalQueryResponse]: List of matching queries including audit history.
    """
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(ClinicalQuery.is_deleted.is_(False))
        if study_id:
            stmt = stmt.where(ClinicalQuery.study_id == study_id)
        if subject_id:
            stmt = stmt.where(ClinicalQuery.subject_id == subject_id)
        if visit_id:
            stmt = stmt.where(ClinicalQuery.visit_id == visit_id)
        if status:
            stmt = stmt.where(ClinicalQuery.status == status)

        res = await session.execute(stmt)
        queries = res.scalars().all()

        responses = []
        for q in queries:
            history = await fetch_history(session, q.id)
            responses.append(
                ClinicalQueryResponse(
                    id=q.id,
                    study_id=q.study_id,
                    subject_id=q.subject_id,
                    visit_id=q.visit_id,
                    domain=q.domain,
                    test_code=q.test_code,
                    status=q.status,
                    explanation=q.explanation,
                    response=q.response,
                    created_at=q.created_at,
                    updated_at=q.updated_at,
                    history=history,
                    observation_id=q.observation_id,
                    field_link=q.field_link,
                    message=q.message,
                    origin=q.origin,
                    priority=q.priority,
                    rule_id=q.rule_id,
                    created_by=q.created_by,
                    responder=q.responder,
                    resolver=q.resolver,
                    resolved_at=q.resolved_at,
                    cancellation_reason=q.cancellation_reason,
                    escalated_at=q.escalated_at,
                    form_id=q.form_id,
                    field_id=q.field_id,
                    query_type=q.query_type,
                    action_required=q.action_required,
                )
            )
        return responses


@app.get("/api/v1/execution/queries/{query_id}", response_model=ClinicalQueryResponse)
async def get_query(query_id: str) -> ClinicalQueryResponse:
    """Query a single clinical query by ID, returning its full audit history.

    Args:
        query_id (str): The unique database identifier of the query.

    Returns:
        ClinicalQueryResponse: The query record including detailed history.
    """
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        history = await fetch_history(session, q.id)
        return ClinicalQueryResponse(
            id=q.id,
            study_id=q.study_id,
            subject_id=q.subject_id,
            visit_id=q.visit_id,
            domain=q.domain,
            test_code=q.test_code,
            status=q.status,
            explanation=q.explanation,
            response=q.response,
            created_at=q.created_at,
            updated_at=q.updated_at,
            history=history,
            observation_id=q.observation_id,
            field_link=q.field_link,
            message=q.message,
            origin=q.origin,
            priority=q.priority,
            rule_id=q.rule_id,
            created_by=q.created_by,
            responder=q.responder,
            resolver=q.resolver,
            resolved_at=q.resolved_at,
            cancellation_reason=q.cancellation_reason,
            escalated_at=q.escalated_at,
            form_id=q.form_id,
            field_id=q.field_id,
            query_type=q.query_type,
            action_required=q.action_required,
        )


@app.post(
    "/api/v1/execution/queries",
    response_model=ClinicalQueryResponse,
    status_code=201,
)
async def open_query(
    request: Request,
    payload: QueryCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Raise a new clinical query on a specific field coordinate.

    Args:
        request (Request): The incoming FastAPI request.
        payload (QueryCreate): The coordinate details and query explanation.

    Returns:
        ClinicalQueryResponse: The newly opened clinical query.
    """
    verify_change_justification(request)

    target_status = (payload.status or "OPEN").upper()
    if target_status not in ("CANDIDATE", "OPEN"):
        raise HTTPException(
            status_code=400,
            detail=f"Initial status must be CANDIDATE or OPEN. Received: {target_status}",
        )

    async with db_manager.get_session_maker()() as session:
        # Check if active query already exists on this coordinate
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.study_id == payload.study_id,
            ClinicalQuery.subject_id == payload.subject_id,
            ClinicalQuery.visit_id == payload.visit_id,
            ClinicalQuery.domain == payload.domain,
            ClinicalQuery.test_code == payload.test_code,
            ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
            ClinicalQuery.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        if res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail="An active query already exists on this target field coordinates.",
            )

        q = ClinicalQuery(
            study_id=payload.study_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            domain=payload.domain,
            test_code=payload.test_code,
            status=target_status,
            explanation=payload.explanation,
            observation_id=payload.observation_id,
            field_link=payload.field_link,
            message=payload.message or payload.explanation,
            origin=payload.origin or "manual",
            priority=payload.priority,
            rule_id=payload.rule_id,
            created_by=payload.created_by or current_user_id.get(),
            form_id=payload.form_id,
            field_id=payload.field_id,
            query_type=payload.query_type,
            action_required=payload.action_required,
        )
        session.add(q)
        await session.commit()

        # Refresh to get timestamps and trigger-generated IDs
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


# ==========================================
# SDV Sign-off API
# ==========================================


class SamplingModelEnum(str, Enum):
    SUBJECT_BASED = "SUBJECT_BASED"
    FIELD_BASED = "FIELD_BASED"
    COMBINED = "COMBINED"


class TSDVConfigCreate(BaseModel):
    study_id: str
    sampling_model: SamplingModelEnum
    initial_full_sdv_subject_count: int = Field(default=0, ge=0)
    random_sample_percentage: float = Field(default=0.0, ge=0.0, le=100.0)
    full_sdv_domains: Optional[list[str]] = None
    safety_endpoints: Optional[list[str]] = None
    zero_sdv_domains: Optional[list[str]] = None
    trial_random_seed: Optional[int] = Field(default=None, ge=0)

    @model_validator(mode="after")
    def validate_seed(self) -> "TSDVConfigCreate":
        if self.random_sample_percentage > 0.0 and self.trial_random_seed is None:
            raise ValueError(
                "trial_random_seed is required when random_sample_percentage is greater than 0"
            )
        return self


class TSDVConfigResponse(BaseModel):
    id: str
    study_id: str
    sampling_model: str
    initial_full_sdv_subject_count: int
    random_sample_percentage: float
    full_sdv_domains: Optional[list[str]] = None
    safety_endpoints: Optional[list[str]] = None
    zero_sdv_domains: Optional[list[str]] = None
    trial_random_seed: Optional[int] = None
    version: int

    class Config:
        from_attributes = True


@app.post(
    "/api/v1/execution/tsdv/config",
    response_model=TSDVConfigResponse,
    status_code=201,
)
async def create_or_update_tsdv_config(
    request: Request,
    payload: TSDVConfigCreate,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> TSDVConfig:
    """Create or update Targeted SDV (TSDV) configuration for a study.

    Restricts config writes to CRA/Data Manager roles with GxP change justifications.
    """
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            await session.execute(
                text("SELECT set_config('cadence.app_writing', 'true', true);")
            )
            stmt = select(TSDVConfig).where(TSDVConfig.study_id == payload.study_id)
            res = await session.execute(stmt)
            config = res.scalars().first()

            if config:
                config.sampling_model = payload.sampling_model.value
                config.initial_full_sdv_subject_count = (
                    payload.initial_full_sdv_subject_count
                )
                config.random_sample_percentage = payload.random_sample_percentage
                config.full_sdv_domains = payload.full_sdv_domains
                config.safety_endpoints = payload.safety_endpoints
                config.zero_sdv_domains = payload.zero_sdv_domains
                config.trial_random_seed = payload.trial_random_seed
            else:
                config = TSDVConfig(
                    study_id=payload.study_id,
                    sampling_model=payload.sampling_model.value,
                    initial_full_sdv_subject_count=payload.initial_full_sdv_subject_count,
                    random_sample_percentage=payload.random_sample_percentage,
                    full_sdv_domains=payload.full_sdv_domains,
                    safety_endpoints=payload.safety_endpoints,
                    zero_sdv_domains=payload.zero_sdv_domains,
                    trial_random_seed=payload.trial_random_seed,
                )
                session.add(config)

    async with db_manager.get_session_maker()() as session:
        stmt = select(TSDVConfig).where(TSDVConfig.study_id == payload.study_id)
        res = await session.execute(stmt)
        config = res.scalars().one()
        return config


@app.get(
    "/api/v1/execution/tsdv/config/{study_id}",
    response_model=TSDVConfigResponse,
)
async def get_tsdv_config(
    study_id: str,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> TSDVConfig:
    """Retrieve Targeted SDV (TSDV) configuration for a study."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(TSDVConfig).where(TSDVConfig.study_id == study_id)
        res = await session.execute(stmt)
        config = res.scalars().first()
        if not config:
            raise HTTPException(
                status_code=404,
                detail=f"TSDV configuration not found for study {study_id}",
            )
        return config


class TSDVEvaluationResponse(BaseModel):
    required: bool
    subject_selected: bool
    field_decision: Optional[bool] = None
    sampling_model: str
    config_id: str
    enrollment_index: int
    explanation: str


@app.get(
    "/api/v1/execution/tsdv/required",
    response_model=TSDVEvaluationResponse,
)
async def evaluate_tsdv_rule(
    study_id: str,
    subject_id: str,
    domain: Optional[str] = None,
    enrollment_index: Optional[int] = None,
    roles: list[str] = Depends(get_normalized_roles),
) -> TSDVEvaluationResponse:
    """Evaluate Targeted SDV (TSDV) requirement for a given context.

    Calculates deterministic sampling decisions and returns component results with an audit explanation.
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Resolve Study TSDV Configuration
        stmt_cfg = select(TSDVConfig).where(TSDVConfig.study_id == study_id)
        res_cfg = await session.execute(stmt_cfg)
        config = res_cfg.scalars().first()
        if not config:
            raise HTTPException(
                status_code=404,
                detail=f"TSDV configuration not found for study {study_id}",
            )

        # 2. Resolve Subject and Enrollment Index
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.study_id == study_id,
            ClinicalSubject.is_deleted.is_(False),
        )
        res_subj = await session.execute(stmt_subj)
        subjects = list(res_subj.scalars().all())

        # Sort alphabetically by subject_id
        subjects_sorted = sorted(subjects, key=lambda s: s.subject_id)

        target_sub = None
        resolved_index = None
        for idx, sub in enumerate(subjects_sorted):
            if sub.subject_id == subject_id or sub.id == subject_id:
                target_sub = sub
                resolved_index = idx
                break

        if target_sub is None:
            raise HTTPException(
                status_code=404,
                detail=f"Subject {subject_id} not found in study {study_id}",
            )

        if enrollment_index is None:
            enrollment_index = resolved_index

        subject_uuid = target_sub.id

        # 3. Perform Deterministic Evaluation
        required, subject_selected, field_decision, explanation = (
            evaluate_tsdv_requirement(
                config=config,
                subject_uuid=subject_uuid,
                enrollment_index=enrollment_index,
                domain=domain,
            )
        )

        return TSDVEvaluationResponse(
            required=required,
            subject_selected=subject_selected,
            field_decision=field_decision,
            sampling_model=config.sampling_model,
            config_id=config.id,
            enrollment_index=enrollment_index,
            explanation=explanation,
        )


class SDVScopeEnum(str, Enum):
    FIELD = "FIELD"
    PAGE = "PAGE"
    VISIT = "VISIT"


class SDVSignOffRequest(BaseModel):
    """Pydantic request schema for SDV sign-off."""

    scope: SDVScopeEnum
    target_id: str
    subject_id: str
    study_id: str
    site_id: Optional[str] = None


class SDVSignOffResponse(BaseModel):
    """Pydantic response schema for SDV sign-off."""

    id: str
    scope: str
    target_id: str
    subject_id: str
    study_id: str
    site_id: Optional[str] = None
    is_verified: bool
    verified_by: Optional[str] = None
    verified_at: Optional[datetime] = None
    dropped_reason: Optional[str] = None
    dropped_at: Optional[datetime] = None


@app.post("/api/v1/execution/sdv/signoff", response_model=SDVSignOffResponse)
async def sdv_signoff(
    payload: SDVSignOffRequest,
    roles: list[str] = Depends(require_roles(ROLE_CRA, "monitor")),
) -> SDVSignOffResponse:
    """CRA/monitor-gated SDV sign-off endpoint for Field, Page, or Visit scopes."""
    async with db_manager.get_session_maker()() as session:
        # 1. Validate Subject exists and is consistent with Study
        stmt_subj = select(ClinicalSubject).where(
            ClinicalSubject.subject_id == payload.subject_id,
            ClinicalSubject.study_id == payload.study_id,
        )
        res_subj = await session.execute(stmt_subj)
        subj_db = res_subj.scalars().first()
        if not subj_db:
            raise HTTPException(
                status_code=404,
                detail="Subject not found or inconsistent study reference.",
            )

        # 2. Scope-specific validation
        obs_db = None
        if payload.scope == SDVScopeEnum.FIELD:
            stmt_obs = select(ClinicalObservation).where(
                ClinicalObservation.id == payload.target_id,
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_obs = await session.execute(stmt_obs)
            obs_db = res_obs.scalars().first()
            if not obs_db:
                raise HTTPException(
                    status_code=404,
                    detail="Clinical observation not found or inconsistent target/subject/study reference.",
                )
        elif payload.scope == SDVScopeEnum.VISIT:
            stmt_visit = select(ClinicalVisit).where(
                ClinicalVisit.id == payload.target_id,
                ClinicalVisit.subject_id == payload.subject_id,
                ClinicalVisit.study_id == payload.study_id,
            )
            res_visit = await session.execute(stmt_visit)
            visit_db = res_visit.scalars().first()
            if not visit_db:
                raise HTTPException(
                    status_code=404,
                    detail="Clinical visit not found or inconsistent target/subject/study reference.",
                )
        elif payload.scope == SDVScopeEnum.PAGE:
            stmt_page_obs = select(ClinicalObservation).where(
                ClinicalObservation.page_id == payload.target_id,
                ClinicalObservation.subject_id == payload.subject_id,
                ClinicalObservation.study_id == payload.study_id,
            )
            res_page_obs = await session.execute(stmt_page_obs)
            if not res_page_obs.scalars().first():
                raise HTTPException(
                    status_code=404,
                    detail="Page ID not found or inconsistent target/subject/study reference.",
                )

        # 3. Apply sign-off behavior
        verifier_id = current_user_id.get() or "system"
        verified_at = datetime.utcnow()

        # Update or create the matching SDVSignOff record
        stmt_signoff = select(SDVSignOff).where(
            SDVSignOff.scope == payload.scope.value,
            SDVSignOff.target_id == payload.target_id,
            SDVSignOff.subject_id == payload.subject_id,
            SDVSignOff.study_id == payload.study_id,
        )
        res_signoff = await session.execute(stmt_signoff)
        signoff_db = res_signoff.scalars().first()

        site_id = payload.site_id or (
            subj_db.site_id if hasattr(subj_db, "site_id") else None
        )

        if signoff_db:
            signoff_db.is_verified = True
            signoff_db.verified_by = verifier_id
            signoff_db.verified_at = verified_at
            signoff_db.dropped_reason = None
            signoff_db.dropped_at = None
        else:
            signoff_db = SDVSignOff(
                scope=payload.scope.value,
                target_id=payload.target_id,
                subject_id=payload.subject_id,
                study_id=payload.study_id,
                site_id=site_id,
                is_verified=True,
                verified_by=verifier_id,
                verified_at=verified_at,
            )
            session.add(signoff_db)

        # For FIELD scope, update the ClinicalObservation too
        if payload.scope == SDVScopeEnum.FIELD and obs_db:
            obs_db.is_sdv_verified = True
            obs_db.sdv_verified_by = verifier_id
            obs_db.sdv_verified_at = verified_at

        # Save changes
        await session.commit()

        # Re-query
        stmt_re = select(SDVSignOff).where(SDVSignOff.id == signoff_db.id)
        res_re = await session.execute(stmt_re)
        re_signoff = res_re.scalar_one()

        return SDVSignOffResponse(
            id=re_signoff.id,
            scope=re_signoff.scope,
            target_id=re_signoff.target_id,
            subject_id=re_signoff.subject_id,
            study_id=re_signoff.study_id,
            site_id=re_signoff.site_id,
            is_verified=re_signoff.is_verified,
            verified_by=re_signoff.verified_by,
            verified_at=re_signoff.verified_at,
            dropped_reason=re_signoff.dropped_reason,
            dropped_at=re_signoff.dropped_at,
        )


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


class FormSubmissionCreate(BaseModel):
    study_id: str
    site_id: str
    subject_id: str
    visit_id: Optional[str] = None
    form_id: str


class FormSubmissionResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    subject_id: str
    visit_id: Optional[str] = None
    form_id: str
    status: str
    version: int
    is_deleted: bool
    signature_manifest: Optional[dict[str, Any]] = None


class FormSubmissionApprove(BaseModel):
    signature_manifest: dict[str, Any]
    signing_reason: str


@app.post(
    "/api/v1/execution/form-submissions",
    response_model=FormSubmissionResponse,
    status_code=201,
)
async def create_form_submission(
    request: Request, payload: FormSubmissionCreate
) -> FormSubmissionResponse:
    """Create a new FormSubmission in DRAFT status."""
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        sub = FormSubmission(
            study_id=payload.study_id,
            site_id=payload.site_id,
            subject_id=payload.subject_id,
            visit_id=payload.visit_id,
            form_id=payload.form_id,
            status="DRAFT",
            signature_manifest=None,
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
        )


@app.post(
    "/api/v1/execution/form-submissions/{submission_id}/complete",
    response_model=FormSubmissionResponse,
)
async def complete_form_submission(
    submission_id: str, request: Request
) -> FormSubmissionResponse:
    """Transition a FormSubmission from DRAFT to COMPLETED."""
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        if sub.status != "DRAFT":
            raise HTTPException(
                status_code=400,
                detail=f"Form submission can only be completed from DRAFT status. Current: {sub.status}",
            )

        sub.status = "COMPLETED"
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
        )


@app.post(
    "/api/v1/execution/form-submissions/{submission_id}/approve",
    response_model=FormSubmissionResponse,
)
async def approve_form_submission(
    submission_id: str, request: Request, payload: FormSubmissionApprove
) -> FormSubmissionResponse:
    """PI Approve/Sign-off a completed FormSubmission."""
    verify_change_justification(request)
    verify_roles(request, ["investigator"])

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
        )


@app.get(
    "/api/v1/execution/form-submissions",
    response_model=List[FormSubmissionResponse],
)
async def list_form_submissions(
    study_id: Optional[str] = None,
    subject_id: Optional[str] = None,
    visit_id: Optional[str] = None,
    form_id: Optional[str] = None,
) -> List[FormSubmissionResponse]:
    """List form submissions with filters."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(FormSubmission.is_deleted.is_(False))
        if study_id:
            stmt = stmt.where(FormSubmission.study_id == study_id)
        if subject_id:
            stmt = stmt.where(FormSubmission.subject_id == subject_id)
        if visit_id:
            stmt = stmt.where(FormSubmission.visit_id == visit_id)
        if form_id:
            stmt = stmt.where(FormSubmission.form_id == form_id)

        res = await session.execute(stmt)
        subs = res.scalars().all()

        return [
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
            )
            for sub in subs
        ]


@app.get(
    "/api/v1/execution/form-submissions/{submission_id}",
    response_model=FormSubmissionResponse,
)
async def get_form_submission(submission_id: str) -> FormSubmissionResponse:
    """Retrieve a single form submission by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(FormSubmission).where(
            FormSubmission.id == submission_id, FormSubmission.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        sub = res.scalars().first()
        if not sub:
            raise HTTPException(status_code=404, detail="Form submission not found")

        return FormSubmissionResponse(
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
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/respond",
    response_model=ClinicalQueryResponse,
)
async def respond_query(
    query_id: str,
    request: Request,
    payload: QueryRespond,
    roles: list[str] = Depends(require_roles(ROLE_SITE_INVESTIGATOR)),
) -> ClinicalQueryResponse:
    """Submit an investigator response/answer to an open or reopened clinical query.

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.
        payload (QueryRespond): The investigator's response explanation.

    Returns:
        ClinicalQueryResponse: The updated query with ANSWERED status.
    """
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "ANSWERED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "ANSWERED"
        q.response = payload.response
        q.responder = payload.responder or current_user_id.get()
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


async def _revert_coding_assignment_if_system_query_resolved(
    session, q: ClinicalQuery
) -> None:
    """Helper to revert a QUERY_PENDING coding assignment back to UNCODED when its system query is closed/cancelled."""
    if q.origin == "SYSTEM_CODING":
        stmt_assign = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.observation_id == q.observation_id,
            ClinicalCodingAssignment.status == CodingState.QUERY_PENDING,
            ClinicalCodingAssignment.is_deleted.is_(False),
        )
        res_assign = await session.execute(stmt_assign)
        assignment = res_assign.scalars().first()
        if assignment:
            assignment.status = CodingState.UNCODED
            session.add(assignment)


@app.post(
    "/api/v1/execution/queries/{query_id}/close",
    response_model=ClinicalQueryResponse,
)
async def close_query(
    query_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Close an answered query (resolving the discrepancy loop).

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.

    Returns:
        ClinicalQueryResponse: The updated query with CLOSED status.
    """
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "CLOSED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "CLOSED"
        q.resolver = current_user_id.get()
        q.resolved_at = datetime.now()
        await _revert_coding_assignment_if_system_query_resolved(session, q)
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/reopen",
    response_model=ClinicalQueryResponse,
)
async def reopen_query(
    query_id: str,
    request: Request,
    payload: Optional[QueryReopen] = None,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Reopen an answered or closed clinical query for further clarification.

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.
        payload (Optional[QueryReopen]): Optional reopen payload containing reject reason.

    Returns:
        ClinicalQueryResponse: The updated query with REOPENED status.
    """
    verify_change_justification(request)

    if payload is not None:
        reason_str = payload.reason or ""
    else:
        reason_str = (
            request.headers.get("X-Change-Reason", "")
            or current_change_reason.get()
            or ""
        )
    has_reason = bool(reason_str and reason_str.strip())

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(
                q.status, "REOPENED", has_reason=has_reason
            )
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "REOPENED"
        if has_reason:
            q.explanation = reason_str.strip()
        q.resolver = None
        q.resolved_at = None
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/cancel",
    response_model=ClinicalQueryResponse,
)
async def cancel_query(
    query_id: str,
    request: Request,
    payload: QueryCancel,
    roles: list[str] = Depends(require_roles(ROLE_CRA, ROLE_DATA_MANAGER)),
) -> ClinicalQueryResponse:
    """Cancel a clinical query raised in error.

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.
        payload (QueryCancel): The cancellation reason.

    Returns:
        ClinicalQueryResponse: The updated query with CANCELLED status.
    """
    verify_change_justification(request)

    if not payload.reason or not payload.reason.strip():
        raise HTTPException(
            status_code=400, detail="Cancellation requires a non-empty reason."
        )

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            QueryService.validate_transition(q.status, "CANCELLED", has_reason=True)
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "CANCELLED"
        q.cancellation_reason = payload.reason
        q.resolver = current_user_id.get()
        q.resolved_at = datetime.now()
        await _revert_coding_assignment_if_system_query_resolved(session, q)
        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@app.patch(
    "/api/v1/execution/queries/{query_id}",
    response_model=ClinicalQueryResponse,
)
async def update_query_state(
    query_id: str, request: Request, payload: QueryUpdate
) -> ClinicalQueryResponse:
    """Transition a query through the designated state sequence and perform role checks.

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.
        payload (QueryUpdate): Target status and optional explanation/response fields.

    Returns:
        ClinicalQueryResponse: The updated query record and audit trail.
    """
    verify_change_justification(request)

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        target_status = payload.status.upper()

        # Validate transition
        reason_val = (
            payload.cancellation_reason
            or payload.explanation
            or request.headers.get("X-Change-Reason", "")
            or current_change_reason.get()
            or ""
        ).strip()
        has_reason = bool(reason_val)

        try:
            QueryService.validate_transition(
                q.status, target_status, has_reason=has_reason
            )
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        # Enforce role boundaries depending on target transition state
        user_roles = get_normalized_roles(request)
        cra_dm_roles = {
            "cra",
            "data manager",
            "data_manager",
            "sponsor_dm",
            "dm",
            "admin",
        }
        inv_roles = {
            "site investigator",
            "site_investigator",
            "site-investigator",
            "investigator",
            "investigator_user",
        }

        if target_status in ("CANDIDATE", "OPEN", "CLOSED", "REOPENED", "CANCELLED"):
            if not any(r in cra_dm_roles for r in user_roles):
                raise HTTPException(
                    status_code=403,
                    detail="User role is not authorized for this action.",
                )
        elif target_status == "ANSWERED":
            if not any(r in inv_roles for r in user_roles):
                raise HTTPException(
                    status_code=403,
                    detail="User role is not authorized for this action.",
                )

        q.status = target_status
        if payload.explanation is not None:
            q.explanation = payload.explanation
        if payload.response is not None:
            q.response = payload.response
        if payload.observation_id is not None:
            q.observation_id = payload.observation_id
        if payload.field_link is not None:
            q.field_link = payload.field_link
        if payload.message is not None:
            q.message = payload.message
        if payload.origin is not None:
            q.origin = payload.origin
        if payload.priority is not None:
            q.priority = payload.priority
        if payload.rule_id is not None:
            q.rule_id = payload.rule_id
        if payload.created_by is not None:
            q.created_by = payload.created_by
        if payload.responder is not None:
            q.responder = payload.responder
        if payload.resolver is not None:
            q.resolver = payload.resolver
        if payload.resolved_at is not None:
            q.resolved_at = payload.resolved_at
        if payload.cancellation_reason is not None:
            q.cancellation_reason = payload.cancellation_reason
        if payload.escalated_at is not None:
            q.escalated_at = payload.escalated_at

        if payload.form_id is not None:
            q.form_id = payload.form_id
        if payload.field_id is not None:
            q.field_id = payload.field_id
        if payload.query_type is not None:
            q.query_type = payload.query_type
        if payload.action_required is not None:
            q.action_required = payload.action_required

        if target_status == "CLOSED":
            q.resolver = current_user_id.get()
            q.resolved_at = datetime.now()
        elif target_status == "REOPENED":
            if q.status == "ANSWERED" and payload.explanation:
                q.explanation = payload.explanation
            q.resolver = None
            q.resolved_at = None
        elif target_status == "ANSWERED":
            q.responder = current_user_id.get()
        elif target_status == "CANCELLED":
            if payload.cancellation_reason:
                q.cancellation_reason = payload.cancellation_reason
            elif payload.explanation:
                q.cancellation_reason = payload.explanation
            q.resolver = current_user_id.get()
            q.resolved_at = datetime.now()

        if target_status in ("CLOSED", "CANCELLED"):
            await _revert_coding_assignment_if_system_query_resolved(session, q)

        await session.commit()

        # Refresh
        stmt_ref = select(ClinicalQuery).where(ClinicalQuery.id == q.id)
        res_ref = await session.execute(stmt_ref)
        q_db = res_ref.scalar_one()

        history = await fetch_history(session, q_db.id)
        return ClinicalQueryResponse(
            id=q_db.id,
            study_id=q_db.study_id,
            subject_id=q_db.subject_id,
            visit_id=q_db.visit_id,
            domain=q_db.domain,
            test_code=q_db.test_code,
            status=q_db.status,
            explanation=q_db.explanation,
            response=q_db.response,
            created_at=q_db.created_at,
            updated_at=q_db.updated_at,
            history=history,
            observation_id=q_db.observation_id,
            field_link=q_db.field_link,
            message=q_db.message,
            origin=q_db.origin,
            priority=q_db.priority,
            rule_id=q_db.rule_id,
            created_by=q_db.created_by,
            responder=q_db.responder,
            resolver=q_db.resolver,
            resolved_at=q_db.resolved_at,
            cancellation_reason=q_db.cancellation_reason,
            escalated_at=q_db.escalated_at,
            form_id=q_db.form_id,
            field_id=q_db.field_id,
            query_type=q_db.query_type,
            action_required=q_db.action_required,
        )


@app.post(
    "/api/v1/execution/queries/sync",
    status_code=200,
)
async def sync_queries(
    request: Request,
    payload: SyncRequest,
    roles: list[str] = Depends(
        require_roles(ROLE_CRA, ROLE_DATA_MANAGER, ROLE_SITE_INVESTIGATOR)
    ),
) -> dict[str, Any]:
    """Synchronize clinical query local ledger blocks to the target database.

    Translates local ledger blocks to correct fields in the target database schema,
    verifying caller roles and payload integrity.
    """
    verify_change_justification(request)

    # We map fieldId to CDASH domain & test_code
    field_map = {
        "brthdt": ("DM", "BRTHDT"),
        "sex": ("DM", "SEX"),
        "vssbp": ("VS", "VSSBP"),
        "vsdpb": ("VS", "VSDPB"),
        "pulse": ("VS", "VSHR"),
    }

    # Normalize caller roles
    from packages.security.rbac import ROLE_EXPANSIONS, get_normalized_roles

    user_roles = get_normalized_roles(request)

    expanded_allowed_dm = set(["data manager", "cra"])
    for r in ["data manager", "cra"]:
        if r in ROLE_EXPANSIONS:
            expanded_allowed_dm.update(ROLE_EXPANSIONS[r])

    expanded_allowed_inv = set(["site investigator"])
    for r in ["site investigator"]:
        if r in ROLE_EXPANSIONS:
            expanded_allowed_inv.update(ROLE_EXPANSIONS[r])

    has_dm_role = any(r in expanded_allowed_dm for r in user_roles)
    has_inv_role = any(r in expanded_allowed_inv for r in user_roles)

    # 21 CFR Part 11 compliant offline transaction sync block validation loop
    processed_count = 0
    async with db_manager.get_session_maker()() as session:
        for block in payload.blocks:
            action = block.action.upper()
            details = block.details

            # Validate role for this specific action
            if action in ("QUERY_CREATE", "QUERY_CLOSE", "QUERY_REOPEN"):
                if not has_dm_role:
                    raise HTTPException(
                        status_code=403,
                        detail=f"User role is not authorized for {action} action.",
                    )
            elif action == "QUERY_RESPOND":
                if not has_inv_role:
                    raise HTTPException(
                        status_code=403,
                        detail=f"User role is not authorized for {action} action.",
                    )

            # Extract/determine query coordinates
            study_id = details.studyId or "STUDY-USDM-001"
            subject_id = details.subjectId or "SUBJ-001"
            visit_id = details.visitId or "Screening"

            # Map domain & test_code from fieldId
            mapped_domain, mapped_test = field_map.get(
                details.fieldId.lower(), ("VS", details.fieldId.upper())
            )
            domain = details.domain or mapped_domain
            test_code = details.testCode or mapped_test

            # Find existing active query
            stmt = select(ClinicalQuery).where(
                ClinicalQuery.study_id == study_id,
                ClinicalQuery.subject_id == subject_id,
                ClinicalQuery.visit_id == visit_id,
                ClinicalQuery.domain == domain,
                ClinicalQuery.test_code == test_code,
                ClinicalQuery.is_deleted.is_(False),
            )
            res = await session.execute(stmt)
            q = res.scalars().first()

            if action == "QUERY_CREATE":
                if not q:
                    # Create a new query
                    q = ClinicalQuery(
                        id=str(uuid.uuid4()),
                        study_id=study_id,
                        subject_id=subject_id,
                        visit_id=visit_id,
                        domain=domain,
                        test_code=test_code,
                        status="OPEN",
                        explanation=details.query.message
                        if details.query
                        else "Offline raised discrepancy",
                        message=details.query.message
                        if details.query
                        else "Offline raised discrepancy",
                        created_by=request.state.user_id,
                    )
                    session.add(q)
                    processed_count += 1

            elif action == "QUERY_RESPOND":
                if q:
                    try:
                        QueryService.validate_transition(q.status, "ANSWERED")
                        q.status = "ANSWERED"
                        if details.query and details.query.response:
                            q.response = details.query.response
                        q.responder = request.state.user_id
                        session.add(q)
                        processed_count += 1
                    except StateTransitionError:
                        pass

            elif action == "QUERY_CLOSE":
                if q:
                    try:
                        QueryService.validate_transition(q.status, "CLOSED")
                        q.status = "CLOSED"
                        q.resolver = request.state.user_id
                        q.resolved_at = datetime.utcnow()
                        session.add(q)
                        processed_count += 1
                    except StateTransitionError:
                        pass

            elif action == "QUERY_REOPEN":
                if q:
                    try:
                        QueryService.validate_transition(
                            q.status, "REOPENED", has_reason=True
                        )
                        q.status = "REOPENED"
                        q.resolver = None
                        q.resolved_at = None
                        session.add(q)
                        processed_count += 1
                    except StateTransitionError:
                        pass

        await session.commit()

    return {
        "status": "success",
        "processed_blocks": processed_count,
    }


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
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Locks or freezes a specific site."""
    verify_change_justification(request)
    TrialLockManager.lock_site(site_id)
    return {"status": "success", "message": f"Site {site_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/site/{site_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/site/{site_id}/unfreeze", status_code=200)
async def unlock_site_endpoint(
    site_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific site."""
    verify_change_justification(request)
    TrialLockManager.unlock_site(site_id)
    return {"status": "success", "message": f"Site {site_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/visit/{visit_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/visit/{visit_id}/freeze", status_code=200)
async def lock_visit_endpoint(
    visit_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Locks or freezes a specific visit."""
    verify_change_justification(request)
    TrialLockManager.lock_visit(visit_id)
    return {"status": "success", "message": f"Visit {visit_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/visit/{visit_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/visit/{visit_id}/unfreeze", status_code=200)
async def unlock_visit_endpoint(
    visit_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific visit."""
    verify_change_justification(request)
    TrialLockManager.unlock_visit(visit_id)
    return {"status": "success", "message": f"Visit {visit_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/form/{form_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/form/{form_id}/freeze", status_code=200)
async def lock_form_endpoint(
    form_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Locks or freezes a specific form."""
    verify_change_justification(request)
    TrialLockManager.lock_form(form_id)
    return {"status": "success", "message": f"Form {form_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/form/{form_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/form/{form_id}/unfreeze", status_code=200)
async def unlock_form_endpoint(
    form_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific form."""
    verify_change_justification(request)
    TrialLockManager.unlock_form(form_id)
    return {"status": "success", "message": f"Form {form_id} is unlocked/unfrozen."}


@app.post("/api/v1/execution/locks/subject/{subject_id}/lock", status_code=200)
@app.post("/api/v1/execution/locks/subject/{subject_id}/freeze", status_code=200)
async def lock_subject_endpoint(
    subject_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Locks or freezes a specific subject."""
    verify_change_justification(request)
    TrialLockManager.lock_subject(subject_id)
    return {"status": "success", "message": f"Subject {subject_id} is locked/frozen."}


@app.post("/api/v1/execution/locks/subject/{subject_id}/unlock", status_code=200)
@app.post("/api/v1/execution/locks/subject/{subject_id}/unfreeze", status_code=200)
async def unlock_subject_endpoint(
    subject_id: str,
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Unlocks or unfreezes a specific subject."""
    verify_change_justification(request)
    TrialLockManager.unlock_subject(subject_id)
    return {
        "status": "success",
        "message": f"Subject {subject_id} is unlocked/unfrozen.",
    }


@app.post("/api/v1/execution/locks/trial/lock", status_code=200)
@app.post("/api/v1/execution/locks/trial/freeze", status_code=200)
async def lock_trial_endpoint(
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Locks or freezes the trial/study."""
    verify_change_justification(request)
    reason = request.headers.get("X-Change-Reason", "Sponsor Lock")
    TrialLockManager.lock_trial(reason=reason)
    return {"status": "success", "message": "Trial is locked/frozen."}


@app.post("/api/v1/execution/locks/trial/unlock", status_code=200)
@app.post("/api/v1/execution/locks/trial/unfreeze", status_code=200)
async def unlock_trial_endpoint(
    request: Request,
    roles: list[str] = Depends(require_roles(ROLE_DATA_MANAGER)),
) -> dict[str, str]:
    """Unlocks or unfreezes the trial/study."""
    verify_change_justification(request)
    TrialLockManager.unlock_trial()
    return {"status": "success", "message": "Trial is unlocked/unfrozen."}


# ==========================================
# Coder Action and Coding Assignment API
# ==========================================


class CodingAssignmentResponse(BaseModel):
    id: str
    verbatim_text: str
    source_field: Optional[str] = None
    observation_id: Optional[str] = None
    dictionary_type: str
    dictionary_version: str
    coded_code: Optional[str] = None
    coded_term: Optional[str] = None
    status: str
    recoding_status: str
    assigned_by: Optional[str] = None
    assigned_at: datetime
    score: Optional[float] = None
    hierarchy: Optional[Any] = None
    suggestions: Optional[Any] = None
    domain: Optional[str] = None
    version: int
    is_deleted: bool


class CoderActionRequest(BaseModel):
    action: str  # "ACCEPT" or "OVERRIDE" or "QUERY"
    code: Optional[str] = None  # required for OVERRIDE
    term: Optional[str] = None  # required for OVERRIDE
    suggestion_index: Optional[int] = None  # optional for ACCEPT
    reason_for_change: Optional[str] = None  # required for OVERRIDE


@app.get(
    "/api/v1/execution/coding/assignments",
    response_model=List[CodingAssignmentResponse],
)
async def list_coding_assignments(
    observation_id: Optional[str] = None,
    status: Optional[str] = None,
    verbatim_text: Optional[str] = None,
    dictionary_type: Optional[str] = None,
    roles: list[str] = Depends(get_normalized_roles),
) -> List[CodingAssignmentResponse]:
    """Lists and filters medical coding assignments."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.is_deleted.is_(False)
        )
        if observation_id:
            stmt = stmt.where(ClinicalCodingAssignment.observation_id == observation_id)
        if status:
            stmt = stmt.where(ClinicalCodingAssignment.status == status.upper())
        if verbatim_text:
            stmt = stmt.where(ClinicalCodingAssignment.verbatim_text == verbatim_text)
        if dictionary_type:
            stmt = stmt.where(
                ClinicalCodingAssignment.dictionary_type == dictionary_type.upper()
            )

        res = await session.execute(stmt)
        assignments = res.scalars().all()

        return [
            CodingAssignmentResponse(
                id=a.id,
                verbatim_text=a.verbatim_text,
                source_field=a.source_field,
                observation_id=a.observation_id,
                dictionary_type=a.dictionary_type.value,
                dictionary_version=a.dictionary_version,
                coded_code=a.coded_code,
                coded_term=a.coded_term,
                status=a.status.value,
                recoding_status=a.recoding_status.value,
                assigned_by=a.assigned_by,
                assigned_at=a.assigned_at,
                score=a.score,
                hierarchy=a.hierarchy,
                suggestions=a.suggestions,
                domain=a.domain,
                version=a.version,
                is_deleted=a.is_deleted,
            )
            for a in assignments
        ]


@app.get(
    "/api/v1/execution/coding/assignments/{assignment_id}",
    response_model=CodingAssignmentResponse,
)
async def get_coding_assignment(
    assignment_id: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> CodingAssignmentResponse:
    """Retrieves a single medical coding assignment by ID."""
    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.id == assignment_id,
            ClinicalCodingAssignment.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        a = res.scalars().first()
        if not a:
            raise HTTPException(status_code=404, detail="Coding assignment not found")

        return CodingAssignmentResponse(
            id=a.id,
            verbatim_text=a.verbatim_text,
            source_field=a.source_field,
            observation_id=a.observation_id,
            dictionary_type=a.dictionary_type.value,
            dictionary_version=a.dictionary_version,
            coded_code=a.coded_code,
            coded_term=a.coded_term,
            status=a.status.value,
            recoding_status=a.recoding_status.value,
            assigned_by=a.assigned_by,
            assigned_at=a.assigned_at,
            score=a.score,
            hierarchy=a.hierarchy,
            suggestions=a.suggestions,
            domain=a.domain,
            version=a.version,
            is_deleted=a.is_deleted,
        )


@app.post(
    "/api/v1/execution/coding/assignments/{assignment_id}/action",
    response_model=CodingAssignmentResponse,
)
async def process_coding_action(
    assignment_id: str,
    request: Request,
    payload: CoderActionRequest,
    roles: list[str] = Depends(require_roles("data manager")),
) -> CodingAssignmentResponse:
    """Accepts a suggestion or submits a manual override, persisting results and updating the ledger."""
    # Enforce GxP signature-gated/justification requirement
    verify_change_justification(request)

    action_upper = payload.action.upper()
    if action_upper not in ("ACCEPT", "OVERRIDE", "QUERY"):
        raise HTTPException(
            status_code=400,
            detail=f"Invalid action '{payload.action}'. Allowed actions: ACCEPT, OVERRIDE, QUERY.",
        )

    async with db_manager.get_session_maker()() as session:
        # 1. Fetch existing assignment
        stmt = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.id == assignment_id,
            ClinicalCodingAssignment.is_deleted.is_(False),
        )
        res = await session.execute(stmt)
        assignment = res.scalars().first()
        if not assignment:
            raise HTTPException(status_code=404, detail="Coding assignment not found")

        old_code = assignment.coded_code
        old_term = assignment.coded_term
        old_version = assignment.dictionary_version
        dict_type = assignment.dictionary_type
        version = assignment.dictionary_version

        status = assignment.status
        coded_code = assignment.coded_code
        coded_term = assignment.coded_term
        score = assignment.score
        hierarchy = assignment.hierarchy

        actor = current_user_id.get() or "system"

        if action_upper == "ACCEPT":
            # Must find a suggestion to accept
            if payload.suggestion_index is not None:
                sug_list = assignment.suggestions
                if (
                    not sug_list
                    or not isinstance(sug_list, list)
                    or payload.suggestion_index < 0
                    or payload.suggestion_index >= len(sug_list)
                ):
                    raise HTTPException(
                        status_code=400, detail="Invalid suggestion_index"
                    )
                sug = sug_list[payload.suggestion_index]
                coded_code = sug.get("code") or sug.get("drug_code")
                coded_term = sug.get("term_name") or sug.get("preferred_name")
                score = sug.get("score")
                if dict_type == DBDictionaryType.MEDDRA:
                    hierarchy = sug.get("hierarchies")
                else:
                    hierarchy = {
                        "atc_context": sug.get("atc_context", []),
                        "ingredients": sug.get("ingredients", []),
                    }
            elif payload.code and payload.term:
                # Direct accept of specified code/term if it matches one of the suggestions
                sug_list = assignment.suggestions or []
                found = False
                for sug in sug_list:
                    s_code = sug.get("code") or sug.get("drug_code")
                    if s_code == payload.code:
                        coded_code = s_code
                        coded_term = sug.get("term_name") or sug.get("preferred_name")
                        score = sug.get("score")
                        if dict_type == DBDictionaryType.MEDDRA:
                            hierarchy = sug.get("hierarchies")
                        else:
                            hierarchy = {
                                "atc_context": sug.get("atc_context", []),
                                "ingredients": sug.get("ingredients", []),
                            }
                        found = True
                        break
                if not found:
                    raise HTTPException(
                        status_code=400,
                        detail="The provided code does not match any available suggestions. Use OVERRIDE for manual coding.",
                    )
            else:
                # Accept highest suggestion if available
                sug_list = assignment.suggestions
                if sug_list and isinstance(sug_list, list) and len(sug_list) > 0:
                    sug = sug_list[0]
                    coded_code = sug.get("code") or sug.get("drug_code")
                    coded_term = sug.get("term_name") or sug.get("preferred_name")
                    score = sug.get("score")
                    if dict_type == DBDictionaryType.MEDDRA:
                        hierarchy = sug.get("hierarchies")
                    else:
                        hierarchy = {
                            "atc_context": sug.get("atc_context", []),
                            "ingredients": sug.get("ingredients", []),
                        }
                else:
                    raise HTTPException(
                        status_code=400,
                        detail="No suggestions available to ACCEPT. Use OVERRIDE instead.",
                    )

            # Double check existence of the code/version in DB
            if dict_type == DBDictionaryType.MEDDRA:
                from apps.execution.database.models import MedDRATerm

                stmt_valid = select(MedDRATerm).where(
                    MedDRATerm.dictionary_version == version,
                    MedDRATerm.code == coded_code,
                )
                res_valid = await session.execute(stmt_valid)
                if not res_valid.scalars().first():
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid code '{coded_code}' for MedDRA version '{version}'.",
                    )
            elif dict_type == DBDictionaryType.WHODRUG:
                from apps.execution.database.models import WHODrugRecord

                stmt_valid = select(WHODrugRecord).where(
                    WHODrugRecord.dictionary_version == version,
                    WHODrugRecord.drug_code == coded_code,
                )
                res_valid = await session.execute(stmt_valid)
                if not res_valid.scalars().first():
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid drug code '{coded_code}' for WHODrug version '{version}'.",
                    )

            status = CodingState.CODED

        elif action_upper == "OVERRIDE":
            # Override requires reason_for_change, code, and term
            if not payload.reason_for_change or not payload.reason_for_change.strip():
                raise HTTPException(
                    status_code=400,
                    detail="reason_for_change is required for OVERRIDE action and cannot be empty.",
                )
            if not payload.code or not payload.code.strip():
                raise HTTPException(
                    status_code=400, detail="code is required for OVERRIDE action."
                )
            if not payload.term or not payload.term.strip():
                raise HTTPException(
                    status_code=400, detail="term is required for OVERRIDE action."
                )

            # Validate target code/version
            if dict_type == DBDictionaryType.MEDDRA:
                from apps.execution.database.models import MedDRATerm

                stmt_valid = select(MedDRATerm).where(
                    MedDRATerm.dictionary_version == version,
                    MedDRATerm.code == payload.code.strip(),
                )
                res_valid = await session.execute(stmt_valid)
                if not res_valid.scalars().first():
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid code '{payload.code}' for MedDRA version '{version}'.",
                    )

                # Fetch hierarchy for the overridden term if possible
                from apps.execution.coding.matcher import _get_meddra_hierarchy

                term_obj = MedDRATerm(
                    code=payload.code.strip(),
                    term_name=payload.term.strip(),
                    level="LLT",
                )
                hierarchy = await _get_meddra_hierarchy(session, term_obj, version)

            elif dict_type == DBDictionaryType.WHODRUG:
                from apps.execution.database.models import WHODrugRecord

                stmt_valid = select(WHODrugRecord).where(
                    WHODrugRecord.dictionary_version == version,
                    WHODrugRecord.drug_code == payload.code.strip(),
                )
                res_valid = await session.execute(stmt_valid)
                if not res_valid.scalars().first():
                    raise HTTPException(
                        status_code=400,
                        detail=f"Invalid drug code '{payload.code}' for WHODrug version '{version}'.",
                    )

                # Fetch ATC context and ingredients for WHODrug override
                from apps.execution.coding.matcher import _get_whodrug_context

                rec_obj = WHODrugRecord(
                    drug_code=payload.code.strip(), preferred_name=payload.term.strip()
                )
                atc_context, ingredients = await _get_whodrug_context(
                    session, rec_obj, version
                )
                hierarchy = {"atc_context": atc_context, "ingredients": ingredients}

            coded_code = payload.code.strip()
            coded_term = payload.term.strip()
            score = 1.0  # Perfect manual certainty
            status = CodingState.CODED

        elif action_upper == "QUERY":
            status = CodingState.QUERY_PENDING
            coded_code = None
            coded_term = None
            score = None
            hierarchy = None

        # 2. Update assignment state
        assignment.status = status
        assignment.coded_code = coded_code
        assignment.coded_term = coded_term
        assignment.score = score
        assignment.hierarchy = hierarchy
        assignment.assigned_by = actor
        assignment.assigned_at = datetime.utcnow()

        # 3. Create a ledger record for ACCEPT or OVERRIDE
        if action_upper in ("ACCEPT", "OVERRIDE"):
            ledger = ClinicalCodingLedger(
                assignment_id=assignment.id,
                verbatim_text=assignment.verbatim_text,
                observation_id=assignment.observation_id,
                dictionary_type=dict_type,
                old_dictionary_version=old_version if old_code else None,
                old_coded_code=old_code,
                old_coded_term=old_term,
                new_dictionary_version=version,
                new_coded_code=coded_code,
                new_coded_term=coded_term,
                recoding_reason=payload.reason_for_change
                or f"Manual decision: {action_upper}",
                decision_by=actor,
                decision_at=datetime.utcnow(),
            )
            session.add(ledger)

            # Close any open/active SYSTEM_CODING queries for this observation
            stmt_active_q = select(ClinicalQuery).where(
                ClinicalQuery.observation_id == assignment.observation_id,
                ClinicalQuery.origin == "SYSTEM_CODING",
                ClinicalQuery.status.in_(["CANDIDATE", "OPEN", "ANSWERED", "REOPENED"]),
                ClinicalQuery.is_deleted.is_(False),
            )
            res_active_q = await session.execute(stmt_active_q)
            active_queries = res_active_q.scalars().all()
            for active_q in active_queries:
                active_q.status = "CLOSED"
                active_q.resolver = actor
                active_q.resolved_at = datetime.utcnow()
                active_q.response = f"Resolved via manual coding action: {action_upper} on code {coded_code}."
                session.add(active_q)

        await session.commit()

        # Re-fetch
        stmt_ref = select(ClinicalCodingAssignment).where(
            ClinicalCodingAssignment.id == assignment_id
        )
        res_ref = await session.execute(stmt_ref)
        as_db = res_ref.scalar_one()

        return CodingAssignmentResponse(
            id=as_db.id,
            verbatim_text=as_db.verbatim_text,
            source_field=as_db.source_field,
            observation_id=as_db.observation_id,
            dictionary_type=as_db.dictionary_type.value,
            dictionary_version=as_db.dictionary_version,
            coded_code=as_db.coded_code,
            coded_term=as_db.coded_term,
            status=as_db.status.value,
            recoding_status=as_db.recoding_status.value,
            assigned_by=as_db.assigned_by,
            assigned_at=as_db.assigned_at,
            score=as_db.score,
            hierarchy=as_db.hierarchy,
            suggestions=as_db.suggestions,
            domain=as_db.domain,
            version=as_db.version,
            is_deleted=as_db.is_deleted,
        )


# ==========================================
# Authenticated SDTM/ADaM Dataset-JSON API
# ==========================================


async def run_sdtm_extraction(session, study_id: str, domain: str) -> List[dict]:
    """Helper to retrieve and transform raw observations to SDTM records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = res_obs.scalars().all()

    dom_upper = domain.strip().upper()
    records = []
    if dom_upper == "DM":
        records = extract_dm(subjects, observations)
    elif dom_upper == "AE":
        records, _ = extract_ae(subjects, observations)
    elif dom_upper == "VS":
        records, _ = extract_vs(subjects, observations)
    elif dom_upper == "LB":
        records, _ = extract_lb(subjects, observations)
    elif dom_upper == "MH":
        records, _ = extract_mh(subjects, observations)
    else:
        raise ValueError(f"Unsupported SDTM domain: {domain}")

    for r in records:
        if "DOMAIN" not in r:
            r["DOMAIN"] = dom_upper
    return records


async def run_adam_derivation(session, study_id: str, dataset: str) -> List[dict]:
    """Helper to retrieve and derive ADaM analysis records."""
    stmt_subj = select(ClinicalSubject).where(
        ClinicalSubject.study_id == study_id,
        ClinicalSubject.is_deleted.is_(False),
    )
    res_subj = await session.execute(stmt_subj)
    subjects = res_subj.scalars().all()

    stmt_obs = select(ClinicalObservation).where(
        ClinicalObservation.study_id == study_id,
        ClinicalObservation.is_deleted.is_(False),
    )
    res_obs = await session.execute(stmt_obs)
    observations = res_obs.scalars().all()

    ds_upper = dataset.strip().upper()
    if ds_upper == "ADSL":
        return derive_adsl(subjects, observations)
    elif ds_upper == "ADAE":
        adsl_recs = derive_adsl(subjects, observations)
        ae_recs, _ = extract_ae(subjects, observations)
        records = derive_adae(adsl_recs, ae_recs)
        for r in records:
            if "AEDECOD" not in r or r["AEDECOD"] is None:
                r["AEDECOD"] = r.get("AETERM", "")
        return records
    elif ds_upper == "ADVS":
        adsl_recs = derive_adsl(subjects, observations)
        vs_recs, _ = extract_vs(subjects, observations)
        return derive_advs(adsl_recs, vs_recs)
    else:
        raise ValueError(f"Unsupported ADaM dataset: {dataset}")


@app.get("/api/v1/execution/biostat/sdtm/{domain}")
async def export_sdtm_domain(
    domain: str,
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports SDTM domain data (DM, AE, VS, LB, MH) in CDISC Dataset-JSON format.

    - **Protected Endpoint**: Requires authenticated session under GatewayAuthMiddleware.
    - **Authorized Roles**: CRA, Data Manager, Sponsor Statistician.
    - **Validations**: Automatically validates schema, keys, and values before returning payload.
    - **Media Type Contract**: `application/json` conforming to CDISC Dataset-JSON 1.0.0.
    """
    dom_upper = domain.strip().upper()
    valid_domains = {"DM", "AE", "VS", "LB", "MH"}
    if dom_upper not in valid_domains:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported SDTM domain: '{domain}'. Must be one of {sorted(list(valid_domains))}",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            records = await run_sdtm_extraction(session, study_id, dom_upper)
            dataset_json = serialize_to_dataset_json(
                data={dom_upper: records}, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422, detail=f"Dataset-JSON validation failed: {str(e)}"
            )
        except Exception as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="SDTM",
                dataset_name=dom_upper,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {str(e)}"
            )


@app.get("/api/v1/execution/biostat/adam/{dataset}")
async def export_adam_dataset(
    dataset: str,
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports ADaM dataset data (ADSL, ADAE, ADVS) in CDISC Dataset-JSON format.

    - **Protected Endpoint**: Requires authenticated session under GatewayAuthMiddleware.
    - **Authorized Roles**: CRA, Data Manager, Sponsor Statistician.
    - **Validations**: Automatically validates schema, keys, demographics, and referential consistency.
    - **Media Type Contract**: `application/json` conforming to CDISC Dataset-JSON 1.0.0.
    """
    ds_upper = dataset.strip().upper()
    valid_datasets = {"ADSL", "ADAE", "ADVS"}
    if ds_upper not in valid_datasets:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported ADaM dataset: '{dataset}'. Must be one of {sorted(list(valid_datasets))}",
        )

    async with db_manager.get_session_maker()() as session:
        try:
            records = await run_adam_derivation(session, study_id, ds_upper)
            dataset_json = serialize_to_dataset_json(
                data={ds_upper: records}, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422, detail=f"Dataset-JSON validation failed: {str(e)}"
            )
        except Exception as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="ADaM",
                dataset_name=ds_upper,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {str(e)}"
            )


@app.get("/api/v1/execution/biostat/bundle")
async def export_biostat_bundle(
    study_id: str = Query(..., description="The unique study identifier"),
    roles: list[str] = Depends(
        require_roles(
            ROLE_CRA, ROLE_DATA_MANAGER, "sponsor_statistician", "statistician"
        )
    ),
) -> dict:
    """Exports all SDTM domains and ADaM datasets bundled in a single CDISC Dataset-JSON document.

    - **Protected Endpoint**: Requires authenticated session under GatewayAuthMiddleware.
    - **Authorized Roles**: CRA, Data Manager, Sponsor Statistician.
    - **Validations**: Validates complete structural, domain-level, and cross-dataset referential consistency.
    - **Media Type Contract**: `application/json` conforming to CDISC Dataset-JSON 1.0.0.
    """
    async with db_manager.get_session_maker()() as session:
        try:
            bundle_data = {}
            for dom in ["DM", "AE", "VS", "LB", "MH"]:
                records = await run_sdtm_extraction(session, study_id, dom)
                if records:
                    bundle_data[dom] = records
            for ds in ["ADSL", "ADAE", "ADVS"]:
                records = await run_adam_derivation(session, study_id, ds)
                if records:
                    bundle_data[ds] = records

            if not bundle_data:
                raise HTTPException(
                    status_code=404,
                    detail="No biostat records found for the given study.",
                )

            dataset_json = serialize_to_dataset_json(
                data=bundle_data, study_id=study_id
            )
            validate_dataset_json(dataset_json)

            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="SUCCESS",
            )
            session.add(export_log)
            await session.commit()

            return dataset_json.model_dump()
        except DatasetJSONValidationError as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=422, detail=f"Dataset-JSON validation failed: {str(e)}"
            )
        except Exception as e:
            export_log = BiostatExport(
                study_id=study_id,
                export_type="BUNDLE",
                dataset_name=None,
                status="FAILED",
                error_message=str(e),
            )
            session.add(export_log)
            await session.commit()
            raise HTTPException(
                status_code=500, detail=f"Export execution failed: {str(e)}"
            )
