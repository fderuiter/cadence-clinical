"""Domain exceptions for Medical Imaging Adjudication workflows.

Requirements: PRD-IMG-003, PRD-IMG-005, PRD-SYS-001
"""


from packages.hexagonal import DomainError, EntityNotFoundError, ValidationError


class AdjudicationDomainError(DomainError):
    """Base exception for all imaging adjudication domain errors."""

    pass


class AdjudicationSessionNotFoundError(AdjudicationDomainError, EntityNotFoundError):
    """Raised when the requested adjudication session cannot be found.

    Args:
        session_id: The unique identifier of the missing session.
    """

    def __init__(self, session_id: str) -> None:
        super().__init__(f"Adjudication session '{session_id}' not found.")
        self.session_id = session_id


class AdjudicationSignOffRationaleRequiredError(AdjudicationDomainError, ValidationError):
    """Raised when 21 CFR Part 11 step-up sign-off is attempted without a clinical rationale."""

    def __init__(
        self,
        message: str = "Clinical justification rationale is mandatory for 21 CFR Part 11 adjudication sign-off.",
    ) -> None:
        super().__init__(message)


class AdjudicationValidationError(AdjudicationDomainError, ValidationError):
    """Raised when adjudication data or reader assessment fails validation checks."""

    pass


class InvalidAdjudicationStateError(AdjudicationDomainError, ValidationError):
    """Raised when an operation is invalid for the current workflow status."""

    pass
