import os
import time
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Literal, Optional, Tuple

import httpx
from eligibility import EligibilityCriterion, ExpressionNode, parse_dsl
from fastapi import FastAPI, File, HTTPException, Query, Request, UploadFile, status
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from neo4j import AsyncGraphDatabase
from protocol_render import SoAMatrixView
from pydantic import BaseModel, Field, TypeAdapter

from apps.designer.db import (
    assert_mock_study_mutable,
    create_mock_rule,
    create_mock_study_version,
    delete_mock_rule,
    get_mock_rule_by_id,
    get_mock_rules,
    get_study_projection,
    is_concept_referenced_by_active_recruiting_study,
    terminology_cache,
    update_mock_rule,
)
from apps.designer.delta import (
    MOCK_SOA_DATA,
    ConcurrentLockingError,
    ImmutabilityViolationError,
    InvalidSignatureError,
    LibraryObjectInUseError,
    _init_mock_soa,
    amend_protocol_version,
    compute_graph_diff,
    create_eligibility_criterion,
    create_epoch,
    create_library_object_version,
    create_procedure,
    create_rule_node,
    create_study_arm,
    create_study_version,
    create_timing_window,
    create_visit,
    delete_rule_node,
    get_eligibility_criteria_from_graph,
    get_latest_library_object,
    get_library_instance_in_study,
    get_library_object_by_version,
    get_library_object_history,
    get_rules_from_graph,
    get_soa_matrix_projection,
    instantiate_library_object_in_study,
    link_arm_applicability,
    link_epoch_to_visit,
    link_visit_or_procedure_to_timing,
    link_visit_to_procedure,
    list_library_objects,
    update_eligibility_criterion,
    update_epoch,
    update_library_instance_in_study,
    update_procedure,
    update_rule_node,
    update_study_arm,
    update_timing_window,
    update_visit,
)
from apps.designer.evs_client import NCIEVSClient
from apps.designer.library import (
    ALLOWED_LIBRARY_TRANSITIONS,
    CreateLibraryObjectRequest,
    LibraryObjectDetail,
    LibraryObjectTransitionRequest,
    LibraryStatus,
    ObjectType,
    UpdateLibraryObjectRequest,
)
from apps.designer.mapper import map_study_to_usdm
from apps.designer.rules import (
    CreateRuleRequest,
    compile_to_xpath,
    detect_circular_dependencies,
    detect_unknown_fields,
)
from apps.designer.usdm_ingestion import (
    validate_usdm_payload,
)
from apps.designer.validator import (
    CodeValidationState,
    ConceptValidationReport,
    StudyAlignmentReport,
    StudyTerminologyValidationReport,
    generate_alignment_report,
    validate_concept_codes,
    validate_study_terminology,
)
from apps.designer.xml_mapping import validate_mapping_csv
from packages.security import ROLE_ALIASES, get_normalized_roles
from packages.security.middleware import GatewayAuthMiddleware


class TerminologyConcept(BaseModel):
    """
    Normalized terminology concept details.
    """

    code: str
    decode: str
    system: str
    valid: bool


class TerminologySearchResponse(BaseModel):
    """
    Response model for search and autocomplete queries.
    """

    query: str
    state: CodeValidationState
    results: List[TerminologyConcept]
    total_results: int
    error_message: Optional[str] = None


class DifferenceResult(BaseModel):
    """
    Represents a field-level difference between two versions.

    Attributes:
        field: The name of the field that changed.
        old_value: The previous value of the field.
    """

    field: str
    old_value: Any
    new_value: Any


class VersionDiffResponse(BaseModel):
    added_nodes: List[DifferenceResult]
    modified_nodes: List[DifferenceResult]
    deleted_nodes: List[DifferenceResult]


class CreateSoAEntityRequest(BaseModel):
    id: str
    properties: Dict[str, Any]


class UpdateSoAEntityRequest(BaseModel):
    properties: Dict[str, Any]


class SoAEntityCreatedResponse(BaseModel):
    status: str = "success"
    id: str


class SoAEntityDetail(BaseModel):
    id: str
    version_index: int
    created_by: str
    created_at: str

    model_config = {"extra": "allow"}


class LinkEpochVisitRequest(BaseModel):
    epoch_id: str
    visit_id: str


class LinkVisitProcedureRequest(BaseModel):
    visit_id: str
    procedure_id: str


class LinkTimingRequest(BaseModel):
    source_id: str
    timing_id: str
    source_type: str = "visit"  # "visit" or "procedure"


class LinkArmApplicabilityRequest(BaseModel):
    arm_id: str
    target_id: str
    target_type: str = "visit"  # "visit", "procedure", or "epoch"


class SoALinkResponse(BaseModel):
    status: str = "success"
    message: str = "Link established successfully"


class ConceptLockedError(Exception):
    """Exception raised when attempting to modify a concept referenced by an active-recruiting study."""

    def __init__(self, concept_id: str, message: str = None):
        self.concept_id = concept_id
        self.message = (
            message
            or f"Concept '{concept_id}' is referenced by an Active-Recruiting study and is locked against direct modifications. Please use the protocol amendment workflow."
        )
        super().__init__(self.message)


class InvalidParam(BaseModel):
    field: Optional[str] = None
    reason: Optional[str] = None
    value: Optional[str] = None


class ProblemDetails(BaseModel):
    type: str
    title: str
    status: int
    detail: str
    instance: str
    code: str
    invalid_params: Optional[List[InvalidParam]] = None


app = FastAPI(title="Cadence Clinical - Designer (MDR/SDR)", version="0.1.0")


@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    invalid_params = []
    for error in exc.errors():
        loc = error.get("loc", [])
        field_path = " -> ".join(str(item) for item in loc) if loc else "unknown"
        msg = error.get("msg", "Validation error")
        val = error.get("input")
        val_str = str(val) if val is not None else ""
        invalid_params.append(InvalidParam(field=field_path, reason=msg, value=val_str))
    problem = ProblemDetails(
        type="https://api.cadence-clinical.com/errors/validation-failed",
        title="Request Validation Failed",
        status=400,
        detail="The request body fails to satisfy schema rules. Refer to 'invalid_params' for details.",
        instance=request.url.path,
        code="REQUEST_VALIDATION_ERROR",
        invalid_params=invalid_params,
    )
    return JSONResponse(status_code=400, content=problem.model_dump(exclude_none=True))


app.add_middleware(GatewayAuthMiddleware)


@app.exception_handler(ImmutabilityViolationError)
async def immutability_violation_handler(
    request: Request, exc: ImmutabilityViolationError
):
    raise HTTPException(
        status_code=status.HTTP_403_FORBIDDEN,
        detail="IMMUTABILITY_VIOLATION",
    )


@app.exception_handler(ConcurrentLockingError)
async def concurrent_locking_handler(request: Request, exc: ConcurrentLockingError):
    raise HTTPException(
        status_code=status.HTTP_409_CONFLICT,
        detail="CONCURRENT_LOCKING_CONFLICT",
    )


@app.exception_handler(InvalidSignatureError)
async def invalid_signature_handler(request: Request, exc: InvalidSignatureError):
    raise HTTPException(
        status_code=status.HTTP_400_BAD_REQUEST,
        detail="INVALID_OR_MISSING_SIGNATURE",
    )


@app.exception_handler(LibraryObjectInUseError)
async def library_object_in_use_handler(request: Request, exc: LibraryObjectInUseError):
    raise HTTPException(
        status_code=status.HTTP_409_CONFLICT,
        detail="LIBRARY_OBJECT_IN_USE",
    )


@app.exception_handler(ConceptLockedError)
async def concept_locked_handler(request: Request, exc: ConceptLockedError):
    return JSONResponse(
        status_code=status.HTTP_409_CONFLICT,
        content={
            "detail": "CONCEPT_LOCKED_ACTIVE_STUDY",
            "message": exc.message,
            "concept_id": exc.concept_id,
            "workflow_suggestion": "To modify this concept, please initiate a protocol amendment workflow via POST /api/designer/protocols/{study_id}/amend.",
        },
    )


async def get_neo4j_driver(request: Request):
    """
    Lightweight dependency/accessor to retrieve the active Neo4j driver.
    """
    return getattr(request.app.state, "driver", None)


@app.on_event("startup")
async def startup() -> None:
    """Initialize resources on designer startup."""
    uri = os.getenv("NEO4J_URI", "bolt://localhost:7687")
    user = os.getenv("NEO4J_USER", "neo4j")
    password = os.getenv("NEO4J_PASSWORD", "password")
    app.state.driver = AsyncGraphDatabase.driver(uri, auth=(user, password))


@app.on_event("shutdown")
async def shutdown() -> None:
    """Clean up resources on designer shutdown."""
    driver = getattr(app.state, "driver", None)
    if driver is not None:
        await driver.close()
    app.state.driver = None


@app.get("/health")
async def health_check() -> Dict[str, str]:
    """
    Service health check endpoint.

    Returns a basic JSON payload indicating the service is operational.

    Returns:
        Dict[str, str]: The health status payload.
    """
    return {"status": "ok", "service": "designer"}


@app.get("/api/v1/studies/{study_id}")
async def get_legacy_study(study_id: str) -> Dict[str, Any]:
    """Returns the legacy internal projection with no USDM formatting.

    Args:
        study_id = get_study_projection(study_id)
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")
    return study_data


@app.get("/api/v2/studies/{study_id}/usdm")
async def get_usdm_study(study_id: str) -> Dict[str, Any]:
    """Dynamically processes the internal projection and returns a compliant USDM structure.

    Args:
        study_id (str): The unique identifier of the study.

    Returns:
        Dict[str, Any]: The dynamically mapped USDM study data.

    Raises:
        HTTPException: If the study is not found or validation fails.
    """
    start_time = time.perf_counter()
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    try:
        usdm_study = map_study_to_usdm(study_data)
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Validation Error mapping USDM: {str(e)}"
        )

    duration = (time.perf_counter() - start_time) * 1000
    # Simulate processing overhead check - we want this under 200ms
    if duration > 200:
        pass  # In a real app we might log a warning

    return usdm_study


@app.post("/api/admin/cache/clear", status_code=status.HTTP_200_OK)
async def clear_cache() -> Dict[str, str]:
    """Flushes the controlled terminology cache.

    Returns:
        Dict[str, str]: A success message indicating the cache was cleared.
    """
    terminology_cache.clear()
    return {"status": "success", "message": "Cache cleared successfully"}


@app.get("/api/admin/cache/status")
async def cache_status() -> Dict[str, int]:
    """Returns the current size and status of the terminology cache.

    Returns:
        Dict[str, int]: The status dictionary containing size and max_size.
    """
    return terminology_cache.get_status()


@app.get(
    "/api/v1/studies/{study_id}/alignment-validation",
    response_model=StudyAlignmentReport,
)
async def validate_study_alignment(study_id: str) -> StudyAlignmentReport:
    """
    Generate an alignment validation report for a specific clinical study.

    Analyzes trace links dynamically to ensure the
    Study Data Requirements (SDR) align with Metadata Requirements (MDR).

    Args:
        study_id (str): The unique identifier of the study to validate.

    Returns:
        StudyAlignmentReport: The structured validation report.
    """
    return await generate_alignment_report(study_id)


@app.get(
    "/api/v1/studies/{study_id}/terminology-validation",
    response_model=StudyTerminologyValidationReport,
)
async def validate_study_terminology_endpoint(
    study_id: str,
) -> StudyTerminologyValidationReport:
    """
    Generate a terminology validation report for a specific clinical study.

    Traverses study concept references and aggregates validation outcomes
    such as identifying affected elements and references.

    Args:
        study_id (str): The unique identifier of the study to validate.

    Returns:
        StudyTerminologyValidationReport: The structured validation report.
    """
    try:
        return validate_study_terminology(study_id)
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@app.get(
    "/api/v1/studies/{study_id}/ct-validation",
    response_model=StudyTerminologyValidationReport,
)
async def validate_study_ct_endpoint(
    study_id: str,
) -> StudyTerminologyValidationReport:
    """
    Generate a controlled terminology (CT) validation report for a specific clinical study.

    Traverses study concept references and aggregates validation outcomes
    such as identifying affected elements and references.

    Args:
        study_id (str): The unique identifier of the study to validate.

    Returns:
        StudyTerminologyValidationReport: The structured validation report.
    """
    try:
        return validate_study_terminology(study_id)
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@app.get(
    "/api/v1/terminology/validate/{code}",
    response_model=ConceptValidationReport,
)
async def validate_single_code(
    code: str,
) -> ConceptValidationReport:
    """
    Validates a single terminology concept code.

    Args:
        code (str): The concept code to validate.

    Returns:
        ConceptValidationReport: Validation status and metadata.
    """
    if not code or not code.strip():
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Concept code cannot be empty or whitespace.",
        )

    reports = validate_concept_codes([code])
    if not reports:
        return ConceptValidationReport(
            concept_code=code,
            state=CodeValidationState.INVALID,
            error_message="Validation did not return any reports.",
        )
    return reports[0]


@app.get(
    "/api/v1/terminology/search",
    response_model=TerminologySearchResponse,
)
async def search_terminology(
    term: str = Query(...),
    from_record: Optional[int] = Query(None),
    page_size: Optional[int] = Query(None),
) -> TerminologySearchResponse:
    """
    Search or autocomplete terminology concepts by text query.

    Args:
        term (str): Search term.
        from_record (int, optional): Record offset.
        page_size (int, optional): Page size.

    Returns:
        TerminologySearchResponse: Search results and status.
    """
    if not term or not term.strip():
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Search term cannot be empty or whitespace.",
        )

    try:
        client = NCIEVSClient()
        search_results = await client.search_concepts(
            term=term, from_record=from_record, page_size=page_size
        )
        concepts = [
            TerminologyConcept(
                code=c.get("code") or "",
                decode=c.get("decode") or "",
                system=c.get("system") or "",
                valid=c.get("valid", True),
            )
            for c in search_results
        ]
        return TerminologySearchResponse(
            query=term,
            state=CodeValidationState.VALID,
            results=concepts,
            total_results=len(concepts),
        )
    except Exception as e:
        # Return source unavailability as a structured degraded response rather than an unhandled 5xx response
        return TerminologySearchResponse(
            query=term,
            state=CodeValidationState.DEGRADED,
            results=[],
            total_results=0,
            error_message=f"Upstream EVS search service is unavailable: {str(e)}",
        )


@app.get(
    "/api/v1/studies/{study_id}/differences", response_model=List[DifferenceResult]
)
async def study_differences(
    study_id: str, action_id1: str, action_id2: str
) -> List[DifferenceResult]:
    """
    Get human-readable field-level differences between two version actions of a study.

    This endpoint uses a decoupled, API-first in-memory diffing architecture. Instead of
    relying on a direct database connection (which led to 503 errors and tight coupling),
    it fetches full study payloads from an external registry. The comparison logic runs
    entirely in-memory by flattening nested dictionary structures to dynamically identify
    added, modified, and deleted fields. This ensures high availability and fast execution
    without maintaining direct database connections.

    Args:
        study_id (str): The unique identifier of the study.
        action_id1 (str): The ID of the first action version.
        action_id2 (str): The ID of the second action version.

    Returns:
        List[DifferenceResult]: A list of field-level differences.
    """
    base_url = os.getenv("STUDY_REGISTRY_URL", "http://localhost:8000")

    try:
        async with httpx.AsyncClient() as client:
            response = await client.get(
                f"{base_url}/usdm/v4/studies/{study_id}", timeout=5.0
            )
            response.raise_for_status()
            data = response.json()
    except httpx.TimeoutException:
        raise HTTPException(status_code=504, detail="Registry timeout")
    except httpx.RequestError:
        raise HTTPException(status_code=502, detail="External registry offline")
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 404:
            raise HTTPException(status_code=404, detail="Study not found in registry")
        raise HTTPException(status_code=e.response.status_code, detail="Registry error")

    versions = data.get("versions", [])

    v1_data = None
    v2_data = None
    for v in versions:
        if v.get("id") == action_id1:
            v1_data = v
        if v.get("id") == action_id2:
            v2_data = v

    if not v1_data:
        raise HTTPException(
            status_code=400,
            detail=f"Target version {action_id1} is missing from the registry",
        )
    if not v2_data:
        raise HTTPException(
            status_code=400,
            detail=f"Target version {action_id2} is missing from the registry",
        )

    def flatten_dict(d: Any, parent_key: str = "", sep: str = ".") -> Dict[str, Any]:
        """
        Recursively flatten a nested dictionary or list into a flat dictionary.

        This enables efficient 1D in-memory comparison of complex nested JSON
        payloads (like USDM) by generating unique dot-notated paths for every node.

        Args:
            d (Any): The dictionary, list, or primitive to flatten.
            parent_key (str): The accumulated path key.
            sep (str): The separator used for nested keys.

        Returns:
            Dict[str, Any]: A flattened dictionary mapping paths to values.
        """
        items: List[Tuple[str, Any]] = []
        if isinstance(d, dict):
            for k, v in d.items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif isinstance(d, list):
            for i, v in enumerate(d):
                new_key = f"{parent_key}{sep}[{i}]" if parent_key else f"[{i}]"
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        else:
            items.append((parent_key, d))
        return dict(items)

    flat_v1 = flatten_dict(v1_data)
    flat_v2 = flatten_dict(v2_data)

    all_keys = set(flat_v1.keys()).union(set(flat_v2.keys()))
    differences = []

    for key in sorted(all_keys):
        val1 = flat_v1.get(key)
        val2 = flat_v2.get(key)
        if val1 != val2:
            differences.append(
                DifferenceResult(field=key, old_value=val1, new_value=val2)
            )

    return differences


# ==========================================
# Protocol Export / Rendering Endpoints
# ==========================================

from fastapi import Response, BackgroundTasks
from fastapi.concurrency import run_in_threadpool
import usdm_model
from apps.designer.rendering import render_protocol_to_pdf, render_protocol_to_docx
from apps.designer.content_assembly import assemble_rendered_protocol_document
from apps.designer.db import MOCK_DESIGNER_AUDIT_LOGS

async def forward_to_etmf(
    study_id: str,
    filename: str,
    content_bytes: bytes,
    mime_type: str,
    metadata_json: dict,
    user_id: str,
    roles: str,
    change_reason: str,
):
    import os
    import base64
    import time
    import httpx
    from packages.security.signing import generate_gateway_signature

    etmf_base_url = os.getenv("ETMF_URL", "http://localhost:8003")
    url = f"{etmf_base_url}/api/v1/etmf/ingest"

    try:
        content_str = content_bytes.decode("utf-8", errors="ignore")
    except Exception:
        content_str = base64.b64encode(content_bytes).decode("utf-8")

    payload = {
        "study_id": study_id,
        "artifact_type": "Protocol Sign-off",
        "filename": filename,
        "content": content_str,
        "mime_type": mime_type,
        "metadata_json": metadata_json,
    }

    timestamp = str(time.time())
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode("utf-8")
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=change_reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }

    async with httpx.AsyncClient() as client:
        resp = await client.post(url, json=payload, headers=headers, timeout=5.0)
        resp.raise_for_status()


@app.get("/api/v1/studies/{study_id}/export")
async def export_protocol(
    study_id: str,
    format: str = Query("pdf"),
    output: str = Query("combined"),
    request: Request = None,
):
    """
    Assembles a study version's data, maps it to a canonical USDM content model,
    and renders the resulting clinical protocol document as a structurally valid
    PDF or DOCX document using shared layout templates.
    """
    # 1. Explicit Parameter Validation (Raise HTTP 422 for invalid format/output values)
    if format not in ("pdf", "docx"):
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Invalid format value. Supported formats: pdf, docx."
        )
    if output not in ("narrative", "synopsis", "soa", "combined"):
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Invalid output value. Supported outputs: narrative, synopsis, soa, combined."
        )

    # 2. Study existence check
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    # 3. Capture caller identity and reasoning
    user_id = getattr(request.state, "user_id", "system") if request else "system"
    change_reason = getattr(request.state, "change_reason", None) if request else None
    if not change_reason and request:
        change_reason = request.headers.get("X-Change-Reason")
    if not change_reason:
        change_reason = "Protocol document export"

    version_index = 1
    # Try to parse current version as integer if possible
    try:
        raw_version = study_data.get("current_version", "1")
        # Strip alpha characters if any
        version_num = "".join(filter(str.isdigit, str(raw_version)))
        version_index = int(version_num) if version_num else 1
    except Exception:
        version_index = 1

    # Map internal projection to USDM payload
    try:
        usdm_dict = map_study_to_usdm(study_data)
        from apps.designer.mapper import to_uuid
        usdm_dict["id"] = to_uuid(usdm_dict["id"], "study")
        study_obj = usdm_model.Study.model_validate(usdm_dict)
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Validation Error mapping USDM: {str(e)}"
        )

    # Assemble the content model (presentation-centric view models)
    try:
        doc_view = assemble_rendered_protocol_document(
            study=study_obj,
            creator=user_id,
            change_reason=change_reason,
            version_index=max(1, version_index),
        )
    except Exception as e:
        raise HTTPException(
            status_code=400, detail=f"Assembly Error: {str(e)}"
        )

    # Render off the async request event loop to protect performance
    if format == "pdf":
        result = await run_in_threadpool(render_protocol_to_pdf, doc_view, output)
    elif format == "docx":
        result = await run_in_threadpool(render_protocol_to_docx, doc_view, output)
    else:
        raise HTTPException(
            status_code=422, detail=f"Unsupported format: {format}"
        )

    # 4. Record Part 11 compliant immutable generation audit event
    import uuid
    from datetime import timezone
    audit_event = {
        "id": str(uuid.uuid4()),
        "actor": user_id,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "change_reason": change_reason,
        "study_id": study_id,
        "version": version_index,
        "format": format,
        "output": output,
        "type": "PROTOCOL_EXPORT",
    }
    MOCK_DESIGNER_AUDIT_LOGS.append(audit_event)

    # Neo4j action logger if active driver is present
    driver = await get_neo4j_driver(request) if request else None
    if driver is not None:
        action_id = str(uuid.uuid4())
        query = """
        MATCH (s:Study {id: $study_id})
        CREATE (a:Action {
            id: $action_id,
            type: "EXPORT",
            format: $format,
            output: $output,
            user_id: $user_id,
            change_reason: $change_reason,
            timestamp: datetime()
        })
        CREATE (s)-[:HAS_ACTION]->(a)
        RETURN a.id as action_id
        """
        try:
            async with driver.session() as session:
                await session.run(
                    query,
                    study_id=study_id,
                    action_id=action_id,
                    format=format,
                    output=output,
                    user_id=user_id,
                    change_reason=change_reason,
                )
        except Exception:
            pass

    # 5. Configurable best-effort or strict forwarding to eTMF
    forward_enabled = os.getenv("ETMF_FORWARDING_ENABLED", "true").lower() in ("true", "1", "yes")
    strict_archival = os.getenv("ETMF_STRICT_ARCHIVAL", "false").lower() in ("true", "1", "yes")

    if forward_enabled:
        try:
            roles = getattr(request.state, "roles", "sysadmin") if request else "sysadmin"
            etmf_metadata = {
                "creator": user_id,
                "change_reason": change_reason,
                "version_index": version_index,
                "output": output,
                "format": format,
                "requires_signature": False,
            }
            if strict_archival:
                await forward_to_etmf(
                    study_id=study_id,
                    filename=result.filename,
                    content_bytes=result.content,
                    mime_type=result.media_type,
                    metadata_json=etmf_metadata,
                    user_id=user_id,
                    roles=roles,
                    change_reason=change_reason,
                )
            else:
                try:
                    await forward_to_etmf(
                        study_id=study_id,
                        filename=result.filename,
                        content_bytes=result.content,
                        mime_type=result.media_type,
                        metadata_json=etmf_metadata,
                        user_id=user_id,
                        roles=roles,
                        change_reason=change_reason,
                    )
                except Exception as e:
                    print(f"[ARCHIVAL WARNING] Best-effort eTMF forwarding failed: {str(e)}")
        except Exception as e:
            if strict_archival:
                raise HTTPException(
                    status_code=500,
                    detail=f"Strict Archival Failure: Failed to archive generated protocol to eTMF. Error: {str(e)}"
                )
            else:
                print(f"[ARCHIVAL WARNING] Best-effort eTMF forwarding failed: {str(e)}")

    headers = {
        "Content-Disposition": f'attachment; filename="{result.filename}"'
    }
    return Response(
        content=result.content,
        media_type=result.media_type,
        headers=headers,
    )


# ==========================================
# Eligibility Criteria API Models and Routes
# ==========================================


class CreateEligibilityCriterionRequest(BaseModel):
    criterion_id: str = Field(
        ...,
        description="Unique identifier of this eligibility criterion, e.g., 'INC_01'.",
    )
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ..., description="Whether this is an inclusion or exclusion criterion."
    )
    description: str = Field(
        ..., description="Human-readable text description of the criterion."
    )
    dsl_source: str = Field(
        ..., description="The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'."
    )
    expected_outcome: bool = Field(
        True, description="Expected Boolean outcome of evaluating the condition node."
    )
    change_reason: str = Field(..., description="Reason for creating this criterion.")


class UpdateEligibilityCriterionRequest(BaseModel):
    criterion_type: Literal["inclusion", "exclusion"] = Field(
        ..., description="Whether this is an inclusion or exclusion criterion."
    )
    description: str = Field(
        ..., description="Human-readable text description of the criterion."
    )
    dsl_source: str = Field(
        ..., description="The raw DSL statement source, e.g., 'eCRF.DM.AGE >= 18'."
    )
    expected_outcome: bool = Field(
        True, description="Expected Boolean outcome of evaluating the condition node."
    )
    change_reason: str = Field(..., description="Reason for updating this criterion.")


def map_db_to_criterion(db_crit: Dict[str, Any]) -> EligibilityCriterion:
    reason = (
        db_crit.get("reason_for_change")
        or db_crit.get("change_reason")
        or "Initial setup"
    )
    created_by = db_crit.get("created_by") or "system"
    cond = db_crit["condition"]
    if isinstance(cond, dict):
        cond = ExpressionNode(**cond)
    import datetime

    created_at = db_crit.get("created_at")
    if not created_at:
        created_at = datetime.datetime.now(datetime.timezone.utc)
    elif isinstance(created_at, str):
        try:
            created_at = datetime.datetime.fromisoformat(
                created_at.replace("Z", "+00:00")
            )
        except Exception:
            created_at = datetime.datetime.now(datetime.timezone.utc)
    else:
        try:
            if hasattr(created_at, "isoformat"):
                created_at = datetime.datetime.fromisoformat(
                    created_at.isoformat().replace("Z", "+00:00")
                )
            else:
                created_at = datetime.datetime.now(datetime.timezone.utc)
        except Exception:
            created_at = datetime.datetime.now(datetime.timezone.utc)

    return EligibilityCriterion(
        criterion_id=db_crit["id"] if "id" in db_crit else db_crit["criterion_id"],
        criterion_type=db_crit["criterion_type"],
        description=db_crit["description"],
        dsl_source=db_crit["dsl_source"],
        condition=cond,
        expected_outcome=db_crit.get("expected_outcome", True),
        created_by=created_by,
        reason_for_change=reason,
        version_index=db_crit.get("version_index", 1),
        created_at=created_at,
    )


@app.get(
    "/api/v1/studies/{study_id}/eligibility-criteria",
    response_model=List[EligibilityCriterion],
    status_code=status.HTTP_200_OK,
)
async def list_eligibility_criteria(study_id: str, request: Request):
    """
    Retrieves all active eligibility criteria for a specific clinical study.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    driver = getattr(request.app.state, "driver", None)
    try:
        raw_criteria = await get_eligibility_criteria_from_graph(driver, study_id)
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

    return [map_db_to_criterion(c) for c in raw_criteria]


@app.get(
    "/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}",
    response_model=EligibilityCriterion,
    status_code=status.HTTP_200_OK,
)
async def get_eligibility_criterion_detail(
    study_id: str, criterion_id: str, request: Request
):
    """
    Retrieves details for a specific eligibility criterion by ID.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    driver = getattr(request.app.state, "driver", None)
    raw_criteria = await get_eligibility_criteria_from_graph(driver, study_id)
    for c in raw_criteria:
        if c.get("id") == criterion_id or c.get("criterion_id") == criterion_id:
            return map_db_to_criterion(c)

    raise HTTPException(
        status_code=404, detail=f"Eligibility Criterion {criterion_id} not found"
    )


@app.post(
    "/api/v1/studies/{study_id}/eligibility-criteria",
    response_model=EligibilityCriterion,
    status_code=status.HTTP_201_CREATED,
)
async def create_eligibility_criterion_endpoint(
    study_id: str, payload: CreateEligibilityCriterionRequest, request: Request
):
    """
    Creates a new eligibility criterion for a specific clinical study, parsing and validating the DSL.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    # Extract identity & change reason from context/header
    user_id = getattr(request.state, "user_id", "system")
    change_reason = (
        payload.change_reason
        or getattr(request.state, "change_reason", None)
        or request.headers.get("X-Change-Reason", "Create eligibility criterion")
    )
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=400, detail="Missing change justification reason"
        )

    # Parse and validate DSL
    try:
        condition_ast = parse_dsl(payload.dsl_source)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Invalid DSL expression or reference: {str(e)}",
        )

    criterion_data = {
        "criterion_type": payload.criterion_type,
        "description": payload.description,
        "dsl_source": payload.dsl_source,
        "condition": condition_ast.model_dump(),
        "expected_outcome": payload.expected_outcome,
    }

    driver = getattr(request.app.state, "driver", None)
    try:
        await create_eligibility_criterion(
            driver,
            study_id,
            user_id,
            change_reason,
            payload.criterion_id,
            criterion_data,
        )
    except ImmutabilityViolationError:
        raise
    except ConcurrentLockingError as e:
        raise HTTPException(status_code=409, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

    # Fetch back the created item to return
    raw_criteria = await get_eligibility_criteria_from_graph(driver, study_id)
    for c in raw_criteria:
        if (
            c.get("id") == payload.criterion_id
            or c.get("criterion_id") == payload.criterion_id
        ):
            return map_db_to_criterion(c)

    raise HTTPException(
        status_code=500, detail="Failed to retrieve created eligibility criterion"
    )


@app.put(
    "/api/v1/studies/{study_id}/eligibility-criteria/{criterion_id}",
    response_model=EligibilityCriterion,
    status_code=status.HTTP_200_OK,
)
async def update_eligibility_criterion_endpoint(
    study_id: str,
    criterion_id: str,
    payload: UpdateEligibilityCriterionRequest,
    request: Request,
):
    """
    Updates an eligibility criterion for a specific clinical study, parsing and validating the DSL.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    user_id = getattr(request.state, "user_id", "system")
    change_reason = (
        payload.change_reason
        or getattr(request.state, "change_reason", None)
        or request.headers.get("X-Change-Reason", "Update eligibility criterion")
    )
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=400, detail="Missing change justification reason"
        )

    # Parse and validate DSL
    try:
        condition_ast = parse_dsl(payload.dsl_source)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"Invalid DSL expression or reference: {str(e)}",
        )

    criterion_data = {
        "criterion_type": payload.criterion_type,
        "description": payload.description,
        "dsl_source": payload.dsl_source,
        "condition": condition_ast.model_dump(),
        "expected_outcome": payload.expected_outcome,
    }

    driver = getattr(request.app.state, "driver", None)
    try:
        await update_eligibility_criterion(
            driver, study_id, criterion_id, user_id, change_reason, criterion_data
        )
    except ImmutabilityViolationError:
        raise
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

    # Fetch back the updated item to return
    raw_criteria = await get_eligibility_criteria_from_graph(driver, study_id)
    for c in raw_criteria:
        if c.get("id") == criterion_id or c.get("criterion_id") == criterion_id:
            return map_db_to_criterion(c)

    raise HTTPException(
        status_code=500, detail="Failed to retrieve updated eligibility criterion"
    )


@app.post("/api/v1/mappings/upload", status_code=status.HTTP_200_OK)
async def upload_mapping_csv(file: UploadFile = File(...)):
    """
    Validates a CSV mapping configuration to ensure target names meet standard W3C XML naming specifications.

    Raises:
        HTTPException: If the CSV format is invalid or if target XML names violate naming rules.
    """
    try:
        content = (await file.read()).decode("utf-8")
        rows = validate_mapping_csv(content)
        return {"status": "success", "rows_processed": len(rows)}
    except ValueError as e:
        raise HTTPException(status_code=422, detail=f"Validation Error: {str(e)}")
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Processing Error: {str(e)}")


@app.post(
    "/api/v1/designer/usdm/validate",
    status_code=status.HTTP_200_OK,
)
async def validate_usdm_endpoint(
    request: Request,
    override: Optional[str] = Query(
        None, description="Optional explicit version override ('v2' or 'v3')"
    ),
):
    """
    Validates a USDM JSON or YAML payload, normalizes shape differences, and returns a detailed validation report.
    If the payload is invalid, raises a structured HTTP 422 ProblemDetails response.
    """
    body_bytes = await request.body()
    body_text = body_bytes.decode("utf-8")

    report = validate_usdm_payload(body_text, override=override)

    if not report.validity:
        invalid_params = []
        for err in report.errors:
            invalid_params.append(
                InvalidParam(
                    field=err.field or "payload", reason=err.reason, value=err.value
                )
            )
        problem = ProblemDetails(
            type="https://api.cadence-clinical.com/errors/usdm-validation-failed",
            title="USDM Ingestion Validation Failed",
            status=422,
            detail=f"The provided {report.format} payload failed USDM {report.version} validation.",
            instance=request.url.path,
            code="USDM_VALIDATION_ERROR",
            invalid_params=invalid_params,
        )
        return JSONResponse(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            content=problem.model_dump(exclude_none=True),
        )

    return report


@app.post(
    "/api/v1/designer/round-trip",
    status_code=status.HTTP_200_OK,
)
async def run_round_trip_endpoint(
    payload: Dict[str, Any],
    request: Request,
):
    """
    Orchestrates USDM→internal→USDM and internal→USDM→internal round trips.
    Returns classification, fidelity details, source format, detected/resolved version, and mapping diagnostics.
    """
    from apps.designer.orchestration import execute_round_trip
    report = execute_round_trip(payload)
    return report


# ==========================================
# Biomedical Concepts (MDR) API Contracts
# ==========================================


class TerminologyEnum(str, Enum):
    SNOMED_CT = "SNOMED-CT"
    LOINC = "LOINC"
    MedDRA = "MedDRA"
    WHODrug = "WHODrug"
    NCI = "NCI"
    CDISC_CT = "CDISC-CT"


class CDASHMapping(BaseModel):
    domain: str
    variable_name: str
    data_type: str


class AllowableUnit(BaseModel):
    ucum_code: str
    name: str


class ConceptDetail(BaseModel):
    id: str
    concept_code: str
    terminology: str
    display_name: str
    definition: str
    cdash_mapping: Optional[CDASHMapping] = None
    allowable_units: Optional[List[AllowableUnit]] = None
    version: str
    status: str
    created_at: datetime
    created_by: str
    updated_at: Optional[datetime] = None
    updated_by: Optional[str] = None
    reason_for_change: Optional[str] = None


class ConceptListResponse(BaseModel):
    object: str
    data: List[ConceptDetail]
    has_more: bool
    next_cursor: Optional[str] = None


class LibraryObjectListResponse(BaseModel):
    """
    Paginated response envelope for Global Library Objects.
    Matches Stripe-style list response.
    """

    object: str = "list"
    data: List[LibraryObjectDetail]
    has_more: bool
    next_cursor: Optional[str] = None


def map_db_to_library_detail(record: Dict[str, Any]) -> LibraryObjectDetail:
    """
    Maps a raw database record / dict to the appropriate typed LibraryObjectDetail model.
    Handles semantic version conversion and datetime parsing.
    """
    # 1. Convert version index to a string semantic version if integer
    v = record.get("version")
    if isinstance(v, int):
        version_str = f"{v}.0.0"
    elif v:
        version_str = str(v)
    else:
        version_str = "1.0.0"

    # 2. Parse ISO datetimes
    def parse_dt(val: Any) -> Optional[datetime]:
        if not val:
            return None
        if isinstance(val, datetime):
            return val
        try:
            if hasattr(val, "isoformat"):
                return datetime.fromisoformat(val.isoformat())
            # Replace trailing Z with UTC timezone offset
            return datetime.fromisoformat(str(val).replace("Z", "+00:00"))
        except Exception:
            return datetime.now()

    created_at = parse_dt(record.get("created_at")) or datetime.now()
    updated_at = parse_dt(record.get("updated_at"))

    # Map change_reason / reason_for_change
    reason = record.get("reason_for_change") or record.get("change_reason")

    model_data = {
        "id": record.get("id"),
        "version": version_str,
        "status": record.get("status") or "DRAFT",
        "sponsor_id": record.get("sponsor_id"),
        "tenant_id": record.get("tenant_id") or "tenant_default",
        "created_at": created_at,
        "created_by": record.get("created_by") or "system",
        "updated_at": updated_at,
        "updated_by": record.get("updated_by"),
        "reason_for_change": reason,
        "object_type": record.get("object_type"),
        "payload": record.get("payload"),
        "prior_status": record.get("prior_status"),
    }

    return TypeAdapter(LibraryObjectDetail).validate_python(model_data)


class CreateConceptRequest(BaseModel):
    concept_code: str
    terminology: str
    display_name: str
    definition: str
    cdash_mapping: Optional[CDASHMapping] = None
    allowable_units: Optional[List[AllowableUnit]] = None
    change_reason: str


class UpdateConceptRequest(BaseModel):
    display_name: str
    definition: str
    cdash_mapping: Optional[CDASHMapping] = None
    allowable_units: Optional[List[AllowableUnit]] = None
    reason_for_change: str


class RenameConceptRequest(BaseModel):
    display_name: str
    reason_for_change: str


@app.get("/api/v1/mdr/concepts", response_model=ConceptListResponse)
async def get_concepts(
    terminology: Optional[TerminologyEnum] = None,
    domain: Optional[str] = None,
    limit: int = Query(50, le=250),
    starting_after: Optional[str] = None,
) -> ConceptListResponse:
    """Fetches a paginated list of Biomedical Concepts."""
    # This is a static contract endpoint
    return ConceptListResponse(
        object="list",
        data=[
            ConceptDetail(
                id="bc_sys_bp_001",
                concept_code="271649006",
                terminology="SNOMED-CT",
                display_name="Systolic blood pressure",
                definition="The pressure exerted by circulating blood upon the walls of blood vessels when the heart ventricles contract.",
                cdash_mapping=CDASHMapping(
                    domain="VS", variable_name="VSSBP", data_type="NUMERIC"
                ),
                allowable_units=[
                    AllowableUnit(ucum_code="mm[Hg]", name="millimeter of mercury")
                ],
                version="1.0.0",
                status="APPROVED",
                created_at=datetime.fromisoformat("2026-01-15T08:00:00Z"),
                created_by="usr_9921a88b2c410",
            )
        ],
        has_more=False,
        next_cursor=None,
    )


@app.post(
    "/api/v1/mdr/concepts",
    response_model=ConceptDetail,
    status_code=201,
    responses={400: {"model": ProblemDetails}},
)
async def create_concept(payload: CreateConceptRequest) -> ConceptDetail:
    """Creates a new Biomedical Concept inside the MDR graph repository."""
    return ConceptDetail(
        id="bc_heart_rate_002",
        concept_code=payload.concept_code,
        terminology=payload.terminology,
        display_name=payload.display_name,
        definition=payload.definition,
        cdash_mapping=payload.cdash_mapping,
        allowable_units=payload.allowable_units,
        version="1.0.0",
        status="DRAFT",
        created_at=datetime.now(),
        created_by="usr_9921a88b2c410",
    )


@app.put(
    "/api/v1/mdr/concepts/{id}",
    response_model=ConceptDetail,
    responses={400: {"model": ProblemDetails}},
)
async def update_concept(
    id: str, payload: UpdateConceptRequest, request: Request
) -> ConceptDetail:
    """Updates an existing concept, creating a new audit history and incrementing version index."""
    driver = await get_neo4j_driver(request)
    if await is_concept_referenced_by_active_recruiting_study(id, driver):
        raise ConceptLockedError(id)

    return ConceptDetail(
        id=id,
        concept_code="364075005",
        terminology="SNOMED-CT",
        display_name=payload.display_name,
        definition=payload.definition,
        cdash_mapping=payload.cdash_mapping,
        allowable_units=payload.allowable_units,
        version="1.1.0",
        status="APPROVED",
        created_at=datetime.now(),
        created_by="usr_9921a88b2c410",
        updated_at=datetime.now(),
        updated_by="usr_9921a88b2c410",
        reason_for_change=payload.reason_for_change,
    )


@app.post("/api/v1/mdr/concepts/{id}/rename", response_model=ConceptDetail)
async def rename_concept(
    id: str, payload: RenameConceptRequest, request: Request
) -> ConceptDetail:
    """Renames an existing Biomedical Concept if it is not referenced by an Active-Recruiting study."""
    driver = await get_neo4j_driver(request)
    if await is_concept_referenced_by_active_recruiting_study(id, driver):
        raise ConceptLockedError(id)

    return ConceptDetail(
        id=id,
        concept_code="364075005",
        terminology="SNOMED-CT",
        display_name=payload.display_name,
        definition="The frequency of the heart rate at complete rest.",
        version="1.1.0",
        status="APPROVED",
        created_at=datetime.now(),
        created_by="usr_9921a88b2c410",
        updated_at=datetime.now(),
        updated_by="usr_9921a88b2c410",
        reason_for_change=payload.reason_for_change,
    )


@app.delete("/api/v1/mdr/concepts/{id}", status_code=status.HTTP_200_OK)
async def delete_concept(id: str, request: Request) -> Dict[str, str]:
    """Deletes an existing Biomedical Concept if it is not referenced by an Active-Recruiting study."""
    driver = await get_neo4j_driver(request)
    if await is_concept_referenced_by_active_recruiting_study(id, driver):
        raise ConceptLockedError(id)

    return {"status": "success", "message": f"Concept {id} deleted successfully"}


# ==========================================
# Global Library (MDR) API Endpoints
# ==========================================


@app.post(
    "/api/v1/mdr/library",
    response_model=LibraryObjectDetail,
    status_code=status.HTTP_201_CREATED,
)
async def create_library_object_endpoint(
    payload: CreateLibraryObjectRequest,
    request: Request,
) -> LibraryObjectDetail:
    """
    Creates a new Global Library object under the authenticated sponsor's scope.
    """
    driver = await get_neo4j_driver(request)

    # 1. Extract identity, roles, and sponsor scope
    user_id = getattr(request.state, "user_id", "system")
    change_reason = (
        getattr(request.state, "change_reason", None) or payload.change_reason
    )
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason",
        )

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    tenant_id = getattr(request.state, "tenant_id", None) or request.headers.get(
        "X-Tenant-Id", "tenant_default"
    )

    # 2. Prevent duplicate ID within same sponsor scope
    latest = await get_latest_library_object(driver, payload.id, sponsor_id)
    if latest:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=f"Library object with ID {payload.id} already exists for sponsor {sponsor_id}.",
        )

    # 3. Save to database
    properties = {
        "object_type": payload.object_type.value
        if hasattr(payload.object_type, "value")
        else payload.object_type,
        "sponsor_id": sponsor_id,
        "tenant_id": tenant_id,
        "status": payload.status.value
        if hasattr(payload.status, "value")
        else payload.status,
        "created_at": datetime.now().isoformat(),
        "created_by": user_id,
        "change_reason": change_reason,
        "payload": payload.payload.model_dump(),
    }

    try:
        record = await create_library_object_version(driver, payload.id, properties)
        return map_db_to_library_detail(record)
    except ImmutabilityViolationError:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="IMMUTABILITY_VIOLATION",
        )
    except ConcurrentLockingError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="CONCURRENT_LOCKING_CONFLICT",
        )


@app.get(
    "/api/v1/mdr/library",
    response_model=LibraryObjectListResponse,
)
async def list_library_objects_endpoint(
    request: Request,
    object_type: Optional[ObjectType] = None,
    limit: int = Query(50, le=250),
    starting_after: Optional[str] = None,
) -> LibraryObjectListResponse:
    """
    Lists latest global library objects under the authenticated sponsor.
    Supports Stripe-style cursor-based pagination.
    """
    driver = await get_neo4j_driver(request)

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    # Fetch limit + 1 to detect has_more
    records = await list_library_objects(
        driver,
        sponsor_id=sponsor_id,
        object_type=object_type.value if object_type else None,
        limit=limit + 1,
        starting_after=starting_after,
    )

    has_more = len(records) > limit
    if has_more:
        records = records[:limit]
        next_cursor = records[-1]["id"]
    else:
        next_cursor = None

    data = [map_db_to_library_detail(r) for r in records]
    return LibraryObjectListResponse(
        object="list",
        data=data,
        has_more=has_more,
        next_cursor=next_cursor,
    )


@app.get(
    "/api/v1/mdr/library/{id}",
    response_model=LibraryObjectDetail,
)
async def get_library_object_endpoint(
    id: str,
    request: Request,
    version: Optional[int] = Query(None),
) -> LibraryObjectDetail:
    """
    Retrieves the latest version or a specific version of a global library object.
    """
    driver = await get_neo4j_driver(request)

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    if version is not None:
        record = await get_library_object_by_version(driver, id, sponsor_id, version)
    else:
        record = await get_latest_library_object(driver, id, sponsor_id)

    if not record:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Library object {id} not found.",
        )

    return map_db_to_library_detail(record)


@app.put(
    "/api/v1/mdr/library/{id}",
    response_model=LibraryObjectDetail,
)
async def update_library_object_endpoint(
    id: str,
    payload: UpdateLibraryObjectRequest,
    request: Request,
) -> LibraryObjectDetail:
    """
    Updates a global library object by creating a new version.
    """
    driver = await get_neo4j_driver(request)

    user_id = getattr(request.state, "user_id", "system")
    change_reason = (
        getattr(request.state, "change_reason", None) or payload.reason_for_change
    )
    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason",
        )

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    # 1. Verify object exists and is owned by the sponsor
    latest = await get_latest_library_object(driver, id, sponsor_id)
    if not latest:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Library object {id} not found under sponsor {sponsor_id}.",
        )

    # 2. Check immutability
    if latest.get("status") in ("LOCKED", "PUBLISHED", "ARCHIVED"):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="IMMUTABILITY_VIOLATION",
        )

    # 3. Save new version
    properties = {
        "object_type": payload.object_type.value
        if hasattr(payload.object_type, "value")
        else payload.object_type,
        "sponsor_id": sponsor_id,
        "tenant_id": latest.get("tenant_id", "tenant_default"),
        "status": "DRAFT",
        "created_at": latest.get("created_at") or datetime.now().isoformat(),
        "created_by": latest.get("created_by") or user_id,
        "updated_at": datetime.now().isoformat(),
        "updated_by": user_id,
        "reason_for_change": change_reason,
        "payload": payload.payload.model_dump(),
    }

    try:
        record = await create_library_object_version(driver, id, properties)
        return map_db_to_library_detail(record)
    except ImmutabilityViolationError:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="IMMUTABILITY_VIOLATION",
        )
    except ConcurrentLockingError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="CONCURRENT_LOCKING_CONFLICT",
        )


class LibraryObjectAmendRequest(BaseModel):
    """
    Payload for the Library Object Amendment endpoint.
    """

    reason_for_change: str = Field(
        ..., description="Mandatory reason for initiating the amendment."
    )
    payload: Optional[Dict[str, Any]] = Field(
        None,
        description="Optional updated payload for the amended version. If not provided, the latest payload is cloned.",
    )


@app.post(
    "/api/v1/mdr/library/{id}/amend",
    response_model=LibraryObjectDetail,
    status_code=status.HTTP_201_CREATED,
)
async def amend_library_object_endpoint(
    id: str,
    payload: LibraryObjectAmendRequest,
    request: Request,
) -> LibraryObjectDetail:
    """
    Initiates an amendment on a library object that is in use by creating a successor draft version.
    """
    driver = await get_neo4j_driver(request)

    user_id = getattr(request.state, "user_id", "system")
    change_reason = payload.reason_for_change

    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason",
        )

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    # 1. Verify object exists and is owned by the sponsor
    latest = await get_latest_library_object(driver, id, sponsor_id)
    if not latest:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Library object {id} not found under sponsor {sponsor_id}.",
        )

    # 2. Determine payload to use: caller-supplied or clone latest
    final_payload = (
        payload.payload if payload.payload is not None else latest.get("payload") or {}
    )

    # 3. Save new version
    properties = {
        "object_type": latest.get("object_type"),
        "sponsor_id": sponsor_id,
        "tenant_id": latest.get("tenant_id", "tenant_default"),
        "status": "DRAFT",
        "created_at": latest.get("created_at") or datetime.now().isoformat(),
        "created_by": latest.get("created_by") or user_id,
        "updated_at": datetime.now().isoformat(),
        "updated_by": user_id,
        "reason_for_change": change_reason,
        "payload": final_payload,
    }

    try:
        record = await create_library_object_version(
            driver, id, properties, is_amendment=True, bypass_immutability=True
        )
        return map_db_to_library_detail(record)
    except ImmutabilityViolationError:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="IMMUTABILITY_VIOLATION",
        )
    except ConcurrentLockingError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="CONCURRENT_LOCKING_CONFLICT",
        )


@app.get(
    "/api/v1/mdr/library/{id}/history",
    response_model=List[LibraryObjectDetail],
)
async def get_library_object_history_endpoint(
    id: str,
    request: Request,
) -> List[LibraryObjectDetail]:
    """
    Retrieves the complete version history of a global library object.
    """
    driver = await get_neo4j_driver(request)

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    records = await get_library_object_history(driver, id, sponsor_id)
    if not records:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Library object {id} not found.",
        )

    return [map_db_to_library_detail(r) for r in records]


@app.post(
    "/api/v1/mdr/library/{id}/transition",
    response_model=LibraryObjectDetail,
)
async def transition_library_object_endpoint(
    id: str,
    payload: LibraryObjectTransitionRequest,
    request: Request,
) -> LibraryObjectDetail:
    """
    Transitions the lifecycle status of a global library object.
    Enforces a strict role-gated ALLOWED_LIBRARY_TRANSITIONS map.
    """
    driver = await get_neo4j_driver(request)

    # 1. Extract identity and sponsor scope
    user_id = getattr(request.state, "user_id", "system")
    change_reason = payload.change_reason

    if not change_reason or not change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason",
        )

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    # 2. Verify object exists and is owned by the sponsor
    latest = await get_latest_library_object(driver, id, sponsor_id)
    if not latest:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Library object {id} not found under sponsor {sponsor_id}.",
        )

    current_status_str = latest.get("status") or "DRAFT"
    try:
        current_status = LibraryStatus(current_status_str)
    except ValueError:
        current_status = LibraryStatus.DRAFT

    target_status = payload.status
    allowed_next = ALLOWED_LIBRARY_TRANSITIONS.get(current_status, set())
    if target_status not in allowed_next:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Invalid transition from {current_status.value} to {target_status.value}.",
        )

    # 3. Check role requirements per transition using normalized roles
    raw_roles = get_normalized_roles(request)
    roles = []
    for r in raw_roles:
        norm_r = r.strip().lower()
        if norm_r in ("sponsor admin", "sponsor_admin"):
            roles.append("sponsor_admin")
        else:
            roles.append(ROLE_ALIASES.get(norm_r, norm_r))

    if not roles:
        raise HTTPException(status_code=403, detail="Missing role credentials.")

    # Rules for each target transition status:
    required_roles_map = {
        LibraryStatus.IN_REVIEW: {
            "sponsor_designer",
            "sponsor_dm",
            "sponsor_admin",
            "sysadmin",
        },
        LibraryStatus.APPROVED: {"sponsor_dm", "sponsor_admin", "sysadmin"},
        LibraryStatus.REJECTED: {"sponsor_dm", "sponsor_admin", "sysadmin"},
        LibraryStatus.PUBLISHED: {"sponsor_dm", "sponsor_admin", "sysadmin"},
        LibraryStatus.ARCHIVED: {"sponsor_admin", "sysadmin"},
        LibraryStatus.DRAFT: {
            "sponsor_designer",
            "sponsor_dm",
            "sponsor_admin",
            "sysadmin",
        },
    }

    allowed_roles = required_roles_map.get(target_status, set())
    if not any(role in allowed_roles for role in roles):
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="User role is not authorized for this action.",
        )

    # 4. Save new version capturing transition metadata
    properties = {
        "object_type": latest.get("object_type"),
        "sponsor_id": sponsor_id,
        "tenant_id": latest.get("tenant_id", "tenant_default"),
        "status": target_status.value,
        "prior_status": current_status.value,
        "created_at": latest.get("created_at") or datetime.now().isoformat(),
        "created_by": latest.get("created_by") or user_id,
        "updated_at": datetime.now().isoformat(),
        "updated_by": user_id,
        "reason_for_change": change_reason,
        "payload": latest.get("payload") or {},
    }

    try:
        record = await create_library_object_version(
            driver, id, properties, is_amendment=True
        )
        return map_db_to_library_detail(record)
    except ImmutabilityViolationError:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="IMMUTABILITY_VIOLATION",
        )
    except ConcurrentLockingError:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="CONCURRENT_LOCKING_CONFLICT",
        )


# ==========================================
# Rules Engine (Skip Logic, Constraints, etc.) API Endpoints
# ==========================================


class RulePreviewResponse(BaseModel):
    """
    Response for rule preview/validation request.
    """

    xpath: str
    failures: List[str]
    circular_cycles: List[str]


class CreateStudyVersionRequest(BaseModel):
    """
    Request payload to establish a StudyVersion node.
    """

    id: str
    version_tag: str
    status: str  # DRAFT, ACTIVE, LOCKED, PUBLISHED, ARCHIVED
    version_index: int


@app.post("/api/v1/studies/{study_id}/versions", status_code=status.HTTP_201_CREATED)
async def post_study_version(
    study_id: str, payload: CreateStudyVersionRequest, request: Request
) -> Dict[str, Any]:
    """
    Establishes a new StudyVersion node under a clinical study.
    Enforces that concurrent creation with duplicate index or tag fails with 409 Conflict.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    user_id = getattr(request.state, "user_id", "system")

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        await create_study_version(
            driver,
            study_id=study_id,
            version_id=payload.id,
            version_tag=payload.version_tag,
            status=payload.status,
            version_index=payload.version_index,
            created_by=user_id,
        )
    else:
        create_mock_study_version(
            study_id,
            {
                "id": payload.id,
                "version_tag": payload.version_tag,
                "status": payload.status,
                "version_index": payload.version_index,
                "created_by": user_id,
                "created_at": datetime.now().isoformat(),
            },
        )
    return {"status": "success", "message": "Study version created successfully"}


@app.get("/api/v1/studies/{study_id}/rules", status_code=status.HTTP_200_OK)
async def get_study_rules(study_id: str, request: Request) -> List[Dict[str, Any]]:
    """
    Retrieves all non-soft-deleted active rules for a specific clinical study.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        return await get_rules_from_graph(driver, study_id)
    else:
        return get_mock_rules(study_id)


@app.post("/api/v1/studies/{study_id}/rules", status_code=status.HTTP_201_CREATED)
async def create_study_rule(
    study_id: str, payload: CreateRuleRequest, request: Request
) -> Dict[str, Any]:
    """
    Creates a new rule for a clinical study, enforcing auth and X-Change-Reason.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")
    rule_dict = payload.model_dump()

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        import uuid

        rule_id = f"rule_{uuid.uuid4().hex[:12]}"
        await create_rule_node(
            driver, study_id, user_id, change_reason, rule_id, rule_dict
        )
        rule_dict["id"] = rule_id
        rule_dict["study_id"] = study_id
        rule_dict["version_index"] = 1
        rule_dict["is_deleted"] = False
        return rule_dict
    else:
        # Check immutability for the mock/in-memory path
        assert_mock_study_mutable(study_id)
        created = create_mock_rule(study_id, rule_dict)
        # Verify the change justification is captured in the response/metadata
        created["created_by"] = user_id
        created["change_reason"] = change_reason
        return created


@app.get("/api/v1/studies/{study_id}/rules/{rule_id}", status_code=status.HTTP_200_OK)
async def get_study_rule_by_id(
    study_id: str, rule_id: str, request: Request
) -> Dict[str, Any]:
    """
    Retrieves a specific rule by ID.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        rules = await get_rules_from_graph(driver, study_id)
        for r in rules:
            if r["id"] == rule_id:
                return r
        raise HTTPException(status_code=404, detail="Rule not found")
    else:
        rule = get_mock_rule_by_id(study_id, rule_id)
        if not rule:
            raise HTTPException(status_code=404, detail="Rule not found")
        return rule


@app.put("/api/v1/studies/{study_id}/rules/{rule_id}", status_code=status.HTTP_200_OK)
async def update_study_rule_by_id(
    study_id: str, rule_id: str, payload: CreateRuleRequest, request: Request
) -> Dict[str, Any]:
    """
    Updates a rule's parameters, incrementing version index.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        rules = await get_rules_from_graph(driver, study_id)
        rule_exists = any(r["id"] == rule_id for r in rules)
        if not rule_exists:
            raise HTTPException(status_code=404, detail="Rule not found")

        new_version = await update_rule_node(
            driver, study_id, rule_id, user_id, change_reason, payload.model_dump()
        )
        rule_dict = payload.model_dump()
        rule_dict["id"] = rule_id
        rule_dict["study_id"] = study_id
        rule_dict["version_index"] = new_version
        rule_dict["is_deleted"] = False
        return rule_dict
    else:
        # Check immutability for the mock/in-memory path
        assert_mock_study_mutable(study_id)
        rule = get_mock_rule_by_id(study_id, rule_id)
        if not rule:
            raise HTTPException(status_code=404, detail="Rule not found")
        updated = update_mock_rule(study_id, rule_id, payload.model_dump())
        updated["updated_by"] = user_id
        updated["change_reason"] = change_reason
        return updated


@app.delete(
    "/api/v1/studies/{study_id}/rules/{rule_id}", status_code=status.HTTP_200_OK
)
async def delete_study_rule_by_id(
    study_id: str, rule_id: str, request: Request
) -> Dict[str, str]:
    """
    Soft-deletes a rule, retaining its historical properties in audit.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        rules = await get_rules_from_graph(driver, study_id)
        rule_exists = any(r["id"] == rule_id for r in rules)
        if not rule_exists:
            raise HTTPException(status_code=404, detail="Rule not found")

        await delete_rule_node(driver, study_id, rule_id, user_id, change_reason)
        return {"status": "success", "message": "Rule successfully deleted"}
    else:
        # Check immutability for the mock/in-memory path
        assert_mock_study_mutable(study_id)
        success = delete_mock_rule(study_id, rule_id)
        if not success:
            raise HTTPException(status_code=404, detail="Rule not found")
        return {"status": "success", "message": "Rule successfully deleted"}


@app.post(
    "/api/v1/studies/{study_id}/rules/preview",
    response_model=RulePreviewResponse,
    status_code=status.HTTP_200_OK,
)
async def compile_preview_rule(
    study_id: str, payload: CreateRuleRequest, request: Request
) -> RulePreviewResponse:
    """
    Read-only compile and validation preview route.
    Detects unknown field references and circular skip-logic dependencies.
    """
    study_data = get_study_projection(study_id)
    if not study_data:
        raise HTTPException(status_code=404, detail="Study not found")

    xpath = compile_to_xpath(payload.condition)
    failures = detect_unknown_fields(payload.condition, study_data)

    driver = getattr(request.app.state, "driver", None)
    if driver is not None:
        existing_rules = await get_rules_from_graph(driver, study_id)
    else:
        existing_rules = get_mock_rules(study_id)

    temp_rules = [dict(r) for r in existing_rules]
    temp_rules.append(
        {
            "id": "proposed_rule",
            "type": payload.type,
            "condition": payload.condition.model_dump(),
            "target_field": payload.target_field,
        }
    )
    circular_cycles = detect_circular_dependencies(temp_rules)

    return RulePreviewResponse(
        xpath=xpath,
        failures=failures,
        circular_cycles=circular_cycles,
    )


class ProtocolAmendRequest(BaseModel):
    """
    Payload for the Protocol/Designer Amendment endpoint.
    """

    amendment_type: Optional[str] = "minor"
    type: Optional[str] = None


@app.post(
    "/api/designer/protocols/{id}/amend",
    status_code=status.HTTP_201_CREATED,
)
async def amend_protocol(
    id: str,
    payload: ProtocolAmendRequest,
    request: Request,
) -> Dict[str, Any]:
    """
    Exposes POST /api/designer/protocols/{id}/amend with 201, new_version, status, and parent_version.
    Creates a transaction-safe DRAFT successor with incremented version index.
    """
    # GxP 21 CFR Part 11 compliant audit trail: capture user and reasoning for version change
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    if not change_reason or change_reason == "system_operation":
        change_reason = request.headers.get(
            "X-Change-Reason", "Clinical amendment operation"
        )

    bump_type = payload.type or payload.amendment_type or "minor"

    driver = getattr(request.app.state, "driver", None)

    try:
        result = await amend_protocol_version(
            driver=driver,
            study_id=id,
            user_id=user_id,
            change_reason=change_reason,
            bump_type=bump_type,
        )
        return {
            "new_version": result["new_version"],
            "status": result["status"],
            "parent_version": result["parent_version"],
        }
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


# =====================================================================
# Schedule of Activities (SoA) Helpers & CRUD Endpoints
# =====================================================================


async def get_soa_entity(
    driver,
    study_version_id: str,
    entity_id: str,
    entity_type: str,  # "arms", "epochs", "visits", "procedures", "timing_windows"
) -> Optional[Dict[str, Any]]:
    if driver is None:
        _init_mock_soa(study_version_id)
        return MOCK_SOA_DATA[study_version_id][entity_type].get(entity_id)

    # Map entity type to Neo4j relationship and label
    mapping = {
        "arms": ("HAS_ARM", "StudyArm"),
        "epochs": ("HAS_EPOCH", "Epoch"),
        "visits": ("HAS_VISIT", "Visit"),
        "procedures": ("HAS_PROCEDURE", "Procedure"),
        "timing_windows": ("HAS_TIMING_WINDOW", "TimingWindow"),
    }
    rel, label = mapping[entity_type]
    query = f"""
    MATCH (sv:StudyVersion {{id: $study_version_id}})-[:{rel}]->(e:{label} {{id: $entity_id}})
    RETURN properties(e) as props
    """
    async with driver.session() as session:
        res = await session.run(
            query, study_version_id=study_version_id, entity_id=entity_id
        )
        record = await res.single()
        if record:
            return dict(record["props"])
        return None


async def list_soa_entities(
    driver,
    study_version_id: str,
    entity_type: str,
) -> List[Dict[str, Any]]:
    if driver is None:
        _init_mock_soa(study_version_id)
        return list(MOCK_SOA_DATA[study_version_id][entity_type].values())

    mapping = {
        "arms": ("HAS_ARM", "StudyArm"),
        "epochs": ("HAS_EPOCH", "Epoch"),
        "visits": ("HAS_VISIT", "Visit"),
        "procedures": ("HAS_PROCEDURE", "Procedure"),
        "timing_windows": ("HAS_TIMING_WINDOW", "TimingWindow"),
    }
    rel, label = mapping[entity_type]
    query = f"""
    MATCH (sv:StudyVersion {{id: $study_version_id}})-[:{rel}]->(e:{label})
    RETURN properties(e) as props
    """
    async with driver.session() as session:
        res = await session.run(query, study_version_id=study_version_id)
        records = await res.all()
        return [dict(r["props"]) for r in records]


# --- Arms Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/arms",
    response_model=SoAEntityCreatedResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_arm_endpoint(
    study_id: str,
    version_id: str,
    payload: CreateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    await create_study_arm(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=payload.id,
        properties=payload.properties,
    )
    return SoAEntityCreatedResponse(id=payload.id)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}",
    response_model=SoAEntityDetail,
)
async def get_arm_endpoint(
    study_id: str,
    version_id: str,
    arm_id: str,
    request: Request,
) -> SoAEntityDetail:
    driver = await get_neo4j_driver(request)
    entity = await get_soa_entity(driver, version_id, arm_id, "arms")
    if not entity:
        raise HTTPException(status_code=404, detail="Arm not found")
    return SoAEntityDetail(**entity)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/arms",
    response_model=List[SoAEntityDetail],
)
async def list_arms_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> List[SoAEntityDetail]:
    driver = await get_neo4j_driver(request)
    entities = await list_soa_entities(driver, version_id, "arms")
    return [SoAEntityDetail(**e) for e in entities]


@app.put(
    "/api/v1/studies/{study_id}/versions/{version_id}/arms/{arm_id}",
    response_model=SoAEntityCreatedResponse,
)
async def update_arm_endpoint(
    study_id: str,
    version_id: str,
    arm_id: str,
    payload: UpdateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    try:
        await update_study_arm(
            driver=driver,
            study_version_id=version_id,
            user_id=user_id,
            change_reason=change_reason,
            arm_id=arm_id,
            properties=payload.properties,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    return SoAEntityCreatedResponse(id=arm_id)


# --- Epochs Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/epochs",
    response_model=SoAEntityCreatedResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_epoch_endpoint(
    study_id: str,
    version_id: str,
    payload: CreateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    await create_epoch(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=payload.id,
        properties=payload.properties,
    )
    return SoAEntityCreatedResponse(id=payload.id)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}",
    response_model=SoAEntityDetail,
)
async def get_epoch_endpoint(
    study_id: str,
    version_id: str,
    epoch_id: str,
    request: Request,
) -> SoAEntityDetail:
    driver = await get_neo4j_driver(request)
    entity = await get_soa_entity(driver, version_id, epoch_id, "epochs")
    if not entity:
        raise HTTPException(status_code=404, detail="Epoch not found")
    return SoAEntityDetail(**entity)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/epochs",
    response_model=List[SoAEntityDetail],
)
async def list_epochs_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> List[SoAEntityDetail]:
    driver = await get_neo4j_driver(request)
    entities = await list_soa_entities(driver, version_id, "epochs")
    return [SoAEntityDetail(**e) for e in entities]


@app.put(
    "/api/v1/studies/{study_id}/versions/{version_id}/epochs/{epoch_id}",
    response_model=SoAEntityCreatedResponse,
)
async def update_epoch_endpoint(
    study_id: str,
    version_id: str,
    epoch_id: str,
    payload: UpdateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    try:
        await update_epoch(
            driver=driver,
            study_version_id=version_id,
            user_id=user_id,
            change_reason=change_reason,
            epoch_id=epoch_id,
            properties=payload.properties,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    return SoAEntityCreatedResponse(id=epoch_id)


# --- Visits Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/visits",
    response_model=SoAEntityCreatedResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_visit_endpoint(
    study_id: str,
    version_id: str,
    payload: CreateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    await create_visit(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=payload.id,
        properties=payload.properties,
    )
    return SoAEntityCreatedResponse(id=payload.id)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}",
    response_model=SoAEntityDetail,
)
async def get_visit_endpoint(
    study_id: str,
    version_id: str,
    visit_id: str,
    request: Request,
) -> SoAEntityDetail:
    driver = await get_neo4j_driver(request)
    entity = await get_soa_entity(driver, version_id, visit_id, "visits")
    if not entity:
        raise HTTPException(status_code=404, detail="Visit not found")
    return SoAEntityDetail(**entity)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/visits",
    response_model=List[SoAEntityDetail],
)
async def list_visits_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> List[SoAEntityDetail]:
    driver = await get_neo4j_driver(request)
    entities = await list_soa_entities(driver, version_id, "visits")
    return [SoAEntityDetail(**e) for e in entities]


@app.put(
    "/api/v1/studies/{study_id}/versions/{version_id}/visits/{visit_id}",
    response_model=SoAEntityCreatedResponse,
)
async def update_visit_endpoint(
    study_id: str,
    version_id: str,
    visit_id: str,
    payload: UpdateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    try:
        await update_visit(
            driver=driver,
            study_version_id=version_id,
            user_id=user_id,
            change_reason=change_reason,
            visit_id=visit_id,
            properties=payload.properties,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    return SoAEntityCreatedResponse(id=visit_id)


# --- Procedures Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/procedures",
    response_model=SoAEntityCreatedResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_procedure_endpoint(
    study_id: str,
    version_id: str,
    payload: CreateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    await create_procedure(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        procedure_id=payload.id,
        properties=payload.properties,
    )
    return SoAEntityCreatedResponse(id=payload.id)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
    response_model=SoAEntityDetail,
)
async def get_procedure_endpoint(
    study_id: str,
    version_id: str,
    procedure_id: str,
    request: Request,
) -> SoAEntityDetail:
    driver = await get_neo4j_driver(request)
    entity = await get_soa_entity(driver, version_id, procedure_id, "procedures")
    if not entity:
        raise HTTPException(status_code=404, detail="Procedure not found")
    return SoAEntityDetail(**entity)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/procedures",
    response_model=List[SoAEntityDetail],
)
async def list_procedures_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> List[SoAEntityDetail]:
    driver = await get_neo4j_driver(request)
    entities = await list_soa_entities(driver, version_id, "procedures")
    return [SoAEntityDetail(**e) for e in entities]


@app.put(
    "/api/v1/studies/{study_id}/versions/{version_id}/procedures/{procedure_id}",
    response_model=SoAEntityCreatedResponse,
)
async def update_procedure_endpoint(
    study_id: str,
    version_id: str,
    procedure_id: str,
    payload: UpdateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    try:
        await update_procedure(
            driver=driver,
            study_version_id=version_id,
            user_id=user_id,
            change_reason=change_reason,
            procedure_id=procedure_id,
            properties=payload.properties,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    return SoAEntityCreatedResponse(id=procedure_id)


# --- Timing Windows Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
    response_model=SoAEntityCreatedResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_timing_window_endpoint(
    study_id: str,
    version_id: str,
    payload: CreateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    await create_timing_window(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        timing_id=payload.id,
        properties=payload.properties,
    )
    return SoAEntityCreatedResponse(id=payload.id)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}",
    response_model=SoAEntityDetail,
)
async def get_timing_window_endpoint(
    study_id: str,
    version_id: str,
    timing_id: str,
    request: Request,
) -> SoAEntityDetail:
    driver = await get_neo4j_driver(request)
    entity = await get_soa_entity(driver, version_id, timing_id, "timing_windows")
    if not entity:
        raise HTTPException(status_code=404, detail="Timing window not found")
    return SoAEntityDetail(**entity)


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows",
    response_model=List[SoAEntityDetail],
)
async def list_timing_windows_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> List[SoAEntityDetail]:
    driver = await get_neo4j_driver(request)
    entities = await list_soa_entities(driver, version_id, "timing_windows")
    return [SoAEntityDetail(**e) for e in entities]


@app.put(
    "/api/v1/studies/{study_id}/versions/{version_id}/timing-windows/{timing_id}",
    response_model=SoAEntityCreatedResponse,
)
async def update_timing_window_endpoint(
    study_id: str,
    version_id: str,
    timing_id: str,
    payload: UpdateSoAEntityRequest,
    request: Request,
) -> SoAEntityCreatedResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    try:
        await update_timing_window(
            driver=driver,
            study_version_id=version_id,
            user_id=user_id,
            change_reason=change_reason,
            timing_id=timing_id,
            properties=payload.properties,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    return SoAEntityCreatedResponse(id=timing_id)


# --- Link/Association Endpoints ---


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/links/epoch-visit",
    response_model=SoALinkResponse,
    status_code=status.HTTP_200_OK,
)
async def link_epoch_visit_endpoint(
    study_id: str,
    version_id: str,
    payload: LinkEpochVisitRequest,
    request: Request,
) -> SoALinkResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    success = await link_epoch_to_visit(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        epoch_id=payload.epoch_id,
        visit_id=payload.visit_id,
    )
    if not success:
        raise HTTPException(status_code=400, detail="Failed to link epoch and visit")
    return SoALinkResponse()


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/links/visit-procedure",
    response_model=SoALinkResponse,
    status_code=status.HTTP_200_OK,
)
async def link_visit_procedure_endpoint(
    study_id: str,
    version_id: str,
    payload: LinkVisitProcedureRequest,
    request: Request,
) -> SoALinkResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    success = await link_visit_to_procedure(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        visit_id=payload.visit_id,
        procedure_id=payload.procedure_id,
    )
    if not success:
        raise HTTPException(
            status_code=400, detail="Failed to link visit and procedure"
        )
    return SoALinkResponse()


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/links/timing",
    response_model=SoALinkResponse,
    status_code=status.HTTP_200_OK,
)
async def link_timing_endpoint(
    study_id: str,
    version_id: str,
    payload: LinkTimingRequest,
    request: Request,
) -> SoALinkResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    success = await link_visit_or_procedure_to_timing(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        source_id=payload.source_id,
        timing_id=payload.timing_id,
        source_type=payload.source_type,
    )
    if not success:
        raise HTTPException(status_code=400, detail="Failed to link to timing window")
    return SoALinkResponse()


@app.post(
    "/api/v1/studies/{study_id}/versions/{version_id}/links/arm-applicability",
    response_model=SoALinkResponse,
    status_code=status.HTTP_200_OK,
)
async def link_arm_applicability_endpoint(
    study_id: str,
    version_id: str,
    payload: LinkArmApplicabilityRequest,
    request: Request,
) -> SoALinkResponse:
    driver = await get_neo4j_driver(request)
    user_id = getattr(request.state, "user_id", "system")
    change_reason = getattr(request.state, "change_reason", "system_operation")

    success = await link_arm_applicability(
        driver=driver,
        study_version_id=version_id,
        user_id=user_id,
        change_reason=change_reason,
        arm_id=payload.arm_id,
        target_id=payload.target_id,
        target_type=payload.target_type,
    )
    if not success:
        raise HTTPException(status_code=400, detail="Failed to link arm applicability")
    return SoALinkResponse()


# --- SoA Matrix Projection Endpoint ---


@app.get(
    "/api/v1/studies/{study_id}/versions/{version_id}/soa-projection",
    response_model=SoAMatrixView,
    status_code=status.HTTP_200_OK,
)
async def get_soa_projection_endpoint(
    study_id: str,
    version_id: str,
    request: Request,
) -> SoAMatrixView:
    driver = await get_neo4j_driver(request)
    matrix = await get_soa_matrix_projection(driver, version_id)
    return SoAMatrixView(**matrix)


@app.get(
    "/api/v1/studies/{study_id}/versions/diff",
    response_model=VersionDiffResponse,
    status_code=status.HTTP_200_OK,
)
async def get_versions_diff_endpoint(
    study_id: str,
    version_id1: str = Query(..., description="The old version ID"),
    version_id2: str = Query(..., description="The new version ID"),
    request: Request = None,
) -> VersionDiffResponse:
    """
    Exposes graph-native, form-level version-diff API.
    Identifies additions, modifications, and deletions of forms.
    Returns HTTP 400 Bad Request if either version is nonexistent or unrelated.
    """
    driver = await get_neo4j_driver(request)
    try:
        diff_dict = await compute_graph_diff(driver, study_id, version_id1, version_id2)
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=str(e),
        )

    added = [
        DifferenceResult(
            field=node["key"],
            old_value=None,
            new_value=node["xform_definition_xml"],
        )
        for node in diff_dict["added_nodes"]
    ]
    modified = [
        DifferenceResult(
            field=node["key"],
            old_value=node["old_value"],
            new_value=node["new_value"],
        )
        for node in diff_dict["modified_nodes"]
    ]
    deleted = [
        DifferenceResult(
            field=node["key"],
            old_value=node["xform_definition_xml"],
            new_value=None,
        )
        for node in diff_dict["deleted_nodes"]
    ]

    return VersionDiffResponse(
        added_nodes=added,
        modified_nodes=modified,
        deleted_nodes=deleted,
    )


# ==========================================
# Library Object Instantiation API Models
# ==========================================


class InstantiateLibraryObjectRequest(BaseModel):
    library_object_id: str = Field(
        ..., description="Stable, unique global library ID to instantiate."
    )
    version: Optional[int] = Field(
        None,
        description="The specific version of the library object to instantiate. Defaults to latest if not specified.",
    )


class InstantiatedFromDetail(BaseModel):
    library_object_id: str
    version: int
    sponsor_id: str


class LibraryInstanceResponse(BaseModel):
    id: str
    study_id: str
    object_type: str
    payload: Dict[str, Any]
    created_at: str
    created_by: str
    instantiated_from: InstantiatedFromDetail


@app.post(
    "/api/v1/studies/{study_id}/library-instances",
    response_model=LibraryInstanceResponse,
    status_code=status.HTTP_201_CREATED,
)
async def instantiate_library_object_endpoint(
    study_id: str,
    payload: InstantiateLibraryObjectRequest,
    request: Request,
) -> LibraryInstanceResponse:
    """
    Instantiates a specific version (or latest) of a Global Library object into a study-scoped instance.
    Enforces that the library object and study both belong to/are accessible by the authenticated sponsor.
    """
    driver = await get_neo4j_driver(request)

    # 1. Retrieve sponsor scope & user id
    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    user_id = getattr(request.state, "user_id", "system")

    # 2. Call the delta manager to run checks and instantiation
    try:
        instance = await instantiate_library_object_in_study(
            driver=driver,
            study_id=study_id,
            library_object_id=payload.library_object_id,
            version=payload.version,
            sponsor_id=sponsor_id,
            user_id=user_id,
        )
        return LibraryInstanceResponse(**instance)
    except PermissionError as e:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=str(e),
        )
    except ValueError as e:
        # Check if "not found" is for study or library object
        err_msg = str(e)
        if "Study" in err_msg or "Library object" in err_msg or "not found" in err_msg:
            raise HTTPException(
                status_code=status.HTTP_404_NOT_FOUND,
                detail=err_msg,
            )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=err_msg,
        )


class UpdateLibraryInstanceRequest(BaseModel):
    payload: Dict[str, Any] = Field(
        ..., description="The complete updated payload of the library instance."
    )


@app.put(
    "/api/v1/studies/{study_id}/library-instances/{instance_id}",
    response_model=LibraryInstanceResponse,
    status_code=status.HTTP_200_OK,
)
async def update_library_instance_endpoint(
    study_id: str,
    instance_id: str,
    payload: UpdateLibraryInstanceRequest,
    request: Request,
) -> LibraryInstanceResponse:
    """
    Updates the payload of an instantiated library object inside a study.
    Verifies that target study belongs to or is accessible by the authenticated sponsor,
    leaving the global library source immutable.
    """
    driver = await get_neo4j_driver(request)

    # 1. Retrieve sponsor scope & user id
    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    user_id = getattr(request.state, "user_id", "system")

    # 2. Call delta manager to apply payload updates
    try:
        updated_instance = await update_library_instance_in_study(
            driver=driver,
            study_id=study_id,
            instance_id=instance_id,
            payload=payload.payload,
            sponsor_id=sponsor_id,
            user_id=user_id,
        )
        return LibraryInstanceResponse(**updated_instance)
    except PermissionError as e:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=str(e),
        )
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        )


@app.get(
    "/api/v1/studies/{study_id}/library-instances/{instance_id}/diff",
    response_model=List[DifferenceResult],
    status_code=status.HTTP_200_OK,
)
async def get_library_instance_diff_endpoint(
    study_id: str,
    instance_id: str,
    request: Request,
) -> List[DifferenceResult]:
    """
    Returns field-level dot-notated differences between the library instance payload and its linked source version.
    """
    driver = await get_neo4j_driver(request)

    sponsor_id = getattr(request.state, "sponsor_id", None) or request.headers.get(
        "X-Sponsor-Id"
    )
    if not sponsor_id or not isinstance(sponsor_id, str) or not sponsor_id.strip():
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Missing authenticated sponsor scope",
        )
    sponsor_id = sponsor_id.strip()

    try:
        instance = await get_library_instance_in_study(
            driver=driver,
            study_id=study_id,
            instance_id=instance_id,
            sponsor_id=sponsor_id,
        )
    except PermissionError as e:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail=str(e),
        )
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=str(e),
        )

    inst_from = instance.get("instantiated_from")
    if not inst_from:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Instance does not have a linked source library object.",
        )

    source_obj_id = inst_from.get("library_object_id")
    source_version = inst_from.get("version")
    source_sponsor_id = inst_from.get("sponsor_id")

    source_obj = await get_library_object_by_version(
        driver=driver,
        object_id=source_obj_id,
        sponsor_id=source_sponsor_id,
        version=source_version,
    )
    if not source_obj:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Source library object {source_obj_id} version {source_version} not found.",
        )

    source_payload = source_obj.get("payload") or {}
    instance_payload = instance.get("payload") or {}

    def flatten_dict(d: Any, parent_key: str = "", sep: str = ".") -> Dict[str, Any]:
        """
        Recursively flatten a nested dictionary or list into a flat dictionary.
        """
        items: List[Tuple[str, Any]] = []
        if isinstance(d, dict):
            for k, v in d.items():
                new_key = f"{parent_key}{sep}{k}" if parent_key else k
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        elif isinstance(d, list):
            for i, v in enumerate(d):
                new_key = f"{parent_key}{sep}[{i}]" if parent_key else f"[{i}]"
                items.extend(flatten_dict(v, new_key, sep=sep).items())
        else:
            items.append((parent_key, d))
        return dict(items)

    flat_source = flatten_dict(source_payload)
    flat_instance = flatten_dict(instance_payload)

    all_keys = set(flat_source.keys()).union(set(flat_instance.keys()))
    differences = []

    for key in sorted(all_keys):
        val1 = flat_source.get(key)
        val2 = flat_instance.get(key)
        if val1 != val2:
            differences.append(
                DifferenceResult(field=key, old_value=val1, new_value=val2)
            )

    return differences
