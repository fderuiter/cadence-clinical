"""
FastAPI router for Knowledge article lifecycle endpoints.

Implements the REST API for article CRUD and lifecycle transitions.
All routes are protected by GatewayAuthMiddleware.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002
"""

import logging

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.database import get_db_session
from apps.knowledge.domain.models import (
    ArticleApprovalConflictError,
    ArticleReasonRequiredError,
    ArticleStatus,
    ArticleTransitionError,
)
from apps.knowledge.infrastructure.models import (
    ContextualHelpMapping,
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
    KnowledgeCategory,
)
from apps.knowledge.presentation.dtos import (
    ArticleCreate,
    ArticleDraftSave,
    ArticleResponse,
    ArticleTransitionRequest,
    ArticleVersionResponse,
    AuditLogResponse,
    CategoryCreate,
    CategoryResponse,
    ContextualHelpLookupResponse,
    ContextualHelpMappingCreate,
    ContextualHelpMappingResponse,
)
from apps.knowledge.services.article_service import ArticleLifecycleService
from packages.security.context import current_user_id
from packages.security.rbac import require_roles

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
    dependencies=[Depends(require_roles(ADMIN_ROLES))],
)
async def create_category(
    payload: CategoryCreate,
    session: AsyncSession = Depends(get_db_session),
) -> CategoryResponse:
    """
    Creates a new knowledge article category. Requires super_admin role.

    Args:
        payload: Category creation data.
        session: Injected async database session.

    Returns:
        The created CategoryResponse.

    Raises:
        HTTPException 409: If the category name or slug already exists.
    """
    actor = current_user_id.get()
    svc = ArticleLifecycleService(session)
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
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return CategoryResponse.model_validate(category)


@router.get(
    "/categories",
    response_model=list[CategoryResponse],
    dependencies=[Depends(require_roles(ALL_ROLES))],
)
async def list_categories(
    session: AsyncSession = Depends(get_db_session),
) -> list[CategoryResponse]:
    """
    Lists all active knowledge categories.

    Returns:
        List of CategoryResponse objects.
    """
    result = await session.execute(
        select(KnowledgeCategory).where(KnowledgeCategory.is_deleted.is_(False))
    )
    return [CategoryResponse.model_validate(c) for c in result.scalars().all()]


# ---------------------------------------------------------------------------
# Article endpoints
# ---------------------------------------------------------------------------


@router.post(
    "/articles",
    response_model=ArticleResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(ADMIN_ROLES))],
)
async def create_article(
    payload: ArticleCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Creates a new KnowledgeArticle in DRAFT status. Requires super_admin role.

    Args:
        payload: Article creation data including initial body_markdown.
        session: Injected async database session.

    Returns:
        The created ArticleResponse.

    Raises:
        HTTPException 409: If the article slug is already in use.
    """
    actor = current_user_id.get()
    svc = ArticleLifecycleService(session)
    try:
        article = await svc.create_article(
            title=payload.title,
            slug=payload.slug,
            category_id=payload.category_id,
            body_markdown=payload.body_markdown,
            version_label=payload.version_label,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleResponse.model_validate(article)


@router.get(
    "/articles",
    response_model=list[ArticleResponse],
    dependencies=[Depends(require_roles(ALL_ROLES))],
)
async def list_articles(
    status_filter: ArticleStatus | None = None,
    category_id: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> list[ArticleResponse]:
    """
    Lists knowledge articles, optionally filtered by status and/or category.

    Non-admin users only receive PUBLISHED articles. Admins receive all.

    Args:
        status_filter: Optional ArticleStatus to filter by.
        category_id: Optional category UUID to filter by.
        session: Injected async database session.

    Returns:
        List of ArticleResponse objects.
    """
    stmt = select(KnowledgeArticle).where(KnowledgeArticle.is_deleted.is_(False))

    if status_filter:
        stmt = stmt.where(KnowledgeArticle.status == status_filter.value)
    if category_id:
        stmt = stmt.where(KnowledgeArticle.category_id == category_id)

    result = await session.execute(stmt)
    return [ArticleResponse.model_validate(a) for a in result.scalars().all()]


@router.get(
    "/articles/{article_id}",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(ALL_ROLES))],
)
async def get_article(
    article_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Retrieves a single KnowledgeArticle by ID.

    If the requesting user has an auditor role, records a READ_BY_AUDITOR audit event.

    Args:
        article_id: UUID of the article to retrieve.
        session: Injected async database session.

    Returns:
        The ArticleResponse.

    Raises:
        HTTPException 404: If the article does not exist.
    """
    from packages.security.context import current_user_id as uid_ctx
    from packages.security.rbac import get_current_user_roles

    result = await session.execute(
        select(KnowledgeArticle).where(
            KnowledgeArticle.id == article_id,
            KnowledgeArticle.is_deleted.is_(False),
        )
    )
    article = result.scalar_one_or_none()
    if not article:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Article not found"
        )

    # Emit auditor read event
    actor = uid_ctx.get()
    try:
        roles = get_current_user_roles()
        if any(r in roles for r in AUDITOR_ROLES):
            svc = ArticleLifecycleService(session)
            await svc.record_auditor_read(article=article, actor_user_id=actor)
    except Exception:
        pass  # Never block a read due to audit log failure

    return ArticleResponse.model_validate(article)


@router.patch(
    "/articles/{article_id}/draft",
    response_model=ArticleVersionResponse,
    dependencies=[Depends(require_roles(ADMIN_ROLES))],
)
async def save_article_draft(
    article_id: str,
    payload: ArticleDraftSave,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleVersionResponse:
    """
    Saves updated body content to a DRAFT article. Requires super_admin role.

    Args:
        article_id: UUID of the article to update.
        payload: Draft save payload with body_markdown.
        session: Injected async database session.

    Returns:
        The new ArticleVersionResponse snapshot.

    Raises:
        HTTPException 404: Article not found.
        HTTPException 409: Article is not in DRAFT status.
    """
    result = await session.execute(
        select(KnowledgeArticle).where(KnowledgeArticle.id == article_id)
    )
    article = result.scalar_one_or_none()
    if not article:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Article not found"
        )

    actor = current_user_id.get()
    svc = ArticleLifecycleService(session)
    try:
        version = await svc.save_draft(
            article=article,
            body_markdown=payload.body_markdown,
            actor_user_id=actor,
            reason_for_change=payload.reason_for_change,
        )
    except ArticleTransitionError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT, detail=str(exc)
        ) from exc
    return ArticleVersionResponse.model_validate(version)


@router.post(
    "/articles/{article_id}/transition",
    response_model=ArticleResponse,
    dependencies=[Depends(require_roles(ADMIN_ROLES))],
)
async def transition_article(
    article_id: str,
    payload: ArticleTransitionRequest,
    session: AsyncSession = Depends(get_db_session),
) -> ArticleResponse:
    """
    Executes a lifecycle state machine transition on a KnowledgeArticle.
    Requires super_admin role.

    Args:
        article_id: UUID of the article to transition.
        payload: Transition request with target_status and reason_for_change.
        session: Injected async database session.

    Returns:
        The updated ArticleResponse.

    Raises:
        HTTPException 404: Article not found.
        HTTPException 409: Invalid transition, four-eyes violation.
        HTTPException 422: Missing reason_for_change on a regulated transition.
    """
    result = await session.execute(
        select(KnowledgeArticle).where(KnowledgeArticle.id == article_id)
    )
    article = result.scalar_one_or_none()
    if not article:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND, detail="Article not found"
        )

    actor = current_user_id.get()
    svc = ArticleLifecycleService(session)
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
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY, detail=str(exc)
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
    dependencies=[Depends(require_roles(ALL_ROLES))],
)
async def list_article_versions(
    article_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[ArticleVersionResponse]:
    """
    Lists all immutable version snapshots for an article (chronological order).

    Args:
        article_id: UUID of the article.
        session: Injected async database session.

    Returns:
        List of ArticleVersionResponse objects, oldest first.
    """
    result = await session.execute(
        select(KnowledgeArticleVersion)
        .where(KnowledgeArticleVersion.article_id == article_id)
        .order_by(KnowledgeArticleVersion.version_index.asc())
    )
    return [ArticleVersionResponse.model_validate(v) for v in result.scalars().all()]


@router.get(
    "/articles/{article_id}/audit-log",
    response_model=list[AuditLogResponse],
    dependencies=[Depends(require_roles(AUDITOR_ROLES))],
)
async def get_article_audit_log(
    article_id: str,
    session: AsyncSession = Depends(get_db_session),
) -> list[AuditLogResponse]:
    """
    Returns the immutable audit trail for a specific article.
    Requires auditor or super_admin role.

    Args:
        article_id: UUID of the article.
        session: Injected async database session.

    Returns:
        List of AuditLogResponse objects, oldest first.
    """
    result = await session.execute(
        select(KnowledgeArticleAuditLog)
        .where(KnowledgeArticleAuditLog.article_id == article_id)
        .order_by(KnowledgeArticleAuditLog.created_at.asc())
    )
    return [AuditLogResponse.model_validate(log) for log in result.scalars().all()]


# ---------------------------------------------------------------------------
# Contextual help endpoints
# ---------------------------------------------------------------------------


@router.post(
    "/contextual-help",
    response_model=ContextualHelpMappingResponse,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_roles(ADMIN_ROLES))],
)
async def create_contextual_help_mapping(
    payload: ContextualHelpMappingCreate,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpMappingResponse:
    """
    Creates a contextual help mapping (route pattern + persona -> article).
    Requires super_admin role.

    Args:
        payload: Mapping creation payload.
        session: Injected async database session.

    Returns:
        The created ContextualHelpMappingResponse.
    """
    actor = current_user_id.get()
    mapping = ContextualHelpMapping(
        route_pattern=payload.route_pattern,
        persona=payload.persona,
        article_id=payload.article_id,
        priority=payload.priority,
        created_by=actor,
        reason_for_change=payload.reason_for_change,
    )
    session.add(mapping)
    await session.flush()
    return ContextualHelpMappingResponse.model_validate(mapping)


@router.get(
    "/contextual-help/lookup",
    response_model=ContextualHelpLookupResponse,
    dependencies=[Depends(require_roles(ALL_ROLES))],
)
async def lookup_contextual_help(
    route: str,
    persona: str | None = None,
    session: AsyncSession = Depends(get_db_session),
) -> ContextualHelpLookupResponse:
    """
    Returns the most relevant Published article for a given route and persona.

    Matches the highest-priority mapping (lowest priority number) for the route
    pattern that also matches the requesting persona (or has no persona filter).

    Args:
        route: The frontend route path (e.g. "/ecrf", "/tickets").
        persona: The requesting user's persona role, or None.
        session: Injected async database session.

    Returns:
        ContextualHelpLookupResponse with the matching article and current version,
        or null fields if no match is found.
    """
    stmt = (
        select(ContextualHelpMapping)
        .join(KnowledgeArticle, ContextualHelpMapping.article_id == KnowledgeArticle.id)
        .where(
            ContextualHelpMapping.route_pattern == route,
            KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
            KnowledgeArticle.is_deleted.is_(False),
        )
        .order_by(ContextualHelpMapping.priority.asc())
    )
    result = await session.execute(stmt)
    mappings = result.scalars().all()

    # Find the best match for the persona
    best: ContextualHelpMapping | None = None
    for m in mappings:
        if m.persona is None or m.persona == persona:
            best = m
            break

    if not best:
        return ContextualHelpLookupResponse(article=None, version=None)

    art_result = await session.execute(
        select(KnowledgeArticle).where(KnowledgeArticle.id == best.article_id)
    )
    article = art_result.scalar_one_or_none()
    if not article:
        return ContextualHelpLookupResponse(article=None, version=None)

    ver_result = await session.execute(
        select(KnowledgeArticleVersion)
        .where(
            KnowledgeArticleVersion.article_id == article.id,
            KnowledgeArticleVersion.status_at_snapshot == ArticleStatus.APPROVED.value,
        )
        .order_by(KnowledgeArticleVersion.version_index.desc())
        .limit(1)
    )
    version = ver_result.scalar_one_or_none()

    return ContextualHelpLookupResponse(
        article=ArticleResponse.model_validate(article) if article else None,
        version=ArticleVersionResponse.model_validate(version) if version else None,
    )
