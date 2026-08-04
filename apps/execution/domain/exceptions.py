from packages.hexagonal import DomainError, EntityNotFoundError, ValidationError


class ExecutionDomainError(DomainError):
    pass


class ExecutionDelegationNotFoundError(EntityNotFoundError, ExecutionDomainError):
    pass


class ExecutionStaffNotFoundError(EntityNotFoundError, ExecutionDomainError):
    pass


class ExecutionValidationError(ValidationError, ExecutionDomainError):
    pass
