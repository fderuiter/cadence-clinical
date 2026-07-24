import os
import uuid
from contextlib import asynccontextmanager
from datetime import datetime
from typing import AsyncGenerator, Optional

from audit import AuditFields
from fastapi import Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel, ConfigDict, Field
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.database import db_manager
from apps.econsent.models import (
    Base,
    ConsentAuditLog,
    ConsentClause,
    ConsentDocument,
    ConsentTemplate,
)
from packages.security.middleware import GatewayAuthMiddleware
from packages.security.rbac import verify_not_auditor


# Pydantic Schemas for eConsent API Requests/Responses
class ConsentDocumentCreate(AuditFields):
    """
    Schema for creating a new eConsent document.
    Reuses the shared 21 CFR Part 11 AuditFields base.
    """

    study_id: str = Field(..., description="Unique clinical study identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    document_name: str = Field(
        ..., max_length=255, description="Name of the eConsent form/document"
    )
    content: str = Field(..., description="Full text/content of the consent form")


class ConsentDocumentResponse(AuditFields):
    """
    Schema for eConsent document response.
    Reuses the shared 21 CFR Part 11 AuditFields base.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of the document")
    study_id: str = Field(..., description="Unique clinical study identifier")
    site_id: str = Field(..., description="Unique clinical site identifier")
    document_name: str = Field(..., description="Name of the eConsent form/document")
    content: str = Field(..., description="Full text/content of the consent form")


# Pydantic Schemas for ConsentClause
class ConsentClauseCreate(AuditFields):
    """
    Schema for creating/ingesting a new eConsent clause.
    """

    clause_id: Optional[str] = Field(
        None,
        description="Unique clause identifier across versions. Generated if not provided.",
    )
    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., max_length=255, description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


class ConsentClauseResponse(AuditFields):
    """
    Schema for retrieving an eConsent clause version.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str = Field(..., description="Unique generated UUID of this version")
    clause_id: str = Field(..., description="Unique clause identifier across versions")
    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


class ConsentClauseUpdate(AuditFields):
    """
    Schema for updating/versioning an existing eConsent clause.
    """

    study_id: str = Field(..., description="Unique clinical study identifier")
    title: str = Field(..., max_length=255, description="Title of the clause")
    text: str = Field(..., description="Content of the clause")


# Pydantic Schemas for ConsentTemplate
class ConsentTemplateCreate(AuditFields):
    """
    Schema for creating/ingesting a new eConsent template.
    """

    template_id: Optional[str] = Field(
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
    """
    Schema for retrieving an eConsent template version.
    """

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
    """
    Schema for updating/versioning an existing eConsent template.
    """

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
    """
    Schema for a resolved clause inside a composed template.
    """

    clause_id: str
    title: str
    text: str
    version_index: int


class ComposedTemplateResponse(BaseModel):
    """
    Schema for a composed template with fully resolved clause texts.
    """

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


DATABASE_URL = os.getenv("ECONSENT_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """
    Lifespan events for the eConsent microservice.
    Initializes database manager, creates SQLite tables, and disposes resources on shutdown.
    """
    db_manager.init_db(DATABASE_URL)

    if DATABASE_URL.startswith("sqlite"):
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.create_all)

    yield

    await db_manager.close()


app = FastAPI(
    title="Cadence Clinical - eConsent",
    version="0.1.0",
    lifespan=lifespan,
)

# Register secure API gateway authentication and context propagation middleware
app.add_middleware(GatewayAuthMiddleware)


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
    Appends an entry to the 21 CFR Part 11 compliant ConsentAuditLog.
    """
    log_entry = ConsentAuditLog(
        actor_id=actor_id,
        actor_role=actor_role,
        action=action,
        document_id=document_id,
        details=details,
        reason_for_change=reason_for_change,
    )
    session.add(log_entry)
    await session.flush()


@app.get("/health")
async def health_check() -> dict[str, str]:
    """
    Service health check endpoint.
    Exempt from gateway authentication checks.
    """
    return {"status": "ok", "service": "econsent"}


def map_document_to_response(doc: ConsentDocument) -> ConsentDocumentResponse:
    return ConsentDocumentResponse(
        id=doc.id,
        study_id=doc.study_id,
        site_id=doc.site_id,
        document_name=doc.document_name,
        content=doc.content,
        created_at=doc.created_at,
        created_by=doc.created_by,
        reason_for_change=doc.reason_for_change,
        version_index=doc.version_index,
    )


@app.post(
    "/api/v1/econsent/documents",
    response_model=ConsentDocumentResponse,
    status_code=201,
)
async def create_consent_document(
    request: Request,
    payload: ConsentDocumentCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentDocumentResponse:
    """
    Create a new clinical trial eConsent document.
    Enforces Part 11 validation and logs access to the immutable audit trail.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    doc = ConsentDocument(
        study_id=payload.study_id,
        site_id=payload.site_id,
        document_name=payload.document_name,
        content=payload.content,
        created_by=user_id,
        reason_for_change=change_reason,
        version_index=payload.version_index,
    )
    session.add(doc)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="CREATE_DOCUMENT",
        document_id=doc.id,
        details=f"Created consent document '{payload.document_name}' for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return map_document_to_response(doc)


@app.get("/api/v1/econsent/documents/{id}", response_model=ConsentDocumentResponse)
async def get_consent_document(
    request: Request,
    id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentDocumentResponse:
    """
    Retrieve an existing eConsent document by its unique identifier.
    Logs access to the audit trail.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")

    # Access reason or default change reason for reading if present
    change_reason = getattr(request.state, "change_reason", "Standard document access")

    stmt = select(ConsentDocument).where(ConsentDocument.id == id)
    result = await session.execute(stmt)
    doc = result.scalars().first()

    if not doc:
        raise HTTPException(
            status_code=404, detail=f"Consent document with ID '{id}' not found."
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=user_role,
        action="VIEW_DOCUMENT",
        document_id=doc.id,
        details=f"Viewed consent document '{doc.document_name}' (ID: {doc.id}).",
        reason_for_change=change_reason,
    )

    return map_document_to_response(doc)


# --- Versioned ICF Clause Endpoints ---


@app.post(
    "/api/v1/econsent/clauses",
    response_model=ConsentClauseResponse,
    status_code=201,
)
async def create_consent_clause(
    request: Request,
    payload: ConsentClauseCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    """
    Create/ingest a new ICF clause version (starts at version_index = 1).
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    clause_id = payload.clause_id or str(uuid.uuid4())

    clause = ConsentClause(
        clause_id=clause_id,
        study_id=payload.study_id,
        title=payload.title,
        text=payload.text,
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(clause)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="INGEST",
        document_id=clause.id,
        details=f"Ingested clause '{clause_id}' version 1 for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return clause


@app.put(
    "/api/v1/econsent/clauses/{clause_id}",
    response_model=ConsentClauseResponse,
)
async def update_consent_clause(
    request: Request,
    clause_id: str,
    payload: ConsentClauseUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    """
    Create a new version of an existing ICF clause with incremented version_index.
    Ensures prior versions are preserved unchanged.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Lookup existing clauses for this clause_id and study_id to compute max version_index
    stmt = (
        select(ConsentClause)
        .where(
            ConsentClause.clause_id == clause_id,
            ConsentClause.study_id == payload.study_id,
        )
        .order_by(desc(ConsentClause.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Clause with ID '{clause_id}' not found for study '{payload.study_id}'.",
        )

    next_version = existing[0].version_index + 1

    clause = ConsentClause(
        clause_id=clause_id,
        study_id=payload.study_id,
        title=payload.title,
        text=payload.text,
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(clause)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=clause.id,
        details=f"Updated clause '{clause_id}' to version {next_version} for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return clause


@app.get(
    "/api/v1/econsent/clauses",
    response_model=list[ConsentClauseResponse],
)
async def list_consent_clauses(
    request: Request,
    study_id: Optional[str] = None,
    clause_id: Optional[str] = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentClauseResponse]:
    """
    List clauses, optionally filtering by study_id and/or clause_id.
    By default, returns only the latest version of each unique clause.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List clauses")

    stmt = select(ConsentClause)
    if study_id:
        stmt = stmt.where(ConsentClause.study_id == study_id)
    if clause_id:
        stmt = stmt.where(ConsentClause.clause_id == clause_id)
    stmt = stmt.order_by(ConsentClause.clause_id, desc(ConsentClause.version_index))

    result = await session.execute(stmt)
    clauses_list = result.scalars().all()

    if not all_versions:
        # Filter in-memory to keep only the latest version of each clause_id
        seen = set()
        latest_clauses = []
        for c in clauses_list:
            if c.clause_id not in seen:
                seen.add(c.clause_id)
                latest_clauses.append(c)
        clauses_list = latest_clauses

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST",
        document_id=None,
        details=f"Listed clauses (study_id: {study_id}, clause_id: {clause_id}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return clauses_list


@app.get(
    "/api/v1/econsent/clauses/{clause_id}",
    response_model=ConsentClauseResponse,
)
async def get_consent_clause(
    request: Request,
    clause_id: str,
    version_index: Optional[int] = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentClauseResponse:
    """
    Retrieve a single clause by its clause_id. Returns the latest version by default
    unless version_index is specified.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve clause")

    stmt = select(ConsentClause).where(ConsentClause.clause_id == clause_id)
    if version_index is not None:
        stmt = stmt.where(ConsentClause.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentClause.version_index))

    result = await session.execute(stmt)
    clause = result.scalars().first()

    if not clause:
        raise HTTPException(
            status_code=404,
            detail=f"Clause '{clause_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=clause.id,
        details=f"Viewed clause '{clause_id}' version {clause.version_index}.",
        reason_for_change=change_reason,
    )

    return clause


# --- Versioned eConsent Template / Workflow Endpoints ---


@app.post(
    "/api/v1/econsent/templates",
    response_model=ConsentTemplateResponse,
    status_code=201,
)
async def create_consent_template(
    request: Request,
    payload: ConsentTemplateCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    """
    Create/ingest a new consent template (starts at version_index = 1 and is_published = False).
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    template_id = payload.template_id or str(uuid.uuid4())

    template = ConsentTemplate(
        template_id=template_id,
        study_id=payload.study_id,
        template_name=payload.template_name,
        protocol_version=payload.protocol_version,
        requires_reconsent=payload.requires_reconsent,
        clauses=payload.clauses,
        workflow_steps=payload.workflow_steps,
        is_published=False,
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="INGEST",
        document_id=template.id,
        details=f"Ingested template '{template_id}' version 1 for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return template


@app.put(
    "/api/v1/econsent/templates/{template_id}",
    response_model=ConsentTemplateResponse,
)
async def update_consent_template(
    request: Request,
    template_id: str,
    payload: ConsentTemplateUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    """
    Create a new version of an existing consent template with incremented version_index.
    Ensures prior versions are preserved unchanged.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    stmt = (
        select(ConsentTemplate)
        .where(
            ConsentTemplate.template_id == template_id,
            ConsentTemplate.study_id == payload.study_id,
        )
        .order_by(desc(ConsentTemplate.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Template with ID '{template_id}' not found for study '{payload.study_id}'.",
        )

    next_version = existing[0].version_index + 1

    template = ConsentTemplate(
        template_id=template_id,
        study_id=payload.study_id,
        template_name=payload.template_name,
        protocol_version=payload.protocol_version,
        requires_reconsent=payload.requires_reconsent,
        clauses=payload.clauses,
        workflow_steps=payload.workflow_steps,
        is_published=False,  # Edits are drafts
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=template.id,
        details=f"Updated template '{template_id}' to version {next_version} for study '{payload.study_id}'.",
        reason_for_change=change_reason,
    )

    return template


@app.get(
    "/api/v1/econsent/templates",
    response_model=list[ConsentTemplateResponse],
)
async def list_consent_templates(
    request: Request,
    study_id: Optional[str] = None,
    template_id: Optional[str] = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentTemplateResponse]:
    """
    List templates, optionally filtering by study_id and/or template_id.
    By default, returns only the latest version of each unique template.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List templates")

    stmt = select(ConsentTemplate)
    if study_id:
        stmt = stmt.where(ConsentTemplate.study_id == study_id)
    if template_id:
        stmt = stmt.where(ConsentTemplate.template_id == template_id)
    stmt = stmt.order_by(
        ConsentTemplate.template_id, desc(ConsentTemplate.version_index)
    )

    result = await session.execute(stmt)
    templates_list = result.scalars().all()

    if not all_versions:
        # Filter in-memory to keep only the latest version of each template_id
        seen = set()
        latest_templates = []
        for t in templates_list:
            if t.template_id not in seen:
                seen.add(t.template_id)
                latest_templates.append(t)
        templates_list = latest_templates

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST",
        document_id=None,
        details=f"Listed templates (study_id: {study_id}, template_id: {template_id}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return templates_list


@app.get(
    "/api/v1/econsent/templates/{template_id}",
    response_model=ConsentTemplateResponse,
)
async def get_consent_template(
    request: Request,
    template_id: str,
    version_index: Optional[int] = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    """
    Retrieve a single template by its template_id. Returns the latest version by default
    unless version_index is specified.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve template")

    stmt = select(ConsentTemplate).where(ConsentTemplate.template_id == template_id)
    if version_index is not None:
        stmt = stmt.where(ConsentTemplate.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTemplate.version_index))

    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=template.id,
        details=f"Viewed template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return template


@app.get(
    "/api/v1/econsent/templates/{template_id}/compose",
    response_model=ComposedTemplateResponse,
)
async def compose_consent_template(
    request: Request,
    template_id: str,
    version_index: Optional[int] = None,
    session: AsyncSession = Depends(get_db_session),
) -> ComposedTemplateResponse:
    """
    Retrieve a template and fully resolve/hydrate all its referenced clauses.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Compose template")

    stmt = select(ConsentTemplate).where(ConsentTemplate.template_id == template_id)
    if version_index is not None:
        stmt = stmt.where(ConsentTemplate.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTemplate.version_index))

    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    # Hydrate each clause referenced by template.clauses
    composed_clauses = []
    for clause_id in template.clauses:
        clause_stmt = (
            select(ConsentClause)
            .where(
                ConsentClause.clause_id == clause_id,
                ConsentClause.study_id == template.study_id,
            )
            .order_by(desc(ConsentClause.version_index))
        )
        clause_res = await session.execute(clause_stmt)
        clause = clause_res.scalars().first()

        if not clause:
            raise HTTPException(
                status_code=404,
                detail=f"Referenced clause '{clause_id}' not found for study '{template.study_id}'.",
            )

        composed_clauses.append(
            ComposedClauseResponse(
                clause_id=clause.clause_id,
                title=clause.title,
                text=clause.text,
                version_index=clause.version_index,
            )
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW",
        document_id=template.id,
        details=f"Composed template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return ComposedTemplateResponse(
        id=template.id,
        template_id=template.template_id,
        study_id=template.study_id,
        template_name=template.template_name,
        protocol_version=template.protocol_version,
        is_published=template.is_published,
        requires_reconsent=template.requires_reconsent,
        version_index=template.version_index,
        clauses=composed_clauses,
        workflow_steps=template.workflow_steps,
        created_at=template.created_at,
        created_by=template.created_by,
        reason_for_change=template.reason_for_change,
    )


@app.post(
    "/api/v1/econsent/templates/{template_id}/publish",
    response_model=ConsentTemplateResponse,
)
async def publish_consent_template(
    request: Request,
    template_id: str,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTemplateResponse:
    """
    Publish a consent template after validating that referenced clauses exist
    and required workflow steps (comprehension check and signature placeholder) are present.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Publish template")

    # Fetch the latest version of the template
    stmt = (
        select(ConsentTemplate)
        .where(ConsentTemplate.template_id == template_id)
        .order_by(desc(ConsentTemplate.version_index))
    )
    result = await session.execute(stmt)
    template = result.scalars().first()

    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Template '{template_id}' not found.",
        )

    if template.is_published:
        raise HTTPException(
            status_code=400,
            detail=f"Template '{template_id}' is already published.",
        )

    # 1. Validate referenced clauses exist under the same study_id
    for clause_id in template.clauses:
        clause_stmt = select(ConsentClause).where(
            ConsentClause.clause_id == clause_id,
            ConsentClause.study_id == template.study_id,
        )
        clause_res = await session.execute(clause_stmt)
        if not clause_res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Validation failed: Referenced clause '{clause_id}' does not exist for study '{template.study_id}'.",
            )

    # 2. Validate required workflow steps are present
    has_comprehension = any(
        step.get("type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        or step.get("step_type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        for step in template.workflow_steps
    )
    has_signature = any(
        step.get("type")
        in ("signature_placeholder", "signature-placeholder", "signature")
        or step.get("step_type")
        in ("signature_placeholder", "signature-placeholder", "signature")
        for step in template.workflow_steps
    )

    if not has_comprehension:
        raise HTTPException(
            status_code=400,
            detail="Validation failed: Template must include a comprehension-check workflow step.",
        )
    if not has_signature:
        raise HTTPException(
            status_code=400,
            detail="Validation failed: Template must include a signature placeholder workflow step.",
        )

    # Mark as published
    template.is_published = True
    template.reason_for_change = change_reason
    template.created_by = user_id
    session.add(template)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE",
        document_id=template.id,
        details=f"Published template '{template_id}' version {template.version_index}.",
        reason_for_change=change_reason,
    )

    return template
