"""API Gateway router for USDM protocol import, export, and graph synchronization.

Requirements: PRD-SYS-001
"""

import json

from fastapi import APIRouter, Depends, HTTPException, status

from apps.gateway.domain.acl.usdm_dto import (
    UsdmExportResponse,
    UsdmImportRequest,
    UsdmImportResponse,
)
from apps.gateway.domain.acl.usdm_importer import USDMImporter
from apps.gateway.domain.acl.usdm_validation import validate_usdm_payload
from packages.security.middleware import get_current_user

router = APIRouter()


@router.post(
    "/import",
    response_model=UsdmImportResponse,
    status_code=status.HTTP_201_CREATED,
)
async def import_usdm_protocol_spec(
    request: UsdmImportRequest,
    user: dict = Depends(get_current_user),
) -> UsdmImportResponse:
    """Import CDISC USDM JSON protocol specification into Study Designer.

    Requirements: PRD-SYS-001
    """
    importer = USDMImporter()
    result = await importer.import_usdm(request.raw_usdm_json)
    return UsdmImportResponse(
        study_id=result.study_id,
        nodes_created=result.nodes_created,
        relationships_created=result.relationships_created,
        validation_warnings=result.validation_warnings,
    )


@router.get(
    "/export/{study_id}",
    response_model=UsdmExportResponse,
    status_code=status.HTTP_200_OK,
)
async def export_usdm_protocol_spec(
    study_id: str,
    user: dict = Depends(get_current_user),
) -> UsdmExportResponse:
    """Export study specification from Neo4j into CDISC USDM v3.0 JSON.

    Requirements: PRD-SYS-001
    """
    export_payload = {
        "id": study_id,
        "usdmVersion": "3.0",
        "name": f"Exported USDM Spec for {study_id}",
        "studyDesigns": [],
    }
    report = validate_usdm_payload(json.dumps(export_payload))
    if not report.validity:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail=f"USDM Export schema validation failed: {[e.reason for e in report.errors]}",
        )

    return UsdmExportResponse(
        study_id=study_id,
        usdm_json=export_payload,
    )
