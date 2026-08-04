"""
Clinical Domain Exceptions for hexagonal decoupling.
"""


class DomainError(Exception):
    """Base exception for all domain-level errors.

    Used for framework-agnostic error propagation across service boundaries,
    ensuring decoupling from web or persistence exceptions.
    """

    def __init__(self, message: str):
        super().__init__(message)
        self.message = message


class SubjectEligibilityError(DomainError):
    """Raised when subject eligibility checks fail or eligibility status is invalid."""

    pass


class CodingAssignmentNotFoundError(DomainError):
    """Raised when a medical coding assignment cannot be found."""

    pass


class InvalidCodingActionError(DomainError):
    """Raised when an invalid coding action (ACCEPT/OVERRIDE/QUERY) or validation fails."""

    pass


class DictionaryNotFoundError(DomainError):
    """Raised when a dictionary type or version is not found or unsupported."""

    pass


class ChangeRequestNotFoundError(DomainError):
    """Raised when a compliance change request cannot be found."""

    pass


class InvalidChangeRequestActionError(DomainError):
    """Raised when a compliance change request action fails GxP verification."""

    pass
