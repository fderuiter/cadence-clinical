"""Pydantic schemas for eConsent service presentation layer.

Includes 21 CFR Part 11 multisig schemas, granular research options,
template amendment diff models, re-consent tracking, withdrawal records, and CDISC ODM exports.
"""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field, field_validator

from apps.econsent.domain.localization.models import validate_language_code
from packages.database.audit import AuditFields


class ConsentDocumentCreate(AuditFields):
    study_id: str = Field(..., description="Unique clinical study identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    document_name: str = Field(
        ..., max_length=255, description="Name of the eConsent form/document"
    )
    content: str = Field(..., description="Full text/content of the consent form")


class ConsentDocumentResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of the document")
    study_id: str = Field(..., description="Unique clinical study identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    document_name: str = Field(..., description="Name of the eConsent form/document")
    content: str = Field(..., description="Full text/content of the consent form")


class ConsentClauseCreate(AuditFields):
    clause_id: str | None = Field(
        None,
        description="Unique clause identifier across versions. Generated if not provided.",
    )
    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., max_length=255, description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


class ConsentClauseResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of this version")
    clause_id: str = Field(..., description="Unique clause identifier across versions")
    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


class ConsentClauseUpdate(AuditFields):
    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., max_length=255, description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


class ConsentTemplateCreate(AuditFields):
    template_id: str | None = Field(
        None,
        description="Unique template identifier across versions. Generated if not provided.",
    )
    study_id: str = Field(..., description="Unique clinical study identifier")
    template_name: str = Field(..., max_length=255, description="Name of the template")
    protocol_version: str = Field(
        ..., max_length=255, description="Associated clinical protocol version"
    )
    requires_reconsent: bool = Field(False, description="Requires re-consent on change")
    clauses: list[str] = Field(
        default_factory=list,
        description="Ordered clause_ids referenced by this template",
    )
    workflow_steps: list[dict] = Field(
        default_factory=list, description="Workflow steps config"
    )


class ConsentTemplateResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of this version")
    template_id: str = Field(
        ..., description="Unique template identifier across versions"
    )
    study_id: str = Field(..., description="Unique clinical study identifier")
    template_name: str = Field(..., description="Name of the template")
    protocol_version: str = Field(
        ..., description="Associated clinical protocol version"
    )
    is_published: bool = Field(..., description="Publication state")
    requires_reconsent: bool = Field(..., description="Requires re-consent on change")
    clauses: list[str] = Field(
        default_factory=list,
        description="Ordered clause_ids referenced by this template",
    )
    workflow_steps: list[dict] = Field(
        default_factory=list, description="Workflow steps config"
    )


class ConsentTemplateUpdate(AuditFields):
    study_id: str = Field(..., description="Unique clinical study identifier")
    template_name: str = Field(..., max_length=255, description="Name of the template")
    protocol_version: str = Field(
        ..., max_length=255, description="Associated clinical protocol version"
    )
    requires_reconsent: bool = Field(False, description="Requires re-consent on change")
    clauses: list[str] = Field(
        default_factory=list,
        description="Ordered clause_ids referenced by this template",
    )
    workflow_steps: list[dict] = Field(
        default_factory=list, description="Workflow steps config"
    )


class ComposedClauseResponse(BaseModel):
    clause_id: str
    title: str
    text: str
    version_index: int


class ComposedTemplateResponse(BaseModel):
    id: str
    template_id: str
    study_id: str
    template_name: str
    protocol_version: str
    is_published: bool
    requires_reconsent: bool
    version_index: int
    clauses: list[ComposedClauseResponse]
    workflow_steps: list[dict]
    created_at: datetime
    created_by: str
    reason_for_change: str


class ConsentTranslationCreate(AuditFields):
    translation_id: str | None = Field(
        None,
        description="Unique translation identifier across versions. Generated if not provided.",
    )
    source_id: str = Field(
        ..., description="Unique source clause_id or template_id being translated"
    )
    source_type: str = Field(
        ..., description="The type of the source: 'clause' or 'template'"
    )
    source_version_index: int = Field(
        ..., description="The version of the source being translated"
    )
    language_code: str = Field(..., description="Validated ISO 639-1 language code")
    translated_title: str = Field(
        ..., max_length=255, description="Translated title of the clause/template"
    )
    translated_text: str = Field(..., description="Translated text/content")

    @field_validator("language_code")
    @classmethod
    def check_lang_code(cls, v: str) -> str:
        return validate_language_code(v)

    @field_validator("source_type")
    @classmethod
    def check_source_type(cls, v: str) -> str:
        v_clean = v.strip().lower()
        if v_clean not in ("clause", "template"):
            raise ValueError("source_type must be either 'clause' or 'template'")
        return v_clean


class ConsentTranslationResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str = Field(
        ..., description="Unique generated UUID of this translation version"
    )
    translation_id: str = Field(
        ..., description="Unique translation identifier across versions"
    )
    source_id: str = Field(
        ..., description="Unique source clause_id or template_id being translated"
    )
    source_type: str = Field(
        ..., description="The type of the source: 'clause' or 'template'"
    )
    source_version_index: int = Field(
        ..., description="The version of the source being translated"
    )
    language_code: str = Field(..., description="Validated ISO 639-1 language code")
    translated_title: str = Field(
        ..., description="Translated title of the clause/template"
    )
    translated_text: str = Field(..., description="Translated text/content")
    status: str = Field(
        ..., description="The status of the translation (DRAFT, IN_REVIEW, APPROVED)"
    )


class ConsentTranslationUpdate(AuditFields):
    source_id: str = Field(
        ..., description="Unique source clause_id or template_id being translated"
    )
    source_type: str = Field(
        ..., description="The type of the source: 'clause' or 'template'"
    )
    source_version_index: int = Field(
        ..., description="The version of the source being translated"
    )
    language_code: str = Field(..., description="Validated ISO 639-1 language code")
    translated_title: str = Field(
        ..., max_length=255, description="Translated title of the clause/template"
    )
    translated_text: str = Field(..., description="Translated text/content")

    @field_validator("language_code")
    @classmethod
    def check_lang_code(cls, v: str) -> str:
        return validate_language_code(v)

    @field_validator("source_type")
    @classmethod
    def check_source_type(cls, v: str) -> str:
        v_clean = v.strip().lower()
        if v_clean not in ("clause", "template"):
            raise ValueError("source_type must be either 'clause' or 'template'")
        return v_clean


class TranslationTransitionRequest(BaseModel):
    status: str = Field(
        ..., description="Target status: 'DRAFT', 'IN_REVIEW', or 'APPROVED'"
    )
    reason_for_change: str = Field(..., description="Explanation of transition")

    @field_validator("status")
    @classmethod
    def check_status(cls, v: str) -> str:
        v_clean = v.strip().upper()
        if v_clean not in (
            "DRAFT",
            "IN_REVIEW",
            "APPROVED",
            "PUBLISHED",
            "REJECTED",
            "RETIRED",
        ):
            raise ValueError(f"status '{v_clean}' is not recognized.")
        return v_clean


class ComprehensionCheckCreate(AuditFields):
    questions: list[dict] = Field(..., description="List of question dicts")
    expected_answers: dict[str, str] = Field(
        ..., description="Mapping of question_id to expected answer"
    )
    threshold_policy: dict = Field(
        ..., description="Evaluation threshold policy, e.g. {'min_correct': 2}"
    )


class ComprehensionCheckResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    questions: list[dict]
    expected_answers: dict[str, str]
    threshold_policy: dict


class ComprehensionSubmissionRequest(BaseModel):
    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    submitted_answers: dict[str, str] = Field(
        ..., description="Mapping of question_id to submitted answer"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 signature/evaluation change reason"
    )


class ComprehensionSubmissionResponse(BaseModel):
    passed: bool
    score: float
    total_questions: int
    correct_count: int
    min_required: int
    next_step: str
    message: str
    details: list[dict[str, Any]] | None = None


class ConsentSignatureRequest(BaseModel):
    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    signature_data: str | None = Field(
        None, description="Electronic signature data (drawing or string)"
    )
    reason_for_change: str = Field(..., description="Change reason for signing")
    site_id: str | None = Field(None, description="Optional site identifier")
    role: str = Field(
        "SUBJECT",
        description="Signer role: SUBJECT, LAR, MINOR_ASSENT, INVESTIGATOR, WITNESS",
    )
    signer_name: str | None = Field(None, description="Printed legal name of signer")
    signer_email: str | None = Field(None, description="Contact email of signer")
    meaning: str | None = Field(None, description="21 CFR Part 11 signing intent")
    lar_relationship: str | None = Field(
        None, description="Relationship if role is LAR"
    )
    lar_authority_basis: str | None = Field(
        None, description="Legal basis if role is LAR"
    )


class ConsentSignatureResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    subject_pseudonym: str
    role: str = "SUBJECT"
    signer_name: str | None = None
    signer_email: str | None = None
    meaning: str | None = None
    signature_data: str | None
    signed_at: datetime
    digest_sha256: str | None = None
    lar_relationship: str | None = None
    lar_authority_basis: str | None = None


class ArchivalDeliveryResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    status: str
    attempts: int
    last_error: str | None = None
    next_retry_at: datetime | None = None
    completed_at: datetime | None = None
    retry_eligible: bool
    correlation_id: str
    template_id: str
    version_index: int
    subject_pseudonym: str
    study_id: str
    site_id: str | None = None
    etmf_document_id: str | None = None


class SubjectConsentCaptureRequest(BaseModel):
    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    site_id: str = Field(..., description="Unique clinical site identifier")
    device_timestamp: datetime | None = Field(None, description="Device-side timestamp")
    source_content_identity: str = Field(
        ..., description="Hash/clause-set identifier at capture time"
    )
    reason_for_change: str = Field(
        ..., description="Part 11 rationale/change justification"
    )
    signatures: list[dict[str, Any]] | None = Field(
        None, description="Optional multi-party signature list"
    )
    granular_selections: list[dict[str, Any]] | None = Field(
        None, description="Optional granular opt-in selections"
    )


class SubjectConsentResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of this consent record")
    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    study_id: str = Field(..., description="Unique clinical study identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    template_id: str = Field(
        ..., description="The template identifier used for consent"
    )
    protocol_version: str = Field(
        ..., description="Associated clinical protocol version snapshot"
    )
    source_content_identity: str = Field(
        ..., description="Hash/clause-set identifier at capture time"
    )
    status: str = Field(
        "ACTIVE", description="Consent status: ACTIVE, SUPERSEDED, WITHDRAWN"
    )
    server_timestamp: datetime = Field(
        ..., description="Server-side chronological capture timestamp"
    )
    device_timestamp: datetime | None = Field(
        None, description="Device-side capture timestamp"
    )
    signature_manifest: dict = Field(
        ...,
        description="Detailed signature manifestation, canonical hmac signature, and payload hash",
    )


class SubjectConsentStatusResponse(BaseModel):
    subject_pseudonym: str
    study_id: str
    site_id: str
    template_id: str
    version_index: int
    protocol_version: str
    signed: bool
    comprehension_passed: bool
    status: str = "ACTIVE"
    requires_reconsent: bool = False


# --- Granular Options DTOs ---
class GranularOptionCreate(BaseModel):
    option_code: str = Field(
        ..., description="Unique key for the option, e.g. OPT_GENETICS"
    )
    title: str = Field(..., max_length=255, description="Title of the optional choice")
    description: str = Field(
        ..., description="Detailed description of optional procedure"
    )
    category: str = Field(
        "OTHER",
        description="Category: GENETICS, BIOBANKING, DATA_SHARING, FUTURE_CONTACT, SUBSTUDY",
    )
    is_mandatory: bool = Field(False, description="Whether selection is mandatory")
    default_selected: bool = Field(False, description="Default initial state")
    reason_for_change: str = Field(..., description="Mandatory change justification")


class GranularOptionResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    option_code: str
    title: str
    description: str
    category: str
    is_mandatory: bool
    default_selected: bool


class GranularSelectionInput(BaseModel):
    option_code: str
    selected: bool


# --- Template Diff DTOs ---
class ClauseDiffDTO(BaseModel):
    clause_id: str
    change_type: str
    old_title: str | None
    new_title: str | None
    old_text: str | None
    new_text: str | None
    text_diff: str | None
    is_substantive: bool


class TemplateDiffResponse(BaseModel):
    template_id: str
    base_version_index: int
    target_version_index: int
    total_added: int
    total_removed: int
    total_modified: int
    total_unchanged: int
    requires_reconsent: bool
    substantive_summary: list[str]
    clause_diffs: list[ClauseDiffDTO]


# --- Reconsent DTOs ---
class ReconsentTriggerRequest(BaseModel):
    study_id: str
    site_id: str | None = None
    prior_version_index: int
    new_version_index: int
    change_summary: str
    substantive_changes: list[dict[str, Any]] = Field(default_factory=list)
    reason_for_change: str = Field(..., description="Mandatory change justification")


class ReconsentRequirementResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str | None
    template_id: str
    prior_version_index: int
    new_version_index: int
    subject_pseudonym: str
    status: str
    change_summary: str
    substantive_changes: list[dict[str, Any]]
    deadline_at: datetime | None
    completed_consent_id: str | None


# --- Consent Withdrawal DTOs ---
class ConsentWithdrawalRequest(BaseModel):
    study_id: str
    site_id: str
    subject_pseudonym: str
    template_id: str
    withdrawal_date: datetime | None = None
    reason_category: str = Field(
        ..., description="Category: Adverse Event, Personal Choice, Relocation, Other"
    )
    reason_detail: str = Field(..., description="Specific explanation for withdrawal")
    scope: str = Field(
        "STOP_ALL_DATA_COLLECTION",
        description="STOP_INTERVENTIONS_ONLY, STOP_ALL_DATA_COLLECTION, DESTROY_SAMPLES",
    )
    investigator_id: str | None = None
    reason_for_change: str = Field(..., description="Mandatory change justification")


class ConsentWithdrawalResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    study_id: str
    site_id: str
    subject_pseudonym: str
    template_id: str
    withdrawal_date: datetime
    reason_category: str
    reason_detail: str
    scope: str
    acknowledged_by_investigator: bool
    investigator_id: str | None


# --- Export & Audit DTOs ---
class CdiscOdmExportResponse(BaseModel):
    study_id: str
    subject_pseudonym: str
    template_id: str
    version_index: int
    odm_version: str
    xml_content: str


class VerifiableCertificateResponse(BaseModel):
    study_id: str
    subject_pseudonym: str
    template_id: str
    version_index: int
    html_content: str
    digest_sha256: str


class ConsentAuditLogResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    timestamp: datetime
    actor_id: str
    actor_role: str
    action: str
    document_id: str | None
    details: str
    reason_for_change: str


# --- Readability & Jargon Harmonization DTOs ---
class ReadabilityMetricsDTO(BaseModel):
    word_count: int
    sentence_count: int
    syllable_count: int
    difficult_word_count: int
    difficult_words: list[str]
    flesch_reading_ease: float
    flesch_kincaid_grade_level: float
    dale_chall_score: float
    dale_chall_grade_level: str
    is_target_grade_level: bool
    interpretation: str


class ReadabilityAnalysisRequest(BaseModel):
    text: str = Field(
        ..., min_length=1, description="Consent narrative or clause text to analyze"
    )
    study_id: str | None = Field(
        None, description="Optional associated clinical study identifier"
    )


class ReadabilityAnalysisResponse(BaseModel):
    metrics: ReadabilityMetricsDTO


class JargonSubstitutionDTO(BaseModel):
    original_term: str
    suggested_term: str
    rationale: str
    category: str = "clinical_terminology"
    confidence_score: float = 0.95
    start_offset: int | None = None
    end_offset: int | None = None


class ReadabilityHarmonizationRequest(BaseModel):
    text: str = Field(
        ..., min_length=1, description="Original consent clause or text to harmonize"
    )
    study_id: str | None = Field(
        None, description="Optional associated clinical study identifier"
    )
    target_grade_level: float = Field(
        8.0, ge=4.0, le=12.0, description="Target reading grade level"
    )
    protocol_version: str | None = Field(
        None, description="Protocol amendment version tag"
    )


class ReadabilityHarmonizationResponse(BaseModel):
    original_metrics: ReadabilityMetricsDTO
    harmonized_metrics: ReadabilityMetricsDTO
    substitutions: list[JargonSubstitutionDTO]
    harmonized_text: str
    grade_level_delta: float
    model_identifier: str


class ClauseHarmonizationApplyRequest(BaseModel):
    harmonized_text: str = Field(
        ..., min_length=1, description="Harmonized plain-language clause text"
    )
    reason_for_change: str = Field(
        ...,
        min_length=1,
        description="21 CFR Part 11 audit reason and protocol amendment reference",
    )
    protocol_version: str | None = Field(
        None, description="Protocol amendment version reference tag"
    )


class ClauseHarmonizationApplyResponse(BaseModel):
    clause_id: str
    version_index: int
    title: str
    text: str
    metrics: ReadabilityMetricsDTO
    protocol_version: str | None
    created_at: datetime
    created_by: str
    reason_for_change: str
