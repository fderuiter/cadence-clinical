from packages.hexagonal import DomainError


class ETMFDomainError(DomainError):
    """Base domain error for eTMF microservice."""

    pass


class DocumentAlreadySignedError(ETMFDomainError):
    """Raised when attempting to re-sign or modify a signed document."""

    pass


class DocumentNotFoundError(ETMFDomainError):
    """Raised when a requested TMF document is not found."""

    pass


class ImmutabilityViolationError(ETMFDomainError):
    """Raised when an immutable document or record is illegally mutated."""

    pass


class InvalidTransitionError(ETMFDomainError):
    """Raised when a document lifecycle transition is invalid."""

    pass


class TrialLockedError(ETMFDomainError):
    """Raised when a mutation is attempted on a locked trial."""

    pass
