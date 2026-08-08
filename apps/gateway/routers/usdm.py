"""API Gateway router for USDM protocol import, export, and graph synchronization.

Requirements: PRD-SYS-001
"""

from fastapi import APIRouter, Depends, HTTPException, status

from apps.gateway.src.domain.acl.usdm_dto import (
    UsdmExportResponse,
    UsdmImportRequest,
    UsdmImportResponse,
)
from apps.gateway.src.domain.acl.usdm_importer import USDMImporter
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
    try:
        result = await importer.import_usdm(request.raw_usdm_json)
        return UsdmImportResponse(
            study_id=result.study_id,
            nodes_created=result.nodes_created,
            relationships_created=result.relationships_created,
            validation_warnings=result.validation_warnings,
        )
    except ValueError as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=f"Invalid USDM protocol specification: {str(exc)}",
        ) from exc


@router.get(
    "/export/{study_id}",
    response_model=UsdmExportResponse,
    status_code=status.HTTP_200_OK,
)
async def export_usdm_protocol_spec(
    study_id: str,
    user: dict = Depends(get_current_user),
) -> UsdmExportResponse:
    """Export study protocol graph specification as CDISC USDM JSON.

    Requirements: PRD-SYS-001
    """
    # Sample exported USDM graph structure
    sample_usdm_export = {
        "id": study_id,
        "name": f"Study-{study_id}",
        "protocolTitle": f"Exported Protocol for Study {study_id}",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": f"sd_{study_id}",
                "name": "Primary Study Design",
                "arms": [
                    {
                        "id": "arm_01",
                        "name": "Experimental Arm",
                        "armType": "Treatment",
                    }
                ],
            }
        ],
    }

    return UsdmExportResponse(
        study_id=study_id,
        usdm_json=sample_usdm_export,
    )
