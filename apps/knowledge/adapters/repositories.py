"""
SQLAlchemy repository adapters for the Knowledge & Support Hub microservice.

Implements concrete database operations adhering to domain repository ports.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.domain.models import ArticleStatus
from apps.knowledge.infrastructure.models import (
    ContextualHelpMapping,
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
    KnowledgeCategory,
)
from apps.knowledge.ports.repository_port import (
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
        """List active contextual help mappings matching a route pattern."""
        stmt = (
            select(ContextualHelpMapping)
            .join(
                KnowledgeArticle,
                ContextualHelpMapping.article_id == KnowledgeArticle.id,
            )
            .where(
                ContextualHelpMapping.route_pattern == route_pattern,
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
                KnowledgeArticle.is_deleted.is_(False),
            )
            .order_by(ContextualHelpMapping.priority.asc())
        )
        result = await self._session.execute(stmt)
        return list(result.scalars().all())

    async def save(self, entity: ContextualHelpMapping) -> ContextualHelpMapping:
        """Persist or update contextual help mapping."""
        self._session.add(entity)
        await self._session.flush()
        return entity


__all__ = [
    "SQLAlchemyContextualHelpMappingRepository",
    "SQLAlchemyKnowledgeArticleRepository",
    "SQLAlchemyKnowledgeAuditLogRepository",
    "SQLAlchemyKnowledgeCategoryRepository",
]
