from packages.hexagonal import DomainError, EntityNotFoundError, ValidationError


class CTMSDomainError(DomainError):
    pass


class CTMSDelegationNotFoundError(EntityNotFoundError, CTMSDomainError):
    pass


class CTMSValidationError(ValidationError, CTMSDomainError):
    pass


class GreenlightPrerequisiteError(CTMSDomainError):
    """Raised when greenlight certification fails due to unapproved prerequisites."""

    pass


class DeviationNotFoundError(EntityNotFoundError, CTMSDomainError):
    """Raised when a protocol deviation is not found."""

    pass


class ConcurrencyConflictError(CTMSDomainError):
    """Raised when an optimistic locking / version index conflict occurs."""

    pass


class ActionItemNotFoundError(EntityNotFoundError, CTMSDomainError):
    """Raised when a deviation action item is not found."""

    pass


class GrantLockedError(CTMSDomainError):
    """Raised when attempting to modify a finalized/approved investigator grant."""

    pass


class IPQuarantineError(CTMSDomainError):
    """Raised when attempting to dispense or use quarantined/expired IP kits."""

    pass


class IPKitNotFoundError(EntityNotFoundError, CTMSDomainError):
    """Raised when an IP kit is not found."""

    pass


class RBQMThresholdBreachError(CTMSDomainError):
    """Raised when a QTL threshold breach occurs."""

    pass
