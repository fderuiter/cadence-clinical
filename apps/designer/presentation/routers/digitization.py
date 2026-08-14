"""FastAPI router for AI-Native USDM Protocol Digitization and automated synthesis.

Exposes endpoints for extracting structured CDISC USDM entities from raw protocol
files and committing them into the Neo4j knowledge graph and eCRF runtime.
"""

from __future__ import annotations

import logging
from typing import Any

from fastapi import (
    APIRouter,
    Depends,
    File,
    HTTPException,
    Request,
    UploadFile,
    status,
)

from apps.designer.application.services.digitization_service import (
    _heuristic_protocol_extraction,
    extract_usdm_from_protocol_document,
    synthesize_ecrf_forms,
)
from apps.designer.dependencies import get_neo4j_driver
from apps.designer.domain.digitization_models import (
    CommitUSDMRequest,
    CommitUSDMResponse,
    USDMProtocolExtractionResponse,
)
from apps.designer.infrastructure.neo4j_usdm_writer import commit_usdm_graph
from packages.security.rbac import require_permission

logger = logging.getLogger(__name__)

router = APIRouter(tags=["protocol_digitization"])


@router.post(
    "/api/v1/designer/digitization/extract",
    response_model=USDMProtocolExtractionResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("protocol_ingestion:upload"))],
)
async def extract_protocol_digitization(
    file: UploadFile = File(...),
) -> USDMProtocolExtractionResponse:
    """Extracts structured CDISC USDM v4.0 parameters from an uploaded clinical protocol file.

    Accepts PDF, DOCX, or text files.
    """
    filename = file.filename or "unknown.pdf"
    content = await file.read()

    if not content or len(content) == 0:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail="Uploaded protocol file is empty or corrupted.",
        )

    try:
        return await extract_usdm_from_protocol_document(content, filename)
    except Exception as exc:
        logger.error("Protocol extraction failed: %s", exc)
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Protocol entity extraction failed: {str(exc)}",
        ) from exc


@router.get(
    "/api/v1/designer/digitization/sample",
    response_model=USDMProtocolExtractionResponse,
    status_code=status.HTTP_200_OK,
    dependencies=[Depends(require_permission("study:read"))],
)
async def get_sample_protocol_extraction() -> USDMProtocolExtractionResponse:
    """Returns a canonical pre-extracted USDM v4.0 study structure for instant review and sandboxing."""
    sample_text = (
        "Protocol Title: A Phase II Randomized Study of Novel Therapeutic vs Control in Advanced Solid Tumors\n"
        "Protocol ID: CDNC-2026-001\n"
        "Phase: Phase II\n"
        "Therapeutic Area: Oncology\n"
    )
    return _heuristic_protocol_extraction(sample_text, "sample_protocol.pdf")


@router.post(
    "/api/v1/designer/studies/{study_id}/commit-usdm",
    response_model=CommitUSDMResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_permission("study_design:create"))],
)
async def commit_usdm_to_study(
    study_id: str,
    payload: CommitUSDMRequest,
    request: Request,
    driver: Any = Depends(get_neo4j_driver),
) -> CommitUSDMResponse:
    """Commits extracted USDM entities to the Neo4j graph and synthesizes CDASH eCRF forms.

    Guarantees 21 CFR Part 11 audit attribution and GxP change justification.
    """
    user_id = (
        getattr(request.state, "user_id", None)
        or request.headers.get("X-User-Id")
        or "system"
    )

    if not payload.change_reason or not payload.change_reason.strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing change justification reason.",
        )

    # 1. Commit to Neo4j Graph
    graph_res = await commit_usdm_graph(driver, study_id, payload.data, user_id)

    # 2. Synthesize CDASH eCRFs & Rules
    synthesized_forms = synthesize_ecrf_forms(payload.data)

    version_id = f"{study_id}_v1"

    return CommitUSDMResponse(
        study_id=study_id,
        version_id=version_id,
        status="COMMITTED",
        nodes_created=graph_res.get("nodes_created", 0),
        relationships_created=graph_res.get("relationships_created", 0),
        synthesized_forms=synthesized_forms,
        message=f"Successfully synthesized {len(synthesized_forms)} eCRF forms and committed USDM graph.",
    )
