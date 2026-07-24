import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from fastapi.responses import Response
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.database import db_manager
from apps.etmf.lifecycle import validate_and_transition_document_status
from apps.etmf.models import (
    Base,
    DocumentQCTransition,
    ExpectedDocument,
    TMFAuditLog,
    TMFDocument,
)
from packages.security.middleware import GatewayAuthMiddleware

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
    if canonical not in ("INITIATION", "CONDUCT", "CLOSEOUT"):
        return

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

    # Map milestone to mandatory artifacts
    if canonical == "INITIATION":
        artifacts = [("Approved Protocol", 1, "1.1 Protocol")]
    elif canonical == "CONDUCT":
        artifacts = [
            ("Approved Protocol", 1, "1.1 Protocol"),
            ("Define-XML", 10, "10.1 Data Management Specifications"),
            ("Blank CRF", 10, "10.2 Case Report Forms"),
        ]
    else:  # CLOSEOUT
        artifacts = [
            ("Approved Protocol", 1, "1.1 Protocol"),
            ("Define-XML", 10, "10.1 Data Management Specifications"),
            ("Blank CRF", 10, "10.2 Case Report Forms"),
            ("Data Lock Certificate", 11, "11.1 Statistical Analysis"),
        ]

    for art, zone, sec in artifacts:
        doc = ExpectedDocument(
            study_id=study_id,
            milestone=canonical,
            artifact_type=art,
            zone=zone,
            section=sec,
            created_by="system",
            reason_for_change="System-initiated default seeding of expected documents list",
            version_index=1,
            metadata_json={"default_seeded": True},
        )
        session.add(doc)
    await session.flush()


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Handle the lifespan events for the eTMF application.

    Initializes the database session manager on startup and securely
    cleans up connections on shutdown. Creates all tables if sqlite in-memory is used.
    """
    db_manager.init_db(DATABASE_URL)

    # Automatically create tables for sqlite in-memory/file databases
    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    # Idempotently seed default EDL configurations
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

    from apps.etmf.sealer import (
        start_background_etmf_sealer,
        stop_background_etmf_sealer,
    )

    await start_background_etmf_sealer(db_manager.get_session_maker())

    yield

    await stop_background_etmf_sealer()

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - Event-Driven eTMF Module",
    version="0.1.0",
    lifespan=lifespan,
)

# Enforce secure gateway authentication middleware
app.add_middleware(GatewayAuthMiddleware)


# Dependable to obtain database session
async def get_db_session() -> AsyncGenerator[AsyncSession, None]:
    """
    Dependency to yield an asynchronous database session.
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise


# Helper to map standard artifact types to DIA TMF Zones
def map_artifact_to_tmf(artifact_type: str) -> tuple[int, str]:
    """
    Maps standard clinical artifacts to DIA TMF Reference Model Zones and Sections.

    Args:
        artifact_type (str): The name of the artifact.

    Returns:
        tuple[int, str]: A tuple of (Zone, Section Description).
    """
    norm = artifact_type.strip().lower()
    if "protocol" in norm:
        return 1, "1.1 Protocol"
    elif "define" in norm:
        return 10, "10.1 Data Management Specifications"
    elif "crf" in norm:
        return 10, "10.2 Case Report Forms"
    elif "lock" in norm:
        return 11, "11.1 Statistical Analysis"
    else:
        # Default fallback to central files (Zone 2)
        return 2, "2.1 Study Files"


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
    metadata_json: Optional[Dict[str, Any]] = None


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
    user_role: str,
    action: str,
    document_id: Optional[str],
    details: str,
) -> None:
    """
    Utility function to write to the immutable eTMF audit ledger.
    """
    log_entry = TMFAuditLog(
        user_id=user_id,
        user_role=user_role,
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
) -> Dict[str, Any]:
    """
    Listen to and ingest system publication events or manual document archives.
    Automatically assigns DIA TMF Zone and Section taxonomy, and indexes the content.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")

    # Only write-privileged roles can ingest documents (No Inspectors)
    roles_list = [r.strip().lower() for r in user_roles.split(",")]
    if "inspector" in roles_list or "regulatory_inspector" in roles_list:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    # Restrict affected trial to read-only state if trial is locked
    from apps.execution.trial_lock import TrialLockManager

    if TrialLockManager.is_locked():
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Trial is currently locked in a read-only state due to a security violation.",
        )

    # Validate embedded X.509 signature
    from apps.etmf.cryptography import (
        extract_signature_from_content,
        validate_document_signature,
    )

    is_valid, status_msg = validate_document_signature(
        artifact_type=payload.artifact_type,
        content=payload.content,
        metadata_json=payload.metadata_json,
    )
    if not is_valid:
        raise HTTPException(
            status_code=422,
            detail=f"Validation Error: {status_msg}",
        )

    # Extract signature to set signature verification status in metadata
    cert_pem, _, _ = extract_signature_from_content(payload.content)
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

    # Determine TMF Zone and Section
    zone, section = map_artifact_to_tmf(payload.artifact_type)

    # Check if a document version already exists (for study_id + artifact_type)
    stmt = (
        select(TMFDocument)
        .where(TMFDocument.study_id == payload.study_id)
        .where(TMFDocument.artifact_type == payload.artifact_type)
        .order_by(TMFDocument.version_index.desc())
    )
    result = await session.execute(stmt)
    existing_doc = result.scalars().first()

    new_version_index = 1
    if existing_doc:
        new_version_index = existing_doc.version_index + 1

    doc = TMFDocument(
        study_id=payload.study_id,
        zone=zone,
        section=section,
        artifact_type=payload.artifact_type,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        created_by=user_id,
        version_index=new_version_index,
        metadata_json=metadata_json,
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
        details=f"Ingested artifact type '{payload.artifact_type}' for study '{payload.study_id}' as Version {new_version_index} (TMF Zone {zone}, Section {section}).",
    )

    return {
        "status": "success",
        "document_id": doc.id,
        "zone": zone,
        "section": section,
        "version_index": new_version_index,
    }


@app.get("/api/v1/etmf/documents", response_model=List[DocumentResponse])
async def list_documents(
    request: Request,
    study_id: Optional[str] = Query(None, description="Filter by study ID"),
    zone: Optional[int] = Query(None, description="Filter by TMF Zone"),
    search: Optional[str] = Query(None, description="Search document content"),
    session: AsyncSession = Depends(get_db_session),
) -> List[DocumentResponse]:
    """
    Retrieve and search indexed, searchable eTMF document records.
    All views are logged to the immutable audit ledger.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

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
            metadata_json=doc.metadata_json,
        )
        for doc in docs
    ]


@app.get("/api/v1/etmf/documents/{document_id}", response_model=DocumentResponse)
async def view_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> DocumentResponse:
    """
    View metadata for a specific eTMF document.
    All views are logged to the immutable audit ledger.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

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
        metadata_json=doc.metadata_json,
    )


@app.get("/api/v1/etmf/documents/{document_id}/download")
async def download_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> Response:
    """
    Download/stream indexed content for a specific eTMF document.
    All downloads are logged to the immutable audit ledger.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded content for eTMF document '{doc.filename}' (ID: {doc.id}).",
    )

    return Response(
        content=doc.content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@app.get("/api/v1/etmf/audit-logs", response_model=List[AuditLogResponse])
async def get_audit_trail(
    request: Request,
    document_id: Optional[str] = Query(None, description="Filter logs by document ID"),
    session: AsyncSession = Depends(get_db_session),
) -> List[AuditLogResponse]:
    """
    Retrieve audit trail of all eTMF interactions.
    Restricted to authorized roles like regulatory inspectors.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    # Log access to the audit trail itself first
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="AUDIT_VIEW",
        document_id=document_id,
        details="Accessed eTMF immutable audit trail logs.",
    )

    stmt = select(TMFAuditLog)
    if document_id:
        stmt = stmt.where(TMFAuditLog.document_id == document_id)
    stmt = stmt.order_by(TMFAuditLog.timestamp.desc())

    result = await session.execute(stmt)
    logs = result.scalars().all()

    return [
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


@app.get("/api/v1/etmf/edl", response_model=List[ExpectedDocumentResponse])
async def list_expectations(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    site_id: Optional[str] = Query(None, description="Optional clinical site ID"),
    milestone: Optional[str] = Query(None, description="Optional milestone"),
    session: AsyncSession = Depends(get_db_session),
) -> List[ExpectedDocumentResponse]:
    """
    List expected documents for a study, optionally filtered by site and milestone.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

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
) -> ExpectedDocumentResponse:
    """
    Create a new Expected Document List (EDL) expectation.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")

    roles_list = [r.strip().lower() for r in user_roles.split(",")]
    if "inspector" in roles_list or "regulatory_inspector" in roles_list:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.execution.trial_lock import TrialLockManager

    if TrialLockManager.is_locked():
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
) -> ExpectedDocumentResponse:
    """
    Update an existing Expected Document List (EDL) expectation.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")

    roles_list = [r.strip().lower() for r in user_roles.split(",")]
    if "inspector" in roles_list or "regulatory_inspector" in roles_list:
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Inspectors are restricted to read-only access.",
        )

    from apps.execution.trial_lock import TrialLockManager

    if TrialLockManager.is_locked():
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
) -> CompletenessResponse:
    """
    Completeness checking dashboard to verify mandatory artifacts
    before study milestone transitions.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    milestone_normalized = normalize_milestone(milestone)

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

    # When no EDL is defined for a requested milestone, return a clear error,
    # preserving the current unknown-milestone behavior by relying on the seeded canonical milestone set.
    if not expected_docs:
        raise HTTPException(
            status_code=400,
            detail="Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT",
        )

    # Query all archived documents for this study
    stmt_docs = select(TMFDocument).where(TMFDocument.study_id == study_id)
    result_docs = await session.execute(stmt_docs)
    archived_docs = result_docs.scalars().all()

    present_artifacts = []
    missing_artifacts = []
    per_artifact_detail = []

    for exp in expected_docs:
        matched_doc = None
        for arch in archived_docs:
            if exp.artifact_type.lower() in arch.artifact_type.lower():
                if not matched_doc or arch.version_index > matched_doc.version_index:
                    matched_doc = arch

        scope = "site" if exp.site_id else "study"
        if matched_doc:
            if matched_doc.artifact_type not in present_artifacts:
                present_artifacts.append(matched_doc.artifact_type)
            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=exp.artifact_type,
                    scope=scope,
                    status="PRESENT",
                    document_id=matched_doc.id,
                    version_index=matched_doc.version_index,
                )
            )
        else:
            if exp.artifact_type not in missing_artifacts:
                missing_artifacts.append(exp.artifact_type)
            per_artifact_detail.append(
                ArtifactDetail(
                    artifact_type=exp.artifact_type,
                    scope=scope,
                    status="MISSING",
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
) -> Dict[str, Any]:
    """
    Perform a secure, 21 CFR Part 11 compliant Quality Control (QC) status transition on an eTMF document.
    Enforces role-based access gates and logs an append-only state transition history record.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_roles = getattr(request.state, "roles", "system")

    stmt = select(TMFDocument).where(TMFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()
    if not doc:
        raise HTTPException(status_code=404, detail="eTMF Document not found")

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


@app.get(
    "/api/v1/etmf/documents/{document_id}/transitions",
    response_model=List[TransitionResponse],
)
async def get_document_transition_history(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> List[TransitionResponse]:
    """
    Retrieve the append-only Quality Control (QC) transition history for a specific eTMF document.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

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
