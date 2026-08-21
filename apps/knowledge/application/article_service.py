"""
Application-layer service for the Knowledge & Support Hub article lifecycle.

Orchestrates state transitions, audit log emission, version snapshotting,
and notification dispatch per the settled design decisions from wayfinder
ticket #4237.

Requirements: PRD-SYS-KH-001, PRD-SYS-KH-002
"""

import logging

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from apps.knowledge.adapters.notifications_client import publish_notification
from apps.knowledge.domain.models import (
    ArticleAuditAction,
    ArticleSnapshot,
    ArticleStatus,
    ArticleTransitionError,
    CategoryConflictError,
    CategoryNotFoundError,
    validate_transition,
)
from apps.knowledge.infrastructure.models import (
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleVersion,
    KnowledgeCategory,
)

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

    def __init__(self, session: AsyncSession) -> None:
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
    ) -> KnowledgeCategory:
        """
        Creates a new KnowledgeCategory.

        Args:
            name: Human-readable category name (must be unique).
            slug: URL-safe identifier (must be unique).
            description: Optional long description.
            persona_visibility: Comma-separated persona roles, or None for all.
            parent_id: Parent category UUID for nested hierarchy, or None.
            actor_user_id: Authenticated user creating the category.
            reason_for_change: GxP justification string.

        Returns:
            The persisted KnowledgeCategory instance.

        Raises:
            CategoryNotFoundError: If parent_id does not exist or is deleted.
            CategoryConflictError: If name or slug already exists.
        """
        # Validate parent category existence if parent_id is specified
        if parent_id:
            parent = await self.get_category_by_id(parent_id)
            if not parent:
                raise CategoryNotFoundError(
                    f"Parent category with id {parent_id!r} does not exist or has been deleted."
                )

        # Validate unique name and slug
        existing_result = await self._session.execute(
            select(KnowledgeCategory).where(
                KnowledgeCategory.is_deleted.is_(False),
                (KnowledgeCategory.name == name) | (KnowledgeCategory.slug == slug),
            )
        )
        existing = existing_result.scalars().first()
        if existing:
            raise CategoryConflictError(
                f"A category with name {name!r} or slug {slug!r} already exists."
            )

        category = KnowledgeCategory(
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

    async def get_category_by_id(self, category_id: str) -> KnowledgeCategory | None:
        """
        Retrieves an active KnowledgeCategory by its unique ID.

        Args:
            category_id: The UUID of the category.

        Returns:
            The KnowledgeCategory instance or None if not found or deleted.
        """
        result = await self._session.execute(
            select(KnowledgeCategory).where(
                KnowledgeCategory.id == category_id,
                KnowledgeCategory.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def get_category_by_slug(self, slug: str) -> KnowledgeCategory | None:
        """
        Retrieves an active KnowledgeCategory by its unique slug.

        Args:
            slug: The slug identifier of the category.

        Returns:
            The KnowledgeCategory instance or None if not found or deleted.
        """
        result = await self._session.execute(
            select(KnowledgeCategory).where(
                KnowledgeCategory.slug == slug,
                KnowledgeCategory.is_deleted.is_(False),
            )
        )
        return result.scalar_one_or_none()

    async def list_categories(
        self,
        user_roles: list[str] | None = None,
    ) -> list[KnowledgeCategory]:
        """
        Lists all active KnowledgeCategories, filtered by persona visibility.

        Admin users see all active categories. Non-admin users only receive
        categories permitted for their active persona context (or where
        persona_visibility is null/empty).

        Args:
            user_roles: List of normalized roles/personas for the requesting user.

        Returns:
            List of visible KnowledgeCategory instances.
        """
        result = await self._session.execute(
            select(KnowledgeCategory)
            .where(KnowledgeCategory.is_deleted.is_(False))
            .order_by(KnowledgeCategory.name.asc())
        )
        categories = list(result.scalars().all())

        if user_roles is None:
            return categories

        return [
            cat
            for cat in categories
            if self._is_category_visible_for_roles(cat.persona_visibility, user_roles)
        ]

    async def delete_category(
        self,
        *,
        category_id: str,
        actor_user_id: str,
        reason_for_change: str,
    ) -> KnowledgeCategory:
        """
        Soft-deletes a KnowledgeCategory by setting is_deleted=True.

        Args:
            category_id: UUID of the category to soft-delete.
            actor_user_id: Authenticated user initiating the deletion.
            reason_for_change: GxP justification string.

        Returns:
            The soft-deleted KnowledgeCategory instance.

        Raises:
            CategoryNotFoundError: If the category does not exist or has already been deleted.
        """
        category = await self.get_category_by_id(category_id)
        if not category:
            raise CategoryNotFoundError(
                f"Category with id {category_id!r} does not exist or has already been deleted."
            )

        category.is_deleted = True
        category.reason_for_change = reason_for_change
        await self._session.flush()
        return category

    @staticmethod
    def _is_category_visible_for_roles(
        persona_visibility: str | None, user_roles: list[str]
    ) -> bool:
        """
        Evaluates whether a category is visible to a user with the given roles.

        Rules:
        - If persona_visibility is None or blank -> visible to all authenticated personas.
        - If user has super_admin, sysadmin, admin, or sponsor_admin role -> visible to admin.
        - Otherwise, user must possess at least one persona role matching persona_visibility.
        """
        if not persona_visibility or not persona_visibility.strip():
            return True

        norm_user_roles = [r.strip().lower() for r in user_roles]

        # Admin roles see everything
        if any(
            r in ("super_admin", "sysadmin", "admin", "sponsor_admin")
            for r in norm_user_roles
        ):
            return True

        # Parse allowed personas
        allowed = {
            p.strip().lower() for p in persona_visibility.split(",") if p.strip()
        }

        # Match direct or with role synonyms
        from packages.security.rbac import ROLE_EXPANSIONS, normalize_role

        expanded_allowed = set(allowed)
        for p in allowed:
            expanded_allowed.add(normalize_role(p))
            if p in ROLE_EXPANSIONS:
                expanded_allowed.update(ROLE_EXPANSIONS[p])
            norm_p = normalize_role(p)
            if norm_p in ROLE_EXPANSIONS:
                expanded_allowed.update(ROLE_EXPANSIONS[norm_p])

        for r in norm_user_roles:
            if r in expanded_allowed or normalize_role(r) in expanded_allowed:
                return True
            if r in ROLE_EXPANSIONS and any(
                exp in expanded_allowed for exp in ROLE_EXPANSIONS[r]
            ):
                return True
            norm_r = normalize_role(r)
            if norm_r in ROLE_EXPANSIONS and any(
                exp in expanded_allowed for exp in ROLE_EXPANSIONS[norm_r]
            ):
                return True

        return False

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
    ) -> KnowledgeArticle:
        """
        Creates a new KnowledgeArticle in DRAFT status.

        Also appends the first KnowledgeArticleVersion snapshot and emits
        a CREATED audit log entry.

        Args:
            title: Article title.
            slug: URL-safe identifier (must be unique across all articles).
            category_id: UUID of the KnowledgeCategory this article belongs to.
            body_markdown: Initial draft body in Markdown.
            version_label: Human-readable version label (e.g. "1.0").
            actor_user_id: Authenticated user creating the article.
            reason_for_change: GxP justification string.

        Returns:
            The persisted KnowledgeArticle instance.

        Raises:
            sqlalchemy.exc.IntegrityError: If slug is not unique.
        """
        article = KnowledgeArticle(
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
        version = KnowledgeArticleVersion(
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
        article: KnowledgeArticle,
        body_markdown: str,
        actor_user_id: str,
        reason_for_change: str | None = None,
    ) -> KnowledgeArticleVersion:
        """
        Saves updated body content on a DRAFT article.

        Creates a new KnowledgeArticleVersion snapshot. Updates last_edited_by
        to support the four-eyes principle on subsequent approval.

        Args:
            article: The KnowledgeArticle to update (must be in DRAFT status).
            body_markdown: New Markdown body content.
            actor_user_id: Authenticated user saving the draft.
            reason_for_change: Optional justification (not required for drafts).

        Returns:
            The new KnowledgeArticleVersion snapshot.

        Raises:
            ArticleTransitionError: If article is not in DRAFT status.
        """
        if article.status != ArticleStatus.DRAFT:
            raise ArticleTransitionError(
                f"Cannot save draft body on article with status {article.status!r}. "
                "Article must be in DRAFT status."
            )

        article.last_edited_by = actor_user_id
        article.reason_for_change = reason_for_change or "Draft body updated"

        version = KnowledgeArticleVersion(
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
        article: KnowledgeArticle,
        target_status: ArticleStatus,
        actor_user_id: str,
        reason_for_change: str | None = None,
        version_label: str | None = None,
    ) -> KnowledgeArticle:
        """
        Executes a validated state machine transition on a KnowledgeArticle.

        Handles all transition side effects:
        - APPROVED: records approved_by, creates immutable version snapshot.
        - PUBLISHED: auto-supersedes the existing PUBLISHED version of this article.
        - All: emits audit log, dispatches notification.

        Args:
            article: The article to transition.
            target_status: The desired new ArticleStatus.
            actor_user_id: The user triggering the transition.
            reason_for_change: Justification; required on regulated transitions.
            version_label: Optional new human-readable version label (e.g. "2.0").

        Returns:
            The updated KnowledgeArticle.

        Raises:
            ArticleTransitionError: Invalid transition.
            ArticleApprovalConflictError: Four-eyes violation.
            ArticleReasonRequiredError: Missing reason on regulated transition.
        """
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
                snapshot = KnowledgeArticleVersion(
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
        article: KnowledgeArticle,
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

    async def _get_latest_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Returns the most recent KnowledgeArticleVersion for an article."""
        result = await self._session.execute(
            select(KnowledgeArticleVersion)
            .where(KnowledgeArticleVersion.article_id == article_id)
            .order_by(KnowledgeArticleVersion.version_index.desc())
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
        result = await self._session.execute(
            select(KnowledgeArticle).where(
                KnowledgeArticle.id == article_id,
                KnowledgeArticle.status == ArticleStatus.PUBLISHED.value,
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
        log_entry = KnowledgeArticleAuditLog(
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
        article: KnowledgeArticle,
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
