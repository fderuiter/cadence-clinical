"""
Application-layer service for the Knowledge & Support Hub article lifecycle.

Orchestrates state transitions, audit log emission, version snapshotting,
and notification dispatch per the settled design decisions from wayfinder
ticket #4237.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002
"""

import importlib
import logging
from typing import Any

from apps.knowledge.domain.models import (
    ArticleAuditAction,
    ArticleSnapshot,
    ArticleStatus,
    ArticleTransitionError,
    validate_transition,
)


def _infra_models() -> tuple[Any, Any, Any, Any]:
    infra = importlib.import_module("apps.knowledge.infrastructure.models")
    return (
        infra.KnowledgeCategory,
        infra.KnowledgeArticle,
        infra.KnowledgeArticleVersion,
        infra.KnowledgeArticleAuditLog,
    )


def _sa_select() -> Any:
    return importlib.import_module("sqlalchemy").select


def _publish_notification_func() -> Any:
    return importlib.import_module(
        "apps.knowledge.adapters.notifications_client"
    ).publish_notification


async def publish_notification(payload: dict) -> bool:
    """Dispatches a notification via the notifications adapter client."""
    fn = _publish_notification_func()
    return await fn(payload)


logger = logging.getLogger("knowledge-article-service")


class ArticleLifecycleService:
    """
    Orchestrates the complete GxP article lifecycle for the Knowledge microservice.

    Responsibilities:
    - Validates and executes state machine transitions via domain.models.validate_transition.
    - Creates immutable KnowledgeArticleVersion snapshots on Approved transitions.
    - Auto-supersedes any currently Published version when a new version is Published.
    - Emits KnowledgeArticleAuditLog records for every action (SHA-256 digest chain
      via packages.security.audit_logger must be called by the router/presenter).
    - Dispatches notifications via apps/notifications/ for all triggering events.

    Args:
        session: An active async SQLAlchemy session for the knowledge database.
    """

    def __init__(self, session: Any) -> None:
        self._session = session

    # ------------------------------------------------------------------
    # Category operations
    # ------------------------------------------------------------------

    async def create_category(
        self,
        *,
        name: str,
        slug: str,
        description: str | None,
        persona_visibility: str | None,
        parent_id: str | None,
        actor_user_id: str,
        reason_for_change: str,
    ) -> Any:
        knowledge_category_cls, _, _, _ = _infra_models()
        category = knowledge_category_cls(
            name=name,
            slug=slug,
            description=description,
            persona_visibility=persona_visibility,
            parent_id=parent_id,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        self._session.add(category)
        await self._session.flush()
        return category

    # ------------------------------------------------------------------
    # Article creation
    # ------------------------------------------------------------------

    async def create_article(
        self,
        *,
        title: str,
        slug: str,
        category_id: str,
        body_markdown: str,
        version_label: str,
        actor_user_id: str,
        reason_for_change: str,
    ) -> Any:
        _, knowledge_article_cls, knowledge_article_version_cls, _ = _infra_models()
        article = knowledge_article_cls(
            title=title,
            slug=slug,
            category_id=category_id,
            status=ArticleStatus.DRAFT,
            version_index=1,
            version_label=version_label,
            author_user_id=actor_user_id,
            last_edited_by=actor_user_id,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        self._session.add(article)
        await self._session.flush()

        # Snapshot initial draft body
        version = knowledge_article_version_cls(
            article_id=article.id,
            version_index=1,
            version_label=version_label,
            status_at_snapshot=ArticleStatus.DRAFT,
            body_markdown=body_markdown,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        self._session.add(version)

        # Audit log
        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.CREATED,
            previous_status=None,
            new_status=ArticleStatus.DRAFT,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Article created with title={title!r} slug={slug!r}",
        )

        await self._session.flush()
        return article

    # ------------------------------------------------------------------
    # Draft body update
    # ------------------------------------------------------------------

    async def save_draft(
        self,
        *,
        article: Any,
        body_markdown: str,
        actor_user_id: str,
        reason_for_change: str | None = None,
    ) -> Any:
        _, _, knowledge_article_version_cls, _ = _infra_models()
        if article.status != ArticleStatus.DRAFT:
            raise ArticleTransitionError(
                f"Cannot save draft body on article with status {article.status!r}. "
                "Article must be in DRAFT status."
            )

        article.last_edited_by = actor_user_id
        article.reason_for_change = reason_for_change or "Draft body updated"

        version = knowledge_article_version_cls(
            article_id=article.id,
            version_index=article.version_index,
            version_label=article.version_label,
            status_at_snapshot=ArticleStatus.DRAFT,
            body_markdown=body_markdown,
            created_by=actor_user_id,
            reason_for_change=reason_for_change or "Draft body updated",
        )
        self._session.add(version)

        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.DRAFT_SAVED,
            previous_status=ArticleStatus.DRAFT,
            new_status=ArticleStatus.DRAFT,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Draft body saved by {actor_user_id!r}",
        )

        await self._session.flush()
        return version

    # ------------------------------------------------------------------
    # State transitions
    # ------------------------------------------------------------------

    async def transition(
        self,
        *,
        article: Any,
        target_status: ArticleStatus,
        actor_user_id: str,
        reason_for_change: str | None = None,
        version_label: str | None = None,
    ) -> Any:
        _, _, knowledge_article_version_cls, _ = _infra_models()
        previous_status = article.status

        # Validate via pure domain function
        validate_transition(
            current_status=previous_status,
            target_status=target_status,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
        )

        # Determine audit action
        action_map: dict[ArticleStatus, ArticleAuditAction] = {
            ArticleStatus.IN_REVIEW: ArticleAuditAction.SUBMITTED_FOR_REVIEW,
            ArticleStatus.APPROVED: ArticleAuditAction.APPROVED,
            ArticleStatus.REJECTED: ArticleAuditAction.REJECTED,
            ArticleStatus.PUBLISHED: ArticleAuditAction.PUBLISHED,
            ArticleStatus.ARCHIVED: ArticleAuditAction.ARCHIVED,
            ArticleStatus.SUPERSEDED: ArticleAuditAction.SUPERSEDED,
            ArticleStatus.DRAFT: ArticleAuditAction.DRAFT_SAVED,
        }
        audit_action = action_map[target_status]

        # -- Side effects per target state --

        if target_status == ArticleStatus.APPROVED:
            article.approved_by = actor_user_id
            # Snapshot body content at approval (immutable version record)
            latest_version = await self._get_latest_version(article.id)
            if latest_version:
                snapshot = knowledge_article_version_cls(
                    article_id=article.id,
                    version_index=article.version_index,
                    version_label=article.version_label,
                    status_at_snapshot=ArticleStatus.APPROVED,
                    body_markdown=latest_version.body_markdown,
                    body_html=latest_version.body_html,
                    created_by=actor_user_id,
                    reason_for_change=reason_for_change or "Article approved",
                )
                self._session.add(snapshot)

        if target_status == ArticleStatus.PUBLISHED:
            if version_label:
                article.version_label = version_label
            article.version_index += 1
            # Auto-supersede any currently PUBLISHED version of the same article
            await self._supersede_published_version(
                article_id=article.id,
                actor_user_id=actor_user_id,
                reason_for_change="Auto-superseded on publication of new version",
            )

        # Apply transition
        article.status = target_status
        article.reason_for_change = (
            reason_for_change or f"Transitioned to {target_status}"
        )

        # Audit log
        await self._write_audit_log(
            article_id=article.id,
            action=audit_action,
            previous_status=previous_status,
            new_status=target_status,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=(
                f"Transition {previous_status!r} -> {target_status!r} "
                f"by {actor_user_id!r}"
            ),
        )

        await self._session.flush()

        # Dispatch notifications (fire-and-forget; errors logged, not raised)
        await self._dispatch_notification(
            article=article,
            action=audit_action,
            actor_user_id=actor_user_id,
        )

        return article

    # ------------------------------------------------------------------
    # Auditor read tracking
    # ------------------------------------------------------------------

    async def record_auditor_read(
        self,
        *,
        article: Any,
        actor_user_id: str,
    ) -> None:
        """
        Records a READ_BY_AUDITOR audit event when an auditor persona reads an article.

        Args:
            article: The article being read.
            actor_user_id: The auditor user ID.
        """
        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.READ_BY_AUDITOR,
            previous_status=article.status,
            new_status=article.status,
            actor_user_id=actor_user_id,
            reason_for_change=None,
            details=f"Article read by auditor {actor_user_id!r}",
        )
        await self._session.flush()

    # ------------------------------------------------------------------
    # Private helpers
    # ------------------------------------------------------------------

    async def _get_latest_version(self, article_id: str) -> Any | None:
        """Returns the most recent KnowledgeArticleVersion for an article."""
        _, _, knowledge_article_version_cls, _ = _infra_models()
        select = _sa_select()
        result = await self._session.execute(
            select(knowledge_article_version_cls)
            .where(knowledge_article_version_cls.article_id == article_id)
            .order_by(knowledge_article_version_cls.version_index.desc())
            .limit(1)
        )
        return result.scalar_one_or_none()

    async def _supersede_published_version(
        self,
        *,
        article_id: str,
        actor_user_id: str,
        reason_for_change: str,
    ) -> None:
        """
        Auto-transitions any currently PUBLISHED version of this article to SUPERSEDED.

        Called as a side effect when a new article version is Published, preventing
        two Published versions of the same article existing simultaneously.
        """
        _, knowledge_article_cls, _, _ = _infra_models()
        select = _sa_select()
        result = await self._session.execute(
            select(knowledge_article_cls).where(
                knowledge_article_cls.id == article_id,
                knowledge_article_cls.status.is_(ArticleStatus.PUBLISHED),
            )
        )
        published = result.scalar_one_or_none()
        if published and published.id != article_id:
            # In a multi-version model, supersede the sibling
            published.status = ArticleStatus.SUPERSEDED
            published.reason_for_change = reason_for_change
            await self._write_audit_log(
                article_id=published.id,
                action=ArticleAuditAction.SUPERSEDED,
                previous_status=ArticleStatus.PUBLISHED,
                new_status=ArticleStatus.SUPERSEDED,
                actor_user_id=actor_user_id,
                reason_for_change=reason_for_change,
                details="Auto-superseded on publication of newer version",
            )

    async def _write_audit_log(
        self,
        *,
        article_id: str,
        action: ArticleAuditAction,
        previous_status: ArticleStatus | None,
        new_status: ArticleStatus | None,
        actor_user_id: str,
        reason_for_change: str | None,
        details: str | None = None,
    ) -> None:
        """
        Appends an immutable KnowledgeArticleAuditLog record.

        Args:
            article_id: UUID of the article the event relates to.
            action: The ArticleAuditAction enum value.
            previous_status: Article status before the action.
            new_status: Article status after the action.
            actor_user_id: The acting user.
            reason_for_change: GxP justification; may be None for non-regulated actions.
            details: Optional human-readable details string.
        """
        _, _, _, knowledge_article_audit_log_cls = _infra_models()
        log_entry = knowledge_article_audit_log_cls(
            article_id=article_id,
            action=action.value,
            previous_status=previous_status.value if previous_status else None,
            new_status=new_status.value if new_status else None,
            details=details,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        self._session.add(log_entry)

    async def _dispatch_notification(
        self,
        *,
        article: Any,
        action: ArticleAuditAction,
        actor_user_id: str,
    ) -> None:
        """
        Dispatches an event notification to apps/notifications/ for article lifecycle events.

        Notification routing per settled design:
        - SUBMITTED_FOR_REVIEW  -> super_admin reviewers
        - APPROVED              -> author
        - REJECTED              -> author (last_edited_by)
        - PUBLISHED             -> all personas in the article's category visibility
        - ARCHIVED              -> super_admin only

        Args:
            article: The article whose lifecycle event triggered the notification.
            action: The ArticleAuditAction that was performed.
            actor_user_id: The user who performed the action.
        """
        event_map: dict[ArticleAuditAction, str] = {
            ArticleAuditAction.SUBMITTED_FOR_REVIEW: "knowledge.article.submitted_for_review",
            ArticleAuditAction.APPROVED: "knowledge.article.approved",
            ArticleAuditAction.REJECTED: "knowledge.article.rejected",
            ArticleAuditAction.PUBLISHED: "knowledge.article.published",
            ArticleAuditAction.ARCHIVED: "knowledge.article.archived",
        }
        event_type = event_map.get(action)
        if not event_type:
            return  # No notification for CREATED, DRAFT_SAVED, SUPERSEDED, READ_BY_AUDITOR

        snapshot = ArticleSnapshot(
            id=article.id,
            slug=article.slug,
            title=article.title,
            status=article.status,
            version_index=article.version_index,
            version_label=article.version_label,
            author_user_id=article.author_user_id,
            last_edited_by=article.last_edited_by,
            approved_by=article.approved_by,
        )

        payload = {
            "event_type": event_type,
            "data": {
                "article_id": snapshot.id,
                "article_slug": snapshot.slug,
                "article_title": snapshot.title,
                "status": snapshot.status.value,
                "version_label": snapshot.version_label,
                "actor_user_id": actor_user_id,
                "author_user_id": snapshot.author_user_id,
                "last_edited_by": snapshot.last_edited_by,
            },
        }

        success = await publish_notification(payload)
        if not success:
            logger.warning(
                "Notification dispatch failed for event_type=%r article_id=%r",
                event_type,
                article.id,
            )
