"""
Repository ports for Knowledge & Support Hub domain entities.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

from abc import ABC, abstractmethod

from apps.knowledge.domain.models import ArticleStatus
from apps.knowledge.infrastructure.models import (
    ContextualHelpMapping,
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
    KnowledgeCategory,
)
from packages.hexagonal import RepositoryPort


class KnowledgeArticleRepositoryPort(RepositoryPort[KnowledgeArticle], ABC):
    """Port for persisting and querying KnowledgeArticle domain entities and version snapshots."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> KnowledgeArticle | None:
        """Retrieve active knowledge article by ID."""
        pass

    @abstractmethod
    async def get_by_slug(self, slug: str) -> KnowledgeArticle | None:
        """Retrieve active knowledge article by unique slug."""
        pass

    @abstractmethod
    async def list_articles(
        self,
        status: ArticleStatus | None = None,
        category_id: str | None = None,
    ) -> list[KnowledgeArticle]:
        """List active knowledge articles with optional status and category filters."""
        pass

    @abstractmethod
    async def save(self, entity: KnowledgeArticle) -> KnowledgeArticle:
        """Persist or update knowledge article."""
        pass

    @abstractmethod
    async def delete(self, entity_id: str) -> bool:
        """Soft-delete knowledge article."""
        pass

    @abstractmethod
    async def get_working_draft_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve the working draft version for an article if one exists."""
        pass

    @abstractmethod
    async def get_version_by_id(
        self, version_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve a specific article version by its unique ID."""
        pass

    @abstractmethod
    async def get_version_by_index(
        self, article_id: str, version_index: int
    ) -> KnowledgeArticleVersion | None:
        """Retrieve a specific article version by version index."""
        pass

    @abstractmethod
    async def get_latest_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieve the latest version snapshot for an article."""
        pass

    @abstractmethod
    async def list_versions(self, article_id: str) -> list[KnowledgeArticleVersion]:
        """List all version snapshots for an article, ordered by version index."""
        pass

    @abstractmethod
    async def save_version(
        self, version: KnowledgeArticleVersion
    ) -> KnowledgeArticleVersion:
        """Persist or update an article version snapshot."""
        pass


class KnowledgeCategoryRepositoryPort(RepositoryPort[KnowledgeCategory], ABC):
    """Port for persisting and querying KnowledgeCategory domain entities."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> KnowledgeCategory | None:
        """Retrieve active knowledge category by ID."""
        pass

    @abstractmethod
    async def get_by_slug(self, slug: str) -> KnowledgeCategory | None:
        """Retrieve active knowledge category by unique slug."""
        pass

    @abstractmethod
    async def list_categories(self) -> list[KnowledgeCategory]:
        """List all active knowledge categories."""
        pass

    @abstractmethod
    async def save(self, entity: KnowledgeCategory) -> KnowledgeCategory:
        """Persist or update knowledge category."""
        pass

    @abstractmethod
    async def delete(self, entity_id: str) -> bool:
        """Soft-delete knowledge category."""
        pass


class KnowledgeAuditLogRepositoryPort(RepositoryPort[KnowledgeArticleAuditLog], ABC):
    """Port for appending and querying immutable KnowledgeArticleAuditLog records."""

    @abstractmethod
    async def append_log(
        self, log_entry: KnowledgeArticleAuditLog
    ) -> KnowledgeArticleAuditLog:
        """Append an immutable audit log entry."""
        pass

    @abstractmethod
    async def list_by_article(self, article_id: str) -> list[KnowledgeArticleAuditLog]:
        """List audit logs for a specific article ordered by created_at timestamp."""
        pass


class ContextualHelpMappingRepositoryPort(RepositoryPort[ContextualHelpMapping], ABC):
    """Port for persisting and querying ContextualHelpMapping records."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> ContextualHelpMapping | None:
        """Retrieve contextual help mapping by ID."""
        pass

    @abstractmethod
    async def list_by_route(self, route_pattern: str) -> list[ContextualHelpMapping]:
        """List active contextual help mappings matching a route pattern."""
        pass

    @abstractmethod
    async def save(self, entity: ContextualHelpMapping) -> ContextualHelpMapping:
        """Persist or update contextual help mapping."""
        pass


__all__ = [
    "ContextualHelpMapping",
    "ContextualHelpMappingRepositoryPort",
    "KnowledgeArticle",
    "KnowledgeArticleAuditLog",
    "KnowledgeArticleRepositoryPort",
    "KnowledgeArticleVersion",
    "KnowledgeAuditLogRepositoryPort",
    "KnowledgeCategory",
    "KnowledgeCategoryRepositoryPort",
]
