from packages.hexagonal import DomainError, EntityNotFoundError


class OrgDomainError(DomainError):
    """Base domain exception for Organization service."""

    pass


class OrganizationNotFoundError(EntityNotFoundError):
    """Raised when an organization is not found."""

    pass


class SiteNotFoundError(EntityNotFoundError):
    """Raised when a site is not found."""

    pass


class PersonnelNotFoundError(EntityNotFoundError):
    """Raised when a personnel record is not found."""

    pass
