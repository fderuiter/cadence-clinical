# ADR-2455: Decouple GxP Audit and Clinical Transaction Architecture

* **Status:** Accepted
* **Date:** 2026-08-05
* **Authors:** @google-labs-jules[bot], @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, our clinical trial delegation and verification logic within Cadence Clinical were directly coupled to database session lifecycles and physical transaction controls. This architectural coupling posed significant GxP compliance and system stability risks:
1. **Partial or Incomplete Audits:** If an operation failed mid-process, database transaction rollbacks could leave application-level audit logs partially committed or out-of-sync.
2. **Session Leakage:** Database sessions leaked outside of the active request lifecycle, causing connection pool exhaustion.
3. **Impaired Testing and Simulation:** Testing core compliance and domain logic required live, active database connections, hindering fast unit tests and isolated in-memory verification.

To guarantee 100% compliant, atomic GxP clinical audits and secure transactional behavior, we need a decoupled transaction architecture following a hexagonal-style structure.

## 2. Decision Drivers & Constraints

* **Compliance:** Enforce absolute compliance under **PRD-SYS-001** with 21 CFR Part 11 and GxP standards for clinical audits and delegation state tracking.
* **Atomicity:** Guarantee that all database actions and application-level audits succeed together or roll back completely.
* **Decoupling:** Separate business rules (e.g. delegation and verification) from persistence engines, enabling isolated database-free execution.
* **Testing:** Maintain high test coverage and allow complete, in-memory validation of GxP changes.

## 3. Options Considered

### Option 1: Request-Bound Session Filter with Unified Exception Boundary
* **Overview:** Bind database transaction lifecycles directly to the HTTP request context. Wrap execution pathways in standard middleware that catches exceptions and issues a rollback for both database modifications and stateful auditor stages.
* **Pros:**
  * ✅ High reliability and simple transaction handling.
  * ✅ Ensures no database sessions are leaked outside request contexts.
* **Cons:**
  * ❌ Requires refactoring existing routing and service injection points to align with request scope.

### Option 2: Maintain Explicit Database Session Lifecycle Controls (De-selected)
* **Overview:** Allow routes and services to manually instantiate, commit, and close database transactions.
* **Pros:**
  * ✅ Fine-grained transaction boundaries.
* **Cons:**
  * ❌ Extreme risk of connection pool leaks and uncommitted transaction states on unexpected failures.
  * ❌ Violates hexagonal architecture principles by blending persistence and domain layers.

## 4. Decision Outcome

* **Chosen Option:** Option 1 (Request-Bound Session with Unified Exception Boundary)
* **Justification:** Option 1 provides the rigorous guarantees required for GxP validation. It aligns database session lifecycles cleanly with HTTP request/response boundaries, guarantees atomic rollback of audits on exception, and enables isolated in-memory testing of domain algorithms.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * ✅ Zero session leaks and 100% atomic GxP audits.
  * ✅ In-memory, database-free unit test capabilities.
  * ✅ Clean compliance reporting synced automatically with the Requirements Traceability Matrix.
* **Negative Impact / Technical Debt:**
  * ❌ Initial refactoring overhead for stateful service endpoints.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `apps/execution/`
  * `apps/gateway/`
  * `packages/ui/`
* **Verification Plan:**
  * Validate using programmatic architectural and unit test suites:
    ```bash
    uv run pytest tests/test_decoupled_services_in_memory.py
    ```
  * Verify documentation sync via:
    ```bash
    uv run python scripts/sync_gxp.py
    ```
