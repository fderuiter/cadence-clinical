"""Unit and Integration Tests for CEC Clinical Endpoint Adjudication Suite.

Requirements:
- PRD-SYS-172: Blinded CEC Dossier Compilation & Dual-Reader Workflow Engine
- PRD-SYS-173: CEC Committee Governance, Consensus Determination & 21 CFR Part 11 Electronic Signatures
- PRD-CEC-001: Automated Patient Event Dossier Compilation & PHI Redaction
- PRD-CEC-002: 2+1 Independent Blinded Reader & Tie-Breaker Workflow
- PRD-CEC-003: Bi-Directional EDC Feedback & CDISC SDTM CE Domain Export
- PRD-SYS-124: Charter Governance, Quorum Tracking, and 21 CFR Part 11 Electronic Signatures
- PRD-SYS-001: 21 CFR Part 11 Audit Trail Immutability
- ADR-2199: Blinded Dual-Reader Charter & Tie-Breaker Adjudication Architecture
"""

import os

import httpx
import pytest
import pytest_asyncio

from apps.execution.database.core import db_manager
from apps.execution.database.models import Base, ClinicalObservation
from apps.execution.domain.cec_models import (
    AdjudicationClassification,
    AdjudicationWorkflowStatus,
    BindingDeterminationSource,
    CreateCharterRequest,
    CreateDossierRequest,
    EndpointCategory,
    ReaderRole,
    VotingRule,
)
from apps.execution.main import app
from apps.execution.services.cec_adjudication_service import CecAdjudicationService
from apps.execution.trial_lock import TrialLockManager
from packages.testing.security import create_test_auth_headers


@pytest_asyncio.fixture(autouse=True)
async def setup_cec_adj_db():
    """Initializes a clean in-memory test database with triggers for CEC tests."""
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
    change_reason: str = "CEC adjudication test",
    site_id: str | None = None,
    reader_role: str | None = None,
) -> dict[str, str]:
    """Generates signed gateway headers for authenticated API requests."""
    headers = create_test_auth_headers(
        user_id=user_id,
        roles=[roles],
        change_reason=change_reason,
        site_id=site_id,
    )
    if reader_role:
        headers["X-Reader-Role"] = reader_role
    return headers


@pytest.mark.asyncio
async def test_cec_custom_chair_determination_workflow():
    """Verify Committee Chair custom tie-breaker determination and 21 CFR Part 11 sign-off.

    @req:PRD-SYS-173, @req:PRD-CEC-002, @req:PRD-SYS-124, @req:ADR-2199
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        req = CreateDossierRequest(
            study_id="STUDY-202",
            site_id="SITE-202",
            subject_id="SUBJ-CUSTOM-01",
            event_id="AE-CUSTOM-01",
            candidate_event_type=EndpointCategory.STROKE,
            reported_term="Transient Neurological Deficit",
            verbatim_description="Unilateral arm weakness lasting 45 minutes.",
            reported_onset_date="2026-08-18",
            medical_narrative="Patient seen by Dr. William Jones at Mercy General Hospital.",
            reader_1_id="dr_r1",
            reader_2_id="dr_r2",
            chair_id="dr_chair",
        )
        record = await service.package_dossier(session, req, created_by="crc_lead")
        await session.commit()

        # Reader 1 submits
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_r1",
            reader_name="Dr. Reader One",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_ISCHEMIC_STROKE,
            subtype="Ischemic Stroke",
            adjudicated_onset_date="2026-08-18",
            criteria_met=["Focal deficit"],
            confidence_score=3,
            clinical_rationale="Suspected small cortical infarct.",
        )

        # Reader 2 submits discordant finding
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="dr_r2",
            reader_name="Dr. Reader Two",
            reader_role=ReaderRole.READER_2,
            classification=AdjudicationClassification.NOT_AN_ENDPOINT,
            subtype="TIA / Non-stroke",
            adjudicated_onset_date="2026-08-18",
            criteria_met=[],
            confidence_score=4,
            clinical_rationale="Symptoms resolved within 1 hour, DWI negative.",
        )
        await session.commit()
        await session.refresh(record)

        assert record.is_discordant is True
        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED.value
        )

        # Chair issues custom ruling
        record = await service.adjudicate_chair_tie_breaker(
            session=session,
            dossier_id=record.id,
            chair_id="dr_chair",
            chair_name="Dr. Evelyn Reed, MD, Chair",
            final_classification=AdjudicationClassification.INDETERMINATE,
            final_subtype="Indeterminate Cerebral Ischemic Event",
            final_adjudicated_onset_date="2026-08-18",
            binding_reader=BindingDeterminationSource.CHAIR_CUSTOM_DETERMINATION,
            criteria_confirmed=["Inconclusive neuroimaging"],
            adjudication_rationale="Clinical presentation indeterminate between prolonged TIA and aborted minor stroke without neuroimaging confirmation.",
            signer_name="Dr. Evelyn Reed, MD, Chair",
            signature_meaning="I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines",
            password="ChairSecurePassword123!",
        )
        await session.commit()
        await session.refresh(record)

        assert (
            record.workflow_status
            == AdjudicationWorkflowStatus.FINALIZED_BY_CHAIR.value
        )
        assert (
            record.final_consensus_classification
            == AdjudicationClassification.INDETERMINATE.value
        )
        assert (
            record.binding_determination_source
            == BindingDeterminationSource.CHAIR_CUSTOM_DETERMINATION.value
        )
        assert record.electronic_signature is not None
        assert record.electronic_signature["is_valid"] is True


@pytest.mark.asyncio
async def test_cec_unauthorized_and_invalid_reader_rejection():
    """Verify unauthorized reader role assignment rejection and access controls.

    @req:PRD-SYS-172, @req:PRD-SYS-173, @req:ADR-2199
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        req = CreateDossierRequest(
            study_id="STUDY-303",
            site_id="SITE-303",
            subject_id="SUBJ-RBAC-01",
            event_id="AE-RBAC-01",
            candidate_event_type=EndpointCategory.MACE,
            reported_term="Acute Coronary Syndrome",
            verbatim_description="Chest pain with troponin leak.",
            reported_onset_date="2026-08-19",
            medical_narrative="Cardiac cath showed non-obstructive CAD.",
            reader_1_id="dr_assigned_1",
            reader_2_id="dr_assigned_2",
            chair_id="dr_assigned_chair",
        )
        record = await service.package_dossier(session, req, created_by="crc_lead")
        await session.commit()

        # Attempt submission by an unassigned third doctor
        with pytest.raises(ValueError, match="is not an assigned reader for dossier"):
            await service.submit_reader_assessment(
                session=session,
                dossier_id=record.id,
                reader_id="dr_unauthorized_stranger",
                reader_name="Dr. Stranger",
                reader_role=ReaderRole.COMMITTEE_MEMBER,
                classification=AdjudicationClassification.CONFIRMED_MI,
                subtype="Type 2 MI",
                adjudicated_onset_date="2026-08-19",
                criteria_met=[],
                confidence_score=3,
                clinical_rationale="Random opinion.",
            )


@pytest.mark.asyncio
async def test_cec_dossier_deidentification_deep_scrub():
    """Verify deterministic in-flight de-identification of multi-category clinical identifiers.

    @req:PRD-SYS-172, @req:PRD-CEC-001, @req:ADR-2199
    """
    service = CecAdjudicationService()

    dirty_text = (
        "Subject John Doe (MRN: 11223344) admitted to Saint Jude Medical Center under Dr. Sarah Connor. "
        "Contact daughter at 555-876-5432 or sconnor@clinic.org. Site 104 reported event."
    )

    scrubbed, counts = service.scrub_text(dirty_text, subject_id="SUBJ-777")

    assert "John Doe" not in scrubbed
    assert "11223344" not in scrubbed
    assert "Saint Jude Medical Center" not in scrubbed
    assert "Dr. Sarah Connor" not in scrubbed
    assert "555-876-5432" not in scrubbed
    assert "sconnor@clinic.org" not in scrubbed
    assert "Site 104" not in scrubbed
    assert counts["names_redacted"] > 0
    assert counts["sites_redacted"] > 0
    assert counts["phones_redacted"] > 0
    assert counts["emails_redacted"] > 0
    assert counts["mrns_redacted"] > 0


@pytest.mark.asyncio
async def test_cec_charter_voting_rules_and_quorum_edge_cases():
    """Verify charter quorum threshold calculations and default fallback governance.

    @req:PRD-SYS-173, @req:PRD-SYS-124
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        # Create 3-member charter with 66.67% quorum
        req = CreateCharterRequest(
            charter_id="CHARTER-STUDY-QUORUM",
            study_id="STUDY-QUORUM",
            committee_name="Oncology Endpoint Adjudication Committee",
            version="2.0",
            voting_rule=VotingRule.MAJORITY_2_OF_3,
            minimum_quorum_percentage=66.67,
            minimum_members_count=2,
            eligible_members=[
                {"member_id": "u1", "member_name": "Dr. A", "role": "Chair"},
                {"member_id": "u2", "member_name": "Dr. B", "role": "Reader 1"},
                {"member_id": "u3", "member_name": "Dr. C", "role": "Reader 2"},
            ],
            endpoint_definitions=["DISEASE_PROGRESSION"],
        )
        charter = await service.create_or_update_charter(
            session, req, created_by="sponsor_admin"
        )
        await session.commit()

        # Update charter
        req.committee_name = "Updated Oncology Endpoint Adjudication Committee"
        updated_charter = await service.create_or_update_charter(
            session, req, created_by="sponsor_admin"
        )
        await session.commit()
        assert (
            updated_charter.committee_name
            == "Updated Oncology Endpoint Adjudication Committee"
        )
        assert updated_charter.version_index == 2

        # Quorum test: 2 of 3 attending = 66.67% -> Pass
        m_pass = await service.record_meeting_quorum(
            session=session,
            study_id="STUDY-QUORUM",
            charter_id=charter.id,
            meeting_date="2026-08-22T09:00:00Z",
            attendees=[
                {"member_id": "u1", "member_name": "Dr. A", "role": "Chair"},
                {"member_id": "u2", "member_name": "Dr. B", "role": "Reader 1"},
            ],
            total_eligible_members=3,
            dossiers_reviewed=[],
            voting_results={},
            meeting_minutes="Quorum achieved with 2 of 3 members.",
        )
        assert m_pass.quorum_met is True
        assert m_pass.quorum_percentage == 66.67


@pytest.mark.asyncio
async def test_cec_edc_writeback_and_sdtm_suppce_mapping():
    """Verify bidirectional EDC observation synchronization and SDTM SUPPCE qualifiers.

    @req:PRD-SYS-173, @req:PRD-CEC-003, @req:PRD-SYS-001
    """
    service = CecAdjudicationService()

    async with db_manager.session_maker() as session:
        # Seed observation
        obs = ClinicalObservation(
            id="OBS-EDC-99",
            study_id="STUDY-EDC-01",
            subject_id="SUBJ-EDC-99",
            site_id="SITE-01",
            form_submission_id="FORM-EDC-99",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Unstable Angina",
            additional_properties={},
        )
        session.add(obs)
        await session.commit()

        # Create dossier and finalize
        req = CreateDossierRequest(
            study_id="STUDY-EDC-01",
            site_id="SITE-01",
            subject_id="SUBJ-EDC-99",
            event_id="OBS-EDC-99",
            candidate_event_type=EndpointCategory.MYOCARDIAL_INFARCTION,
            reported_term="Unstable Angina / Non-STEMI",
            verbatim_description="Anginal symptoms with biomarker rise.",
            reported_onset_date="2026-08-20",
            medical_narrative="Confirmed NSTEMI with peak troponin 3.4.",
            reader_1_id="r1",
            reader_2_id="r2",
            chair_id="chair1",
        )
        record = await service.package_dossier(session, req, created_by="crc_user")
        await session.commit()

        # Concordant submissions
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="r1",
            reader_name="Reader 1",
            reader_role=ReaderRole.READER_1,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 2 MI",
            adjudicated_onset_date="2026-08-20",
            criteria_met=["Troponin elevation"],
            confidence_score=5,
            clinical_rationale="NSTEMI confirmed",
        )
        await service.submit_reader_assessment(
            session=session,
            dossier_id=record.id,
            reader_id="r2",
            reader_name="Reader 2",
            reader_role=ReaderRole.READER_2,
            classification=AdjudicationClassification.CONFIRMED_MI,
            subtype="Type 2 MI",
            adjudicated_onset_date="2026-08-20",
            criteria_met=["Troponin elevation"],
            confidence_score=5,
            clinical_rationale="NSTEMI confirmed",
        )
        await session.commit()
        await session.refresh(record)

        # Verify EDC writeback
        wb_res = await service.write_back_to_edc_casebook(session, record)
        await session.commit()

        assert wb_res.status == "SUCCESS"
        assert "OBS-EDC-99" in wb_res.observation_ids_updated
        assert record.edc_writeback_status == "SYNCED"

        # Verify SDTM SUPPCE records
        assert record.sdtm_suppce_records is not None
        qnams = [s["QNAM"] for s in record.sdtm_suppce_records]
        assert "CECADJ" in qnams
        assert "CECADJR" in qnams
        assert "CECRATIO" in qnams


@pytest.mark.asyncio
async def test_cec_api_http_workflow_integration():
    """Integration test: API endpoints for dossier, chair adjudication, and SDTM compilation.

    @req:PRD-SYS-172, @req:PRD-SYS-173, @req:PRD-CEC-001, @req:PRD-CEC-002, @req:PRD-CEC-003, @req:ADR-2199
    """
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        headers = get_auth_headers(user_id="test_admin", roles="sponsor_admin")

        # 1. Package dossier
        res = await client.post(
            "/api/v1/execution/cec/dossiers",
            json={
                "study_id": "STUDY-INTEG-1",
                "site_id": "SITE-INTEG-1",
                "subject_id": "SUBJ-INT-01",
                "event_id": "EVT-INT-01",
                "candidate_event_type": "MYOCARDIAL_INFARCTION",
                "reported_term": "Acute STEMI",
                "verbatim_description": "Anterior STEMI",
                "reported_onset_date": "2026-08-21",
                "medical_narrative": "Patient Dr. Robert Smith treated at Saint Jude Hospital.",
                "reader_1_id": "rdr_1",
                "reader_2_id": "rdr_2",
                "chair_id": "chair_1",
            },
            headers=headers,
        )
        assert res.status_code == 201
        dossier_id = res.json()["dossier_id"]

        # 2. Get unblinded full dossier
        full_res = await client.get(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/full",
            headers=headers,
        )
        assert full_res.status_code == 200
        full_data = full_res.json()
        assert full_data["dossier_id"] == dossier_id
        assert full_data["is_deidentified"] is True

        # 3. Reader 1 assessment via API
        r1_headers = get_auth_headers(
            user_id="rdr_1", roles="sponsor_admin", reader_role="READER_1"
        )
        res_r1 = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json={
                "reader_role": "READER_1",
                "classification": "CONFIRMED_MI",
                "subtype": "Type 1 STEMI",
                "adjudicated_onset_date": "2026-08-21",
                "criteria_met": ["ST elevation in V1-V4"],
                "confidence_score": 5,
                "clinical_rationale": "Anterior STEMI",
            },
            headers=r1_headers,
        )
        assert res_r1.status_code == 200

        # 4. Reader 2 concordant assessment via API
        r2_headers = get_auth_headers(
            user_id="rdr_2", roles="sponsor_admin", reader_role="READER_2"
        )
        res_r2 = await client.post(
            f"/api/v1/execution/cec/dossiers/{dossier_id}/assessments",
            json={
                "reader_role": "READER_2",
                "classification": "CONFIRMED_MI",
                "subtype": "Type 1 STEMI",
                "adjudicated_onset_date": "2026-08-21",
                "criteria_met": ["ST elevation in V1-V4"],
                "confidence_score": 5,
                "clinical_rationale": "Anterior STEMI concur",
            },
            headers=r2_headers,
        )
        assert res_r2.status_code == 200
        assert res_r2.json()["workflow_status"] == "FINALIZED_CONCORDANT"

        # 5. Compile SDTM CE dataset
        sdtm_res = await client.post(
            "/api/v1/execution/cec/sdtm-ce/compile",
            json={"study_id": "STUDY-INTEG-1"},
            headers=headers,
        )
        assert sdtm_res.status_code == 200
        assert sdtm_res.json()["record_count"] >= 1
