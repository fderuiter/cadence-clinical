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


class FileQuarantinedError(DomainError):
    """Raised when an action is blocked because the file failed malware scanning and is quarantined."""

    def __init__(
        self,
        message: str = "File is quarantined due to detected malware.",
        reason: str = "QUARANTINED",
    ) -> None:
        super().__init__(message)
        self.reason = reason


class FileScanError(DomainError):
    """Raised when a virus scanning engine error or timeout occurs."""

    pass


class FileImmutableError(DomainError):
    """Raised when an action is blocked because the file is approved and immutable."""

    def __init__(
        self,
        message: str = "DOCUMENT_APPROVED_IMMUTABLE: Document is approved and immutable.",
        reason: str = "DOCUMENT_APPROVED_IMMUTABLE",
    ) -> None:
        super().__init__(message)
        self.reason = reason


class InvalidCredentialError(DomainError):
    """Raised when in-product re-authentication credential verification fails."""

    pass
