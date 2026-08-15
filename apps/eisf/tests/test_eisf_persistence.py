import os

import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.eisf.database import db_manager
from apps.eisf.models import Base, ISFAuditLog, ISFDocument
from packages.database import RelationalDatabaseManager


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    """
    Setup in-memory eISF database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def test_uninitialized_database_manager_eisf():
    """
    Ensure the EISF database manager raises an exception when accessed before initialization.
    """
    mgr = RelationalDatabaseManager(service_name="eISF")
    with pytest.raises(Exception) as exc_info:
        mgr.get_session_maker()
    assert "eISF database session manager is not initialized" in str(exc_info.value)


@pytest.mark.asyncio
async def test_database_url_override_and_init(monkeypatch):
    """
    Verify that database lifecycle supports EISF_DATABASE_URL override.
    """
    monkeypatch.setenv("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
    mgr = RelationalDatabaseManager(service_name="eISF")
    db_url = os.environ.get("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")
    mgr.init_db(db_url)  # Should pick up EISF_DATABASE_URL from env
    assert mgr.engine is not None
    assert mgr.session_maker is not None
    await mgr.close()


@pytest.mark.asyncio
async def test_eisf_document_creation_and_site_scoped():
    """
    Verify that every document is site-scoped and binder-classified, and that
    all required fields are correctly persisted and retrieved.
    """
    async with db_manager.get_session_maker()() as session:
        # Create a site-scoped, binder-classified document
        doc = ISFDocument(
            study_id="study-001",
            site_id="site-boston-01",
            binder_classification="Investigator CVs",
            filename="cv_dr_smith.pdf",
            content="Base64EncodedContentOrTextRepresentation",
            mime_type="application/pdf",
            version_index=1,
            created_by="user-admin-99",
            metadata_json={"academic_degree": "MD", "specialty": "Oncology"},
            correlation_key="ext-corr-cv-101",
            content_checksum="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",  # pragma: allowlist secret
            sync_status="PENDING",
            source_system="eISF",
        )
        session.add(doc)
        await session.commit()

        # Retrieve and verify
        stmt = select(ISFDocument).where(ISFDocument.id == doc.id)
        result = await session.execute(stmt)
        retrieved_doc = result.scalar_one()

        assert retrieved_doc.study_id == "study-001"
        assert retrieved_doc.site_id == "site-boston-01"  # Site-scoped
        assert (
            retrieved_doc.binder_classification == "Investigator CVs"
        )  # Binder-classified
        assert retrieved_doc.filename == "cv_dr_smith.pdf"
        assert retrieved_doc.content == "Base64EncodedContentOrTextRepresentation"
        assert retrieved_doc.mime_type == "application/pdf"
        assert retrieved_doc.version_index == 1
        assert retrieved_doc.created_by == "user-admin-99"
        assert retrieved_doc.created_at is not None
        assert retrieved_doc.metadata_json == {
            "academic_degree": "MD",
            "specialty": "Oncology",
        }
        assert retrieved_doc.correlation_key == "ext-corr-cv-101"
        assert (
            retrieved_doc.content_checksum
            == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"  # pragma: allowlist secret
        )
        assert retrieved_doc.sync_status == "PENDING"
        assert retrieved_doc.source_system == "eISF"


@pytest.mark.asyncio
async def test_eisf_part11_audit_log_retention():
    """
    Verify that Part 11 audit records correctly retain actor, role, timestamp,
    action, document reference, details, and reason for change.
    """
    async with db_manager.get_session_maker()() as session:
        audit_rec = ISFAuditLog(
            actor_id="user-cra-04",
            actor_role="CRA",
            action="CREATE_DOCUMENT",
            document_id="some-doc-uuid",
            details="Successfully ingested Investigator CV for Dr. Smith.",
            reason_for_change="Initial onboarding of Investigator Dr. Smith.",
        )
        session.add(audit_rec)
        await session.commit()

        # Retrieve and verify
        stmt = select(ISFAuditLog).where(ISFAuditLog.id == audit_rec.id)
        result = await session.execute(stmt)
        retrieved_audit = result.scalar_one()

        assert retrieved_audit.actor_id == "user-cra-04"
        assert retrieved_audit.actor_role == "CRA"
        assert retrieved_audit.action == "CREATE_DOCUMENT"
        assert retrieved_audit.document_id == "some-doc-uuid"
        assert (
            retrieved_audit.details
            == "Successfully ingested Investigator CV for Dr. Smith."
        )
        assert (
            retrieved_audit.reason_for_change
            == "Initial onboarding of Investigator Dr. Smith."
        )
        assert retrieved_audit.timestamp is not None


@pytest.mark.asyncio
async def test_eisf_append_only_versions_and_deduplication():
    """
    Verify that the data model supports append-only document versions and later
    deduplication/synchronization via content checksums and correlation keys.
    """
    async with db_manager.get_session_maker()() as session:
        # Ingest Version 1
        doc_v1 = ISFDocument(
            study_id="study-100",
            site_id="site-london-02",
            binder_classification="Lab Accreditations",
            filename="lab_accreditation_2026.pdf",
            content="Version 1 Content",
            mime_type="application/pdf",
            version_index=1,
            created_by="user-monitor-02",
            correlation_key="sync-id-lab-acc-london-02",
            content_checksum="checksum-v1-hash",
            sync_status="SYNCED",
            source_system="eISF",
        )
        session.add(doc_v1)

        # Ingest Version 2 (Append-only style)
        doc_v2 = ISFDocument(
            study_id="study-100",
            site_id="site-london-02",
            binder_classification="Lab Accreditations",
            filename="lab_accreditation_2026_updated.pdf",
            content="Version 2 Content (updated signatures)",
            mime_type="application/pdf",
            version_index=2,  # Incremented version index
            created_by="user-monitor-02",
            correlation_key="sync-id-lab-acc-london-02",  # Same correlation key to relate them
            content_checksum="checksum-v2-hash",
            sync_status="PENDING",
            source_system="eISF",
        )
        session.add(doc_v2)
        await session.commit()

        # Query and assert we have both versions, sorted chronologically
        stmt = (
            select(ISFDocument)
            .where(
                ISFDocument.study_id == "study-100",
                ISFDocument.site_id == "site-london-02",
                ISFDocument.correlation_key == "sync-id-lab-acc-london-02",
            )
            .order_by(ISFDocument.version_index.asc())
        )
        result = await session.execute(stmt)
        docs = result.scalars().all()

        assert len(docs) == 2
        assert docs[0].version_index == 1
        assert docs[0].content_checksum == "checksum-v1-hash"
        assert docs[0].sync_status == "SYNCED"

        assert docs[1].version_index == 2
        assert docs[1].content_checksum == "checksum-v2-hash"
        assert docs[1].sync_status == "PENDING"
