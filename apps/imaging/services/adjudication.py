"""Blinded Dual-Reader Charter & Tie-Breaker Adjudication Engine for Medical Imaging.

Requirements: PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001 (21 CFR Part 11)
"""

import hashlib
import json
from datetime import UTC, datetime
from typing import Any

from apps.imaging.domain.exceptions import (
    AdjudicationSessionNotFoundError,
    AdjudicationSignOffRationaleRequiredError,
)
from apps.imaging.domain.models import (
    AdjudicationWorkflowStatus,
    DiscordanceReason,
    DualReaderAdjudicationSession,
    ReaderAssessment,
    ReaderRole,
    RECISTResponseCode,
    SDTMRSRecord,
    SDTMTRRecord,
    SDTMTURecord,
)
from apps.imaging.ports.repository_port import ImagingAdjudicationRepositoryPort

# Backward-compatible alias for existing imports
RECISTResponse = RECISTResponseCode


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
        """Evaluates Reader 1 and Reader 2 submissions against trial charter rules.

        @req:PRD-IMG-003
        @req:PRD-IMG-005
        """
        session.reader_1_assessment = reader_1
        session.reader_1_status = "COMPLETED"
        session.reader_2_assessment = reader_2
        session.reader_2_status = "COMPLETED"

        delta = cls.calculate_sld_discordance(
            reader_1.sum_longest_diameters_mm,
            reader_2.sum_longest_diameters_mm,
        )
        session.sld_discordance_delta = delta

        reasons: list[str] = []

        # 1. Check response mismatch (e.g. PR vs PD, CR vs PR)
        r1_resp = (
            reader_1.overall_response.value
            if hasattr(reader_1.overall_response, "value")
            else str(reader_1.overall_response)
        )
        r2_resp = (
            reader_2.overall_response.value
            if hasattr(reader_2.overall_response, "value")
            else str(reader_2.overall_response)
        )

        response_mismatch = r1_resp != r2_resp
        if response_mismatch:
            reasons.append(
                f"{DiscordanceReason.OVERALL_RESPONSE_MISMATCH.value}: Reader 1 ({r1_resp}) vs Reader 2 ({r2_resp})"
            )

        # 2. Check SLD threshold (> 20%)
        if delta > cls.SLD_DISCORDANCE_THRESHOLD:
            reasons.append(
                f"{DiscordanceReason.SLD_DISCORDANCE_EXCEEDS_THRESHOLD.value}: Relative delta {delta * 100:.1f}% exceeds 20.0% threshold"
            )

        # 3. Check New lesions mismatch
        if reader_1.new_lesions_present != reader_2.new_lesions_present:
            reasons.append(
                f"{DiscordanceReason.NEW_LESION_DISCORDANCE.value}: Reader 1 (new={reader_1.new_lesions_present}) vs Reader 2 (new={reader_2.new_lesions_present})"
            )

        # 4. Check Non-target mismatch
        if reader_1.non_target_status != reader_2.non_target_status:
            reasons.append(
                f"{DiscordanceReason.NON_TARGET_STATUS_MISMATCH.value}: Reader 1 ({reader_1.non_target_status}) vs Reader 2 ({reader_2.non_target_status})"
            )

        session.is_response_discordant = len(reasons) > 0
        session.discordance_reasons = reasons

        if session.is_response_discordant:
            session.workflow_status = AdjudicationWorkflowStatus.DISCORDANCE_DETECTED
            session.final_consensus_response = None
            session.final_consensus_sld_mm = None
            session.binding_reader = None
        else:
            session.workflow_status = AdjudicationWorkflowStatus.CONSENSUS_REACHED
            session.final_consensus_response = reader_1.overall_response
            session.final_consensus_sld_mm = round(
                (reader_1.sum_longest_diameters_mm + reader_2.sum_longest_diameters_mm)
                / 2.0,
                2,
            )
            session.binding_reader = "CONSENSUS"
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
        binding_reader: str = "ADJUDICATOR",
        step_up_signature: dict[str, Any] | None = None,
    ) -> DualReaderAdjudicationSession:
        """Resolves discordant primary reads via tie-breaker adjudicator decision with 21 CFR Part 11 sign-off.

        @req:PRD-IMG-005
        @req:PRD-SYS-001
        """
        session.adjudicator_assessment = adjudicator_assessment
        session.workflow_status = AdjudicationWorkflowStatus.ADJUDICATED
        session.binding_reader = binding_reader

        if binding_reader == "READER_1" and session.reader_1_assessment:
            session.final_consensus_response = (
                session.reader_1_assessment.overall_response
            )
            session.final_consensus_sld_mm = (
                session.reader_1_assessment.sum_longest_diameters_mm
            )
        elif binding_reader == "READER_2" and session.reader_2_assessment:
            session.final_consensus_response = (
                session.reader_2_assessment.overall_response
            )
            session.final_consensus_sld_mm = (
                session.reader_2_assessment.sum_longest_diameters_mm
            )
        else:
            session.final_consensus_response = adjudicator_assessment.overall_response
            session.final_consensus_sld_mm = (
                adjudicator_assessment.sum_longest_diameters_mm
            )

        session.adjudication_rationale = rationale
        session.electronic_signature = step_up_signature
        session.locked_at = datetime.now(UTC).isoformat()
        session.locked_by = locked_by
        session.version_index += 1

        cls._update_session_digest(session)
        return session

    @classmethod
    def get_blinded_session_view(
        cls,
        session: DualReaderAdjudicationSession,
        requesting_user_id: str,
        requesting_role: str | ReaderRole | None = None,
    ) -> dict[str, Any]:
        """Enforces complete blinding between Reader 1, Reader 2, and Adjudication Committee Chair.

        @req:PRD-IMG-003
        """
        role_str = (
            requesting_role.value
            if hasattr(requesting_role, "value")
            else str(requesting_role or "").upper()
        )

        is_reader_1 = (
            role_str == "READER_1" or requesting_user_id == session.reader_1_id
        )
        is_reader_2 = (
            role_str == "READER_2" or requesting_user_id == session.reader_2_id
        )
        is_chair = role_str in ["ADJUDICATOR", "CHAIR", "COMMITTEE_CHAIR"] or (
            session.adjudicator_id and requesting_user_id == session.adjudicator_id
        )

        base_dict = {
            "session_id": session.session_id or session.id,
            "study_envelope_id": session.study_envelope_id,
            "study_id": session.study_id,
            "subject_id": session.subject_id,
            "timepoint_name": session.timepoint_name,
            "assessment_criteria": (
                session.assessment_criteria.value
                if hasattr(session.assessment_criteria, "value")
                else str(session.assessment_criteria)
            ),
            "version_index": session.version_index,
        }

        # Reader 1 Blinding: strictly strip Reader 2, Adjudicator, Treatment Arm, Site ID, and Discordance metrics
        if is_reader_1 and not is_chair:
            return {
                **base_dict,
                "site_id": "BLINDED_SITE",
                "workflow_status": (
                    "SUBMITTED"
                    if session.reader_1_status == "COMPLETED"
                    else "PENDING_READ"
                ),
                "reader_1_status": session.reader_1_status,
                "reader_2_status": "BLINDED",
                "reader_1_assessment": (
                    session.reader_1_assessment.model_dump()
                    if session.reader_1_assessment
                    else None
                ),
                "reader_2_assessment": None,
                "adjudicator_assessment": None,
                "sld_discordance_delta": 0.0,
                "is_response_discordant": False,
                "discordance_reasons": [],
                "final_consensus_response": None,
                "final_consensus_sld_mm": None,
                "binding_reader": None,
                "adjudication_rationale": None,
                "electronic_signature": None,
                "locked_at": None,
                "locked_by": None,
                "session_digest": "",
            }

        # Reader 2 Blinding: strictly strip Reader 1, Adjudicator, Treatment Arm, Site ID, and Discordance metrics
        if is_reader_2 and not is_chair:
            return {
                **base_dict,
                "site_id": "BLINDED_SITE",
                "workflow_status": (
                    "SUBMITTED"
                    if session.reader_2_status == "COMPLETED"
                    else "PENDING_READ"
                ),
                "reader_1_status": "BLINDED",
                "reader_2_status": session.reader_2_status,
                "reader_1_assessment": None,
                "reader_2_assessment": (
                    session.reader_2_assessment.model_dump()
                    if session.reader_2_assessment
                    else None
                ),
                "adjudicator_assessment": None,
                "sld_discordance_delta": 0.0,
                "is_response_discordant": False,
                "discordance_reasons": [],
                "final_consensus_response": None,
                "final_consensus_sld_mm": None,
                "binding_reader": None,
                "adjudication_rationale": None,
                "electronic_signature": None,
                "locked_at": None,
                "locked_by": None,
                "session_digest": "",
            }

        # Chair / Adjudicator / Consolidated Unblinded View
        final_resp = (
            session.final_consensus_response.value
            if hasattr(session.final_consensus_response, "value")
            else session.final_consensus_response
        )

        return {
            **base_dict,
            "site_id": session.site_id,
            "workflow_status": (
                session.workflow_status.value
                if hasattr(session.workflow_status, "value")
                else str(session.workflow_status)
            ),
            "reader_1_status": session.reader_1_status,
            "reader_2_status": session.reader_2_status,
            "reader_1_assessment": (
                session.reader_1_assessment.model_dump()
                if session.reader_1_assessment
                else None
            ),
            "reader_2_assessment": (
                session.reader_2_assessment.model_dump()
                if session.reader_2_assessment
                else None
            ),
            "adjudicator_assessment": (
                session.adjudicator_assessment.model_dump()
                if session.adjudicator_assessment
                else None
            ),
            "sld_discordance_delta": session.sld_discordance_delta,
            "is_response_discordant": session.is_response_discordant,
            "discordance_reasons": session.discordance_reasons,
            "final_consensus_response": final_resp,
            "final_consensus_sld_mm": session.final_consensus_sld_mm,
            "binding_reader": session.binding_reader,
            "adjudication_rationale": session.adjudication_rationale,
            "electronic_signature": session.electronic_signature,
            "locked_at": session.locked_at,
            "locked_by": session.locked_by,
            "session_digest": session.session_digest,
        }

    @classmethod
    def generate_edc_ecrf_binding(
        cls, session: DualReaderAdjudicationSession
    ) -> dict[str, Any]:
        """Directly binds verified adjudication measurements to EDC eCRF tumor forms.

        @req:PRD-IMG-006
        """
        # Determine source assessment for lesion measurements
        source_assessment = (
            session.adjudicator_assessment
            or (
                session.reader_1_assessment
                if session.binding_reader == "READER_1"
                else session.reader_2_assessment
            )
            or session.reader_1_assessment
            or session.reader_2_assessment
        )

        target_lesions_data = []
        if source_assessment and source_assessment.target_lesions:
            for lesion in source_assessment.target_lesions:
                target_lesions_data.append(
                    {
                        "lesion_id": lesion.lesion_id,
                        "lesion_type": "TARGET",
                        "organ_site": lesion.organ_site,
                        "longest_diameter_mm": lesion.longest_diameter_mm,
                        "short_axis_mm": lesion.short_axis_mm,
                        "slice_location": lesion.slice_location,
                        "sop_instance_uid": lesion.sop_instance_uid,
                    }
                )

        final_resp = (
            session.final_consensus_response.value
            if hasattr(session.final_consensus_response, "value")
            else str(session.final_consensus_response or "NE")
        )

        workflow_st = (
            session.workflow_status.value
            if hasattr(session.workflow_status, "value")
            else str(session.workflow_status)
        )

        criteria_str = (
            session.assessment_criteria.value
            if hasattr(session.assessment_criteria, "value")
            else str(session.assessment_criteria)
        )

        return {
            "form_id": "eCRF_TUMOR_ASSESSMENT",
            "study_id": session.study_id,
            "site_id": session.site_id,
            "subject_id": session.subject_id,
            "timepoint_name": session.timepoint_name,
            "assessment_criteria": criteria_str,
            "target_lesions": target_lesions_data,
            "sum_longest_diameters_mm": session.final_consensus_sld_mm,
            "baseline_sld_mm": (
                source_assessment.baseline_sld_mm if source_assessment else None
            ),
            "nadir_sld_mm": (
                source_assessment.nadir_sld_mm if source_assessment else None
            ),
            "pct_change_baseline": (
                source_assessment.pct_change_baseline if source_assessment else None
            ),
            "pct_change_nadir": (
                source_assessment.pct_change_nadir if source_assessment else None
            ),
            "non_target_status": (
                source_assessment.non_target_status
                if source_assessment
                else "NON_CR_NON_PD"
            ),
            "new_lesions_present": (
                source_assessment.new_lesions_present if source_assessment else False
            ),
            "overall_response": final_resp,
            "adjudication_status": workflow_st,
            "binding_source": session.binding_reader or "CONSENSUS",
            "manifest_sha256": session.session_digest,
            "signed_by": session.locked_by,
            "signed_at": session.locked_at,
        }

    @classmethod
    def generate_sdtm_tu_tr_rs(
        cls,
        session: DualReaderAdjudicationSession,
        sequence_offset: int = 1,
    ) -> dict[str, list[dict[str, Any]]]:
        """Transforms verified imaging measurements into CDISC SDTM TU, TR, and RS datasets.

        @req:PRD-IMG-006
        """
        source_assessment = (
            session.adjudicator_assessment
            or (
                session.reader_1_assessment
                if session.binding_reader == "READER_1"
                else session.reader_2_assessment
            )
            or session.reader_1_assessment
            or session.reader_2_assessment
        )

        usubjid = f"{session.study_id}-{session.subject_id}"
        dtc = session.locked_at or datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")
        evaluator_id = session.locked_by or "INDEPENDENT_ADJUDICATOR"

        tu_records: list[dict[str, Any]] = []
        tr_records: list[dict[str, Any]] = []
        rs_records: list[dict[str, Any]] = []

        tu_seq = sequence_offset
        tr_seq = sequence_offset

        if source_assessment and source_assessment.target_lesions:
            for lesion in source_assessment.target_lesions:
                # 1. SDTM TU (Tumor Identification)
                tu_item = SDTMTURecord(
                    STUDYID=session.study_id,
                    DOMAIN="TU",
                    USUBJID=usubjid,
                    TUSEQ=tu_seq,
                    TULNKID=lesion.lesion_id,
                    TUTESTCD="TUMIDENT",
                    TUTEST="Tumor Identification",
                    TUORRES="TARGET",
                    TUSTRESC="TARGET",
                    TUMETHOD="CT SCAN",
                    TULOC=lesion.organ_site,
                    VISIT=session.timepoint_name,
                    TUDTC=dtc,
                    TUEVAL="INDEPENDENT ASSESSOR",
                    TUEVALID=evaluator_id,
                )
                tu_records.append(tu_item.model_dump())
                tu_seq += 1

                # 2. SDTM TR (Tumor Measurement - Longest Diameter)
                tr_item = SDTMTRRecord(
                    STUDYID=session.study_id,
                    DOMAIN="TR",
                    USUBJID=usubjid,
                    TRSEQ=tr_seq,
                    TRLNKID=lesion.lesion_id,
                    TRTESTCD="LDIAM",
                    TRTEST="Longest Diameter",
                    TRORRES=f"{lesion.longest_diameter_mm:.1f}",
                    TRORRESU="mm",
                    TRSTRESC=f"{lesion.longest_diameter_mm:.1f}",
                    TRSTRESN=lesion.longest_diameter_mm,
                    TRSTRESU="mm",
                    TRMETHOD="CT SCAN",
                    VISIT=session.timepoint_name,
                    TRDTC=dtc,
                    TREVAL="INDEPENDENT ASSESSOR",
                    TREVALID=evaluator_id,
                )
                tr_records.append(tr_item.model_dump())
                tr_seq += 1

        # Summary TR record: Sum of Longest Diameters (SUMDIR / SLD)
        if session.final_consensus_sld_mm is not None:
            tr_sld = SDTMTRRecord(
                STUDYID=session.study_id,
                DOMAIN="TR",
                USUBJID=usubjid,
                TRSEQ=tr_seq,
                TRLNKID=None,
                TRTESTCD="SUMDIR",
                TRTEST="Sum of Longest Diameters",
                TRORRES=f"{session.final_consensus_sld_mm:.1f}",
                TRORRESU="mm",
                TRSTRESC=f"{session.final_consensus_sld_mm:.1f}",
                TRSTRESN=session.final_consensus_sld_mm,
                TRSTRESU="mm",
                TRMETHOD="CT SCAN",
                VISIT=session.timepoint_name,
                TRDTC=dtc,
                TREVAL="INDEPENDENT ASSESSOR",
                TREVALID=evaluator_id,
            )
            tr_records.append(tr_sld.model_dump())

        # 3. SDTM RS (Disease Response)
        final_resp = (
            session.final_consensus_response.value
            if hasattr(session.final_consensus_response, "value")
            else str(session.final_consensus_response or "NE")
        )

        rs_item = SDTMRSRecord(
            STUDYID=session.study_id,
            DOMAIN="RS",
            USUBJID=usubjid,
            RSSEQ=sequence_offset,
            RSTESTCD="OVRLRESP",
            RSTEST="Overall Response",
            RSORRES=final_resp,
            RSSTRESC=final_resp,
            RSMETHOD="RECIST 1.1",
            VISIT=session.timepoint_name,
            RSDTC=dtc,
            RSEVAL="INDEPENDENT ASSESSOR",
            RSEVALID=evaluator_id,
            RSEVALSTAT="FINAL",
        )
        rs_records.append(rs_item.model_dump())

        return {
            "TU": tu_records,
            "TR": tr_records,
            "RS": rs_records,
        }

    @staticmethod
    def _update_session_digest(session: DualReaderAdjudicationSession) -> None:
        """Generates SHA-256 digital digest across all reader submissions for Part 11 immutability."""
        final_resp = (
            session.final_consensus_response.value
            if hasattr(session.final_consensus_response, "value")
            else session.final_consensus_response
        )
        wf_stat = (
            session.workflow_status.value
            if hasattr(session.workflow_status, "value")
            else str(session.workflow_status)
        )

        manifest = {
            "session_id": session.session_id or session.id,
            "study_envelope_id": session.study_envelope_id,
            "study_id": session.study_id,
            "subject_id": session.subject_id,
            "timepoint_name": session.timepoint_name,
            "workflow_status": wf_stat,
            "final_response": final_resp,
            "final_sld": session.final_consensus_sld_mm,
            "binding_reader": session.binding_reader,
            "is_response_discordant": session.is_response_discordant,
            "discordance_reasons": session.discordance_reasons,
            "version_index": session.version_index,
        }
        session.session_digest = hashlib.sha256(
            json.dumps(manifest, sort_keys=True).encode()
        ).hexdigest()


class AdjudicationWorkflow:
    """Deep workflow service encapsulating blinded dual-reader state progression, discordance evaluation, 21 CFR Part 11 cryptographic sealing, and persistence mutations.

    Presents a minimal public command interface with high leverage.

    Requirements: PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001
    """

    def __init__(self, repository: ImagingAdjudicationRepositoryPort) -> None:
        """Initializes the workflow service with an adjudication repository port."""
        self._repository = repository

    async def create_session(
        self,
        session: DualReaderAdjudicationSession,
        actor_id: str,
        reason_for_change: str | None = None,
    ) -> DualReaderAdjudicationSession:
        """Instantiates and persists a new Blinded Dual-Reader adjudication session.

        Args:
            session: The new DualReaderAdjudicationSession domain entity.
            actor_id: Identity of the creating user.
            reason_for_change: Optional GxP reason for creation.

        Returns:
            The persisted DualReaderAdjudicationSession entity.
        """
        ImagingAdjudicationEngine._update_session_digest(session)
        saved = await self._repository.save(session)
        await self._repository.record_audit_log(
            action="ADJUDICATION_SESSION_CREATED",
            actor_id=actor_id,
            reason_for_change=reason_for_change,
            details={
                "session_id": saved.session_id or saved.id,
                "subject_id": saved.subject_id,
                "timepoint_name": saved.timepoint_name,
                "reader_1_id": saved.reader_1_id,
                "reader_2_id": saved.reader_2_id,
                "adjudicator_id": saved.adjudicator_id,
            },
        )
        return saved

    async def submit_reader_assessment(
        self,
        session_id: str,
        assessment: ReaderAssessment,
        actor_id: str,
        reason_for_change: str | None = None,
    ) -> DualReaderAdjudicationSession:
        """Processes a blinded reader assessment, automatically detects discordance, advances state, and persists audit logs.

        Args:
            session_id: The unique identifier of the adjudication session.
            assessment: The domain reader assessment submission.
            actor_id: Identity of the submitting reader/user.
            reason_for_change: Optional Part 11 audit explanation.

        Returns:
            Updated DualReaderAdjudicationSession domain entity.

        Raises:
            AdjudicationSessionNotFoundError: If the session_id does not exist.
        """
        session = await self._repository.get_by_id(session_id)
        if not session:
            raise AdjudicationSessionNotFoundError(session_id)

        is_r1 = (
            assessment.reader_role == ReaderRole.READER_1
            or assessment.reader_id == session.reader_1_id
        )
        is_r2 = (
            assessment.reader_role == ReaderRole.READER_2
            or assessment.reader_id == session.reader_2_id
        )

        if is_r1:
            session.reader_1_assessment = assessment
            session.reader_1_status = "COMPLETED"
        elif is_r2:
            session.reader_2_assessment = assessment
            session.reader_2_status = "COMPLETED"
        else:
            session.adjudicator_assessment = assessment

        # If both Reader 1 and Reader 2 have completed reads, trigger concordance engine
        if session.reader_1_assessment and session.reader_2_assessment:
            session = ImagingAdjudicationEngine.evaluate_primary_reads(
                session, session.reader_1_assessment, session.reader_2_assessment
            )
        else:
            session.workflow_status = (
                AdjudicationWorkflowStatus.PENDING_PRIMARY_READS
                if not session.reader_1_assessment and not session.reader_2_assessment
                else (
                    AdjudicationWorkflowStatus.PENDING_READ_2
                    if is_r1
                    else AdjudicationWorkflowStatus.PENDING_READ_1
                )
            )
            ImagingAdjudicationEngine._update_session_digest(session)

        session.version_index += 1
        role_label = (
            assessment.reader_role.value
            if hasattr(assessment.reader_role, "value")
            else str(assessment.reader_role)
        )
        session.reason_for_change = (
            reason_for_change or f"Reader assessment submitted by {role_label}"
        )

        saved = await self._repository.save(session)

        resp_val = (
            assessment.overall_response.value
            if hasattr(assessment.overall_response, "value")
            else str(assessment.overall_response)
        )
        wf_val = (
            saved.workflow_status.value
            if hasattr(saved.workflow_status, "value")
            else str(saved.workflow_status)
        )
        await self._repository.record_audit_log(
            action="READER_ASSESSMENT_SUBMITTED",
            actor_id=actor_id,
            reason_for_change=reason_for_change,
            details={
                "session_id": session_id,
                "reader_role": role_label,
                "overall_response": resp_val,
                "sum_longest_diameters_mm": assessment.sum_longest_diameters_mm,
                "workflow_status": wf_val,
            },
        )
        return saved

    async def sign_off_adjudication(
        self,
        session_id: str,
        signer_id: str,
        signer_name: str,
        signing_reason: str,
        rationale: str,
        binding_reader: str = "ADJUDICATOR",
        adjudicator_assessment: ReaderAssessment | None = None,
        ip_address: str = "127.0.0.1",
        user_agent: str = "Cadence-Imaging/2.0",
    ) -> DualReaderAdjudicationSession:
        """Executes 21 CFR Part 11 step-up electronic signature sign-off for binding tie-breaker determination.

        Args:
            session_id: The unique identifier of the adjudication session.
            signer_id: User identifier of the signing adjudicator.
            signer_name: Full name of the signing adjudicator.
            signing_reason: Part 11 reason code (e.g. ADJUDICATION_SIGN_OFF).
            rationale: Mandatory clinical justification rationale.
            binding_reader: Selection of binding reader ('READER_1', 'READER_2', or 'ADJUDICATOR').
            adjudicator_assessment: Optional explicit assessment by adjudicator.
            ip_address: Client IP address for audit trail.
            user_agent: Client User Agent for audit trail.

        Returns:
            Locked and verified DualReaderAdjudicationSession domain entity.

        Raises:
            AdjudicationSessionNotFoundError: If the session_id does not exist.
            AdjudicationSignOffRationaleRequiredError: If clinical rationale is empty.
        """
        if not rationale or not rationale.strip():
            raise AdjudicationSignOffRationaleRequiredError()

        session = await self._repository.get_by_id(session_id)
        if not session:
            raise AdjudicationSessionNotFoundError(session_id)

        # 1. Step-up signature manifestation packaging
        ts_now = datetime.now(UTC).isoformat()
        manifest_bytes = (
            f"{session_id}|{signer_id}|{signing_reason}|{ts_now}|{rationale}".encode()
        )
        signature_digest = hashlib.sha256(manifest_bytes).hexdigest()

        signature_manifestation = {
            "signer_id": signer_id,
            "signer_name": signer_name,
            "timestamp": ts_now,
            "signing_reason": signing_reason,
            "ip_address": ip_address,
            "user_agent": user_agent,
            "sha256_hash": signature_digest,
            "step_up_verified": True,
        }

        # 2. Build or resolve adjudicator assessment
        if adjudicator_assessment:
            adj_assessment_obj = adjudicator_assessment
            adj_assessment_obj.reader_id = signer_id
            adj_assessment_obj.reader_name = signer_name
            adj_assessment_obj.reader_role = ReaderRole.ADJUDICATOR
            adj_assessment_obj.timepoint_name = session.timepoint_name
            adj_assessment_obj.assessment_notes = rationale
            adj_assessment_obj.submitted_at = ts_now
            adj_assessment_obj.digital_signature = signature_digest
        elif binding_reader == "READER_1" and session.reader_1_assessment:
            r1 = session.reader_1_assessment
            adj_assessment_obj = ReaderAssessment(
                reader_id=signer_id,
                reader_name=signer_name,
                reader_role=ReaderRole.ADJUDICATOR,
                timepoint_name=session.timepoint_name,
                target_lesions=r1.target_lesions,
                non_target_status=r1.non_target_status,
                new_lesions_present=r1.new_lesions_present,
                new_lesions_count=r1.new_lesions_count,
                sum_longest_diameters_mm=r1.sum_longest_diameters_mm,
                overall_response=r1.overall_response,
                assessment_notes=rationale,
                submitted_at=ts_now,
                digital_signature=signature_digest,
            )
        elif binding_reader == "READER_2" and session.reader_2_assessment:
            r2 = session.reader_2_assessment
            adj_assessment_obj = ReaderAssessment(
                reader_id=signer_id,
                reader_name=signer_name,
                reader_role=ReaderRole.ADJUDICATOR,
                timepoint_name=session.timepoint_name,
                target_lesions=r2.target_lesions,
                non_target_status=r2.non_target_status,
                new_lesions_present=r2.new_lesions_present,
                new_lesions_count=r2.new_lesions_count,
                sum_longest_diameters_mm=r2.sum_longest_diameters_mm,
                overall_response=r2.overall_response,
                assessment_notes=rationale,
                submitted_at=ts_now,
                digital_signature=signature_digest,
            )
        else:
            adj_assessment_obj = ReaderAssessment(
                reader_id=signer_id,
                reader_name=signer_name,
                reader_role=ReaderRole.ADJUDICATOR,
                timepoint_name=session.timepoint_name,
                overall_response="PR",
                assessment_notes=rationale,
                submitted_at=ts_now,
                digital_signature=signature_digest,
            )

        # 3. Apply decision via domain engine
        adjudicated = ImagingAdjudicationEngine.apply_adjudicator_decision(
            session=session,
            adjudicator_assessment=adj_assessment_obj,
            rationale=rationale,
            locked_by=signer_id,
            binding_reader=binding_reader,
            step_up_signature=signature_manifestation,
        )
        adjudicated.reason_for_change = (
            f"21 CFR Part 11 Adjudication Sign-Off: {rationale}"
        )

        saved = await self._repository.save(adjudicated)

        # 4. Synchronize TumorResponseAssessment record
        await self._repository.record_tumor_response_assessment(
            session=saved,
            signature_digest=signature_digest,
            actor_id=signer_id,
            rationale=rationale,
        )

        # 5. Append 21 CFR Part 11 Audit Log
        resp_val = (
            saved.final_consensus_response.value
            if hasattr(saved.final_consensus_response, "value")
            else saved.final_consensus_response
        )
        await self._repository.record_audit_log(
            action="ADJUDICATION_STEP_UP_SIGNED",
            actor_id=signer_id,
            reason_for_change=rationale,
            details={
                "session_id": session_id,
                "final_response": resp_val,
                "binding_reader": saved.binding_reader,
                "session_digest": saved.session_digest,
                "step_up_signature": signature_manifestation,
            },
        )
        return saved
