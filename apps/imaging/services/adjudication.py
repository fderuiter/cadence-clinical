"""Blinded Dual-Reader Charter & Tie-Breaker Adjudication Engine for Medical Imaging.

Requirements: PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001 (21 CFR Part 11)
"""

import hashlib
import json
from datetime import UTC, datetime
from typing import Any

from pydantic import BaseModel, Field, model_validator

from apps.imaging.domain.models import (
    AdjudicationWorkflowStatus,
    DiscordanceReason,
    OncologicCriteria,
    ReaderAssessment,
    ReaderRole,
    RECISTResponseCode,
    SDTMRSRecord,
    SDTMTRRecord,
    SDTMTURecord,
)

# Backward-compatible alias for existing imports
RECISTResponse = RECISTResponseCode


class DualReaderAdjudicationSession(BaseModel):
    """Independent Review Charter session encapsulating dual-read and tie-breaker decisions."""

    id: str | None = None
    session_id: str | None = None
    study_envelope_id: str | None = None
    study_id: str
    site_id: str = "SITE-DEFAULT"
    subject_id: str
    timepoint_name: str
    assessment_criteria: OncologicCriteria = OncologicCriteria.RECIST_1_1
    reader_1_id: str = "rad_01"
    reader_1_name: str = "Reader 1"
    reader_1_status: str = "PENDING"
    reader_1_assessment: ReaderAssessment | None = None
    reader_2_id: str = "rad_02"
    reader_2_name: str = "Reader 2"
    reader_2_status: str = "PENDING"
    reader_2_assessment: ReaderAssessment | None = None
    adjudicator_id: str | None = None
    adjudicator_name: str | None = "Adjudication Committee Chair"
    adjudicator_assessment: ReaderAssessment | None = None
    sld_discordance_delta: float = 0.0
    is_response_discordant: bool = False
    discordance_reasons: list[str] = Field(default_factory=list)
    workflow_status: AdjudicationWorkflowStatus = (
        AdjudicationWorkflowStatus.PENDING_PRIMARY_READS
    )
    final_consensus_response: RECISTResponseCode | str | None = None
    final_consensus_sld_mm: float | None = None
    binding_reader: str | None = None
    adjudication_rationale: str | None = None
    electronic_signature: dict[str, Any] | None = None
    locked_at: str | None = None
    locked_by: str | None = None
    session_digest: str = ""
    tenant_id: str = "tenant_default"
    created_at: str | None = None
    created_by: str = "system"
    updated_at: str | None = None
    reason_for_change: str | None = None
    version_index: int = 1

    @model_validator(mode="after")
    def sync_id_and_session_id(self) -> DualReaderAdjudicationSession:
        if self.session_id and not self.id:
            self.id = self.session_id
        elif self.id and not self.session_id:
            self.session_id = self.id
        elif not self.id and not self.session_id:
            import uuid

            new_id = str(uuid.uuid4())
            self.id = new_id
            self.session_id = new_id
        return self


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
