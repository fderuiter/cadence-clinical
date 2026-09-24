"""Unit and integration tests for multimodal eTMF/eISF document intelligence, DIA classifier, and CRA QC staging.

@req:PRD-TMF-006
"""

from datetime import UTC, date, datetime
from typing import Any

import fitz
import pytest
from httpx import ASGITransport, AsyncClient

from apps.etmf.application.document_intelligence_use_case import (
    CRAQCReviewUseCase,
    GetQCQueueUseCase,
    StageClassifiedArtifactUseCase,
)
from apps.etmf.domain.intelligence_models import (
    ClassificationConfidenceTier,
    DocumentModality,
    QCRecommendation,
    SignaturePresenceStatus,
)
from apps.etmf.domain.services.dia_classifier import (
    DIAReferenceModelClassifier,
)
from apps.etmf.domain.services.document_intelligence_parser import (
    DocumentIntelligenceParser,
)
from apps.etmf.domain.services.metadata_extractor import (
    RegulatoryMetadataExtractor,
)
from apps.etmf.domain.services.signature_analyzer import (
    SignatureCompletenessAnalyzer,
)
from apps.etmf.main import app
from apps.etmf.workers.document_intelligence_worker import (
    DocumentIntelligenceWorker,
)
from packages.testing.security import create_test_auth_headers


class FakeETMFIntelligenceRepository:
    """In-memory mock repository for eTMF document intelligence tests."""

    def __init__(self) -> None:
        self.documents = {}
        self.audit_logs = []
        self.qc_transitions = []

    async def get_max_version_index(
        self, study_id: str, site_id: str | None, artifact_code: str
    ) -> int:
        versions = [
            d.version_index
            for d in self.documents.values()
            if d.study_id == study_id
            and d.site_id == site_id
            and d.artifact_code == artifact_code
        ]
        return max(versions) if versions else 0

    async def create_document(self, **kwargs) -> Any:
        from apps.etmf.adapters.models import TMFDocument

        doc = TMFDocument(**kwargs)
        doc.id = f"doc_{len(self.documents) + 1}"
        doc.created_at = datetime.now(UTC)
        self.documents[doc.id] = doc
        return doc

    async def get_document_by_id(self, doc_id: str) -> Any:
        return self.documents.get(doc_id)

    async def save_document(self, doc: Any) -> Any:
        self.documents[doc.id] = doc
        return doc

    async def get_documents_by_study(self, study_id: str) -> list[Any]:
        return [d for d in self.documents.values() if d.study_id == study_id]

    async def get_documents_filtered(self, *args, **kwargs) -> list[Any]:
        return list(self.documents.values())

    async def create_audit_log(self, **kwargs) -> Any:
        self.audit_logs.append(kwargs)
        return kwargs

    async def create_qc_transition(self, **kwargs) -> Any:
        self.qc_transitions.append(kwargs)
        return kwargs


@pytest.mark.asyncio
async def test_multimodal_layout_parser_text_and_pdf():
    """Verify parser extracts layout blocks, OMB numbers, key-values, and signature anchors.

    @req:PRD-TMF-006
    """
    raw_content = """
    DEPARTMENT OF HEALTH AND HUMAN SERVICES
    Food and Drug Administration
    STATEMENT OF INVESTIGATOR (FDA Form 1572)
    OMB No. 0910-0014 Expiration Date: 2027-12-31

    Protocol Number: CAD-ONC-2026
    Site Number: SITE-101
    Principal Investigator: Dr. Sarah Connor, MD
    Date of Issuance: 2026-03-15

    Investigator Signature: /s/ Dr. Sarah Connor
    """

    parser = DocumentIntelligenceParser()
    parsed = parser.parse(
        raw_content, filename="fda_1572_site101.pdf", mime_type="application/pdf"
    )

    assert parsed.modality == DocumentModality.PDF_BINARY
    assert "0910-0014" in parsed.detected_omb_numbers
    assert "FDA_FORM_1572" in parsed.detected_form_markers
    assert parsed.detected_key_values.get("protocol_number") == "CAD-ONC-2026"
    assert parsed.detected_key_values.get("site_id") == "SITE-101"
    assert "Dr. Sarah Connor" in parsed.detected_key_values.get("investigator_name", "")


@pytest.mark.asyncio
async def test_multimodal_layout_parser_pymupdf_blocks_and_bounding_boxes():
    """Verify PyMuPDF binary PDF parser extracts visual bounding boxes and text blocks.

    @req:PRD-TMF-006
    """
    # Create in-memory binary PDF using PyMuPDF (fitz)
    doc = fitz.open()
    page = doc.new_page(width=595, height=842)
    p1 = fitz.Point(50, 72)
    page.insert_text(
        p1, "STATEMENT OF INVESTIGATOR (FDA Form 1572) OMB 0910-0014", fontsize=12
    )
    p2 = fitz.Point(50, 120)
    page.insert_text(p2, "Protocol Number: STUDY-ONC-99 Site ID: SITE-01", fontsize=10)
    p3 = fitz.Point(50, 160)
    page.insert_text(p3, "Investigator Signature: /s/ Dr. Gregory House", fontsize=10)

    pdf_bytes = doc.tobytes()
    doc.close()

    parser = DocumentIntelligenceParser()
    parsed = parser.parse(
        pdf_bytes, filename="form1572_binary.pdf", mime_type="application/pdf"
    )

    assert parsed.modality == DocumentModality.PDF_BINARY
    assert len(parsed.layout_elements) >= 3

    # Verify visual bounding boxes are extracted with float coordinates [x0, y0, x1, y1]
    bbox_elements = [el for el in parsed.layout_elements if el.bbox is not None]
    assert len(bbox_elements) >= 1
    first_bbox = bbox_elements[0].bbox
    assert isinstance(first_bbox, list)
    assert len(first_bbox) == 4
    assert first_bbox[0] >= 0.0 and first_bbox[2] > first_bbox[0]


@pytest.mark.asyncio
async def test_dia_reference_model_classifier_multi_signal():
    """Validate multi-signal DIA classifier accurately ranks standard and extension artifacts.

    @req:PRD-TMF-006
    """
    parser = DocumentIntelligenceParser()
    classifier = DIAReferenceModelClassifier(default_catalog_version="v3.2.0-extended")

    # 1. Test FDA Form 1572
    payload_1572 = parser.parse(
        "STATEMENT OF INVESTIGATOR FDA 1572 OMB No. 0910-0014",
        filename="Form1572.pdf",
    )
    primary, alts, conf, rec, eisf_map = classifier.classify(
        payload_1572, filename="Form1572.pdf"
    )
    assert primary.artifact_code == "05.02.01"
    assert primary.confidence >= 0.85
    assert conf == ClassificationConfidenceTier.HIGH
    assert rec == QCRecommendation.AUTO_CLASSIFY
    assert eisf_map is not None
    assert eisf_map["eisf_section"] == "04_REGULATORY"

    # 2. Test Medical License (Extension Code 05.02.98)
    payload_license = parser.parse(
        "State Medical Board Physician License Certification for Dr. Smith",
        filename="medical_license_smith.pdf",
    )
    primary, _, conf, _, eisf_map = classifier.classify(
        payload_license,
        filename="medical_license_smith.pdf",
        taxonomy_version="v3.2.0-extended",
    )
    assert primary.artifact_code == "05.02.98"
    assert primary.is_extension is True

    # 3. Test Delegation of Authority Log
    payload_doa = parser.parse(
        "Site Responsibility and Delegation of Authority Log (DOA Log)",
        filename="doa_log_2026.pdf",
    )
    primary, _, conf, _, _ = classifier.classify(
        payload_doa, filename="doa_log_2026.pdf"
    )
    assert primary.artifact_code == "05.02.04"
    assert conf == ClassificationConfidenceTier.HIGH


@pytest.mark.asyncio
async def test_dia_classifier_benchmark_standard_regulatory_documents():
    """Benchmark test verifying standard regulatory documents classify with >90% confidence.

    @req:PRD-TMF-006
    """
    parser = DocumentIntelligenceParser()
    classifier = DIAReferenceModelClassifier(default_catalog_version="v3.2.0-extended")

    test_corpus = [
        # (Content, Filename, Expected Artifact Code, Expected Name)
        (
            "STATEMENT OF INVESTIGATOR (FDA Form 1572) OMB 0910-0014",
            "FDA_1572.pdf",
            "05.02.01",
            "FDA Form 1572",
        ),
        (
            "Financial Disclosure Form (FDA 3454) OMB 0910-0396",
            "Financial_Disclosure.pdf",
            "05.02.02",
            "Financial Disclosure",
        ),
        (
            "Curriculum Vitae Dr. Emmet Brown MD Principal Investigator",
            "Investigator_CV.pdf",
            "05.02.03",
            "Investigator CV",
        ),
        (
            "Delegation of Authority and Site Responsibility Log",
            "DOA_Log.pdf",
            "05.02.04",
            "Delegation of Authority Log",
        ),
        (
            "IRB Approval Letter Institutional Review Board Ethics Clearance",
            "IRB_Approval_Notice.pdf",
            "04.01.01",
            "IRB or IEC Approval",
        ),
        (
            "State Medical Board Physician License Certification",
            "Medical_License_Dr_Jones.pdf",
            "05.02.98",
            "Medical License",
        ),
        (
            "Protocol Signature and Sign-off Approval Page",
            "Protocol_Signoff.pdf",
            "01.01.03",
            "Protocol Signature Page",
        ),
        (
            "Investigator's Brochure IB Edition 4 Clinical Drug Overview",
            "Investigator_Brochure.pdf",
            "02.01.01",
            "Investigator's Brochure",
        ),
        (
            "Informed Consent Form ICF Subject Information Sheet Version 2.0",
            "Informed_Consent_Form.pdf",
            "05.02.05",
            "Informed Consent Form",
        ),
        (
            "Central Laboratory Certificate CAP CLIA Accreditation",
            "Lab_Certificate.pdf",
            "08.01.01",
            "Laboratory Accreditation / Certification",
        ),
        (
            "Statistical Analysis Plan SAP Version 1.0 Final",
            "Statistical_Analysis_Plan.pdf",
            "11.01.01",
            "Statistical Analysis Plan",
        ),
        (
            "Clinical Study Report CSR Final Study Outcomes Summary",
            "Clinical_Study_Report.pdf",
            "11.02.01",
            "Clinical Study Report",
        ),
    ]

    for content, fn, expected_code, expected_name in test_corpus:
        parsed = parser.parse(content, filename=fn)
        primary, _, conf_tier, rec, _ = classifier.classify(parsed, filename=fn)

        assert primary.artifact_code == expected_code, (
            f"Failed for {fn}: got {primary.artifact_code}, expected {expected_code}"
        )
        assert primary.confidence >= 0.90, (
            f"Confidence below 90% for {fn}: {primary.confidence}"
        )
        assert conf_tier == ClassificationConfidenceTier.HIGH
        assert rec == QCRecommendation.AUTO_CLASSIFY


@pytest.mark.asyncio
async def test_regulatory_metadata_and_dates_extraction():
    """Verify accurate extraction of protocol ID, site ID, dates, and form identifiers.

    @req:PRD-TMF-006
    """
    raw_text = """
    Financial Disclosure Form (FDA 3454)
    OMB No. 0910-0396
    Protocol ID: CAD-2026-PH3
    Site Number: SITE-777
    Principal Investigator: Dr. John Watson
    Date: 2026-06-01
    Expiration Date: 2027-06-01
    """
    parser = DocumentIntelligenceParser()
    extractor = RegulatoryMetadataExtractor()

    parsed = parser.parse(raw_text, filename="fin_disclosure.pdf")
    meta = extractor.extract(parsed)

    assert meta.protocol_number == "CAD-2026-PH3"
    assert meta.site_id == "SITE-777"
    assert meta.investigator_name == "Dr. John Watson"
    assert meta.issue_date == date(2026, 6, 1)
    assert meta.expiration_date == date(2027, 6, 1)
    assert meta.form_identifier == "OMB-0910-0396"


@pytest.mark.asyncio
async def test_relational_envelope_population_and_persistence():
    """Verify extracted dates and investigator metadata populate the relational envelope columns.

    @req:PRD-TMF-006
    """
    repo = FakeETMFIntelligenceRepository()
    stage_use_case = StageClassifiedArtifactUseCase(repo)

    doc_content = """
    Financial Disclosure Form (FDA 3454)
    OMB 0910-0396
    Protocol Number: STUDY-ALPHA
    Site Number: SITE-88
    Principal Investigator: Dr. Lisa Cuddy
    Date of Issuance: 2026-04-10
    Expiration Date: 2027-04-10
    """

    doc, report = await stage_use_case.execute(
        content=doc_content,
        filename="cuddy_financial_disclosure.pdf",
        mime_type="application/pdf",
        study_id="STUDY-ALPHA",
        actor_id="crc.user",
        actor_role="site_crc",
        reason_for_change="Initial regulatory upload",
        site_id="SITE-88",
    )

    # Verify relational envelope attributes directly on TMFDocument model
    assert doc.status == "TECHNICAL_QC"
    assert doc.artifact_code == "05.02.02"
    assert doc.issue_date == date(2026, 4, 10)
    assert doc.expiration_date is not None
    assert doc.expiration_date.year == 2027
    assert doc.expiration_date.month == 4
    assert doc.expiration_date.day == 10
    assert doc.document_owner_id == "Dr. Lisa Cuddy"
    assert doc.metadata_json["ai_staging_status"] == "DRAFT_AI"
    assert doc.metadata_json["confidence_score"] >= 0.90


@pytest.mark.asyncio
async def test_signature_completeness_analyzer():
    """Validate signature detection and missing signature flagging.

    @req:PRD-TMF-006
    """
    parser = DocumentIntelligenceParser()
    analyzer = SignatureCompletenessAnalyzer()

    # Case A: Fully signed FDA 1572
    signed_text = """
    STATEMENT OF INVESTIGATOR (FDA Form 1572)
    Investigator Signature: /s/ Dr. Robert Oppenheimer
    """
    parsed_signed = parser.parse(signed_text, filename="1572.pdf")
    res_signed = analyzer.analyze(parsed_signed, artifact_code="05.02.01")
    assert res_signed.status == SignaturePresenceStatus.FULLY_SIGNED
    assert len(res_signed.extracted_signatures) >= 1
    assert len(res_signed.missing_required_signatures) == 0

    # Case B: Unsigned FDA 1572
    unsigned_text = "STATEMENT OF INVESTIGATOR (FDA Form 1572) - Draft Copy Unsigned"
    parsed_unsigned = parser.parse(unsigned_text, filename="1572.pdf")
    res_unsigned = analyzer.analyze(parsed_unsigned, artifact_code="05.02.01")
    assert res_unsigned.status == SignaturePresenceStatus.UNSIGNED
    assert "Principal Investigator" in res_unsigned.missing_required_signatures

    # Case C: Digital Manifestation
    digital_text = """
    Protocol Sign-off Page
    Digitally Approved & Signed by pi.investigator (Principal Investigator) on 2026-08-20T12:00:00Z
    Digitally Approved & Signed by sponsor.rep (Sponsor Representative) on 2026-08-20T12:30:00Z
    """
    parsed_digital = parser.parse(digital_text, filename="protocol_signoff.pdf")
    res_digital = analyzer.analyze(parsed_digital, artifact_code="01.01.03")
    assert res_digital.status == SignaturePresenceStatus.FULLY_SIGNED
    assert len(res_digital.extracted_signatures) >= 2


@pytest.mark.asyncio
async def test_document_intelligence_worker_processing_and_polling():
    """Verify background DocumentIntelligenceWorker processes pending documents into TECHNICAL_QC with Part 11 audit.

    @req:PRD-TMF-006
    """
    repo = FakeETMFIntelligenceRepository()
    worker = DocumentIntelligenceWorker(repo=repo)

    # Ingest document in initial DRAFT status
    doc = await repo.create_document(
        study_id="STUDY-WORKER",
        site_id="SITE-55",
        artifact_type="Pending Classification",
        filename="Investigator_CV_Dr_Watson.pdf",
        content="Curriculum Vitae Dr. John Watson, MD Principal Investigator Site: SITE-55 Date: 2026-02-01 Expiration Date: 2028-02-01",
        mime_type="application/pdf",
        created_by="crc.uploader",
        version_index=1,
        status="DRAFT",
        taxonomy_version="v3.2.0",
        artifact_code="01.01.01",
        zone=1,
        section="01.01",
        reason_for_change="Uploaded via site portal",
        metadata_json={"needs_ai_processing": True},
    )

    # Run worker poll
    processed_count = await worker.poll_and_process_pending()
    assert processed_count == 1

    # Reload document from repo
    updated_doc = await repo.get_document_by_id(doc.id)
    assert updated_doc.status == "TECHNICAL_QC"
    assert updated_doc.artifact_code == "05.02.03"
    assert updated_doc.artifact_type == "Investigator CV"
    assert updated_doc.issue_date == date(2026, 2, 1)
    assert updated_doc.expiration_date is not None
    assert "Dr. John Watson" in updated_doc.document_owner_id
    assert updated_doc.metadata_json["ai_staging_status"] == "DRAFT_AI"

    # Verify Part 11 audit log
    assert len(repo.audit_logs) >= 1
    worker_audit = [
        a
        for a in repo.audit_logs
        if a["action"] == "AI_DOCUMENT_INTELLIGENCE_PROCESSED"
    ]
    assert len(worker_audit) == 1
    assert "Investigator CV" in worker_audit[0]["details"]


@pytest.mark.asyncio
async def test_cra_qc_staging_and_review_workflow():
    """Test full staging in CRA QC queue and Part 11 adjudication.

    @req:PRD-TMF-006
    """
    repo = FakeETMFIntelligenceRepository()
    stage_use_case = StageClassifiedArtifactUseCase(repo)
    review_use_case = CRAQCReviewUseCase(repo)
    queue_use_case = GetQCQueueUseCase(repo)

    doc_content = """
    FDA Form 1572 Statement of Investigator
    OMB 0910-0014
    Protocol Number: STUDY-101
    Site Number: SITE-01
    Investigator Signature: /s/ Dr. Gregory House
    """

    # 1. Stage artifact into QC queue
    doc, report = await stage_use_case.execute(
        content=doc_content,
        filename="house_1572.pdf",
        mime_type="application/pdf",
        study_id="STUDY-101",
        actor_id="crc.user",
        actor_role="site_crc",
        reason_for_change="Regulatory document upload for site activation",
        site_id="SITE-01",
    )

    assert doc.status == "TECHNICAL_QC"
    assert doc.artifact_code == "05.02.01"
    assert report.primary_classification.artifact_name == "FDA Form 1572"
    assert len(repo.audit_logs) == 1
    assert repo.audit_logs[0]["action"] == "AI_STAGE_QC"

    # 2. Check QC Queue
    queue = await queue_use_case.execute(study_id="STUDY-101")
    assert len(queue) == 1
    assert queue[0].document_id == doc.id
    assert queue[0].status == "TECHNICAL_QC"

    # 3. CRA Review: Accept Classification
    approved_doc = await review_use_case.execute(
        document_id=doc.id,
        decision="ACCEPT",
        actor_id="cra.monitor",
        actor_role="cra_monitor",
        reason_for_change="CRA verified signature and OMB metadata on FDA 1572",
    )
    assert approved_doc.status == "APPROVED"
    assert approved_doc.approval_status == "APPROVED"
    assert len(repo.qc_transitions) == 1
    assert repo.qc_transitions[0]["to_status"] == "APPROVED"


@pytest.mark.asyncio
async def test_cra_qc_override_and_rejection_workflows():
    """Verify CRA can override AI classification with alternative code or reject document with reason.

    @req:PRD-TMF-006
    """
    repo = FakeETMFIntelligenceRepository()
    stage_use_case = StageClassifiedArtifactUseCase(repo)
    review_use_case = CRAQCReviewUseCase(repo)

    # 1. Stage document
    doc, _ = await stage_use_case.execute(
        content="General Regulatory Document Text",
        filename="ambiguous_doc.pdf",
        mime_type="application/pdf",
        study_id="STUDY-202",
        actor_id="crc.user",
        actor_role="site_crc",
        reason_for_change="Initial upload",
    )

    # 2. Override classification to Medical License (05.02.98)
    overridden_doc = await review_use_case.execute(
        document_id=doc.id,
        decision="OVERRIDE",
        actor_id="cra.monitor",
        actor_role="cra_monitor",
        reason_for_change="CRA classified document as Medical License based on seal",
        override_artifact_code="05.02.98",
    )
    assert overridden_doc.status == "APPROVED"
    assert overridden_doc.artifact_code == "05.02.98"
    assert overridden_doc.artifact_type == "Medical License"

    # 3. Stage second document and reject
    doc2, _ = await stage_use_case.execute(
        content="Corrupted or illegible document text",
        filename="corrupted.pdf",
        mime_type="application/pdf",
        study_id="STUDY-202",
        actor_id="crc.user",
        actor_role="site_crc",
        reason_for_change="Initial upload",
    )

    rejected_doc = await review_use_case.execute(
        document_id=doc2.id,
        decision="REJECT",
        actor_id="cra.monitor",
        actor_role="cra_monitor",
        reason_for_change="Document text is illegible and missing required signatures",
        discrepancy_comment="Illegible scan",
    )
    assert rejected_doc.status == "REJECTED"
    assert rejected_doc.approval_status == "REJECTED"


@pytest.mark.asyncio
async def test_intelligence_rest_endpoints():
    """Verify REST API endpoints for classify, extract-metadata, verify-signatures, and analyze.

    @req:PRD-TMF-006
    """
    headers = create_test_auth_headers(user_id="cra.user", roles=["cra_monitor"])
    transport = ASGITransport(app=app)

    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Test /api/v1/etmf/intelligence/classify
        resp = await client.post(
            "/api/v1/etmf/intelligence/classify",
            headers=headers,
            json={
                "filename": "FDA_Form_1572_Signed.pdf",
                "content": "Statement of Investigator OMB No. 0910-0014 FDA Form 1572",
            },
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["primary_classification"]["artifact_code"] == "05.02.01"
        assert data["confidence_tier"] == "HIGH"

        # 2. Test /api/v1/etmf/intelligence/extract-metadata
        resp_meta = await client.post(
            "/api/v1/etmf/intelligence/extract-metadata",
            headers=headers,
            json={
                "filename": "doa_log.pdf",
                "content": "Delegation of Authority Log Protocol ID: CAD-700 Site: SITE-99 Investigator: Dr. Smith",
                "study_id": "CAD-700",
            },
        )
        assert resp_meta.status_code == 200
        meta_data = resp_meta.json()
        assert meta_data["protocol_number"] == "CAD-700"
        assert meta_data["site_id"] == "SITE-99"

        # 3. Test /api/v1/etmf/intelligence/verify-signatures
        resp_sig = await client.post(
            "/api/v1/etmf/intelligence/verify-signatures",
            headers=headers,
            json={
                "filename": "1572.pdf",
                "artifact_code": "05.02.01",
                "content": "Investigator Signature: /s/ Dr. Smith",
            },
        )
        assert resp_sig.status_code == 200
        sig_data = resp_sig.json()
        assert sig_data["status"] == "FULLY_SIGNED"

        # 4. Test /api/v1/etmf/intelligence/analyze
        resp_analyze = await client.post(
            "/api/v1/etmf/intelligence/analyze",
            headers=headers,
            json={
                "filename": "Investigator_CV_Dr_Brown.pdf",
                "content": "Curriculum Vitae Dr. Emmet Brown, MD Site Number: SITE-01 Issue Date: 2026-01-01",
                "study_id": "CAD-ONC",
                "site_id": "SITE-01",
            },
        )
        assert resp_analyze.status_code == 200
        analyze_data = resp_analyze.json()
        assert analyze_data["primary_classification"]["artifact_code"] == "05.02.03"
        assert analyze_data["modality"] == "PDF_BINARY"
        assert (
            analyze_data["eisf_target_mapping"]["eisf_section"]
            == "05_STAFF_QUALIFICATIONS"
        )
