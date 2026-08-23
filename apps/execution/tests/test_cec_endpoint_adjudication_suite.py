"""Comprehensive Unit and Integration Test Suite for Clinical Endpoint Adjudication Committee (CEC).

Requirements:
- ADR-2199: Blinded Dual-Reader Charter & Tie-Breaker Adjudication Architecture
- PRD-CEC-001: Automated Patient Event Dossier Compilation & In-Flight PHI Masking
- PRD-CEC-002: 2+1 Independent Blinded Reader & Tie-Breaker Workflow
- PRD-CEC-003: Bi-Directional EDC Feedback & CDISC SDTM CE Domain Export
- PRD-SYS-124: Charter Governance, Quorum Tracking, and 21 CFR Part 11 Electronic Signatures
- PRD-SYS-001: 21 CFR Part 11 Audit Trail Immutability
"""

import os

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    Base,
    ClinicalObservation,
)
from apps.execution.domain.cec_models import (
    AdjudicationClassification,
    AdjudicationWorkflowStatus,
    BindingDeterminationSource,
    CreateCharterRequest,
    CreateDossierRequest,
    DiagnosticAttachment,
    ECGRecord,
    EndpointCategory,
    LabPanelItem,
    ReaderRole,
    VotingRule,
)
from apps.execution.domain.sdtm.models import CE
from apps.execution.main import app
from apps.execution.services.cec_adjudication_service import CecAdjudicationService
from apps.execution.trial_lock import TrialLockManager


@pytest_asyncio.fixture(autouse=True)
async def setup_cec_db():
    """Initializes a clean in-memory SQLite test database with triggers."""
    from apps.execution.database.migrate import deploy_database_triggers

    TrialLockManager.reset()
    db_manager.init_db(
        os.getenv("TEST_DATABASE_URL", "sqlite+aiosqlite:///:memory:"),
        echo=False,
    )
    async with db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
        await deploy_database_triggers(conn, db_manager.engine.dialect.name)
    yield
    TrialLockManager.reset()
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(
    user_id: str = "dr_chair_evelyn",
    roles: str = "sponsor_admin",
    change_reason: str = "CEC test execution",
    site_id: str | None = None,
    reader_role: str | None = None,
) -> dict[str, str]:
    """Generates signed gateway headers for authenticated API requests."""
    from packages.testing.security import create_test_auth_headers

    headers = create_test_auth_headers(
        user_id=user_id,
        roles=[roles],
        change_reason=change_reason,
        site_id=site_id,
    )
    if reader_role:
        headers["X-Reader-Role"] = reader_role
    return headers


# =============================================================================
# UNIT TESTS: Service-Layer Logic, Blinding, Concordance, & SDTM Compilation
# =============================================================================


@pytest.mark.asyncio
async def test_dossier_packaging_and_phi_masking_unit():
    """Verify automated dossier compilation and in-flight PHI masking.

    @req:PRD-CEC-001, @req:PRD-SYS-124, @req:ADR-2199
    """
    service = CecAdjudicationService()

    narrative_with_phi = (
        "Patient John Doe (MRN: 98765432) was admitted to Saint Jude Hospital under the care of "
        "Dr. Robert Smith. Contact spouse at 555-234-5678 or jdoe@family.org. Emergent coronary angio showed 99% LAD."
    )

    scrubbed, counts = service.scrub_text(narrative_with_phi, subject_id="SUBJ-101")

    assert "John Doe" not in scrubbed
    assert "98765432" not in scrubbed
    assert "555-234-5678" not in scrubbed
    assert "jdoe@family.org" not in scrubbed
    assert "Saint Jude Hospital" not in scrubbed
    assert counts["names_redacted"] > 0
    assert counts["phones_redacted"] > 0
    assert counts["emails_redacted"] > 0

    # Package Dossier in database
    async with db_manager.session_maker() as session:
        req = CreateDossierRequest(
            study_id="STUDY-101",
            site_id="SITE-101",
            subject_id="SUBJ-101",
            event_id="AE-001",
            candidate_event_type=EndpointCategory.MYOCARDIAL_INFARCTION,
            reported_term="Acute Myocardial Infarction",
            verbatim_description="Patient experienced severe substernal chest pressure.",
            reported_onset_date="2026-08-10",
            medical_narrative=narrative_with_phi,
            lab_panels=[
                LabPanelItem(
                    test_name="High-Sensitivity Troponin I",
                    loinc_code="49563-0",
                    result_value="5.2",
                    result_unit="ng/mL",
                    reference_low=0.0,
                    reference_high=0.04,
                    is_out_of_range=True,
                    toxicity_grade=4,
                )
            ],
            ecg_records=[
                ECGRecord(
                    ecg_id="ECG-01",
                    timestamp="2026-08-10T12:00:00Z",
                    rhythm="Sinus rhythm",
                    st_elevation_leads=["V1", "V2", "V3", "V4"],
                    lbbb_present=False,
                    qtcf_ms=430.0,
                    interpretation="Acute anterior STEMI",
                )
            ],
            diagnostic_attachments=[
                DiagnosticAttachment(
                    attachment_id="ATT-01",
                    document_type="Coronary Angiogram",
                    title="Angiography Report",
                    summary="99% proximal LAD occlusion with thrombus.",
                    key_findings=["Proximal LAD 99% stenosis", "TIMI 0 flow initially"],
                )
            ],
            reader_1_id="reader_01",
            reader_2_id="reader_02",
            chair_id="chair_01",
        )

        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()
        await session.refresh(record)

        assert record.id is not None
        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.PENDING_PRIMARY_REVIEWS.value
        )
        assert record.masked_subject_id.startswith("SUBJ-BLINDED-")
        assert record.masked_site_id.startswith("SITE-BLINDED-")
        assert record.dossier_digest != ""
        assert "John Doe" not in (record.medical_narrative or "")


@pytest.mark.asyncio
async def test_blinded_reader_isolation_unit():
    """Verify that Reader 1 and Reader 2 views are completely blinded from each other.

    @req:PRD-CEC-002, @req:ADR-2199
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        req = CreateDossierRequest(
            study_id="STUDY-101",
            site_id="SITE-101",
            subject_id="SUBJ-202",
            event_id="AE-002",
            candidate_event_type=EndpointCategory.STROKE,
            reported_term="Acute Ischemic Stroke",
            verbatim_description="Sudden right-sided hemiparesis and aphasia.",
            reported_onset_date="2026-08-11",
            medical_narrative="Brain MRI showed acute left MCA diffusion restriction.",
            reader_1_id="dr_reader_1",
            reader_2_id="dr_reader_2",
            chair_id="dr_chair",
        )
        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()
        await session.refresh(record)

        # Reader 1 completes assessment
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_reader_1",
            reader_name="Dr. Marcus Vance",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_ISCHEMIC_STROKE,
            subtype="Left MCA Infarction",
            adjudicated_onset_date="2026-08-11",
            criteria_met=["Acute focal deficit > 24h", "MRI diffusion restriction"],
            confidence_score=5,
            clinical_rationale="Classic left MCA stroke with MRI confirmation.",
        )
        await session.commit()
        await session.refresh(record)

        # 1. Test Reader 2 view: MUST NOT see Reader 1's assessment
        r2_view = service.get_blinded_dossier_view(
            record=record,
            requesting_user_id="dr_reader_2",
            requesting_role=ReaderRole.READER_2.value,
        )
        assert r2_view.peer_assessment is None
        assert (
            r2_view.peer_assessment_completed is True
        )  # knows peer finished, but content is blinded

        # 2. Test Reader 1 view: Sees own assessment, but peer is None
        r1_view = service.get_blinded_dossier_view(
            record=record,
            requesting_user_id="dr_reader_1",
            requesting_role=ReaderRole.READER_1.value,
        )
        assert r1_view.my_assessment is not None
        assert (
            r1_view.my_assessment.classification
            == AdjudicationClassification.CONFIRMED_ISCHEMIC_STROKE
        )
        assert r1_view.peer_assessment is None


@pytest.mark.asyncio
async def test_concordant_consensus_auto_finalization_unit():
    """Verify that when Reader 1 and Reader 2 agree, consensus is finalized automatically.

    @req:PRD-CEC-002, @req:PRD-CEC-003, @req:ADR-2199
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        # Seed an observation to verify EDC write-back
        obs = ClinicalObservation(
            id="OBS-AE-003",
            study_id="STUDY-101",
            subject_id="SUBJ-303",
            site_id="SITE-101",
            form_submission_id="AE-FORM-003",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Acute Coronary Syndrome",
            additional_properties={},
        )
        session.add(obs)

        req = CreateDossierRequest(
            study_id="STUDY-101",
            site_id="SITE-101",
            subject_id="SUBJ-303",
            event_id="OBS-AE-003",
            candidate_event_type=EndpointCategory.MYOCARDIAL_INFARCTION,
            reported_term="Acute Myocardial Infarction",
            verbatim_description="Crushing retrosternal chest pain with troponin elevation.",
            reported_onset_date="2026-08-12",
            medical_narrative="STEMI treated with PCI to LAD.",
            reader_1_id="dr_reader_1",
            reader_2_id="dr_reader_2",
            chair_id="dr_chair",
        )
        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()

        # Reader 1 submits
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_reader_1",
            reader_name="Dr. Reader 1",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 1 Spontaneous MI",
            adjudicated_onset_date="2026-08-12",
            criteria_met=["Biomarker elevation", "ST elevation"],
            confidence_score=5,
            clinical_rationale="Definitive Type 1 STEMI.",
        )

        # Reader 2 submits concordant review
        record = await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_reader_2",
            reader_name="Dr. Reader 2",
            reader_role=ReaderRole.READER_2,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 1 Spontaneous MI",
            adjudicated_onset_date="2026-08-12",
            criteria_met=["Biomarker elevation", "ST elevation"],
            confidence_score=5,
            clinical_rationale="Concur with Type 1 STEMI.",
        )
        await session.commit()
        await session.refresh(record)

        # Verify automated consensus
        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.FINALIZED_CONCORDANT.value
        )
        assert record.is_discordant is False
        assert (
            record.final_consensus_classification
            == AdjudicationClassification.CONFIRMED_MI.value
        )
        assert record.final_consensus_subtype == "Type 1 Spontaneous MI"
        assert (
            record.binding_determination_source
            == BindingDeterminationSource.CONCORDANT_CONSENSUS.value
        )
        assert record.sdtm_ce_record is not None
        assert (
            record.sdtm_ce_record["CEDECOD"]
            == AdjudicationClassification.CONFIRMED_MI.value
        )
        assert record.edc_writeback_status == "SYNCED"


@pytest.mark.asyncio
async def test_discordance_detection_and_chair_tiebreaker_unit():
    """Verify discordance detection, escalation, and Chair tie-breaker resolution with Part 11 signature.

    @req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        req = CreateDossierRequest(
            study_id="STUDY-101",
            site_id="SITE-101",
            subject_id="SUBJ-404",
            event_id="AE-004",
            candidate_event_type=EndpointCategory.CARDIOVASCULAR_MORTALITY,
            reported_term="Fatal Arrest at Home",
            verbatim_description="Patient collapsed at home.",
            reported_onset_date="2026-08-13",
            medical_narrative="Patient found unresponsive. Autopsy inconclusive.",
            reader_1_id="dr_reader_1",
            reader_2_id="dr_reader_2",
            chair_id="dr_chair",
        )
        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()

        # Reader 1 votes CV Death
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_reader_1",
            reader_name="Dr. Reader 1",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_CV_DEATH,
            subtype="Sudden Cardiac Death",
            adjudicated_onset_date="2026-08-13",
            criteria_met=["Sudden witnessed death"],
            confidence_score=4,
            clinical_rationale="Sudden death presumed CV.",
        )

        # Reader 2 votes Non-CV Death (Discordance!)
        record = await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_reader_2",
            reader_name="Dr. Reader 2",
            reader_role=ReaderRole.READER_2,
            classification=AdjudicationClassification.CONFIRMED_NON_CV_DEATH,
            subtype="Presumed Trauma / Unwitnessed",
            adjudicated_onset_date="2026-08-13",
            criteria_met=[],
            confidence_score=3,
            clinical_rationale="Insufficient evidence for primary cardiac etiology.",
        )
        await session.commit()
        await session.refresh(record)

        # Assert Discordance Detected
        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED.value
        )
        assert record.is_discordant is True
        assert len(record.discordance_reasons or []) > 0
        assert "Primary classification discordance" in record.discordance_reasons[0]

        # Chair Tie-Breaker Ruling with 21 CFR Part 11 Electronic Signature
        record = await service.adjudicate_chair_tie_breaker(
            session=session,
            dossier_id=record.id,
            chair_id="dr_chair",
            chair_name="Dr. Evelyn Reed, MD, FACC",
            final_classification=AdjudicationClassification.CONFIRMED_CV_DEATH,
            final_subtype="Sudden Cardiac Arrest (VF)",
            final_adjudicated_onset_date="2026-08-13",
            binding_reader=BindingDeterminationSource.CHAIR_UPHELD_READER_1,
            criteria_confirmed=["Witnessed sudden collapse with VF rhythm"],
            adjudication_rationale="Emergency EMS telemetry clearly demonstrates primary ventricular fibrillation.",
            signer_name="Dr. Evelyn Reed, MD, FACC",
            signature_meaning="I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines",
            password="SecureChairPassword123!",
        )
        await session.commit()
        await session.refresh(record)

        # Assert Finalized by Chair
        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.FINALIZED_BY_CHAIR.value
        )
        assert (
            record.final_consensus_classification
            == AdjudicationClassification.CONFIRMED_CV_DEATH.value
        )
        assert (
            record.binding_determination_source
            == BindingDeterminationSource.CHAIR_UPHELD_READER_1.value
        )
        assert record.electronic_signature is not None
        assert record.electronic_signature["signer_name"] == "Dr. Evelyn Reed, MD, FACC"
        assert record.electronic_signature["is_valid"] is True


@pytest.mark.asyncio
async def test_charter_governance_and_quorum_tracking_unit():
    """Verify charter configuration, voting rules, and meeting quorum calculation.

    @req:PRD-SYS-124
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        # Create Charter
        charter_req = CreateCharterRequest(
            charter_id="CHARTER-STUDY-101",
            study_id="STUDY-101",
            committee_name="Cardiovascular Clinical Endpoint Adjudication Committee",
            version="1.0",
            voting_rule=VotingRule.CHAIR_TIE_BREAKER,
            minimum_quorum_percentage=60.0,
            minimum_members_count=3,
            eligible_members=[
                {
                    "member_id": "m1",
                    "member_name": "Dr. Reed",
                    "role": "Chair",
                    "specialty": "Cardiology",
                },
                {
                    "member_id": "m2",
                    "member_name": "Dr. Vance",
                    "role": "Reader 1",
                    "specialty": "Imaging",
                },
                {
                    "member_id": "m3",
                    "member_name": "Dr. Finch",
                    "role": "Reader 2",
                    "specialty": "Neurology",
                },
                {
                    "member_id": "m4",
                    "member_name": "Dr. Chen",
                    "role": "Member",
                    "specialty": "Electrophysiology",
                },
                {
                    "member_id": "m5",
                    "member_name": "Dr. Martinez",
                    "role": "Member",
                    "specialty": "Critical Care",
                },
            ],
        )

        charter = await service.create_or_update_charter(
            session, charter_req, created_by="sponsor_admin"
        )
        await session.commit()
        await session.refresh(charter)

        assert charter.id is not None
        assert charter.minimum_quorum_percentage == 60.0

        # Meeting 1: Quorum Met (4 of 5 attendees = 80%)
        m1 = await service.record_meeting_quorum(
            session=session,
            study_id="STUDY-101",
            charter_id=charter.id,
            meeting_date="2026-08-20T10:00:00Z",
            attendees=[
                {"member_id": "m1", "member_name": "Dr. Reed", "role": "Chair"},
                {"member_id": "m2", "member_name": "Dr. Vance", "role": "Reader 1"},
                {"member_id": "m3", "member_name": "Dr. Finch", "role": "Reader 2"},
                {"member_id": "m4", "member_name": "Dr. Chen", "role": "Member"},
            ],
            total_eligible_members=5,
            dossiers_reviewed=["DOSSIER-MACE-001"],
            voting_results={
                "DOSSIER-MACE-001": {"votes_for_mi": 4, "votes_against": 0}
            },
            meeting_minutes="Reviewed STEMI dossier; consensus confirmed.",
            chair_signature={"signature_id": "SIG-M1", "signed_by": "Dr. Reed"},
        )
        await session.commit()
        await session.refresh(m1)

        assert m1.quorum_met is True
        assert m1.quorum_percentage == 80.0
        assert m1.minutes_digest != ""

        # Meeting 2: Quorum Failed (1 attendee = 20%)
        m2 = await service.record_meeting_quorum(
            session=session,
            study_id="STUDY-101",
            charter_id=charter.id,
            meeting_date="2026-08-21T10:00:00Z",
            attendees=[
                {"member_id": "m1", "member_name": "Dr. Reed", "role": "Chair"},
            ],
            total_eligible_members=5,
            dossiers_reviewed=[],
            voting_results={},
            meeting_minutes="Meeting adjourned due to lack of quorum.",
        )
        await session.commit()
        await session.refresh(m2)

        assert m2.quorum_met is False
        assert m2.quorum_percentage == 20.0


@pytest.mark.asyncio
async def test_sdtm_ce_domain_model_compilation_unit():
    """Verify compilation of finalized dossiers into valid CDISC SDTM CE & SUPPCE dataset.

    @req:PRD-CEC-003, @req:PRD-SYS-001
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        # Create and finalize a dossier
        req = CreateDossierRequest(
            study_id="STUDY-SDTM-101",
            site_id="SITE-101",
            subject_id="SUBJ-999",
            event_id="AE-999",
            candidate_event_type=EndpointCategory.MYOCARDIAL_INFARCTION,
            reported_term="Severe Chest Pain and Troponin Elevation",
            verbatim_description="STEMI event",
            reported_onset_date="2026-08-15",
            medical_narrative="Acute MI confirmed by coronary catheterization.",
        )
        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()

        # Concordant finalization
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="r1",
            reader_name="Reader 1",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 1 STEMI",
            adjudicated_onset_date="2026-08-15",
            criteria_met=["Biomarker rise", "ECG ST elevation"],
            confidence_score=5,
            clinical_rationale="Type 1 MI",
        )
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="r2",
            reader_name="Reader 2",
            reader_role=ReaderRole.READER_2,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 1 STEMI",
            adjudicated_onset_date="2026-08-15",
            criteria_met=["Biomarker rise", "ECG ST elevation"],
            confidence_score=5,
            clinical_rationale="Type 1 MI",
        )
        await session.commit()

        # Compile Study SDTM CE dataset
        result = await service.compile_study_sdtm_ce_dataset(session, "STUDY-SDTM-101")

        assert result.record_count == 1
        assert len(result.ce_records) == 1
        assert len(result.suppce_records) >= 3

        ce_row = result.ce_records[0]
        assert ce_row["STUDYID"] == "STUDY-SDTM-101"
        assert ce_row["DOMAIN"] == "CE"
        assert ce_row["USUBJID"] == "STUDY-SDTM-101-SUBJ-999"
        assert ce_row["CEDECOD"] == "CONFIRMED_MI"
        assert ce_row["CECAT"] == "MYOCARDIAL_INFARCTION"
        assert ce_row["CEOCCUR"] == "Y"
        assert ce_row["CESTDTC"] == "2026-08-15"

        # Validate with strongly-typed SDTM CE domain model
        ce_model = CE(**ce_row)
        assert ce_model.DOMAIN == "CE"
        assert ce_model.CESEQ == 1


# =============================================================================
# INTEGRATION TESTS: HTTP REST API Endpoints over AsyncClient
# =============================================================================


@pytest.mark.asyncio
async def test_cec_api_dossier_lifecycle_and_blinding():
    """Integration test: Package dossier, blind readers, submit reviews, and verify consensus via API.

    @req:PRD-CEC-001, @req:PRD-CEC-002, @req:PRD-CEC-003, @req:ADR-2199
    """
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        # 1. Package new dossier
        create_payload = {
            "study_id": "STUDY-101",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-API-01",
            "event_id": "EVT-API-01",
            "candidate_event_type": "MYOCARDIAL_INFARCTION",
            "reported_term": "Acute Coronary Syndrome / NSTEMI",
            "verbatim_description": "Substernal tightness with positive cardiac enzymes.",
            "reported_onset_date": "2026-08-16",
            "medical_narrative": "Patient Dr. John Doe was seen at City General Hospital for acute NSTEMI.",
            "bundled_forms": [],
            "lab_panels": [
                {
                    "test_name": "Troponin T",
                    "loinc_code": "6598-7",
                    "result_value": "1.8",
                    "result_unit": "ng/mL",
                    "reference_low": 0.0,
                    "reference_high": 0.01,
                    "is_out_of_range": True,
                    "toxicity_grade": 4,
                }
            ],
            "ecg_records": [],
            "diagnostic_attachments": [],
            "reader_1_id": "api_reader_1",
            "reader_1_name": "Dr. Reader One",
            "reader_2_id": "api_reader_2",
            "reader_2_name": "Dr. Reader Two",
            "chair_id": "api_chair",
            "chair_name": "Dr. Chair Evelyn",
        }

        headers = get_auth_headers(user_id="api_crc_user", roles="sponsor_admin")
        res = await client.post(
            "/api/v1/execution/cec/dossiers", json=create_payload, headers=headers
        )
        assert res.status_code == 201
        data = res.json()
        dossier_id = data["dossier_id"]
        assert data["workflow_status"] == "PENDING_PRIMARY_REVIEWS"

        # 2. Query dossier list
        list_res = await client.get(
            "/api/v1/execution/cec/dossiers?study_id=STUDY-101",
            headers=headers,
        )
        assert list_res.status_code == 200
        items = list_res.json()
        assert any(item["dossier_id"] == dossier_id for item in items)

        # 3. Reader 1 fetches blinded view
        r1_headers = get_auth_headers(
            user_id="api_reader_1", roles="sponsor_admin", reader_role="READER_1"
        )
        view_res = await client.get(
            f"/api/v1/execution/cec/dossiers/{dossier_id}", headers=r1_headers
        )
        assert view_res.status_code == 200
        view_data = view_res.json()
        assert view_data["peer_assessment"] is None
        assert "City General Hospital" not in view_data["medical_narrative"]

        # 4. Reader 1 submits assessment
        r1_assess_payload = {
            "reader_role": "READER_1",
            "classification": "CONFIRMED_MI",
            "subtype": "Type 2 Demand Ischemia MI",
            "adjudicated_onset_date": "2026-08-16",
            "criteria_met": ["Troponin elevation > 99th percentile"],
            "confidence_score": 4,
            "clinical_rationale": "Enzyme rise consistent with Type 2 NSTEMI.",
        }
        res_r1 = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json=r1_assess_payload,
            headers=r1_headers,
        )
        assert res_r1.status_code == 200
        assert res_r1.json()["workflow_status"] == "READER_1_COMPLETED"

        # 5. Reader 2 submits concordant assessment
        r2_headers = get_auth_headers(
            user_id="api_reader_2", roles="sponsor_admin", reader_role="READER_2"
        )
        r2_assess_payload = {
            "reader_role": "READER_2",
            "classification": "CONFIRMED_MI",
            "subtype": "Type 2 Demand Ischemia MI",
            "adjudicated_onset_date": "2026-08-16",
            "criteria_met": ["Troponin elevation > 99th percentile"],
            "confidence_score": 4,
            "clinical_rationale": "Agree with Type 2 NSTEMI.",
        }
        res_r2 = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json=r2_assess_payload,
            headers=r2_headers,
        )
        assert res_r2.status_code == 200
        assert res_r2.json()["workflow_status"] == "FINALIZED_CONCORDANT"
        assert res_r2.json()["is_discordant"] is False

        # 6. Compile SDTM CE dataset via API
        compile_res = await client.post(
            "/api/v1/execution/cec/sdtm-ce/compile",
            json={"study_id": "STUDY-101"},
            headers=headers,
        )
        assert compile_res.status_code == 200
        comp_data = compile_res.json()
        assert comp_data["record_count"] >= 1
        assert any(
            r["USUBJID"] == "STUDY-101-SUBJ-API-01" for r in comp_data["ce_records"]
        )


@pytest.mark.asyncio
async def test_cec_api_discordance_and_chair_tiebreaker():
    """Integration test: Create discordant reviews and resolve via Chair tie-breaker API with eSignature.

    @req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199
    """
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        # 1. Package dossier
        create_payload = {
            "study_id": "STUDY-101",
            "site_id": "SITE-101",
            "subject_id": "SUBJ-DISC-01",
            "event_id": "EVT-DISC-01",
            "candidate_event_type": "STROKE",
            "reported_term": "Suspected Stroke / Transient Neurological Deficit",
            "verbatim_description": "Brief speech difficulty lasting 2 hours.",
            "reported_onset_date": "2026-08-17",
            "medical_narrative": "Patient had transient slurred speech.",
            "reader_1_id": "api_reader_1",
            "reader_2_id": "api_reader_2",
            "chair_id": "api_chair",
        }
        headers = get_auth_headers(user_id="api_crc", roles="sponsor_admin")
        res = await client.post(
            "/api/v1/execution/cec/dossiers", json=create_payload, headers=headers
        )
        assert res.status_code == 201
        dossier_id = res.json()["dossier_id"]

        # 2. Reader 1 votes Ischemic Stroke
        r1_headers = get_auth_headers(
            user_id="api_reader_1", roles="sponsor_admin", reader_role="READER_1"
        )
        await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json={
                "reader_role": "READER_1",
                "classification": "CONFIRMED_ISCHEMIC_STROKE",
                "subtype": "Ischemic Stroke",
                "adjudicated_onset_date": "2026-08-17",
                "clinical_rationale": "Definite focal event.",
            },
            headers=r1_headers,
        )

        # 3. Reader 2 votes NOT AN ENDPOINT (Discordance)
        r2_headers = get_auth_headers(
            user_id="api_reader_2", roles="sponsor_admin", reader_role="READER_2"
        )
        disc_res = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json={
                "reader_role": "READER_2",
                "classification": "NOT_AN_ENDPOINT",
                "subtype": "Transient Ischemic Attack (<24h)",
                "adjudicated_onset_date": "2026-08-17",
                "clinical_rationale": "Symptoms resolved within 2 hours without imaging infarction.",
            },
            headers=r2_headers,
        )
        assert disc_res.status_code == 200
        disc_data = disc_res.json()
        assert disc_data["workflow_status"] == "DISCORDANCE_DETECTED"
        assert disc_data["is_discordant"] is True

        # 4. Committee Chair Tie-Breaker Adjudication with 21 CFR Part 11 eSignature
        chair_headers = get_auth_headers(user_id="api_chair", roles="sponsor_admin")
        chair_payload = {
            "final_classification": "NOT_AN_ENDPOINT",
            "final_subtype": "Transient Ischemic Attack (TIA)",
            "final_adjudicated_onset_date": "2026-08-17",
            "binding_reader": "CHAIR_UPHELD_READER_2",
            "criteria_confirmed": [
                "Symptoms resolved in <24 hours",
                "No acute DWI infarction",
            ],
            "adjudication_rationale": "Adjudication Committee Chair upholds Reader 2 finding: TIA does not meet protocol endpoint definition for stroke.",
            "signer_name": "Dr. Evelyn Reed, MD, FACC",
            "password": "ChairPassword123!",
            "signature_meaning": "I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines",
        }

        chair_res = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/chair-adjudicate",
            json=chair_payload,
            headers=chair_headers,
        )
        assert chair_res.status_code == 200
        chair_data = chair_res.json()
        assert chair_data["status"] == "FINALIZED"
        assert chair_data["workflow_status"] == "FINALIZED_BY_CHAIR"
        assert chair_data["final_consensus_classification"] == "NOT_AN_ENDPOINT"
        assert chair_data["electronic_signature_id"] is not None
        assert chair_data["edc_writeback_status"] == "SYNCED"


@pytest.mark.asyncio
async def test_cec_api_charter_and_meeting_quorum():
    """Integration test: Charter configuration and meeting quorum endpoints.

    @req:PRD-SYS-124
    """
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        headers = get_auth_headers(user_id="sponsor_admin_user", roles="sponsor_admin")

        # 1. Create Charter
        charter_payload = {
            "study_id": "STUDY-CEC-99",
            "committee_name": "Cardio-Renal Endpoint Committee",
            "version": "1.0",
            "voting_rule": "MAJORITY_2_OF_3",
            "minimum_quorum_percentage": 66.67,
            "minimum_members_count": 3,
            "eligible_members": [
                {"member_id": "u1", "member_name": "Dr. Alpha", "role": "Chair"},
                {"member_id": "u2", "member_name": "Dr. Beta", "role": "Reader 1"},
                {"member_id": "u3", "member_name": "Dr. Gamma", "role": "Reader 2"},
            ],
            "endpoint_definitions": ["MACE", "STROKE"],
        }
        res = await client.post(
            "/api/v1/execution/cec/charters", json=charter_payload, headers=headers
        )
        assert res.status_code == 201
        charter_data = res.json()
        assert charter_data["voting_rule"] == "MAJORITY_2_OF_3"
        charter_id = charter_data["charter_id"]

        # 2. Get Charter
        get_res = await client.get(
            "/api/v1/execution/cec/charters/STUDY-CEC-99", headers=headers
        )
        assert get_res.status_code == 200
        assert get_res.json()["committee_name"] == "Cardio-Renal Endpoint Committee"

        # 3. Record Meeting Quorum
        meeting_payload = {
            "study_id": "STUDY-CEC-99",
            "charter_id": charter_id,
            "meeting_date": "2026-08-23T14:00:00Z",
            "attendees": [
                {"member_id": "u1", "member_name": "Dr. Alpha", "role": "Chair"},
                {"member_id": "u2", "member_name": "Dr. Beta", "role": "Reader 1"},
                {"member_id": "u3", "member_name": "Dr. Gamma", "role": "Reader 2"},
            ],
            "total_eligible_members": 3,
            "dossiers_reviewed": ["DOSSIER-101"],
            "meeting_minutes": "Quorum achieved (100%). All members concordant.",
        }
        m_res = await client.post(
            "/api/v1/execution/cec/meetings", json=meeting_payload, headers=headers
        )
        assert m_res.status_code == 201
        m_data = m_res.json()
        assert m_data["quorum_met"] is True
        assert m_data["quorum_percentage"] == 100.0

        # 4. List meetings for study
        list_m = await client.get(
            "/api/v1/execution/cec/meetings/STUDY-CEC-99", headers=headers
        )
        assert list_m.status_code == 200
        assert len(list_m.json()) >= 1
