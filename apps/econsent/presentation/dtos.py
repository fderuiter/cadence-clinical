"""Pydantic schemas for eConsent service presentation layer."""

from datetime import datetime

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
        if v_clean not in ("DRAFT", "IN_REVIEW", "APPROVED"):
            raise ValueError(
                "status must be either 'DRAFT', 'IN_REVIEW', or 'APPROVED'"
            )
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


class ConsentSignatureRequest(BaseModel):
    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    signature_data: str | None = Field(
        None, description="Electronic signature data (drawing or string)"
    )
    reason_for_change: str = Field(..., description="Change reason for signing")
    site_id: str | None = Field(None, description="Optional site identifier")


class ConsentSignatureResponse(AuditFields):
    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    subject_pseudonym: str
    signature_data: str | None
    signed_at: datetime


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
    requires_reconsent: bool = False
