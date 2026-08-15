# ADR-2179: Comprehensive Hexagonal Architecture Standardization and Frontend Modularization

- **Status:** Accepted
- **Date:** 2026-08-14
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As the Cadence Clinical platform scales across 14 microservices, multiple shared packages, and two web applications (`apps/web` and `apps/subject-portal`), architectural drift has emerged in several areas:
1. Microservices have accumulated loose root-level service and helper files, mixed directory layouts (`infrastructure/` vs `adapters/` vs `services/`), and inconsistent exception handling.
2. API errors returned varying formats across services rather than a unified RFC 7807 problem details specification.
3. The patient/subject portal frontend (`apps/subject-portal`) accumulated logic in monolithic single files (`App.vue`, `index.js`, `style.css`, `sync-queue.js`) rather than modular components, views, and stores.

To satisfy system requirement **PRD-SYS-001**, we establish a comprehensive platform-wide architectural standardization covering the 4-layer Hexagonal layout, plural `adapters/` consolidation, RFC 7807 error formatting, and frontend modularization.

## 2. Decision Drivers & Constraints

- **GxP Immutability & Auditability (PRD-SYS-001):** Enforce strict decoupling between pure clinical domain logic, persistence layers, and HTTP delivery mechanics.
- **Structural Convergence:** Standardize directory layouts across all 14 microservices so developer tooling, linters, and architectural sentinels operate uniformly.
- **Standardized API Error Contracts:** Align all HTTP 4xx and 5xx error responses with RFC 7807 problem details (`type`, `title`, `status`, `detail`, `instance`, `invalid_params`).
- **Frontend Modularity & Design Token Parity:** Refactor monolithic frontend structures into modular Vue components and Pinia stores, sharing semantic CSS design tokens from `packages/ui/tokens.css`.
- **Zero Dead Code:** Directly refactor in-place and remove obsolete directories, loose files, and legacy shims.

## 3. Options Considered

### Option 1: Comprehensive Full-Stack Standardization (Selected)

- **Backend Hexagonal Layering:** Every service under `apps/*` strictly conforms to `domain/` (pure entities & exceptions), `application/` (ports & use cases), `adapters/` (persistence ORM, repositories, external HTTP clients, background workers), and `presentation/` (FastAPI routers & DTOs).
- **Consolidation on Plural `adapters/`:** Retire all `infrastructure/` and `services/` folders in favor of canonical `adapters/` and `application/`.
- **Centralized RFC 7807 Problem Details:** Register standard `DomainError` exception handlers via `packages.hexagonal.register_rfc7807_handlers(app)` across all microservices.
- **Frontend Modularization:** Modularize `apps/subject-portal` into `src/views/`, `src/components/`, `src/stores/`, and `src/services/` sharing tokens with `apps/web`.
- **Package Cleanup:** Purge empty and unused packages such as `packages/deid`.

### Option 2: Partial / Backend-Only Migration

- Refactor only primary backend services (`designer`, `execution`, `etmf`, `ctms`, `quality`) while keeping satellite services and frontend structures unchanged.
- **Trade-off:** Leaves architectural fragmentation in place and increases maintenance burden over time.

## 4. Decision Outcome

Chosen option: **Option 1 (Comprehensive Full-Stack Standardization)**.

### Architectural Rules:
1. **`apps/<service>/domain/`:** Pure Python entities, value objects, domain invariants, and domain exceptions subclassing `packages.hexagonal.DomainError`. No framework dependencies (no SQLAlchemy, no FastAPI).
2. **`apps/<service>/application/`:** Interface ports (subclassing `RepositoryPort[T]`, `ExternalServiceClientPort`, `UseCasePort`) and application use case orchestrators.
3. **`apps/<service>/adapters/`:** Persistence models (SQLAlchemy / SQLModel), database repositories, external HTTP clients, and background workers.
4. **`apps/<service>/presentation/`:** FastAPI routers, Pydantic DTO schemas, and response serializers.
5. **`apps/<service>/main.py`:** Thin FastAPI bootstrap with lifespan lifecycle, `GatewayAuthMiddleware`, and `register_rfc7807_handlers(app)`.
6. **Frontend Modularization:** Shared design tokens in `packages/ui/tokens.css` consumed by both `apps/web` and `apps/subject-portal`.

## 5. Consequences & Trade-offs

- **Positive:**
  - Standardized, predictable directory hierarchies across all 14 microservices.
  - Uniform RFC 7807 error responses improving API contract reliability.
  - Modularized frontend code with clean component boundaries and reusability.
  - Seamless automated validation across all 10 quality gates.
- **Negative:**
  - One-time refactoring overhead across multiple services and test import paths.

## 6. Implementation & Verification

- **Shared Kernel:** `packages/hexagonal` updated with `ProblemDetails` and `register_rfc7807_handlers`.
- **Service Refactoring:** Consolidated `infrastructure/` and `services/` into `adapters/` and `application/` across `apps/*`.
- **Frontend Refactoring:** Modularized `apps/subject-portal` and aligned token usage.
- **Verification:** All 10 architecture sentinels and quality gates validated via `uv run cadence check --parallel`, with full test suite passing via `uv run pytest -n auto`.
