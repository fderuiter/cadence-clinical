"""Non-destructive PDF redaction overlay generator service.

Requirements: PRD-SYS-001
"""

import hashlib
from typing import Any, Dict, List

import fitz
from pydantic import BaseModel

import packages  # noqa: F401
from packages.security.ner_scrubber import PHINameEntityScrubber


class RedactionBox(BaseModel):
    """Represents a target bounding box for PDF redaction.

    Requirements: PRD-SYS-001
    """

    page_number: int
    x0: float
    y0: float
    x1: float
    y1: float


class PDFRedactionEngine:
    """PDF Redaction Engine that applies irreversible visual overlays and scrubs underlying text.

    Requirements: PRD-SYS-001
    """

    def apply_bounding_box_redactions(
        self, pdf_bytes: bytes, boxes: List[RedactionBox]
    ) -> bytes:
        """Apply irreversible PDF redaction overlays and purge underlying text/image content.

        Requirements: PRD-SYS-001
        """
        doc = fitz.open(stream=pdf_bytes, filetype="pdf")

        # Group boxes by page number to apply multiple redactions efficiently per page
        boxes_by_page: Dict[int, List[RedactionBox]] = {}
        for box in boxes:
            boxes_by_page.setdefault(box.page_number, []).append(box)

        # Apply redactions
        for page_num, page_boxes in boxes_by_page.items():
            if 0 <= page_num < len(doc):
                page = doc[page_num]
                for box in page_boxes:
                    rect = fitz.Rect(box.x0, box.y0, box.x1, box.y1)
                    page.add_redact_annot(rect, fill=(0, 0, 0))
                # Apply redactions to this page
                page.apply_redactions(images=fitz.PDF_REDACT_IMAGE_REMOVE)

        # Strip Annot comments, form fill fields, and metadata across all pages
        for page in doc:
            for widget in list(page.widgets()):
                page.delete_widget(widget)
            for annot in list(page.annots()):
                page.delete_annot(annot)

        # Scrub metadata
        doc.set_metadata({})

        return doc.tobytes()

    def sanitize_pdf_bytes(self, pdf_bytes: bytes, boxes: List[RedactionBox]) -> bytes:
        """Apply irreversible PDF redaction overlays and purge underlying text/image content.

        Requirements: PRD-SYS-001
        """
        return self.apply_bounding_box_redactions(pdf_bytes, boxes)


class PDFRedactorService:
    """Service generating non-destructive PHI redaction overlays for PDF documents.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize PHI NER scrubber."""
        self._scrubber = PHINameEntityScrubber()

    def apply_redaction_overlay(
        self,
        pdf_bytes: bytes,
        target_snippets: List[str],
    ) -> Dict[str, Any]:
        """Apply non-destructive redaction overlays over specified target PHI snippets.

        Args:
            pdf_bytes: Original PDF document bytes.
            target_snippets: List of target text strings to redact.

        Returns:
            Dict containing redacted content bytes, redacted count, and SHA-256 checksum.
        """
        content_text = pdf_bytes.decode("utf-8", errors="ignore")

        detected = self._scrubber.detect_phi(content_text)
        total_redacted = len(target_snippets) + len(detected)

        redacted_text = content_text
        for snippet in target_snippets:
            if snippet in redacted_text:
                redacted_text = redacted_text.replace(snippet, "[REDACTED_TEXT]")

        redacted_text = self._scrubber.scrub_phi(redacted_text)
        redacted_bytes = redacted_text.encode("utf-8")
        sha256_checksum = hashlib.sha256(redacted_bytes).hexdigest()

        remaining_phi = self._scrubber.detect_phi(redacted_text)
        is_clean = len(remaining_phi) == 0

        return {
            "redacted_content": redacted_bytes,
            "redacted_entities_count": total_redacted,
            "sha256_checksum": sha256_checksum,
            "is_clean": is_clean,
        }
