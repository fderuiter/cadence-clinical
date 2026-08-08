# Project: Hexagonal Architecture Migration (14 Microservices)

## Architecture
Standardized 4-Layer Hexagonal Architecture per service:
- `domain/`: Entities, value objects, domain exceptions, repository interfaces (ports inheriting from `packages.hexagonal.RepositoryPort`). No external framework imports (no FastAPI, no SQLAlchemy).
- `application/`: Use cases, services, DTOs. Depends only on `domain/` and shared packages.
- `infrastructure/`: Database implementations (`repositories/`), external APIs, ORM models/mappers. Implements `domain/` repository ports.
- `presentation/`: FastAPI routers, request/response models. `main.py` contains only FastAPI setup and router inclusions.

Shared Packages & Architecture:
- `packages/hexagonal/`: Contains `RepositoryPort` base class, shared hexagonal utilities, and `pytest-archon` boundary tests (`tests/test_hexagonal_architecture.py`).
- `packages/compliance/`: Moved from `apps/compliance/` (shared library without HTTP endpoints).

## Feature Inventory
| # | Feature / Microservice | Description | Subagent Worker | Source |
|---|------------------------|-------------|-----------------|--------|
| 1 | Phase 0 Foundation & `compliance` | Remove `sqlalchemy` from `packages/hexagonal/__init__.py`, move `map_database_exceptions` to `packages/database/`, ruff exclusion for `execution` models, create ADR `2026-08-08-hexagonal-architecture-standard.md`, move `apps/compliance/` to `packages/compliance/` and update references. | Subagent 1 | R1, R3 |
| 2 | Thin & Medium 9 Microservices | Migrate `gateway`, `interop`, `notifications`, `org`, `safety`, `econsent`, `quality`, `eisf`, `etmf` to 4 flat layers (`domain/`, `application/`, `infrastructure/`, `presentation/`). Router extraction from `main.py`. Repository ports inherit from `RepositoryPort`. Delete `src/` dirs. | Subagent 2 | R2, R3 |
| 3 | CTMS & Execution | Migrate `ctms` and `execution` to 4 flat layers. Iteratively split 236KB repository file in `ctms` into `infrastructure/repositories/`. Resolve domain duplication in `execution`. Clean `main.py`. | Subagent 3 | R2 |
| 4 | Designer & Tickets | Migrate `designer` and `tickets` to 4 flat layers. Extract 5,788-line `main.py` in `designer` into routers, application, domain, infrastructure. Split massive repositories. Clean `main.py`. | Subagent 4 | R4 |
| 5 | Boundary Enforcement & Verification | Implement `pytest-archon` boundary tests in `packages/hexagonal/tests/test_hexagonal_architecture.py`. Run full test suite, coverage check (≥80%), ruff check/format, `validate_imports.py`, and `sync_gxp.py`. | Subagent 5 | R4, AC |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | M1: Foundation & Compliance | R1 + Move compliance library | None | DONE |
| 2 | M2: Thin & Medium Services (9 services) | gateway, interop, notifications, org, safety, econsent, quality, eisf, etmf | M1 | DONE |
| 3 | M3: Complex Services (CTMS & Execution) | ctms, execution | M1 | DONE |
| 4 | M4: High Complexity Services (Designer & Tickets) | designer, tickets | M1 | DONE |
| 5 | M5: Boundary Enforcement & Final Verification | pytest-archon tests, full test suite, coverage, ruff, validate_imports, GxP sync | M2, M3, M4 | DONE |

## Code Layout
Target structure for each microservice in `apps/<service>/`:
- `apps/<service>/domain/` (models, exceptions, repository ports)
- `apps/<service>/application/` (services, use cases)
- `apps/<service>/infrastructure/` (repositories, external adapters, ORM)
- `apps/<service>/presentation/` (routers, endpoints)
- `apps/<service>/main.py` (FastAPI app instantiation, router inclusions ONLY)
