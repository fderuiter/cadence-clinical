from abc import ABC, abstractmethod
from typing import Any, Generic, TypeVar

# ==========================================
# 1. Base Domain Exceptions
# ==========================================


class DomainError(Exception):
    """Base exception for all domain errors."""

    pass


class EntityNotFoundError(DomainError):
    """Raised when a domain entity is not found."""

    pass


class EntityAlreadyExistsError(DomainError):
    """Raised when an entity with duplicate unique attributes already exists."""

    pass


class ValidationError(DomainError):
    """Raised when domain business rules or invariants are violated."""

    pass


class DatabaseError(DomainError):
    """Raised when a database level error occurs."""

    pass


# ==========================================
# 2. Hexagonal Ports (Interfaces)
# ==========================================


T = TypeVar("T")


class RepositoryPort(Generic[T], ABC):  # noqa: UP046
    """Abstract driven port for persistence operations."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> T | None:
        pass

    @abstractmethod
    async def save(self, entity: T) -> T:
        pass


class UseCasePort(ABC):
    """Abstract driving port for application business use cases."""

    @abstractmethod
    async def execute(self, *args: Any, **kwargs: Any) -> Any:
        pass
