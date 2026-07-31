"""Unit test suite for eISF regulatory binder document taxonomy Pydantic models.

Requirements: PRD-SYS-001
"""

from datetime import datetime, timezone

from execution.eisf_models import (
    EISFDocumentRecord,
    EISFTaxonomyCategoryEnum,
)

import packages  # noqa: F401


def test_eisf_document_record_creation() -> None:
    """Validate EISFDocumentRecord instantiation and taxonomy mapping.

    Requirements: PRD-SYS-001
    """
    now_iso = datetime.now(timezone.utc).isoformat()

    doc = EISFDocumentRecord(
        document_id="doc_eisf_001",
        study_id="study_eisf_01",
        site_id="site_101",
        category=EISFTaxonomyCategoryEnum.INVESTIGATOR_CV,
        title="Principal Investigator Curriculum Vitae - Dr. Smith",
        version="2.0",
        file_name="PI_CV_Smith_2026.pdf",
        file_size_bytes=1048576,
        sha256_hash="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",  # pragma: allowlist secret
        uploaded_by="crc_user_01",
        uploaded_at=now_iso,
        expiration_date="2028-12-31",
        is_redacted=False,
    )

    assert doc.document_id == "doc_eisf_001"
    assert doc.site_id == "site_101"
    assert doc.category == EISFTaxonomyCategoryEnum.INVESTIGATOR_CV
    assert doc.version == "2.0"
    assert doc.file_size_bytes == 1048576
