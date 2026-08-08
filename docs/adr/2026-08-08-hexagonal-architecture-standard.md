# ADR-2165: Hexagonal Architecture Standard

- **Status:** Accepted
- **Date:** 2026-08-08
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As the Cadence Clinical Research Software Platform grows across 14 microservices and multiple shared packages, maintaining strict separation between pure clinical domain logic, application use cases, persistence adapters, and HTTP presentation layers is essential for GxP compliance (21 CFR Part 11) and long-term system maintainability.

Prior to standardizing, microservices exhibited mixed responsibilities, direct database queries in API routes, and duplicated repository interfaces. To satisfy system requirement **PRD-SYS-001**, a unified 4-layer flat Hexagonal Architecture standard is established across all microservices.

## 2. Decision Drivers & Constraints

- **GxP Immutability & Auditability (PRD-SYS-001):** Domain business logic must be decoupled from ORM infrastructure to guarantee testability and eliminate unintended side effects.
- **Pure Domain Layer Isolation:** Domain models and business rules must remain pure Python with zero framework dependencies (no SQLAlchemy, no FastAPI, no Pydantic ORM bindings).
- **Standardized Port Interfaces:** All service-specific repository ports must inherit from `packages.hexagonal.RepositoryPort` to ensure type safety and interface consistency.
- **Shared Exception Translation:** Infrastructure adapters must map database operational exceptions (e.g. SQLAlchemy `NoResultFound`, `IntegrityError`) to clean domain exceptions (`EntityNotFoundError`, `EntityAlreadyExistsError`, `DatabaseError`) via `@map_database_exceptions` in `packages.database`.
- **Programmatic Boundary Enforcement:** Layer dependencies must be validated in CI via `pytest-archon` static architecture tests.

## 3. Options Considered

### Option A: Standardized 4-Layer Flat Hexagonal Architecture (Chosen)

Every microservice in `apps/` follows a standardized 4-layer flat directory hierarchy:

```
apps/<service_name>/
├── domain/
│   ├── models.py       # Pure Python entities & value objects
│   ├── exceptions.py   # Domain-specific exception classes (inheriting from packages.hexagonal.DomainError)
│   └── services.py     # Pure domain logic & calculation engines
├── application/
│   ├── ports.py        # Driving/driven interface ports (repository ports inherit from RepositoryPort)
│   └── use_cases.py    # Business use cases executing domain workflows (inheriting from UseCasePort)
├── infrastructure/
│   ├── models.py       # SQLAlchemy ORM models (if applicable)
│   └── repositories.py # Database repository implementations wrapping SQLAlchemy session operations
└── presentation/
    ├── dtos.py         # Pydantic request/response schemas
    └── routers.py      # FastAPI API routers mapping HTTP requests to application use cases
```

### Layer Rules & Isolation Guarantees

1. **`domain/` (Inner Core):** Contains business entities, domain exceptions, and core rules. Imports ONLY standard library and `packages.hexagonal`. No framework imports (FastAPI, SQLAlchemy).
2. **`application/` (Use Cases & Ports):** Defines driving and driven ports. Repository ports inherit from `packages.hexagonal.RepositoryPort[T]`. Application use cases implement `packages.hexagonal.UseCasePort`. Depend ONLY on `domain/` and `packages.hexagonal`.
3. **`infrastructure/` (Adapters & Persistence):** Contains persistence models, database repositories, external HTTP client adapters, and event handlers. Maps database exceptions using `@map_database_exceptions` from `packages.database`. Depends on `domain/` and `application/`.
4. **`presentation/` (Delivery Mechanism):** Contains FastAPI routers and Pydantic DTOs. Handles HTTP serialization, authentication via `packages.security`, and delegates execution to application use cases. Depends on `application/` and `domain/`.

## 4. Decision Outcome

Chosen option: **Standardized 4-Layer Flat Hexagonal Architecture**.

- Base interface ports (`RepositoryPort`, `UseCasePort`) and base exceptions (`DomainError`, `EntityNotFoundError`, `EntityAlreadyExistsError`, `ValidationError`, `DatabaseError`) reside in `packages/hexagonal`.
- Framework-dependent exception translation (`@map_database_exceptions`) resides in `packages/database`.
- Service-specific repository ports explicitly subclass `packages.hexagonal.RepositoryPort`.

## 5. Consequences & Trade-offs

- **Positive:**
  - Clear architectural boundaries across all microservices.
  - Complete decoupling of core clinical logic from database engines and web frameworks.
  - Enhanced unit testability using lightweight in-memory repository implementations.
  - Automated verification of architectural constraints via `pytest-archon`.
- **Negative:**
  - Additional boilerplate when creating new domain entities and mapping to persistence models.

## 6. Implementation & Verification

- **Base Framework:** Updated `packages/hexagonal` to be pure Python and moved `@map_database_exceptions` to `packages/database`.
- **Shared Library Migration:** Extracted `apps/compliance` into `packages/compliance`.
- **Automated Enforcement:** Verified via `pytest-archon` test suite in `packages/hexagonal/tests/test_hexagonal_architecture.py`.
