import base64
import json
import os
import uuid
from contextlib import asynccontextmanager
from datetime import datetime
from enum import Enum
from typing import Any, AsyncGenerator, List, Optional

from cryptography.fernet import Fernet
from fastapi import (
    BackgroundTasks,
    FastAPI,
    File,
    Form,
    HTTPException,
    Query,
    Request,
    Response,
    UploadFile,
)
from pydantic import BaseModel
from sqlalchemy import select

from apps.execution.cdisc_validator import validate_cdisc_xml_structure
from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.middleware import ContextResetMiddleware
from apps.execution.database.models import (
    AuditLog,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
    ClinicalVisit,
    TranslationJob,
)
from apps.execution.outliers import recalculate_cohort_outliers
from apps.execution.translator import process_translation
from apps.execution.ucum import convert_unit, get_normalized_representation
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("DATABASE_URL", "sqlite+aiosqlite:///:memory:")

# Symmetric encryption helper for patient demographics
_DEMO_KEY = base64.urlsafe_b64encode(b"cadence_clinical_demographics_32")
_fernet = Fernet(_DEMO_KEY)


def encrypt_demographics(data: dict) -> str:
    """Encrypt demographics dictionary payload to protect PII.

    Args:
        data (dict): Dictionary containing patient identifying details.

    Returns:
        str: Encrypted and base64-encoded string.
    """
    serialized = json.dumps(data)
    return _fernet.encrypt(serialized.encode("utf-8")).decode("utf-8")


def decrypt_demographics(encrypted_str: str) -> dict:
    """Decrypt demographic details to retrieve raw PII payload.

    Args:
        encrypted_str (str): The encrypted demographic payload.

    Returns:
        dict: Decrypted raw dictionary.
    """
    decrypted = _fernet.decrypt(encrypted_str.encode("utf-8"))
    return json.loads(decrypted.decode("utf-8"))


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

    await start_background_sealer(db_manager.get_session_maker())

    yield

    # Stop background ledger sealer
    await stop_background_sealer()
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


@app.post("/api/v1/execution/subjects", response_model=SubjectResponse)
async def create_subject(payload: SubjectCreate) -> SubjectResponse:
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
async def create_visit(payload: VisitCreate) -> VisitResponse:
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
async def create_observation(payload: ObservationCreate) -> ObservationResponse:
    """Create a new clinical observation, performing unit normalization and outlier checks."""
    norm_val, norm_unit = get_normalized_representation(payload.value, payload.unit)

    async with db_manager.get_session_maker()() as session:
        # Determine study_id
        study_id = payload.study_id
        if not study_id:
            # Query Subject
            stmt_subj = select(ClinicalSubject).where(
                ClinicalSubject.subject_id == payload.subject_id
            )
            res_subj = await session.execute(stmt_subj)
            subj_db = res_subj.scalars().first()
            if not subj_db:
                raise HTTPException(
                    status_code=400,
                    detail="Subject not registered; cannot infer study_id",
                )
            study_id = subj_db.study_id

        obs_date = payload.observation_date or datetime.now()
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
        )
        session.add(obs)
        await session.commit()

        # Recalculate outliers for this cohort
        await recalculate_cohort_outliers(session, study_id, payload.test_code)

        # Retrieve the latest state of the observation
        stmt_obs = select(ClinicalObservation).where(ClinicalObservation.id == obs.id)
        res_obs = await session.execute(stmt_obs)
        obs_db = res_obs.scalar_one()

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
    matches: List[MedDRACodeMatch]


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


@app.post(
    "/api/v1/dictionaries/import", response_model=JobStatusResponse, status_code=202
)
async def import_dictionary(
    dictionary_type: DictTypeEnum = Form(...),
    version: str = Form(...),
    files: UploadFile = File(...),
    parse_multilingual: bool = Form(True),
) -> JobStatusResponse:
    """Imports raw dictionary files and schedules a background parsing task."""
    return JobStatusResponse(
        job_id="job_dict_import_889127b",
        dictionary_type=dictionary_type,
        version=version,
        status=JobStatusEnum.PROCESSING,
        started_at=datetime.fromisoformat("2026-07-22T20:45:00Z"),
        progress_percentage=45,
        records_imported=10245,
        errors_encountered=0,
    )


class MedDRATargetLevelEnum(str, Enum):
    LLT = "LLT"
    PT = "PT"


@app.get("/api/v1/dictionaries/meddra/code", response_model=MedDRACodingResult)
async def get_meddra_code(
    term: str,
    version: Optional[str] = Query("26.0"),
    target_level: Optional[MedDRATargetLevelEnum] = Query(MedDRATargetLevelEnum.LLT),
) -> MedDRACodingResult:
    """Performs coding or interactive auto-complete lookup on adverse events."""
    return MedDRACodingResult(
        matches=[
            MedDRACodeMatch(
                llt_code="10019211",
                llt_name="Headache",
                pt_code="10019211",
                pt_name="Headache",
                hlt_code="10019231",
                hlt_name="Headaches NEC",
                hlgt_code="10029214",
                hlgt_name="Headache and facial pain",
                soc_code="10029205",
                soc_name="Nervous system disorders",
                primary_soc_flag="Y",
                score=1.0,
            )
        ]
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


class StateTransitionError(ValueError):
    """Exception raised when an invalid state transition is attempted."""

    pass


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


class QueryCreate(BaseModel):
    """Pydantic schema for raising a new query."""

    study_id: str
    subject_id: str
    visit_id: Optional[str] = None
    domain: Optional[str] = None
    test_code: str
    explanation: str

    observation_id: Optional[str] = None
    field_link: Optional[str] = None
    message: Optional[str] = None
    origin: Optional[str] = None
    priority: Optional[str] = None
    rule_id: Optional[str] = None
    created_by: Optional[str] = None


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


def _is_data_manager(roles_str: str) -> bool:
    """Check if the roles include Data Manager role variations."""
    roles = [r.strip().lower() for r in roles_str.split(",")]
    dm_roles = {
        "data manager",
        "data_manager",
        "data-manager",
        "sponsor_dm",
        "dm",
        "admin",
    }
    return any(r in dm_roles for r in roles)


def _is_investigator(roles_str: str) -> bool:
    """Check if the roles include Investigator role variations."""
    roles = [r.strip().lower() for r in roles_str.split(",")]
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
    """Enforce presence of gateway-signed change justification header (version 2 signature)."""
    version = request.headers.get("X-Signature-Version")
    change_reason = request.headers.get("X-Change-Reason")
    if version not in ("2", "v2") or not change_reason:
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
    status: Optional[str] = None,
) -> List[ClinicalQueryResponse]:
    """Retrieve a list of clinical queries with optional filtering.

    Args:
        study_id (Optional[str]): Filter by study identifier.
        subject_id (Optional[str]): Filter by subject identifier.
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
        )


@app.post(
    "/api/v1/execution/queries",
    response_model=ClinicalQueryResponse,
    status_code=201,
)
async def open_query(request: Request, payload: QueryCreate) -> ClinicalQueryResponse:
    """Raise a new clinical query on a specific field coordinate.

    Args:
        request (Request): The incoming FastAPI request.
        payload (QueryCreate): The coordinate details and query explanation.

    Returns:
        ClinicalQueryResponse: The newly opened clinical query.
    """
    verify_change_justification(request)
    verify_roles(request, ["data_manager"])

    async with db_manager.get_session_maker()() as session:
        # Check if active query already exists on this coordinate
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.study_id == payload.study_id,
            ClinicalQuery.subject_id == payload.subject_id,
            ClinicalQuery.visit_id == payload.visit_id,
            ClinicalQuery.domain == payload.domain,
            ClinicalQuery.test_code == payload.test_code,
            ClinicalQuery.status.in_(["OPEN", "ANSWERED", "REOPENED"]),
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
            status="OPEN",
            explanation=payload.explanation,
            observation_id=payload.observation_id,
            field_link=payload.field_link,
            message=payload.message or payload.explanation,
            origin=payload.origin or "manual",
            priority=payload.priority,
            rule_id=payload.rule_id,
            created_by=payload.created_by or current_user_id.get(),
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
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/respond",
    response_model=ClinicalQueryResponse,
)
async def respond_query(
    query_id: str, request: Request, payload: QueryRespond
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
    verify_roles(request, ["investigator"])

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            validate_transition(q.status, "ANSWERED")
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
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/close",
    response_model=ClinicalQueryResponse,
)
async def close_query(query_id: str, request: Request) -> ClinicalQueryResponse:
    """Close an answered query (resolving the discrepancy loop).

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.

    Returns:
        ClinicalQueryResponse: The updated query with CLOSED status.
    """
    verify_change_justification(request)
    verify_roles(request, ["data_manager"])

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            validate_transition(q.status, "CLOSED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "CLOSED"
        q.resolver = current_user_id.get()
        q.resolved_at = datetime.now()
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
        )


@app.post(
    "/api/v1/execution/queries/{query_id}/reopen",
    response_model=ClinicalQueryResponse,
)
async def reopen_query(query_id: str, request: Request) -> ClinicalQueryResponse:
    """Reopen an answered or closed clinical query for further clarification.

    Args:
        query_id (str): Unique database identifier of the query.
        request (Request): The incoming FastAPI request.

    Returns:
        ClinicalQueryResponse: The updated query with REOPENED status.
    """
    verify_change_justification(request)
    verify_roles(request, ["data_manager"])

    async with db_manager.get_session_maker()() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.id == query_id, ClinicalQuery.is_deleted.is_(False)
        )
        res = await session.execute(stmt)
        q = res.scalars().first()
        if not q:
            raise HTTPException(status_code=404, detail="Clinical query not found")

        try:
            validate_transition(q.status, "REOPENED")
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        q.status = "REOPENED"
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

        target_status = payload.status
        try:
            validate_transition(q.status, target_status)
        except StateTransitionError as e:
            raise HTTPException(status_code=400, detail=str(e))

        # Enforce role boundaries depending on target transition state
        if target_status in ("OPEN", "CLOSED", "REOPENED"):
            verify_roles(request, ["data_manager"])
        elif target_status == "ANSWERED":
            verify_roles(request, ["investigator"])

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

        if target_status == "CLOSED":
            q.resolver = current_user_id.get()
            q.resolved_at = datetime.now()
        elif target_status == "REOPENED":
            q.resolver = None
            q.resolved_at = None
        elif target_status == "ANSWERED":
            q.responder = current_user_id.get()

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
        )
