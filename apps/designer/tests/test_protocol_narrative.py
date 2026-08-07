"""Integration test suite verifying rendered clinical protocol narrative and synopsis document completeness.

Requirements: PRD-SYS-001
"""

from fastapi.testclient import TestClient

import packages  # noqa: F401
from apps.designer.content_assembly import (
    USDMSynopsisAssembler,
    assemble_rendered_protocol_document,
)
from apps.designer.main import app
from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer
from apps.designer.tests.test_synopsis_router import _make_auth_headers
from scripts.tests.test_content_assembly import base_study  # noqa: F401

client = TestClient(app)


def test_rendered_protocol_narrative_completeness(base_study) -> None:  # noqa: F811
    """Validate full protocol narrative document contains all required regulatory sections.

    Requirements: PRD-SYS-001
    """
    # Step 1: Assemble rendered protocol document model
    rendered_doc = assemble_rendered_protocol_document(
        base_study,
        creator="Dr. Lead Principal Investigator",
        change_reason="Final Submission Package V1.0",
        version_index=1,
    )

    assert rendered_doc.metadata.creator == "Dr. Lead Principal Investigator"
    assert rendered_doc.metadata.change_reason == "Final Submission Package V1.0"
    assert rendered_doc.synopsis is not None
    assert rendered_doc.soa_matrix is not None

    # Step 2: Render HTML document using Jinja2 engine
    assembler = USDMSynopsisAssembler()
    html_output = assembler.assemble_and_render_html(
        base_study,
        creator="Dr. Lead Principal Investigator",
        change_reason="Final Submission Package V1.0",
    )

    assert "<!DOCTYPE html>" in html_output
    assert "Clinical Protocol Synopsis" in html_output
    assert "Dr. Lead Principal Investigator" in html_output
    assert "Final Submission Package V1.0" in html_output
    assert "1. Study Design & Objectives" in html_output
    assert "2. Eligibility Criteria" in html_output
    assert "3. Schedule of Activities (SoA)" in html_output
    assert "Vital Signs Collection" in html_output

    # Step 3: Render Word (.docx) document and verify structure
    renderer = ProtocolDocumentRenderer()
    docx_bytes = renderer.render_docx(rendered_doc)
    assert isinstance(docx_bytes, bytes)
    assert len(docx_bytes) > 2000
    assert docx_bytes.startswith(b"PK\x03\x04")

    # Step 4: Render PDF stream and verify PDF container signature
    pdf_bytes = renderer.render_pdf(html_output)
    assert isinstance(pdf_bytes, bytes)
    assert len(pdf_bytes) > 100
    assert pdf_bytes.startswith(b"%PDF-")


def test_synopsis_endpoint_end_to_end_flow() -> None:
    """Validate end-to-end API export flow returns complete documents.

    Requirements: PRD-SYS-001
    """
    headers = _make_auth_headers()

    # Request HTML export
    res_html = client.post(
        "/api/v1/synopsis/export",
        json={"study_id": "study-narrative-999", "format": "html"},
        headers=headers,
    )
    assert res_html.status_code == 200
    assert res_html.json()["format"] == "html"

    # Request direct PDF download
    res_pdf = client.get(
        "/api/v1/synopsis/render/study-narrative-999?format=pdf",
        headers=headers,
    )
    assert res_pdf.status_code == 200
    assert res_pdf.content.startswith(b"%PDF-")
