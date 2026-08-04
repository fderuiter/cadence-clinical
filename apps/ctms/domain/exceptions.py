from packages.hexagonal import DomainError, EntityNotFoundError, ValidationError


class CTMSDomainError(DomainError):
    pass


class CTMSDelegationNotFoundError(EntityNotFoundError, CTMSDomainError):
    pass


class CTMSValidationError(ValidationError, CTMSDomainError):
    pass
