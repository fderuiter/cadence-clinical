"""
Pure domain models, enums, and state machine for the Knowledge & Support Hub microservice.

Requirements: PRD-SYS-KH-001 (article lifecycle), PRD-SYS-KH-002 (GxP compliance)
"""

from dataclasses import dataclass
from enum import StrEnum


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
# (Note: SUPERSEDED and ARCHIVED both allow revert to DRAFT, so neither is truly terminal.)
PUBLISHED_STATE = ArticleStatus.PUBLISHED


def validate_transition(
    current_status: ArticleStatus,
    target_status: ArticleStatus,
    reason_for_change: str | None,
    actor_user_id: str,
    last_edited_by: str | None,
) -> None:
    """
    Validates an article status transition against the state machine and GxP rules.

    Enforces:
    - Allowed transition graph (ARTICLE_TRANSITIONS)
    - reason_for_change requirement on regulated transitions
    - Four-eyes principle: the actor who last edited cannot approve

    Args:
        current_status: The article's current ArticleStatus.
        target_status: The desired ArticleStatus after transition.
        reason_for_change: Justification string; required on regulated transitions.
        actor_user_id: The user initiating the transition.
        last_edited_by: The user who last saved the article body (for four-eyes check).

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


class ArticleTransitionError(ValueError):
    """Raised when an article status transition is not permitted by the state machine."""


class ArticleApprovalConflictError(PermissionError):
    """Raised when the four-eyes principle is violated: editor cannot also approve."""


class ArticleReasonRequiredError(ValueError):
    """Raised when a regulated transition is attempted without a reason_for_change."""


class CategoryNotFoundError(ValueError):
    """Raised when a referenced category or parent category cannot be found."""


class CategoryConflictError(ValueError):
    """Raised when category name or slug is not unique."""


class CategoryCircularParentError(ValueError):
    """Raised when category parent creates a circular reference."""


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
