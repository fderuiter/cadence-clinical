# ADR-112: Durable and Idempotent eTMF Expiration Scanning Service

- **Status:** Accepted
- **Date:** 2026-08-26
- **Authors:** @jules
- **Deciders:** @fderuiter
- **Requirement Reference:** PRD-SYS-001

---

## 1. Context & Problem Statement

An asynchronous and robust mechanism is required to detect upcoming and past document expirations in the eTMF module. The service must scan documents with non-null `expiration_date` fields against configurable warning thresholds (e.g., 90, 30, and 7 days) and an expired state, persistently tracking whether a specific alert window has already been emitted for a given document to prevent duplicate alerts.

## 2. Decision Drivers & Constraints

- **GxP 21 CFR Part 11 Compliance:** Scanning processes must execute with a dedicated, auditable service identity (`expiration_scanner`) via standard `service_audit_context`, and metadata updates must carry this identity.
- **Idempotency:** A document must generate at most one alert/event per warning window, requiring a persistent dedup-state table with db-level unique constraints.
- **Failure Isolation:** Any individual scanning iteration or insertion error must be isolated to prevent thread/loop termination.
- **Lifecycle Integration:** The background scanning task must cleanly hook into FastAPI startup and shutdown lifetime events.

## 3. Options Considered

### Option 1: Log-based tracking

- **Overview:** Infer previous alerts from existing system or audit logs without a dedicated state table.
- **Pros:**
  - ✅ No new database schema required.
- **Cons:**
  - ❌ Unreliable across log retention rotations or format changes.
  - ❌ Hard to programmatically query and re-arm.

### Option 2: Persistent State Table (Selected)

- **Overview:** Track alert-delivery state in a dedicated table `tmf_document_expiration_alert_states` with a database-level unique constraint on `(document_id, warning_window)`.
- **Pros:**
  - ✅ Guaranteed database-level uniqueness/idempotency.
  - ✅ Clean and explicit support for "re-arming" (deleting a row allows alert re-emission).
  - ✅ Supports standard Part 11 audit fields (`created_at`, `created_by`, `reason_for_change`).
- **Cons:**
  - ❌ Requires schema modifications.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing a persistent state table with a database-level unique constraint on `(document_id, warning_window)` ensures absolute correctness, GxP trace compliance, and clean re-arming.

## 5. Consequences & Trade-offs

- **Positive Impact:** Guaranteed single-alert emissions per warning window, fully auditable and compliant with 21 CFR Part 11.
- **Negative Impact / Technical Debt:** Added a small table to the eTMF relational schema.
- **Mitigation Strategy:** Database triggers/filters are bypassed for eTMF alert writes in the global execution `before_flush` listener to avoid schema resolution issues.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/etmf`, `packages/security`
- **Verification Plan:** Full test coverage under `tests/test_etmf_expiration_scanner.py` validating threshold boundaries, idempotency, restart, failure isolation, shutdown, and audit attribution.
