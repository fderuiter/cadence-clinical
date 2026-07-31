import hashlib
import os
import time
from datetime import date, datetime, timezone
from typing import Any, Dict, List, Optional

from fastapi import Depends, FastAPI, HTTPException, Query, Request, Response, status
from pydantic import BaseModel, ConfigDict, Field, model_validator
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.eisf.database import db_manager
from apps.eisf.models import Base, ISFAuditLog, ISFDocument
from apps.eisf.routers.eisf import router as eisf_router
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import (
    SITE_SCOPED_ROLES,
    Principal,
    can_access_site,
    get_principal,
    has_permission,
    require_permission,
)

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
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> "DocumentCreate":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


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
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
    )

    @model_validator(mode="after")
    def validate_dates(self) -> "DocumentUpdate":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


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
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
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

    @model_validator(mode="after")
    def validate_dates(self) -> "EISFIngestionRequest":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class EISFSyncItem(BaseModel):
    id: Optional[str] = None
    study_id: str = Field(..., description="The clinical study ID")
    site_id: str = Field(..., description="The clinical site ID")
    binder_classification: str = Field(..., description="Binder classification")
    filename: str = Field(..., description="Document filename")
    content: str = Field(..., description="Base64 or raw content")
    mime_type: str = Field(..., description="MIME type")
    version_index: Optional[int] = Field(None, description="Optional version index")
    metadata_json: Optional[Dict[str, Any]] = Field(None, description="Metadata JSON")
    correlation_key: Optional[str] = Field(None, description="Correlation key")
    content_checksum: Optional[str] = Field(None, description="Content checksum")
    source_system: str = Field("eISF", description="Source system name")
    sync_status: str = Field("PENDING", description="Sync status")
    conflict_policy: str = Field(
        "CLIENT_WINS", description="CLIENT_WINS, SERVER_WINS, or MERGE"
    )
    issue_date: Optional[date] = Field(None, description="Optional document issue date")
    expiration_date: Optional[date] = Field(
        None, description="Optional document expiration date"
    )
    document_owner_id: Optional[str] = Field(
        None, description="Optional document owner ID"
    )

    @classmethod
    @model_validator(mode="before")
    def resolve_conflict_policy(cls, data: Any) -> Any:
        if isinstance(data, dict):
            cp = data.get("conflict_policy")
            cs = data.get("conflict_strategy")
            if not cp and cs:
                data["conflict_policy"] = cs
        return data

    @model_validator(mode="after")
    def validate_dates(self) -> "EISFSyncItem":
        if self.issue_date and self.expiration_date:
            if self.issue_date > self.expiration_date:
                raise ValueError("issue_date cannot be later than expiration_date")
        return self


class EISFSyncRequest(BaseModel):
    submissions: List[EISFSyncItem] = Field(..., description="List of sync items")


class EISFSyncResponse(BaseModel):
    status: str = "success"
    processed_count: int
    created_count: int
    updated_count: int
    ignored_count: int


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
    issue_date: Optional[date] = None
    expiration_date: Optional[date] = None
    document_owner_id: Optional[str] = None

    model_config = ConfigDict(from_attributes=True)


class BinderSectionStatus(BaseModel):
    section_name: str
    required_artifacts: List[str]
    present: List[str]
    missing: List[str]


class BinderCompletenessResponse(BaseModel):
    site_id: str
    is_complete: bool
    sections: List[BinderSectionStatus]


app = FastAPI(
    title="Cadence Clinical - eISF Service",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Mount the shared GatewayAuthMiddleware
app.add_middleware(GatewayAuthMiddleware)

app.include_router(eisf_router)


get_db_session = DatabaseSessionDependency(db_manager)


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


async def enforce_document_site_visibility(
    principal: Principal,
    resource_site_id: str,
    session: AsyncSession,
) -> None:
    """
    Enforces site isolation constraints using Principal and can_access_site.
    If denied, records a SECURITY_ALERT audit event and raises 403.
    """
    if not can_access_site(principal, resource_site_id):
        actor_id = principal.user_id or "system"
        actor_roles = (
            ",".join(principal.raw_roles)
            if principal.raw_roles
            else (",".join(principal.roles) if principal.roles else "anonymous")
        )
        caller_scope = (
            ",".join(principal.assigned_sites) if principal.assigned_sites else "global"
        )

        details = (
            f"SECURITY ALERT: Access Violation. User '{actor_id}' with roles '{actor_roles}' (scope: '{caller_scope}') "
            f"attempted to access/mutate resource at site '{resource_site_id}' but is not permitted."
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
        await session.flush()

        # Write to a separate committed session to ensure the alert survives the HTTP route rollback
        async with db_manager.get_session_maker()() as audit_session:
            audit_alert = ISFAuditLog(
                actor_id=actor_id,
                actor_role=actor_roles,
                action="SECURITY_ALERT",
                details=details,
                reason_for_change=reason_for_change,
            )
            audit_session.add(audit_alert)
            await audit_session.commit()

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


@app.get("/api/v1/eisf/binders/{site_id}", response_model=List[DocumentResponse])
async def get_site_binder_endpoint(
    site_id: str,
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    """
    Retrieve site-isolated regulatory binder documents for specified site.
    Enforces site isolation strictly.
    """
    await enforce_document_site_visibility(principal, site_id, session)

    stmt = select(ISFDocument).where(ISFDocument.site_id == site_id)
    result = await session.execute(stmt)
    return result.scalars().all()


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
    principal: Principal = Depends(get_principal),
):
    """
    List site-scoped, binder-classified documents. Constrains by the authenticated Principal scope.
    """
    # Check if the principal is site-scoped
    is_site_scoped = any(r in SITE_SCOPED_ROLES for r in principal.roles) or bool(
        principal.assigned_sites
    )

    if is_site_scoped:
        if principal.assigned_sites:
            if site_id:
                if site_id in principal.assigned_sites:
                    site_id_filter = site_id
                else:
                    site_id_filter = principal.assigned_sites
            else:
                site_id_filter = principal.assigned_sites
        else:
            site_id_filter = []
    else:
        site_id_filter = site_id

    if site_id_filter is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    stmt = select(ISFDocument)
    if isinstance(site_id_filter, list):
        stmt = stmt.where(ISFDocument.site_id.in_(site_id_filter))
    else:
        stmt = stmt.where(ISFDocument.site_id == site_id_filter)

    if study_id:
        stmt = stmt.where(ISFDocument.study_id == study_id)
    if binder_section:
        stmt = stmt.where(ISFDocument.binder_classification == binder_section)
    if binder_classification:
        stmt = stmt.where(ISFDocument.binder_classification == binder_classification)

    result = await session.execute(stmt)
    docs = result.scalars().all()

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    # Log view action to audit trail
    await write_audit_log(
        session=session,
        actor_id=actor_id,
        actor_role=actor_roles,
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
    _not_auditor=Depends(require_permission("eisf_document:create")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, payload.site_id, session)

    # Enforce manage_expiration permission if any expiration metadata is provided
    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

    # Calculate deterministic content checksum
    checksum = (
        payload.content_checksum
        or hashlib.sha256(payload.content.encode("utf-8")).hexdigest()
    )

    # Derive correlation key if not provided
    correlation_key = payload.correlation_key
    if not correlation_key:
        from apps.eisf.adapter import derive_correlation_key

        artifact_type = (payload.metadata_json or {}).get(
            "artifact_type"
        ) or payload.binder_classification
        correlation_key = derive_correlation_key(
            payload.study_id,
            payload.site_id,
            payload.binder_classification,
            artifact_type,
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
        correlation_key=correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
        issue_date=payload.issue_date,
        expiration_date=payload.expiration_date,
        document_owner_id=payload.document_owner_id,
    )
    session.add(doc)
    await session.flush()

    # Log creation to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=actor_roles,
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
    _not_auditor=Depends(require_permission("eisf_document:create")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, payload.site_id, session)

    # Enforce manage_expiration permission if any expiration metadata is provided
    if (
        payload.issue_date is not None
        or payload.expiration_date is not None
        or payload.document_owner_id is not None
    ):
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

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

    # Derive correlation key if not provided
    correlation_key = payload.correlation_key
    if not correlation_key:
        from apps.eisf.adapter import derive_correlation_key

        artifact_type = (
            (payload.metadata_json or {}).get("artifact_type")
            or payload.binder_classification
            or payload.artifact_type
            or ""
        )
        correlation_key = derive_correlation_key(
            payload.study_id, payload.site_id, binder_class, artifact_type
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
        correlation_key=correlation_key,
        content_checksum=checksum,
        source_system=payload.source_system,
        sync_status="PENDING",
        issue_date=payload.issue_date,
        expiration_date=payload.expiration_date,
        document_owner_id=payload.document_owner_id,
    )
    session.add(doc)
    await session.flush()

    # Log ingestion to audit trail (action should be INGEST)
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=actor_roles,
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
    principal: Principal = Depends(get_principal),
):
    """
    View metadata for a specific eISF document. Constrains by the authenticated site claim.
    """
    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, doc.site_id, session)

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    # Log view to audit trail
    await write_audit_log(
        session=session,
        actor_id=actor_id,
        actor_role=actor_roles,
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
    principal: Principal = Depends(get_principal),
):
    """
    Download/stream file content for a specific eISF document. Constrains by the authenticated site claim.
    """
    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, doc.site_id, session)

    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    # Log download to audit trail
    await write_audit_log(
        session=session,
        actor_id=actor_id,
        actor_role=actor_roles,
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
    _not_auditor=Depends(require_permission("eisf_document:update")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, doc.site_id, session)
    await enforce_document_site_visibility(principal, payload.site_id, session)

    # Enforce manage_expiration permission if any expiration metadata is set or changed
    is_expiration_metadata_changing = (
        payload.issue_date != doc.issue_date
        or payload.expiration_date != doc.expiration_date
        or payload.document_owner_id != doc.document_owner_id
    )
    if is_expiration_metadata_changing:
        if not has_permission(principal, "etmf_document:manage_expiration"):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail="Forbidden: Lacks manage_expiration permission to set or change expiration metadata.",
            )

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
    doc.issue_date = payload.issue_date
    doc.expiration_date = payload.expiration_date
    doc.document_owner_id = payload.document_owner_id

    await session.flush()

    # Log update to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=actor_roles,
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
    _not_auditor=Depends(require_permission("eisf_document:delete")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    user_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    stmt = select(ISFDocument).where(ISFDocument.id == document_id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"eISF Document with ID '{document_id}' not found.",
        )

    # Enforce site isolation
    await enforce_document_site_visibility(principal, doc.site_id, session)

    # Log deletion to audit trail
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=actor_roles,
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
        "Financial Disclosure",
        "Medical License",
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
    principal: Principal = Depends(get_principal),
):
    """
    Check completeness of the electronic Investigator Site File (eISF) binder.
    Compares filed artifacts for the study and site against a required artifact list by section.
    Enforces site isolation strictly.
    """
    is_site_scoped = any(r in SITE_SCOPED_ROLES for r in principal.roles) or bool(
        principal.assigned_sites
    )

    if is_site_scoped:
        if principal.assigned_sites:
            if site_id:
                if site_id in principal.assigned_sites:
                    site_id_filter = site_id
                else:
                    site_id_filter = principal.assigned_sites
            else:
                site_id_filter = principal.assigned_sites
        else:
            site_id_filter = []
    else:
        site_id_filter = site_id

    if site_id_filter is None:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="site_id is required and must be provided either in the query or via authenticated claim.",
        )

    # Query all filed documents for the site and study
    stmt = select(ISFDocument).where(ISFDocument.study_id == study_id)
    if isinstance(site_id_filter, list):
        stmt = stmt.where(ISFDocument.site_id.in_(site_id_filter))
    else:
        stmt = stmt.where(ISFDocument.site_id == site_id_filter)

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
    actor_id = principal.user_id or "system"
    actor_roles = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    await write_audit_log(
        session=session,
        actor_id=actor_id,
        actor_role=actor_roles,
        action="COMPLETENESS",
        document_id=None,
        details=f"Checked completeness for study '{study_id}', site '{site_id_filter}'. Complete: {global_is_complete}.",
        reason_for_change="Standard completeness verification",
    )

    response_site_id = (
        site_id
        if (
            site_id
            and (not principal.assigned_sites or site_id in principal.assigned_sites)
        )
        else (principal.assigned_sites[0] if principal.assigned_sites else "unknown")
    )

    return BinderCompletenessResponse(
        site_id=response_site_id,
        is_complete=global_is_complete,
        sections=sections_status,
    )


async def propagate_to_etmf(
    study_id: str,
    site_id: str,
    binder_classification: str,
    filename: str,
    content: str,
    mime_type: str,
    metadata_json: Optional[dict] = None,
    correlation_key: Optional[str] = None,
    content_checksum: Optional[str] = None,
    source_system: Optional[str] = "eISF",
    reason_for_change: Optional[str] = None,
) -> None:
    """
    Propagates the synchronized document to the eTMF service.
    """
    import logging

    logging.getLogger("eisf_sync")
    from apps.eisf.adapter import derive_correlation_key, map_eisf_to_etmf
    from packages.security.signing import generate_gateway_signature

    # 1. Determine zone, section, artifact_type, and artifact_code
    artifact_type = (metadata_json or {}).get("artifact_type") or binder_classification
    try:
        mapped = map_eisf_to_etmf(binder_classification, artifact_type)
        zone = mapped["zone"]
        section = mapped["section"]
        etmf_art_type = mapped["artifact_type"]
        etmf_art_code = mapped["artifact_code"]
    except ValueError:
        zone = None
        section = None
        etmf_art_type = binder_classification
        etmf_art_code = None

    # Ensure stable correlation key is derived if not supplied
    resolved_correlation_key = correlation_key
    if not resolved_correlation_key:
        resolved_correlation_key = derive_correlation_key(
            study_id, site_id, binder_classification, artifact_type
        )

    # Ensure content checksum is derived if not supplied
    resolved_checksum = content_checksum
    if not resolved_checksum and content is not None:
        resolved_checksum = hashlib.sha256(content.encode("utf-8")).hexdigest()

    # 2. Build payload for eTMF IngestionRequest
    payload = {
        "study_id": study_id,
        "site_id": site_id,
        "artifact_type": etmf_art_type,
        "filename": filename,
        "content": content,
        "mime_type": mime_type,
        "zone": zone,
        "section": section,
        "artifact_code": etmf_art_code,
        "metadata_json": metadata_json or {},
        "correlation_key": resolved_correlation_key,
        "content_checksum": resolved_checksum,
        "source_system": source_system,
    }

    # 3. Sign the service-to-service request using the internal gateway convention (V2)
    # Switch impersonated roles from admin to the scoped system role for least-privilege provenance
    user_id = "eisf_sync_service"
    roles = "system"
    timestamp = str(time.time())
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345").encode()

    # Thread originating reason_for_change into X-Change-Reason; otherwise fallback to structured sync reason
    resolved_change_reason = reason_for_change
    if not resolved_change_reason:
        resolved_change_reason = "eISF to eTMF bidirectional sync propagation"

    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=secret,
        change_reason=resolved_change_reason,
    )

    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": resolved_change_reason,
    }

    # 4. Make the call to eTMF
    etmf_base_url = os.getenv("ETMF_URL", "http://localhost:8003")
    url = f"{etmf_base_url}/api/v1/etmf/ingest"

    try:
        import httpx

        async with httpx.AsyncClient() as client:
            resp = await client.post(url, json=payload, headers=headers, timeout=5.0)
            # Accept both created/versioned (200, 201) and any successful response status,
            # as well as tolerating the "ignored/no-op" status returned inside success response payloads.
            if resp.status_code not in (200, 201):
                # We log the warning but don't fail the sync transaction
                pass
    except Exception:
        # We handle network exceptions gracefully during sync
        pass


@app.post(
    "/api/v1/eisf/sync",
    response_model=EISFSyncResponse,
)
async def sync_documents(
    request: Request,
    payload: EISFSyncRequest,
    _not_auditor=Depends(require_permission("eisf_document:sync")),
    session: AsyncSession = Depends(get_db_session),
    principal: Principal = Depends(get_principal),
):
    import logging

    logging.getLogger("eisf_sync")
    user_id = principal.user_id or "system"
    role_str = (
        ",".join(principal.raw_roles)
        if principal.raw_roles
        else (",".join(principal.roles) if principal.roles else "anonymous")
    )

    processed_count = 0
    created_count = 0
    updated_count = 0
    ignored_count = 0

    from apps.eisf.adapter import derive_correlation_key

    for item in payload.submissions:
        # Enforce site isolation for each item
        await enforce_document_site_visibility(principal, item.site_id, session)

        processed_count += 1

        # Derive correlation key if not provided
        correlation_key = item.correlation_key
        if not correlation_key:
            art_type = (item.metadata_json or {}).get(
                "artifact_type"
            ) or item.binder_classification
            correlation_key = derive_correlation_key(
                item.study_id, item.site_id, item.binder_classification, art_type
            )

        # Compute checksum if not provided
        checksum = (
            item.content_checksum
            or hashlib.sha256(item.content.encode("utf-8")).hexdigest()
        )

        # Query existing documents for this key or matching logical fields if correlation_key is null
        stmt = (
            select(ISFDocument)
            .where(
                (ISFDocument.correlation_key == correlation_key)
                | (
                    ISFDocument.correlation_key.is_(None)
                    & (ISFDocument.study_id == item.study_id)
                    & (ISFDocument.site_id == item.site_id)
                    & (ISFDocument.binder_classification == item.binder_classification)
                )
            )
            .order_by(ISFDocument.version_index.desc())
        )
        res = await session.execute(stmt)
        existing_docs = res.scalars().all()

        # 1. Duplicate detection
        is_duplicate = False
        for ex_doc in existing_docs:
            if ex_doc.content_checksum == checksum:
                # If metadata is also identical, then it is an exact duplicate
                if (ex_doc.metadata_json or {}) == (item.metadata_json or {}):
                    is_duplicate = True
                    break

        if is_duplicate:
            ignored_count += 1
            await write_audit_log(
                session=session,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=existing_docs[0].id if existing_docs else None,
                details=f"SYNC: Ignored duplicate document with correlation_key '{correlation_key}' (checksum matching).",
                reason_for_change="Bidirectional sync: Exact duplicate ignored",
            )
            continue

        # 2. No existing documents: CREATE new document
        if not existing_docs:
            new_version_index = item.version_index or 1
            doc = ISFDocument(
                study_id=item.study_id,
                site_id=item.site_id,
                binder_classification=item.binder_classification,
                filename=item.filename,
                content=item.content,
                mime_type=item.mime_type,
                version_index=new_version_index,
                created_by=user_id,
                metadata_json=item.metadata_json,
                correlation_key=correlation_key,
                content_checksum=checksum,
                source_system=item.source_system,
                sync_status="SYNCED",
            )
            session.add(doc)
            await session.flush()

            # Trigger signed service-to-service eTMF propagation unless eTMF originated
            if item.source_system != "eTMF":
                await propagate_to_etmf(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=item.filename,
                    content=item.content,
                    mime_type=item.mime_type,
                    metadata_json=item.metadata_json,
                    correlation_key=correlation_key,
                    content_checksum=checksum,
                    source_system=item.source_system,
                    reason_for_change=request.headers.get("X-Change-Reason")
                    or item.metadata_json.get("change_reason")
                    if item.metadata_json
                    else None,
                )

            created_count += 1
            await write_audit_log(
                session=session,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=doc.id,
                details=f"SYNC: Created new document '{item.filename}' (correlation_key: '{correlation_key}', version: {new_version_index}) from '{item.source_system}'.",
                reason_for_change=f"Bidirectional sync: Created from {item.source_system}",
            )
            continue

        # 3. Existing documents exist: Apply conflict resolution policies
        latest_existing = existing_docs[0]
        policy = item.conflict_policy.upper() if item.conflict_policy else "CLIENT_WINS"

        if policy == "CLIENT_WINS":
            new_version_index = latest_existing.version_index + 1
            doc = ISFDocument(
                study_id=item.study_id,
                site_id=item.site_id,
                binder_classification=item.binder_classification,
                filename=item.filename,
                content=item.content,
                mime_type=item.mime_type,
                version_index=new_version_index,
                created_by=user_id,
                metadata_json=item.metadata_json,
                correlation_key=correlation_key,
                content_checksum=checksum,
                source_system=item.source_system,
                sync_status="SYNCED",
            )
            session.add(doc)
            await session.flush()

            if item.source_system != "eTMF":
                await propagate_to_etmf(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=item.filename,
                    content=item.content,
                    mime_type=item.mime_type,
                    metadata_json=item.metadata_json,
                    correlation_key=correlation_key,
                    content_checksum=checksum,
                    source_system=item.source_system,
                    reason_for_change=request.headers.get("X-Change-Reason")
                    or item.metadata_json.get("change_reason")
                    if item.metadata_json
                    else None,
                )

            updated_count += 1
            await write_audit_log(
                session=session,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=doc.id,
                details=f"SYNC: Updated document '{item.filename}' to version {new_version_index} via CLIENT_WINS policy.",
                reason_for_change=f"Bidirectional sync: CLIENT_WINS update from {item.source_system}",
            )

        elif policy == "SERVER_WINS":
            ignored_count += 1
            await write_audit_log(
                session=session,
                actor_id=user_id,
                actor_role=role_str,
                action="SYNC",
                document_id=latest_existing.id,
                details=f"SYNC: Ignored incoming document '{item.filename}' (correlation_key: '{correlation_key}') via SERVER_WINS policy.",
                reason_for_change="Bidirectional sync: SERVER_WINS - incoming ignored",
            )

        elif policy == "MERGE":
            # Document Merge Semantics (LWW and lexicographic tiebreakers)
            # Parse overall timestamps
            t_inc = None
            if item.metadata_json:
                ts_val = item.metadata_json.get("timestamp") or item.metadata_json.get(
                    "timestamps", {}
                ).get("content")
                if ts_val:
                    try:
                        t_inc = datetime.fromisoformat(str(ts_val))
                        # If timezone-aware, convert to naive UTC
                        if t_inc.tzinfo is not None:
                            t_inc = t_inc.astimezone(timezone.utc).replace(tzinfo=None)
                    except Exception:
                        pass
            if not t_inc:
                t_inc = datetime.utcnow()

            t_exist = None
            if latest_existing.metadata_json:
                ts_val = latest_existing.metadata_json.get(
                    "timestamp"
                ) or latest_existing.metadata_json.get("timestamps", {}).get("content")
                if ts_val:
                    try:
                        t_exist = datetime.fromisoformat(str(ts_val))
                        if t_exist.tzinfo is not None:
                            t_exist = t_exist.astimezone(timezone.utc).replace(
                                tzinfo=None
                            )
                    except Exception:
                        pass
            if not t_exist:
                t_exist = (
                    latest_existing.created_at.replace(tzinfo=None)
                    if latest_existing.created_at
                    else datetime.utcnow()
                )

            # Get tiebreaker identifiers
            inc_mod_by = (
                (item.metadata_json or {}).get("modified_by")
                or item.source_system
                or "eISF"
            )
            exist_mod_by = (
                (latest_existing.metadata_json or {}).get("modified_by")
                or latest_existing.source_system
                or "server"
            )

            # Determine core field winner
            incoming_wins = False
            if t_inc > t_exist:
                incoming_wins = True
            elif t_inc < t_exist:
                incoming_wins = False
            else:
                if inc_mod_by > exist_mod_by:
                    incoming_wins = True

            if incoming_wins:
                merged_content = item.content
                merged_filename = item.filename
                merged_mime_type = item.mime_type
                merged_checksum = checksum
            else:
                merged_content = latest_existing.content
                merged_filename = latest_existing.filename
                merged_mime_type = latest_existing.mime_type
                merged_checksum = latest_existing.content_checksum

            # Merge metadata_json
            merged_metadata = dict(latest_existing.metadata_json or {})
            incoming_meta = dict(item.metadata_json or {})

            for k, v in incoming_meta.items():
                if k not in merged_metadata:
                    merged_metadata[k] = v
                else:
                    # Overlapping key - Apply LWW
                    t_k_inc = None
                    t_k_exist = None
                    if item.metadata_json and "timestamps" in item.metadata_json:
                        tk_val = item.metadata_json["timestamps"].get(k)
                        if tk_val:
                            try:
                                t_k_inc = datetime.fromisoformat(str(tk_val))
                                if t_k_inc.tzinfo is not None:
                                    t_k_inc = t_k_inc.astimezone(timezone.utc).replace(
                                        tzinfo=None
                                    )
                            except Exception:
                                pass
                    if (
                        latest_existing.metadata_json
                        and "timestamps" in latest_existing.metadata_json
                    ):
                        tk_val = latest_existing.metadata_json["timestamps"].get(k)
                        if tk_val:
                            try:
                                t_k_exist = datetime.fromisoformat(str(tk_val))
                                if t_k_exist.tzinfo is not None:
                                    t_k_exist = t_k_exist.astimezone(
                                        timezone.utc
                                    ).replace(tzinfo=None)
                            except Exception:
                                pass

                    if not t_k_inc:
                        t_k_inc = t_inc
                    if not t_k_exist:
                        t_k_exist = t_exist

                    if t_k_inc > t_k_exist:
                        merged_metadata[k] = v
                    elif t_k_inc < t_k_exist:
                        merged_metadata[k] = latest_existing.metadata_json[k]
                    else:
                        if inc_mod_by > exist_mod_by:
                            merged_metadata[k] = v
                        else:
                            merged_metadata[k] = latest_existing.metadata_json[k]

            # Check if merged document represents any actual change
            has_changes = (
                merged_checksum != latest_existing.content_checksum
                or merged_metadata != latest_existing.metadata_json
                or merged_filename != latest_existing.filename
                or merged_mime_type != latest_existing.mime_type
            )

            if has_changes:
                new_version_index = latest_existing.version_index + 1
                doc = ISFDocument(
                    study_id=item.study_id,
                    site_id=item.site_id,
                    binder_classification=item.binder_classification,
                    filename=merged_filename,
                    content=merged_content,
                    mime_type=merged_mime_type,
                    version_index=new_version_index,
                    created_by=user_id,
                    metadata_json=merged_metadata,
                    correlation_key=correlation_key,
                    content_checksum=merged_checksum,
                    source_system=item.source_system,
                    sync_status="SYNCED",
                )
                session.add(doc)
                await session.flush()

                if item.source_system != "eTMF":
                    await propagate_to_etmf(
                        study_id=item.study_id,
                        site_id=item.site_id,
                        binder_classification=item.binder_classification,
                        filename=merged_filename,
                        content=merged_content,
                        mime_type=merged_mime_type,
                        metadata_json=merged_metadata,
                        correlation_key=correlation_key,
                        content_checksum=merged_checksum,
                        source_system=item.source_system,
                        reason_for_change=request.headers.get("X-Change-Reason")
                        or item.metadata_json.get("change_reason")
                        if item.metadata_json
                        else None,
                    )

                updated_count += 1
                await write_audit_log(
                    session=session,
                    actor_id=user_id,
                    actor_role=role_str,
                    action="SYNC",
                    document_id=doc.id,
                    details=f"SYNC: Merged and updated document '{merged_filename}' to version {new_version_index}.",
                    reason_for_change="Bidirectional sync: MERGE update",
                )
            else:
                ignored_count += 1
                await write_audit_log(
                    session=session,
                    actor_id=user_id,
                    actor_role=role_str,
                    action="SYNC",
                    document_id=latest_existing.id,
                    details=f"SYNC: Merged document result was identical to existing version {latest_existing.version_index}. Ignored.",
                    reason_for_change="Bidirectional sync: MERGE - no changes detected",
                )

    await session.commit()

    return EISFSyncResponse(
        status="success",
        processed_count=processed_count,
        created_count=created_count,
        updated_count=updated_count,
        ignored_count=ignored_count,
    )
