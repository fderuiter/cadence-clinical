"""
Clinical Domain Exceptions for hexagonal decoupling.
"""

from packages.hexagonal import DomainError as BaseDomainError
from packages.hexagonal import EntityNotFoundError, ValidationError


class DomainError(BaseDomainError):
    """Base exception for all domain-level errors.

    Used for framework-agnostic error propagation across service boundaries,
    ensuring decoupling from web or persistence exceptions.
    """

    def __init__(self, message: str):
        super().__init__(message)
        self.message = message


class SubjectEligibilityError(DomainError, ValidationError):
    """Raised when subject eligibility checks fail or eligibility status is invalid."""

    pass


class CodingAssignmentNotFoundError(DomainError, EntityNotFoundError):
    """Raised when a medical coding assignment cannot be found."""

    pass


class InvalidCodingActionError(DomainError, ValidationError):
    """Raised when an invalid coding action (ACCEPT/OVERRIDE/QUERY) or validation fails."""

    pass


class DictionaryNotFoundError(DomainError, EntityNotFoundError):
    """Raised when a dictionary type or version is not found or unsupported."""

    pass


class ChangeRequestNotFoundError(DomainError, EntityNotFoundError):
    """Raised when a compliance change request cannot be found."""

    pass


class InvalidChangeRequestActionError(DomainError, ValidationError):
    """Raised when a compliance change request action fails GxP verification."""

    pass
