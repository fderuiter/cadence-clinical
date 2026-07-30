import os
import uuid
from datetime import datetime
from typing import Optional

from audit import AuditFields
from fastapi import Depends, FastAPI, HTTPException, Request
from localization import validate_language_code
from pydantic import BaseModel, ConfigDict, Field, field_validator
from sqlalchemy import desc, select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.econsent.cache import (
    ApprovedTranslationCache,
    get_approved_template_translation,
)
from apps.econsent.database import db_manager
from apps.econsent.evaluator import (
    submit_comprehension_answers,
)
from apps.econsent.models import (
    Base,
    ComprehensionCheck,
    ComprehensionResult,
    ConsentAuditLog,
    ConsentClause,
    ConsentDocument,
    ConsentSignature,
    ConsentTemplate,
    ConsentTranslation,
)
from packages.database import DatabaseSessionDependency, get_relational_db_lifespan
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


# Pydantic Schemas for ConsentTranslation
class ConsentTranslationCreate(AuditFields):
    """
    Schema for creating/ingesting a new consent translation.
    """

    translation_id: Optional[str] = Field(
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
    """
    Schema for retrieving an eConsent translation version.
    """

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
    """
    Schema for updating/versioning an existing eConsent translation.
    """

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
    """
    Request payload to transition translation status.
    """

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


# Pydantic Schemas for ComprehensionCheck / Result / Signature
class ComprehensionCheckCreate(AuditFields):
    """
    Schema for creating/configuring a new comprehension check for a template version.
    """

    questions: list[dict] = Field(..., description="List of question dicts")
    expected_answers: dict[str, str] = Field(
        ..., description="Mapping of question_id to expected answer"
    )
    threshold_policy: dict = Field(
        ..., description="Evaluation threshold policy, e.g. {'min_correct': 2}"
    )


class ComprehensionCheckResponse(AuditFields):
    """
    Schema for retrieving comprehension check configurations.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    questions: list[dict]
    expected_answers: dict[str, str]
    threshold_policy: dict


class ComprehensionSubmissionRequest(BaseModel):
    """
    Schema for submitting answers for evaluation.
    """

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
    """
    Schema for the UI-ready progression state after submitting answers.
    """

    passed: bool
    score: float
    total_questions: int
    correct_count: int
    min_required: int
    next_step: str  # "sign_consent" or "review_material" / "retry_checks"
    message: str


class ConsentSignatureRequest(BaseModel):
    """
    Schema for submitting an electronic signature on a template version.
    """

    subject_pseudonym: str = Field(
        ..., description="Pseudonym identifier of the subject"
    )
    signature_data: Optional[str] = Field(
        None, description="Electronic signature data (drawing or string)"
    )
    reason_for_change: str = Field(..., description="Change reason for signing")


class ConsentSignatureResponse(AuditFields):
    """
    Schema for signature response.
    """

    model_config = ConfigDict(from_attributes=True)

    id: str
    template_id: str
    version_index: int
    subject_pseudonym: str
    signature_data: Optional[str]
    signed_at: datetime


DATABASE_URL = os.getenv("ECONSENT_DATABASE_URL", "sqlite+aiosqlite:///:memory:")


app = FastAPI(
    title="Cadence Clinical - eConsent",
    version="0.1.0",
    lifespan=get_relational_db_lifespan(
        db_manager=db_manager,
        database_url=DATABASE_URL,
        base_metadata=Base.metadata,
    ),
)

# Register secure API gateway authentication and context propagation middleware
app.add_middleware(GatewayAuthMiddleware)


get_db_session = DatabaseSessionDependency(db_manager)


approved_translation_cache = ApprovedTranslationCache()


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


@app.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
    response_model=ComprehensionCheckResponse,
    status_code=201,
)
async def create_or_update_comprehension_check(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ComprehensionCheckCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionCheckResponse:
    """
    Create or update the comprehension check definition for a specific template version.
    Records a Part 11 compliant audit log entry.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Validate template version exists
    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    # Check if a ComprehensionCheck already exists for this version
    stmt_check = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res_check = await session.execute(stmt_check)
    check = res_check.scalars().first()

    if check:
        check.questions = payload.questions
        check.expected_answers = payload.expected_answers
        check.threshold_policy = payload.threshold_policy
        check.created_by = user_id
        check.reason_for_change = change_reason
    else:
        check = ComprehensionCheck(
            template_id=template_id,
            version_index=version_index,
            questions=payload.questions,
            expected_answers=payload.expected_answers,
            threshold_policy=payload.threshold_policy,
            created_by=user_id,
            reason_for_change=change_reason,
        )
        session.add(check)

    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="DEFINE_COMPREHENSION_CHECK",
        document_id=check.id,
        details=f"Defined comprehension check for template '{template_id}' version {version_index}.",
        reason_for_change=change_reason,
    )

    return ComprehensionCheckResponse(
        id=check.id,
        template_id=check.template_id,
        version_index=check.version_index,
        questions=check.questions,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
        created_at=check.created_at,
        created_by=check.created_by,
        reason_for_change=check.reason_for_change,
    )


@app.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers",
    response_model=ComprehensionSubmissionResponse,
)
async def submit_comprehension_answers_endpoint(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ComprehensionSubmissionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionSubmissionResponse:
    """
    Submit subject answers for comprehension checks, evaluate them, persist the append-only results,
    and return a UI-ready progression state.
    """
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Validate template exists
    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    try:
        # Submit and evaluate
        result = await submit_comprehension_answers(
            session=session,
            template_id=template_id,
            version_index=version_index,
            subject_pseudonym=payload.subject_pseudonym,
            submitted_answers=payload.submitted_answers,
            created_by=user_id,
            reason_for_change=change_reason,
        )
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=str(e),
        )

    # Calculate count metadata
    total_questions = len(result.expected_answers)
    correct_count = 0
    for q_id, expected_val in result.expected_answers.items():
        sub_val = result.submitted_answers.get(q_id)
        if (
            sub_val is not None
            and str(sub_val).strip().lower() == str(expected_val).strip().lower()
        ):
            correct_count += 1

    import math

    if "min_correct" in result.threshold_policy:
        min_required = int(result.threshold_policy["min_correct"])
    elif "passing_percentage" in result.threshold_policy:
        min_required = math.ceil(
            (float(result.threshold_policy["passing_percentage"]) / 100.0)
            * total_questions
        )
    else:
        min_required = total_questions

    # Set UI progression state
    if result.passed:
        next_step = "sign_consent"
        message = "Congratulations! You have passed the comprehension check and can proceed to sign the consent form."
    else:
        next_step = "retry_checks"
        message = "You did not meet the passing threshold. Please review the material and try again."

    # Write audit log
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="COMPREHENSION_EVALUATION",
        document_id=result.id,
        details=f"Evaluated comprehension answers for template '{template_id}' version {version_index}, subject '{payload.subject_pseudonym}'. Score: {result.score}%. Passed: {result.passed}.",
        reason_for_change=change_reason,
    )

    return ComprehensionSubmissionResponse(
        passed=result.passed,
        score=result.score,
        total_questions=total_questions,
        correct_count=correct_count,
        min_required=min_required,
        next_step=next_step,
        message=message,
    )


@app.post(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/sign",
    response_model=ConsentSignatureResponse,
)
async def sign_consent_template_endpoint(
    request: Request,
    template_id: str,
    version_index: int,
    payload: ConsentSignatureRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentSignatureResponse:
    """
    Electronic signature endpoint that requires all defined/required checks to pass for the exact template version.
    """
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Validate template exists
    stmt_tpl = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    res_tpl = await session.execute(stmt_tpl)
    template = res_tpl.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    # Check if a ComprehensionCheck exists in the database
    stmt_check = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res_check = await session.execute(stmt_check)
    check = res_check.scalars().first()

    # Also check if template specifies a comprehension check in its workflow steps
    has_comprehension_step = any(
        step.get("type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        or step.get("step_type")
        in ("comprehension_check", "comprehension-check", "comprehension")
        for step in template.workflow_steps
    )

    if check or has_comprehension_step:
        # Require a successful ComprehensionResult for this exact subject pseudonym and template version
        stmt_result = select(ComprehensionResult).where(
            ComprehensionResult.template_id == template_id,
            ComprehensionResult.version_index == version_index,
            ComprehensionResult.subject_pseudonym == payload.subject_pseudonym,
            ComprehensionResult.passed.is_(True),
        )
        res_result = await session.execute(stmt_result)
        passing_result = res_result.scalars().first()

        if not passing_result:
            raise HTTPException(
                status_code=400,
                detail="Cannot sign consent. Comprehension checks have not been completed or passed for this template version.",
            )

    # Proceed to save the electronic signature (append-only)
    sig = ConsentSignature(
        template_id=template_id,
        version_index=version_index,
        subject_pseudonym=payload.subject_pseudonym,
        signature_data=payload.signature_data,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(sig)
    await session.flush()

    # Write audit log
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="SIGN_CONSENT",
        document_id=sig.id,
        details=f"Subject '{payload.subject_pseudonym}' signed consent template '{template_id}' version {version_index}.",
        reason_for_change=change_reason,
    )

    return ConsentSignatureResponse(
        id=sig.id,
        template_id=sig.template_id,
        version_index=sig.version_index,
        subject_pseudonym=sig.subject_pseudonym,
        signature_data=sig.signature_data,
        signed_at=sig.signed_at,
        created_by=sig.created_by,
        reason_for_change=sig.reason_for_change,
    )


@app.get(
    "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks",
    response_model=ComprehensionCheckResponse,
)
async def get_comprehension_check(
    request: Request,
    template_id: str,
    version_index: int,
    session: AsyncSession = Depends(get_db_session),
) -> ComprehensionCheckResponse:
    """
    Retrieve the comprehension check definition for a specific template version.
    """
    stmt = select(ComprehensionCheck).where(
        ComprehensionCheck.template_id == template_id,
        ComprehensionCheck.version_index == version_index,
    )
    res = await session.execute(stmt)
    check = res.scalars().first()

    if not check:
        raise HTTPException(
            status_code=404,
            detail=f"Comprehension check for template '{template_id}' version {version_index} not found.",
        )

    return ComprehensionCheckResponse(
        id=check.id,
        template_id=check.template_id,
        version_index=check.version_index,
        questions=check.questions,
        expected_answers=check.expected_answers,
        threshold_policy=check.threshold_policy,
        created_at=check.created_at,
        created_by=check.created_by,
        reason_for_change=check.reason_for_change,
    )


# --- Translation Management Endpoints ---


@app.post(
    "/api/v1/econsent/translations",
    response_model=ConsentTranslationResponse,
    status_code=201,
)
async def create_consent_translation(
    request: Request,
    payload: ConsentTranslationCreate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    """
    Create/ingest a new translation draft (starts at version_index = 1, status = DRAFT).
    Validates that the source clause or template exists.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Validate source exists
    if payload.source_type == "clause":
        stmt = select(ConsentClause).where(
            ConsentClause.clause_id == payload.source_id,
            ConsentClause.version_index == payload.source_version_index,
        )
        res = await session.execute(stmt)
        if not res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Source clause '{payload.source_id}' version {payload.source_version_index} not found.",
            )
    elif payload.source_type == "template":
        stmt = select(ConsentTemplate).where(
            ConsentTemplate.template_id == payload.source_id,
            ConsentTemplate.version_index == payload.source_version_index,
        )
        res = await session.execute(stmt)
        if not res.scalars().first():
            raise HTTPException(
                status_code=400,
                detail=f"Source template '{payload.source_id}' version {payload.source_version_index} not found.",
            )

    translation_id = payload.translation_id or str(uuid.uuid4())

    translation = ConsentTranslation(
        translation_id=translation_id,
        source_id=payload.source_id,
        source_type=payload.source_type,
        source_version_index=payload.source_version_index,
        language_code=payload.language_code,
        translated_title=payload.translated_title,
        translated_text=payload.translated_text,
        status="DRAFT",
        version_index=1,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="CREATE_TRANSLATION",
        document_id=translation.id,
        details=f"Created translation '{translation_id}' draft version 1 for {payload.source_type} '{payload.source_id}' in '{payload.language_code}'.",
        reason_for_change=change_reason,
    )

    return translation


@app.put(
    "/api/v1/econsent/translations/{translation_id}",
    response_model=ConsentTranslationResponse,
)
async def update_consent_translation(
    request: Request,
    translation_id: str,
    payload: ConsentTranslationUpdate,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    """
    Create a new version of an existing translation with incremented version_index.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Check existing translation
    stmt = (
        select(ConsentTranslation)
        .where(ConsentTranslation.translation_id == translation_id)
        .order_by(desc(ConsentTranslation.version_index))
    )
    result = await session.execute(stmt)
    existing = result.scalars().all()

    if not existing:
        raise HTTPException(
            status_code=404,
            detail=f"Translation with ID '{translation_id}' not found.",
        )

    next_version = existing[0].version_index + 1

    translation = ConsentTranslation(
        translation_id=translation_id,
        source_id=payload.source_id,
        source_type=payload.source_type,
        source_version_index=payload.source_version_index,
        language_code=payload.language_code,
        translated_title=payload.translated_title,
        translated_text=payload.translated_text,
        status="DRAFT",  # New versions reset to DRAFT
        version_index=next_version,
        created_by=user_id,
        reason_for_change=change_reason,
    )
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="UPDATE_TRANSLATION",
        document_id=translation.id,
        details=f"Updated translation '{translation_id}' to version {next_version} for {payload.source_type} '{payload.source_id}' in '{payload.language_code}'.",
        reason_for_change=change_reason,
    )

    return translation


@app.get(
    "/api/v1/econsent/translations",
    response_model=list[ConsentTranslationResponse],
)
async def list_consent_translations(
    request: Request,
    source_id: Optional[str] = None,
    source_type: Optional[str] = None,
    language_code: Optional[str] = None,
    status: Optional[str] = None,
    all_versions: bool = False,
    session: AsyncSession = Depends(get_db_session),
) -> list[ConsentTranslationResponse]:
    """
    List translations, optionally filtering by source_id, source_type, language_code, and/or status.
    By default, returns only the latest version of each unique translation.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "List translations")

    stmt = select(ConsentTranslation)
    if source_id:
        stmt = stmt.where(ConsentTranslation.source_id == source_id)
    if source_type:
        stmt = stmt.where(ConsentTranslation.source_type == source_type)
    if language_code:
        stmt = stmt.where(ConsentTranslation.language_code == language_code)
    if status:
        stmt = stmt.where(ConsentTranslation.status == status)

    stmt = stmt.order_by(
        ConsentTranslation.translation_id, desc(ConsentTranslation.version_index)
    )

    result = await session.execute(stmt)
    translations_list = result.scalars().all()

    if not all_versions:
        # Filter in-memory to keep only the latest version of each translation_id
        seen = set()
        latest_translations = []
        for t in translations_list:
            if t.translation_id not in seen:
                seen.add(t.translation_id)
                latest_translations.append(t)
        translations_list = latest_translations

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="LIST_TRANSLATIONS",
        document_id=None,
        details=f"Listed translations (source_id: {source_id}, language_code: {language_code}, all_versions: {all_versions}).",
        reason_for_change=change_reason,
    )

    return translations_list


@app.get(
    "/api/v1/econsent/translations/{translation_id}",
    response_model=ConsentTranslationResponse,
)
async def get_consent_translation(
    request: Request,
    translation_id: str,
    version_index: Optional[int] = None,
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    """
    Retrieve a single translation by translation_id. Returns the latest version by default
    unless version_index is specified.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", "Retrieve translation")

    stmt = select(ConsentTranslation).where(
        ConsentTranslation.translation_id == translation_id
    )
    if version_index is not None:
        stmt = stmt.where(ConsentTranslation.version_index == version_index)
    else:
        stmt = stmt.order_by(desc(ConsentTranslation.version_index))

    result = await session.execute(stmt)
    translation = result.scalars().first()

    if not translation:
        raise HTTPException(
            status_code=404,
            detail=f"Translation '{translation_id}' not found.",
        )

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW_TRANSLATION",
        document_id=translation.id,
        details=f"Viewed translation '{translation_id}' version {translation.version_index}.",
        reason_for_change=change_reason,
    )

    return translation


@app.post(
    "/api/v1/econsent/translations/{translation_id}/transition",
    response_model=ConsentTranslationResponse,
)
async def transition_consent_translation(
    request: Request,
    translation_id: str,
    payload: TranslationTransitionRequest,
    _auth=Depends(verify_not_auditor),
    session: AsyncSession = Depends(get_db_session),
) -> ConsentTranslationResponse:
    """
    Transition translation status: DRAFT -> IN_REVIEW -> APPROVED.
    Invalid transitions are rejected with HTTP 400.
    """
    user_id = getattr(request.state, "user_id", "system")
    user_role = getattr(request.state, "roles", "system")
    change_reason = getattr(request.state, "change_reason", payload.reason_for_change)

    # Fetch latest version
    stmt = (
        select(ConsentTranslation)
        .where(ConsentTranslation.translation_id == translation_id)
        .order_by(desc(ConsentTranslation.version_index))
    )
    result = await session.execute(stmt)
    translation = result.scalars().first()

    if not translation:
        raise HTTPException(
            status_code=404,
            detail=f"Translation '{translation_id}' not found.",
        )

    current_status = translation.status
    target_status = payload.status

    if current_status == target_status:
        return translation

    # Allowed transitions:
    # DRAFT -> IN_REVIEW
    # IN_REVIEW -> APPROVED
    # IN_REVIEW -> DRAFT
    allowed = False
    if current_status == "DRAFT" and target_status == "IN_REVIEW":
        allowed = True
    elif current_status == "IN_REVIEW" and target_status in ("APPROVED", "DRAFT"):
        allowed = True

    if not allowed:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid translation status transition from '{current_status}' to '{target_status}'.",
        )

    # Perform transition
    translation.status = target_status
    translation.reason_for_change = change_reason
    translation.created_by = user_id
    session.add(translation)
    await session.flush()

    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="TRANSITION_TRANSLATION",
        document_id=translation.id,
        details=f"Transitioned translation '{translation_id}' from '{current_status}' to '{target_status}'.",
        reason_for_change=change_reason,
    )

    # Invalidate cache if APPROVED or updated
    if target_status == "APPROVED":
        if translation.source_type == "template":
            approved_translation_cache.invalidate(
                translation.source_id,
                translation.source_version_index,
                translation.language_code,
            )
        elif translation.source_type == "clause":
            # For simplicity and absolute correctness, invalidate all cached entries
            approved_translation_cache.clear()

    return translation


# --- Patient-Facing Approved Content Retrieval ---


async def fetch_composed_translation_from_db(
    template_id: str,
    version_index: int,
    language_code: str,
    session: AsyncSession,
) -> dict:
    """
    Retrieves and composes fully resolved template translation content from database.
    Only returns approved translations.
    """
    # 1. Fetch template by template_id and version_index
    stmt = select(ConsentTemplate).where(
        ConsentTemplate.template_id == template_id,
        ConsentTemplate.version_index == version_index,
    )
    result = await session.execute(stmt)
    template = result.scalars().first()
    if not template:
        raise HTTPException(
            status_code=404,
            detail=f"Consent template '{template_id}' version {version_index} not found.",
        )

    # 2. Fetch approved translation for the template
    tpl_trans_stmt = (
        select(ConsentTranslation)
        .where(
            ConsentTranslation.source_id == template_id,
            ConsentTranslation.source_type == "template",
            ConsentTranslation.source_version_index == version_index,
            ConsentTranslation.language_code == language_code,
            ConsentTranslation.status == "APPROVED",
        )
        .order_by(desc(ConsentTranslation.version_index))
    )
    tpl_trans_res = await session.execute(tpl_trans_stmt)
    tpl_translation = tpl_trans_res.scalars().first()

    if not tpl_translation:
        raise HTTPException(
            status_code=404,
            detail=f"Approved template translation for '{template_id}' (version {version_index}) in '{language_code}' not found.",
        )

    # 3. For each clause referenced by the template, fetch its approved translation in that language
    composed_clauses = []
    for clause_id in template.clauses:
        # Find latest clause version under the template study context
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
                detail=f"Referenced clause '{clause_id}' not found.",
            )

        # Find approved translation for this specific clause version
        clause_trans_stmt = (
            select(ConsentTranslation)
            .where(
                ConsentTranslation.source_id == clause_id,
                ConsentTranslation.source_type == "clause",
                ConsentTranslation.source_version_index == clause.version_index,
                ConsentTranslation.language_code == language_code,
                ConsentTranslation.status == "APPROVED",
            )
            .order_by(desc(ConsentTranslation.version_index))
        )
        clause_trans_res = await session.execute(clause_trans_stmt)
        clause_translation = clause_trans_res.scalars().first()

        if not clause_translation:
            raise HTTPException(
                status_code=404,
                detail=f"Approved clause translation for '{clause_id}' (version {clause.version_index}) in '{language_code}' not found.",
            )

        composed_clauses.append(
            {
                "clause_id": clause.clause_id,
                "title": clause_translation.translated_title,
                "text": clause_translation.translated_text,
                "version_index": clause.version_index,
            }
        )

    return {
        "id": template.id,
        "template_id": template.template_id,
        "study_id": template.study_id,
        "template_name": tpl_translation.translated_title,
        "protocol_version": template.protocol_version,
        "language_code": language_code,
        "is_published": template.is_published,
        "requires_reconsent": template.requires_reconsent,
        "version_index": template.version_index,
        "clauses": composed_clauses,
        "workflow_steps": template.workflow_steps,
    }


@app.get(
    "/api/v1/econsent/templates/{template_id}/approved-content",
)
async def get_approved_composed_template(
    request: Request,
    template_id: str,
    language_code: str,
    version_index: Optional[int] = None,
    session: AsyncSession = Depends(get_db_session),
) -> dict:
    """
    Patient-facing endpoint to retrieve composed approved template content in a specific language.
    Uses the thread-safe read-through cache with TTL and stale-on-error behavior.
    """
    # 1. Resolve version_index if not provided
    if version_index is None:
        stmt = (
            select(ConsentTemplate)
            .where(ConsentTemplate.template_id == template_id)
            .order_by(desc(ConsentTemplate.version_index))
        )
        res = await session.execute(stmt)
        templates = res.scalars().all()
        if not templates:
            raise HTTPException(
                status_code=404,
                detail=f"Template '{template_id}' not found.",
            )
        # Prioritize published version
        published_tpls = [t for t in templates if t.is_published]
        if published_tpls:
            version_index = published_tpls[0].version_index
        else:
            version_index = templates[0].version_index

    # Validate language code
    try:
        language_code = validate_language_code(language_code)
    except ValueError as e:
        raise HTTPException(
            status_code=400,
            detail=str(e),
        )

    # 2. Call read-through cache
    async def fetch_db_fn(tid: str, vidx: int, lang: str) -> dict:
        return await fetch_composed_translation_from_db(tid, vidx, lang, session)

    try:
        composed_data = await get_approved_template_translation(
            approved_translation_cache,
            template_id,
            version_index,
            language_code,
            fetch_db_fn,
        )
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(
            status_code=500,
            detail=f"Database retrieval error: {str(e)}",
        )

    # Write audit log for access
    user_id = getattr(request.state, "user_id", "patient")
    user_role = getattr(request.state, "roles", "patient")
    await write_audit_log(
        session=session,
        actor_id=user_id,
        actor_role=str(user_role),
        action="VIEW_APPROVED_TRANSLATION",
        document_id=composed_data.get("id"),
        details=f"Viewed approved composed template translation for '{template_id}' version {version_index} in '{language_code}'.",
        reason_for_change="Standard translation retrieval",
    )

    return composed_data


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
