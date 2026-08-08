"""eISF document upload, dynamic watermarking, and storage service.

Requirements: PRD-SYS-001
"""

import hashlib
import uuid
from datetime import UTC, datetime

import packages  # noqa: F401
from apps.execution.domain.eisf_models import (
    EISFDocumentRecord,
    EISFTaxonomyCategoryEnum,
)


class EISFService:
    """Service handling site-isolated regulatory binder document uploads, checksum validation, and dynamic watermarking.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize in-memory document metadata store."""
        self._document_store: dict[str, EISFDocumentRecord] = {}

    def upload_document(
        self,
        study_id: str,
        site_id: str,
        category: EISFTaxonomyCategoryEnum,
        title: str,
        file_name: str,
        content_bytes: bytes,
        uploader_id: str,
        version: str = "1.0",
    ) -> EISFDocumentRecord:
        """Process document upload, calculate SHA-256 checksum, and register eISF record.

        Args:
            study_id: Target protocol study ID.
            site_id: Target investigator site ID.
            category: DIA eISF taxonomy category.
            title: Document title.
            file_name: Original file name.
            content_bytes: Raw file content bytes.
            uploader_id: User ID of uploader.
            version: Document version string.

        Returns:
            Registered EISFDocumentRecord instance.
        """
        doc_id = f"doc_{uuid.uuid4().hex[:8]}"
        sha256_hash = hashlib.sha256(content_bytes).hexdigest()
        now_iso = datetime.now(UTC).isoformat()

        record = EISFDocumentRecord(
            document_id=doc_id,
            study_id=study_id,
            site_id=site_id,
            category=category,
            title=title,
            version=version,
            file_name=file_name,
            file_size_bytes=len(content_bytes),
            sha256_hash=sha256_hash,
            uploaded_by=uploader_id,
            uploaded_at=now_iso,
            is_redacted=False,
        )

        self._document_store[doc_id] = record
        return record

    def apply_watermark(self, content_bytes: bytes, watermark_text: str) -> bytes:
        """Apply dynamic security watermark banner text to document content.

        Args:
            content_bytes: Raw file content bytes.
            watermark_text: Watermark text string (e.g., CONFIDENTIAL - SITE 101).

        Returns:
            Watermarked document content bytes.
        """
        header = f"% WATERMARK: {watermark_text} %\n".encode()
        return header + content_bytes
