import email.utils
import os
import time
from datetime import date, datetime
from typing import Any, Dict, List, Optional

from fastapi import (
    Depends,
    FastAPI,
    HTTPException,
    Query,
    Request,
)
from fastapi.responses import Response
from protocol_version_ref import ProtocolVersionRef
from pydantic import BaseModel, Field, model_validator
from signature import SigningReason
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from tmf_reference_model import (
    get_active_catalog,
    get_mandatory_artifacts,
    resolve_artifact,
)

from apps.etmf.database import db_manager
from apps.etmf.export import generate_binder_zip
from apps.etmf.ingestion_service import ingest_tmf_document
from apps.etmf.lifecycle import validate_and_transition_document_status
from apps.etmf.models import (
    Base,
    DocumentQCTransition,
    DocumentStatus,
    ExpectedDocument,
    TMFAuditLog,
    TMFDocument,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.deid.detector import DeidDetector
from packages.deid.manifest import build_redaction_manifest, sign_manifest_symmetric
from packages.deid.models import ComplianceProfile, DetectionResult, DetectorCategory
from packages.deid.transforms import apply_deid_transforms
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import Principal, get_principal, has_permission

DATABASE_URL = os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


def normalize_milestone(milestone: str) -> str:
    """
    Normalizes milestone string to one of the canonical forms: INITIATION, CONDUCT, CLOSEOUT.
    """
    norm = milestone.strip().upper()
    if norm in ("INITIATION", "STUDY START"):
        return "INITIATION"
    elif norm in ("CONDUCT", "DATA COLLECTION"):
        return "CONDUCT"
    elif norm in ("CLOSEOUT", "STUDY CLOSED", "LOCK"):
        return "CLOSEOUT"
    return norm


async def seed_default_edl(
    session: AsyncSession, study_id: str, milestone: str
) -> None:
    """
    Idempotently seeds default study-scope ExpectedDocument rows for a given study and milestone.
    """
    canonical = normalize_milestone(milestone)

    # Check if any expectations already exist for this study and milestone
    stmt = select(ExpectedDocument).where(
        ExpectedDocument.study_id == study_id,
        ExpectedDocument.milestone == canonical,
        ExpectedDocument.site_id.is_(None),
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()
    if existing:
        return

    # Map milestone to mandatory artifacts using the catalog API
    version = get_active_catalog().version
    try:
        mandatory_artifacts = get_mandatory_artifacts(canonical, version)
    except ValueError:
        return

    for art in mandatory_artifacts:
        doc = ExpectedDocument(
            study_id=study_id,
            milestone=canonical,
            artifact_type=art.name,
            zone=art.zone_code,
            section=art.section_code,
            created_by="system",
            reason_for_change="System-initiated default seeding of expected documents list",
            version_index=1,
            metadata_json={"default_seeded": True},
        )
        session.add(doc)
    await session.flush()


async def etmf_startup() -> None:
    """Startup hook to seed default EDL and start background sealer."""
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        for study_id in [
            "study_001",
            "study_abc",
            "study_xyz",
            "study_123",
            "study_111",
        ]:
            for milestone in ["INITIATION", "CONDUCT", "CLOSEOUT"]:
                await seed_default_edl(session, study_id, milestone)
        await session.commit()

    from apps.etmf.sealer import start_background_etmf_sealer

    await start_background_etmf_sealer(db_manager.get_session_maker())


async def etmf_shutdown() -> None:
    """Shutdown hook to stop the background sealer."""
    from apps.etmf.sealer import stop_background_etmf_sealer

    await stop_background_etmf_sealer()


app = FastAPI(
    title="Cadence Clinical - Event-Driven eTMF Module",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
        startup_hooks=[etmf_startup],
        shutdown_hooks=[etmf_shutdown],
    ),
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


# Dependable to obtain database session
get_db_session = DatabaseSessionDependency(db_manager)


# Helper to map standard artifact types to DIA TMF Zones
def map_artifact_to_tmf(artifact_type: str) -> tuple[int, str]:
    """
    Maps standard clinical artifacts to DIA TMF Zones and Sections using the active catalog.
    Uses the active taxonomy catalog version under the hood.
    Raises ValueError if artifact cannot be resolved.
    """
    version = get_active_catalog().version
    is_code = False
    cleaned_type = artifact_type.strip()

    # Map/Normalize aliases to canonical names or codes
    if cleaned_type == "FORM_1572":
        cleaned_type = "FDA Form 1572"
    elif cleaned_type == "FINANCIAL_DISCLOSURE":
        cleaned_type = "Financial Disclosure"
    elif cleaned_type == "PROTOCOL_SIGNOFF":
        cleaned_type = "Protocol Sign-off"

    if cleaned_type and cleaned_type.replace(".", "").isdigit():
        is_code = True

    if is_code:
        res = resolve_artifact(version, code=cleaned_type)
    else:
        res = resolve_artifact(version, name=cleaned_type)

    return res["zone"].code, res["section"].code


# Pydantic models for eTMF
class IngestionRequest(BaseModel):
    """
    Payload for system event or manual ingestion of TMF documents.
    """

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: Optional[str] = Field(None, description="Optional site identifier")
    artifact_type: str = Field(
        ..., description="Type of artifact (e.g. Approved Protocol, Define-XML)"
    )
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Indexed, searchable content of the document")
    mime_type: str = Field(..., description="MIME type of the document")
    zone: Optional[int] = Field(None, description="Optional expected DIA TMF Zone")
    section: Optional[str] = Field(
        None, description="Optional expected DIA TMF Section"
    )
    artifact_code: Optional[str] = Field(
        None, description="Optional canonical artifact code"
    )
    taxonomy_version: Optional[str] = Field(
        None, description="Optional taxonomy version"
    )
    metadata_json: Optional[Dict[str, Any]] = Field(
        None, description="Optional metadata fields"
    )
    protocol_version: Optional[ProtocolVersionRef] = Field(
        None, description="Optional shared protocol version reference"
    )
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> "IngestionRequest":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class DocumentResponse(BaseModel):
    """
    Representation of an eTMF document.
    """

    id: str
    study_id: str
    site_id: Optional[str] = None
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
    metadata_json: Optional[Dict[str, Any]] = None

    # New signature and lifecycle fields
    document_type: Optional[str] = None
    approval_status: str = "PENDING"
    signature_manifestation: Optional[Dict[str, Any]] = None
    signer: Optional[str] = None
    signing_timestamp: Optional[str] = None

    # Redaction-related fields
    is_redacted: bool = False
    redaction_source_id: Optional[str] = None
    redaction_manifest_json: Optional[Dict[str, Any]] = None

    # Extended justification and protocol amendment version references
    reason_for_change: Optional[str] = None
    protocol_version: Optional[ProtocolVersionRef] = None
    issue_date: Optional[date] = None
    expiration_date: Optional[date] = None
    document_owner_id: Optional[str] = None


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
        expiration_date=doc.expiration_date,
        document_owner_id=doc.document_owner_id,
    )


class DocumentExpirationUpdate(BaseModel):
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> "DocumentExpirationUpdate":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class RedactRequest(BaseModel):
    """
    Payload for submitting redacted content as a new version.
    """

    redacted_content: str = Field(..., description="The redacted text content")
    redacted_filename: Optional[str] = Field(
        None, description="Optional new filename for the redacted document"
    )
    manifest: Dict[str, Any] = Field(
        ..., description="The signed redaction manifest and provenance data"
    )


class AutomatedRedactRequest(BaseModel):
    """
    Payload for requesting automated redaction on an eTMF document.
    """

    profile: ComplianceProfile = Field(
        ComplianceProfile.HIPAA,
        description="The compliance profile governing active detection categories (e.g., HIPAA, GDPR, EU_CTR)",
    )
    custom_terms: Optional[List[str]] = Field(
        None, description="Optional list of custom/literal terms to scan and redact"
    )
    strategies: Optional[Dict[str, str]] = Field(
        None,
        description="Optional custom mapping of category to specific strategy (e.g., mask, pseudonymize, date_shift, age_cap)",
    )
    redacted_filename: Optional[str] = Field(
        None, description="Optional new filename for the redacted successor document"
    )


class AutomatedRedactResponse(BaseModel):
    """
    Response detailing the automated redaction operation outcomes.
    Crucially, it never exposes raw matched PII/PHI identifiers.
    """

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
    categories_counts: Dict[str, int] = Field(
        ..., description="Count of redacted items per category"
    )
    manifest: Dict[str, Any] = Field(
        ..., description="The signed manifest and provenance data"
    )


class SpanItem(BaseModel):
    """
    Explicit character span to redact in manual redaction.
    """

    start: int = Field(..., description="The character start offset in the source text")
    end: int = Field(..., description="The character end offset in the source text")
    label: Optional[str] = Field(
        "manual", description="Optional label or category for the redacted span"
    )


class ManualRedactRequest(BaseModel):
    """
    Payload for submitting manual redaction parameters (spans and/or terms).
    """

    spans: Optional[List[SpanItem]] = Field(
        None, description="Explicit character spans to redact"
    )
    terms: Optional[List[str]] = Field(
        None, description="Literal terms to search and redact"
    )
    redacted_filename: Optional[str] = Field(
        None, description="Optional new filename for the redacted successor document"
    )


class ManualRedactResponse(BaseModel):
    """
    Response detailing the manual redaction operation outcomes.
    Crucially, it never exposes raw matched PII/PHI identifiers.
    """

    status: str = Field("success", description="Outcome status of the manual redaction")
    document_id: str = Field(
        ..., description="ID of the newly created redacted document version"
    )
    version_index: int = Field(
        ..., description="Version index of the new redacted document"
    )
    filename: str = Field(..., description="Filename of the new redacted document")
    categories_counts: Dict[str, int] = Field(
        ..., description="Count of redacted items per category"
    )
    manifest: Dict[str, Any] = Field(
        ..., description="The signed manifest and provenance data"
    )


class StudyArchiveRequest(BaseModel):
    """
    Payload to request bulk study-level document archival.
    """

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
    """
    Detailed result for an individual document's archival attempt.
    """

    document_id: str
    filename: str
    from_status: str
    to_status: str
    status: str  # "success", "skipped", "failed"
    error_message: Optional[str] = None


class StudyArchiveResponse(BaseModel):
    """
    Response model for bulk study archive operation.
    """

    status: str  # "success", "partial_success", "failed"
    study_id: str
    total_processed: int
    successful_count: int
    failed_count: int
    skipped_count: int
    results: List[StudyArchiveItemResult]


class TransitionRequest(BaseModel):
    """
    Payload to request a secure 21 CFR Part 11 compliant QC transition on a document.
    """

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
    """
    Payload for submitting a signing and approval request.
    """

    signing_reason: SigningReason = Field(
        ...,
        description="Controlled reason for creating this electronic signature in compliance with 21 CFR Part 11",
    )


class TransitionResponse(BaseModel):
    """
    Representation of an immutable append-only DocumentQCTransition log record.
    """

    id: str
    document_id: str
    from_status: str
    to_status: str
    actor_id: str
    actor_role: str
    reason_for_change: str
    timestamp: str


class AuditLogResponse(BaseModel):
    """
    Representation of an eTMF audit trail log.
    """

    id: str
    timestamp: str
    user_id: str
    user_role: str
    action: str
    document_id: Optional[str]
    details: str


class PaginatedAuditLogResponse(BaseModel):
    """
    Paginated representation of eTMF audit trail logs.
    """

    items: List[AuditLogResponse]
    total_count: int
    limit: int
    offset: int
    next_page: Optional[str] = None
    next_cursor: Optional[str] = None
    has_more: bool


class ExpectedDocumentCreate(BaseModel):
    """
    Payload to create/update an Expected Document List (EDL) expectation.
    """

    study_id: str = Field(..., description="Unique identifier of the clinical study")
    site_id: Optional[str] = Field(
        None, description="Optional site identifier (null = study-scope)"
    )
    milestone: str = Field(
        ..., description="Milestone name (e.g. INITIATION, CONDUCT, CLOSEOUT)"
    )
    artifact_type: str = Field(..., description="Mandatory artifact type")
    zone: Optional[int] = Field(None, description="Optional DIA TMF Zone")
    section: Optional[str] = Field(None, description="Optional DIA TMF Section")
    metadata_json: Optional[Dict[str, Any]] = Field(
        None, description="Optional metadata rules or notes"
    )
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 justification reason"
    )


class ExpectedDocumentResponse(BaseModel):
    """
    Representation of an EDL expectation record.
    """

    id: str
    study_id: str
    site_id: Optional[str] = None
    milestone: str
    artifact_type: str
    zone: Optional[int] = None
    section: Optional[str] = None
    metadata_json: Optional[Dict[str, Any]] = None
    created_at: str
    created_by: str
    reason_for_change: str
    version_index: int


class ArtifactDetail(BaseModel):
    """
    Enriched per-artifact completeness detail.
    """

    artifact_type: str
    scope: str
    status: str
    document_id: Optional[str] = None
    version_index: Optional[int] = None


class CompletenessResponse(BaseModel):
    """
    Completeness dashboard check response.
    """

    study_id: str
    site_id: Optional[str] = None
    milestone: str
    is_complete: bool
    scope: str
    present_artifacts: List[str]
    missing_artifacts: List[str]
    per_artifact_detail: List[ArtifactDetail]


class BinderArtifactNode(BaseModel):
    """
    Representation of an artifact node in the binder structure.
    """

    artifact_code: str
    artifact_name: str
    status: str  # EXPECTED/PRESENT/MISSING
    document_id: Optional[str] = None
    version_index: Optional[int] = None


class BinderSectionNode(BaseModel):
    """
    Representation of a section node in the binder structure.
    """

    section_code: str
    section_name: str
    artifacts: List[BinderArtifactNode]


class BinderZoneNode(BaseModel):
    """
    Representation of a zone node in the binder structure.
    """

    zone_code: int
    zone_name: str
    sections: List[BinderSectionNode]


class BinderStructureResponse(BaseModel):
    """
    Top-level binder structure response.
    """

    study_id: str
    milestone: Optional[str] = None
    site_id: Optional[str] = None
    zones: List[BinderZoneNode]
    present_artifacts: List[str]
    missing_artifacts: List[str]


class DocumentVersionEntry(BaseModel):
    """
    Representation of a specific document version lineage entry.
    """

    id: str
    version_index: int
    status: str
    approval_status: str
    created_at: str
    created_by: str
    filename: str
    artifact_code: str
    signer: Optional[str] = None
    signing_timestamp: Optional[str] = None
    transitions: List[TransitionResponse]


class DocumentVersionsResponse(BaseModel):
    """
    Response containing all versions and transitions for a document's lineage.
    """

    study_id: str
    artifact_code: str
    versions: List[DocumentVersionEntry]


# Helper to secure and log actions
async def write_audit_log(
    session: AsyncSession,
    user_id: str,
    user_role: str | list[str],
    action: str,
    document_id: Optional[str],
    details: str,
) -> None:
    """
    Utility function to write to the immutable eTMF audit ledger.
    """
    if isinstance(user_role, list):
        user_role_str = ",".join(user_role)
    else:
        user_role_str = user_role

    log_entry = TMFAuditLog(
        user_id=user_id,
        user_role=user_role_str,
        action=action,
        document_id=document_id,
        details=details,
    )
    session.add(log_entry)
    await session.flush()


def enforce_document_site_visibility(doc: TMFDocument, principal: Principal) -> None:
    """
    Enforces document-level site-scope visibility rules.
    Site-scoped users are restricted to documents at their assigned sites and cannot see study-level documents (null site_id).
    Sponsor/DM/Sysadmin users with global access can see all documents.
    """
    is_site_scoped = len(principal.assigned_sites) > 0

    if is_site_scoped:
        if not doc.site_id or doc.site_id not in principal.assigned_sites:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Access is restricted to documents at your assigned site(s).",
            )


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    """
    return {"status": "ok", "service": "etmf"}


@app.post("/events/publish", status_code=201)
@app.post("/api/v1/etmf/ingest", status_code=201)
async def ingest_document(
    request: Request,
    payload: IngestionRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> Dict[str, Any]:
    """
    Listen to and ingest system publication events or manual document archives.
    Automatically assigns DIA TMF Zone and Section taxonomy, and indexes the content.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Only write-privileged roles can ingest documents (No Inspectors)
    if not has_permission(principal, "etmf_document:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Enforce manage_expiration permission if any expiration metadata is provided
    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=403,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # Extract change justification reason_for_change from request/state/principal
    reason_for_change = request.headers.get("X-Change-Reason", "").strip()
    if not reason_for_change:
        reason_for_change = getattr(request.state, "change_reason", "").strip()
    if not reason_for_change:
        reason_for_change = principal.change_reason or "system_operation"
    reason_for_change = reason_for_change.strip()

    try:
        doc = await ingest_tmf_document(
            session=session,
            study_id=payload.study_id,
            site_id=payload.site_id,
            artifact_type=payload.artifact_type,
            filename=payload.filename,
            content=payload.content,
            mime_type=payload.mime_type,
            created_by=user_id,
            created_role=user_roles,
            assigned_sites=principal.assigned_sites,
            zone=payload.zone,
            section=payload.section,
            artifact_code=payload.artifact_code,
            taxonomy_version=payload.taxonomy_version,
            metadata_json=payload.metadata_json,
            reason_for_change=reason_for_change,
            protocol_version=payload.protocol_version,
            issue_date=payload.issue_date,
            expiration_date=payload.expiration_date,
            document_owner_id=payload.document_owner_id,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=422,
            detail=str(e),
        )
    except PermissionError as e:
        raise HTTPException(
            status_code=403,
            detail=str(e),
        )
    return {
        "status": "success",
        "id": doc.id,
        "document_id": doc.id,
        "zone": doc.zone,
        "section": doc.section,
        "version_index": doc.version_index,
        "taxonomy_version": doc.taxonomy_version,
        "artifact_code": doc.artifact_code,
        "document_status": doc.status,
    }


@app.get("/api/v1/etmf/documents", response_model=List[DocumentResponse])
async def list_documents(
    request: Request,
    study_id: Optional[str] = Query(None, description="Filter by study ID"),
    zone: Optional[int] = Query(None, description="Filter by TMF Zone"),
    search: Optional[str] = Query(None, description="Search document content"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[DocumentResponse]:
    """
    Retrieve and search indexed, searchable eTMF document records.
    All views are logged to the immutable audit ledger.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    stmt = select(TMFDocument)
    if study_id:
        stmt = stmt.where(TMFDocument.study_id == study_id)
    if zone:
        stmt = stmt.where(TMFDocument.zone == zone)
    if search:
        # Simple SQLite/Postgres text search indexing
        stmt = stmt.where(TMFDocument.content.contains(search))

    # Enforce site visibility and study-level semantics
    is_site_scoped = len(principal.assigned_sites) > 0

    if is_site_scoped:
        if principal.assigned_sites:
            stmt = stmt.where(TMFDocument.site_id.in_(principal.assigned_sites))
        else:
            stmt = stmt.where(TMFDocument.site_id == "NONE_ASSIGNED")

    result = await session.execute(stmt)
    docs = result.scalars().all()

    # Log action to immutable audit trail
    search_criteria = f"study_id={study_id}, zone={zone}, search={search}"
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="LIST",
        document_id=None,
        details=f"Listed eTMF documents matching criteria: {search_criteria}.",
    )

    return [to_document_response(doc) for doc in docs]


@app.get("/api/v1/etmf/documents/{document_id}", response_model=DocumentResponse)
async def view_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    """
    View metadata for a specific eTMF document.
    All views are logged to the immutable audit ledger.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # Enforce raw-original authorization controls
    if not doc.is_redacted:
        stmt_redacted = select(TMFDocument).where(
            TMFDocument.redaction_source_id == doc.id
        )
        res_redacted = await session.execute(stmt_redacted)
        if res_redacted.scalars().first() is not None:
            if not has_permission(principal, "etmf_document:read_raw"):
                raise HTTPException(
                    status_code=403,
                    detail="Forbidden: Raw-original retrieval is restricted to privileged roles.",
                )

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed metadata for eTMF document '{doc.filename}' (ID: {doc.id}).",
    )

    return to_document_response(doc)


@app.get(
    "/api/v1/etmf/documents/{document_id}/versions",
    response_model=DocumentVersionsResponse,
)
async def get_document_versions(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> DocumentVersionsResponse:
    """
    Retrieve all versions/revisions of a document's lineage and their QC transition histories.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # Query the full lineage (all documents of same study and artifact code) sorted by version_index asc
    stmt_lineage = (
        select(TMFDocument)
        .where(
            TMFDocument.study_id == doc.study_id,
            TMFDocument.artifact_code == doc.artifact_code,
        )
        .order_by(TMFDocument.version_index.asc())
    )
    res_lineage = await session.execute(stmt_lineage)
    versions_docs = res_lineage.scalars().all()

    versions_list = []
    for v in versions_docs:
        # For each version, fetch its QC transitions ordered chronologically by timestamp
        stmt_transitions = (
            select(DocumentQCTransition)
            .where(DocumentQCTransition.document_id == v.id)
            .order_by(DocumentQCTransition.timestamp.asc())
        )
        res_trans = await session.execute(stmt_transitions)
        transitions = res_trans.scalars().all()

        versions_list.append(
            DocumentVersionEntry(
                id=v.id,
                version_index=v.version_index,
                status=v.status,
                approval_status=v.approval_status,
                created_at=v.created_at.isoformat(),
                created_by=v.created_by,
                filename=v.filename,
                artifact_code=v.artifact_code,
                signer=v.signer,
                signing_timestamp=(
                    v.signing_timestamp.isoformat() if v.signing_timestamp else None
                ),
                transitions=[
                    TransitionResponse(
                        id=t.id,
                        document_id=t.document_id,
                        from_status=t.from_status,
                        to_status=t.to_status,
                        actor_id=t.actor_id,
                        actor_role=t.actor_role,
                        reason_for_change=t.reason_for_change,
                        timestamp=t.timestamp.isoformat(),
                    )
                    for t in transitions
                ],
            )
        )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="VERSION_HISTORY_VIEW",
        document_id=doc.id,
        details=f"Viewed version history and QC transitions for document lineage (study: {doc.study_id}, artifact: {doc.artifact_code}).",
    )
    await session.commit()

    return DocumentVersionsResponse(
        study_id=doc.study_id,
        artifact_code=doc.artifact_code,
        versions=versions_list,
    )


@app.get("/api/v1/etmf/documents/{document_id}/download")
async def download_document(
    request: Request,
    document_id: str,
    watermark: bool = Query(False, description="Request watermarked document"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> Response:
    """
    Download/stream indexed content for a specific eTMF document.
    All downloads are logged to the immutable audit ledger.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    is_auditor = "auditor" in principal.roles or any(
        r in {"auditor", "inspector", "regulatory_inspector"}
        for r in principal.raw_roles
    )

    if watermark and not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    should_watermark = watermark or is_auditor

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # Enforce raw-original authorization controls
    if not doc.is_redacted:
        stmt_redacted = select(TMFDocument).where(
            TMFDocument.redaction_source_id == doc.id
        )
        res_redacted = await session.execute(stmt_redacted)
        if res_redacted.scalars().first() is not None:
            if not has_permission(principal, "etmf_document:read_raw"):
                raise HTTPException(
                    status_code=403,
                    detail="Forbidden: Raw-original retrieval is restricted to privileged roles.",
                )

    if should_watermark:
        from apps.etmf.watermark import apply_watermark

        final_content = apply_watermark(doc.content, doc.mime_type, user_id, user_roles)
        action_name = "WATERMARKED_DOWNLOAD"
        details_msg = f"Downloaded watermarked content for eTMF document '{doc.filename}' (ID: {doc.id})."
    else:
        final_content = doc.content
        action_name = "DOWNLOAD"
        details_msg = (
            f"Downloaded content for eTMF document '{doc.filename}' (ID: {doc.id})."
        )

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action=action_name,
        document_id=doc.id,
        details=details_msg,
    )

    return Response(
        content=final_content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@app.get("/api/v1/etmf/documents/{document_id}/watermark")
async def download_watermarked_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> Response:
    """
    Dedicated watermarked view/download path for external auditors.
    Access is strictly auditor-role-gated.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    is_auditor = "auditor" in principal.roles or any(
        r in {"auditor", "inspector", "regulatory_inspector"}
        for r in principal.raw_roles
    )

    if not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # Enforce raw-original authorization controls
    if not doc.is_redacted:
        stmt_redacted = select(TMFDocument).where(
            TMFDocument.redaction_source_id == doc.id
        )
        res_redacted = await session.execute(stmt_redacted)
        if res_redacted.scalars().first() is not None:
            if not has_permission(principal, "etmf_document:read_raw"):
                raise HTTPException(
                    status_code=403,
                    detail="Forbidden: Raw-original retrieval is restricted to privileged roles.",
                )

    from apps.etmf.watermark import apply_watermark

    watermarked_content = apply_watermark(
        doc.content, doc.mime_type, user_id, user_roles
    )

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="WATERMARKED_DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded watermarked content for eTMF document '{doc.filename}' (ID: {doc.id}).",
    )

    return Response(
        content=watermarked_content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@app.get("/api/v1/etmf/audit-logs", response_model=PaginatedAuditLogResponse)
async def get_audit_trail(
    request: Request,
    user_id: Optional[str] = Query(None, description="Filter logs by user ID"),
    action: Optional[str] = Query(None, description="Filter logs by action"),
    document_id: Optional[str] = Query(None, description="Filter logs by document ID"),
    start_time: Optional[datetime] = Query(
        None, description="Filter logs starting from this timestamp (inclusive)"
    ),
    end_time: Optional[datetime] = Query(
        None, description="Filter logs up to this timestamp (inclusive)"
    ),
    limit: int = Query(
        50, ge=1, le=250, description="Limit the number of audit log records returned"
    ),
    offset: int = Query(0, ge=0, description="Offset for pagination"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> PaginatedAuditLogResponse:
    """
    Retrieve audit trail of all eTMF interactions.
    Restricted to authorized roles like regulatory inspectors.
    """
    request_user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_audit_logs:read"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    # Log access to the audit trail itself
    await write_audit_log(
        session=session,
        user_id=request_user_id,
        user_role=user_roles,
        action="AUDIT_VIEW",
        document_id=document_id,
        details="Accessed eTMF immutable audit trail logs.",
    )

    # 1. Build base filter criteria
    filters = []
    if user_id:
        filters.append(TMFAuditLog.user_id == user_id)
    if action:
        filters.append(TMFAuditLog.action == action)
    if document_id:
        filters.append(TMFAuditLog.document_id == document_id)
    if start_time:
        filters.append(TMFAuditLog.timestamp >= start_time)
    if end_time:
        filters.append(TMFAuditLog.timestamp <= end_time)

    # 2. Query total count
    from sqlalchemy import func

    count_stmt = select(func.count()).select_from(TMFAuditLog)
    if filters:
        count_stmt = count_stmt.where(*filters)

    total_res = await session.execute(count_stmt)
    total_count = total_res.scalar_one()

    # 3. Query items with pagination and descending timestamp order
    stmt = select(TMFAuditLog)
    if filters:
        stmt = stmt.where(*filters)
    # Order descending by timestamp, and secondary ID for determinism
    stmt = stmt.order_by(TMFAuditLog.timestamp.desc(), TMFAuditLog.id.desc())
    stmt = stmt.offset(offset).limit(limit)

    result = await session.execute(stmt)
    logs = result.scalars().all()

    # 4. Construct metadata
    has_more = (offset + limit) < total_count
    next_page = None
    next_cursor = None
    if has_more:
        next_cursor = str(offset + limit)
        # Construct next_page URL with existing filters
        base_path = "/api/v1/etmf/audit-logs"
        params = []
        if user_id:
            params.append(f"user_id={user_id}")
        if action:
            params.append(f"action={action}")
        if document_id:
            params.append(f"document_id={document_id}")
        if start_time:
            params.append(f"start_time={start_time.isoformat()}")
        if end_time:
            params.append(f"end_time={end_time.isoformat()}")
        params.append(f"limit={limit}")
        params.append(f"offset={offset + limit}")
        next_page = f"{base_path}?" + "&".join(params)

    items = [
        AuditLogResponse(
            id=log.id,
            timestamp=log.timestamp.isoformat(),
            user_id=log.user_id,
            user_role=log.user_role,
            action=log.action,
            document_id=log.document_id,
            details=log.details,
        )
        for log in logs
    ]

    return PaginatedAuditLogResponse(
        items=items,
        total_count=total_count,
        limit=limit,
        offset=offset,
        next_page=next_page,
        next_cursor=next_cursor,
        has_more=has_more,
    )


@app.get("/api/v1/etmf/edl", response_model=List[ExpectedDocumentResponse])
async def list_expectations(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    site_id: Optional[str] = Query(None, description="Optional clinical site ID"),
    milestone: Optional[str] = Query(None, description="Optional milestone"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[ExpectedDocumentResponse]:
    """
    List expected documents for a study, optionally filtered by site and milestone.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
    if site_id:
        stmt = stmt.where(ExpectedDocument.site_id == site_id)
    if milestone:
        stmt = stmt.where(ExpectedDocument.milestone == normalize_milestone(milestone))

    result = await session.execute(stmt)
    expectations = result.scalars().all()

    # Log action
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_VIEW",
        document_id=None,
        details=f"Listed EDL expectations for study '{study_id}', site '{site_id}', milestone '{milestone}'.",
    )

    return [
        ExpectedDocumentResponse(
            id=exp.id,
            study_id=exp.study_id,
            site_id=exp.site_id,
            milestone=exp.milestone,
            artifact_type=exp.artifact_type,
            zone=exp.zone,
            section=exp.section,
            metadata_json=exp.metadata_json,
            created_at=exp.created_at.isoformat(),
            created_by=exp.created_by,
            reason_for_change=exp.reason_for_change,
            version_index=exp.version_index,
        )
        for exp in expectations
    ]


@app.post("/api/v1/etmf/edl", response_model=ExpectedDocumentResponse, status_code=201)
async def create_expectation(
    request: Request,
    payload: ExpectedDocumentCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> ExpectedDocumentResponse:
    """
    Create a new Expected Document List (EDL) expectation.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_edl:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    milestone_normalized = normalize_milestone(payload.milestone)

    exp = ExpectedDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        milestone=milestone_normalized,
        artifact_type=payload.artifact_type,
        zone=payload.zone,
        section=payload.section,
        metadata_json=payload.metadata_json,
        created_by=user_id,
        reason_for_change=payload.reason_for_change,
        version_index=1,
    )

    session.add(exp)
    await session.flush()

    # Log action
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_UPDATE",
        document_id=exp.id,
        details=f"Created expected document '{payload.artifact_type}' for study '{payload.study_id}', site '{payload.site_id}', milestone '{milestone_normalized}'. Reason: {payload.reason_for_change}",
    )

    return ExpectedDocumentResponse(
        id=exp.id,
        study_id=exp.study_id,
        site_id=exp.site_id,
        milestone=exp.milestone,
        artifact_type=exp.artifact_type,
        zone=exp.zone,
        section=exp.section,
        metadata_json=exp.metadata_json,
        created_at=exp.created_at.isoformat(),
        created_by=exp.created_by,
        reason_for_change=exp.reason_for_change,
        version_index=exp.version_index,
    )


@app.put("/api/v1/etmf/edl/{edl_id}", response_model=ExpectedDocumentResponse)
async def update_expectation(
    request: Request,
    edl_id: str,
    payload: ExpectedDocumentCreate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> ExpectedDocumentResponse:
    """
    Update an existing Expected Document List (EDL) expectation.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    if not has_permission(principal, "etmf_edl:create"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    stmt = select(ExpectedDocument).where(ExpectedDocument.id == edl_id)
    result = await session.execute(stmt)
    exp = result.scalars().first()

    if not exp:
        raise HTTPException(
            status_code=404, detail="ExpectedDocument expectation not found"
        )

    milestone_normalized = normalize_milestone(payload.milestone)

    exp.study_id = payload.study_id
    exp.site_id = payload.site_id
    exp.milestone = milestone_normalized
    exp.artifact_type = payload.artifact_type
    exp.zone = payload.zone
    exp.section = payload.section
    exp.metadata_json = payload.metadata_json
    exp.reason_for_change = payload.reason_for_change
    exp.version_index += 1

    await session.flush()

    # Log action
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="EDL_UPDATE",
        document_id=exp.id,
        details=f"Updated expected document '{payload.artifact_type}' (ID: {edl_id}) for study '{payload.study_id}', site '{payload.site_id}', milestone '{milestone_normalized}'. Reason: {payload.reason_for_change}",
    )

    return ExpectedDocumentResponse(
        id=exp.id,
        study_id=exp.study_id,
        site_id=exp.site_id,
        milestone=exp.milestone,
        artifact_type=exp.artifact_type,
        zone=exp.zone,
        section=exp.section,
        metadata_json=exp.metadata_json,
        created_at=exp.created_at.isoformat(),
        created_by=exp.created_by,
        reason_for_change=exp.reason_for_change,
        version_index=exp.version_index,
    )


@app.get("/api/v1/etmf/completeness", response_model=CompletenessResponse)
async def check_completeness(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    milestone: str = Query(..., description="The transition milestone to check"),
    site_id: Optional[str] = Query(None, description="Optional clinical site ID"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> CompletenessResponse:
    """
    Completeness checking dashboard to verify mandatory artifacts
    before study milestone transitions.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Enforce site isolation on completeness checking for site-scoped users
    is_site_scoped = len(principal.assigned_sites) > 0

    if is_site_scoped:
        if not site_id or site_id not in principal.assigned_sites:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: You can only check completeness for your assigned site(s).",
            )

    milestone_normalized = normalize_milestone(milestone)

    # Validate milestone with catalog first. If unknown, raise 400 immediately.
    version = get_active_catalog().version
    try:
        get_mandatory_artifacts(milestone_normalized, version)
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT. Error: {str(e)}",
        )

    # Idempotent dynamic seeding of default study-scope EDL if none exist yet for the study
    await seed_default_edl(session, study_id, milestone_normalized)

    # Query expected documents for this study, milestone and site_id (if provided)
    stmt = select(ExpectedDocument).where(
        ExpectedDocument.study_id == study_id,
        ExpectedDocument.milestone == milestone_normalized,
    )
    if site_id:
        stmt = stmt.where(
            (ExpectedDocument.site_id.is_(None)) | (ExpectedDocument.site_id == site_id)
        )
    else:
        stmt = stmt.where(ExpectedDocument.site_id.is_(None))

    result = await session.execute(stmt)
    expected_docs = result.scalars().all()

    # Query all archived documents for this study
    stmt_docs = select(TMFDocument).where(TMFDocument.study_id == study_id)
    result_docs = await session.execute(stmt_docs)
    archived_docs = result_docs.scalars().all()

    present_artifacts = []
    missing_artifacts = []
    per_artifact_detail = []

    for exp in expected_docs:
        # Resolve expectation artifact to its canonical details to match by canonical artifact identity
        try:
            resolved_exp = resolve_artifact(version, name=exp.artifact_type)
            exp_code = resolved_exp["artifact"].code
            canonical_name = resolved_exp["artifact"].name
        except ValueError:
            # Fallback to current artifact_type & None code if not found in catalog
            exp_code = None
            canonical_name = exp.artifact_type

        matched_doc = None
        for arch in archived_docs:
            is_match = False
            if exp_code and arch.artifact_code:
                # Direct comparison by canonical artifact identity
                is_match = arch.artifact_code == exp_code
            else:
                # Fallback to case-insensitive name matching
                is_match = canonical_name.lower() in arch.artifact_type.lower()

            if is_match:
                if not matched_doc or arch.version_index > matched_doc.version_index:
                    matched_doc = arch

        from apps.etmf.cryptography import requires_signature

        sig_required = requires_signature(canonical_name)

        scope = "site" if exp.site_id else "study"
        if matched_doc:
            is_signed = matched_doc.approval_status == "APPROVED"

            if sig_required:
                if is_signed:
                    status_val = "SIGNED"
                    if canonical_name not in present_artifacts:
                        present_artifacts.append(canonical_name)
                else:
                    status_val = "UNSIGNED"
                    if canonical_name not in missing_artifacts:
                        missing_artifacts.append(canonical_name)
            else:
                status_val = "PRESENT"
                if canonical_name not in present_artifacts:
                    present_artifacts.append(canonical_name)

            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=canonical_name,
                    scope=scope,
                    status=status_val,
                    document_id=matched_doc.id,
                    version_index=matched_doc.version_index,
                )
            )
        else:
            status_val = "ABSENT"
            if canonical_name not in missing_artifacts:
                missing_artifacts.append(canonical_name)
            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=canonical_name,
                    scope=scope,
                    status=status_val,
                    document_id=None,
                    version_index=None,
                )
            )

    is_complete = len(missing_artifacts) == 0
    scope_repr = "site" if site_id else "study"

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="COMPLETENESS",
        document_id=None,
        details=f"Performed completeness checking for study '{study_id}', site '{site_id}', milestone '{milestone_normalized}'. Complete: {is_complete}.",
    )

    return CompletenessResponse(
        study_id=study_id,
        site_id=site_id,
        milestone=milestone_normalized,
        is_complete=is_complete,
        scope=scope_repr,
        present_artifacts=present_artifacts,
        missing_artifacts=missing_artifacts,
        per_artifact_detail=per_artifact_detail,
    )


@app.post(
    "/api/v1/etmf/documents/{document_id}/redact",
    response_model=DocumentResponse,
    status_code=201,
)
async def redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: RedactRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    """
    Perform controlled redaction on an existing unredacted eTMF document, producing a new
    redacted document version linked to the source.
    All redactions are logged to the immutable audit trail and block auditor/inspector personas.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Only write-privileged roles can redact documents (No Inspectors/Auditors)
    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # 1. Fetch the source document
    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    source_doc = result.scalars().first()
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(source_doc, principal)

    # Check if already signed
    if (
        source_doc.status == "SIGNED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    # 2. Extract X-Change-Reason
    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        # Check if stored in request state
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    # 3. Determine new version index (highest version_index for this study + artifact_code)
    stmt_v = (
        select(TMFDocument.version_index)
        .where(TMFDocument.study_id == source_doc.study_id)
        .where(TMFDocument.artifact_code == source_doc.artifact_code)
    )
    res_v = await session.execute(stmt_v)
    versions = res_v.scalars().all()
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    # 4. Copy and prepare metadata
    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    # Build redacted document version
    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}",
        content=payload.redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=payload.manifest,
    )

    session.add(redacted_doc)
    await session.flush()

    # 5. Log action to immutable audit trail (REDACT action)
    # A REDACT audit entry records actor, role, operation type, document/version references, and manifest signature.
    manifest_signature = payload.manifest.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return to_document_response(redacted_doc)


@app.post(
    "/api/v1/etmf/documents/{document_id}/auto-redact",
    response_model=AutomatedRedactResponse,
    status_code=201,
)
async def auto_redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: AutomatedRedactRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> AutomatedRedactResponse:
    """
    Perform controlled automated redaction on an existing unredacted eTMF document, producing a new
    redacted document version linked to the source.
    All redactions are logged to the immutable audit trail and block auditor/inspector personas.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Only write-privileged roles can redact documents (No Inspectors/Auditors)
    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # 1. Fetch the source document
    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    source_doc = result.scalars().first()
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(source_doc, principal)

    # Check if already signed
    if (
        source_doc.status == "SIGNED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to auto-redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    # 2. Extract X-Change-Reason
    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        # Check if stored in request state
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    # 3. Use DeidDetector to detect PII/PHI candidates
    detector = DeidDetector()
    try:
        results = detector.detect(
            source_doc.content,
            profile=payload.profile,
            custom_terms=payload.custom_terms,
        )
    except Exception as e:
        raise HTTPException(status_code=422, detail=f"Detection failed: {str(e)}")

    # 4. Apply transforms
    try:
        redacted_content, record = apply_deid_transforms(
            source_doc.content,
            results,
            strategies=payload.strategies,
            default_strategy="mask",
        )
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Redaction transforms failed: {str(e)}"
        )

    # 5. Determine new version index
    stmt_v = (
        select(TMFDocument.version_index)
        .where(TMFDocument.study_id == source_doc.study_id)
        .where(TMFDocument.artifact_code == source_doc.artifact_code)
    )
    res_v = await session.execute(stmt_v)
    versions = res_v.scalars().all()
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    # 6. Build and symmetrically sign the redaction manifest
    try:
        manifest = build_redaction_manifest(
            redaction_record=record,
            operator_identity=user_id,
            reason=change_reason,
            source_version="v" + str(source_doc.version_index),
            target_version="v" + str(new_version_index),
        )
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    # Sign with HMAC symmetric key
    secret_key = os.getenv(
        "REDACTION_SIGNING_SECRET", "internal-gateway-secret-12345"
    ).encode("utf-8")
    signed_manifest = sign_manifest_symmetric(manifest, secret_key)
    manifest_data = signed_manifest.model_dump()

    # 7. Prepare and save the new redacted version
    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    # Build filename
    redacted_filename = (
        payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}"
    )

    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=redacted_filename,
        content=redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=manifest_data,
    )

    session.add(redacted_doc)
    await session.flush()

    # 8. Log action to immutable audit trail (REDACT action)
    manifest_signature = manifest_data.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return AutomatedRedactResponse(
        status="success",
        document_id=redacted_doc.id,
        version_index=redacted_doc.version_index,
        filename=redacted_doc.filename,
        categories_counts=manifest_data.get("categories_counts", {}),
        manifest=manifest_data,
    )


@app.post(
    "/api/v1/etmf/documents/{document_id}/manual-redact",
    response_model=ManualRedactResponse,
    status_code=201,
)
async def manual_redact_document_endpoint(
    request: Request,
    document_id: str,
    payload: ManualRedactRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> ManualRedactResponse:
    """
    Perform controlled manual redaction on an existing unredacted eTMF document using specified character spans and literal terms.
    Produces a new redacted document version linked to the source.
    All redactions are logged to the immutable audit trail and block auditor/inspector personas.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Only write-privileged roles can redact documents (No Inspectors/Auditors)
    if not has_permission(principal, "etmf_document:redact"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # 1. Fetch the source document
    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    source_doc = result.scalars().first()
    if not source_doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(source_doc, principal)

    # Check if already signed
    if (
        source_doc.status == "SIGNED"
        or source_doc.approval_status == "APPROVED"
        or source_doc.signature_manifestation is not None
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=source_doc.id,
            details=f"Rejected attempt to manually redact signed document '{source_doc.filename}' (ID: {source_doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    # 2. Extract X-Change-Reason
    change_reason = request.headers.get("X-Change-Reason", "").strip()
    if not change_reason:
        # Check if stored in request state
        change_reason = principal.change_reason or "system_operation".strip()
    if not change_reason:
        raise HTTPException(
            status_code=400,
            detail="Missing change justification reason under X-Change-Reason",
        )

    content_len = len(source_doc.content)
    results = []

    # 3. Validate and process explicit character spans
    if payload.spans:
        sorted_spans = sorted(payload.spans, key=lambda s: s.start)
        seen = []
        for span in sorted_spans:
            if span.start < 0 or span.end > content_len or span.start >= span.end:
                raise HTTPException(
                    status_code=422,
                    detail=f"Invalid span offsets: [{span.start}, {span.end}] is invalid or out of range for document of length {content_len}.",
                )
            # Check overlap with previous span in sorted order
            for prev in seen:
                if max(span.start, prev.start) < min(span.end, prev.end):
                    raise HTTPException(
                        status_code=422,
                        detail=f"Overlapping or conflicting span inputs detected: [{span.start}, {span.end}] conflicts with [{prev.start}, {prev.end}].",
                    )
            seen.append(span)

            span_value = source_doc.content[span.start : span.end]
            results.append(
                DetectionResult(
                    category=span.label or "manual",
                    start=span.start,
                    end=span.end,
                    value=span_value,
                )
            )

    # 4. Safe literal term matching following shared safe escaping/match policy
    if payload.terms:
        import re

        valid_terms = [t for t in payload.terms if t and t.strip()]
        if valid_terms:
            # Sort descending to match longer strings first
            valid_terms.sort(key=len, reverse=True)
            escaped_terms = [re.escape(term) for term in valid_terms]
            patterns = []
            for term in escaped_terms:
                start_b = r"\b" if re.match(r"^\w", term) else ""
                end_b = r"\b" if re.search(r"\w$", term) else ""
                patterns.append(f"{start_b}{term}{end_b}")

            custom_regex = re.compile("|".join(patterns), re.IGNORECASE)
            for m in custom_regex.finditer(source_doc.content):
                results.append(
                    DetectionResult(
                        category=DetectorCategory.CUSTOM,
                        start=m.start(),
                        end=m.end(),
                        value=m.group(),
                    )
                )

    # 5. Apply deid transforms to sanitize content
    try:
        redacted_content, record = apply_deid_transforms(
            source_doc.content,
            results,
            default_strategy="mask",
        )
    except Exception as e:
        raise HTTPException(
            status_code=422, detail=f"Redaction transforms failed: {str(e)}"
        )

    # 6. Determine new version index
    stmt_v = (
        select(TMFDocument.version_index)
        .where(TMFDocument.study_id == source_doc.study_id)
        .where(TMFDocument.artifact_code == source_doc.artifact_code)
    )
    res_v = await session.execute(stmt_v)
    versions = res_v.scalars().all()
    new_version_index = max(versions) + 1 if versions else source_doc.version_index + 1

    # 7. Build and symmetrically sign the redaction manifest
    try:
        manifest = build_redaction_manifest(
            redaction_record=record,
            operator_identity=user_id,
            reason=change_reason,
            source_version="v" + str(source_doc.version_index),
            target_version="v" + str(new_version_index),
        )
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))

    # Sign with HMAC symmetric key
    secret_key = os.getenv(
        "REDACTION_SIGNING_SECRET", "internal-gateway-secret-12345"
    ).encode("utf-8")
    signed_manifest = sign_manifest_symmetric(manifest, secret_key)
    manifest_data = signed_manifest.model_dump()

    # 8. Prepare and save the new redacted version
    metadata_json = dict(source_doc.metadata_json) if source_doc.metadata_json else {}
    metadata_json["change_reason"] = change_reason
    metadata_json["is_redacted"] = True

    redacted_filename = (
        payload.redacted_filename
        or f"{os.path.splitext(source_doc.filename)[0]}_redacted{os.path.splitext(source_doc.filename)[1]}"
    )

    redacted_doc = TMFDocument(
        study_id=source_doc.study_id,
        site_id=source_doc.site_id,
        zone=source_doc.zone,
        section=source_doc.section,
        artifact_type=source_doc.artifact_type,
        filename=redacted_filename,
        content=redacted_content,
        mime_type=source_doc.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=source_doc.taxonomy_version,
        artifact_code=source_doc.artifact_code,
        metadata_json=metadata_json,
        document_type=source_doc.document_type,
        approval_status=source_doc.approval_status,
        signature_manifestation=source_doc.signature_manifestation,
        signer=source_doc.signer,
        signing_timestamp=source_doc.signing_timestamp,
        is_redacted=True,
        redaction_source_id=source_doc.id,
        redaction_manifest_json=manifest_data,
    )

    session.add(redacted_doc)
    await session.flush()

    # 9. Log action to immutable audit trail (REDACT action)
    manifest_signature = manifest_data.get("signature", "unsigned")
    details_str = (
        f"REDACT action executed. Actor: {user_id}, Role: {user_roles}. "
        f"Source Document Reference ID: {source_doc.id} (Version {source_doc.version_index}). "
        f"Redacted Document Reference ID: {redacted_doc.id} (Version {redacted_doc.version_index}). "
        f"Manifest Signature: {manifest_signature}."
    )
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="REDACT",
        document_id=redacted_doc.id,
        details=details_str,
    )

    return ManualRedactResponse(
        status="success",
        document_id=redacted_doc.id,
        version_index=redacted_doc.version_index,
        filename=redacted_doc.filename,
        categories_counts=manifest_data.get("categories_counts", {}),
        manifest=manifest_data,
    )


@app.get("/api/v1/etmf/test-exception")
async def test_exception_route(session: AsyncSession = Depends(get_db_session)):
    """
    Test-only endpoint to trigger a database session exception and rollback.
    """
    raise RuntimeError("Intentional test database rollback error")


@app.post(
    "/api/v1/etmf/documents/{document_id}/transition", response_model=Dict[str, Any]
)
async def transition_document_status_endpoint(
    request: Request,
    document_id: str,
    payload: TransitionRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> Dict[str, Any]:
    """
    Perform a secure, 21 CFR Part 11 compliant Quality Control (QC) status transition on an eTMF document.
    Enforces role-based access gates and logs an append-only state transition history record.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # Keep signing semantics out of manual QC transition input
    valid_qc_statuses = {
        DocumentStatus.DRAFT.value,
        DocumentStatus.TECHNICAL_QC.value,
        DocumentStatus.CLINICAL_QC.value,
        DocumentStatus.APPROVED.value,
        DocumentStatus.ARCHIVED.value,
        DocumentStatus.REJECTED.value,
    }
    if payload.to_status not in valid_qc_statuses:
        raise HTTPException(
            status_code=422,
            detail=f"Invalid status: '{payload.to_status}'. Must be one of {sorted(list(valid_qc_statuses))}.",
        )

    # Check if already signed
    if (
        doc.status == "SIGNED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to transition status of signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    try:
        await validate_and_transition_document_status(
            session=session,
            document=doc,
            to_status=payload.to_status,
            actor_id=user_id,
            actor_role=user_roles,
            reason_for_change=payload.reason_for_change,
        )
    except ValueError as e:
        raise HTTPException(status_code=422, detail=str(e))
    except PermissionError as e:
        raise HTTPException(status_code=403, detail=str(e))

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="QC_TRANSITION",
        document_id=doc.id,
        details=f"Document '{doc.filename}' (ID: {doc.id}) transitioned to status '{payload.to_status}'.",
    )

    return {
        "status": "success",
        "document_id": doc.id,
        "new_status": doc.status,
    }


@app.put(
    "/api/v1/etmf/documents/{document_id}/expiration",
    response_model=DocumentResponse,
)
async def update_document_expiration_endpoint(
    request: Request,
    document_id: str,
    payload: DocumentExpirationUpdate,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    """
    Update expiration-related metadata for an eTMF document.
    Enforces the etmf_document:manage_expiration permission and checks trial locks.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # 1. Fetch document by ID
    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # 2. Check permission
    if not has_permission(principal, "etmf_document:manage_expiration"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
        )

    # 3. Verify trial lock status
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # 4. Check if already signed
    if (
        doc.status == "SIGNED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to update expiration of signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    # 5. Mutate the fields and increment version index
    doc.issue_date = payload.issue_date
    doc.expiration_date = payload.expiration_date
    doc.document_owner_id = payload.document_owner_id
    doc.version_index += 1

    # 6. Flush session
    await session.flush()

    # 7. Write audit log
    details = f"Updated expiration metadata for document '{doc.filename}' (ID: {doc.id}): issue_date={payload.issue_date}, expiration_date={payload.expiration_date}, owner={payload.document_owner_id}."
    reason_for_change = request.headers.get("X-Change-Reason", "").strip()
    if not reason_for_change:
        reason_for_change = getattr(request.state, "change_reason", "").strip()
    if not reason_for_change:
        reason_for_change = principal.change_reason or "System operation"
    reason_for_change = reason_for_change.strip()

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="UPDATE_EXPIRATION",
        document_id=doc.id,
        details=details + f" Reason: {reason_for_change}",
    )

    return to_document_response(doc)


@app.post(
    "/api/v1/etmf/documents/{document_id}/sign-off",
    response_model=DocumentResponse,
    status_code=200,
)
@app.post(
    "/api/v1/etmf/documents/{document_id}/approve",
    response_model=DocumentResponse,
    status_code=200,
)
async def sign_document_endpoint(
    request: Request,
    document_id: str,
    payload: SignDocumentRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> DocumentResponse:
    """
    Approve and cryptographically sign an eTMF document, producing a 21 CFR Part 11 compliant
    persisted signature manifestation, recording immutable audit actions (SIGN & APPROVE),
    and transitioning the record to SIGNED.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Enforce write roles can sign (no read-only roles like auditor, inspector)
    if not has_permission(principal, "etmf_document:sign"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # 1. Fetch document
    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc, principal)

    # 2. Check if already signed/approved
    if (
        doc.status == "SIGNED"
        or doc.approval_status == "APPROVED"
        or doc.signature_manifestation is not None
    ):
        # Already signed. Reject with IMMUTABILITY_VIOLATION and write rejected audit log!
        await write_audit_log(
            session=session,
            user_id=user_id,
            user_role=user_roles,
            action="MUTATION_REJECTED",
            document_id=doc.id,
            details=f"Rejected attempt to sign already signed document '{doc.filename}' (ID: {doc.id}). Error: IMMUTABILITY_VIOLATION.",
        )
        await session.commit()
        raise HTTPException(
            status_code=403,
            detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
        )

    # 3. Build SignatureManifestation
    import hashlib
    from datetime import datetime, timedelta, timezone

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID
    from signature import SignatureManifestation

    from packages.security.signing import (
        asymmetric_sign,
        capture_certificate_identifiers,
    )

    client_ip = getattr(request.state, "ip_address", None)
    if not client_ip:
        client_ip = request.headers.get("x-forwarded-for") or (
            request.client.host if request.client else "127.0.0.1"
        )
        if "," in client_ip:
            client_ip = client_ip.split(",")[0].strip()

    user_agent = request.headers.get("user-agent") or "eTMF Service"
    now_utc = datetime.now(timezone.utc)
    doc_hash = hashlib.sha256(doc.content.encode("utf-8")).hexdigest()

    manifest = SignatureManifestation(
        signer_id=user_id,
        timestamp=now_utc,
        signing_reason=payload.signing_reason,
        ip_address=client_ip,
        user_agent=user_agent,
        sha256_hash=doc_hash,
    )

    # 4. Generate transient X.509 RSA certificate and key for certificate-signing
    private_key = rsa.generate_private_key(
        public_exponent=65537,
        key_size=2048,
    )

    subject = issuer = x509.Name(
        [
            x509.NameAttribute(NameOID.COUNTRY_NAME, "US"),
            x509.NameAttribute(NameOID.STATE_OR_PROVINCE_NAME, "California"),
            x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Cadence Clinical"),
            x509.NameAttribute(NameOID.COMMON_NAME, f"user-{user_id}"),
        ]
    )

    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(private_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now_utc - timedelta(days=1))
        .not_valid_after(now_utc + timedelta(days=10))
        .add_extension(
            x509.SubjectKeyIdentifier.from_public_key(private_key.public_key()),
            critical=False,
        )
        .sign(private_key, hashes.SHA256())
    )

    private_key_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.TraditionalOpenSSL,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")

    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode("utf-8")

    # Sign manifestation canonical bytes
    canonical_bytes = manifest.get_canonical_bytes()
    sig_b64 = asymmetric_sign(canonical_bytes, private_key_pem)
    ids = capture_certificate_identifiers(cert_pem)

    manifest.signature = sig_b64
    manifest.certificate_pem = cert_pem
    manifest.key_identifier = ids["subject_key_identifier"]

    # Verify signature
    assert manifest.verify() is True

    # 5. Mutate the document record
    doc.status = "SIGNED"
    doc.approval_status = "APPROVED"
    doc.signature_manifestation = manifest.model_dump(mode="json")
    doc.signer = user_id
    doc.signing_timestamp = now_utc.replace(tzinfo=None)

    # 6. Add immutable SIGN and APPROVE eTMF audit actions
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="SIGN",
        document_id=doc.id,
        details=f"Successfully signed document '{doc.filename}' (ID: {doc.id}) with reason '{payload.signing_reason.value}'.",
    )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="APPROVE",
        document_id=doc.id,
        details=f"Successfully approved document '{doc.filename}' (ID: {doc.id}) with reason '{payload.signing_reason.value}'.",
    )

    await session.flush()

    return to_document_response(doc)


@app.get(
    "/api/v1/etmf/studies/{study_id}/artifacts/{artifact_type}/history",
    response_model=List[DocumentResponse],
)
async def get_artifact_history(
    study_id: str,
    artifact_type: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[DocumentResponse]:
    """
    Retrieve the chronological, ordered version history of a specific artifact type within a study.
    All views are logged to the immutable audit trail.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Resolve active taxonomy/catalog to obtain the canonical artifact type if possible
    from tmf_reference_model import get_active_catalog, resolve_artifact

    version = get_active_catalog().version
    canonical_name = artifact_type
    try:
        resolved = resolve_artifact(version=version, name=artifact_type)
        canonical_name = resolved["artifact"].name
    except ValueError:
        pass

    stmt = select(TMFDocument).where(
        TMFDocument.study_id == study_id,
        (TMFDocument.artifact_type == canonical_name)
        | (TMFDocument.artifact_type == artifact_type),
    )

    # Order chronologically by version_index ascending
    stmt = stmt.order_by(TMFDocument.version_index.asc())

    # Enforce site visibility and study-level semantics
    is_site_scoped = len(principal.assigned_sites) > 0
    if is_site_scoped:
        stmt = stmt.where(TMFDocument.site_id.in_(principal.assigned_sites))

    result = await session.execute(stmt)
    docs = result.scalars().all()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="HISTORY_VIEW",
        document_id=None,
        details=f"Viewed artifact history for study '{study_id}', artifact_type '{artifact_type}'.",
    )

    return [to_document_response(doc) for doc in docs]


@app.get(
    "/api/v1/etmf/documents/{document_id}/transitions",
    response_model=List[TransitionResponse],
)
async def get_document_transition_history(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[TransitionResponse]:
    """
    Retrieve the append-only Quality Control (QC) transition history for a specific eTMF document.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Verify document exists
    stmt_exist = select(TMFDocument).where(TMFDocument.id == document_id)
    res_exist = await session.execute(stmt_exist)
    doc_obj = res_exist.scalars().first()
    if not doc_obj:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc_obj, principal)

    stmt = (
        select(DocumentQCTransition)
        .where(DocumentQCTransition.document_id == document_id)
        .order_by(DocumentQCTransition.timestamp.asc())
    )
    result = await session.execute(stmt)
    transitions = result.scalars().all()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="QC_HISTORY_VIEW",
        document_id=document_id,
        details=f"Viewed QC transition history for document ID: {document_id}.",
    )

    return [
        TransitionResponse(
            id=t.id,
            document_id=t.document_id,
            from_status=t.from_status,
            to_status=t.to_status,
            actor_id=t.actor_id,
            actor_role=t.actor_role,
            reason_for_change=t.reason_for_change,
            timestamp=t.timestamp.isoformat(),
        )
        for t in transitions
    ]


def parse_recipient_address(recipient: str) -> tuple[str, Optional[str]]:
    """
    Parses recipient field and extracts study_id and optional binder-hint.
    Convention: study-<STUDY_ID>[+<binder-hint>]@<domain>
    """
    _, address = email.utils.parseaddr(recipient)
    if not address or "@" not in address:
        raise ValueError("Invalid recipient address format")
    local_part = address.split("@")[0]
    if not local_part.startswith("study-"):
        raise ValueError("Recipient address local part must start with 'study-'")

    parts = local_part[len("study-") :].split("+", 1)
    study_id = parts[0].strip()
    if not study_id:
        raise ValueError("Study ID cannot be empty or whitespace")

    binder_hint = parts[1].strip() if len(parts) > 1 else None
    if binder_hint == "":
        binder_hint = None
    return study_id, binder_hint


def resolve_binder_hint(binder_hint: Optional[str]) -> tuple[int, str, str, str]:
    """
    Resolves the optional binder hint to canonical zone/section/artifact_code/artifact_type.
    If no hint is provided, defaults to 'Site Communication Log' ('05.04.01').
    """
    if not binder_hint:
        return 5, "04", "05.04.01", "Site Communication Log"

    version = get_active_catalog().version
    cleaned_hint = binder_hint.strip()
    is_code = cleaned_hint.replace(".", "").isdigit()

    if cleaned_hint.lower() in ("conduct", "initiation", "closeout", "milestone"):
        cleaned_hint = "Site Communication Log"

    try:
        if is_code:
            res = resolve_artifact(version, code=cleaned_hint)
        else:
            if (
                cleaned_hint.upper() == "FORM_1572"
                or cleaned_hint.lower() == "form 1572"
            ):
                cleaned_hint = "FDA Form 1572"
            elif (
                cleaned_hint.upper() == "FINANCIAL_DISCLOSURE"
                or cleaned_hint.lower() == "financial disclosure"
            ):
                cleaned_hint = "Financial Disclosure"
            elif (
                cleaned_hint.upper() == "PROTOCOL_SIGNOFF"
                or cleaned_hint.lower() == "protocol signoff"
            ):
                cleaned_hint = "Protocol Sign-off"
            res = resolve_artifact(version, name=cleaned_hint)
    except Exception as e:
        raise ValueError(f"Unresolvable binder hint: {str(e)}")

    return (
        res["zone"].code,
        res["section"].code,
        res["artifact"].code,
        res["artifact"].name,
    )


@app.post("/api/v1/etmf/inbound-email", status_code=201)
async def inbound_email_webhook(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> Dict[str, Any]:
    """
    Inbound-email webhook that validates provider requests, resolves a target study/binder location,
    and routes message content and attachments into the shared eTMF ingestion service.
    """
    content_length_str = request.headers.get("content-length")
    max_size = int(os.getenv("INBOUND_EMAIL_MAX_SIZE_BYTES", str(10 * 1024 * 1024)))
    if content_length_str:
        try:
            content_length = int(content_length_str)
            if content_length > max_size:
                raise HTTPException(status_code=413, detail="Payload too large")
        except ValueError:
            raise HTTPException(status_code=400, detail="Invalid Content-Length")

    try:
        form_data = await request.form()
    except Exception:
        raise HTTPException(status_code=400, detail="Invalid form data")

    sender = form_data.get("sender")
    recipient = form_data.get("recipient")
    subject = form_data.get("subject")
    body_plain = form_data.get("body-plain") or form_data.get("body_plain") or ""
    timestamp = form_data.get("timestamp")
    token = form_data.get("token")
    signature = form_data.get("signature")
    message_id = (
        form_data.get("Message-Id")
        or form_data.get("message-id")
        or form_data.get("Message-ID")
    )

    sender = str(sender) if sender is not None else ""
    recipient = str(recipient) if recipient is not None else ""
    subject = str(subject) if subject is not None else ""
    body_plain = str(body_plain)
    timestamp = str(timestamp) if timestamp is not None else ""
    token = str(token) if token is not None else ""
    signature = str(signature) if signature is not None else ""
    message_id = str(message_id) if message_id is not None else ""

    from packages.security.signing import verify_inbound_email_signature

    if not verify_inbound_email_signature(timestamp, token, signature, message_id):
        raise HTTPException(status_code=401, detail="Unauthorized")

    try:
        study_id, binder_hint = parse_recipient_address(recipient)
        zone, section, artifact_code, artifact_type = resolve_binder_hint(binder_hint)
    except Exception:
        raise HTTPException(status_code=422, detail="Invalid routing metadata")

    if message_id:
        stmt = select(TMFDocument).where(
            TMFDocument.metadata_json["message_id"].as_string() == message_id
        )
        res = await session.execute(stmt)
        if res.scalars().first():
            return {"status": "accepted"}

    try:
        attachments = []
        for key, value in form_data.multi_items():
            if hasattr(value, "filename") and value.filename:
                attachments.append(value)

        body_filename = f"email_body_{message_id or int(time.time())}.txt"

        metadata_json = {
            "message_id": message_id,
            "sender": sender,
            "recipient": recipient,
            "subject": subject,
            "ingested_via": "inbound-email",
        }

        audit_details = (
            f"Inbound email webhook ingestion. Sender: {sender}, Subject: '{subject}', "
            f"Message-Id: {message_id}."
        )

        async with session.begin_nested():
            await ingest_tmf_document(
                session=session,
                study_id=study_id,
                artifact_type=artifact_type,
                filename=body_filename,
                content=body_plain
                or f"Subject: {subject}\nFrom: {sender}\n(Empty Body)",
                mime_type="text/plain",
                created_by="system",
                created_role="system",
                zone=zone,
                section=section,
                artifact_code=artifact_code,
                metadata_json=metadata_json,
                audit_action="EMAIL_INGEST",
                audit_details=audit_details,
                reason_for_change="inbound_email_webhook",
            )

            for idx, att in enumerate(attachments):
                att_bytes = await att.read()
                if len(att_bytes) > max_size:
                    raise HTTPException(status_code=413, detail="Attachment too large")

                att_content = att_bytes.decode("utf-8", errors="ignore")
                att_filename = att.filename or f"attachment_{idx}"
                att_metadata = dict(metadata_json)
                att_metadata["attachment_index"] = idx
                att_metadata["original_filename"] = att_filename

                await ingest_tmf_document(
                    session=session,
                    study_id=study_id,
                    artifact_type=artifact_type,
                    filename=att_filename,
                    content=att_content,
                    mime_type=att.content_type or "application/octet-stream",
                    created_by="system",
                    created_role="system",
                    zone=zone,
                    section=section,
                    artifact_code=artifact_code,
                    metadata_json=att_metadata,
                    audit_action="EMAIL_INGEST",
                    audit_details=f"Ingested attachment '{att_filename}' from email Message-Id {message_id}.",
                    reason_for_change="inbound_email_webhook",
                )

        await session.commit()
        return {"status": "accepted"}

    except Exception as e:
        if isinstance(e, PermissionError):
            if "IMMUTABILITY_VIOLATION" in str(e):
                raise HTTPException(
                    status_code=403,
                    detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
                )
            raise HTTPException(status_code=403, detail="Forbidden")
        if isinstance(e, HTTPException):
            raise e
        raise HTTPException(status_code=500, detail="Internal processing failure")


def build_binder_structure(
    catalog,
    archived_docs: List[TMFDocument],
    expected_codes: set[str],
    site_id: Optional[str] = None,
    is_site_scoped: bool = False,
    principal: Optional[Principal] = None,
) -> tuple[List[BinderZoneNode], List[str], List[str]]:
    """
    Build the nested structure models.
    """
    highest_docs = {}  # artifact_code -> TMFDocument
    for doc in archived_docs:
        if not doc.artifact_code:
            continue

        if is_site_scoped and principal:
            if not doc.site_id or doc.site_id not in principal.assigned_sites:
                continue
        elif site_id:
            if doc.site_id != site_id:
                continue
        else:
            if doc.site_id is not None:
                continue

        existing = highest_docs.get(doc.artifact_code)
        if not existing or doc.version_index > existing.version_index:
            highest_docs[doc.artifact_code] = doc

    zones_list = []
    present_artifacts = []
    missing_artifacts = []

    for z in catalog.zones:
        zone = catalog.get_zone(z.code)
        if not zone:
            continue
        sections_list = []
        for s in zone.sections:
            section = catalog.get_section(s.code)
            if not section:
                continue
            artifacts_list = []
            for artifact in section.artifacts:
                doc = highest_docs.get(artifact.code)
                if doc:
                    status = "PRESENT"
                    doc_id = doc.id
                    v_idx = doc.version_index
                    if artifact.name not in present_artifacts:
                        present_artifacts.append(artifact.name)
                else:
                    doc_id = None
                    v_idx = None
                    if artifact.code in expected_codes:
                        status = "MISSING"
                        if artifact.name not in missing_artifacts:
                            missing_artifacts.append(artifact.name)
                    else:
                        status = "EXPECTED"

                artifacts_list.append(
                    BinderArtifactNode(
                        artifact_code=artifact.code,
                        artifact_name=artifact.name,
                        status=status,
                        document_id=doc_id,
                        version_index=v_idx,
                    )
                )
            sections_list.append(
                BinderSectionNode(
                    section_code=section.code,
                    section_name=section.name,
                    artifacts=artifacts_list,
                )
            )
        zones_list.append(
            BinderZoneNode(
                zone_code=zone.code,
                zone_name=zone.name,
                sections=sections_list,
            )
        )

    return zones_list, present_artifacts, missing_artifacts


@app.get(
    "/api/v1/etmf/studies/{study_id}/binder/structure",
    response_model=BinderStructureResponse,
)
async def get_binder_structure(
    study_id: str,
    milestone: Optional[str] = Query(
        None, description="Optional clinical study milestone"
    ),
    site_id: Optional[str] = Query(None, description="Optional clinical site ID"),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> BinderStructureResponse:
    """
    Expose the structured Zone -> Section -> Artifact tree for a study binder,
    annotated with expected/present/missing status.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    is_site_scoped = len(principal.assigned_sites) > 0

    if is_site_scoped:
        if not site_id or site_id not in principal.assigned_sites:
            raise HTTPException(
                status_code=403,
                detail="Forbidden: You can only view binder structure for your assigned site(s).",
            )

    version = get_active_catalog().version

    milestone_normalized = None
    if milestone:
        milestone_normalized = normalize_milestone(milestone)
        try:
            get_mandatory_artifacts(milestone_normalized, version)
        except ValueError as e:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT. Error: {str(e)}",
            )
        await seed_default_edl(session, study_id, milestone_normalized)

    stmt = select(ExpectedDocument).where(ExpectedDocument.study_id == study_id)
    if milestone_normalized:
        stmt = stmt.where(ExpectedDocument.milestone == milestone_normalized)
    if site_id:
        stmt = stmt.where(
            (ExpectedDocument.site_id.is_(None)) | (ExpectedDocument.site_id == site_id)
        )
    else:
        stmt = stmt.where(ExpectedDocument.site_id.is_(None))

    result = await session.execute(stmt)
    expected_docs = result.scalars().all()

    stmt_docs = select(TMFDocument).where(TMFDocument.study_id == study_id)
    result_docs = await session.execute(stmt_docs)
    archived_docs = result_docs.scalars().all()

    expected_codes = set()
    for exp in expected_docs:
        try:
            resolved_exp = resolve_artifact(version, name=exp.artifact_type)
            expected_codes.add(resolved_exp["artifact"].code)
        except ValueError:
            pass

    catalog = get_active_catalog()
    zones_list, present_artifacts, missing_artifacts = build_binder_structure(
        catalog=catalog,
        archived_docs=archived_docs,
        expected_codes=expected_codes,
        site_id=site_id,
        is_site_scoped=is_site_scoped,
        principal=principal,
    )

    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="BINDER_STRUCTURE_VIEW",
        document_id=None,
        details=f"Viewed binder structure for study '{study_id}', site '{site_id}', milestone '{milestone_normalized}'.",
    )
    await session.commit()

    return BinderStructureResponse(
        study_id=study_id,
        milestone=milestone_normalized,
        site_id=site_id,
        zones=zones_list,
        present_artifacts=present_artifacts,
        missing_artifacts=missing_artifacts,
    )


@app.get("/api/v1/etmf/studies/{study_id}/binder")
async def export_regulatory_binder(
    study_id: str,
    include_history: bool = Query(
        False, description="Include full version history of documents"
    ),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> Response:
    """
    Generate an inspection-ready ZIP binder for an eTMF study.
    Restricted to authorized auditor roles.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Restrict access to authorized auditor roles
    is_auditor = "auditor" in principal.roles or any(
        r in {"auditor", "inspector", "regulatory_inspector"}
        for r in principal.raw_roles
    )

    if not is_auditor:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )

    # Log binder export action
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="BINDER_EXPORT",
        document_id=None,
        details=f"Exported regulatory binder for study '{study_id}' (include_history={include_history}).",
    )
    await session.commit()

    # Generate the ZIP binder content
    zip_bytes = await generate_binder_zip(
        session=session,
        study_id=study_id,
        include_history=include_history,
        requester_id=user_id,
        requester_role=user_roles,
        principal=principal,
    )

    filename = f"study_{study_id}_binder.zip"
    return Response(
        content=zip_bytes,
        media_type="application/zip",
        headers={"Content-Disposition": f"attachment; filename={filename}"},
    )


@app.post(
    "/api/v1/etmf/studies/{study_id}/archive",
    response_model=StudyArchiveResponse,
    status_code=200,
)
async def bulk_archive_study_documents(
    request: Request,
    study_id: str,
    payload: StudyArchiveRequest,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> StudyArchiveResponse:
    """
    Perform authorized bulk study-level document archival transitioning eligible eTMF documents to
    the terminal ARCHIVED status under 21 CFR Part 11 requirements.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Require transition_archived permission
    if not has_permission(principal, "etmf_document:transition_archived"):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Caller lacks the required etmf_document:transition_archived permission.",
        )

    # Fetch all documents under the specified study
    stmt = select(TMFDocument).where(TMFDocument.study_id == study_id)
    result = await session.execute(stmt)
    documents = result.scalars().all()

    if not documents:
        # Repeating an already-completed archive request (or an empty study) is safe and observable
        return StudyArchiveResponse(
            status="success",
            study_id=study_id,
            total_processed=0,
            successful_count=0,
            failed_count=0,
            skipped_count=0,
            results=[],
        )

    results: List[StudyArchiveItemResult] = []
    successful_count = 0
    failed_count = 0
    skipped_count = 0

    # Execute inside a nested transaction (savepoint) to allow rollback on failure
    async with session.begin_nested() as nested_tx:
        failed = False
        first_error = None
        for doc in documents:
            from_status = doc.status or "DRAFT"

            # Skip if already ARCHIVED
            if from_status == "ARCHIVED":
                skipped_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="skipped",
                    )
                )
                continue

            try:
                # Transition using the state machine which saves a DocumentQCTransition
                await validate_and_transition_document_status(
                    session=session,
                    document=doc,
                    to_status="ARCHIVED",
                    actor_id=user_id,
                    actor_role=user_roles,
                    reason_for_change=payload.reason_for_change,
                )
                successful_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="success",
                    )
                )
            except Exception as e:
                failed_count += 1
                results.append(
                    StudyArchiveItemResult(
                        document_id=doc.id,
                        filename=doc.filename,
                        from_status=from_status,
                        to_status="ARCHIVED",
                        status="failed",
                        error_message=str(e),
                    )
                )
                if payload.all_or_nothing:
                    failed = True
                    first_error = str(e)
                    break

        if failed and payload.all_or_nothing:
            await nested_tx.rollback()
            raise HTTPException(
                status_code=400,
                detail=f"All-or-nothing validation failure: Archival aborted because document transition failed. Error: {first_error}",
            )

    # Log overall study-level archival results to audit trail
    overall_status = "success"
    if failed_count > 0:
        if successful_count > 0:
            overall_status = "partial_success"
        else:
            overall_status = "failed"

    details_msg = (
        f"Bulk study archive completed for study '{study_id}'. "
        f"Status: {overall_status}. Successful: {successful_count}, Failed: {failed_count}, Skipped: {skipped_count}."
    )
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="STUDY_ARCHIVE",
        document_id=None,
        details=details_msg,
    )

    return StudyArchiveResponse(
        status=overall_status,
        study_id=study_id,
        total_processed=len(documents),
        successful_count=successful_count,
        failed_count=failed_count,
        skipped_count=skipped_count,
        results=results,
    )


@app.get(
    "/api/v1/etmf/documents/{document_id}/qc-history",
    response_model=List[TransitionResponse],
)
async def get_document_qc_history(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
) -> List[TransitionResponse]:
    """
    Retrieve the append-only Quality Control (QC) review history for a specific eTMF document.
    """
    user_id = principal.user_id
    user_roles = ",".join(principal.raw_roles)

    # Verify document exists
    stmt_exist = select(TMFDocument).where(TMFDocument.id == document_id)
    res_exist = await session.execute(stmt_exist)
    doc_obj = res_exist.scalars().first()
    if not doc_obj:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Enforce site visibility and study-level semantics
    enforce_document_site_visibility(doc_obj, principal)

    stmt = (
        select(DocumentQCTransition)
        .where(DocumentQCTransition.document_id == document_id)
        .order_by(DocumentQCTransition.timestamp.asc())
    )
    result = await session.execute(stmt)
    transitions = result.scalars().all()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="QC_HISTORY_VIEW",
        document_id=document_id,
        details=f"Viewed QC transition history for document ID: {document_id}.",
    )

    return [
        TransitionResponse(
            id=t.id,
            document_id=t.document_id,
            from_status=t.from_status,
            to_status=t.to_status,
            actor_id=t.actor_id,
            actor_role=t.actor_role,
            reason_for_change=t.reason_for_change,
            timestamp=t.timestamp.isoformat(),
        )
        for t in transitions
    ]
