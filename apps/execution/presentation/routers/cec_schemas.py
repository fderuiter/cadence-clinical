"""Pydantic v2 schemas for Clinical Endpoint Adjudication Committee (CEC) REST API.

Requirements: PRD-CEC-001, PRD-CEC-002, PRD-CEC-003, PRD-SYS-124, 21 CFR Part 11
"""

from typing import Any

from pydantic import BaseModel, Field

from apps.execution.domain.cec_models import (
    AdjudicationClassification,
    BindingDeterminationSource,
    BundledEcrfForm,
    DiagnosticAttachment,
    ECGRecord,
    EndpointCategory,
    LabPanelItem,
    ReaderRole,
    VotingRule,
)


class DossierPackageCreateDTO(BaseModel):
    """Payload to compile and package a new candidate clinical event dossier."""

    study_id: str = Field(..., description="Target protocol study ID")
    site_id: str = Field(..., description="Investigational site ID")
    subject_id: str = Field(..., description="Subject UUID")
    event_id: str = Field(..., description="Adverse event or clinical observation ID")
    candidate_event_type: EndpointCategory = Field(
        ..., description="Endpoint category (e.g. MACE, STROKE, MI)"
    )
    reported_term: str = Field(..., description="Reported event term")
    verbatim_description: str = Field(
        default="", description="Verbatim investigator clinical description"
    )
    reported_onset_date: str = Field(
        ..., description="Reported onset date (ISO 8601 / DTC)"
    )
    medical_narrative: str = Field(
        default="", description="Detailed clinical narrative"
    )
    bundled_forms: list[BundledEcrfForm] = Field(default_factory=list)
    lab_panels: list[LabPanelItem] = Field(default_factory=list)
    ecg_records: list[ECGRecord] = Field(default_factory=list)
    diagnostic_attachments: list[DiagnosticAttachment] = Field(default_factory=list)
    reader_1_id: str = Field(default="reader_01")
    reader_1_name: str = Field(default="Dr. Primary Reader 1")
    reader_2_id: str = Field(default="reader_02")
    reader_2_name: str = Field(default="Dr. Primary Reader 2")
    chair_id: str = Field(default="chair_adjudicator")
    chair_name: str = Field(default="Dr. Adjudication Committee Chair")


class ReaderAssessmentSubmitDTO(BaseModel):
    """Payload for Reader 1 or Reader 2 independent blinded assessment submission."""

    reader_role: ReaderRole = Field(
        ..., description="Reader role: READER_1 or READER_2"
    )
    classification: AdjudicationClassification = Field(
        ..., description="Event classification"
    )
    subtype: str | None = Field(
        None, description="Specific event subtype (e.g. Type 1 MI, Ischemic Stroke)"
    )
    adjudicated_onset_date: str | None = Field(
        None, description="Adjudicated onset date (ISO 8601)"
    )
    criteria_met: list[str] = Field(
        default_factory=list, description="Diagnostic criteria satisfied"
    )
    confidence_score: int = Field(
        default=5, ge=1, le=5, description="Confidence score 1 to 5"
    )
    clinical_rationale: str = Field(
        default="", description="Clinical rationale narrative"
    )


class ChairAdjudicateDTO(BaseModel):
    """Payload for Adjudication Committee Chair tie-breaker ruling with e-signature."""

    final_classification: AdjudicationClassification = Field(
        ..., description="Definitive classification"
    )
    final_subtype: str | None = Field(None, description="Definitive subtype")
    final_adjudicated_onset_date: str = Field(
        ..., description="Definitive onset date (ISO 8601)"
    )
    binding_reader: BindingDeterminationSource = Field(
        default=BindingDeterminationSource.CHAIR_CUSTOM_DETERMINATION,
        description="Binding determination source",
    )
    criteria_confirmed: list[str] = Field(
        default_factory=list, description="Criteria confirmed"
    )
    adjudication_rationale: str = Field(
        ..., description="Formal adjudication rationale"
    )
    signature_token: str | None = Field(
        None, description="Step-up HMAC signature token"
    )
    signer_name: str = Field(
        default="Dr. Adjudication Chair", description="Printed signer name"
    )
    password: str = Field(
        default="SignOffPassword123!", description="Re-authentication password"
    )
    signature_meaning: str = Field(
        default="I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines",
        description="21 CFR Part 11 legal signature manifestation",
    )


class CharterCreateDTO(BaseModel):
    """Payload to configure or update a committee charter."""

    study_id: str = Field(..., description="Target study ID")
    committee_name: str = Field(..., description="Committee name")
    version: str = Field(default="1.0")
    voting_rule: VotingRule = Field(default=VotingRule.CHAIR_TIE_BREAKER)
    minimum_quorum_percentage: float = Field(default=66.67, ge=0.0, le=100.0)
    minimum_members_count: int = Field(default=3, ge=1)
    eligible_members: list[dict[str, str]] = Field(default_factory=list)
    endpoint_definitions: list[str] = Field(default_factory=list)
    diagnostic_criteria_templates: dict[str, list[str]] = Field(default_factory=dict)


class MeetingQuorumRecordDTO(BaseModel):
    """Payload to record committee meeting quorum and minutes."""

    study_id: str = Field(..., description="Study ID")
    charter_id: str = Field(..., description="Charter ID")
    meeting_date: str = Field(..., description="Meeting date (ISO 8601)")
    attendees: list[dict[str, str]] = Field(
        ..., description="Attendees: [{member_id, member_name, role}]"
    )
    total_eligible_members: int = Field(..., ge=1)
    dossiers_reviewed: list[str] = Field(default_factory=list)
    voting_results: dict[str, dict[str, Any]] = Field(default_factory=dict)
    meeting_minutes: str = Field(default="")
    chair_signature: dict[str, Any] | None = None


class SdtmCeCompileRequestDTO(BaseModel):
    """Request payload to compile SDTM CE dataset for a study."""

    study_id: str = Field(..., description="Study ID")


class DossierDetailResponseDTO(BaseModel):
    """Complete dossier representation returned to authorized callers."""

    dossier_id: str
    event_id: str
    study_id: str
    site_id: str
    masked_site_id: str
    subject_id: str
    masked_subject_id: str
    candidate_event_type: str
    reported_term: str
    verbatim_description: str
    reported_onset_date: str
    medical_narrative: str
    is_deidentified: bool
    bundled_forms: list[dict[str, Any]]
    lab_panels: list[dict[str, Any]]
    ecg_records: list[dict[str, Any]]
    diagnostic_attachments: list[dict[str, Any]]
    redaction_summary: dict[str, int]
    reader_1_id: str
    reader_1_name: str
    reader_1_status: str
    reader_1_assessment: dict[str, Any] | None
    reader_2_id: str
    reader_2_name: str
    reader_2_status: str
    reader_2_assessment: dict[str, Any] | None
    chair_id: str | None
    chair_name: str | None
    chair_determination: dict[str, Any] | None
    workflow_status: str
    is_discordant: bool
    discordance_reasons: list[str]
    final_consensus_classification: str | None
    final_consensus_subtype: str | None
    final_adjudicated_onset_date: str | None
    binding_determination_source: str | None
    adjudication_rationale: str | None
    electronic_signature: dict[str, Any] | None
    edc_writeback_status: str
    dossier_digest: str
    created_at: str
    version_index: int
