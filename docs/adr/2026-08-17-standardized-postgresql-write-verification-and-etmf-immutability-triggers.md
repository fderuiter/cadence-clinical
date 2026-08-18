# ADR-[NUMBER]: Standardized PostgreSQL Write Verification and eTMF Immutability Triggers

- **Status:** Accepted
- **Date:** 2026-08-17
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter
- **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement

In clinical trial applications governed by regulatory frameworks like GxP and FDA 21 CFR Part 11, data integrity, auditability, and immutability are non-negotiable requirements. When records are modified or deleted, transaction-linked session variables for user identity and reasons for change must be verified. Additionally, finalized Quality Control (QC) statuses/transitions must be absolutely immutable, and certain foundational documents must have delete operations blocked at the database level.

## 2. Decision Drivers & Constraints

- **Driver 1:** Absolute compliance with GxP and FDA 21 CFR Part 11 requirements for audit trail and immutability.
- **Driver 2:** Robust enforcement at the database layer (PostgreSQL and SQLite emulation) to prevent unauthorized updates/deletes from any application path or direct DB access.
- **Driver 3:** Clean transactional boundaries and verification of session variables before allowing changes.

## 3. Options Considered

### Option 1: Application-Level Triggers and Auditing

- **Overview:** Rely entirely on application services and SQLAlchemy/SQLModel listeners to validate change reasons and block deletes.
- **Pros:**
  - ✅ Simple to implement in Python.
  - ✅ Same code runs on SQLite and PostgreSQL.
- **Cons:**
  - ❌ Leaves the database vulnerable to bypasses if direct SQL queries or database-level operations are run.
  - ❌ Lacks absolute guarantees of compliance at the persistence layer.

### Option 2: Database-Native Write Verification and Immutability Triggers (Selected)

- **Overview:** Enforce constraints via database-native triggers.
- **Pros:**
  - ✅ Guaranteed GxP compliance at the persistence layer.
  - ✅ Blocks any bypass from direct SQL or misconfigured backend routes.
  - ✅ Custom SQLite emulation ensures local and CI tests match production PostgreSQL behavior.
- **Cons:**
  - ❌ Higher complexity in writing database-level SQL triggers and replicating behaviors in test environments.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Enforcing compliance at the lowest level of the persistence layer guarantees absolute immutability and precise auditing without relying on the correctness of application code.

## 5. Consequences & Trade-offs

- **Positive Impact:** All modifications or deletions to GxP-sensitive tables require verified transaction context (user identity and change reason), ensuring perfect compliance. Finalized eTMF document status and transitions are fully immutable.
- **Negative Impact / Technical Debt:** Requires mocking transaction contexts in test suites and keeping PostgreSQL triggers and SQLite emulated triggers perfectly synchronized.
- **Mitigation Strategy:** Created a comprehensive trigger compliance test suite to verify SQLite trigger emulation matching PG's GxP triggers.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf/`, `apps/execution/`
- **Verification Plan:** Validated via automated test suite `test_etmf_triggers_compliance.py` and standard migration validation tests.
