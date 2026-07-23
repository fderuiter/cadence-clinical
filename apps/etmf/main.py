import os
from contextlib import asynccontextmanager
from typing import Any, AsyncGenerator, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request
from fastapi.responses import Response
from pydantic import BaseModel, Field
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.etmf.database import db_manager
from apps.etmf.models import Base, TMFAuditLog, TMFDocument
from packages.security.middleware import GatewayAuthMiddleware

DATABASE_URL = os.getenv("ETMF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


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

    yield

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
    metadata_json: Optional[Dict[str, Any]] = None


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


class CompletenessResponse(BaseModel):
    """
    Completeness dashboard check response.
    """

    study_id: str
    milestone: str
    is_complete: bool
    present_artifacts: List[str]
    missing_artifacts: List[str]


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
        metadata_json=payload.metadata_json,
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


@app.get("/api/v1/etmf/completeness", response_model=CompletenessResponse)
async def check_completeness(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    milestone: str = Query(..., description="The transition milestone to check"),
    session: AsyncSession = Depends(get_db_session),
) -> CompletenessResponse:
    """
    Completeness checking dashboard to verify mandatory artifacts
    before study milestone transitions.
    """
    user_id = getattr(request.state, "user_id", "anonymous")
    user_roles = getattr(request.state, "roles", "anonymous")

    # Define mandatory artifact types per milestone
    milestone_normalized = milestone.strip().upper()
    if milestone_normalized in ("INITIATION", "STUDY START"):
        mandatory = ["Approved Protocol"]
    elif milestone_normalized in ("CONDUCT", "DATA COLLECTION"):
        mandatory = ["Approved Protocol", "Define-XML", "Blank CRF"]
    elif milestone_normalized in ("CLOSEOUT", "STUDY CLOSED", "LOCK"):
        mandatory = [
            "Approved Protocol",
            "Define-XML",
            "Blank CRF",
            "Data Lock Certificate",
        ]
    else:
        raise HTTPException(
            status_code=400,
            detail="Unknown milestone. Supported: INITIATION, CONDUCT, CLOSEOUT",
        )

    # Query all archived artifact types for this study
    stmt = select(TMFDocument.artifact_type).where(TMFDocument.study_id == study_id)
    result = await session.execute(stmt)
    archived_types = set(result.scalars().all())

    present_artifacts = []
    missing_artifacts = []

    # Map different possible aliases of artifacts for robust checking
    for item in mandatory:
        matched = False
        for archived in archived_types:
            if item.lower() in archived.lower():
                present_artifacts.append(archived)
                matched = True
                break
        if not matched:
            missing_artifacts.append(item)

    is_complete = len(missing_artifacts) == 0

    # Log action to immutable audit trail
    await write_audit_log(
        session=session,
        user_id=user_id,
        user_role=user_roles,
        action="COMPLETENESS",
        document_id=None,
        details=f"Performed completeness checking for study '{study_id}' and milestone '{milestone_normalized}'. Complete: {is_complete}.",
    )

    return CompletenessResponse(
        study_id=study_id,
        milestone=milestone_normalized,
        is_complete=is_complete,
        present_artifacts=present_artifacts,
        missing_artifacts=missing_artifacts,
    )
