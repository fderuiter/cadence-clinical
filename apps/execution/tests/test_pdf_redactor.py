"""Unit test suite for PDF redaction overlay generator.

Requirements: PRD-SYS-001
"""

import fitz

import packages  # noqa: F401
from apps.execution.services.pdf_redactor import (
    PDFRedactionEngine,
    PDFRedactorService,
    RedactionBox,
)


def test_pdf_redaction_overlay_generation() -> None:
    """Validate PDFRedactorService applies non-destructive redactions and verifies output cleanliness.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    redactor = PDFRedactorService()
    pdf_sample = (
        b"Subject Name: Jane Doe. SSN: 111-22-3333. Clinical Assessment Report."
    )

    result = redactor.apply_redaction_overlay(pdf_sample, ["Jane Doe"])

    assert result["is_clean"] is True
    assert result["redacted_entities_count"] >= 2
    assert b"111-22-3333" not in result["redacted_content"]
    assert b"Jane Doe" not in result["redacted_content"]
    assert len(result["sha256_checksum"]) == 64


def test_pdf_redaction_engine_bounding_box() -> None:
    """Validate PDFRedactionEngine applies bounding box redactions and purges underlying text.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    # Create a simple PDF in memory using fitz
    doc = fitz.open()
    page = doc.new_page()
    page.insert_text((50, 50), "SECRET PASSWORD")
    pdf_bytes = doc.tobytes()

    engine = PDFRedactionEngine()
    # Target the exact coordinates of "SECRET PASSWORD"
    boxes = [RedactionBox(page_number=0, x0=40.0, y0=40.0, x1=200.0, y1=60.0)]

    redacted_bytes = engine.apply_bounding_box_redactions(pdf_bytes, boxes)
    assert redacted_bytes is not None

    # Verify that the text cannot be extracted from the redacted document
    redacted_doc = fitz.open(stream=redacted_bytes, filetype="pdf")
    redacted_page = redacted_doc[0]
    extracted_text = redacted_page.get_text()

    assert "SECRET" not in extracted_text
    assert "PASSWORD" not in extracted_text


def test_pdf_redaction_engine_purges_metadata_and_fields() -> None:
    """Validate PDFRedactionEngine strips comments, fields, and metadata.

    Requirements: PRD-SYS-001
    """
    # @req:PRD-SYS-001
    doc = fitz.open()
    page = doc.new_page()
    page.insert_text((50, 50), "Hello World")

    # Add a widget (form field)
    widget = fitz.Widget()
    widget.rect = fitz.Rect(10, 10, 100, 30)
    widget.field_name = "test_field"
    widget.field_type = fitz.PDF_WIDGET_TYPE_TEXT
    page.add_widget(widget)

    # Add a square annotation (comment/visual)
    rect = fitz.Rect(10, 10, 100, 30)
    page.add_rect_annot(rect)

    # Add metadata
    doc.set_metadata({"title": "Top Secret Document", "author": "John Doe"})

    pdf_bytes = doc.tobytes()

    engine = PDFRedactionEngine()
    redacted_bytes = engine.apply_bounding_box_redactions(pdf_bytes, [])

    # Load and verify
    redacted_doc = fitz.open(stream=redacted_bytes, filetype="pdf")

    # Verify metadata is empty dictionary (or contains only empty standard keys)
    metadata = redacted_doc.metadata
    assert not metadata.get("title")
    assert not metadata.get("author")

    # Verify page contains no widgets or annotations
    redacted_page = redacted_doc[0]
    assert len(list(redacted_page.widgets())) == 0
    assert len(list(redacted_page.annots())) == 0
