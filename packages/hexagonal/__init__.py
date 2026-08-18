"""Cadence Clinical Enterprise Hexagonal Architecture Kernel.

Provides foundational domain primitives, event dispatcher ports, standard persistence ports,
and domain exception definitions for clean architecture across all microservices.
"""

import uuid
from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from datetime import UTC, datetime
from typing import Any, TypeVar

# ==========================================
# 1. Base Domain Exceptions & HTTP Mappings
# ==========================================


class DomainError(Exception):
    """Base exception for all domain errors."""

    def __init__(
        self,
        message: str = "Domain error occurred",
        details: dict[str, Any] | None = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.details = details or {}


class EntityNotFoundError(DomainError):
    """Raised when a domain entity is not found (HTTP 404)."""

    pass


class EntityAlreadyExistsError(DomainError):
    """Raised when an entity with duplicate unique attributes already exists (HTTP 409)."""

    pass


class ValidationError(DomainError):
    """Raised when domain business rules or invariants are violated (HTTP 422)."""

    pass


class DatabaseError(DomainError):
    """Raised when a database level error occurs (HTTP 500)."""

    pass


class UnauthorizedActionError(DomainError):
    """Raised when an action violates domain authorization rules (HTTP 403)."""

    pass


class PreconditionFailedError(DomainError):
    """Raised when an entity precondition is not met (HTTP 412)."""

    pass


class ConflictError(DomainError):
    """Raised when concurrent state conflicts occur (HTTP 409)."""

    pass


def map_domain_error_to_http_status(err: DomainError) -> int:
    """Maps a DomainError subclass to its canonical HTTP status code."""
    mapping = {
        EntityNotFoundError: 404,
        EntityAlreadyExistsError: 409,
        ValidationError: 422,
        UnauthorizedActionError: 403,
        PreconditionFailedError: 412,
        ConflictError: 409,
        DatabaseError: 500,
    }
    for err_cls, status in mapping.items():
        if isinstance(err, err_cls):
            return status
    return 400


@dataclass
class ProblemDetails:
    """RFC 7807 Problem Details representation for HTTP APIs."""

    type: str = "about:blank"
    title: str = "An error occurred"
    status: int = 400
    detail: str = ""
    instance: str | None = None
    invalid_params: list[dict[str, Any]] | None = None
    extensions: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Serializes ProblemDetails to standard RFC 7807 JSON structure."""
        data: dict[str, Any] = {
            "type": self.type,
            "title": self.title,
            "status": self.status,
            "detail": self.detail,
        }
        if self.instance is not None:
            data["instance"] = self.instance
        if self.invalid_params:
            data["invalid_params"] = self.invalid_params
        if self.extensions:
            data.update(self.extensions)
        return data


def create_problem_details_from_domain_error(
    err: DomainError, instance: str | None = None
) -> dict[str, Any]:
    """Builds an RFC 7807 compliant problem details dict from a DomainError."""
    status = map_domain_error_to_http_status(err)
    titles = {
        EntityNotFoundError: "Entity Not Found",
        EntityAlreadyExistsError: "Entity Already Exists",
        ValidationError: "Validation Error",
        UnauthorizedActionError: "Unauthorized Action",
        PreconditionFailedError: "Precondition Failed",
        ConflictError: "Conflict",
        DatabaseError: "Database Error",
    }
    type_slugs = {
        EntityNotFoundError: "entity-not-found",
        EntityAlreadyExistsError: "entity-already-exists",
        ValidationError: "validation-error",
        UnauthorizedActionError: "unauthorized-action",
        PreconditionFailedError: "precondition-failed",
        ConflictError: "conflict",
        DatabaseError: "database-error",
    }
    err_cls = err.__class__
    title = titles.get(err_cls, "Domain Error")
    type_slug = type_slugs.get(err_cls, "domain-error")
    problem = ProblemDetails(
        type=f"https://cadence.clinical/errors/{type_slug}",
        title=title,
        status=status,
        detail=str(err.message or err),
        instance=instance,
        extensions=err.details if hasattr(err, "details") and err.details else {},
    )
    return problem.to_dict()


def register_rfc7807_handlers(app: Any) -> None:
    """Registers standard RFC 7807 exception handlers on a FastAPI application."""
    try:
        from fastapi import Request
        from fastapi.responses import JSONResponse

        @app.exception_handler(DomainError)
        async def domain_error_handler(request: Request, exc: DomainError):
            status = map_domain_error_to_http_status(exc)
            body = create_problem_details_from_domain_error(
                exc, instance=str(request.url)
            )
            return JSONResponse(
                status_code=status,
                content=body,
                media_type="application/problem+json",
            )

    except ImportError:
        pass


# ==========================================
# 2. Domain Events & Primitives
# ==========================================


@dataclass(frozen=True)
class DomainEvent:
    """Immutable domain event recorded when significant domain state transitions occur."""

    event_id: str = field(default_factory=lambda: str(uuid.uuid4()))
    occurred_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    event_type: str = field(init=False)

    def __post_init__(self) -> None:
        object.__setattr__(self, "event_type", self.__class__.__name__)


ID = TypeVar("ID")
T = TypeVar("T")


class ValueObject:
    """Base class for domain value objects characterized by structural equality."""

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, self.__class__):
            return False
        return self.__dict__ == other.__dict__

    def __hash__(self) -> int:
        return hash(tuple(sorted(self.__dict__.items())))


class BaseEntity[ID]:
    """Base domain entity characterized by unique identity and lifecycle metadata."""

    def __init__(
        self,
        id: ID,
        version_index: int = 1,
        created_at: datetime | None = None,
    ) -> None:
        self.id = id
        self.version_index = version_index
        self.created_at = created_at or datetime.now(UTC)

    def __eq__(self, other: object) -> bool:
        if not isinstance(other, self.__class__):
            return False
        return self.id == other.id

    def __hash__(self) -> int:
        return hash((self.__class__, self.id))


class AggregateRoot[ID](BaseEntity[ID]):
    """Aggregate Root entity responsible for maintaining domain invariants and managing domain events."""

    def __init__(
        self,
        id: ID,
        version_index: int = 1,
        created_at: datetime | None = None,
    ) -> None:
        super().__init__(id=id, version_index=version_index, created_at=created_at)
        self._domain_events: list[DomainEvent] = []

    def record_event(self, event: DomainEvent) -> None:
        """Appends a domain event to the aggregate's internal event queue."""
        self._domain_events.append(event)

    def flush_events(self) -> list[DomainEvent]:
        """Returns all recorded domain events and clears the internal event queue."""
        events = list(self._domain_events)
        self._domain_events.clear()
        return events

    def clear_events(self) -> None:
        """Clears all uncommitted domain events without returning them."""
        self._domain_events.clear()


# ==========================================
# 3. Hexagonal Driving & Driven Ports
# ==========================================


class RepositoryPort[T](ABC):
    """Abstract driven port for persistence operations."""

    @abstractmethod
    async def get_by_id(self, entity_id: str) -> T | None:
        """Retrieve entity by primary identifier."""
        pass

    @abstractmethod
    async def save(self, entity: T) -> T:
        """Persist or update entity state."""
        pass

    async def delete(self, entity_id: str) -> bool:
        """Remove entity by identifier. Optional in repositories."""
        raise NotImplementedError(
            "Delete operation not implemented for this repository."
        )


class UseCasePort(ABC):
    """Abstract driving port for application business use cases."""

    @abstractmethod
    async def execute(self, *args: Any, **kwargs: Any) -> Any:
        """Execute the application use case."""
        pass


class ExternalServiceClientPort(ABC):
    """Abstract driven port for communicating with upstream or external microservices."""

    @abstractmethod
    async def is_healthy(self) -> bool:
        """Check if remote service is reachable."""
        pass


class AuditLoggerPort(ABC):
    """Abstract driven port for 21 CFR Part 11 compliant audit trail logging."""

    @abstractmethod
    async def log_action(
        self,
        user_id: str,
        action: str,
        resource_type: str,
        resource_id: str,
        reason_for_change: str,
        details: dict[str, Any] | None = None,
    ) -> None:
        """Record an immutable, compliant audit log entry."""
        pass


class EventDispatcherPort(ABC):
    """Abstract driven port for publishing domain events."""

    @abstractmethod
    async def dispatch(self, event: DomainEvent) -> None:
        """Publish a domain event to subscribed listeners."""
        pass
