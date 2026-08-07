"""Unit test suite for eISF document upload and dynamic watermarking service.

Requirements: PRD-SYS-001
"""

import hashlib

import pytest
from execution.eisf_models import EISFTaxonomyCategoryEnum

import packages  # noqa: F401
from apps.execution.services.eisf_service import EISFService


@pytest.mark.asyncio
async def test_eisf_upload_and_watermark() -> None:
    """Validate uploading eISF document calculates SHA-256 checksum and applies watermark.

    Requirements: PRD-SYS-001
    """
    service = EISFService()
    file_data = b"Sample IRB Approval Letter PDF content"

    doc = await service.upload_document(
        study_id="study_eisf_02",
        site_id="site_102",
        category=EISFTaxonomyCategoryEnum.IRB_IEC_APPROVAL,
        title="Central IRB Approval Notice",
        file_name="IRB_Approval_Notice.pdf",
        content_bytes=file_data,
        uploader_id="crc_user_02",
    )

    expected_hash = hashlib.sha256(file_data).hexdigest()
    assert doc.sha256_hash == expected_hash
    assert doc.file_size_bytes == len(file_data)
    assert doc.category == EISFTaxonomyCategoryEnum.IRB_IEC_APPROVAL

    # Verify we can retrieve raw document content bytes from storage layer
    retrieved_content = await service.get_document_content(doc.document_id)
    assert retrieved_content == file_data

    # Test dynamic watermark
    watermarked = service.apply_watermark(file_data, "CONFIDENTIAL - SITE 102")
    assert b"CONFIDENTIAL - SITE 102" in watermarked
    assert file_data in watermarked
