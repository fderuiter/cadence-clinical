"""Domain models for Generative Pharmacovigilance Safety Narratives and grounded claim tracking.

Requirements: PRD-SYS-052
"""

from enum import StrEnum
from typing import Any

from pydantic import BaseModel, Field

from packages.database.audit import (
    AIAssistedRecordMixin,
    AIReviewStatus,
    Part11AuditMixin,
)


class TimelineEventType(StrEnum):
    """Categorized clinical event types for chronological timeline synthesis."""

    DEMOGRAPHICS = "DEMOGRAPHICS"
    MEDICAL_HISTORY = "MEDICAL_HISTORY"
    DRUG_ADMINISTRATION = "DRUG_ADMINISTRATION"
    CONCOMITANT_MEDICATION = "CONCOMITANT_MEDICATION"
    ADVERSE_EVENT = "ADVERSE_EVENT"
    DIAGNOSTIC_LAB = "DIAGNOSTIC_LAB"
    HOSPITALIZATION = "HOSPITALIZATION"
    DECHALLENGE_RECHALLENGE = "DECHALLENGE_RECHALLENGE"


class ClinicalTimelineEvent(BaseModel):
    """Normalized, de-identified clinical event record from execution eCRF datasets."""

    event_id: str = Field(..., description="Unique deterministic event identifier.")
    event_type: TimelineEventType = Field(
        ..., description="Category of clinical event."
    )
    event_date: str | None = Field(
        default=None,
        description="Event timestamp or onset date in ISO/CDISC DTC format.",
    )
    title: str = Field(..., description="Short summary title of the clinical event.")
    description: str = Field(..., description="Detailed event description.")
    domain: str | None = Field(
        default=None,
        description="Underlying SDTM domain code (e.g. DM, MH, CM, AE, LB).",
    )
    sequence: int | None = Field(
        default=None, description="Original domain sequence number (e.g. AESEQ, LBSEQ)."
    )
    source_record_id: str | None = Field(
        default=None, description="Original eCRF observation or submission ID."
    )
    details: dict[str, Any] = Field(
        default_factory=dict, description="Domain-specific raw metadata fields."
    )


class SubjectSafetyTimeline(BaseModel):
    """Aggregated chronological event stream for a clinical trial subject."""

    study_id: str = Field(..., description="Clinical study identifier.")
    subject_id: str = Field(..., description="De-identified subject key.")
    sae_event_key: str | None = Field(
        default=None, description="Target index SAE identifier."
    )
    events: list[ClinicalTimelineEvent] = Field(
        default_factory=list,
        description="Chronologically sorted clinical event records.",
    )


class NarrativeSectionType(StrEnum):
    """Standard regulatory safety narrative sections adhering to ICH E2B(R3) & FDA MedWatch 3500A."""

    DEMOGRAPHICS_BASELINE = "DEMOGRAPHICS_BASELINE"
    MEDICAL_TREATMENT_HISTORY = "MEDICAL_TREATMENT_HISTORY"
    INDEX_AE_CHRONOLOGY = "INDEX_AE_CHRONOLOGY"
    DIAGNOSTIC_LABS = "DIAGNOSTIC_LABS"
    CLINICAL_MANAGEMENT = "CLINICAL_MANAGEMENT"
    OUTCOME_CAUSALITY = "OUTCOME_CAUSALITY"


SECTION_TITLE_MAP: dict[NarrativeSectionType, str] = {
    NarrativeSectionType.DEMOGRAPHICS_BASELINE: "Patient Demographics & Baseline Condition",
    NarrativeSectionType.MEDICAL_TREATMENT_HISTORY: "Medical & Treatment History",
    NarrativeSectionType.INDEX_AE_CHRONOLOGY: "Index Adverse Event Description & Chronology",
    NarrativeSectionType.DIAGNOSTIC_LABS: "Diagnostic Workup & Laboratory Results",
    NarrativeSectionType.CLINICAL_MANAGEMENT: "Clinical Management & Hospital Course",
    NarrativeSectionType.OUTCOME_CAUSALITY: "Outcome & Causality Assessment",
}


class GroundedClaim(BaseModel):
    """A specific factual claim or sentence grounded to underlying clinical timeline events."""

    claim_id: str = Field(..., description="Unique claim identifier.")
    sentence_text: str = Field(
        ..., description="Narrative sentence or factual assertion."
    )
    section_type: NarrativeSectionType = Field(
        ..., description="Section containing this claim."
    )
    grounded_event_ids: list[str] = Field(
        default_factory=list,
        description="List of ClinicalTimelineEvent IDs supporting this claim.",
    )
    confidence_score: float = Field(
        default=1.0,
        ge=0.0,
        le=1.0,
        description="Confidence score for this grounded assertion.",
    )


class SafetyNarrativeSection(BaseModel):
    """A structured narrative section containing formatted prose and grounded claim references."""

    section_type: NarrativeSectionType = Field(
        ..., description="Regulatory section type."
    )
    section_title: str = Field(..., description="Human-readable section header.")
    content: str = Field(..., description="Generated or edited narrative prose.")
    grounded_claims: list[GroundedClaim] = Field(
        default_factory=list,
        description="Factual claims cross-referenced to source event IDs.",
    )
    order_index: int = Field(default=0, description="Sequential section order.")


class SafetyNarrativeDTO(AIAssistedRecordMixin, Part11AuditMixin):
    """Data Transfer Object representing a complete generative safety narrative."""

    id: str = Field(..., description="Unique narrative record ID.")
    study_id: str = Field(..., description="Clinical study identifier.")
    subject_id: str = Field(..., description="De-identified subject key.")
    case_id: str = Field(..., description="Worldwide unique case ID.")
    sae_event_key: str = Field(..., description="Target SAE event key.")
    title: str = Field(..., description="Narrative summary header.")
    sections: list[SafetyNarrativeSection] = Field(
        default_factory=list, description="Ordered ICH E2B(R3) narrative sections."
    )
    raw_narrative_text: str = Field(
        ..., description="Full concatenated narrative summary text."
    )
    timeline_events: list[ClinicalTimelineEvent] = Field(
        default_factory=list,
        description="Snapshot of chronological timeline events used for generation.",
    )
    grounded_claims: list[GroundedClaim] = Field(
        default_factory=list,
        description="All grounded claims extracted across sections.",
    )


class NarrativeGenerateRequest(BaseModel):
    """Request payload to generate a new AI safety narrative."""

    study_id: str = Field(..., description="Clinical study identifier.")
    subject_id: str = Field(..., description="De-identified subject key.")
    sae_event_key: str = Field(
        ..., description="Unique SAE event key (e.g. SUBJ-001:SEQ-1)."
    )
    worldwide_unique_case_id: str | None = Field(
        default=None, description="Optional external worldwide unique case ID."
    )
    reason_for_change: str = Field(
        ..., description="Mandatory GxP audit justification reason."
    )
    additional_context: str | None = Field(
        default=None, description="Optional clinical guidance or focus instructions."
    )


class NarrativeSignRequest(BaseModel):
    """Request payload to apply a 21 CFR Part 11 electronic signature to a safety narrative."""

    narrative_id: str = Field(..., description="Unique narrative record ID to sign.")
    reason_for_change: str = Field(
        ..., description="Electronic signature intent / reason for change."
    )
    signature_secret: str | None = Field(
        default=None, description="Optional signer secret or signing credential."
    )


class NarrativeSignResponse(BaseModel):
    """Response payload returned upon successful Part 11 signing."""

    narrative_id: str = Field(..., description="Signed narrative ID.")
    review_status: AIReviewStatus = Field(
        default=AIReviewStatus.APPROVED, description="Updated review status."
    )
    approved_by_user_id: str = Field(..., description="Signer user ID.")
    approved_at: str = Field(..., description="ISO UTC timestamp of approval.")
    esignature_manifest_id: str = Field(
        ..., description="Cryptographic signature manifest ID."
    )
    message: str = Field(..., description="Confirmation message.")


__all__ = [
    "ClinicalTimelineEvent",
    "GroundedClaim",
    "NarrativeGenerateRequest",
    "NarrativeSectionType",
    "NarrativeSignRequest",
    "NarrativeSignResponse",
    "SECTION_TITLE_MAP",
    "SafetyNarrativeDTO",
    "SafetyNarrativeSection",
    "SubjectSafetyTimeline",
    "TimelineEventType",
]
