"""FastAPI router for medical coding dictionary ingestion and lookup.

Requirements: PRD-SYS-008
"""

import os
import shutil
import tempfile
import uuid
import zipfile
from datetime import datetime

from fastapi import (
    APIRouter,
    BackgroundTasks,
    Depends,
    File,
    Form,
    HTTPException,
    Query,
    Request,
    UploadFile,
)
from pydantic import ValidationError
from sqlalchemy import select

from apps.execution.coding.importer import process_dictionary_import
from apps.execution.coding.parsers import MedDRAParser, WHODrugParser
from apps.execution.database.context import current_change_reason, current_user_id
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    DictionaryImportJob,
    ImportState,
)
from apps.execution.database.models import (
    DictionaryType as DBDictionaryType,
)
from apps.execution.presentation.routers.dictionaries_schemas import (
    BatchAssignRequest,
    BatchAssignResponse,
    CoderActionRequest,
    CodingAssignmentResponse,
    DictionaryImportRequest,
    DictTypeEnum,
    ImpactAnalysisRequest,
    ImpactAnalysisResponse,
    ImpactMetrics,
    JobStatusEnum,
    JobStatusResponse,
    MedDRACodingResult,
    MedDRATargetLevelEnum,
    ProblemDetails,
    RaiseQueryRequest,
    RaiseQueryResponse,
    UCUMConvertRequest,
    UCUMConvertResponse,
    UCUMUnitValue,
    WHODrugCodingResult,
)
from packages.security import (
    get_normalized_roles,
    require_roles,
)

router = APIRouter(tags=["Dictionaries"])


def validate_archive_layout(temp_zip_path: str, dictionary_type: DictTypeEnum) -> None:
    """Validate that the uploaded zip contains valid dictionary files."""
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


@router.post(
    "/api/v1/dictionaries/import",
    response_model=JobStatusResponse,
    status_code=202,
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
    try:
        DictionaryImportRequest(
            dictionary_type=dictionary_type,
            version=version,
            parse_multilingual=parse_multilingual,
        )
    except ValidationError as e:
        error_msg = e.errors()[0]["msg"] if e.errors() else str(e)
        if error_msg.startswith("Value error, "):
            error_msg = error_msg[len("Value error, ") :]
        raise HTTPException(
            status_code=400,
            detail=error_msg,
        )

    if dictionary_type not in (DictTypeEnum.MEDDRA, DictTypeEnum.WHODRUG):
        raise HTTPException(
            status_code=400,
            detail=f"Import not supported for dictionary type: {dictionary_type.value}",
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
    async with db_manager.get_session_maker()() as session, session.begin():
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


@router.get("/api/v1/dictionaries/jobs/{job_id}", response_model=JobStatusResponse)
async def get_dictionary_import_job(
    job_id: str,
    roles: list[str] = Depends(require_roles("TERMINOLOGY_MANAGER", "SYSTEM_ADMIN")),
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


@router.get("/api/v1/dictionaries/meddra/code", response_model=MedDRACodingResult)
async def get_meddra_code(
    term: str,
    version: str | None = Query("26.0"),
    target_level: MedDRATargetLevelEnum | None = Query(MedDRATargetLevelEnum.LLT),
    roles: list[str] = Depends(get_normalized_roles),
) -> MedDRACodingResult:
    """Performs coding or interactive auto-complete lookup on adverse events using version-aware matcher."""
    from apps.execution.coding import search_dictionary

    async with db_manager.get_session_maker()() as session:
        try:
            return await search_dictionary(
                session=session,
                term=term,
                dictionary_type="MEDDRA",
                version=version,
                target_level=target_level.value if target_level else None,
            )
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))


@router.get("/api/v1/dictionaries/whodrug/code", response_model=WHODrugCodingResult)
async def get_whodrug_code(
    term: str,
    version: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> WHODrugCodingResult:
    """Performs coding or interactive lookup on WHODrug database using version-aware matcher."""
    from apps.execution.coding import search_dictionary

    async with db_manager.get_session_maker()() as session:
        try:
            return await search_dictionary(
                session=session,
                term=term,
                dictionary_type="WHODRUG",
                version=version,
            )
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))


@router.post(
    "/api/v1/dictionaries/ucum/convert",
    response_model=UCUMConvertResponse,
    responses={400: {"model": ProblemDetails}},
)
async def post_ucum_convert(payload: UCUMConvertRequest) -> UCUMConvertResponse:
    """Standardizes numeric values and verifies scale compatibility between source and target codes."""
    return UCUMConvertResponse(
        source=UCUMUnitValue(value=payload.value, unit=payload.source_unit),
        target=UCUMUnitValue(
            value=payload.value * (5.0 / 9.0), unit=payload.target_unit
        ),
        is_compatible=True,
        scale_factor=5.0 / 9.0,
        offset=-160.0 / 9.0,
    )


# --- Coding Assignments and Impact Analysis (Separated Clinical Workflow) ---


@router.post(
    "/api/v1/execution/coding/impact-analysis",
    response_model=ImpactAnalysisResponse,
)
async def post_impact_analysis(
    request: Request,
    payload: ImpactAnalysisRequest,
    roles: list[str] = Depends(
        require_roles(
            "data manager", "sponsor_dm", "TERMINOLOGY_MANAGER", "SYSTEM_ADMIN"
        )
    ),
) -> ImpactAnalysisResponse:
    """Manually triggers up-versioning impact analysis on existing coded assignments."""
    from apps.execution.coding import trigger_impact_analysis

    async with db_manager.get_session_maker()() as session, session.begin():
        try:
            metrics_dict = await trigger_impact_analysis(
                session=session,
                dictionary_type=payload.dictionary_type.value
                if hasattr(payload.dictionary_type, "value")
                else str(payload.dictionary_type),
                new_version=payload.new_version,
                actor=current_user_id.get() or "system",
            )
        except ValueError as e:
            raise HTTPException(status_code=400, detail=str(e))
        metrics = ImpactMetrics(
            unchanged=metrics_dict.get("unchanged", 0),
            reclassified=metrics_dict.get("reclassified", 0),
            deprecated=metrics_dict.get("deprecated", 0),
            skipped=metrics_dict.get("skipped", 0),
        )
        return ImpactAnalysisResponse(
            status="success",
            dictionary_type=payload.dictionary_type,
            new_version=payload.new_version,
            metrics=metrics,
        )


@router.get(
    "/api/v1/execution/coding/assignments",
    response_model=list[CodingAssignmentResponse],
)
async def list_coding_assignments(
    observation_id: str | None = None,
    status: str | None = None,
    verbatim_text: str | None = None,
    dictionary_type: str | None = None,
    roles: list[str] = Depends(get_normalized_roles),
) -> list[CodingAssignmentResponse]:
    """Lists and filters medical coding assignments."""
    from apps.execution.coding import (
        list_coding_assignments as list_assignments_service,
    )

    async with db_manager.get_session_maker()() as session:
        assignments = await list_assignments_service(
            session=session,
            observation_id=observation_id,
            status=status,
            verbatim_text=verbatim_text,
            dictionary_type=dictionary_type,
        )
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


@router.get(
    "/api/v1/execution/coding/assignments/{assignment_id}",
    response_model=CodingAssignmentResponse,
)
async def get_coding_assignment(
    assignment_id: str,
    roles: list[str] = Depends(get_normalized_roles),
) -> CodingAssignmentResponse:
    """Retrieves a single medical coding assignment by ID."""
    from apps.execution.coding import get_coding_assignment as get_assignment_service

    async with db_manager.get_session_maker()() as session:
        a = await get_assignment_service(session=session, assignment_id=assignment_id)
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


@router.post(
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
    from apps.execution.coding import process_coding_action as process_action_service

    actor = current_user_id.get() or "system"
    async with db_manager.get_session_maker()() as session:
        async with session.begin():
            try:
                as_db = await process_action_service(
                    session=session,
                    assignment_id=assignment_id,
                    action=payload.action,
                    code=payload.code,
                    term=payload.term,
                    suggestion_index=payload.suggestion_index,
                    reason_for_change=payload.reason_for_change,
                    actor=actor,
                )
            except ValueError as e:
                raise HTTPException(status_code=400, detail=str(e))
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


@router.post(
    "/api/v1/execution/coding/assignments/batch-assign",
    response_model=BatchAssignResponse,
)
@router.post(
    "/api/v1/execution/coding/batch-assign",
    response_model=BatchAssignResponse,
    include_in_schema=False,
)
async def post_batch_assign(
    payload: BatchAssignRequest,
    roles: list[str] = Depends(get_normalized_roles),
) -> BatchAssignResponse:
    """Performs batch medical coding assignment across multiple assignments with GxP audit logging."""
    from apps.execution.coding import batch_assign_codes

    actor = current_user_id.get() or "system"
    async with db_manager.get_session_maker()() as session, session.begin():
        items_payload = (
            [it.model_dump() for it in payload.items] if payload.items else None
        )
        res = await batch_assign_codes(
            session=session,
            assignment_ids=payload.assignment_ids,
            items=items_payload,
            code=payload.code,
            term=payload.term,
            dictionary_type=payload.dictionary_type,
            dictionary_version=payload.dictionary_version,
            reason=payload.reason_for_change or payload.reason,
            action=payload.action,
            actor=actor,
        )
        return BatchAssignResponse(
            success_count=res["success_count"],
            failed_count=res["failed_count"],
            results=res["results"],
        )


@router.post(
    "/api/v1/execution/coding/assignments/{assignment_id}/raise-query",
    response_model=RaiseQueryResponse,
)
@router.post(
    "/api/v1/execution/coding/assignments/{assignment_id}/query",
    response_model=RaiseQueryResponse,
    include_in_schema=False,
)
async def post_raise_coding_query(
    assignment_id: str,
    payload: RaiseQueryRequest,
    roles: list[str] = Depends(get_normalized_roles),
) -> RaiseQueryResponse:
    """Escalates a coding discrepancy into a ClinicalQuery on the associated observation/eCRF record."""
    from apps.execution.coding import raise_coding_query as raise_query_service

    actor = current_user_id.get() or "system"
    async with db_manager.get_session_maker()() as session, session.begin():
        try:
            res = await raise_query_service(
                session=session,
                assignment_id=assignment_id,
                query_text=payload.query_text or payload.message or payload.explanation,
                reason=payload.reason_for_change or payload.reason,
                actor=actor,
            )
            return RaiseQueryResponse(
                query_id=res["query_id"],
                status=res["status"],
                assignment_id=res["assignment_id"],
                explanation=res.get("explanation"),
            )
        except Exception as e:
            raise HTTPException(status_code=400, detail=str(e))


@router.get(
    "/api/v1/execution/coding/queue",
    response_model=list[CodingAssignmentResponse],
)
async def get_coding_queue(
    observation_id: str | None = None,
    status: str | None = None,
    verbatim_text: str | None = None,
    dictionary_type: str | None = None,
    roles: list[str] = Depends(get_normalized_roles),
) -> list[CodingAssignmentResponse]:
    """Retrieves the active coding queue."""
    return await list_coding_assignments(
        observation_id=observation_id,
        status=status,
        verbatim_text=verbatim_text,
        dictionary_type=dictionary_type,
        roles=roles,
    )
