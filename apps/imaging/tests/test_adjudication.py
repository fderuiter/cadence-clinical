"""Unit tests for Blinded Dual-Reader Imaging Adjudication Engine and Deep Workflow Module.

@req:PRD-IMG-003
@req:PRD-IMG-005
@req:PRD-SYS-001
"""

from typing import Any

import pytest

from apps.imaging.domain.exceptions import (
    AdjudicationSessionNotFoundError,
    AdjudicationSignOffRationaleRequiredError,
)
from apps.imaging.domain.models import (
    AdjudicationWorkflowStatus,
    DualReaderAdjudicationSession,
    LesionMeasurement,
    OncologicCriteria,
    ReaderAssessment,
    ReaderRole,
    RECISTResponseCode,
)
from apps.imaging.ports.repository_port import ImagingAdjudicationRepositoryPort
from apps.imaging.services.adjudication import (
    AdjudicationWorkflow,
    ImagingAdjudicationEngine,
)

# Backward-compatible alias
RECISTResponse = RECISTResponseCode


class InMemoryImagingAdjudicationRepository(ImagingAdjudicationRepositoryPort):
    """In-memory test fake for ImagingAdjudicationRepositoryPort."""

    def __init__(self) -> None:
        self.sessions: dict[str, DualReaderAdjudicationSession] = {}
        self.tumor_assessments: list[dict[str, Any]] = []
        self.audit_logs: list[dict[str, Any]] = []

    async def get_by_id(
        self, entity_id: str, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession | None:
        return self.sessions.get(entity_id)

    async def save(
        self, entity: DualReaderAdjudicationSession, *args: Any, **kwargs: Any
    ) -> DualReaderAdjudicationSession:
        session_id = entity.session_id or entity.id or "ADJ-TEST"
        self.sessions[session_id] = entity
        return entity

    async def list_by_filters(
        self,
        study_id: str | None = None,
        subject_id: str | None = None,
        timepoint_name: str | None = None,
        workflow_status: str | None = None,
        skip: int = 0,
        limit: int = 50,
    ) -> list[DualReaderAdjudicationSession]:
        results = list(self.sessions.values())
        if study_id:
            results = [s for s in results if s.study_id == study_id]
        if subject_id:
            results = [s for s in results if s.subject_id == subject_id]
        if timepoint_name:
            results = [s for s in results if s.timepoint_name == timepoint_name]
        if workflow_status:
            results = [
                s
                for s in results
                if (
                    s.workflow_status.value
                    if hasattr(s.workflow_status, "value")
                    else str(s.workflow_status)
                )
                == workflow_status
            ]
        return results[skip : skip + limit]

    async def record_tumor_response_assessment(
        self,
        session: DualReaderAdjudicationSession,
        signature_digest: str,
        actor_id: str,
        rationale: str,
    ) -> None:
        self.tumor_assessments.append(
            {
                "session_id": session.session_id or session.id,
                "study_id": session.study_id,
                "subject_id": session.subject_id,
                "overall_response": session.final_consensus_response,
                "signature_digest": signature_digest,
                "actor_id": actor_id,
                "rationale": rationale,
            }
        )

    async def record_audit_log(
        self,
        action: str,
        actor_id: str,
        reason_for_change: str | None = None,
        details: dict[str, Any] | None = None,
    ) -> None:
        self.audit_logs.append(
            {
                "action": action,
                "actor_id": actor_id,
                "reason_for_change": reason_for_change,
                "details": details or {},
            }
        )


def test_sld_discordance_calculation():
    """Verify relative SLD difference calculations.

    @req:PRD-IMG-005
    @req:PRD-SYS-166
    """
    # Reader 1: 50mm, Reader 2: 55mm -> |50-55|/55 = 5/55 = 0.0909 (9.09% < 20%)
    delta_concordant = ImagingAdjudicationEngine.calculate_sld_discordance(50.0, 55.0)
    assert delta_concordant == 0.0909

    # Reader 1: 50mm, Reader 2: 70mm -> |50-70|/70 = 20/70 = 0.2857 (28.57% > 20%)
    delta_discordant = ImagingAdjudicationEngine.calculate_sld_discordance(50.0, 70.0)
    assert delta_discordant == 0.2857


def test_primary_reads_concordance_happy_path():
    """Verify that concordant primary reads auto-lock into consensus.

    @req:PRD-IMG-003
    @req:PRD-SYS-166
    """
    session = DualReaderAdjudicationSession(
        session_id="ADJ-SESS-001",
        study_envelope_id="ENV-101",
        study_id="STUDY-ONCOLOGY",
        subject_id="SUB-999",
        timepoint_name="VISIT_02",
    )

    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="VISIT_02",
        target_lesions=[
            LesionMeasurement(
                lesion_id="L1",
                organ_site="LIVER",
                longest_diameter_mm=30.0,
                sop_instance_uid="sop_001",
            )
        ],
        sum_longest_diameters_mm=30.0,
        overall_response=RECISTResponse.PR,
    )

    r2 = ReaderAssessment(
        reader_id="rad_02",
        reader_role=ReaderRole.READER_2,
        timepoint_name="VISIT_02",
        target_lesions=[
            LesionMeasurement(
                lesion_id="L1",
                organ_site="LIVER",
                longest_diameter_mm=32.0,
                sop_instance_uid="sop_001",
            )
        ],
        sum_longest_diameters_mm=32.0,
        overall_response=RECISTResponse.PR,
    )

    updated = ImagingAdjudicationEngine.evaluate_primary_reads(session, r1, r2)
    assert updated.workflow_status == AdjudicationWorkflowStatus.CONSENSUS_REACHED
    assert updated.final_consensus_response == RECISTResponse.PR
    assert updated.final_consensus_sld_mm == 31.0
    assert updated.locked_at is not None
    assert updated.session_digest != ""


def test_primary_reads_discordance_and_tie_breaker_escalation():
    """Verify that divergent responses trigger tie-breaker escalation and adjudicator lock.

    @req:PRD-IMG-005
    @req:PRD-SYS-166
    """
    session = DualReaderAdjudicationSession(
        session_id="ADJ-SESS-002",
        study_envelope_id="ENV-102",
        study_id="STUDY-ONCOLOGY",
        subject_id="SUB-999",
        timepoint_name="VISIT_03",
    )

    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="VISIT_03",
        sum_longest_diameters_mm=25.0,
        overall_response=RECISTResponse.PR,
    )

    r2 = ReaderAssessment(
        reader_id="rad_02",
        reader_role=ReaderRole.READER_2,
        timepoint_name="VISIT_03",
        sum_longest_diameters_mm=45.0,  # Discordant SLD and response (PD)
        overall_response=RECISTResponse.PD,
    )

    updated = ImagingAdjudicationEngine.evaluate_primary_reads(session, r1, r2)
    assert updated.workflow_status == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED
    assert updated.is_response_discordant is True
    assert updated.final_consensus_response is None

    # Expert adjudicator tie-breaker assessment
    adjudicator = ReaderAssessment(
        reader_id="expert_rad_99",
        reader_role=ReaderRole.ADJUDICATOR,
        timepoint_name="VISIT_03",
        sum_longest_diameters_mm=26.0,
        overall_response=RECISTResponse.PR,
        assessment_notes="Reader 2 incorrectly included non-target lymph node in SLD.",
    )

    adjudicated = ImagingAdjudicationEngine.apply_adjudicator_decision(
        session=updated,
        adjudicator_assessment=adjudicator,
        rationale="Overruled Reader 2 per imaging charter protocol Section 4.2",
        locked_by="expert_rad_99",
    )

    assert adjudicated.workflow_status == AdjudicationWorkflowStatus.ADJUDICATED
    assert adjudicated.final_consensus_response == RECISTResponse.PR
    assert adjudicated.final_consensus_sld_mm == 26.0
    assert adjudicated.locked_by == "expert_rad_99"
    assert adjudicated.version_index == 2
    assert adjudicated.session_digest != ""


# =========================================================================
# Deep Module Tests: AdjudicationWorkflow with In-Memory Repository Fake
# =========================================================================


@pytest.mark.asyncio
async def test_workflow_submit_reader_assessment_lifecycle():
    """Verify AdjudicationWorkflow encapsulates multi-step dual-reader progression.

    @req:PRD-IMG-003
    @req:PRD-SYS-166
    """
    repo = InMemoryImagingAdjudicationRepository()
    workflow = AdjudicationWorkflow(repo)

    initial_session = DualReaderAdjudicationSession(
        session_id="ADJ-WF-001",
        study_id="STUDY-101",
        subject_id="SUBJ-001",
        timepoint_name="CYCLE_1_DAY_1",
        assessment_criteria=OncologicCriteria.RECIST_1_1,
        reader_1_id="rad_01",
        reader_2_id="rad_02",
        adjudicator_id="chair_01",
    )
    created = await workflow.create_session(initial_session, actor_id="admin_01")
    assert created.session_id == "ADJ-WF-001"
    assert len(repo.audit_logs) == 1
    assert repo.audit_logs[0]["action"] == "ADJUDICATION_SESSION_CREATED"

    # 1. Reader 1 Assessment
    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_1_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=20.0,
                sop_instance_uid="sop_01",
            )
        ],
        sum_longest_diameters_mm=20.0,
        overall_response=RECISTResponse.PR,
    )
    s1 = await workflow.submit_reader_assessment("ADJ-WF-001", r1, actor_id="rad_01")
    assert s1.reader_1_status == "COMPLETED"
    assert s1.workflow_status == AdjudicationWorkflowStatus.PENDING_READ_2
    assert len(repo.audit_logs) == 2

    # 2. Reader 2 Concordant Assessment
    r2 = ReaderAssessment(
        reader_id="rad_02",
        reader_role=ReaderRole.READER_2,
        timepoint_name="CYCLE_1_DAY_1",
        target_lesions=[
            LesionMeasurement(
                lesion_id="TL1",
                organ_site="LIVER",
                longest_diameter_mm=21.0,
                sop_instance_uid="sop_01",
            )
        ],
        sum_longest_diameters_mm=21.0,
        overall_response=RECISTResponse.PR,
    )
    s2 = await workflow.submit_reader_assessment("ADJ-WF-001", r2, actor_id="rad_02")
    assert s2.reader_2_status == "COMPLETED"
    assert s2.workflow_status == AdjudicationWorkflowStatus.CONSENSUS_REACHED
    assert s2.final_consensus_response == RECISTResponse.PR
    assert s2.final_consensus_sld_mm == 20.5
    assert s2.locked_by == "system_concordance_evaluator"
    assert len(repo.audit_logs) == 3


@pytest.mark.asyncio
async def test_workflow_discordance_and_part11_signoff():
    """Verify discordance detection, 21 CFR Part 11 cryptographic sealing, and tumor response sync.

    @req:PRD-IMG-005
    @req:PRD-SYS-001
    @req:PRD-SYS-166
    """
    repo = InMemoryImagingAdjudicationRepository()
    workflow = AdjudicationWorkflow(repo)

    initial_session = DualReaderAdjudicationSession(
        session_id="ADJ-WF-002",
        study_id="STUDY-101",
        subject_id="SUBJ-002",
        timepoint_name="CYCLE_2_DAY_1",
        reader_1_id="rad_01",
        reader_2_id="rad_02",
        adjudicator_id="chair_01",
    )
    await workflow.create_session(initial_session, actor_id="admin_01")

    # Reader 1: PR (20mm)
    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_2_DAY_1",
        sum_longest_diameters_mm=20.0,
        overall_response=RECISTResponse.PR,
    )
    await workflow.submit_reader_assessment("ADJ-WF-002", r1, actor_id="rad_01")

    # Reader 2: PD (45mm + new lesion) -> Discordant
    r2 = ReaderAssessment(
        reader_id="rad_02",
        reader_role=ReaderRole.READER_2,
        timepoint_name="CYCLE_2_DAY_1",
        sum_longest_diameters_mm=45.0,
        new_lesions_present=True,
        overall_response=RECISTResponse.PD,
    )
    disc = await workflow.submit_reader_assessment("ADJ-WF-002", r2, actor_id="rad_02")
    assert disc.workflow_status == AdjudicationWorkflowStatus.DISCORDANCE_DETECTED
    assert disc.is_response_discordant is True

    # Attempt sign-off with empty rationale -> raises AdjudicationSignOffRationaleRequiredError
    with pytest.raises(AdjudicationSignOffRationaleRequiredError):
        await workflow.sign_off_adjudication(
            session_id="ADJ-WF-002",
            signer_id="chair_01",
            signer_name="Dr. Chair",
            signing_reason="ADJUDICATION_SIGN_OFF",
            rationale="   ",  # whitespace only
        )

    # Valid Part 11 sign-off
    signed = await workflow.sign_off_adjudication(
        session_id="ADJ-WF-002",
        signer_id="chair_01",
        signer_name="Dr. Arthur Chair",
        signing_reason="ADJUDICATION_SIGN_OFF",
        rationale="Overruled Reader 2 after reviewing axial contrast CT. Reader 2 artifact identified as portal vein.",
        binding_reader="READER_1",
    )
    assert signed.workflow_status == AdjudicationWorkflowStatus.ADJUDICATED
    assert signed.final_consensus_response == RECISTResponse.PR
    assert signed.final_consensus_sld_mm == 20.0
    assert signed.binding_reader == "READER_1"
    assert signed.electronic_signature is not None
    assert signed.electronic_signature["step_up_verified"] is True
    assert len(repo.tumor_assessments) == 1
    assert repo.tumor_assessments[0]["actor_id"] == "chair_01"


@pytest.mark.asyncio
async def test_workflow_session_not_found():
    """Verify AdjudicationSessionNotFoundError is raised for non-existent sessions.

    @req:PRD-SYS-166
    """
    repo = InMemoryImagingAdjudicationRepository()
    workflow = AdjudicationWorkflow(repo)

    r1 = ReaderAssessment(
        reader_id="rad_01",
        reader_role=ReaderRole.READER_1,
        timepoint_name="CYCLE_1_DAY_1",
        overall_response=RECISTResponse.PR,
    )
    with pytest.raises(AdjudicationSessionNotFoundError):
        await workflow.submit_reader_assessment("NON_EXISTENT", r1, actor_id="rad_01")

    with pytest.raises(AdjudicationSessionNotFoundError):
        await workflow.sign_off_adjudication(
            session_id="NON_EXISTENT",
            signer_id="chair_01",
            signer_name="Dr. Chair",
            signing_reason="ADJUDICATION_SIGN_OFF",
            rationale="Valid clinical rationale",
        )
