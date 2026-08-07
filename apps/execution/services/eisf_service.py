"""eISF document upload, dynamic watermarking, and storage service.

Requirements: PRD-SYS-001
"""

import hashlib
import uuid
from datetime import UTC, datetime

from execution.eisf_models import (
    EISFDocumentRecord,
    EISFTaxonomyCategoryEnum,
)

import packages  # noqa: F401
from packages.storage import get_storage_provider


class EISFService:
    """Service handling site-isolated regulatory binder document uploads, checksum validation, and dynamic watermarking.

    Requirements: PRD-SYS-001
    """

    def __init__(self) -> None:
        """Initialize in-memory document metadata store."""
        self._document_store: dict[str, EISFDocumentRecord] = {}

    async def upload_document(
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

        # Save raw content bytes to the active storage provider
        storage_key = f"eisf_documents/{doc_id}"
        provider = get_storage_provider()
        await provider.put_object(
            storage_key, content_bytes, expected_sha256=sha256_hash
        )

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

    async def get_document_content(self, doc_id: str) -> bytes:
        """Retrieve raw document content bytes for the given document ID.

        Requirements: PRD-SYS-001
        """
        storage_key = f"eisf_documents/{doc_id}"
        provider = get_storage_provider()
        content_bytes, _ = await provider.get_object(storage_key)
        return content_bytes

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
