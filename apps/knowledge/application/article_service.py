"""
Application-layer service for the Knowledge & Support Hub article lifecycle.

Orchestrates state transitions, four-eyes review validation, immutable version
snapshotting, GxP audit logging, and notification dispatch per ADR-2188.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002, ADR-2188
"""

import json
import logging
from dataclasses import dataclass, field

from apps.knowledge.domain.exceptions import (
    ArticleNotFoundError,
    ArticleReasonRequiredError,
    ArticleTransitionError,
    CategoryConflictError,
    CategoryNotFoundError,
    ContextualHelpMappingNotFoundError,
)
from apps.knowledge.domain.markdown_renderer import render_markdown_to_html
from apps.knowledge.domain.models import (
    ArticleAuditAction,
    ArticleSnapshot,
    ArticleStatus,
    validate_transition,
)
from apps.knowledge.domain.route_matcher import (
    normalize_route,
    rank_matching_mappings,
)
from apps.knowledge.ports.repository_port import (
    ContextualHelpMapping,
    ContextualHelpMappingRepositoryPort,
    KnowledgeArticle,
    KnowledgeArticleAuditLog,
    KnowledgeArticleRepositoryPort,
    KnowledgeArticleVersion,
    KnowledgeAuditLogRepositoryPort,
    KnowledgeCategory,
    KnowledgeCategoryRepositoryPort,
)
from packages.security.audit_logger import CentralAuditLogger
from packages.security.notifications import publish_notification

logger = logging.getLogger("knowledge-article-service")


@dataclass
class ContextualHelpResolutionResult:
    """Result container for dynamic contextual help resolution."""

    matched_mapping: ContextualHelpMapping | None = None
    primary_article: KnowledgeArticle | None = None
    primary_version: KnowledgeArticleVersion | None = None
    section_anchor: str | None = None
    related_articles: list[tuple[KnowledgeArticle, KnowledgeArticleVersion | None]] = (
        field(default_factory=list)
    )


def normalize_tags_for_storage(tags: list[str] | str | None) -> str | None:
    """Normalizes tags input into a canonical JSON array string for database storage."""
    if tags is None:
        return None
    if isinstance(tags, list):
        return json.dumps([str(item) for item in tags if str(item).strip()])
    if isinstance(tags, str):
        v_str = tags.strip()
        if not v_str:
            return None
        if v_str.startswith("[") and v_str.endswith("]"):
            try:
                parsed = json.loads(v_str)
                if isinstance(parsed, list):
                    return json.dumps(
                        [str(item) for item in parsed if str(item).strip()]
                    )
            except Exception:
                pass
        items = [t.strip() for t in v_str.split(",") if t.strip()]
        return json.dumps(items)
    return str(tags)


class ArticleLifecycleService:
    """
    Orchestrates the complete GxP article lifecycle for the Knowledge microservice.

    Responsibilities:
    - Validates and executes state machine transitions via domain.models.validate_transition.
    - Manages working drafts updating a single KnowledgeArticleVersion row during DRAFT status.
    - Enforces Four-Eyes principle: article cannot be approved by author_user_id or last_edited_by.
    - Locks KnowledgeArticleVersion records permanently on APPROVED transition.
    - Auto-supersedes prior published version on publication of version N+1.
    - Maintains KnowledgeArticle.current_published_version_id for O(1) published lookups.
    - Emits KnowledgeArticleAuditLog records for every action.
    - Dispatches notifications via apps/notifications/ for triggering events.
    """

    def __init__(
        self,
        article_repo: KnowledgeArticleRepositoryPort,
        category_repo: KnowledgeCategoryRepositoryPort,
        audit_repo: KnowledgeAuditLogRepositoryPort,
        help_repo: ContextualHelpMappingRepositoryPort,
    ) -> None:
        self._article_repo = article_repo
        self._category_repo = category_repo
        self._audit_repo = audit_repo
        self._help_repo = help_repo

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
        if parent_id:
            parent = await self._category_repo.get_by_id(parent_id)
            if not parent:
                raise CategoryNotFoundError(
                    f"Parent category with id {parent_id!r} does not exist or has been deleted."
                )

        existing_by_slug = await self._category_repo.get_by_slug(slug)
        if existing_by_slug:
            raise CategoryConflictError(
                f"A category with slug {slug!r} already exists."
            )

        all_cats = await self._category_repo.list_categories()
        if any(c.name == name for c in all_cats):
            raise CategoryConflictError(
                f"A category with name {name!r} already exists."
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
        return await self._category_repo.save(category)

    async def get_category_by_id(self, category_id: str) -> KnowledgeCategory | None:
        """Retrieves an active KnowledgeCategory by its unique ID."""
        return await self._category_repo.get_by_id(category_id)

    async def get_category_by_slug(self, slug: str) -> KnowledgeCategory | None:
        """Retrieves an active KnowledgeCategory by its unique slug."""
        return await self._category_repo.get_by_slug(slug)

    async def list_categories(
        self,
        user_roles: list[str] | None = None,
    ) -> list[KnowledgeCategory]:
        """Lists active KnowledgeCategories, filtered by persona visibility."""
        categories = await self._category_repo.list_categories()
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
        """Soft-deletes a KnowledgeCategory."""
        category = await self.get_category_by_id(category_id)
        if not category:
            raise CategoryNotFoundError(
                f"Category with id {category_id!r} does not exist or has already been deleted."
            )

        category.is_deleted = True
        category.reason_for_change = reason_for_change
        await self._category_repo.save(category)
        return category

    @staticmethod
    def _is_category_visible_for_roles(
        persona_visibility: str | None, user_roles: list[str]
    ) -> bool:
        """Evaluates whether a category is visible to a user with the given roles."""
        if not persona_visibility or not persona_visibility.strip():
            return True

        norm_user_roles = [r.strip().lower() for r in user_roles]

        if any(
            r in ("super_admin", "sysadmin", "admin", "sponsor_admin")
            for r in norm_user_roles
        ):
            return True

        allowed = {
            p.strip().lower() for p in persona_visibility.split(",") if p.strip()
        }

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
    # Article creation & Draft Storage (Issue #4325)
    # ------------------------------------------------------------------

    async def create_article(
        self,
        *,
        title: str,
        slug: str,
        category_id: str,
        body_markdown: str,
        version_label: str = "1.0",
        tags: list[str] | str | None = None,
        actor_user_id: str,
        reason_for_change: str,
    ) -> KnowledgeArticle:
        """
        Creates a new KnowledgeArticle in DRAFT status.

        Appends the single working KnowledgeArticleVersion snapshot for version 1
        with auto-rendered HTML and GxP audit fields, and emits CREATED audit log.

        Args:
            title: Article title.
            slug: URL-safe identifier (must be unique across all articles).
            category_id: UUID of the KnowledgeCategory this article belongs to.
            body_markdown: Initial draft body in Markdown.
            version_label: Human-readable version label (e.g. "1.0").
            tags: Optional tags list, JSON array string, or comma-separated string.
            actor_user_id: Authenticated user creating the article.
            reason_for_change: GxP justification string.

        Returns:
            The persisted KnowledgeArticle instance.
        """
        existing = await self._article_repo.get_by_slug(slug)
        if existing:
            raise ArticleTransitionError(
                f"An article with slug {slug!r} already exists."
            )

        rendered_html = render_markdown_to_html(body_markdown)
        stored_tags = normalize_tags_for_storage(tags)

        article = KnowledgeArticle(
            title=title,
            slug=slug,
            category_id=category_id,
            status=ArticleStatus.DRAFT,
            version_index=1,
            version_label=version_label,
            tags=stored_tags,
            author_user_id=actor_user_id,
            last_edited_by=actor_user_id,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        article = await self._article_repo.save(article)

        # Working draft version row (DRAFT status, is_locked=False)
        version = KnowledgeArticleVersion(
            article_id=article.id,
            version_index=1,
            version_label=version_label,
            status_at_snapshot=ArticleStatus.DRAFT.value,
            body_markdown=body_markdown,
            body_html=rendered_html,
            is_locked=False,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        await self._article_repo.save_version(version)

        # Append audit log
        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.CREATED,
            previous_status=None,
            new_status=ArticleStatus.DRAFT,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Article created with title={title!r} slug={slug!r}",
        )

        return article

    async def get_article_by_id(self, article_id: str) -> KnowledgeArticle | None:
        """Retrieves an active KnowledgeArticle by its unique ID."""
        return await self._article_repo.get_by_id(article_id)

    async def get_article_by_slug(self, slug: str) -> KnowledgeArticle | None:
        """Retrieves an active KnowledgeArticle by its unique slug."""
        return await self._article_repo.get_by_slug(slug)

    async def get_working_draft_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """Retrieves the active working draft version for an article."""
        return await self._article_repo.get_working_draft_version(article_id)

    async def get_current_published_version(
        self, article_id: str
    ) -> KnowledgeArticleVersion | None:
        """
        Retrieves the current published version snapshot in O(1) via
        KnowledgeArticle.current_published_version_id pointer.
        """
        article = await self.get_article_by_id(article_id)
        if not article or not article.current_published_version_id:
            return None
        return await self._article_repo.get_version_by_id(
            article.current_published_version_id
        )

    async def list_articles(
        self,
        status: ArticleStatus | None = None,
        category_id: str | None = None,
    ) -> list[KnowledgeArticle]:
        """Lists active knowledge articles with optional status and category filters."""
        return await self._article_repo.list_articles(
            status=status, category_id=category_id
        )

    async def list_article_versions(
        self, article_id: str
    ) -> list[KnowledgeArticleVersion]:
        """Lists all version snapshots for an article ordered by version index."""
        return await self._article_repo.list_versions(article_id)

    async def update_draft(
        self,
        *,
        article_id: str,
        body_markdown: str,
        actor_user_id: str,
        title: str | None = None,
        slug: str | None = None,
        category_id: str | None = None,
        tags: list[str] | str | None = None,
        reason_for_change: str | None = None,
    ) -> tuple[KnowledgeArticle, KnowledgeArticleVersion]:
        """
        Updates the working draft of an article (Issue #4325).

        Updates the single KnowledgeArticleVersion row during DRAFT status with
        markdown body, auto-rendered HTML, and GxP audit fields (created_by,
        reason_for_change, version_index). Updates last_edited_by on KnowledgeArticle.

        Args:
            article_id: UUID of the article to update.
            body_markdown: Updated Markdown body.
            actor_user_id: Authenticated user updating the draft.
            title: Optional updated title.
            slug: Optional updated slug.
            category_id: Optional updated category ID.
            tags: Optional updated tags list, JSON array string, or comma-separated string.
            reason_for_change: GxP reason for change.

        Returns:
            Tuple of (KnowledgeArticle, KnowledgeArticleVersion).

        Raises:
            ArticleNotFoundError: If article does not exist.
            ArticleTransitionError: If article is not in DRAFT status.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist."
            )

        if article.status != ArticleStatus.DRAFT:
            raise ArticleTransitionError(
                f"Cannot update draft body on article with status {article.status!r}. "
                "Article must be in DRAFT status."
            )

        # Update article metadata if provided
        if title:
            article.title = title
        if slug and slug != article.slug:
            existing = await self._article_repo.get_by_slug(slug)
            if existing and existing.id != article.id:
                raise ArticleTransitionError(
                    f"An article with slug {slug!r} already exists."
                )
            article.slug = slug
        if category_id:
            article.category_id = category_id
        if tags is not None:
            article.tags = normalize_tags_for_storage(tags)

        article.last_edited_by = actor_user_id
        article.reason_for_change = reason_for_change or "Draft body updated"
        await self._article_repo.save(article)

        rendered_html = render_markdown_to_html(body_markdown)

        # Find existing working draft version row or create if none exists
        draft_version = await self._article_repo.get_working_draft_version(article.id)
        if draft_version:
            # Update the single existing working draft row in-place
            draft_version.body_markdown = body_markdown
            draft_version.body_html = rendered_html
            draft_version.created_by = actor_user_id
            draft_version.reason_for_change = reason_for_change or "Draft body updated"
            draft_version = await self._article_repo.save_version(draft_version)
        else:
            draft_version = KnowledgeArticleVersion(
                article_id=article.id,
                version_index=article.version_index,
                version_label=article.version_label,
                status_at_snapshot=ArticleStatus.DRAFT.value,
                body_markdown=body_markdown,
                body_html=rendered_html,
                is_locked=False,
                created_by=actor_user_id,
                reason_for_change=reason_for_change or "Draft body updated",
            )
            draft_version = await self._article_repo.save_version(draft_version)

        # Emit audit log
        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.DRAFT_SAVED,
            previous_status=ArticleStatus.DRAFT,
            new_status=ArticleStatus.DRAFT,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Draft body updated by {actor_user_id!r}",
        )

        return article, draft_version

    async def save_draft(
        self,
        *,
        article: KnowledgeArticle,
        body_markdown: str,
        actor_user_id: str,
        reason_for_change: str | None = None,
    ) -> KnowledgeArticleVersion:
        """Convenience method to save draft body on an existing KnowledgeArticle instance."""
        _, version = await self.update_draft(
            article_id=article.id,
            body_markdown=body_markdown,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
        )
        return version

    # ------------------------------------------------------------------
    # Four-Eyes Review & Snapshots (Issue #4326)
    # ------------------------------------------------------------------

    async def submit_for_review(
        self,
        *,
        article_id: str,
        actor_user_id: str,
        reason_for_change: str | None = None,
    ) -> KnowledgeArticle:
        """
        Submits a DRAFT article for peer/quality review (Issue #4326).

        Transitions status from DRAFT -> IN_REVIEW.

        Args:
            article_id: UUID of the article.
            actor_user_id: User initiating review submission.
            reason_for_change: Optional justification.

        Returns:
            The updated KnowledgeArticle.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist."
            )

        validate_transition(
            current_status=article.status,
            target_status=ArticleStatus.IN_REVIEW,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
            author_user_id=article.author_user_id,
        )

        article.status = ArticleStatus.IN_REVIEW
        article.reason_for_change = reason_for_change or "Submitted for peer review"
        await self._article_repo.save(article)

        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.SUBMITTED_FOR_REVIEW,
            previous_status=ArticleStatus.DRAFT,
            new_status=ArticleStatus.IN_REVIEW,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Submitted for review by {actor_user_id!r}",
        )

        await self._dispatch_notification(
            article=article,
            action=ArticleAuditAction.SUBMITTED_FOR_REVIEW,
            actor_user_id=actor_user_id,
        )

        return article

    async def approve_article(
        self,
        *,
        article_id: str,
        actor_user_id: str,
        reason_for_change: str,
    ) -> KnowledgeArticle:
        """
        Approves an article in IN_REVIEW status (Issue #4326).

        Enforces Four-Eyes principle:
        - Approver cannot be the original creator (author_user_id).
        - Approver cannot be the last editor (last_edited_by).

        On APPROVED:
        - Locks the working KnowledgeArticleVersion record as permanently immutable (is_locked=True).
        - Sets status_at_snapshot to APPROVED.
        - Records approved_by.

        Args:
            article_id: UUID of the article.
            actor_user_id: User approving the article.
            reason_for_change: Required GxP justification string.

        Returns:
            The approved KnowledgeArticle.

        Raises:
            ArticleNotFoundError: If article does not exist.
            ArticleApprovalConflictError: If four-eyes principle is violated.
            ArticleReasonRequiredError: If reason_for_change is missing.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist."
            )

        validate_transition(
            current_status=article.status,
            target_status=ArticleStatus.APPROVED,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
            author_user_id=article.author_user_id,
        )

        # Lock the working draft version snapshot permanently
        draft_version = await self._article_repo.get_working_draft_version(article.id)
        if not draft_version:
            draft_version = await self._article_repo.get_latest_version(article.id)

        if draft_version:
            draft_version.status_at_snapshot = ArticleStatus.APPROVED.value
            draft_version.is_locked = True
            draft_version.reason_for_change = reason_for_change
            await self._article_repo.save_version(draft_version)

        article.status = ArticleStatus.APPROVED
        article.approved_by = actor_user_id
        article.reason_for_change = reason_for_change
        await self._article_repo.save(article)

        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.APPROVED,
            previous_status=ArticleStatus.IN_REVIEW,
            new_status=ArticleStatus.APPROVED,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Article approved by {actor_user_id!r}",
        )

        await self._dispatch_notification(
            article=article,
            action=ArticleAuditAction.APPROVED,
            actor_user_id=actor_user_id,
        )

        return article

    async def reject_article(
        self,
        *,
        article_id: str,
        actor_user_id: str,
        reason_for_change: str | None = None,
    ) -> KnowledgeArticle:
        """
        Rejects an article in IN_REVIEW status (Issue #4326).

        Transitions status from IN_REVIEW -> REJECTED.

        Args:
            article_id: UUID of the article.
            actor_user_id: User rejecting the article.
            reason_for_change: Optional review comments/reasons.

        Returns:
            The rejected KnowledgeArticle.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist."
            )

        validate_transition(
            current_status=article.status,
            target_status=ArticleStatus.REJECTED,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
            author_user_id=article.author_user_id,
        )

        article.status = ArticleStatus.REJECTED
        article.reason_for_change = reason_for_change or "Rejected by reviewer"
        await self._article_repo.save(article)

        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.REJECTED,
            previous_status=ArticleStatus.IN_REVIEW,
            new_status=ArticleStatus.REJECTED,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Article rejected by {actor_user_id!r}",
        )

        await self._dispatch_notification(
            article=article,
            action=ArticleAuditAction.REJECTED,
            actor_user_id=actor_user_id,
        )

        return article

    # ------------------------------------------------------------------
    # Publication & Auto-Supersede (Issue #4327)
    # ------------------------------------------------------------------

    async def publish_article(
        self,
        *,
        article_id: str,
        actor_user_id: str,
        reason_for_change: str,
        version_label: str | None = None,
    ) -> KnowledgeArticle:
        """
        Publishes an APPROVED article (Issue #4327).

        - Sets fast O(1) lookup pointer KnowledgeArticle.current_published_version_id.
        - Publishing version N+1 automatically sets prior active version N to SUPERSEDED
          status without data loss.
        - Increments version index metadata for subsequent draft cycle.

        Args:
            article_id: UUID of the article.
            actor_user_id: User publishing the article.
            reason_for_change: Required GxP justification string.
            version_label: Optional human-readable version label.

        Returns:
            The published KnowledgeArticle.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist."
            )

        validate_transition(
            current_status=article.status,
            target_status=ArticleStatus.PUBLISHED,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
            author_user_id=article.author_user_id,
        )

        # Retrieve the latest approved version snapshot
        approved_version = await self._article_repo.get_latest_version(article.id)

        # Auto-supersede prior published version snapshot if one exists
        if (
            article.current_published_version_id
            and approved_version
            and article.current_published_version_id != approved_version.id
        ):
            prior_version = await self._article_repo.get_version_by_id(
                article.current_published_version_id
            )
            if prior_version:
                prior_version.status_at_snapshot = ArticleStatus.SUPERSEDED.value
                await self._article_repo.save_version(prior_version)
                await self._write_audit_log(
                    article_id=article.id,
                    action=ArticleAuditAction.SUPERSEDED,
                    previous_status=ArticleStatus.PUBLISHED,
                    new_status=ArticleStatus.SUPERSEDED,
                    actor_user_id=actor_user_id,
                    reason_for_change="Auto-superseded on publication of newer version",
                    details=f"Version index {prior_version.version_index} superseded by version index {approved_version.version_index}",
                )

        if approved_version:
            approved_version.status_at_snapshot = ArticleStatus.PUBLISHED.value
            approved_version.is_locked = True
            if version_label:
                approved_version.version_label = version_label
            await self._article_repo.save_version(approved_version)
            article.current_published_version_id = approved_version.id

        if version_label:
            article.version_label = version_label

        article.status = ArticleStatus.PUBLISHED
        article.version_index += 1
        article.reason_for_change = reason_for_change
        await self._article_repo.save(article)

        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.PUBLISHED,
            previous_status=ArticleStatus.APPROVED,
            new_status=ArticleStatus.PUBLISHED,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Article published as active version {article.version_label}",
        )

        await self._dispatch_notification(
            article=article,
            action=ArticleAuditAction.PUBLISHED,
            actor_user_id=actor_user_id,
        )

        return article

    # ------------------------------------------------------------------
    # Generic State Machine Transition Dispatcher
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

        Delegates to dedicated methods for specialized actions (review submission,
        approval, rejection, publication) or handles reopening to DRAFT.
        """
        if target_status == ArticleStatus.IN_REVIEW:
            return await self.submit_for_review(
                article_id=article.id,
                actor_user_id=actor_user_id,
                reason_for_change=reason_for_change,
            )

        if target_status == ArticleStatus.APPROVED:
            if not reason_for_change:
                raise ArticleReasonRequiredError(
                    "A reason_for_change is required when transitioning to 'APPROVED'."
                )
            return await self.approve_article(
                article_id=article.id,
                actor_user_id=actor_user_id,
                reason_for_change=reason_for_change,
            )

        if target_status == ArticleStatus.REJECTED:
            return await self.reject_article(
                article_id=article.id,
                actor_user_id=actor_user_id,
                reason_for_change=reason_for_change,
            )

        if target_status == ArticleStatus.PUBLISHED:
            if not reason_for_change:
                raise ArticleReasonRequiredError(
                    "A reason_for_change is required when transitioning to 'PUBLISHED'."
                )
            return await self.publish_article(
                article_id=article.id,
                actor_user_id=actor_user_id,
                reason_for_change=reason_for_change,
                version_label=version_label,
            )

        # Standard transition (e.g. ARCHIVED, or reopen to DRAFT)
        previous_status = article.status
        validate_transition(
            current_status=previous_status,
            target_status=target_status,
            reason_for_change=reason_for_change,
            actor_user_id=actor_user_id,
            last_edited_by=article.last_edited_by,
            author_user_id=article.author_user_id,
        )

        action_map: dict[ArticleStatus, ArticleAuditAction] = {
            ArticleStatus.ARCHIVED: ArticleAuditAction.ARCHIVED,
            ArticleStatus.SUPERSEDED: ArticleAuditAction.SUPERSEDED,
            ArticleStatus.DRAFT: ArticleAuditAction.DRAFT_SAVED,
        }
        audit_action = action_map.get(target_status, ArticleAuditAction.DRAFT_SAVED)

        if target_status == ArticleStatus.DRAFT:
            # Reopening article as a new working draft
            article.last_edited_by = actor_user_id
            latest_version = await self._article_repo.get_latest_version(article.id)
            body_md = latest_version.body_markdown if latest_version else "# New Draft"
            rendered_html = render_markdown_to_html(body_md)

            new_version = KnowledgeArticleVersion(
                article_id=article.id,
                version_index=article.version_index,
                version_label=article.version_label,
                status_at_snapshot=ArticleStatus.DRAFT.value,
                body_markdown=body_md,
                body_html=rendered_html,
                is_locked=False,
                created_by=actor_user_id,
                reason_for_change=reason_for_change
                or f"Reopened to DRAFT from {previous_status}",
            )
            await self._article_repo.save_version(new_version)

        article.status = target_status
        article.reason_for_change = (
            reason_for_change or f"Transitioned to {target_status}"
        )
        await self._article_repo.save(article)

        await self._write_audit_log(
            article_id=article.id,
            action=audit_action,
            previous_status=previous_status,
            new_status=target_status,
            actor_user_id=actor_user_id,
            reason_for_change=reason_for_change,
            details=f"Transition {previous_status!r} -> {target_status!r} by {actor_user_id!r}",
        )

        if target_status == ArticleStatus.ARCHIVED:
            await self._dispatch_notification(
                article=article,
                action=ArticleAuditAction.ARCHIVED,
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
        """Records a READ_BY_AUDITOR audit event when an auditor persona reads an article."""
        await self._write_audit_log(
            article_id=article.id,
            action=ArticleAuditAction.READ_BY_AUDITOR,
            previous_status=article.status,
            new_status=article.status,
            actor_user_id=actor_user_id,
            reason_for_change=None,
            details=f"Article read by auditor {actor_user_id!r}",
        )

    # ------------------------------------------------------------------
    # Audit log writing
    # ------------------------------------------------------------------

    async def _write_audit_log(
        self,
        *,
        article_id: str,
        action: ArticleAuditAction | str,
        previous_status: ArticleStatus | str | None,
        new_status: ArticleStatus | str | None,
        actor_user_id: str,
        reason_for_change: str | None,
        details: str | None = None,
    ) -> None:
        """Appends an immutable KnowledgeArticleAuditLog record."""
        action_val = action.value if hasattr(action, "value") else str(action)
        prev_val = (
            previous_status.value
            if hasattr(previous_status, "value")
            else str(previous_status)
            if previous_status is not None
            else None
        )
        new_val = (
            new_status.value
            if hasattr(new_status, "value")
            else str(new_status)
            if new_status is not None
            else None
        )
        log_entry = KnowledgeArticleAuditLog(
            article_id=article_id,
            action=action_val,
            previous_status=prev_val,
            new_status=new_val,
            details=details,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        await self._audit_repo.append_log(log_entry)

        try:
            CentralAuditLogger.log_event(
                service_name="apps/knowledge",
                action_type=action_val,
                entity_name="KnowledgeArticle",
                entity_id=article_id,
                user_id=actor_user_id,
                reason_for_change=reason_for_change
                or f"Knowledge article lifecycle action: {action_val}",
                details={
                    "previous_status": prev_val,
                    "new_status": new_val,
                    "details": details,
                },
            )
        except Exception as exc:
            logger.warning("CentralAuditLogger event emission failed: %s", exc)

    # ------------------------------------------------------------------
    # Notifications dispatch
    # ------------------------------------------------------------------

    async def _dispatch_notification(
        self,
        *,
        article: KnowledgeArticle,
        action: ArticleAuditAction,
        actor_user_id: str,
    ) -> None:
        """Dispatches an event notification to apps/notifications/ for article lifecycle events."""
        event_map: dict[ArticleAuditAction, str] = {
            ArticleAuditAction.SUBMITTED_FOR_REVIEW: "knowledge.article.submitted_for_review",
            ArticleAuditAction.APPROVED: "knowledge.article.approved",
            ArticleAuditAction.REJECTED: "knowledge.article.rejected",
            ArticleAuditAction.PUBLISHED: "knowledge.article.published",
            ArticleAuditAction.ARCHIVED: "knowledge.article.archived",
        }
        event_type = event_map.get(action)
        if not event_type:
            return

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

        success = await publish_notification(
            payload,
            service_name="knowledge-service",
        )
        if not success:
            logger.warning(
                "Notification dispatch failed for event_type=%r article_id=%r",
                event_type,
                article.id,
            )

    # ------------------------------------------------------------------
    # Contextual Help Mappings & Resolution (Issue #4328)
    # ------------------------------------------------------------------

    async def create_help_mapping(
        self,
        *,
        route_pattern: str,
        persona: str | None,
        article_id: str,
        priority: int = 100,
        section_anchor: str | None = None,
        is_active: bool = True,
        actor_user_id: str,
        reason_for_change: str,
    ) -> ContextualHelpMapping:
        """
        Creates a new ContextualHelpMapping.

        Args:
            route_pattern: Target route pattern (e.g. '/ecrf/*', '/mdr/:studyId/*').
            persona: Specific persona role or None for universal.
            article_id: UUID of the target KnowledgeArticle.
            priority: Priority integer (lowest integer = highest priority).
            section_anchor: Optional anchor heading (e.g. '#enrollment-procedure').
            is_active: Boolean active status flag.
            actor_user_id: Authenticated user creating the mapping.
            reason_for_change: GxP justification string.

        Returns:
            The persisted ContextualHelpMapping instance.

        Raises:
            ArticleNotFoundError: If article_id does not exist or is deleted.
        """
        article = await self.get_article_by_id(article_id)
        if not article:
            raise ArticleNotFoundError(
                f"Article with id {article_id!r} does not exist or has been deleted."
            )

        mapping = ContextualHelpMapping(
            route_pattern=normalize_route(route_pattern),
            persona=persona.strip().lower() if persona and persona.strip() else None,
            article_id=article_id,
            priority=priority,
            section_anchor=section_anchor.strip()
            if section_anchor and section_anchor.strip()
            else None,
            is_active=is_active,
            created_by=actor_user_id,
            reason_for_change=reason_for_change,
        )
        return await self._help_repo.save(mapping)

    async def get_help_mapping_by_id(
        self, mapping_id: str
    ) -> ContextualHelpMapping | None:
        """Retrieves a ContextualHelpMapping by ID."""
        return await self._help_repo.get_by_id(mapping_id)

    async def list_help_mappings(
        self,
        *,
        route_pattern: str | None = None,
        persona: str | None = None,
        is_active: bool | None = None,
    ) -> list[ContextualHelpMapping]:
        """Lists contextual help mappings with optional filters."""
        return await self._help_repo.list_mappings(
            route_pattern=route_pattern,
            persona=persona,
            is_active=is_active,
        )

    async def update_help_mapping(
        self,
        *,
        mapping_id: str,
        actor_user_id: str,
        reason_for_change: str,
        route_pattern: str | None = None,
        persona: str | None = None,
        article_id: str | None = None,
        priority: int | None = None,
        section_anchor: str | None = None,
        is_active: bool | None = None,
    ) -> ContextualHelpMapping:
        """
        Updates an existing ContextualHelpMapping.

        Args:
            mapping_id: UUID of mapping.
            actor_user_id: Authenticated user making change.
            reason_for_change: GxP reason for change.
            route_pattern: Optional updated pattern.
            persona: Optional updated persona.
            article_id: Optional updated article ID.
            priority: Optional updated priority integer.
            section_anchor: Optional updated section anchor.
            is_active: Optional updated active status.

        Returns:
            The updated ContextualHelpMapping instance.

        Raises:
            ContextualHelpMappingNotFoundError: If mapping does not exist.
            ArticleNotFoundError: If updated article_id does not exist.
        """
        mapping = await self._help_repo.get_by_id(mapping_id)
        if not mapping:
            raise ContextualHelpMappingNotFoundError(
                f"ContextualHelpMapping with id {mapping_id!r} does not exist."
            )

        if article_id and article_id != mapping.article_id:
            art = await self.get_article_by_id(article_id)
            if not art:
                raise ArticleNotFoundError(
                    f"Article with id {article_id!r} does not exist or has been deleted."
                )
            mapping.article_id = article_id

        if route_pattern is not None:
            mapping.route_pattern = normalize_route(route_pattern)
        if persona is not None:
            mapping.persona = persona.strip().lower() if persona.strip() else None
        if priority is not None:
            mapping.priority = priority
        if section_anchor is not None:
            mapping.section_anchor = (
                section_anchor.strip() if section_anchor.strip() else None
            )
        if is_active is not None:
            mapping.is_active = is_active

        mapping.reason_for_change = reason_for_change
        mapping.version_index += 1
        return await self._help_repo.save(mapping)

    async def delete_help_mapping(
        self,
        *,
        mapping_id: str,
        actor_user_id: str,
        reason_for_change: str,
    ) -> bool:
        """Deletes a ContextualHelpMapping by ID."""
        mapping = await self._help_repo.get_by_id(mapping_id)
        if not mapping:
            raise ContextualHelpMappingNotFoundError(
                f"ContextualHelpMapping with id {mapping_id!r} does not exist."
            )
        return await self._help_repo.delete(mapping_id)

    async def resolve_contextual_help(
        self,
        *,
        route: str,
        persona: str | None = None,
    ) -> ContextualHelpResolutionResult:
        """
        Dynamically resolves the primary spotlight article and up to 3 secondary
        related guides for a given route and user persona.

        Hierarchical Specificity Resolution:
        1. Evaluates all active mappings linked to published articles.
        2. Filters matching route patterns and persona / fallback rules.
        3. Ranks candidates via route_matcher (priority ASC, persona match, pattern specificity, length, recency).
        4. Deduplicates articles: best mapping selects primary article; next distinct mappings form related articles.

        Args:
            route: In-app route path (e.g. '/ecrf/site-101/subjects/SUBJ-001').
            persona: User's active persona role (e.g. 'site_crc').

        Returns:
            ContextualHelpResolutionResult containing primary spotlight article, version, anchor, and related guides.
        """
        active_mappings = await self._help_repo.list_active_mappings()
        if not active_mappings:
            return ContextualHelpResolutionResult()

        ranked = rank_matching_mappings(active_mappings, route, persona)
        if not ranked:
            return ContextualHelpResolutionResult()

        # Deduplicate articles so the same article is not surfaced multiple times
        seen_articles: set[str] = set()
        primary_mapping: ContextualHelpMapping | None = None
        primary_article: KnowledgeArticle | None = None
        primary_version: KnowledgeArticleVersion | None = None
        related: list[tuple[KnowledgeArticle, KnowledgeArticleVersion | None]] = []

        for m in ranked:
            if m.article_id in seen_articles:
                continue
            seen_articles.add(m.article_id)

            article = await self.get_article_by_id(m.article_id)
            if (
                not article
                or article.status != ArticleStatus.PUBLISHED
                or article.is_deleted
            ):
                continue

            version = await self.get_current_published_version(article.id)
            if not version:
                version = await self._article_repo.get_latest_version(article.id)

            if primary_mapping is None:
                primary_mapping = m
                primary_article = article
                primary_version = version
            elif len(related) < 3:
                related.append((article, version))

            if primary_mapping is not None and len(related) >= 3:
                break

        if primary_mapping is None:
            return ContextualHelpResolutionResult()

        return ContextualHelpResolutionResult(
            matched_mapping=primary_mapping,
            primary_article=primary_article,
            primary_version=primary_version,
            section_anchor=primary_mapping.section_anchor,
            related_articles=related,
        )


__all__ = [
    "ArticleLifecycleService",
    "ContextualHelpResolutionResult",
]
