# ADR-2173: Decoupled GxP Audit and Database-Level Lock Enforcement Triggers

* **Status:** Accepted
* **Date:** 2026-08-13
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent unauthorized, untracked, or direct mutations of clinical data within the PostgreSQL and SQLite execution databases (meeting GxP 21 CFR Part 11 requirements under PRD-SYS-001), we need secure database-level trigger-based locks. If alternative database access tools or ORMs (such as Drizzle) are used, they must not bypass trial-wide or site-level locks. We also need to propagate thread-safe session contexts (such as `cadence.current_user_id`, `cadence.current_change_reason`, and trial locking state parameters) consistently across all connection sessions.

## 2. Decision Drivers & Constraints

* **Compliance (PRD-SYS-001):** Mandate database-level locks on insertions, updates, and deletions of audited records, completely independent of application ORMs.
* **Maintainability & Parity:** Provide unified SQLite/PostgreSQL triggers while avoiding duplicate definition blocks that trigger code duplication threshold errors.
* **Testability:** Support mock databases and in-memory testing setups cleanly.

## 3. Options Considered

### Option 1: ORM-Only Interception

Perform all locking and auditing validations purely within the SQLAlchemy `before_flush` hooks.
* **Pros:** Simple implementation inside application code.
* **Cons:** Bypassed if other database clients (such as Drizzle or raw SQL scripts) mutate the database.

### Option 2: Database-Level Trigger Enforcement with Consolidated Context Propagation (Selected)

Implement direct, native SQL triggers in PostgreSQL/SQLite that validate hierarchies of lock checks (trial, site, visit, subject, form) on every mutation, alongside a centralized helper `propagate_session_context` to propagate session-level config values.
* **Pros:** Bulletproof security that cannot be bypassed by raw SQL or alternative ORMs. Consolidating the propagation logic avoids redundant blocks of code.
* **Cons:** Requires schema migrations and trigger maintenance.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Choosing database-level trigger enforcement ensures strict compliance with 21 CFR Part 11 (PRD-SYS-001). Consolidating session context propagation logic under a single helper prevents code duplication while maintaining operational parity.

## 5. Consequences & Trade-offs

* **Positive Impact:** Robust hierarchical lock enforcement at the database layer. No code duplication warnings.
* **Negative Impact:** Slightly more complex setup during schema provisioning and testing.
* **Mitigation Strategy:** Provide helper functions in `packages/database` to standardize session state updates and trigger deployment.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution/`, `packages/database/`
* **Verification Plan:** Verified via native triggers and unit/integration tests under `apps/execution/tests/test_form_submissions.py` and `apps/execution/tests/test_clinical_queries.py`.
