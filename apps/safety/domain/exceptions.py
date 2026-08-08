from packages.hexagonal import DomainError


class SafetyDomainError(DomainError):
    """Base domain error for Safety microservice."""

    pass
