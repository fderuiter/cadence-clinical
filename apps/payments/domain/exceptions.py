"""Domain exceptions for the Site Payments microservice.

Requirements: PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, ADR-2199
"""

from packages.hexagonal import (
    ConflictError,
    DatabaseError,
    DomainError,
    EntityNotFoundError,
    ValidationError,
)


class PaymentsDomainError(DomainError):
    """Base exception for all payment service domain errors."""

    pass


class ContractNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when a budget or payment contract is not found."""

    pass


class PayableNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when an investigator payable is not found."""

    pass


class InvoiceNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when a self-billing invoice is not found."""

    pass


class WalletNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when a participant wallet is not found."""

    pass


class ClaimNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when a travel expense claim is not found."""

    pass


class LaySummaryNotFoundError(EntityNotFoundError, PaymentsDomainError):
    """Raised when an EU CTR lay summary record is not found."""

    pass


class PaymentsValidationError(ValidationError, PaymentsDomainError):
    """Raised when payment domain validation rules fail."""

    pass


class PaymentsConflictError(ConflictError, PaymentsDomainError):
    """Raised when duplicate or conflicting payment operations occur."""

    pass


class PaymentsDatabaseError(DatabaseError, PaymentsDomainError):
    """Raised when database persistence operations fail in the payments domain."""

    pass
