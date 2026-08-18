"""Unit tests directly targeting decoupled Application Layer Use Cases."""

import pytest
import pytest_asyncio

from apps.etmf.adapters.database import db_manager
from apps.etmf.adapters.models import Base, ExpectedDocument
from apps.etmf.adapters.repositories import SQLETMFRepository
from apps.etmf.application.use_cases import (
    BulkArchiveStudyUseCase,
    CompletenessInspectionUseCase,
    ElectronicSignatureUseCase,
    ExportRegulatoryBinderUseCase,
    ExportTmfEmsUseCase,
    IngestDocumentUseCase,
    InspectionReadinessUseCase,
    QCWorkflowUseCase,
    RedactionUseCase,
    VerifyAuditLedgerChainUseCase,
)
from packages.deid.models import ComplianceProfile
from packages.security.signature import SigningReason


@pytest_asyncio.fixture(autouse=True)
async def setup_db():
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_ingest_and_qc_use_cases():
    """Test IngestDocumentUseCase and QCWorkflowUseCase."""
    async with db_manager.get_session_maker()() as session:
        repo = SQLETMFRepository(session)

        # Ingest
        ingest_uc = IngestDocumentUseCase(repo)
        doc = await ingest_uc.execute(
            study_id="STUDY-UC-1",
            artifact_type="Clinical Trial Protocol",
            filename="protocol.pdf",
            content="Protocol text for use case test.",
            mime_type="application/pdf",
            created_by="crc_user",
            created_role="site_crc",
        )
        assert doc.id is not None
        assert doc.zone == 1
        assert doc.section == "01.01"

        # Transition QC
        qc_uc = QCWorkflowUseCase(repo)
        transition = await qc_uc.transition(
            document_id=doc.id,
            to_status="TECHNICAL_QC",
            actor_id="dm_user",
            actor_role="data_manager",
            reason_for_change="Initial submission for QC review",
        )
        assert transition.from_status == "DRAFT"
        assert transition.to_status == "TECHNICAL_QC"


@pytest.mark.asyncio
async def test_electronic_signature_and_redaction_use_cases():
    """Test ElectronicSignatureUseCase and RedactionUseCase."""
    async with db_manager.get_session_maker()() as session:
        repo = SQLETMFRepository(session)

        # Ingest
        ingest_uc = IngestDocumentUseCase(repo)
        doc = await ingest_uc.execute(
            study_id="STUDY-UC-2",
            artifact_type="Investigator's Brochure",
            filename="ib.pdf",
            content="Investigator John Doe SSN: 123-45-6789 studied cardio medicine.",
            mime_type="application/pdf",
            created_by="author_user",
            created_role="sponsor_designer",
        )

        # Sign
        sig_uc = ElectronicSignatureUseCase(repo)
        signed_doc = await sig_uc.sign_document(
            document_id=doc.id,
            signer_id="dr_smith",
            signer_role="principal_investigator",
            signing_reason=SigningReason.APPROVAL,
        )
        assert signed_doc.status == "SIGNED"
        assert signed_doc.approval_status == "APPROVED"

        # Verify Signature
        v_result = await sig_uc.verify_signature(doc.id)
        assert v_result["is_valid"] is True
        assert v_result["signer"] == "dr_smith"

        # Automated Redaction
        redact_uc = RedactionUseCase(repo)
        redacted_doc, counts, manifest = await redact_uc.execute_automated(
            document_id=doc.id,
            profile=ComplianceProfile.HIPAA,
            custom_terms=None,
            strategies=None,
            redacted_filename="ib_redacted.pdf",
            actor_id="privacy_officer",
            actor_role="data_manager",
            reason_for_change="Redacted SSN and names under HIPAA",
        )
        assert redacted_doc.is_redacted is True
        assert redacted_doc.redaction_source_id == doc.id
        assert manifest is not None


@pytest.mark.asyncio
async def test_completeness_readiness_and_export_use_cases():
    """Test CompletenessInspectionUseCase, InspectionReadinessUseCase, and Export Use Cases."""
    async with db_manager.get_session_maker()() as session:
        repo = SQLETMFRepository(session)

        # Ingest doc
        ingest_uc = IngestDocumentUseCase(repo)
        await ingest_uc.execute(
            study_id="STUDY-UC-3",
            artifact_type="Clinical Trial Protocol",
            filename="protocol.pdf",
            content="Protocol content for completeness.",
            mime_type="application/pdf",
            created_by="author_user",
            created_role="sponsor_designer",
        )

        # Add Expected document
        edl = ExpectedDocument(
            study_id="STUDY-UC-3",
            milestone="INITIATION",
            artifact_type="Clinical Trial Protocol",
            zone=1,
            section="01.01",
            created_by="system",
            reason_for_change="Baseline EDL",
            version_index=1,
        )
        await repo.save_expected_document(edl)
        await session.flush()

        # Completeness Use Case
        comp_uc = CompletenessInspectionUseCase(repo)
        comp_res = await comp_uc.evaluate_completeness(
            study_id="STUDY-UC-3", milestone="INITIATION"
        )
        assert comp_res["is_complete"] is True
        assert "Clinical Trial Protocol" in comp_res["present_artifacts"]

        # Readiness Use Case
        readiness_uc = InspectionReadinessUseCase(repo)
        readiness_report = await readiness_uc.evaluate_readiness("STUDY-UC-3")
        assert readiness_report.study_id == "STUDY-UC-3"
        assert readiness_report.total_documents == 1

        # Binder Export Use Case
        binder_uc = ExportRegulatoryBinderUseCase(repo)
        binder_bytes = await binder_uc.export_zip(
            study_id="STUDY-UC-3",
            include_history=True,
            requester_id="auditor_user",
            requester_role="regulatory_inspector",
        )
        assert len(binder_bytes) > 0

        # EMS Export Use Case
        ems_uc = ExportTmfEmsUseCase(repo)
        ems_bytes = await ems_uc.export_package(
            study_id="STUDY-UC-3",
            study_title="Complete Study 3",
            requester_id="auditor_user",
            requester_role="regulatory_inspector",
        )
        assert len(ems_bytes) > 0

        # Verify Chain Use Case
        chain_uc = VerifyAuditLedgerChainUseCase(repo)
        chain_res = await chain_uc.verify_chain()
        assert chain_res["is_valid"] is True


@pytest.mark.asyncio
async def test_bulk_archive_use_case():
    """Test BulkArchiveStudyUseCase atomic transitions."""
    async with db_manager.get_session_maker()() as session:
        repo = SQLETMFRepository(session)

        # Ingest and approve 2 docs
        ingest_uc = IngestDocumentUseCase(repo)
        doc1 = await ingest_uc.execute(
            study_id="STUDY-UC-4",
            artifact_type="Clinical Trial Protocol",
            filename="protocol.pdf",
            content="Protocol content",
            mime_type="application/pdf",
            created_by="crc",
            created_role="site_crc",
        )
        doc2 = await ingest_uc.execute(
            study_id="STUDY-UC-4",
            artifact_type="Investigator's Brochure",
            filename="ib.pdf",
            content="IB content",
            mime_type="application/pdf",
            created_by="crc",
            created_role="site_crc",
        )

        qc_uc = QCWorkflowUseCase(repo)
        await qc_uc.transition(
            doc1.id,
            "TECHNICAL_QC",
            "qc_user",
            "data_manager",
            "Initial technical QC review submission",
        )
        await qc_uc.transition(
            doc1.id,
            "CLINICAL_QC",
            "clinical_lead",
            "sponsor_clinical",
            "Clinical QC review and inspection passed",
        )
        await qc_uc.transition(
            doc1.id,
            "APPROVED",
            "approver",
            "sponsor_dm",
            "Final approval of clinical protocol",
        )

        await qc_uc.transition(
            doc2.id,
            "TECHNICAL_QC",
            "qc_user",
            "data_manager",
            "Initial technical QC review submission",
        )
        await qc_uc.transition(
            doc2.id,
            "CLINICAL_QC",
            "clinical_lead",
            "sponsor_clinical",
            "Clinical QC review and inspection passed",
        )
        await qc_uc.transition(
            doc2.id,
            "APPROVED",
            "approver",
            "sponsor_dm",
            "Final approval of investigator brochure",
        )

        # Bulk archive
        archive_uc = BulkArchiveStudyUseCase(repo)
        res = await archive_uc.bulk_archive(
            study_id="STUDY-UC-4",
            actor_id="archivist",
            actor_role="admin",
            reason_for_change="Study closeout final archiving",
        )
        assert res["status"] == "SUCCESS"
        assert res["successful_count"] == 2
