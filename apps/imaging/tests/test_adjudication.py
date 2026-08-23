"""Unit tests for Blinded Dual-Reader Imaging Adjudication Engine.

@req:PRD-IMG-003
@req:PRD-SYS-001
"""

from apps.imaging.domain.models import LesionMeasurement
from apps.imaging.services.adjudication import (
    AdjudicationWorkflowStatus,
    DualReaderAdjudicationSession,
    ImagingAdjudicationEngine,
    ReaderAssessment,
    ReaderRole,
    RECISTResponse,
)


def test_sld_discordance_calculation():
    """Verify relative SLD difference calculations."""
    # Reader 1: 50mm, Reader 2: 55mm -> |50-55|/55 = 5/55 = 0.0909 (9.09% < 20%)
    delta_concordant = ImagingAdjudicationEngine.calculate_sld_discordance(50.0, 55.0)
    assert delta_concordant == 0.0909

    # Reader 1: 50mm, Reader 2: 70mm -> |50-70|/70 = 20/70 = 0.2857 (28.57% > 20%)
    delta_discordant = ImagingAdjudicationEngine.calculate_sld_discordance(50.0, 70.0)
    assert delta_discordant == 0.2857


def test_primary_reads_concordance_happy_path():
    """Verify that concordant primary reads auto-lock into consensus."""
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
    """Verify that divergent responses trigger tie-breaker escalation and adjudicator lock."""
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
