import functools
from abc import ABC, abstractmethod
from typing import Any, Generic, TypeVar

from sqlalchemy.exc import IntegrityError, NoResultFound, SQLAlchemyError

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
# 2. Database Exception Mapper
# ==========================================


def map_database_exceptions(func):
    """Decorator to translate SQLAlchemy database errors to clean domain exceptions."""

    @functools.wraps(func)
    async def wrapper(*args, **kwargs):
        try:
            return await func(*args, **kwargs)
        except NoResultFound as e:
            raise EntityNotFoundError(f"Requested entity not found: {e}") from e
        except IntegrityError as e:
            raise EntityAlreadyExistsError(
                f"Database constraint violation or duplicate key: {e}"
            ) from e
        except SQLAlchemyError as e:
            raise DatabaseError(f"Database operational or schema error: {e}") from e

    return wrapper


# ==========================================
# 3. Hexagonal Ports (Interfaces)
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
