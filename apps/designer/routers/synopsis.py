"""FastAPI router for clinical protocol synopsis rendering and document export API.

Requirements: PRD-SYS-001
"""

import base64
import uuid

import usdm_model
from fastapi import APIRouter, Depends, HTTPException, Query, Response
from fastapi.responses import HTMLResponse

from apps.designer.content_assembly import (
    USDMSynopsisAssembler,
    assemble_rendered_protocol_document,
)
from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer
from apps.designer.src.domain.synopsis_transport_models import (
    SynopsisExportRequest,
    SynopsisExportResponse,
)
from packages.security.middleware import get_current_user

router = APIRouter(prefix="/api/v1/synopsis", tags=["Synopsis"])


def _build_mock_study_for_export(study_id: str) -> usdm_model.Study:
    """Build fallback mock USDM study model for export rendering."""
    return usdm_model.Study(
        id=uuid.UUID(study_id) if len(study_id) == 36 else uuid.uuid4(),
        name=f"Study-{study_id}",
        protocolTitle=f"Protocol Specification {study_id}",
        usdmVersion="3.0",
        studyDesigns=[],
    )


@router.post("/export", response_model=SynopsisExportResponse)
async def export_synopsis_document(
    payload: SynopsisExportRequest,
    current_user: dict = Depends(get_current_user),
) -> SynopsisExportResponse:
    """Export authored clinical protocol synopsis into PDF, DOCX, or HTML format.

    Requirements: PRD-SYS-001
    """
    fmt = payload.format.lower()
    if fmt not in ("pdf", "docx", "html"):
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported export format '{payload.format}'. Supported formats: pdf, docx, html",
        )

    study = _build_mock_study_for_export(payload.study_id)
    assembler = USDMSynopsisAssembler()
    renderer = ProtocolDocumentRenderer()

    if fmt == "html":
        raw_bytes = assembler.assemble_and_render_html(
            study=study,
            creator=payload.creator or "Cadence Clinical",
            change_reason=payload.change_reason or "Baseline",
        ).encode("utf-8")
    elif fmt == "docx":
        rendered_doc = assemble_rendered_protocol_document(
            study=study,
            creator=payload.creator or "Cadence Clinical",
            change_reason=payload.change_reason or "Baseline",
        )
        raw_bytes = renderer.render_docx(rendered_doc)
    else:  # pdf
        html_str = assembler.assemble_and_render_html(
            study=study,
            creator=payload.creator or "Cadence Clinical",
            change_reason=payload.change_reason or "Baseline",
        )
        raw_bytes = renderer.render_pdf(html_str)

    encoded_str = base64.b64encode(raw_bytes).decode("utf-8")
    filename = f"synopsis_{payload.study_id}.{fmt}"

    return SynopsisExportResponse(
        study_id=payload.study_id,
        format=fmt,
        content_base64=encoded_str,
        filename=filename,
    )


@router.get("/render/{study_id}")
async def render_synopsis_download(
    study_id: str,
    format: str = Query("pdf", description="Export format: pdf, docx, html"),
    current_user: dict = Depends(get_current_user),
) -> Response:
    """Direct file download endpoint for rendered protocol synopsis documents.

    Requirements: PRD-SYS-001
    """
    fmt = format.lower()
    if fmt not in ("pdf", "docx", "html"):
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported format '{format}'. Must be pdf, docx, or html",
        )

    study = _build_mock_study_for_export(study_id)
    assembler = USDMSynopsisAssembler()
    renderer = ProtocolDocumentRenderer()

    if fmt == "html":
        html_content = assembler.assemble_and_render_html(study=study)
        return HTMLResponse(content=html_content)
    if fmt == "docx":
        rendered_doc = assemble_rendered_protocol_document(study=study)
        docx_bytes = renderer.render_docx(rendered_doc)
        return Response(
            content=docx_bytes,
            media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            headers={
                "Content-Disposition": f"attachment; filename=synopsis_{study_id}.docx"
            },
        )
    # pdf
    html_str = assembler.assemble_and_render_html(study=study)
    pdf_bytes = renderer.render_pdf(html_str)
    return Response(
        content=pdf_bytes,
        media_type="application/pdf",
        headers={
            "Content-Disposition": f"attachment; filename=synopsis_{study_id}.pdf"
        },
    )
