# ADR-2167: Subclass DatabaseSessionManager and Align Repository Exception Handlers

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To standardize the database session management and exception handling workflows across the microservices, the execution engine needs to align with the database abstractions defined in `packages/database`. Specifically, `DatabaseSessionManager` in `apps/execution/database/core.py` should subclass `RelationalDatabaseManager` to eliminate duplication and enforce standard SQLite-to-PostgreSQL connection emulations. In addition, repository methods in the execution infrastructure should consistently map database exceptions to domain exceptions using the shared `@map_database_exceptions` decorator.

This decision addresses the core system requirement **Trace-1** for unified relational database connection lifecycles and consistent error translations across services.

## 2. Decision Drivers & Constraints

* **Maintainability:** Ensure that the database session management logic is shared from a central package (`packages/database`) instead of being duplicated.
* **Compatibility:** Enable SQLite in-memory emulation of PostgreSQL-specific features like session contexts (`set_config`, `current_setting`) and UUID generation (`gen_random_uuid`) during testing.
* **Consistency (Trace-1):** Enforce consistent translation of SQLAlchemy exceptions into domain-level errors across all primary repositories in the execution service.

## 3. Options Considered

### Option 1: Legacy Standalone Session Manager (Superseded)
* **Overview:** Maintain separate standalone database session managers across microservices with distinct exception handling logic.
* **Pros:**
  * ✅ No dependency on a shared package.
* **Cons:**
  * ❌ High code duplication and increased technical debt.
  * ❌ Inconsistent exception handling formats across services.

### Option 2: Shared Hexagonal Database Base Class & Central Decorators (Selected)
* **Overview:** Subclass `RelationalDatabaseManager` from `packages/database` and decorate repositories with the centralized `@map_database_exceptions` decorator.
* **Pros:**
  * ✅ Standardized initialization sequence and centralized connection lifecycle rules.
  * ✅ SQLite emulation logic cleanly contained and integrated with standard setup hooks.
  * ✅ Uniform exception translations to domain errors across all primary clinical repositories.
* **Cons:**
  * ❌ Slight abstraction overhead.

## 4. Decision Outcome

**Chosen Option:** Option 2 was selected because it completely aligns the database lifecycle management with the hexagonal standard, satisfies GxP traceability (Trace-1), and simplifies future relational storage upgrades or migrations.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * Code base simplification by inheriting standard features.
  * Streamlined integration and offline tests by sharing robust PostgreSQL emulation utilities.
  * Standardized audit and tracking workflows across microservices.
* **Negative Impact / Technical Debt:**
  * Introducing a shared package dependency which requires updates to be propagated carefully.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `apps/execution/database/core.py`: Refactored to subclass `RelationalDatabaseManager`.
  * `apps/execution/infrastructure/repositories/execution_repositories.py`: Annotated repository read/write operations with `@map_database_exceptions`.
* **Verification Plan:**
  * Verified unit tests via `pytest` to confirm SQLite emulations continue to function.
  * Ran local static and architectural validation checks (`validate_adrs.py`, `validate_imports.py`) to confirm zero linting or dependency regressions.
