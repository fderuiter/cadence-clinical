"""Unit and integration tests for eISF regulatory binder document taxonomy and versioning.

Requirements: PRD-SYS-001
"""

import os
from datetime import datetime

import pytest
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession, create_async_engine

from apps.eisf.database.migrate import run_migrations
from apps.eisf.models import (
    STANDARD_EISF_SECTIONS,
    EISFDocumentRecord,
    EISFSectionTaxonomy,
)


def test_eisf_document_record_instantiation_defaults() -> None:
    """Test that instantiating EISFDocumentRecord sets default version_index and status.

    Requirements: PRD-SYS-001
    """
    doc = EISFDocumentRecord(
        id="doc-test-123",
        site_id="site-123",
        study_id="study-123",
        section_code="01.01",
        filename="test_file.pdf",
        file_path="/files/test_file.pdf",
        sha256_checksum="abc123sha256",
        created_by="user-admin",
    )

    # Validate defaults
    assert doc.id == "doc-test-123"
    assert doc.version_index == 1
    assert doc.status == "DRAFT"
    assert doc.version_major == 1
    assert doc.version_minor == 0
    assert doc.reason_for_change == "Initial Document Ingestion"
    assert doc.is_active is True
    assert doc.is_deleted is False
    assert isinstance(doc.created_at, datetime)


@pytest.mark.asyncio
async def test_eisf_taxonomy_querying_returns_8_mandatory_sections() -> None:
    """Test that running migrations/seeding and querying the taxonomy returns all 8 mandatory sections.

    Requirements: PRD-SYS-001
    """
    db_file = "test_taxonomy.db"
    db_url = f"sqlite+aiosqlite:///{db_file}"

    # Ensure clean start
    if os.path.exists(db_file):
        os.remove(db_file)

    try:
        # Run pre-boot database migrations which seeds the taxonomy
        await run_migrations(db_url)

        # Verify the database contains all 8 mandatory sections
        engine = create_async_engine(db_url, echo=False)
        async with AsyncSession(engine) as session:
            res = await session.execute(select(EISFSectionTaxonomy))
            sections = res.scalars().all()

            # Verify count is exactly 8
            assert len(sections) == 8

            # Build dictionary mapping code to section
            section_map = {sec.section_code: sec for sec in sections}

            # Verify specific section details
            for sec_data in STANDARD_EISF_SECTIONS:
                code = sec_data["section_code"]
                assert code in section_map
                db_sec = section_map[code]
                assert db_sec.section_number == sec_data["section_number"]
                assert db_sec.title == sec_data["title"]
                assert db_sec.is_mandatory is True

        await engine.dispose()
    finally:
        # Clean up database file
        if os.path.exists(db_file):
            os.remove(db_file)
