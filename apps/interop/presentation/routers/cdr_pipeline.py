"""FastAPI Router for the Automated CDISC SDTM/ADaM Pipeline & Dataset-JSON / Define-XML Compiler.

Endpoints:
- POST /api/v1/interop/cdr/pipeline/execute: Execute full continuous SDTM+ADaM+Dataset-JSON+Define-XML pipeline
- POST /api/v1/interop/cdr/pipeline/sdtm: Execute SDTM 10-domain transformation engine
- POST /api/v1/interop/cdr/pipeline/adam: Execute ADaM 4-dataset derivation engine
- GET  /api/v1/interop/cdr/pipeline/results/{execution_id}: Retrieve execution result manifest
- POST /api/v1/interop/cdr/export/dataset-json: Export datasets in CDISC Dataset-JSON 1.1 / 2.0 format
- POST /api/v1/interop/cdr/export/define-xml: Auto-compile and export CDISC Define-XML v2.1 document
- POST /api/v1/interop/cdr/validate/dataset-json: Conformance validation for Dataset-JSON payloads
- POST /api/v1/interop/cdr/validate/define-xml: Conformance validation for Define-XML 2.1 documents
- GET  /api/v1/interop/cdr/metadata/catalog: Retrieve metadata catalog for domains, variables, and VLM

Requirements: PRD-CDR-002, PRD-CDR-003, ADR-2199, 21 CFR Part 11
"""

from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Request, Response, status
from sqlalchemy.ext.asyncio import AsyncSession

from apps.interop.application.cdr_pipeline_service import CDRPipelineService
from apps.interop.domain.cdr.models import (
    DatasetJSONDocument,
    DefineXMLCompilationRequest,
    DefineXMLValidationResult,
    PipelineExecutionResult,
)
from apps.interop.domain.cdr.submission_engine import CdiscSubmissionEngine
from apps.interop.domain.lakehouse_models import LakehouseAction
from apps.interop.infrastructure.database import db_manager
from apps.interop.infrastructure.lakehouse_repository import LakehouseRepository
from apps.interop.presentation.cdr_pipeline_dtos import (
    ADaMPipelineResponse,
    DomainCatalogResponse,
    ExportDatasetJSONRequest,
    ExportDefineXMLRequest,
    RunADaMPipelineRequest,
    RunContinuousPipelineRequest,
    RunSDTMPipelineRequest,
    SDTMPipelineResponse,
    ValidateDatasetJSONRequest,
    ValidateDatasetJSONResponse,
    ValidateDefineXMLRequest,
)
from packages.database import DatabaseSessionDependency
from packages.security.trial_roles import get_normalized_request_roles

router = APIRouter(prefix="/api/v1/interop/cdr", tags=["CDR Pipeline & CDISC Compiler"])
get_db_session = DatabaseSessionDependency(db_manager)

UNBLINDED_ROLES = {
    "admin",
    "emergency_unblinder",
    "idmc",
    "pharmacist",
    "sponsor_admin",
    "unblinded_statistician",
}

# Global in-memory cache for execution artifacts
_pipeline_service_singleton: CDRPipelineService | None = None


def _get_cdr_service(session: AsyncSession) -> CDRPipelineService:
    global _pipeline_service_singleton
    repository = LakehouseRepository(session)
    if _pipeline_service_singleton is None:
        _pipeline_service_singleton = CDRPipelineService(repository)
    else:
        _pipeline_service_singleton.repository = repository
    return _pipeline_service_singleton


def _extract_principal_info(request: Request) -> tuple[str, str, bool]:
    """Extract user_id, primary role, and unblinded access status from request context."""
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or request.headers.get("x-user-id")
        or "system"
    )

    roles = get_normalized_request_roles(request)
    primary_role = roles[0] if roles else "data_manager"

    unblinded_header = (
        request.headers.get("X-Unblinded-Access")
        or request.headers.get("x-unblinded-access")
        or ""
    ).lower() in ("true", "1", "yes")

    has_unblinded_role = any(r in UNBLINDED_ROLES for r in roles)
    is_unblinded_access = unblinded_header and has_unblinded_role

    return user_id, primary_role, is_unblinded_access


# =========================================================================
# Continuous Transformation Pipeline Endpoints
# =========================================================================


@router.post(
    "/pipeline/execute",
    response_model=PipelineExecutionResult,
    status_code=status.HTTP_200_OK,
    summary="Execute end-to-end continuous CDISC SDTM/ADaM pipeline and compiler",
)
async def execute_continuous_pipeline(
    payload: RunContinuousPipelineRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> PipelineExecutionResult:
    """Execute automated continuous transformation mapping raw clinical observations to 10 SDTM domains, 4 ADaM datasets, Dataset-JSON, and Define-XML."""
    user_id, role, is_unblinded = _extract_principal_info(request)
    service = _get_cdr_service(session)

    return await service.execute_continuous_pipeline(
        study_id=payload.study_id,
        snapshot_id=payload.snapshot_id,
        user_id=user_id,
        user_role=role,
        is_unblinded=is_unblinded,
        dataset_json_version=payload.dataset_json_version,
        reason_for_change=payload.reason_for_change,
    )


@router.get(
    "/pipeline/results/{execution_id}",
    response_model=PipelineExecutionResult,
    summary="Retrieve execution manifest for a pipeline run",
)
async def get_pipeline_execution_result(
    execution_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> PipelineExecutionResult:
    """Retrieve details and metadata summary for a completed pipeline run."""
    service = _get_cdr_service(session)
    result = service.get_execution_result(execution_id)
    if not result:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Pipeline execution result with ID '{execution_id}' not found.",
        )
    return result


@router.post(
    "/pipeline/sdtm",
    response_model=SDTMPipelineResponse,
    summary="Execute CDISC SDTM 10-domain transformation engine",
)
async def execute_sdtm_pipeline(
    payload: RunSDTMPipelineRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> SDTMPipelineResponse:
    """Transform canonical observations into 10 standard CDISC SDTM domains."""
    user_id, role, is_unblinded = _extract_principal_info(request)
    repository = LakehouseRepository(session)

    if payload.snapshot_id:
        records = await repository.get_snapshot_records(
            snapshot_id=payload.snapshot_id,
            study_id=payload.study_id,
            is_unblinded=is_unblinded,
        )
    else:
        records = await repository.get_records(
            study_id=payload.study_id,
        )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    all_domains = engine.compile_sdtm(records=records)

    target_domains = payload.domains if payload.domains else list(all_domains.keys())
    filtered_domains = {
        d: [r.model_dump() for r in all_domains.get(d, [])]
        for d in target_domains
        if d in all_domains
    }

    counts = {k: len(v) for k, v in filtered_domains.items()}
    total = sum(counts.values())

    return SDTMPipelineResponse(
        study_id=payload.study_id,
        snapshot_id=payload.snapshot_id,
        domain_counts=counts,
        total_records=total,
        domains=filtered_domains,
    )


@router.post(
    "/pipeline/adam",
    response_model=ADaMPipelineResponse,
    summary="Execute CDISC ADaM 4-dataset derivation engine",
)
async def execute_adam_pipeline(
    payload: RunADaMPipelineRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> ADaMPipelineResponse:
    """Execute statistical derivation rules generating ADSL, ADAE, ADTTE, BDS datasets."""
    user_id, role, is_unblinded = _extract_principal_info(request)
    repository = LakehouseRepository(session)

    if payload.snapshot_id:
        records = await repository.get_snapshot_records(
            snapshot_id=payload.snapshot_id,
            study_id=payload.study_id,
            is_unblinded=is_unblinded,
        )
    else:
        records = await repository.get_records(
            study_id=payload.study_id,
        )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    sdtm_domains = engine.compile_sdtm(records=records)
    all_adam = engine.compile_adam(sdtm_domains=sdtm_domains)

    target_datasets = payload.datasets if payload.datasets else list(all_adam.keys())
    filtered_adam = {
        d: [r.model_dump() for r in all_adam.get(d, [])]
        for d in target_datasets
        if d in all_adam
    }

    counts = {k: len(v) for k, v in filtered_adam.items()}
    total = sum(counts.values())

    return ADaMPipelineResponse(
        study_id=payload.study_id,
        snapshot_id=payload.snapshot_id,
        dataset_counts=counts,
        total_records=total,
        datasets=filtered_adam,
    )


# =========================================================================
# Export & Validation Endpoints
# =========================================================================


@router.post(
    "/export/dataset-json",
    response_model=DatasetJSONDocument,
    summary="Export clinical datasets in official CDISC Dataset-JSON 1.1 / 2.0",
)
async def export_dataset_json(
    payload: ExportDatasetJSONRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> DatasetJSONDocument:
    """Serialize and export SDTM and ADaM datasets in CDISC Dataset-JSON 1.1 / 2.0 format."""
    user_id, role, is_unblinded = _extract_principal_info(request)
    service = _get_cdr_service(session)

    # Fetch records and build domains
    if payload.snapshot_id:
        records = await service.repository.get_snapshot_records(
            snapshot_id=payload.snapshot_id,
            study_id=payload.study_id,
            is_unblinded=is_unblinded,
        )
    else:
        records = await service.repository.get_records(
            study_id=payload.study_id,
        )

    engine = CdiscSubmissionEngine(
        study_id=payload.study_id, spec_version=payload.dataset_json_version
    )
    sdtm_domains = engine.compile_sdtm(records=records)
    adam_datasets = engine.compile_adam(sdtm_domains=sdtm_domains)

    if not is_unblinded:
        sdtm_domains, adam_datasets = engine.mask_blinding(sdtm_domains, adam_datasets)

    all_data = {**sdtm_domains, **adam_datasets}
    if payload.datasets:
        all_data = {k: v for k, v in all_data.items() if k in payload.datasets}

    doc, json_str, _ = engine.compile_dataset_json(
        datasets=all_data,
        spec_version=payload.dataset_json_version,
    )

    # Write audit log
    await service.write_audit_log(
        user_id=user_id,
        user_role=role,
        action=LakehouseAction.POOL_EXPORT,
        study_id=payload.study_id,
        snapshot_id=payload.snapshot_id,
        details={
            "action": "EXPORT_DATASET_JSON",
            "dataset_count": len(all_data),
            "version": payload.dataset_json_version,
        },
        reason_for_change=payload.reason_for_change,
    )

    return doc


@router.post(
    "/export/define-xml",
    summary="Auto-compile and export CDISC Define-XML v2.1 metadata specification",
)
async def export_define_xml(
    payload: ExportDefineXMLRequest,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """Auto-compile CDISC Define-XML v2.1 metadata document encapsulating dataset definitions, VLM, WhereClauses, and Origins."""
    user_id, role, is_unblinded = _extract_principal_info(request)
    service = _get_cdr_service(session)

    compilation_req = DefineXMLCompilationRequest(
        study_id=payload.study_id,
        study_name=payload.study_name or f"Study {payload.study_id}",
        protocol_name=payload.protocol_name or f"PROTOCOL-{payload.study_id}",
        metadata_version_oid=payload.metadata_version_oid,
        standard_name=payload.standard_name,
        standard_version=payload.standard_version,
        domains=payload.domains,
        adam_datasets=payload.adam_datasets,
        include_vlm=payload.include_vlm,
        include_where_clauses=payload.include_where_clauses,
        include_origins=payload.include_origins,
    )

    engine = CdiscSubmissionEngine(study_id=payload.study_id)
    xml_content, _ = engine.compile_define_xml(compilation_req)

    # Write audit log
    await service.write_audit_log(
        user_id=user_id,
        user_role=role,
        action=LakehouseAction.POOL_EXPORT,
        study_id=payload.study_id,
        snapshot_id=None,
        details={
            "action": "EXPORT_DEFINE_XML",
            "define_version": "2.1.0",
            "domains": payload.domains,
            "adam_datasets": payload.adam_datasets,
        },
        reason_for_change=payload.reason_for_change,
    )

    return Response(
        content=xml_content,
        media_type="application/xml",
        headers={
            "Content-Disposition": f"attachment; filename=define_{payload.study_id.lower()}.xml"
        },
    )


@router.post(
    "/validate/dataset-json",
    response_model=ValidateDatasetJSONResponse,
    summary="Validate a Dataset-JSON 1.1 / 2.0 payload",
)
async def validate_dataset_json_endpoint(
    payload: ValidateDatasetJSONRequest,
) -> ValidateDatasetJSONResponse:
    """Perform strict structural and schema conformance validation for CDISC Dataset-JSON."""
    engine = CdiscSubmissionEngine()
    res = engine.validate_dataset_json(payload.payload)
    return ValidateDatasetJSONResponse(
        is_valid=res["is_valid"],
        errors=res["errors"],
        warnings=res["warnings"],
        dataset_count=res["dataset_count"],
        record_count=res["record_count"],
    )


@router.post(
    "/validate/define-xml",
    response_model=DefineXMLValidationResult,
    summary="Validate a CDISC Define-XML v2.1 document",
)
async def validate_define_xml_endpoint(
    payload: ValidateDefineXMLRequest,
) -> DefineXMLValidationResult:
    """Validate CDISC Define-XML v2.1 document syntax, ODM root, and element presence."""
    engine = CdiscSubmissionEngine()
    return engine.validate_define_xml(payload.xml_content)


@router.get(
    "/metadata/catalog",
    response_model=DomainCatalogResponse,
    summary="Retrieve CDISC domain and variable metadata catalog",
)
async def get_metadata_catalog() -> DomainCatalogResponse:
    """Retrieve catalog of supported SDTM domains, ADaM datasets, variable definitions, and VLM."""
    engine = CdiscSubmissionEngine()
    cat = engine.get_metadata_catalog()

    return DomainCatalogResponse(
        sdtm_domains=cat["sdtm_domains"],
        adam_datasets=cat["adam_datasets"],
        domain_metadata=cat["domain_metadata"],
        vlm_catalog=cat["vlm_catalog"],
        codelist_catalog=cat["codelist_catalog"],
    )
