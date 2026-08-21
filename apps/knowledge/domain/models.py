"""
Pure domain models, enums, and state machine for the Knowledge & Support Hub microservice.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002
"""

from dataclasses import dataclass
from enum import StrEnum

from apps.knowledge.domain.exceptions import (
    ArticleApprovalConflictError,
    ArticleNotFoundError,
    ArticleReasonRequiredError,
    ArticleTransitionError,
    ArticleVersionImmutableError,
    CategoryCircularParentError,
    CategoryConflictError,
    CategoryNotFoundError,
)
from apps.knowledge.domain.markdown_renderer import render_markdown_to_html


class ArticleStatus(StrEnum):
    """
    Lifecycle states for a KnowledgeArticle controlled document.

    Implements a GxP-compliant seven-state machine enforcing four-eyes approval,
    immutable published content, and full audit trail per 21 CFR Part 11.
    """

    DRAFT = "DRAFT"
    IN_REVIEW = "IN_REVIEW"
    APPROVED = "APPROVED"
    PUBLISHED = "PUBLISHED"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"
    REJECTED = "REJECTED"


class ArticleAuditAction(StrEnum):
    """
    Enumeration of all auditable actions on a KnowledgeArticle.

    All actions emit an AuditLogRecord to the SHA-256 digest chain.
    """

    CREATED = "CREATED"
    DRAFT_SAVED = "DRAFT_SAVED"
    SUBMITTED_FOR_REVIEW = "SUBMITTED_FOR_REVIEW"
    APPROVED = "APPROVED"
    REJECTED = "REJECTED"
    PUBLISHED = "PUBLISHED"
    SUPERSEDED = "SUPERSEDED"
    ARCHIVED = "ARCHIVED"
    READ_BY_AUDITOR = "READ_BY_AUDITOR"


# Valid state transitions for ArticleStatus.
# Maps each state to the set of states it may transition into.
ARTICLE_TRANSITIONS: dict[ArticleStatus, set[ArticleStatus]] = {
    ArticleStatus.DRAFT: {ArticleStatus.IN_REVIEW},
    ArticleStatus.IN_REVIEW: {
        ArticleStatus.APPROVED,
        ArticleStatus.REJECTED,
    },
    ArticleStatus.APPROVED: {
        ArticleStatus.PUBLISHED,
        ArticleStatus.DRAFT,  # allow revert to draft before publish
    },
    ArticleStatus.REJECTED: {ArticleStatus.DRAFT},
    ArticleStatus.PUBLISHED: {
        ArticleStatus.ARCHIVED,
        ArticleStatus.SUPERSEDED,  # system-triggered when newer version published
    },
    ArticleStatus.SUPERSEDED: {ArticleStatus.DRAFT},  # can draft a new version
    ArticleStatus.ARCHIVED: {ArticleStatus.DRAFT},  # reopen as new draft
}

# Transitions that require a non-empty reason_for_change.
REASON_REQUIRED_TRANSITIONS: set[ArticleStatus] = {
    ArticleStatus.APPROVED,
    ArticleStatus.PUBLISHED,
    ArticleStatus.ARCHIVED,
    ArticleStatus.SUPERSEDED,
}

# Terminal states: once reached, no further transitions are expected without deliberate action.
PUBLISHED_STATE = ArticleStatus.PUBLISHED


def validate_transition(
    current_status: ArticleStatus,
    target_status: ArticleStatus,
    reason_for_change: str | None,
    actor_user_id: str,
    last_edited_by: str | None = None,
    author_user_id: str | None = None,
) -> None:
    """
    Validates an article status transition against the state machine and GxP rules.

    Enforces:
    - Allowed transition graph (ARTICLE_TRANSITIONS)
    - reason_for_change requirement on regulated transitions
    - Four-eyes principle: the actor who authored or last edited cannot approve

    Args:
        current_status: The article's current ArticleStatus.
        target_status: The desired ArticleStatus after transition.
        reason_for_change: Justification string; required on regulated transitions.
        actor_user_id: The user initiating the transition.
        last_edited_by: The user who last saved the article body (for four-eyes check).
        author_user_id: The user who originally created the article (for four-eyes check).

    Raises:
        ArticleTransitionError: If the transition is not permitted.
        ArticleApprovalConflictError: If the four-eyes principle is violated.
        ArticleReasonRequiredError: If reason_for_change is missing on a regulated transition.
    """
    allowed = ARTICLE_TRANSITIONS.get(current_status, set())
    if target_status not in allowed:
        raise ArticleTransitionError(
            f"Transition from {current_status!r} to {target_status!r} is not permitted. "
            f"Allowed targets: {sorted(s.value for s in allowed)}"
        )

    if target_status == ArticleStatus.APPROVED:
        if author_user_id and author_user_id == actor_user_id:
            raise ArticleApprovalConflictError(
                "The original author of this article cannot approve it. "
                "A different user must perform the approval (four-eyes principle, 21 CFR Part 11)."
            )
        if last_edited_by and last_edited_by == actor_user_id:
            raise ArticleApprovalConflictError(
                "The user who last edited this article cannot also approve it. "
                "A different user must perform the approval (four-eyes principle, 21 CFR Part 11)."
            )

    if target_status in REASON_REQUIRED_TRANSITIONS:
        if not reason_for_change or not reason_for_change.strip():
            raise ArticleReasonRequiredError(
                f"A reason_for_change is required when transitioning to {target_status!r}."
            )


@dataclass
class ArticleSnapshot:
    """
    Lightweight snapshot of a KnowledgeArticle for notification dispatch and audit emission.
    """

    id: str
    slug: str
    title: str
    status: ArticleStatus
    version_index: int
    version_label: str
    author_user_id: str
    last_edited_by: str | None
    approved_by: str | None


__all__ = [
    "ARTICLE_TRANSITIONS",
    "PUBLISHED_STATE",
    "REASON_REQUIRED_TRANSITIONS",
    "ArticleApprovalConflictError",
    "ArticleAuditAction",
    "ArticleNotFoundError",
    "ArticleReasonRequiredError",
    "ArticleSnapshot",
    "ArticleStatus",
    "ArticleTransitionError",
    "ArticleVersionImmutableError",
    "CategoryCircularParentError",
    "CategoryConflictError",
    "CategoryNotFoundError",
    "render_markdown_to_html",
    "validate_transition",
]
