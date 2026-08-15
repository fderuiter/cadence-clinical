from datetime import date, datetime
from typing import Any

from pydantic import BaseModel, Field, model_validator

from apps.etmf.adapters.models import TMFDocument
from apps.etmf.domain.acl import ProtocolVersionRef
from packages.deid.models import ComplianceProfile
from packages.security.signature import SigningReason


class IngestionRequest(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(None, description="Optional site identifier")
    idempotency_key: str | None = Field(
        None, description="Optional idempotency key for deduplication"
    )
    artifact_type: str = Field(
        ..., description="Type of artifact (e.g. Approved Protocol, Define-XML)"
    )
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Indexed, searchable content of the document")
    mime_type: str = Field(..., description="MIME type of the document")
    zone: int | None = Field(None, description="Optional expected DIA TMF Zone")
    section: str | None = Field(None, description="Optional expected DIA TMF Section")
    artifact_code: str | None = Field(
        None, description="Optional canonical artifact code"
    )
    taxonomy_version: str | None = Field(None, description="Optional taxonomy version")
    metadata_json: dict[str, Any] | None = Field(
        None, description="Optional metadata fields"
    )
    protocol_version: ProtocolVersionRef | None = Field(
        None, description="Optional shared protocol version reference"
    )
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )
    correlation_key: str | None = Field(
        None, description="Optional stable correlation key for synchronized documents"
    )
    content_checksum: str | None = Field(
        None, description="Optional deterministic checksum of the content"
    )
    source_system: str | None = Field(
        None, description="Optional originating source system"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> IngestionRequest:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class DocumentResponse(BaseModel):
    id: str
    study_id: str
    site_id: str | None = None
    zone: int
    section: str
    artifact_type: str
    filename: str
    mime_type: str
    created_at: str
    created_by: str
    version_index: int
    status: str
    taxonomy_version: str
    artifact_code: str
    metadata_json: dict[str, Any] | None = None

    document_type: str | None = None
    approval_status: str = "PENDING"
    signature_manifestation: dict[str, Any] | None = None
    signer: str | None = None
    signing_timestamp: str | None = None

    is_redacted: bool = False
    redaction_source_id: str | None = None
    redaction_manifest_json: dict[str, Any] | None = None

    reason_for_change: str | None = None
    protocol_version: ProtocolVersionRef | None = None
    issue_date: date | None = None
    expiration_date: date | None = None
    document_owner_id: str | None = None

    correlation_key: str | None = None
    content_checksum: str | None = None
    source_system: str | None = None
    sync_status: str | None = None


def to_document_response(doc: TMFDocument) -> DocumentResponse:
    return DocumentResponse(
        id=doc.id,
        study_id=doc.study_id,
        site_id=doc.site_id,
        zone=doc.zone,
        section=doc.section,
        artifact_type=doc.artifact_type,
        filename=doc.filename,
        mime_type=doc.mime_type,
        created_at=doc.created_at.isoformat(),
        created_by=doc.created_by,
        version_index=doc.version_index,
        status=doc.status,
        taxonomy_version=doc.taxonomy_version,
        artifact_code=doc.artifact_code,
        metadata_json=doc.metadata_json,
        document_type=doc.document_type,
        approval_status=doc.approval_status,
        signature_manifestation=doc.signature_manifestation,
        signer=doc.signer,
        signing_timestamp=(
            doc.signing_timestamp.isoformat() if doc.signing_timestamp else None
        ),
        is_redacted=doc.is_redacted,
        redaction_source_id=doc.redaction_source_id,
        redaction_manifest_json=doc.redaction_manifest_json,
        reason_for_change=doc.reason_for_change,
        protocol_version=(
            ProtocolVersionRef(
                study_id=doc.study_id,
                version_tag=doc.protocol_version_tag,
                version_index=doc.protocol_version_index,
                status=doc.protocol_version_status,
            )
            if doc.protocol_version_tag is not None
            and doc.protocol_version_index is not None
            and doc.protocol_version_status is not None
            else None
        ),
        issue_date=doc.issue_date,
        expiration_date=(
            doc.expiration_date.date()
            if isinstance(doc.expiration_date, datetime)
            else doc.expiration_date
        ),
        document_owner_id=doc.document_owner_id,
        correlation_key=doc.correlation_key,
        content_checksum=doc.content_checksum,
        source_system=doc.source_system,
        sync_status=doc.sync_status,
    )


class DocumentExpirationUpdate(BaseModel):
    issue_date: date | None = Field(None, description="Optional document issue date")
    expiration_date: date | None = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: str | None = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> DocumentExpirationUpdate:
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class RedactRequest(BaseModel):
    redacted_content: str = Field(..., description="The redacted text content")
    redacted_filename: str | None = Field(
        None, description="Optional new filename for the redacted document"
    )
    manifest: dict[str, Any] = Field(
        ..., description="The signed redaction manifest and provenance data"
    )


class AutomatedRedactRequest(BaseModel):
    profile: ComplianceProfile = Field(
        ComplianceProfile.HIPAA,
        description="The compliance profile governing active detection categories (e.g., HIPAA, GDPR, EU_CTR)",
    )
    custom_terms: list[str] | None = Field(
        None, description="Optional list of custom/literal terms to scan and redact"
    )
    strategies: dict[str, str] | None = Field(
        None,
        description="Optional custom mapping of category to specific strategy (e.g., mask, pseudonymize, date_shift, age_cap)",
    )
    redacted_filename: str | None = Field(
        None, description="Optional new filename for the redacted successor document"
    )


class AutomatedRedactResponse(BaseModel):
    status: str = Field(
        "success", description="Outcome status of the automated redaction"
    )
    document_id: str = Field(
        ..., description="ID of the newly created redacted document version"
    )
    version_index: int = Field(
        ..., description="Version index of the new redacted document"
    )
    filename: str = Field(..., description="Filename of the new redacted document")
    categories_counts: dict[str, int] = Field(
        ..., description="Count of redacted items per category"
    )
    manifest: dict[str, Any] = Field(
        ..., description="The signed manifest and provenance data"
    )


class SpanItem(BaseModel):
    start: int = Field(..., description="The character start offset in the source text")
    end: int = Field(..., description="The character end offset in the source text")
    label: str | None = Field(
        "manual", description="Optional label or category for the redacted span"
    )


class ManualRedactRequest(BaseModel):
    spans: list[SpanItem] | None = Field(
        None, description="Explicit character spans to redact"
    )
    terms: list[str] | None = Field(
        None, description="Literal terms to search and redact"
    )
    redacted_filename: str | None = Field(
        None, description="Optional new filename for the redacted successor document"
    )


class ManualRedactResponse(BaseModel):
    status: str = Field("success", description="Outcome status of the manual redaction")
    document_id: str = Field(
        ..., description="ID of the newly created redacted document version"
    )
    version_index: int = Field(
        ..., description="Version index of the new redacted document"
    )
    filename: str = Field(..., description="Filename of the new redacted document")
    categories_counts: dict[str, int] = Field(
        ..., description="Count of redacted items per category"
    )
    manifest: dict[str, Any] = Field(
        ..., description="The signed manifest and provenance data"
    )


class StudyArchiveRequest(BaseModel):
    reason_for_change: str = Field(
        ...,
        min_length=10,
        max_length=1000,
        description="Part 11 change justification reason for bulk study archive",
    )
    all_or_nothing: bool = Field(
        True,
        description="If True, rolling back the entire operation if any eligible document fails to transition.",
    )


class StudyArchiveItemResult(BaseModel):
    document_id: str
    filename: str
    from_status: str
    to_status: str
    status: str
    error_message: str | None = None


class StudyArchiveResponse(BaseModel):
    status: str
    study_id: str
    total_processed: int
    successful_count: int
    failed_count: int
    skipped_count: int
    results: list[StudyArchiveItemResult]


class TransitionRequest(BaseModel):
    to_status: str = Field(
        ...,
        description="Target status (e.g. TECHNICAL_QC, CLINICAL_QC, APPROVED, ARCHIVED, REJECTED)",
    )
    reason_for_change: str = Field(
        ...,
        min_length=10,
        max_length=1000,
        description="Part 11 change justification reason",
    )


class SignDocumentRequest(BaseModel):
    signing_reason: SigningReason = Field(
        ...,
        description="Controlled reason for creating this electronic signature in compliance with 21 CFR Part 11",
    )


class TransitionResponse(BaseModel):
    id: str
    document_id: str
    from_status: str
    to_status: str
    actor_id: str
    actor_role: str
    reason_for_change: str
    timestamp: str


class AuditLogResponse(BaseModel):
    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    document_id: str | None
    details: str


class PaginatedAuditLogResponse(BaseModel):
    items: list[AuditLogResponse]
    total_count: int
    limit: int
    offset: int
    next_page: str | None = None
    next_cursor: str | None = None
    has_more: bool


class ExpectedDocumentCreate(BaseModel):
    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: str | None = Field(
        None, description="Optional site identifier (null = study-scope)"
    )
    milestone: str = Field(
        ..., description="Milestone name (e.g. INITIATION, CONDUCT, CLOSEOUT)"
    )
    artifact_type: str = Field(..., description="Mandatory artifact type")
    zone: int | None = Field(None, description="Optional DIA TMF Zone")
    section: str | None = Field(None, description="Optional DIA TMF Section")
    metadata_json: dict[str, Any] | None = Field(
        None, description="Optional metadata rules or notes"
    )
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 justification reason"
    )


class ExpectedDocumentResponse(BaseModel):
    id: str
    study_id: str
    site_id: str | None = None
    milestone: str
    artifact_type: str
    zone: int | None = None
    section: str | None = None
    metadata_json: dict[str, Any] | None = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class ArtifactDetail(BaseModel):
    artifact_type: str
    scope: str
    status: str
    document_id: str | None = None
    version_index: int | None = None


class CompletenessResponse(BaseModel):
    study_id: str
    site_id: str | None = None
    milestone: str
    is_complete: bool
    scope: str
    present_artifacts: list[str]
    missing_artifacts: list[str]
    per_artifact_detail: list[ArtifactDetail]


class BinderArtifactNode(BaseModel):
    artifact_code: str
    artifact_name: str
    status: str
    document_id: str | None = None
    version_index: int | None = None


class BinderSectionNode(BaseModel):
    section_code: str
    section_name: str
    artifacts: list[BinderArtifactNode]


class BinderZoneNode(BaseModel):
    zone_code: int
    zone_name: str
    sections: list[BinderSectionNode]


class BinderStructureResponse(BaseModel):
    study_id: str
    milestone: str | None = None
    site_id: str | None = None
    zones: list[BinderZoneNode]
    present_artifacts: list[str]
    missing_artifacts: list[str]


class DocumentVersionEntry(BaseModel):
    id: str
    version_index: int
    status: str
    approval_status: str
    created_at: str
    created_by: str
    filename: str
    artifact_code: str
    signer: str | None = None
    signing_timestamp: str | None = None
    transitions: list[TransitionResponse]


class DocumentVersionsResponse(BaseModel):
    study_id: str
    artifact_code: str
    versions: list[DocumentVersionEntry]


class SeedEDLRequest(BaseModel):
    milestones: list[str] | None = Field(
        None,
        description="Optional list of milestones to seed. Defaults to STUDY_INITIATION, ETHICS_SUBMISSION, SITE_ACTIVATION, FSI.",
    )
    reason_for_change: str = Field(
        "Zero-Click USDM Study Ingestion",
        description="Part 11 change justification reason for seeding EDL",
    )
