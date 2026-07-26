import hashlib
import os
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Any, AsyncGenerator, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request, Response, status
from pydantic import BaseModel, Field, model_validator
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.eisf.database import db_manager
from apps.eisf.models import Base, ISFAuditLog, ISFDocument
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import get_normalized_roles, verify_not_auditor

DATABASE_URL = os.getenv("EISF_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


# Pydantic Schemas for eISF API Requests/Responses
class DocumentCreate(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: Optional[Dict[str, Any]] = Field(None, description="Metadata JSON")
    correlation_key: Optional[str] = Field(None, description="Correlation key")
    content_checksum: Optional[str] = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    )


class DocumentUpdate(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: Optional[Dict[str, Any]] = Field(None, description="Metadata JSON")
    correlation_key: Optional[str] = Field(None, description="Correlation key")
    content_checksum: Optional[str] = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: str = Field(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    )


class EISFIngestionRequest(BaseModel):
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: Optional[str] = Field(
        None, description="Binder classification"
    )
    artifact_type: Optional[str] = Field(
        None,
        description="Artifact classification metadata alias for binder_classification",
    )
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    metadata_json: Optional[Dict[str, Any]] = Field(None, description="Metadata JSON")
    correlation_key: Optional[str] = Field(None, description="Correlation key")
    content_checksum: Optional[str] = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    reason_for_change: Optional[str] = Field(
        None, min_length=10, max_length=1000, description="Part 11 reason for change"
    )

    @classmethod
    @model_validator(mode="before")
    def resolve_binder_class(cls, data: Any) -> Any:
        if isinstance(data, dict):
            bc = data.get("binder_classification")
            at = data.get("artifact_type")
            if not bc and not at:
                raise ValueError(
                    "Either binder_classification or artifact_type must be provided"
                )
            if not bc:
                data["binder_classification"] = at
        return data


class DocumentResponse(BaseModel):
    id: str
    study_id: str
    site_id: str
    binder_classification: str
    filename: str
    content: str
    mime_type: str
    version_index: int
    created_at: datetime
    created_by: str
    metadata_json: Optional[Dict[str, Any]] = None
    correlation_key: Optional[str] = None
    content_checksum: Optional[str] = None
    sync_status: str
    source_system: str


class BinderSectionStatus(BaseModel):
    section_name: str
    required_artifacts: List[str]
    present: List[str]
    missing: List[str]


class BinderCompletenessResponse(BaseModel):
    site_id: str
    is_complete: bool
    sections: List[BinderSectionStatus]


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Lifespan context manager for the eISF FastAPI application.
    Initializes database connections and creates SQLite tables on startup.
    Disposes resources on shutdown.
    """
    db_manager.init_db(DATABASE_URL)

    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - eISF Service",
    version="0.1.0",
    lifespan=lifespan,
)

# Mount the shared GatewayAuthMiddleware
app.add_middleware(GatewayAuthMiddleware)


@app.middleware("http")
async def extract_site_claim_middleware(request: Request, call_next):
    """
    HTTP middleware to extract site ID claim from headers and set it to request.state.site_id.
    """
    site_id = (
        request.headers.get("X-Site-Id")
        or request.headers.get("x-site-id")
        or request.headers.get("X-User-Site")
    )
    request.state.site_id = site_id
    return await call_next(request)


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


async def write_audit_log(
    session: AsyncSession,
    actor_id: str,
    actor_role: str,
    action: str,
    document_id: Optional[str],
    details: str,
    reason_for_change: str,
) -> None:
    """
    Appends an entry to the 21 CFR Part 11 compliant ISFAuditLog.
    """
    log_entry = ISFAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


async def enforce_site_isolation(
    request: Request,
    resource_site_id: str,
    session: AsyncSession,
) -> None:
    """
    Enforces PRD-SYS-004 site isolation constraints centrally.
    Reads authenticated site claim from request.state.site_id.
    If a site user attempts to access a resource belonging to another site,
    rejects with 403 Forbidden and records a SECURITY_ALERT audit event.
    """
    roles = get_normalized_roles(request)

    # Site users are principal investigators, site investigators, CRCs, coordinators, etc.
    is_site_user = any(
        role
        in {
            "site investigator",
            "investigator",
            "site-investigator",
            "site_investigator",
            "investigator_user",
            "crc",
            "coordinator",
        }
        for role in roles
    )

    # Ensure request.state has site_id set
    if not hasattr(request.state, "site_id") or request.state.site_id is None:
        request.state.site_id = (
            request.headers.get("X-Site-Id")
            or request.headers.get("x-site-id")
            or request.headers.get("X-User-Site")
        )

    user_site_id = getattr(request.state, "site_id", None)

    if is_site_user:
        if not user_site_id or user_site_id != resource_site_id:
            # Cross-site access attempt detected! Reject with 403 and log a SECURITY_ALERT audit event
            actor_id = getattr(request.state, "user_id", "system")
            actor_roles = ",".join(roles) if isinstance(roles, list) else str(roles)
            client_ip = request.headers.get(
                "x-forwarded-for",
                request.client.host if request.client else "127.0.0.1",
            )
            if "," in client_ip:
                client_ip = client_ip.split(",")[0].strip()

            details = (
                f"SECURITY ALERT: Access Violation. Site user '{actor_id}' from IP '{client_ip}' "
                f"attempted to access/mutate resource at site '{resource_site_id}' but belongs to site '{user_site_id}'."
            )
            reason_for_change = "Security Violation: Cross-site access denied"

            alert = ISFAuditLog(
                actor_id=actor_id,
                actor_role=actor_roles,
                action="SECURITY_ALERT",
                details=details,
                reason_for_change=reason_for_change,
            )
            session.add(alert)
            await session.commit()

            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Access is restricted to your assigned site.",
            )


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    Exempt from gateway authentication checks.
    """
    return {"status": "ok", "service": "eisf"}


@app.get("/api/v1/eisf/documents", response_model=List[DocumentResponse])
async def list_documents(
    request: Request,
    study_id: Optional[str] = Query(None),
    site_id: Optional[str] = Query(None),
    binder_section: Optional[str] = Query(
        None, description="Filter by binder section / classification"
    ),
    binder_classification: Optional[str] = Query(
        None, description="Filter by binder section / classification"
    ),
    session: AsyncSession = Depends(get_db_session),
):
    """
    List site-scoped, binder-classified documents. Constrains by the authenticated site claim.
    """
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    is_site_user = any(
        role
        in {
            "site investigator",
            "investigator",
            "site-investigator",
            "site_investigator",
            "investigator_user",
            "crc",
            "coordinator",
        }
        for role in roles
    )
    user_site_id = getattr(request.state, "site_id", None)

    if is_site_user:
        if site_id and site_id != user_site_id:
            await enforce_site_isolation(request, site_id, session)
        site_id_filter = user_site_id
    else:
        if user_site_id:
            if site_id and site_id != user_site_id:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Forbidden: Access is restricted to your assigned site.",
                )
            site_id_filter = user_site_id
        else:
            site_id_filter = site_id

    if not site_id_filter:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    stmt = select(ISFDocument).where(ISFDocument.site_id == site_id_filter)
    if study_id:
        stmt = stmt.where(ISFDocument.study_id == study_id)
    if binder_section:
        stmt = stmt.where(ISFDocument.binder_classification == binder_section)
    if binder_classification:
        stmt = stmt.where(ISFDocument.binder_classification == binder_classification)

    result = await session.execute(stmt)
    docs = result.scalars().all()

    # Log view action to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="LIST",
        document_id=None,
        details=f"Listed documents (study_id={study_id}, site_id={site_id_filter}).",
        reason_for_change="Standard document access",
    )

    return docs


@app.post(
    "/api/v1/eisf/documents",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def create_document(
    request: Request,
    payload: DocumentCreate,
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
):
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    # Enforce site isolation
    await enforce_site_isolation(request, payload.site_id, session)

    # Calculate deterministic content checksum
    checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )

    # Calculate version index
    stmt = (
        select(ISFDocument)
        .where(
            ISFDocument.study_id == payload.study_id,
            ISFDocument.site_id == payload.site_id,
            ISFDocument.binder_classification == payload.binder_classification,
        )
        .order_by(ISFDocument.version_index.desc())
    )
    res = await session.execute(stmt)
    latest_doc = res.scalars().first()
    new_version_index = (latest_doc.version_index + 1) if latest_doc else 1

    doc = ISFDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        binder_classification=payload.binder_classification,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        version_index=new_version_index,
        created_by=user_id,
        metadata_json=payload.metadata_json,
        correlation_key=payload.correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
    )
    session.add(doc)
    await session.flush()

    # Log creation to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Created document '{payload.filename}' for study '{payload.study_id}' and site '{payload.site_id}' (Version {new_version_index}).",
        reason_for_change=payload.reason_for_change,
    )

    return doc


@app.post(
    "/events/publish",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
@app.post(
    "/api/v1/eisf/ingest",
    response_model=DocumentResponse,
    status_code=status.HTTP_201_CREATED,
)
async def ingest_document(
    request: Request,
    payload: EISFIngestionRequest,
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
):
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    # Enforce site isolation
    await enforce_site_isolation(request, payload.site_id, session)

    # Determine reason for change
    change_reason = (
        payload.reason_for_change
        or getattr(request.state, "change_reason", None)
        or request.headers.get("x-change-reason")
        or request.headers.get("X-Change-Reason")
    )
    if not change_reason or len(change_reason.strip()) < 10:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Part 11 change justification reason is required and must be at least 10 characters long.",
        )

    # Calculate deterministic content checksum
    checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )

    # Calculate version index
    binder_class = payload.binder_classification or payload.artifact_type
    if not binder_class:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="binder_classification or artifact_type is required",
        )

    stmt = (
        select(ISFDocument)
        .where(
            ISFDocument.study_id == payload.study_id,
            ISFDocument.site_id == payload.site_id,
            ISFDocument.binder_classification == binder_class,
        )
        .order_by(ISFDocument.version_index.desc())
    )
    res = await session.execute(stmt)
    latest_doc = res.scalars().first()
    new_version_index = (latest_doc.version_index + 1) if latest_doc else 1

    doc = ISFDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        binder_classification=binder_class,
        filename=payload.filename,
        content=payload.content,
        mime_type=payload.mime_type,
        version_index=new_version_index,
        created_by=user_id,
        metadata_json=payload.metadata_json,
        correlation_key=payload.correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
    )
    session.add(doc)
    await session.flush()

    # Log ingestion to audit trail (action should be INGEST)
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="INGEST",
        document_id=doc.id,
        details=f"Ingested document '{payload.filename}' for study '{payload.study_id}' and site '{payload.site_id}' (Version {new_version_index}).",
        reason_for_change=change_reason,
    )

    return doc


@app.get("/api/v1/eisf/documents/{document_id}", response_model=DocumentResponse)
async def get_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
):
    """
    View metadata for a specific eISF document. Constrains by the authenticated site claim.
    """
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_site_isolation(request, doc.site_id, session)
    user_site_id = getattr(request.state, "site_id", None)
    if user_site_id and doc.site_id != user_site_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Access is restricted to your assigned site.",
        )

    # Log view to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="VIEW",
        document_id=doc.id,
        details=f"Viewed document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document access",
    )

    return doc


@app.get("/api/v1/eisf/documents/{document_id}/download")
async def download_document(
    request: Request,
    document_id: str,
    session: AsyncSession = Depends(get_db_session),
):
    """
    Download/stream file content for a specific eISF document. Constrains by the authenticated site claim.
    """
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_site_isolation(request, doc.site_id, session)
    user_site_id = getattr(request.state, "site_id", None)
    if user_site_id and doc.site_id != user_site_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Forbidden: Access is restricted to your assigned site.",
        )

    # Log download to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="DOWNLOAD",
        document_id=doc.id,
        details=f"Downloaded document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change="Standard document download",
    )

    return Response(
        content=doc.content,
        media_type=doc.mime_type,
        headers={"Content-Disposition": f"attachment; filename={doc.filename}"},
    )


@app.put("/api/v1/eisf/documents/{document_id}", response_model=DocumentResponse)
async def update_document(
    request: Request,
    document_id: str,
    payload: DocumentUpdate,
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
):
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_site_isolation(request, doc.site_id, session)
    await enforce_site_isolation(request, payload.site_id, session)

    # Update document properties
    doc.study_id = payload.study_id
    doc.site_id = payload.site_id
    doc.binder_classification = payload.binder_classification
    doc.filename = payload.filename
    doc.content = payload.content
    doc.mime_type = payload.mime_type
    doc.metadata_json = payload.metadata_json
    doc.correlation_key = payload.correlation_key
    doc.content_checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )
    doc.source_system = payload.source_system
    doc.version_index += 1

    await session.flush()

    # Log update to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="UPDATE_DOCUMENT",
        document_id=doc.id,
        details=f"Updated document '{doc.filename}' (ID: {doc.id}) to version {doc.version_index}.",
        reason_for_change=payload.reason_for_change,
    )

    return doc


@app.delete(
    "/api/v1/eisf/documents/{document_id}", status_code=status.HTTP_204_NO_CONTENT
)
async def delete_document(
    request: Request,
    document_id: str,
    reason_for_change: str = Query(
        ..., min_length=10, max_length=1000, description="Part 11 reason for change"
    ),
    _not_auditor=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
):
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_site_isolation(request, doc.site_id, session)

    # Log deletion to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="DELETE_DOCUMENT",
        document_id=doc.id,
        details=f"Deleted document '{doc.filename}' (ID: {doc.id}).",
        reason_for_change=reason_for_change,
    )

    await session.delete(doc)
    await session.flush()


# Standard eISF required binder artifact set by section
REQUIRED_BINDER_SECTIONS = {
    "Investigator & Staff": [
        "Investigator CV",
        "Delegation of Authority Log",
    ],
    "Protocols & Amendments": [
        "Approved Protocol",
        "Protocol Sign-off",
    ],
    "Regulatory Approvals": [
        "IRB Approval",
        "FDA Form 1572",
    ],
}


@app.get("/api/v1/eisf/completeness", response_model=BinderCompletenessResponse)
async def get_binder_completeness(
    request: Request,
    study_id: str = Query(..., description="The clinical study ID"),
    site_id: Optional[str] = Query(None, description="The site ID"),
    session: AsyncSession = Depends(get_db_session),
):
    """
    Check completeness of the electronic Investigator Site File (eISF) binder.
    Compares filed artifacts for the study and site against a required artifact list by section.
    Enforces site isolation strictly.
    """
    user_id = getattr(request.state, "user_id", "system")
    roles = get_normalized_roles(request)

    is_site_user = any(
        role
        in {
            "site investigator",
            "investigator",
            "site-investigator",
            "site_investigator",
            "investigator_user",
            "crc",
            "coordinator",
        }
        for role in roles
    )
    user_site_id = getattr(request.state, "site_id", None)

    if is_site_user:
        if site_id and site_id != user_site_id:
            await enforce_site_isolation(request, site_id, session)
        site_id_filter = user_site_id
    else:
        if user_site_id:
            if site_id and site_id != user_site_id:
                raise HTTPException(
                    status_code=status.HTTP_403_FORBIDDEN,
                    detail="Forbidden: Access is restricted to your assigned site.",
                )
            site_id_filter = user_site_id
        else:
            site_id_filter = site_id

    if not site_id_filter:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    # Query all filed documents for the site and study
    stmt = select(ISFDocument).where(
        ISFDocument.study_id == study_id,
        ISFDocument.site_id == site_id_filter,
    )
    res = await session.execute(stmt)
    docs = res.scalars().all()

    # Collect unique present binder classifications (case-insensitive for accurate matching)
    actual_classifications = {doc.binder_classification.strip().lower() for doc in docs}

    sections_status = []
    global_is_complete = True

    for section_name, required_artifacts in REQUIRED_BINDER_SECTIONS.items():
        present = []
        missing = []
        for req_art in required_artifacts:
            if req_art.strip().lower() in actual_classifications:
                present.append(req_art)
            else:
                missing.append(req_art)
                global_is_complete = False

        sections_status.append(
            BinderSectionStatus(
                section_name=section_name,
                required_artifacts=required_artifacts,
                present=present,
                missing=missing,
            )
        )

    # Log completeness checking action to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=",".join(roles) if isinstance(roles, list) else str(roles),
        action="COMPLETENESS",
        document_id=None,
        details=f"Checked completeness for study '{study_id}', site '{site_id_filter}'. Complete: {global_is_complete}.",
        reason_for_change="Standard completeness verification",
    )

    return BinderCompletenessResponse(
        site_id=site_id_filter,
        is_complete=global_is_complete,
        sections=sections_status,
    )
