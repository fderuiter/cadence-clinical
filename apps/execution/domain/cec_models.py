"""Domain models for Clinical Endpoint Adjudication Committee (CEC) Suite.

Requirements: PRD-CEC-001, PRD-CEC-002, PRD-CEC-003, PRD-SYS-124, 21 CFR Part 11, ADR-2199
"""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field


class EndpointCategory(StrEnum):
    """Critical clinical endpoint categories subject to blinded adjudication."""

    MACE = "MACE"
    MYOCARDIAL_INFARCTION = "MYOCARDIAL_INFARCTION"
    STROKE = "STROKE"
    CARDIOVASCULAR_MORTALITY = "CARDIOVASCULAR_MORTALITY"
    ALL_CAUSE_MORTALITY = "ALL_CAUSE_MORTALITY"
    HEART_FAILURE_HOSPITALIZATION = "HEART_FAILURE_HOSPITALIZATION"
    STENT_THROMBOSIS = "STENT_THROMBOSIS"
    VENOUS_THROMBOEMBOLISM = "VENOUS_THROMBOEMBOLISM"
    DISEASE_PROGRESSION = "DISEASE_PROGRESSION"
    OTHER = "OTHER"


class AdjudicationClassification(StrEnum):
    """Standardized adjudicated event classification determinations."""

    CONFIRMED_MI = "CONFIRMED_MI"
    CONFIRMED_ISCHEMIC_STROKE = "CONFIRMED_ISCHEMIC_STROKE"
    CONFIRMED_HEMORRHAGIC_STROKE = "CONFIRMED_HEMORRHAGIC_STROKE"
    CONFIRMED_CV_DEATH = "CONFIRMED_CV_DEATH"
    CONFIRMED_NON_CV_DEATH = "CONFIRMED_NON_CV_DEATH"
    CONFIRMED_HEART_FAILURE_HOSPITALIZATION = "CONFIRMED_HEART_FAILURE_HOSPITALIZATION"
    CONFIRMED_STENT_THROMBOSIS = "CONFIRMED_STENT_THROMBOSIS"
    CONFIRMED_VTE = "CONFIRMED_VTE"
    CONFIRMED_DISEASE_PROGRESSION = "CONFIRMED_DISEASE_PROGRESSION"
    NOT_AN_ENDPOINT = "NOT_AN_ENDPOINT"
    INDETERMINATE = "INDETERMINATE"
    INSUFFICIENT_DATA = "INSUFFICIENT_DATA"


class AdjudicationWorkflowStatus(StrEnum):
    """Lifecycle state machine for 2+1 blinded reader review."""

    PENDING_PRIMARY_REVIEWS = "PENDING_PRIMARY_REVIEWS"
    READER_1_COMPLETED = "READER_1_COMPLETED"
    READER_2_COMPLETED = "READER_2_COMPLETED"
    CONSENSUS_REACHED = "CONSENSUS_REACHED"
    DISCORDANCE_DETECTED = "DISCORDANCE_DETECTED"
    ESCALATED_TO_CHAIR = "ESCALATED_TO_CHAIR"
    FINALIZED_CONCORDANT = "FINALIZED_CONCORDANT"
    FINALIZED_BY_CHAIR = "FINALIZED_BY_CHAIR"
    FINALIZED_BY_COMMITTEE = "FINALIZED_BY_COMMITTEE"


class ReaderRole(StrEnum):
    """Roles within the CEC charter hierarchy."""

    READER_1 = "READER_1"
    READER_2 = "READER_2"
    CHAIR = "CHAIR"
    COMMITTEE_MEMBER = "COMMITTEE_MEMBER"
    COORDINATOR = "COORDINATOR"


class VotingRule(StrEnum):
    """Committee charter decision and voting rules."""

    UNANIMOUS = "UNANIMOUS"
    MAJORITY_2_OF_3 = "MAJORITY_2_OF_3"
    CHAIR_TIE_BREAKER = "CHAIR_TIE_BREAKER"
    SUPERMAJORITY = "SUPERMAJORITY"


class BindingDeterminationSource(StrEnum):
    """Source of the binding adjudication determination."""

    CONCORDANT_CONSENSUS = "CONCORDANT_CONSENSUS"
    CHAIR_UPHELD_READER_1 = "CHAIR_UPHELD_READER_1"
    CHAIR_UPHELD_READER_2 = "CHAIR_UPHELD_READER_2"
    CHAIR_CUSTOM_DETERMINATION = "CHAIR_CUSTOM_DETERMINATION"
    COMMITTEE_QUORUM_VOTE = "COMMITTEE_QUORUM_VOTE"


class LabPanelItem(BaseModel):
    """De-identified laboratory analyte finding pertinent to endpoint review."""

    test_name: str = Field(
        ..., description="Laboratory analyte name (e.g. Troponin I, CK-MB)"
    )
    loinc_code: str | None = Field(None, description="Standard LOINC code")
    result_value: float | str = Field(
        ..., description="Observed numeric or character result"
    )
    result_unit: str = Field(..., description="Unit of measurement (e.g. ng/mL, U/L)")
    reference_low: float | None = Field(None, description="Lower limit of normal")
    reference_high: float | None = Field(
        None, description="Upper limit of normal / 99th percentile URL"
    )
    is_out_of_range: bool = Field(
        default=False, description="Flag indicating value exceeds reference limits"
    )
    toxicity_grade: int | None = Field(
        None, description="Computed CTCAE / DAIDS toxicity grade (0-4)"
    )
    sample_timestamp: str | None = Field(
        None, description="Relative shifted ISO timestamp of specimen collection"
    )


class ECGRecord(BaseModel):
    """12-Lead Electrocardiogram diagnostic findings."""

    ecg_id: str = Field(..., description="Unique ECG record identifier")
    timestamp: str = Field(..., description="Shifted timestamp of ECG acquisition")
    rhythm: str = Field(
        default="Normal sinus rhythm", description="Cardiac rhythm classification"
    )
    st_elevation_leads: list[str] = Field(
        default_factory=list,
        description="Leads exhibiting diagnostic ST elevation (e.g. V1-V4)",
    )
    st_depression_leads: list[str] = Field(
        default_factory=list, description="Leads exhibiting ST depression"
    )
    t_wave_inversion_leads: list[str] = Field(
        default_factory=list, description="Leads exhibiting T wave inversion"
    )
    q_wave_leads: list[str] = Field(
        default_factory=list, description="Leads exhibiting pathological Q waves"
    )
    lbbb_present: bool = Field(
        default=False, description="New or presumed new Left Bundle Branch Block"
    )
    qtcf_ms: float | None = Field(
        None, description="Fridericia-corrected QT interval in milliseconds"
    )
    interpretation: str | None = Field(
        None, description="Summary cardiologist interpretation"
    )


class DiagnosticAttachment(BaseModel):
    """De-identified medical imaging or clinical diagnostic attachment."""

    attachment_id: str = Field(..., description="Unique attachment ID")
    document_type: str = Field(
        ...,
        description="Type of diagnostic doc (e.g. Echocardiogram, Angiography, CT/MRI Brain)",
    )
    title: str = Field(..., description="Descriptive title")
    summary: str = Field(..., description="Clinical narrative summary")
    key_findings: list[str] = Field(
        default_factory=list, description="Key bullet points / measurements"
    )
    file_url: str | None = Field(None, description="Secure presigned blob storage URI")
    content_hash: str | None = Field(
        None, description="SHA-256 integrity hash of document blob"
    )


class BundledEcrfForm(BaseModel):
    """Pertinent eCRF form submission included in the event dossier."""

    form_id: str = Field(..., description="Originating eCRF form submission ID")
    form_name: str = Field(
        ...,
        description="Form name (e.g. Adverse Events, Concomitant Medications, Vitals)",
    )
    visit_name: str = Field(..., description="Study encounter / visit label")
    submission_timestamp: str = Field(
        ..., description="Shifted timestamp of form entry"
    )
    form_data: dict[str, Any] = Field(
        default_factory=dict, description="Scrubbed eCRF field values"
    )


class ReaderAssessment(BaseModel):
    """Blinded expert reader assessment and diagnostic criteria evaluation."""

    assessment_id: str = Field(..., description="Unique assessment ID")
    dossier_id: str = Field(..., description="Target dossier ID")
    reader_id: str = Field(..., description="Adjudicator user identifier")
    reader_name: str = Field(..., description="Adjudicator full name / credentials")
    reader_role: ReaderRole = Field(
        ..., description="Assigned role: READER_1 or READER_2"
    )
    classification: AdjudicationClassification = Field(
        ..., description="Determined classification"
    )
    subtype: str | None = Field(
        None, description="Specific event subtype (e.g. Type 1 MI, Ischemic Stroke)"
    )
    adjudicated_onset_date: str | None = Field(
        None, description="Adjudicated date/time of event onset in ISO 8601"
    )
    criteria_met: list[str] = Field(
        default_factory=list, description="Diagnostic criteria items satisfied"
    )
    confidence_score: int = Field(
        default=5, ge=1, le=5, description="Confidence level (1=Low, 5=Definitive)"
    )
    clinical_rationale: str = Field(
        default="", description="Detailed clinical rationale"
    )
    submitted_at: str | None = Field(None, description="UTC submission timestamp")
    assessment_digest: str = Field(
        default="", description="SHA-256 hash of reader assessment payload"
    )


class DiscordanceEvaluation(BaseModel):
    """Automated evaluation of reader concordance/discordance."""

    is_discordant: bool = Field(..., description="True if primary readers disagree")
    discordance_reasons: list[str] = Field(
        default_factory=list, description="Specific discordance points"
    )
    classification_mismatch: bool = Field(
        default=False, description="True if primary classifications disagree"
    )
    subtype_mismatch: bool = Field(
        default=False, description="True if subtypes disagree"
    )
    date_discordance_days: int = Field(
        default=0, description="Difference between reader onset dates in days"
    )
    escalation_required: bool = Field(
        default=False, description="True if Chair tie-breaker is required"
    )


class ChairAdjudicationDetermination(BaseModel):
    """Final tie-breaker determination issued by the Adjudication Committee Chair."""

    determination_id: str = Field(..., description="Unique determination ID")
    dossier_id: str = Field(..., description="Target dossier ID")
    chair_id: str = Field(..., description="Chair user ID")
    chair_name: str = Field(..., description="Chair full name")
    final_classification: AdjudicationClassification = Field(
        ..., description="Definitive classification"
    )
    final_subtype: str | None = Field(None, description="Definitive event subtype")
    final_adjudicated_onset_date: str = Field(
        ..., description="Definitive event onset date (ISO 8601)"
    )
    binding_reader: BindingDeterminationSource = Field(
        default=BindingDeterminationSource.CHAIR_CUSTOM_DETERMINATION,
        description="Source of binding decision",
    )
    criteria_confirmed: list[str] = Field(
        default_factory=list, description="Diagnostic criteria confirmed"
    )
    adjudication_rationale: str = Field(
        ..., description="Formal chair tie-breaker rationale"
    )
    electronic_signature: dict[str, Any] = Field(
        ..., description="21 CFR Part 11 electronic signature payload"
    )
    finalized_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())


class PatientDossierPackage(BaseModel):
    """Complete de-identified patient event dossier package."""

    dossier_id: str = Field(..., description="Unique dossier UUID")
    event_id: str = Field(
        ..., description="Originating adverse event or clinical observation ID"
    )
    study_id: str = Field(..., description="Study identifier")
    site_id: str = Field(..., description="Clinical trial site identifier")
    masked_site_id: str = Field(
        default="SITE-BLINDED", description="Blinded site surrogate token"
    )
    subject_id: str = Field(..., description="Unique Subject ID")
    masked_subject_id: str = Field(
        default="SUBJ-BLINDED", description="Pseudonymized subject surrogate token"
    )
    candidate_event_type: EndpointCategory = Field(
        ..., description="Candidate endpoint classification category"
    )
    reported_term: str = Field(..., description="Investigator-reported event term")
    verbatim_description: str = Field(..., description="Free-text clinical description")
    reported_onset_date: str = Field(..., description="Reported event onset date")
    medical_narrative: str = Field(
        ..., description="De-identified comprehensive clinical narrative"
    )
    is_deidentified: bool = Field(
        default=True, description="PHI de-identification status flag"
    )
    bundled_forms: list[BundledEcrfForm] = Field(
        default_factory=list, description="Bundled eCRF records"
    )
    lab_panels: list[LabPanelItem] = Field(
        default_factory=list, description="Bundled laboratory analytes"
    )
    ecg_records: list[ECGRecord] = Field(
        default_factory=list, description="Bundled 12-lead ECG findings"
    )
    diagnostic_attachments: list[DiagnosticAttachment] = Field(
        default_factory=list, description="Bundled imaging & diagnostic documents"
    )
    redaction_summary: dict[str, int] = Field(
        default_factory=dict, description="Redaction counts per PII category"
    )
    reader_1_id: str = Field(default="reader_01", description="Assigned Reader 1 ID")
    reader_1_name: str = Field(default="Reader 1", description="Reader 1 Name")
    reader_1_status: str = Field(
        default="PENDING", description="Reader 1 status: PENDING, COMPLETED"
    )
    reader_1_assessment: ReaderAssessment | None = Field(
        None, description="Reader 1 assessment"
    )
    reader_2_id: str = Field(default="reader_02", description="Assigned Reader 2 ID")
    reader_2_name: str = Field(default="Reader 2", description="Reader 2 Name")
    reader_2_status: str = Field(
        default="PENDING", description="Reader 2 status: PENDING, COMPLETED"
    )
    reader_2_assessment: ReaderAssessment | None = Field(
        None, description="Reader 2 assessment"
    )
    chair_id: str | None = Field(None, description="Adjudication Chair ID")
    chair_name: str | None = Field(None, description="Adjudication Chair Name")
    chair_determination: ChairAdjudicationDetermination | None = Field(
        None, description="Chair tie-breaker determination"
    )
    workflow_status: AdjudicationWorkflowStatus = Field(
        default=AdjudicationWorkflowStatus.PENDING_PRIMARY_REVIEWS,
        description="Current workflow lifecycle state",
    )
    is_discordant: bool = Field(default=False, description="Discordance flag")
    discordance_reasons: list[str] = Field(
        default_factory=list, description="List of discordance rationales"
    )
    final_consensus_classification: AdjudicationClassification | None = Field(
        None, description="Final consensus determination"
    )
    final_consensus_subtype: str | None = Field(
        None, description="Final consensus subtype"
    )
    final_adjudicated_onset_date: str | None = Field(
        None, description="Final consensus onset date"
    )
    binding_determination_source: str | None = Field(
        None, description="Source of final decision"
    )
    adjudication_rationale: str | None = Field(
        None, description="Final adjudication rationale"
    )
    electronic_signature: dict[str, Any] | None = Field(
        None, description="21 CFR Part 11 signature metadata"
    )
    edc_writeback_status: str = Field(
        default="PENDING", description="EDC synchronization status: PENDING, SYNCED"
    )
    dossier_digest: str = Field(
        default="",
        description="Cryptographic SHA-256 integrity hash of dossier content",
    )
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    created_by: str = Field(default="system")
    version_index: int = Field(default=1)


class DossierBlindedView(BaseModel):
    """Blinded view of a patient event dossier presented to a specific reader role."""

    dossier_id: str
    event_id: str
    study_id: str
    masked_site_id: str
    masked_subject_id: str
    candidate_event_type: EndpointCategory
    reported_term: str
    verbatim_description: str
    reported_onset_date: str
    medical_narrative: str
    is_deidentified: bool
    bundled_forms: list[BundledEcrfForm]
    lab_panels: list[LabPanelItem]
    ecg_records: list[ECGRecord]
    diagnostic_attachments: list[DiagnosticAttachment]
    reader_role: ReaderRole
    workflow_status: AdjudicationWorkflowStatus
    my_assessment: ReaderAssessment | None = None
    peer_assessment_completed: bool = False
    peer_assessment: ReaderAssessment | None = (
        None  # Populated only if role is Chair or after consensus/unblinding
    )
    chair_determination: ChairAdjudicationDetermination | None = None
    is_discordant: bool = False
    discordance_reasons: list[str] = Field(default_factory=list)
    final_consensus_classification: AdjudicationClassification | None = None
    final_adjudicated_onset_date: str | None = None
    dossier_digest: str = ""


class CecCharterConfig(BaseModel):
    """Charter governance parameters defining voting rules, quorum, and criteria templates."""

    charter_id: str = Field(..., description="Unique charter ID")
    study_id: str = Field(..., description="Study ID")
    committee_name: str = Field(..., description="Name of Adjudication Committee")
    version: str = Field(default="1.0", description="Charter version string")
    voting_rule: VotingRule = Field(
        default=VotingRule.CHAIR_TIE_BREAKER, description="Committee voting rule"
    )
    minimum_quorum_percentage: float = Field(
        default=66.67, ge=0.0, le=100.0, description="Minimum % quorum required"
    )
    minimum_members_count: int = Field(
        default=3, ge=1, description="Minimum member count for quorum"
    )
    eligible_members: list[dict[str, str]] = Field(
        default_factory=list,
        description="Eligible members: [{member_id, member_name, role, specialty}]",
    )
    endpoint_definitions: list[str] = Field(
        default_factory=lambda: [
            "MACE",
            "MYOCARDIAL_INFARCTION",
            "STROKE",
            "CARDIOVASCULAR_MORTALITY",
        ],
        description="List of endpoints governed by this charter",
    )
    diagnostic_criteria_templates: dict[str, list[str]] = Field(
        default_factory=lambda: {
            "MYOCARDIAL_INFARCTION": [
                "Cardiac biomarker rise/fall > 99th percentile URL (Troponin / CK-MB)",
                "Ischemic symptoms > 20 minutes duration",
                "New ST elevation / depression or T-wave inversion on 12-lead ECG",
                "Development of pathological Q waves",
                "Imaging evidence of new loss of viable myocardium or new regional wall motion abnormality",
                "Angiographic confirmation of intracoronary thrombus",
            ],
            "STROKE": [
                "Acute onset of focal neurological deficit lasting > 24 hours (or leading to death)",
                "Neuroimaging (CT/MRI) evidence of acute cerebral infarction or intracranial hemorrhage",
                "Exclusion of non-vascular causes (e.g. brain tumor, trauma, hypoglycemia)",
                "NIHSS score assessment performed",
            ],
            "CARDIOVASCULAR_MORTALITY": [
                "Death attributable to acute myocardial infarction, sudden cardiac death, or heart failure",
                "Death attributable to stroke or pulmonary embolism",
                "Death occurring within 30 days of cardiovascular procedure",
                "No definitive non-cardiovascular cause established",
            ],
        },
        description="Standard criteria checklists per endpoint",
    )
    is_active: bool = Field(default=True)
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    created_by: str = Field(default="system")
    version_index: int = Field(default=1)


class CreateCharterRequest(BaseModel):
    """Request payload for creating or updating committee charter configuration."""

    charter_id: str = Field(..., description="Unique charter ID")
    study_id: str = Field(..., description="Study ID")
    committee_name: str = Field(..., description="Name of Adjudication Committee")
    version: str = Field(default="1.0", description="Charter version string")
    voting_rule: VotingRule = Field(
        default=VotingRule.CHAIR_TIE_BREAKER, description="Committee voting rule"
    )
    minimum_quorum_percentage: float = Field(
        default=66.67, ge=0.0, le=100.0, description="Minimum % quorum required"
    )
    minimum_members_count: int = Field(
        default=3, ge=1, description="Minimum member count for quorum"
    )
    eligible_members: list[dict[str, str]] = Field(
        default_factory=list, description="Eligible members"
    )
    endpoint_definitions: list[str] = Field(
        default_factory=list, description="List of endpoints"
    )
    diagnostic_criteria_templates: dict[str, list[str]] = Field(
        default_factory=dict, description="Diagnostic criteria templates"
    )


class CecMeetingQuorum(BaseModel):
    """Meeting quorum tracking and minutes sign-off record."""

    meeting_id: str = Field(..., description="Unique meeting UUID")
    study_id: str = Field(..., description="Study identifier")
    charter_id: str = Field(..., description="Charter identifier")
    meeting_date: str = Field(..., description="Date/Time of meeting in ISO 8601")
    attendees: list[dict[str, str]] = Field(
        ..., description="List of attending members [{member_id, member_name, role}]"
    )
    total_eligible_members: int = Field(..., ge=1, description="Total roster count")
    quorum_percentage: float = Field(
        ..., description="Calculated attendance percentage"
    )
    quorum_met: bool = Field(..., description="Flag indicating whether quorum was met")
    dossiers_reviewed: list[str] = Field(
        default_factory=list, description="List of dossier IDs discussed"
    )
    voting_results: dict[str, dict[str, Any]] = Field(
        default_factory=dict, description="Votes recorded per dossier"
    )
    meeting_minutes: str = Field(
        default="", description="Formal meeting minutes narrative"
    )
    minutes_digest: str = Field(
        default="", description="SHA-256 hash of meeting minutes and vote ledger"
    )
    signed_by_chair: bool = Field(
        default=False, description="Chair signature confirmation"
    )
    chair_signature: dict[str, Any] | None = Field(
        None, description="21 CFR Part 11 chair signature block"
    )
    created_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    created_by: str = Field(default="system")


class EdcCasebookWritebackResult(BaseModel):
    """Result of bi-directional synchronization back to EDC casebook."""

    dossier_id: str
    subject_id: str
    study_id: str
    event_id: str
    observation_ids_updated: list[str] = Field(default_factory=list)
    adjudication_status: str = "CONFIRMED"
    adjudicated_term: str
    adjudication_date: str
    signature_id: str | None = None
    is_locked: bool = True
    written_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    status: str = "SUCCESS"


class SdtmCeCompilationResult(BaseModel):
    """Compiled CDISC SDTM CE and SUPPCE domain dataset."""

    study_id: str
    ce_records: list[dict[str, Any]] = Field(default_factory=list)
    suppce_records: list[dict[str, Any]] = Field(default_factory=list)
    record_count: int = 0
    compiled_at: str = Field(default_factory=lambda: datetime.now(UTC).isoformat())
    dataset_digest: str = ""


class CreateDossierRequest(BaseModel):
    """Request payload to compile and package a new patient event dossier."""

    study_id: str
    site_id: str
    subject_id: str
    event_id: str
    candidate_event_type: EndpointCategory
    reported_term: str
    verbatim_description: str
    reported_onset_date: str
    medical_narrative: str
    bundled_forms: list[BundledEcrfForm] = Field(default_factory=list)
    lab_panels: list[LabPanelItem] = Field(default_factory=list)
    ecg_records: list[ECGRecord] = Field(default_factory=list)
    diagnostic_attachments: list[DiagnosticAttachment] = Field(default_factory=list)
    reader_1_id: str = "reader_01"
    reader_1_name: str = "Dr. Primary Reader 1"
    reader_2_id: str = "reader_02"
    reader_2_name: str = "Dr. Primary Reader 2"
    chair_id: str = "chair_adjudicator"
    chair_name: str = "Dr. CEC Committee Chair"


class SubmitReaderAssessmentRequest(BaseModel):
    """Request payload for an expert reader assessment."""

    reader_role: ReaderRole
    classification: AdjudicationClassification
    subtype: str | None = None
    adjudicated_onset_date: str | None = None
    criteria_met: list[str] = Field(default_factory=list)
    confidence_score: int = Field(default=5, ge=1, le=5)
    clinical_rationale: str = Field(default="")


class ChairAdjudicateRequest(BaseModel):
    """Request payload for Chair tie-breaker resolution with electronic signature."""

    final_classification: AdjudicationClassification
    final_subtype: str | None = None
    final_adjudicated_onset_date: str
    binding_reader: BindingDeterminationSource = (
        BindingDeterminationSource.CHAIR_CUSTOM_DETERMINATION
    )
    criteria_confirmed: list[str] = Field(default_factory=list)
    adjudication_rationale: str
    signature_token: str | None = None
    signer_name: str = "Dr. Adjudication Chair"
    password: str = "SignOffPassword123!"
    signature_meaning: str = "I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines"


class CecDossierSummaryItem(BaseModel):
    """Summary item for CEC console list views."""

    dossier_id: str
    event_id: str
    study_id: str
    masked_subject_id: str
    candidate_event_type: EndpointCategory
    reported_term: str
    reported_onset_date: str
    workflow_status: AdjudicationWorkflowStatus
    is_discordant: bool
    final_consensus_classification: AdjudicationClassification | None = None
    reader_1_status: str
    reader_2_status: str
    created_at: str
