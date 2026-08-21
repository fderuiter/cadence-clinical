"""Unit and integration test suite for eTMF / eISF storage migration and dual-read fallback.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import base64
import hashlib
import tempfile
from unittest.mock import AsyncMock

import pytest
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from apps.etmf.infrastructure.models import Base, TMFDocument
from apps.etmf.storage import get_document_bytes
from scripts.migrate_etmf_blobs_to_s3 import migrate_documents


@pytest.mark.asyncio
async def test_dual_read_fallback_legacy_content():
    """Verify dual-read fallback decodes legacy in-database base64 _content when object_key is None.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    raw_pdf_bytes = b"%PDF-1.4 Mock PDF Content For Dual Read"
    b64_content = base64.b64encode(raw_pdf_bytes).decode("utf-8")

    doc = TMFDocument(
        id="legacy-doc-1",
        study_id="STUDY-101",
        zone=1,
        section="01.01",
        artifact_type="Protocol",
        filename="protocol_v1.pdf",
        object_key=None,
        _content=b64_content,
        mime_type="application/pdf",
        created_by="crc.user",
        version_index=1,
    )

    result_bytes = await get_document_bytes(doc)
    assert result_bytes == raw_pdf_bytes


@pytest.mark.asyncio
async def test_dual_read_object_storage():
    """Verify dual-read fetches bytes from StoragePort when object_key is populated.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    raw_content = b"Modern binary stored directly in S3/MinIO bucket"
    doc_key = "etmf/STUDY-101/modern-doc-2/site_log.pdf"

    mock_storage = AsyncMock()
    mock_storage.get_object.return_value = (
        raw_content,
        hashlib.sha256(raw_content).hexdigest(),
    )

    doc = TMFDocument(
        id="modern-doc-2",
        study_id="STUDY-101",
        zone=2,
        section="02.01",
        artifact_type="Delegation of Authority Log",
        filename="site_log.pdf",
        object_key=doc_key,
        _content=None,
        mime_type="application/pdf",
        created_by="pi.user",
        version_index=1,
    )

    result_bytes = await get_document_bytes(doc, storage=mock_storage)
    assert result_bytes == raw_content
    mock_storage.get_object.assert_called_once_with(doc_key)


@pytest.mark.asyncio
async def test_migrate_etmf_blobs_to_s3_script_lifecycle():
    """Verify data migration script moves legacy database blobs to StoragePort idempotently.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    @req:PRD-DOC-003
    """
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as tmp_db:
        db_path = tmp_db.name

    db_url = f"sqlite+aiosqlite:///{db_path}"
    engine = create_async_engine(db_url, echo=False)

    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    session_maker = async_sessionmaker(
        engine, expire_on_commit=False, class_=AsyncSession
    )

    payload_1 = b"%PDF-1.4 Trial Document 1"
    payload_2 = b"%PDF-1.4 Trial Document 2"

    async with session_maker() as session:
        doc1 = TMFDocument(
            id="doc-uuid-1",
            study_id="STUDY-MIGRATE",
            zone=1,
            section="01.01",
            artifact_type="Protocol Document",
            filename="protocol.pdf",
            object_key=None,
            _content=base64.b64encode(payload_1).decode("utf-8"),
            mime_type="application/pdf",
            created_by="system",
            version_index=1,
        )
        doc2 = TMFDocument(
            id="doc-uuid-2",
            study_id="STUDY-MIGRATE",
            zone=1,
            section="01.01",
            artifact_type="Investigator Brochure",
            filename="ib.pdf",
            object_key=None,
            _content=base64.b64encode(payload_2).decode("utf-8"),
            mime_type="application/pdf",
            created_by="system",
            version_index=1,
        )
        session.add_all([doc1, doc2])
        await session.commit()

    mock_storage = AsyncMock()
    mock_storage.put_object.return_value = "mock_etag_or_hash"

    # 1. Dry Run Verification
    dry_stats = await migrate_documents(
        db_url=db_url,
        batch_size=10,
        dry_run=True,
        storage=mock_storage,
    )
    assert dry_stats["etmf_migrated"] == 2
    mock_storage.put_object.assert_not_called()

    # Verify DB rows are still unmigrated
    async with session_maker() as session:
        docs = (await session.execute(TMFDocument.__table__.select())).fetchall()
        for row in docs:
            assert row.object_key is None
            assert row.content is not None

    # 2. Live Migration Run
    live_stats = await migrate_documents(
        db_url=db_url,
        batch_size=10,
        dry_run=False,
        storage=mock_storage,
    )
    assert live_stats["etmf_migrated"] == 2
    assert live_stats["errors"] == 0
    assert mock_storage.put_object.call_count == 2

    # Verify DB rows now have object_key and content is None
    async with session_maker() as session:
        docs = (await session.execute(TMFDocument.__table__.select())).fetchall()
        for row in docs:
            assert row.object_key is not None
            assert row.object_key.startswith("etmf/STUDY-MIGRATE/")
            assert row.content is None

    # 3. Idempotency Check (Second run should find 0 unmigrated rows)
    second_run_stats = await migrate_documents(
        db_url=db_url,
        batch_size=10,
        dry_run=False,
        storage=mock_storage,
    )
    assert second_run_stats["etmf_migrated"] == 0

    await engine.dispose()
