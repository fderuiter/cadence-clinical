"""Unit test suite for PDF and DOCX document rendering pipeline.

Requirements: PRD-SYS-001
"""

import packages  # noqa: F401
from apps.designer.content_assembly import (
    USDMSynopsisAssembler,
    assemble_rendered_protocol_document,
)
from apps.designer.renderers.document_renderer import ProtocolDocumentRenderer
from scripts.tests.test_content_assembly import base_study  # noqa: F401


def test_document_renderer_render_pdf(base_study) -> None:  # noqa: F811
    """Validate rendering HTML markup into binary PDF byte stream.

    Requirements: PRD-SYS-001
    """
    assembler = USDMSynopsisAssembler()
    html_content = assembler.assemble_and_render_html(base_study)

    renderer = ProtocolDocumentRenderer()
    pdf_bytes = renderer.render_pdf(html_content)

    assert isinstance(pdf_bytes, bytes)
    assert len(pdf_bytes) > 0
    assert pdf_bytes.startswith(b"%PDF-")


def test_document_renderer_render_docx(base_study) -> None:  # noqa: F811
    """Validate rendering RenderedProtocolDocument into binary Microsoft Word (.docx) stream.

    Requirements: PRD-SYS-001
    """
    rendered_doc = assemble_rendered_protocol_document(
        base_study, creator="Tester", change_reason="Unit Test Setup"
    )

    renderer = ProtocolDocumentRenderer()
    docx_bytes = renderer.render_docx(rendered_doc)

    assert isinstance(docx_bytes, bytes)
    assert len(docx_bytes) > 0
    # Microsoft Word .docx is a ZIP archive starting with PK magic bytes
    assert docx_bytes.startswith(b"PK\x03\x04")
