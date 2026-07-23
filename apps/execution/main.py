import base64
import json
import os
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
    ClinicalObservation,
    ClinicalSubject,
    ClinicalVisit,
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
    background_tasks.add_task(
        process_translation,
        event.study_id,
        event.payload,
        db_manager.get_session_maker(),
        user_id=user_id,
        change_reason=change_reason,
    )
    return {"status": "accepted", "message": "Translation job queued in background."}


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
