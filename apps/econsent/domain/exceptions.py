"""Domain exceptions for eConsent microservice.

Complies with RFC 7807 problem details and hexagonal domain design.
"""

from packages.hexagonal import DomainError


class EConsentDomainError(DomainError):
    """Base domain error for eConsent microservice."""


class ClauseNotFoundError(EConsentDomainError):
    """Raised when a specified consent clause cannot be located."""


class TemplateNotFoundError(EConsentDomainError):
    """Raised when a specified consent template cannot be located."""


class TemplateNotPublishedError(EConsentDomainError):
    """Raised when attempting an operation that requires a PUBLISHED template."""


class TranslationNotFoundError(EConsentDomainError):
    """Raised when a requested consent translation does not exist."""


class InvalidTranslationTransitionError(EConsentDomainError):
    """Raised when a translation status transition is prohibited by policy."""


class ComprehensionCheckNotFoundError(EConsentDomainError):
    """Raised when a template version has no comprehension check configured."""


class ComprehensionCheckFailedError(EConsentDomainError):
    """Raised when subject fails comprehension check and is blocked from signing."""


class ComprehensionLockoutError(EConsentDomainError):
    """Raised when subject exceeds maximum quiz attempts and is locked out."""


class SignerRoleValidationError(EConsentDomainError):
    """Raised when a required signer role or credential validation fails."""


class DuplicateSignatureError(EConsentDomainError):
    """Raised when a signature from the same role is re-submitted on active consent."""


class ReconsentRequiredError(EConsentDomainError):
    """Raised when an operation requires an updated re-consent that is missing."""


class ConsentWithdrawnError(EConsentDomainError):
    """Raised when interacting with a subject record whose consent was formally revoked."""
