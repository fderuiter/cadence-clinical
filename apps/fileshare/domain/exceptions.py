"""Domain exceptions for fileshare microservice.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

from packages.hexagonal import (
    DomainError,
    EntityNotFoundError,
    PreconditionFailedError,
    UnauthorizedActionError,
    ValidationError,
)


class FileNotFoundError(EntityNotFoundError):
    """Raised when a requested file record is not found."""

    pass


class FileSharePermissionDeniedError(UnauthorizedActionError):
    """Raised when caller lacks required permission on a file."""

    pass


class InvalidGrantError(ValidationError):
    """Raised when a share grant configuration is invalid or expired."""

    pass


class GuestLinkExpiredError(PreconditionFailedError):
    """Raised when an external guest link token is expired or revoked."""

    pass


class FileOnHoldError(DomainError):
    """Raised when an action is blocked due to active legal/regulatory retention hold."""

    pass

