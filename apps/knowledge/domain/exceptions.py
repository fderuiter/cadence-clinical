"""
Domain exceptions for the Knowledge & Support Hub microservice.

Requirements: PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002
"""

from packages.hexagonal import (
    ConflictError,
    EntityAlreadyExistsError,
    EntityNotFoundError,
    UnauthorizedActionError,
    ValidationError,
)


class ArticleNotFoundError(EntityNotFoundError):
    """Raised when a requested knowledge article cannot be found."""


class ArticleTransitionError(ValidationError):
    """Raised when an article status transition is not permitted by the state machine."""


class ArticleApprovalConflictError(UnauthorizedActionError):
    """Raised when the four-eyes principle is violated: author or editor cannot approve."""


class ArticleReasonRequiredError(ValidationError):
    """Raised when a regulated transition is attempted without a reason_for_change."""


class ArticleVersionImmutableError(ConflictError):
    """Raised when an approved or published version snapshot is attempted to be mutated."""


class CategoryNotFoundError(EntityNotFoundError):
    """Raised when a referenced category or parent category cannot be found."""


class CategoryConflictError(EntityAlreadyExistsError):
    """Raised when category name or slug is not unique."""


class CategoryCircularParentError(ValidationError):
    """Raised when category parent creates a circular reference."""


__all__ = [
    "ArticleApprovalConflictError",
    "ArticleNotFoundError",
    "ArticleReasonRequiredError",
    "ArticleTransitionError",
    "ArticleVersionImmutableError",
    "CategoryCircularParentError",
    "CategoryConflictError",
    "CategoryNotFoundError",
]
