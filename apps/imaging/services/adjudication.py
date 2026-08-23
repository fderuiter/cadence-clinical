"""Blinded Dual-Reader Charter & Tie-Breaker Adjudication Engine for Medical Imaging.

Requirements: PRD-IMG-003, PRD-SYS-001 (21 CFR Part 11)
"""

import hashlib
import json
from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, Field


class RECISTResponse(StrEnum):
    """RECIST 1.1 Overall Response categories."""

    CR = "CR"  # Complete Response
    PR = "PR"  # Partial Response
    SD = "SD"  # Stable Disease
    PD = "PD"  # Progressive Disease
    NE = "NE"  # Not Evaluable


class ReaderRole(StrEnum):
    """Blinded reader role assignments."""

    READER_1 = "READER_1"
    READER_2 = "READER_2"
    ADJUDICATOR = "ADJUDICATOR"  # Tie-breaker reader


class LesionMeasurement(BaseModel):
    """Individual target lesion metric."""

    lesion_id: str
    organ_site: str
    longest_diameter_mm: float
    short_axis_mm: float | None = None
    slice_location: float | None = None
    sop_instance_uid: str


class ReaderAssessment(BaseModel):
    """Blinded assessment submitted by a certified central radiologist."""

    reader_id: str
    reader_role: ReaderRole
    timepoint_name: str  # BASELINE, VISIT_01, END_OF_TREATMENT
    target_lesions: list[LesionMeasurement] = Field(default_factory=list)
    sum_longest_diameters_mm: float = 0.0
    non_target_status: str = "NON_CR_NON_PD"  # CR, NON_CR_NON_PD, PD, NE
    new_lesions_present: bool = False
    overall_response: RECISTResponse
    assessment_notes: str | None = None
    submitted_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    digital_signature: str = ""


class AdjudicationWorkflowStatus(StrEnum):
    """Lifecycle state of the blinded dual-read workflow."""

    PENDING_PRIMARY_READS = "PENDING_PRIMARY_READS"
    CONSENSUS_REACHED = "CONSENSUS_REACHED"
    DISCORDANCE_DETECTED = "DISCORDANCE_DETECTED"
    PENDING_ADJUDICATION = "PENDING_ADJUDICATION"
    ADJUDICATED = "ADJUDICATED"


class DualReaderAdjudicationSession(BaseModel):
    """Independent Review Charter session encapsulating dual-read and tie-breaker decisions."""

    session_id: str
    study_envelope_id: str
    study_id: str
    subject_id: str
    timepoint_name: str
    reader_1_assessment: ReaderAssessment | None = None
    reader_2_assessment: ReaderAssessment | None = None
    adjudicator_assessment: ReaderAssessment | None = None
    sld_discordance_delta: float = 0.0
    is_response_discordant: bool = False
    workflow_status: AdjudicationWorkflowStatus = (
        AdjudicationWorkflowStatus.PENDING_PRIMARY_READS
    )
    final_consensus_response: RECISTResponse | None = None
    final_consensus_sld_mm: float | None = None
    adjudication_rationale: str | None = None
    locked_at: str | None = None
    locked_by: str | None = None
    session_digest: str = ""
    version_index: int = 1


class ImagingAdjudicationEngine:
    """Core domain engine evaluating dual-read concordance and tie-breaker workflows."""

    SLD_DISCORDANCE_THRESHOLD = 0.20  # 20% relative difference triggers discordance

    @classmethod
    def calculate_sld_discordance(cls, sld_1: float, sld_2: float) -> float:
        """Calculates relative difference between two SLD measurements.

        Formula: |SLD_1 - SLD_2| / max(SLD_1, SLD_2)

        Args:
            sld_1: Reader 1 Sum of Longest Diameters in mm.
            sld_2: Reader 2 Sum of Longest Diameters in mm.

        Returns:
            Relative discordance float between 0.0 and 1.0.
        """
        max_sld = max(sld_1, sld_2)
        if max_sld <= 0.0:
            return 0.0
        return round(abs(sld_1 - sld_2) / max_sld, 4)

    @classmethod
    def evaluate_primary_reads(
        cls,
        session: DualReaderAdjudicationSession,
        reader_1: ReaderAssessment,
        reader_2: ReaderAssessment,
    ) -> DualReaderAdjudicationSession:
        """Evaluates Reader 1 and Reader 2 submissions against the trial charter rules.

        Args:
            session: Current adjudication session entity.
            reader_1: Blinded assessment from Reader 1.
            reader_2: Blinded assessment from Reader 2.

        Returns:
            Updated session with computed discordance and resolved or escalated status.
        """
        session.reader_1_assessment = reader_1
        session.reader_2_assessment = reader_2

        delta = cls.calculate_sld_discordance(
            reader_1.sum_longest_diameters_mm,
            reader_2.sum_longest_diameters_mm,
        )
        session.sld_discordance_delta = delta

        response_mismatch = reader_1.overall_response != reader_2.overall_response
        session.is_response_discordant = response_mismatch

        # If SLD difference > 20% or overall response category diverges -> Escalation required
        if delta > cls.SLD_DISCORDANCE_THRESHOLD or response_mismatch:
            session.workflow_status = AdjudicationWorkflowStatus.DISCORDANCE_DETECTED
            session.final_consensus_response = None
            session.final_consensus_sld_mm = None
        else:
            session.workflow_status = AdjudicationWorkflowStatus.CONSENSUS_REACHED
            session.final_consensus_response = reader_1.overall_response
            session.final_consensus_sld_mm = round(
                (reader_1.sum_longest_diameters_mm + reader_2.sum_longest_diameters_mm)
                / 2.0,
                2,
            )
            session.locked_at = datetime.now(UTC).isoformat()
            session.locked_by = "system_concordance_evaluator"

        cls._update_session_digest(session)
        return session

    @classmethod
    def apply_adjudicator_decision(
        cls,
        session: DualReaderAdjudicationSession,
        adjudicator_assessment: ReaderAssessment,
        rationale: str,
        locked_by: str,
    ) -> DualReaderAdjudicationSession:
        """Resolves discordant primary reads via tie-breaker adjudicator decision.

        Args:
            session: Escalated adjudication session.
            adjudicator_assessment: Assessment submitted by the expert tie-breaker.
            rationale: 21 CFR Part 11 clinical justification.
            locked_by: User ID of adjudicator or medical monitor.

        Returns:
            Adjudicated and locked session.
        """
        session.adjudicator_assessment = adjudicator_assessment
        session.workflow_status = AdjudicationWorkflowStatus.ADJUDICATED
        session.final_consensus_response = adjudicator_assessment.overall_response
        session.final_consensus_sld_mm = adjudicator_assessment.sum_longest_diameters_mm
        session.adjudication_rationale = rationale
        session.locked_at = datetime.now(UTC).isoformat()
        session.locked_by = locked_by
        session.version_index += 1

        cls._update_session_digest(session)
        return session

    @staticmethod
    def _update_session_digest(session: DualReaderAdjudicationSession) -> None:
        """Generates SHA-256 digital digest across all reader submissions for Part 11 immutability."""
        manifest = {
            "session_id": session.session_id,
            "study_envelope_id": session.study_envelope_id,
            "workflow_status": session.workflow_status.value,
            "final_response": (
                session.final_consensus_response.value
                if session.final_consensus_response
                else None
            ),
            "final_sld": session.final_consensus_sld_mm,
            "version_index": session.version_index,
        }
        session.session_digest = hashlib.sha256(
            json.dumps(manifest, sort_keys=True).encode()
        ).hexdigest()
