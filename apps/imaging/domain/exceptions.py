"""Domain exceptions for Medical Imaging Adjudication workflows.

Requirements: PRD-IMG-003, PRD-IMG-005, PRD-SYS-001
"""


class AdjudicationDomainError(Exception):
    """Base exception for all imaging adjudication domain errors."""

    pass


class AdjudicationSessionNotFoundError(AdjudicationDomainError):
    """Raised when the requested adjudication session cannot be found.

    Args:
        session_id: The unique identifier of the missing session.
    """

    def __init__(self, session_id: str) -> None:
        super().__init__(f"Adjudication session '{session_id}' not found.")
        self.session_id = session_id


class AdjudicationSignOffRationaleRequiredError(AdjudicationDomainError):
    """Raised when 21 CFR Part 11 step-up sign-off is attempted without a clinical rationale."""

    def __init__(
        self,
        message: str = "Clinical justification rationale is mandatory for 21 CFR Part 11 adjudication sign-off.",
    ) -> None:
        super().__init__(message)


class AdjudicationValidationError(AdjudicationDomainError):
    """Raised when adjudication data or reader assessment fails validation checks."""

    pass


class InvalidAdjudicationStateError(AdjudicationDomainError):
    """Raised when an operation is invalid for the current workflow status."""

    pass
