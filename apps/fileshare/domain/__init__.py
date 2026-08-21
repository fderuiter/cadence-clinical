"""Domain models and exceptions for fileshare microservice."""

from apps.fileshare.domain.exceptions import (
    FileNotFoundError,
    FileOnHoldError,
    FileSharePermissionDeniedError,
    GuestLinkExpiredError,
    InvalidGrantError,
)
from apps.fileshare.domain.models import (
    FileRecord,
    GuestLink,
    PermissionLevel,
    ShareGrant,
    ShareScope,
)

__all__ = [
    "FileNotFoundError",
    "FileOnHoldError",
    "FileRecord",
    "FileSharePermissionDeniedError",
    "GuestLink",
    "GuestLinkExpiredError",
    "InvalidGrantError",
    "PermissionLevel",
    "ShareGrant",
    "ShareScope",
]

