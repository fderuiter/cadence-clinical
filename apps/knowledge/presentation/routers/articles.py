"""
FastAPI router for Knowledge article lifecycle endpoints.

Implements the REST API for article CRUD, Four-Eyes Review, Immutable Version
Snapshotting, and Auto-Supersede per ADR-2188.
All routes are protected by GatewayAuthMiddleware.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

import logging
from typing import Any

from fastapi import APIRouter, Depends, HTTPException, Request, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.adapters.repositories import create_article_service
from apps.knowledge.domain.exceptions import (
    ArticleApprovalConflictError,
    ArticleNotFoundError,
    ArticleReasonRequiredError,
    ArticleTransitionError,
    CategoryConflictError,
    CategoryNotFoundError,
    ContextualHelpMappingNotFoundError,
)
from apps.knowledge.domain.models import ArticleStatus
from apps.knowledge.infrastructure.models import (
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
)
from apps.knowledge.presentation.dtos import (
    ArticleApproveRequest,
    ArticleCreate,
    ArticleDraftSave,
    ArticlePublishRequest,
    ArticleRejectRequest,
    ArticleResponse,
    ArticleSubmitReviewRequest,
    ArticleTransitionRequest,
    ArticleUpdate,
    ArticleVersionResponse,
    AuditLogResponse,
    CategoryCreate,
    CategoryResponse,
    ContextualHelpMappingCreate,
    ContextualHelpMappingResponse,
    ContextualHelpMappingUpdate,
    ContextualHelpResolutionResponse,
)
from packages.security.context import current_user_id
from packages.security.rbac import get_normalized_roles, require_roles

logger = logging.getLogger("knowledge-router")

router = APIRouter(prefix="/api/v1/knowledge", tags=["knowledge"])

ADMIN_ROLES = ["super_admin", "sysadmin"]
ALL_ROLES = [
    "super_admin",
    "sysadmin",
    "sponsor_designer",
    "data_manager",
    "site_investigator",
    "crc",
    "cra",
    "monitor",
    "auditor",
    "tmf_auditor",
]
AUDITOR_ROLES = ["auditor", "tmf_auditor", "super_admin", "sysadmin"]


# ---------------------------------------------------------------------------
# Category endpoints
# ---------------------------------------------------------------------------


@router.post(
    "/categories",
    response_model=CategoryResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def create_category(
    payload: CategoryCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CategoryResponse:
    """Creates a new knowledge article category. Requires super_admin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        category = await svc.create_category(
            name=payload.name,
            slug=payload.slug,
            description=payload.description,
            persona_visibility=payload.persona_visibility,
            parent_id=payload.parent_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except CategoryNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except CategoryConflictError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)
        ) from exc
    return CategoryResponse.model_validate(category)


@router.get(
    "/categories",
    response_model=list[CategoryResponse],
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def list_categories(
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> list[CategoryResponse]:
    """Lists active knowledge categories, filtered by caller's persona visibility."""
    roles = get_normalized_roles(request)
    svc = create_article_service(session)
    categories = await svc.list_categories(user_roles=roles)
    return [CategoryResponse.model_validate(c) for c in categories]


@router.get(
    "/categories/{category_id}",
    response_model=CategoryResponse,
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def get_category(
    category_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> CategoryResponse:
    """Retrieves a single KnowledgeCategory by ID."""
    svc = create_article_service(session)
    category = await svc.get_category_by_id(category_id)
    if not category:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Category not found"
        )
    return CategoryResponse.model_validate(category)


@router.delete(
    "/categories/{category_id}",
    response_model=CategoryResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def delete_category(
    category_id: str,
    reason_for_change: str = "Category soft-deleted",
    session: AsyncSession = Depends(get_db_session),
) -> CategoryResponse:
    """Soft-deletes a KnowledgeCategory by ID. Requires super_admin or sysadmin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        category = await svc.delete_category(
            category_id=category_id,
            actor_user_id=actor,
            reason_for_change=reason_for_change,
        )
    except CategoryNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return CategoryResponse.model_validate(category)


# ---------------------------------------------------------------------------
# Article CRUD & Draft Storage (Issue #4325)
# ---------------------------------------------------------------------------


@router.post(
    "/articles",
    response_model=ArticleResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def create_article(
    payload: ArticleCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Creates a new KnowledgeArticle in DRAFT status with an initial working draft version.
    Requires super_admin role.
    """
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article = await svc.create_article(
            title=payload.title,
            slug=payload.slug,
            category_id=payload.category_id,
            body_markdown=payload.body_markdown,
            version_label=payload.version_label,
            tags=payload.tags,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc

    resp = ArticleResponse.model_validate(article)
    resp.body_markdown = payload.body_markdown
    return resp


@router.put(
    "/articles/{article_id}",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def update_article_draft(
    article_id: str,
    payload: ArticleUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Updates the working draft of an article (Issue #4325).

    Updates the single KnowledgeArticleVersion row during DRAFT status with markdown
    body, auto-rendered HTML, and GxP audit fields (created_by, reason_for_change, version_index).
    Requires super_admin role.
    """
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article, version = await svc.update_draft(
            article_id=article_id,
            body_markdown=payload.body_markdown,
            actor_user_id=actor,
            title=payload.title,
            slug=payload.slug,
            category_id=payload.category_id,
            tags=payload.tags,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc

    resp = ArticleResponse.model_validate(article)
    resp.body_markdown = version.body_markdown
    resp.body_html = version.body_html
    return resp


@router.get(
    "/articles",
    response_model=list[ArticleResponse],
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def list_articles(
    status_filter: ArticleStatus | None = None,
    category_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ArticleResponse]:
    """Lists knowledge articles, optionally filtered by status and/or category."""
    svc = create_article_service(session)
    articles = await svc.list_articles(status=status_filter, category_id=category_id)
    return [ArticleResponse.model_validate(a) for a in articles]


@router.get(
    "/articles/{article_id}",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def get_article(
    article_id: str,
    request: Request,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Retrieves a single KnowledgeArticle by ID along with its active body content.
    If the requesting user has an auditor role, records a READ_BY_AUDITOR audit event.
    """
    from packages.security.context import current_user_id as uid_ctx

    svc = create_article_service(session)
    article = await svc.get_article_by_id(article_id)
    if not article:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Article not found"
        )

    # Fetch relevant version snapshot
    version: KnowledgeArticleVersion | None = None
    if article.status == ArticleStatus.PUBLISHED:
        version = await svc.get_current_published_version(article.id)
    if not version:
        version = await svc.get_working_draft_version(article.id)
    if not version:
        versions = await svc.list_article_versions(article.id)
        if versions:
            version = versions[-1]

    # Emit auditor read event if auditor persona
    actor = uid_ctx.get()
    try:
        roles = get_normalized_roles(request)
        if any(r in roles for r in AUDITOR_ROLES):
            await svc.record_auditor_read(article=article, actor_user_id=actor)
    except Exception:
        pass

    resp = ArticleResponse.model_validate(article)
    if version:
        resp.body_markdown = version.body_markdown
        resp.body_html = version.body_html
    return resp


@router.put(
    "/articles/{article_id}/draft",
    response_model=ArticleVersionResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
@router.patch(
    "/articles/{article_id}/draft",
    response_model=ArticleVersionResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def save_article_draft(
    article_id: str,
    payload: ArticleDraftSave,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleVersionResponse:
    """Saves updated body content to a DRAFT article (Issue #4325). Requires super_admin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        _, version = await svc.update_draft(
            article_id=article_id,
            body_markdown=payload.body_markdown,
            actor_user_id=actor,
            title=payload.title,
            slug=payload.slug,
            category_id=payload.category_id,
            tags=payload.tags,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleVersionResponse.model_validate(version)


# ---------------------------------------------------------------------------
# Four-Eyes Review & Snapshots Endpoints (Issue #4326)
# ---------------------------------------------------------------------------


@router.post(
    "/articles/{article_id}/submit-review",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def submit_article_review(
    article_id: str,
    payload: ArticleSubmitReviewRequest = ArticleSubmitReviewRequest(),
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """Submits a DRAFT article for peer review (DRAFT -> IN_REVIEW)."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article = await svc.submit_for_review(
            article_id=article_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


@router.post(
    "/articles/{article_id}/approve",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def approve_article(
    article_id: str,
    payload: ArticleApproveRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Approves an article in IN_REVIEW status (IN_REVIEW -> APPROVED).

    Enforces Four-Eyes check: approver cannot be author_user_id or last_edited_by.
    Locks current KnowledgeArticleVersion record as permanently immutable.
    """
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article = await svc.approve_article(
            article_id=article_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleApprovalConflictError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except ArticleReasonRequiredError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


@router.post(
    "/articles/{article_id}/reject",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def reject_article(
    article_id: str,
    payload: ArticleRejectRequest = ArticleRejectRequest(),
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """Rejects an article in IN_REVIEW status (IN_REVIEW -> REJECTED)."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article = await svc.reject_article(
            article_id=article_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


# ---------------------------------------------------------------------------
# Publication & Auto-Supersede (Issue #4327)
# ---------------------------------------------------------------------------


@router.post(
    "/articles/{article_id}/publish",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def publish_article(
    article_id: str,
    payload: ArticlePublishRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Publishes an APPROVED article (APPROVED -> PUBLISHED).

    - Updates fast O(1) lookup pointer KnowledgeArticle.current_published_version_id.
    - Publishing version N+1 automatically sets prior active version N to SUPERSEDED.
    """
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        article = await svc.publish_article(
            article_id=article_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
            version_label=payload.version_label,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleReasonRequiredError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


@router.post(
    "/articles/{article_id}/transition",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def transition_article(
    article_id: str,
    payload: ArticleTransitionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """Generic lifecycle state machine transition endpoint."""
    svc = create_article_service(session)
    article = await svc.get_article_by_id(article_id)
    if not article:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Article not found"
        )

    actor = current_user_id.get()
    try:
        article = await svc.transition(
            article=article,
            target_status=payload.target_status,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
            version_label=payload.version_label,
        )
    except ArticleReasonRequiredError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT, detail=str(exc)
        ) from exc
    except ArticleApprovalConflictError as exc:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN, detail=str(exc)
        ) from exc
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


@router.get(
    "/articles/{article_id}/versions",
    response_model=list[ArticleVersionResponse],
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def list_article_versions(
    article_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[ArticleVersionResponse]:
    """Lists all immutable version snapshots for an article (chronological order)."""
    svc = create_article_service(session)
    versions = await svc.list_article_versions(article_id)
    return [ArticleVersionResponse.model_validate(v) for v in versions]


@router.get(
    "/articles/{article_id}/audit-log",
    response_model=list[AuditLogResponse],
    dependencies=[Depends(require_roles(*AUDITOR_ROLES))],
)
async def get_article_audit_log(
    article_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[AuditLogResponse]:
    """Returns the immutable audit trail for a specific article."""
    result = await session.execute(
        select(KnowledgeArticleAuditLog)
        .where(KnowledgeArticleAuditLog.article_id == article_id)
        .order_by(KnowledgeArticleAuditLog.created_at.asc())
    )
    return [AuditLogResponse.model_validate(log) for log in result.scalars().all()]


# ---------------------------------------------------------------------------
# Contextual Help Admin Management & Dynamic Resolution Endpoints (Issue #4328)
# ---------------------------------------------------------------------------


@router.post(
    "/contextual-help",
    response_model=ContextualHelpMappingResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
@router.post(
    "/contextual-help/mappings",
    response_model=ContextualHelpMappingResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def create_contextual_help_mapping(
    payload: ContextualHelpMappingCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpMappingResponse:
    """Creates a new contextual help route mapping. Requires super_admin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        mapping = await svc.create_help_mapping(
            route_pattern=payload.route_pattern,
            persona=payload.persona,
            article_id=payload.article_id,
            priority=payload.priority,
            section_anchor=payload.section_anchor,
            is_active=payload.is_active,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST, detail=str(exc)
        ) from exc
    return ContextualHelpMappingResponse.model_validate(mapping)


@router.get(
    "/contextual-help/mappings",
    response_model=list[ContextualHelpMappingResponse],
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def list_contextual_help_mappings(
    route_pattern: str | None = None,
    persona: str | None = None,
    is_active: bool | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ContextualHelpMappingResponse]:
    """Lists all contextual help mappings with optional filters. Requires super_admin role."""
    svc = create_article_service(session)
    mappings = await svc.list_help_mappings(
        route_pattern=route_pattern,
        persona=persona,
        is_active=is_active,
    )
    return [ContextualHelpMappingResponse.model_validate(m) for m in mappings]


@router.get(
    "/contextual-help/mappings/{mapping_id}",
    response_model=ContextualHelpMappingResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def get_contextual_help_mapping(
    mapping_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpMappingResponse:
    """Retrieves a single contextual help mapping by ID. Requires super_admin role."""
    svc = create_article_service(session)
    mapping = await svc.get_help_mapping_by_id(mapping_id)
    if not mapping:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"ContextualHelpMapping with id {mapping_id!r} not found",
        )
    return ContextualHelpMappingResponse.model_validate(mapping)


@router.put(
    "/contextual-help/mappings/{mapping_id}",
    response_model=ContextualHelpMappingResponse,
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def update_contextual_help_mapping(
    mapping_id: str,
    payload: ContextualHelpMappingUpdate,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpMappingResponse:
    """Updates an existing contextual help mapping. Requires super_admin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        mapping = await svc.update_help_mapping(
            mapping_id=mapping_id,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
            route_pattern=payload.route_pattern,
            persona=payload.persona,
            article_id=payload.article_id,
            priority=payload.priority,
            section_anchor=payload.section_anchor,
            is_active=payload.is_active,
        )
    except ContextualHelpMappingNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    except ArticleNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return ContextualHelpMappingResponse.model_validate(mapping)


@router.delete(
    "/contextual-help/mappings/{mapping_id}",
    response_model=dict[str, Any],
    dependencies=[Depends(require_roles(*ADMIN_ROLES))],
)
async def delete_contextual_help_mapping(
    mapping_id: str,
    reason_for_change: str = "Contextual help mapping deleted",
    session: AsyncSession = Depends(get_db_session),
) -> dict[str, Any]:
    """Deletes a contextual help mapping by ID. Requires super_admin role."""
    actor = current_user_id.get()
    svc = create_article_service(session)
    try:
        deleted = await svc.delete_help_mapping(
            mapping_id=mapping_id,
            actor_user_id=actor,
            reason_for_change=reason_for_change,
        )
    except ContextualHelpMappingNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail=str(exc)
        ) from exc
    return {"success": deleted, "id": mapping_id}


@router.get(
    "/contextual-help",
    response_model=ContextualHelpResolutionResponse,
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
@router.get(
    "/contextual-help/lookup",
    response_model=ContextualHelpResolutionResponse,
    dependencies=[Depends(require_roles(*ALL_ROLES))],
)
async def resolve_contextual_help(
    route: str,
    persona: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpResolutionResponse:
    """
    Dynamically resolves in-page contextual SOP guidance and relevant articles
    for a given clinical screen route (e.g. '/ecrf/site-101/subjects') and persona role.

    Resolution algorithm:
    - Matches exact routes, parameterized routes, prefix wildcards, and global catch-alls.
    - Resolves via hierarchical specificity: priority ASC, persona match, pattern specificity, length, and recency.
    - Surfaces 1 primary spotlight article + up to 3 secondary related guides.
    """
    svc = create_article_service(session)
    result = await svc.resolve_contextual_help(route=route, persona=persona)

    if not result.primary_article:
        return ContextualHelpResolutionResponse(
            matched_mapping=None,
            primary_article=None,
            primary_version=None,
            section_anchor=None,
            related_articles=[],
            article=None,
            version=None,
        )

    # Prepare primary article response with body content attached
    primary_art_resp = ArticleResponse.model_validate(result.primary_article)
    if result.primary_version:
        primary_art_resp.body_markdown = result.primary_version.body_markdown
        primary_art_resp.body_html = result.primary_version.body_html

    primary_ver_resp = (
        ArticleVersionResponse.model_validate(result.primary_version)
        if result.primary_version
        else None
    )

    mapping_resp = (
        ContextualHelpMappingResponse.model_validate(result.matched_mapping)
        if result.matched_mapping
        else None
    )

    # Prepare related articles response
    related_resp: list[ArticleResponse] = []
    for rel_art, rel_ver in result.related_articles:
        rel_art_resp = ArticleResponse.model_validate(rel_art)
        if rel_ver:
            rel_art_resp.body_markdown = rel_ver.body_markdown
            rel_art_resp.body_html = rel_ver.body_html
        related_resp.append(rel_art_resp)

    return ContextualHelpResolutionResponse(
        matched_mapping=mapping_resp,
        primary_article=primary_art_resp,
        primary_version=primary_ver_resp,
        section_anchor=result.section_anchor,
        related_articles=related_resp,
        article=primary_art_resp,
        version=primary_ver_resp,
    )


__all__ = ["router"]
