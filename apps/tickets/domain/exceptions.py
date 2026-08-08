"""
Domain exceptions for Tickets microservice.
"""

from packages.hexagonal import (
    DomainError,
    EntityAlreadyExistsError,
    EntityNotFoundError,
    ValidationError,
)


class TicketDomainError(DomainError):
    """Base domain error for tickets."""

    pass


class TicketNotFoundError(EntityNotFoundError):
    """Raised when a ticket entity is not found."""

    pass


class TicketAlreadyExistsError(EntityAlreadyExistsError):
    """Raised when a ticket entity already exists."""

    pass


class TicketInvalidTransitionError(TicketDomainError):
    """Raised when an invalid ticket status transition is attempted."""

    pass


class TicketOptimisticLockingError(TicketDomainError):
    """Raised when an optimistic locking version mismatch occurs."""

    pass


__all__ = [
    "DomainError",
    "EntityAlreadyExistsError",
    "EntityNotFoundError",
    "TicketAlreadyExistsError",
    "TicketDomainError",
    "TicketInvalidTransitionError",
    "TicketNotFoundError",
    "TicketOptimisticLockingError",
    "ValidationError",
]
