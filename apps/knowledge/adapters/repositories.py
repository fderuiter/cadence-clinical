"""
SQLAlchemy repository adapters for the Knowledge & Support Hub microservice.

Implements concrete database operations adhering to domain repository ports.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

from typing import TYPE_CHECKING

from sqlalchemy import func, literal_column, or_, select
from sqlalchemy.ext.asyncio import AsyncSession

if TYPE_CHECKING:
    from apps.knowledge.application.article_service import ArticleLifecycleService

from apps.knowledge.domain.models import ArticleStatus
from apps.knowledge.domain.search import (
    calculate_lexical_score,
    extract_query_terms,
    generate_highlighted_snippet,
)
from apps.knowledge.infrastructure.models import (
    ContextualHelpMapping,
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
    KnowledgeCategory,
)
from apps.knowledge.ports.repository_port import (
    ArticleSearchResultItem,
    ContextualHelpMappingRepositoryPort,
    KnowledgeArticleRepositoryPort,
    KnowledgeAuditLogRepositoryPort,
    KnowledgeCategoryRepositoryPort,
)


class SQLAlchemyKnowledgeArticleRepository(KnowledgeArticleRepositoryPort):
    """SQLAlchemy implementation of the KnowledgeArticleRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_id(self, entity_id: str) -> KnowledgeArticle | None:
        """Retrieve active knowledge article by ID."""
        result = await self._session.execute(
            select(KnowledgeArticle).where(
                KnowledgeArticle.id == entity_id,
                KnowledgeArticle.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def get_by_slug(self, slug: str) -> KnowledgeArticle | None:
        """Retrieve active knowledge article by unique slug."""
        result = await self._session.execute(
            select(KnowledgeArticle).where(
                KnowledgeArticle.slug == slug,
                KnowledgeArticle.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def list_articles(
        self,
        status: ArticleStatus | None = None,
        category_id: str | None = None,
    ) -> list[KnowledgeArticle]:
        """List active knowledge articles with optional status and category filters."""
        stmt = select(KnowledgeArticle).where(KnowledgeArticle.is_deleted.is_(False))
        if status is not None:
            stmt = stmt.where(KnowledgeArticle.status == status.value)
        if category_id is not None:
            stmt = stmt.where(KnowledgeArticle.category_id == category_id)
        stmt = stmt.order_by(KnowledgeArticle.created_at.desc())
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def save(self, entity: KnowledgeArticle) -> KnowledgeArticle:
        """Persist or update knowledge article."""
        self._session.add(entity)
        await self._session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        """Soft-delete knowledge article."""
        article = await self.get_by_id(entity_id)
        if not article:
            return False
        article.is_deleted = True
        await self._session.flush()
        return True

    async def get_working_draft_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve the working draft version for an article if one exists."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion)
            .where(
                KnowledgeArticleVersion.article_id == article_id,
                KnowledgeArticleVersion.status_at_snapshot == ArticleStatus.DRAFT.value,
                KnowledgeArticleVersion.is_locked.is_(False),
            )
            .order_by(KnowledgeArticleVersion.version_index.desc())
            .limit(1)
        )
        return result.scalar_one_or_none()

    async def get_version_by_id(
        self, version_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve a specific article version by its unique ID."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion).where(
                KnowledgeArticleVersion.id == version_id
            )
        )
        return result.scalar_one_or_none()

    async def get_version_by_index(
        self, article_id: str, version_index: int
    ) -> KnowledgeArticleVersion | None:
        """Retrieve a specific article version by version index."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion).where(
                KnowledgeArticleVersion.article_id == article_id,
                KnowledgeArticleVersion.version_index == version_index,
            )
        )
        return result.scalar_one_or_none()

    async def get_latest_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve the latest version snapshot for an article."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion)
            .where(KnowledgeArticleVersion.article_id == article_id)
            .order_by(KnowledgeArticleVersion.version_index.desc())
            .limit(1)
        )
        return result.scalar_one_or_none()

    async def list_versions(self, article_id: str) -> list[KnowledgeArticleVersion]:
        """List all version snapshots for an article, ordered by version index."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion)
            .where(KnowledgeArticleVersion.article_id == article_id)
            .order_by(KnowledgeArticleVersion.version_index.asc())
        )
        return list(result.scalars().all())

    async def save_version(
        self, version: KnowledgeArticleVersion
    ) -> KnowledgeArticleVersion:
        """Persist or update an article version snapshot."""
        self._session.add(version)
        await self._session.flush()
        return version

    def _get_dialect_name(self) -> str:
        """Determines the current active database dialect name."""
        if self._session.bind is not None:
            return self._session.bind.dialect.name
        try:
            bind = self._session.get_bind()
            if bind is not None:
                return bind.dialect.name
        except Exception:
            pass
        return "sqlite"

    async def search_published_articles(
        self,
        query: str,
        category_id: str | None = None,
        limit: int = 50,
    ) -> list[ArticleSearchResultItem]:
        """
        Searches active published knowledge articles across titles, tags, and bodies.

        Supports multi-dialect execution:
        - PostgreSQL: Uses native tsvector full-text search with plainto_tsquery,
          ts_rank weighting (Title A, Tags B, Body C), and ts_headline snippets.
        - SQLite / Fallback: Executes term-matching queries with lexical scoring and
          pure-function highlighted snippet extraction.
        """
        norm_query = query.strip() if query else ""
        if not norm_query:
            return []

        dialect = self._get_dialect_name()
        if dialect == "postgresql":
            return await self._search_postgresql(
                query=norm_query,
                category_id=category_id,
                limit=limit,
            )
        return await self._search_sqlite(
            query=norm_query,
            category_id=category_id,
            limit=limit,
        )

    async def _search_postgresql(
        self,
        query: str,
        category_id: str | None = None,
        limit: int = 50,
    ) -> list[ArticleSearchResultItem]:
        """PostgreSQL-specific full-text search execution using TSVECTOR and ts_rank."""
        ts_query = func.plainto_tsquery("english", query)
        title_vec = func.setweight(
            func.to_tsvector("english", func.coalesce(KnowledgeArticle.title, "")),
            "A",
        )
        tags_vec = func.setweight(
            func.to_tsvector("english", func.coalesce(KnowledgeArticle.tags, "")),
            "B",
        )
        body_vec = func.setweight(
            func.to_tsvector(
                "english", func.coalesce(KnowledgeArticleVersion.body_markdown, "")
            ),
            "C",
        )
        full_vec = title_vec.concat(tags_vec).concat(body_vec)

        stmt = (
            select(
                KnowledgeArticle.id,
                KnowledgeArticle.slug,
                KnowledgeArticle.title,
                KnowledgeArticle.category_id,
                KnowledgeCategory.name.label("category_name"),
                KnowledgeCategory.persona_visibility.label(
                    "category_persona_visibility"
                ),
                KnowledgeArticle.tags,
                KnowledgeArticle.version_label,
                KnowledgeArticle.version_index,
                KnowledgeArticle.created_at,
                KnowledgeArticleVersion.body_markdown,
                func.ts_rank(full_vec, ts_query).label("rank_score"),
                func.ts_headline(
                    "english",
                    KnowledgeArticleVersion.body_markdown,
                    ts_query,
                    "StartSel=<mark>, StopSel=</mark>, MaxWords=35, MinWords=15, HighlightAll=FALSE",
                ).label("headline"),
            )
            .join(
                KnowledgeArticleVersion,
                KnowledgeArticle.current_published_version_id
                == KnowledgeArticleVersion.id,
            )
            .join(
                KnowledgeCategory,
                KnowledgeArticle.category_id == KnowledgeCategory.id,
            )
            .where(
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
                KnowledgeArticle.is_deleted.is_(False),
                KnowledgeCategory.is_deleted.is_(False),
                full_vec.op("@@")(ts_query),
            )
        )

        if category_id:
            stmt = stmt.where(KnowledgeArticle.category_id == category_id)

        stmt = stmt.order_by(literal_column("rank_score").desc()).limit(limit)
        result = await self._session.execute(stmt)
        rows = result.all()

        results: list[ArticleSearchResultItem] = []
        for row in rows:
            snippet = row.headline
            if not snippet or not snippet.strip():
                snippet = generate_highlighted_snippet(
                    text=row.body_markdown,
                    query=query,
                    max_words=35,
                )

            results.append(
                ArticleSearchResultItem(
                    id=row.id,
                    slug=row.slug,
                    title=row.title,
                    category_id=row.category_id,
                    category_name=row.category_name,
                    category_persona_visibility=row.category_persona_visibility,
                    tags=row.tags,
                    version_label=row.version_label,
                    version_index=row.version_index,
                    snippet=snippet,
                    relevance_score=round(float(row.rank_score), 4),
                    created_at=row.created_at,
                )
            )
        return results

    async def _search_sqlite(
        self,
        query: str,
        category_id: str | None = None,
        limit: int = 50,
    ) -> list[ArticleSearchResultItem]:
        """SQLite test harness fallback search execution with lexical scoring and snippets."""
        terms = extract_query_terms(query)
        if not terms:
            return []

        stmt = (
            select(
                KnowledgeArticle.id,
                KnowledgeArticle.slug,
                KnowledgeArticle.title,
                KnowledgeArticle.category_id,
                KnowledgeCategory.name.label("category_name"),
                KnowledgeCategory.persona_visibility.label(
                    "category_persona_visibility"
                ),
                KnowledgeArticle.tags,
                KnowledgeArticle.version_label,
                KnowledgeArticle.version_index,
                KnowledgeArticle.created_at,
                KnowledgeArticleVersion.body_markdown,
            )
            .join(
                KnowledgeArticleVersion,
                KnowledgeArticle.current_published_version_id
                == KnowledgeArticleVersion.id,
            )
            .join(
                KnowledgeCategory,
                KnowledgeArticle.category_id == KnowledgeCategory.id,
            )
            .where(
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
                KnowledgeArticle.is_deleted.is_(False),
                KnowledgeCategory.is_deleted.is_(False),
            )
        )

        if category_id:
            stmt = stmt.where(KnowledgeArticle.category_id == category_id)

        # Match across title, tags, or markdown body
        match_clauses = []
        for term in terms:
            pattern = f"%{term}%"
            match_clauses.append(KnowledgeArticle.title.ilike(pattern))
            match_clauses.append(KnowledgeArticle.tags.ilike(pattern))
            match_clauses.append(KnowledgeArticleVersion.body_markdown.ilike(pattern))

        if match_clauses:
            stmt = stmt.where(or_(*match_clauses))

        result = await self._session.execute(stmt)
        rows = result.all()

        scored_items: list[ArticleSearchResultItem] = []
        for row in rows:
            score = calculate_lexical_score(
                title=row.title,
                tags=row.tags,
                body=row.body_markdown,
                query=query,
            )
            if score <= 0.0:
                continue

            snippet = generate_highlighted_snippet(
                text=row.body_markdown,
                query=query,
                max_words=35,
            )

            scored_items.append(
                ArticleSearchResultItem(
                    id=row.id,
                    slug=row.slug,
                    title=row.title,
                    category_id=row.category_id,
                    category_name=row.category_name,
                    category_persona_visibility=row.category_persona_visibility,
                    tags=row.tags,
                    version_label=row.version_label,
                    version_index=row.version_index,
                    snippet=snippet,
                    relevance_score=score,
                    created_at=row.created_at,
                )
            )

        scored_items.sort(key=lambda item: (-item.relevance_score, item.title.lower()))
        return scored_items[:limit]


class SQLAlchemyKnowledgeCategoryRepository(KnowledgeCategoryRepositoryPort):
    """SQLAlchemy implementation of the KnowledgeCategoryRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_id(self, entity_id: str) -> KnowledgeCategory | None:
        """Retrieve active knowledge category by ID."""
        result = await self._session.execute(
            select(KnowledgeCategory).where(
                KnowledgeCategory.id == entity_id,
                KnowledgeCategory.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def get_by_slug(self, slug: str) -> KnowledgeCategory | None:
        """Retrieve active knowledge category by unique slug."""
        result = await self._session.execute(
            select(KnowledgeCategory).where(
                KnowledgeCategory.slug == slug,
                KnowledgeCategory.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def list_categories(self) -> list[KnowledgeCategory]:
        """List all active knowledge categories."""
        result = await self._session.execute(
            select(KnowledgeCategory)
            .where(KnowledgeCategory.is_deleted.is_(False))
            .order_by(KnowledgeCategory.name.asc())
        )
        return list(result.scalars().all())

    async def save(self, entity: KnowledgeCategory) -> KnowledgeCategory:
        """Persist or update knowledge category."""
        self._session.add(entity)
        await self._session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        """Soft-delete knowledge category."""
        cat = await self.get_by_id(entity_id)
        if not cat:
            return False
        cat.is_deleted = True
        await self._session.flush()
        return True


class SQLAlchemyKnowledgeAuditLogRepository(KnowledgeAuditLogRepositoryPort):
    """SQLAlchemy implementation of the KnowledgeAuditLogRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_id(self, entity_id: str) -> KnowledgeArticleAuditLog | None:
        """Retrieve audit log entry by ID."""
        result = await self._session.execute(
            select(KnowledgeArticleAuditLog).where(
                KnowledgeArticleAuditLog.id == entity_id
            )
        )
        return result.scalar_one_or_none()

    async def append_log(
        self, log_entry: KnowledgeArticleAuditLog
    ) -> KnowledgeArticleAuditLog:
        """Append an immutable audit log entry."""
        self._session.add(log_entry)
        await self._session.flush()
        return log_entry

    async def save(self, entity: KnowledgeArticleAuditLog) -> KnowledgeArticleAuditLog:
        """Save an audit log entry."""
        return await self.append_log(entity)

    async def list_by_article(self, article_id: str) -> list[KnowledgeArticleAuditLog]:
        """List audit logs for a specific article ordered by created_at timestamp."""
        result = await self._session.execute(
            select(KnowledgeArticleAuditLog)
            .where(KnowledgeArticleAuditLog.article_id == article_id)
            .order_by(KnowledgeArticleAuditLog.created_at.asc())
        )
        return list(result.scalars().all())


class SQLAlchemyContextualHelpMappingRepository(ContextualHelpMappingRepositoryPort):
    """SQLAlchemy implementation of the ContextualHelpMappingRepositoryPort."""

    def __init__(self, session: AsyncSession) -> None:
        self._session = session

    async def get_by_id(self, entity_id: str) -> ContextualHelpMapping | None:
        """Retrieve contextual help mapping by ID."""
        result = await self._session.execute(
            select(ContextualHelpMapping).where(ContextualHelpMapping.id == entity_id)
        )
        return result.scalar_one_or_none()

    async def list_by_route(self, route_pattern: str) -> list[ContextualHelpMapping]:
        """List active contextual help mappings matching an exact route pattern."""
        stmt = (
            select(ContextualHelpMapping)
            .join(
                KnowledgeArticle,
                ContextualHelpMapping.article_id == KnowledgeArticle.id,
            )
            .where(
                ContextualHelpMapping.route_pattern == route_pattern,
                ContextualHelpMapping.is_active.is_(True),
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
                KnowledgeArticle.is_deleted.is_(False),
            )
            .order_by(ContextualHelpMapping.priority.asc())
        )
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def list_mappings(
        self,
        route_pattern: str | None = None,
        persona: str | None = None,
        is_active: bool | None = None,
    ) -> list[ContextualHelpMapping]:
        """List contextual help mappings with optional filters."""
        stmt = select(ContextualHelpMapping)
        if route_pattern is not None:
            stmt = stmt.where(ContextualHelpMapping.route_pattern == route_pattern)
        if persona is not None:
            stmt = stmt.where(ContextualHelpMapping.persona == persona)
        if is_active is not None:
            stmt = stmt.where(ContextualHelpMapping.is_active.is_(is_active))
        stmt = stmt.order_by(
            ContextualHelpMapping.priority.asc(),
            ContextualHelpMapping.created_at.desc(),
        )
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def list_active_mappings(self) -> list[ContextualHelpMapping]:
        """List all active contextual help mappings linked to published articles."""
        stmt = (
            select(ContextualHelpMapping)
            .join(
                KnowledgeArticle,
                ContextualHelpMapping.article_id == KnowledgeArticle.id,
            )
            .where(
                ContextualHelpMapping.is_active.is_(True),
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
                KnowledgeArticle.is_deleted.is_(False),
            )
            .order_by(
                ContextualHelpMapping.priority.asc(),
                ContextualHelpMapping.created_at.desc(),
            )
        )
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def save(self, entity: ContextualHelpMapping) -> ContextualHelpMapping:
        """Persist or update contextual help mapping."""
        self._session.add(entity)
        await self._session.flush()
        return entity

    async def delete(self, entity_id: str) -> bool:
        """Delete contextual help mapping by ID."""
        mapping = await self.get_by_id(entity_id)
        if not mapping:
            return False
        await self._session.delete(mapping)
        await self._session.flush()
        return True


def create_article_service(session: AsyncSession) -> ArticleLifecycleService:
    """Factory helper to construct an ArticleLifecycleService wired to SQLAlchemy repositories."""
    from apps.knowledge.application.article_service import ArticleLifecycleService

    return ArticleLifecycleService(
        article_repo=SQLAlchemyKnowledgeArticleRepository(session),
        category_repo=SQLAlchemyKnowledgeCategoryRepository(session),
        audit_repo=SQLAlchemyKnowledgeAuditLogRepository(session),
        help_repo=SQLAlchemyContextualHelpMappingRepository(session),
    )


__all__ = [
    "SQLAlchemyContextualHelpMappingRepository",
    "SQLAlchemyKnowledgeArticleRepository",
    "SQLAlchemyKnowledgeAuditLogRepository",
    "SQLAlchemyKnowledgeCategoryRepository",
    "create_article_service",
]
