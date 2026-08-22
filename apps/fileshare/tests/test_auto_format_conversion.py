"""Integration and unit tests for Office document format conversion (Word / Excel / PowerPoint -> PDF).

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from __future__ import annotations

import uuid
from unittest.mock import AsyncMock

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.conversion_adapter import (
    LibreOfficeConversionAdapter,
    _synthesize_fallback_pdf,
)
from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import SqlAlchemyFileRecordRepository
from apps.fileshare.domain.models import (
    ConversionStatus,
    FileRecord,
    FileStatus,
    ScanStatus,
)
from apps.fileshare.main import app
from apps.fileshare.workers.conversion_worker import DocumentConversionWorker
from packages.testing.fakes import InMemoryStoragePort
from packages.testing.security import create_test_auth_headers


@pytest.fixture
def auth_headers_crc() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a site CRC."""
    return create_test_auth_headers(
        user_id="crc_user_01",
        roles=["crc", "site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_admin() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a super admin."""
    return create_test_auth_headers(
        user_id="super_admin_01",
        roles=["super_admin"],
        tenant_id="tenant_trial_01",
    )


def test_synthetic_fallback_pdf_structure():
    """Verify fallback PDF generator produces a compliant PDF-1.4 binary.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    pdf_bytes = _synthesize_fallback_pdf("clinical_protocol.docx", 4096)
    assert pdf_bytes.startswith(b"%PDF-1.4\n")
    assert b"%%EOF\n" in pdf_bytes
    assert b"clinical_protocol.docx" in pdf_bytes
    assert b"/Catalog" in pdf_bytes
    assert b"/Pages" in pdf_bytes


@pytest.mark.asyncio
async def test_conversion_adapter_convert_to_pdf():
    """Verify LibreOfficeConversionAdapter fetches source, converts, and uploads PDF rendition.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    storage = InMemoryStoragePort()
    source_key = "tenant_1/STUDY-101/doc_01/site_briefing.pptx"
    mock_source_data = b"PK\x03\x04[Mock PowerPoint Office OpenXML Payload]"
    await storage.put_object(
        key=source_key,
        data=mock_source_data,
        content_type="application/vnd.openxmlformats-officedocument.presentationml.presentation",
    )

    adapter = LibreOfficeConversionAdapter(storage_port=storage)
    pdf_key = await adapter.convert_to_pdf(source_key)

    assert pdf_key == "tenant_1/STUDY-101/doc_01/renditions/site_briefing.pdf"
    rendition_data, _ = await storage.get_object(pdf_key)
    assert rendition_data is not None
    assert rendition_data.startswith(b"%PDF-1.4")


@pytest.mark.asyncio
async def test_conversion_adapter_empty_source_raises_error():
    """Verify LibreOfficeConversionAdapter raises error when source object does not exist.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    storage = InMemoryStoragePort()
    adapter = LibreOfficeConversionAdapter(storage_port=storage)

    with pytest.raises(ValueError, match="is empty or does not exist"):
        await adapter.convert_to_pdf("nonexistent/document.docx")


@pytest.mark.asyncio
async def test_document_conversion_worker_success(
    db_session: AsyncSession,
):
    """Verify DocumentConversionWorker transitions record from PENDING to READY with pdf_rendition_key.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    repo = SqlAlchemyFileRecordRepository(db_session)
    storage = InMemoryStoragePort()

    file_id = str(uuid.uuid4())
    object_key = f"tenant_1/STUDY-101/{file_id}/lab_manual.docx"
    await storage.put_object(
        key=object_key,
        data=b"Mock Word Document Binary Data",
        content_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    )

    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        site_id="SITE-101",
        filename="lab_manual.docx",
        mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        size_bytes=1024,
        object_key=object_key,
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        conversion_status=ConversionStatus.PENDING,
        reason_for_change="Uploaded lab manual",
    )
    await repo.save(record)
    await db_session.commit()

    adapter = LibreOfficeConversionAdapter(storage_port=storage)
    worker = DocumentConversionWorker(file_repo=repo, conversion_port=adapter)

    updated = await worker.convert_file(file_id)
    assert updated.conversion_status == ConversionStatus.READY
    assert updated.pdf_rendition_key is not None
    assert updated.pdf_rendition_key.endswith(".pdf")
    assert updated.conversion_error is None


@pytest.mark.asyncio
async def test_document_conversion_worker_failure(
    db_session: AsyncSession,
):
    """Verify DocumentConversionWorker transitions record to FAILED with error message on exception.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    repo = SqlAlchemyFileRecordRepository(db_session)

    mock_port = AsyncMock()
    mock_port.convert_to_pdf.side_effect = RuntimeError(
        "Corrupted OLE compound document structure"
    )

    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        site_id="SITE-101",
        filename="corrupted.doc",
        mime_type="application/msword",
        size_bytes=512,
        object_key=f"tenant_1/STUDY-101/{file_id}/corrupted.doc",
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        conversion_status=ConversionStatus.PENDING,
        reason_for_change="Uploaded legacy doc",
    )
    await repo.save(record)
    await db_session.commit()

    worker = DocumentConversionWorker(file_repo=repo, conversion_port=mock_port)
    failed = await worker.convert_file(file_id)

    assert failed.conversion_status == ConversionStatus.FAILED
    assert failed.pdf_rendition_key is None
    assert "Corrupted OLE compound document" in (failed.conversion_error or "")


@pytest.mark.asyncio
async def test_complete_upload_auto_enqueues_conversion(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_crc: dict[str, str],
):
    """Verify complete_upload detects Office document MIME types and initiates conversion.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """

    async def _get_test_session() -> AsyncSession:
        return db_session

    app.dependency_overrides[get_db_session] = _get_test_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Allocate upload session for a DOCX file
        alloc_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-DOC-01",
                "filename": "clinical_investigator_brochure.docx",
                "mime_type": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "size_bytes": 12000,
                "reason_for_change": "Initial IB upload",
            },
            headers=auth_headers_crc,
        )
        assert alloc_resp.status_code == 201
        alloc_data = alloc_resp.json()
        file_id = alloc_data["file_id"]
        object_key = alloc_data["object_key"]

        # Simulate client PUT upload to object storage
        await mock_storage.put_object(
            key=object_key,
            data=b"PK\x03\x04[Mock Word Binary Data]",
            content_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        )

        # 2. Complete upload
        complete_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={
                "reason_for_change": "Completed upload and scan",
            },
            headers=auth_headers_crc,
        )
        assert complete_resp.status_code == 200
        completed_data = complete_resp.json()
        assert (
            completed_data["conversion_status"] == ConversionStatus.PENDING
            or completed_data["conversion_status"] == ConversionStatus.READY
        )


@pytest.mark.asyncio
async def test_download_and_preview_rendition_routing(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """Verify download-url and preview-url endpoints serve PDF rendition when READY and emit X-Rendition headers.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    repo = SqlAlchemyFileRecordRepository(db_session)

    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        site_id="SITE-101",
        filename="statistical_analysis_plan.docx",
        mime_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        size_bytes=81920,
        object_key=f"tenant_trial_01/STUDY-101/{file_id}/statistical_analysis_plan.docx",
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        conversion_status=ConversionStatus.READY,
        pdf_rendition_key=f"tenant_trial_01/STUDY-101/{file_id}/renditions/statistical_analysis_plan.pdf",
        reason_for_change="SAP document approved and converted",
    )
    await repo.save(record)
    await db_session.commit()

    async def _get_test_session() -> AsyncSession:
        return db_session

    app.dependency_overrides[get_db_session] = _get_test_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Default download: routes to PDF rendition
        resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["is_rendition"] is True
        assert data["mime_type"] == "application/pdf"
        assert data["filename"] == "statistical_analysis_plan.pdf"
        assert resp.headers.get("X-Rendition") == "pdf"

        # 2. Original download (with ?original=true): routes to original Word file
        orig_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url?original=true",
            headers=auth_headers_crc,
        )
        assert orig_resp.status_code == 200
        orig_data = orig_resp.json()
        assert orig_data["is_rendition"] is False
        assert (
            orig_data["mime_type"]
            == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        )
        assert orig_data["filename"] == "statistical_analysis_plan.docx"
        assert orig_resp.headers.get("X-Rendition") == "original"

        # 3. Preview URL endpoint: routes to PDF rendition
        prev_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/preview-url",
            headers=auth_headers_crc,
        )
        assert prev_resp.status_code == 200
        prev_data = prev_resp.json()
        assert prev_data["is_rendition"] is True
        assert prev_data["mime_type"] == "application/pdf"
        assert prev_resp.headers.get("X-Rendition") == "pdf"


@pytest.mark.asyncio
async def test_pending_conversion_returns_original_with_header(
    db_session: AsyncSession,
    auth_headers_crc: dict[str, str],
):
    """Verify download endpoint returns original document and X-Rendition: original when conversion is still PENDING.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    repo = SqlAlchemyFileRecordRepository(db_session)

    file_id = str(uuid.uuid4())
    record = FileRecord(
        id=file_id,
        study_id="STUDY-101",
        site_id="SITE-101",
        filename="budget_sheet.xlsx",
        mime_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        size_bytes=24000,
        object_key=f"tenant_trial_01/STUDY-101/{file_id}/budget_sheet.xlsx",
        uploaded_by="crc_user_01",
        created_by="crc_user_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        conversion_status=ConversionStatus.PENDING,
        pdf_rendition_key=None,
        reason_for_change="Uploaded budget spreadsheet",
    )
    await repo.save(record)
    await db_session.commit()

    async def _get_test_session() -> AsyncSession:
        return db_session

    app.dependency_overrides[get_db_session] = _get_test_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_crc,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["is_rendition"] is False
        assert (
            data["mime_type"]
            == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        )
        assert data["filename"] == "budget_sheet.xlsx"
        assert resp.headers.get("X-Rendition") == "original"
