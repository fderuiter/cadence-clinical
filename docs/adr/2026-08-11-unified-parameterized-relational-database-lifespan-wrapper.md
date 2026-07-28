# ADR-068: Unified Parameterized Relational Database Lifespan Wrapper

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

Across our clinical trial microservices architecture, multiple services implemented duplicate database managers, connection wrappers, and manual startup/shutdown event handlers. Empty subclasses (such as `ETMFDatabaseManager` or `CTMSDatabaseManager`) were declared solely to hardcode service names, adding unnecessary boilerplate and maintenance friction. In addition, services like `eConsent`, `eISF`, `Notifications`, and `Interop` built bespoke database initialization and transaction helpers from scratch.

To simplify the database integration layer, standardise SQLite schema initialization, and streamline lifecycle/background tasks (such as the Notifications Poller or eTMF Cryptographic Sealer), we require a unified, parameterized lifespan helper in the core database library (`packages/database`).

This decision implements requirements under Trace-8.

## 2. Decision Drivers & Constraints

- **Type Safety & Style Enforcement:** Strictly typed implementation with comprehensive Docstrings matching PEP 8 / Black / Ruff configurations.
- **Microservice Autonomy:** Individual services must retain control over their routing and middleware without the core database lifespan helper modifying their FastAPI app configurations.
- **Asynchronous Lifecycles:** Asynchronous startup/shutdown callbacks must be supported to prevent blocking the shared application runtime.
- **Transaction Consistency:** Request-scoped APIs must be unified under the shared `DatabaseSessionDependency` transaction helper to prevent session leakage and promote automatic commit/rollback safety.

## 3. Options Considered

### Option 1: Manual Context Manager Wrapping
Each service continues to manage its own lifespan context manager, explicitly initializing and closing the `RelationalDatabaseManager`.

- **Pros:** Maximum control inside the microservice.
- **Cons:** Boilerplate duplication, higher chance of forgetting schema creations or engine cleans.

### Option 2: Parameterized Lifespan Generator Helper
Expose a core utility function `get_relational_db_lifespan` inside `packages/database` that yields a generic FastAPI lifespan context manager. This generator takes optional schema metadata and lists of asynchronous startup/shutdown hooks.

- **Pros:**
  - ✅ Zero boilerplate in individual microservices.
  - ✅ Automatic handling of SQLite database/table initialization and engine disposal.
  - ✅ Robust concurrent or sequential hook execution.
  - ✅ Promotes standard `DatabaseSessionDependency` for scoping requests.
- **Cons:** None.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Standardizes all microservice database setups, ensures clean connection tearing, and makes background runner tasks (such as eTMF sealer or Notifications poller) decoupled, registered, and testable.

## 5. Consequences & Trade-offs

- **Positive Impact:** Less duplicated code, improved connection pooling reliability, robust error handling during startup/shutdown sequences.
- **Negative Impact / Technical Debt:** Requires updating all test suites that verified service-specific uninitialized manager classes, which are cleanly replaced by `RelationalDatabaseManager`.

## 6. Implementation & Verification

- **Affected Services:** `eConsent`, `eISF`, `Notifications`, `Interop`, `eTMF`, `CTMS`, `Org`, `Quality`, `Safety`.
- **Verification Plan:** Validated via automated unit and integration tests under `tests/test_database_managers.py` and service-specific tests. Verified total system coverage satisfies the 80% threshold.
