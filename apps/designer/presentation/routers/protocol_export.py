"""FastAPI router for ICH M11 Word document and USDM JSON protocol export API endpoints.

Requirements: PRD-SYS-001
"""

from fastapi import APIRouter, Depends, HTTPException, Query, Response
from fastapi.responses import PlainTextResponse

import packages  # noqa: F401
from apps.designer.exporters.m11_exporter import M11ProtocolExporter
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/designer/export", tags=["ProtocolExport"])


@router.get("/m11/{study_id}")
async def export_protocol_m11_endpoint(
    study_id: str,
    format: str = Query("docx", description="Target export format: docx or json"),
    current_user: dict = Depends(get_current_user),
) -> Response:
    """Download authored protocol specification in formatted ICH M11 Word (.docx) or USDM JSON format.

    Requirements: PRD-SYS-001
    """
    fmt = format.lower()
    if fmt not in ("docx", "json"):
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported format '{format}'. Must be 'docx' or 'json'",
        )

    study_payload = {
        "id": study_id,
        "name": f"Protocol-{study_id}",
        "protocolTitle": f"Phase III Protocol Specification {study_id}",
        "usdmVersion": "3.0",
        "studyDesigns": [
            {
                "id": "design_01",
                "name": "Parallel Study Design",
                "arms": [{"name": "Treatment 100mg", "armType": "Experimental"}],
                "objectives": [{"name": "Assess Efficacy"}],
            }
        ],
        "eligibilityCriteria": [
            {"criterionType": "Inclusion", "text": "Age >= 18"},
        ],
    }

    exporter = M11ProtocolExporter()

    if fmt == "json":
        json_str = exporter.export_usdm_json(study_payload)
        return PlainTextResponse(
            content=json_str,
            media_type="application/json",
            headers={
                "Content-Disposition": f"attachment; filename=protocol_{study_id}_usdm.json"
            },
        )
    # docx
    docx_bytes = exporter.export_ich_m11_docx(study_payload)
    return Response(
        content=docx_bytes,
        media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        headers={
            "Content-Disposition": f"attachment; filename=protocol_{study_id}_m11.docx"
        },
    )
