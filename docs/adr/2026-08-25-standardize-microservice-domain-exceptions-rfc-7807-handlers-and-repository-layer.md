# ADR-2400: Standardize Microservice Domain Exceptions RFC 7807 Handlers and Repository Layer

Also known as: ADR-2217 (former number)

- **Status:** Accepted
- **Date:** 2026-08-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Microservices across the Cadence eClinical Platform previously used inconsistent exception types and HTTP error responses. Generic `ValueError` exceptions were raised directly in domain services (e.g. site payment ledger engine, billing/ERP gateway, participant compensation service), and database ORM exceptions could leak implementation details across service boundaries. Standardizing domain exceptions and RFC 7807 `application/problem+json` error responses is required to satisfy PRD-SYS-001.

## 2. Decision Drivers & Constraints

* Unified exception hierarchy inheriting from `packages.hexagonal.DomainError`.
* Standardized RFC 7807 problem details error responses across all FastAPI microservices.
* Database exception mapping via `@map_database_exceptions` decorator on repository persistence methods.
* Compliance with GxP 21 CFR Part 11 audit and error reporting standards (PRD-SYS-001).

## 3. Options Considered

### Option 1: Standardize microservice domain exceptions extending `packages.hexagonal.DomainError`, register RFC 7807 error handlers on all FastAPIs, and decorate repository methods with database exception mapping (Selected).

- **Overview:** Standardize microservice domain exceptions extending `packages.hexagonal.DomainError`, register RFC 7807 error handlers on all FastAPIs, and decorate repository methods with database exception mapping (Selected).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: Allow ad-hoc exception types and HTTP responses across services (Rejected - leads to inconsistent client handling and ORM leakages).

- **Overview:** Allow ad-hoc exception types and HTTP responses across services (Rejected - leads to inconsistent client handling and ORM leakages).
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** Option 1 because it establishes a clear, consistent hexagonal domain exception hierarchy and automated RFC 7807 error handling across all platform microservices while preventing ORM leakages.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- **Positive Impact:** Consistent `application/problem+json` responses for all API clients and decoupled database exception mapping.
- **Negative Impact / Technical Debt:** Requires explicit exception mapping across repository implementations.
- **Mitigation Strategy:** Managed via automated tests and verification.

## 6. Implementation & Verification

* Refactored domain exceptions in `apps/payments`, `apps/execution`, and `apps/imaging` to inherit from `packages.hexagonal.DomainError`.
* Registered `register_rfc7807_handlers(app)` across all 14 FastAPI microservices.
* Applied `@map_database_exceptions` to repository persistence methods.
* Verified via `uv run python scripts/validate_adrs.py` and `uv run pytest`.
