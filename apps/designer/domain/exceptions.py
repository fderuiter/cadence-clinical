"""
Domain exception classes for Designer microservice.
Inherits from packages.hexagonal.DomainError.
"""

from packages.hexagonal import (
    DatabaseError,
    DomainError,
    EntityAlreadyExistsError,
    EntityNotFoundError,
    ValidationError,
)


class ImmutabilityViolationError(DomainError):
    """Raised when attempting to modify a locked or published entity."""

    pass


class ConcurrentLockingError(DomainError):
    """Raised when an optimistic lock conflict occurs."""

    pass


class InvalidSignatureError(DomainError):
    """Raised when digital signature validation fails for a study version."""

    pass


class LibraryObjectInUseError(DomainError):
    """Raised when a library object cannot be modified because it is referenced in an active study."""

    pass


class LibraryObjectLockedActiveStudyError(DomainError):
    """Raised when a library object is locked due to active recruiting study usage."""

    def __init__(self, object_id: str | None = None, message: str | None = None):
        self.object_id = object_id
        self.message = (
            message
            or f"Library object '{object_id}' is referenced by an Active-Recruiting study and is locked against direct modifications. Please use the protocol amendment workflow."
        )
        super().__init__(self.message)


class ConceptLockedError(DomainError):
    """Raised when attempting to modify a concept referenced by an active-recruiting study."""

    def __init__(self, concept_id: str, message: str | None = None):
        self.concept_id = concept_id
        self.message = (
            message
            or f"Concept '{concept_id}' is referenced by an Active-Recruiting study and is locked against direct modifications. Please use the protocol amendment workflow."
        )
        super().__init__(self.message)


__all__ = [
    "ConceptLockedError",
    "ConcurrentLockingError",
    "DatabaseError",
    "DomainError",
    "EntityAlreadyExistsError",
    "EntityNotFoundError",
    "ImmutabilityViolationError",
    "InvalidSignatureError",
    "LibraryObjectInUseError",
    "LibraryObjectLockedActiveStudyError",
    "ValidationError",
]
