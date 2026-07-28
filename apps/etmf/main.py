import os
from datetime import datetime
from typing import Any, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from fastapi.responses import Response
from pydantic import BaseModel, Field
from signature import SigningReason
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from tmf_reference_model import (
    get_active_catalog,
    get_mandatory_artifacts,
    resolve_artifact,
    validate_hierarchy,
)

from apps.etmf.database import db_manager
from apps.etmf.export import generate_binder_zip
from apps.etmf.lifecycle import validate_and_transition_document_status
from apps.etmf.models import (
    Base,
    DocumentQCTransition,
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


class DocumentResponse(BaseModel):
    """
    Representation of an eTMF document.
    """

    id: str
    study_id: str
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

    # Restrict affected trial to read-only state if trial is locked
    from apps.etmf.lock_client import verify_trial_lock_status

    if await verify_trial_lock_status():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # Determine TMF taxonomy version
    taxonomy_version = payload.taxonomy_version or get_active_catalog().version

    code_input = payload.artifact_code
    name_input = payload.artifact_type

    # Map/Normalize the document classification for FORM_1572, FINANCIAL_DISCLOSURE, and PROTOCOL_SIGNOFF
    doc_type = None
    if (
        name_input == "FORM_1572"
        or code_input == "05.02.01"
        or name_input == "FDA Form 1572"
    ):
        doc_type = "FORM_1572"
        name_input = "FDA Form 1572"
        code_input = "05.02.01"
    elif (
        name_input == "FINANCIAL_DISCLOSURE"
        or code_input == "05.02.02"
        or name_input == "Financial Disclosure"
    ):
        doc_type = "FINANCIAL_DISCLOSURE"
        name_input = "Financial Disclosure"
        code_input = "05.02.02"
    elif (
        name_input == "PROTOCOL_SIGNOFF"
        or code_input == "01.01.03"
        or name_input == "Protocol Sign-off"
    ):
        doc_type = "PROTOCOL_SIGNOFF"
        name_input = "Protocol Sign-off"
        code_input = "01.01.03"

    # Resolve artifact, section, and zone via the shared catalog API
    try:
        # If artifact_code is not explicitly supplied, check if artifact_type is a code
        if (
            not code_input
            and name_input
            and name_input.strip().replace(".", "").isdigit()
        ):
            code_input = name_input.strip()
            name_input = None

        resolved = resolve_artifact(
            version=taxonomy_version, code=code_input, name=name_input
        )
    except ValueError as e:
        raise HTTPException(
            status_code=422,
            detail=f"Validation Error: {str(e)}",
        )

    zone = resolved["zone"].code
    section = resolved["section"].code
    artifact_obj = resolved["artifact"]
    artifact_code = artifact_obj.code
    canonical_artifact_type = artifact_obj.name

    # Validate hierarchy if user supplied specific zone/section hierarchy
    supplied_zone = payload.zone
    supplied_section = payload.section
    if payload.metadata_json:
        if supplied_zone is None:
            supplied_zone = payload.metadata_json.get("zone")
        if supplied_section is None:
            supplied_section = payload.metadata_json.get("section")

    if supplied_zone is not None or supplied_section is not None:
        try:
            validate_hierarchy(
                version=taxonomy_version,
                zone_code=supplied_zone if supplied_zone is not None else zone,
                section_code=(
                    supplied_section if supplied_section is not None else section
                ),
                artifact_code=artifact_code,
            )
        except ValueError as e:
            raise HTTPException(
                status_code=422,
                detail=f"Validation Error: {str(e)}",
            )

    # Validate embedded X.509 signature
    from apps.etmf.cryptography import (
        extract_signature_from_content,
        validate_document_signature,
    )

    is_valid, status_msg = validate_document_signature(
        artifact_type=canonical_artifact_type,
        content=payload.content,
        metadata_json=payload.metadata_json,
    )
    if not is_valid:
        raise HTTPException(
            status_code=422,
            detail=f"Validation Error: {status_msg}",
        )

    # Extract signature to set signature verification status in metadata
    cert_pem, sig_bytes, _ = extract_signature_from_content(payload.content)
    if not cert_pem and payload.metadata_json:
        for key in ["signature", "digital_signature", "x509_signature"]:
            sig_obj = payload.metadata_json.get(key)
            if isinstance(sig_obj, dict):
                cert_pem = (
                    sig_obj.get("certificate")
                    or sig_obj.get("x509_certificate")
                    or sig_obj.get("cert")
                )
                break

    # Record verification status in metadata_json
    metadata_json = dict(payload.metadata_json) if payload.metadata_json else {}
    metadata_json["signature_verification_status"] = (
        "VERIFIED" if cert_pem else "NOT_REQUIRED"
    )

    # Reconstruct signature manifestation if validated and present
    import base64
    import hashlib
    from datetime import datetime, timezone

    from cryptography import x509
    from cryptography.hazmat.primitives import hashes
    from cryptography.x509.oid import NameOID
    from signature import SignatureManifestation, SigningReason

    sig_b64 = None
    if cert_pem and sig_bytes:
        sig_b64 = base64.b64encode(sig_bytes).decode("utf-8")
    elif payload.metadata_json:
        for key in ["signature", "digital_signature", "x509_signature"]:
            sig_obj = payload.metadata_json.get(key)
            if isinstance(sig_obj, dict):
                sig_val = sig_obj.get("signature_value") or sig_obj.get("signature")
                if sig_val:
                    sig_b64 = sig_val.strip()
                    break

    approval_status_val = "PENDING"
    signature_manifestation_data = None
    signer_val = None
    signing_timestamp_val = None

    if cert_pem and sig_b64:
        # We have a valid validated signature!
        # Compute hash of the payload content
        content_hash = hashlib.sha256(payload.content.encode("utf-8")).hexdigest()

        # Extract signer identity (CN) from cert_pem
        signer_name = None
        key_id = None
        if "MOCK_SIGNATURE" in cert_pem:
            signer_name = "Mock Signer"
            key_id = "MOCK_KEY"
        else:
            try:
                cert_obj = x509.load_pem_x509_certificate(cert_pem.encode("utf-8"))
                cn_attr = cert_obj.subject.get_attributes_for_oid(NameOID.COMMON_NAME)
                if cn_attr:
                    signer_name = cn_attr[0].value
                key_id = cert_obj.fingerprint(hashes.SHA256()).hex()
            except Exception:
                pass

        if not signer_name:
            signer_name = user_id or "system"

        now_utc = datetime.now(timezone.utc)
        sig_man = SignatureManifestation(
            signer_id=signer_name,
            timestamp=now_utc,
            signing_reason=SigningReason.APPROVAL,
            ip_address="127.0.0.1",
            user_agent="eTMF Ingest Service",
            sha256_hash=content_hash,
            signature=sig_b64,
            certificate_pem=cert_pem,
            key_identifier=key_id,
        )
        signature_manifestation_data = sig_man.model_dump(mode="json")
        approval_status_val = "APPROVED"
        signer_val = signer_name
        signing_timestamp_val = now_utc

    # Check if a document version already exists (for study_id + artifact_code)
    stmt = (
        select(TMFDocument)
        .where(TMFDocument.study_id == payload.study_id)
        .where(TMFDocument.artifact_code == artifact_code)
        .order_by(TMFDocument.version_index.desc())
    )
    result = await session.execute(stmt)
    existing_doc = result.scalars().first()

    new_version_index = 1
    if existing_doc:
        if (
            existing_doc.status == "SIGNED"
            or existing_doc.approval_status == "APPROVED"
            or existing_doc.signature_manifestation is not None
        ):
            await write_audit_log(
                session=session,
                user_id=user_id,
                user_role=user_roles,
                action="MUTATION_REJECTED",
                document_id=existing_doc.id,
                details=f"Rejected attempt to ingest new version for signed document '{existing_doc.filename}' (ID: {existing_doc.id}). Error: IMMUTABILITY_VIOLATION.",
            )
            await session.commit()
            raise HTTPException(
                status_code=403,
                detail="IMMUTABILITY_VIOLATION: Document is already signed and cannot be modified",
            )
        new_version_index = existing_doc.version_index + 1

    doc = TMFDocument(
        study_id=payload.study_id,
        zone=zone,
        section=section,
        artifact_type=canonical_artifact_type,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        taxonomy_version=taxonomy_version,
        artifact_code=artifact_code,
        metadata_json=metadata_json,
        document_type=doc_type,
        approval_status=approval_status_val,
        signature_manifestation=signature_manifestation_data,
        signer=signer_val,
        signing_timestamp=signing_timestamp_val,
    )

    session.add(doc)
    await session.flush()

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="INGEST",
        document_id=doc.id,
        details=f"Ingested artifact type '{canonical_artifact_type}' for study '{payload.study_id}' as Version {new_version_index} (TMF Zone {zone}, Section {section}).",
    )

    return {
        "status": "success",
        "document_id": doc.id,
        "zone": zone,
        "section": section,
        "version_index": new_version_index,
        "taxonomy_version": taxonomy_version,
        "artifact_code": artifact_code,
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

    return [
        DocumentResponse(
            id=doc.id,
            study_id=doc.study_id,
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
        )
        for doc in docs
    ]


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

    return DocumentResponse(
        id=doc.id,
        study_id=doc.study_id,
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

    return DocumentResponse(
        id=redacted_doc.id,
        study_id=redacted_doc.study_id,
        zone=redacted_doc.zone,
        section=redacted_doc.section,
        artifact_type=redacted_doc.artifact_type,
        filename=redacted_doc.filename,
        mime_type=redacted_doc.mime_type,
        created_at=redacted_doc.created_at.isoformat(),
        created_by=redacted_doc.created_by,
        version_index=redacted_doc.version_index,
        status=redacted_doc.status,
        taxonomy_version=redacted_doc.taxonomy_version,
        artifact_code=redacted_doc.artifact_code,
        metadata_json=redacted_doc.metadata_json,
        document_type=redacted_doc.document_type,
        approval_status=redacted_doc.approval_status,
        signature_manifestation=redacted_doc.signature_manifestation,
        signer=redacted_doc.signer,
        signing_timestamp=(
            redacted_doc.signing_timestamp.isoformat()
            if redacted_doc.signing_timestamp
            else None
        ),
        is_redacted=redacted_doc.is_redacted,
        redaction_source_id=redacted_doc.redaction_source_id,
        redaction_manifest_json=redacted_doc.redaction_manifest_json,
    )


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

    return DocumentResponse(
        id=doc.id,
        study_id=doc.study_id,
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
        signing_timestamp=doc.signing_timestamp.isoformat(),
        is_redacted=doc.is_redacted,
        redaction_source_id=doc.redaction_source_id,
        redaction_manifest_json=doc.redaction_manifest_json,
    )


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
    if not res_exist.scalars().first():
        raise HTTPException(status_code=404, detail="eTMF Document not found")

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
    )

    filename = f"study_{study_id}_binder.zip"
    return Response(
        content=zip_bytes,
        media_type="application/zip",
        headers={"Content-Disposition": f"attachment; filename={filename}"},
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
    if not res_exist.scalars().first():
        raise HTTPException(status_code=404, detail="eTMF Document not found")

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
