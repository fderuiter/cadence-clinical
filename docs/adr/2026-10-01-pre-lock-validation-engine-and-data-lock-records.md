# ADR-2803: Pre-Lock Validation Engine and Data Lock Records

- **Status:** Accepted
- **Date:** 2026-10-01
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Clinical research data management requires strict controls before locking data at study, site, subject, visit, form, or field levels. Locking unverified source data or open queries violates GxP compliance. We need an automated pre-lock discrepancy and source data validation engine and data lock record persistence in the Execution service.
@req:PRD-SYS-001

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce pre-flight validation checks (open queries, unanswered queries, unverified source data) across a 6-tier scope hierarchy (Study, Site, Subject, Visit, Form, Field).
- **Driver 2:** Require explicit override justification when locking scopes with outstanding discrepancies, auditing all lock and unlock actions (21 CFR Part 11).

## 3. Options Considered

### Option 1: Dedicated PreLockValidationEngine with DataLockRecord Aggregate in Execution Service (Selected)

- **Overview:** Implement `PreLockValidationEngine` and `DataLockRecord` aggregate root in `apps/execution/`, backed by EF Core migration `20261001163350_AddDataLockRecords`.
- **Pros:**
  - ✅ Supports 6-tier scope hierarchy validation with pre-flight check blocks
  - ✅ Full audit trail for data locks, unlocks, and overrides
- **Cons:**
  - ❌ Adds database schema table `data_lock_records` and application service complexity

### Option 2: Inline Validation without Persistent Lock State

- **Overview:** Perform transient checks on lock request without persistent `DataLockRecord` entities.
- **Pros:**
  - ✅ No additional database tables required
- **Cons:**
  - ❌ Fails GxP audit trail and lock state query requirements

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because persistent data lock records and structured pre-lock validation provide compliant auditability and robust discrepancy controls.

## 5. Consequences & Trade-offs

- **Positive Impact:** Clear operational boundaries and compliance tracing for data locking workflows.
- **Negative Impact / Technical Debt:** Additional database table and migration maintenance.
- **Mitigation Strategy:** Comprehensive test coverage in unit and integration test suites.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/execution/`
- **Verification Plan:** Unit tests in `PreLockValidationEngineTests` and `DataLockApplicationServiceTests`, and integration tests against PostgreSQL.
