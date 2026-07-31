# ADR-117: eTMF Expiration Alert Notification Dispatch and Recipient Routing

* **Status:** Accepted
* **Date:** 2026-08-27
* **Authors:** @jules
* **Deciders:** @fderuiter, @gxp-lead
* **Requirement Reference:** PRD-SYS-001 | GxP 21 CFR Part 11

---

## 1. Context & Problem Statement
The eTMF module contains a background process (ADR-112) that periodically scans for upcoming and past document expirations. While scanning generates durable internal state records to avoid duplication, there is a critical need to actively notify human users (such as Document Owners or clinical trial associates/monitors) about these events.

The dispatch boundary must securely post authenticated notification requests to the external Notifications service (V2 HMAC-SHA256 Gateway signature), track the dispatch results/failure details programmatically within the alert state table, and gracefully retry failed dispatches in subsequent cycles.

## 2. Decision Drivers & Constraints
* **GxP & 21 CFR Part 11 Auditability:** Every successful and failed notification dispatch attempt must be recorded in the eTMF audit trail with appropriate context.
* **Deterministic Idempotency:** Only one authenticated notification request must be produced per warning window. Deterministic related entity IDs should prevent duplicate delivery.
* **Recipient Routing Resolution:** When the document owner metadata (`document_owner_id`) is available, the dispatch must route to that owner. When it is missing, the system must route to the documented responsible role fallback: Clinical Research Associate (`"CRA"`).
* **Robust Retryability:** Dispatches must not block other dispatches and must keep alert states as retryable (i.e., `dispatched = False`) if delivery fails due to HTTP or network errors.

## 3. Options Considered
### Option 1: Direct inline HTTP calls during scanning
* **Overview:** Execute HTTP calls directly as we detect expired documents, before committing the alert state records.
* **Pros:**
  * ✅ Simplifies code flow within a single loops block.
* **Cons:**
  * ❌ Severe risk of duplicate notifications if the scanning transaction rolls back after an HTTP call succeeds.
  * ❌ Network latency directly affects the scanning cycle duration.

### Option 2: Post-Commit Two-Phase Dispatch (Selected)
* **Overview:** Scan and commit alert states as pending (undispatched) first. In the second phase, load undispatched alerts, resolve recipients, attempt signed HTTP posts, and commit the dispatch updates individually in nested transaction blocks.
* **Pros:**
  * ✅ Prevents duplicate alerts if database commits fail.
  * ✅ Safe, retryable, and highly resilient to network and HTTP transient failures.
  * ✅ Part 11 compliant with atomic audits matching state transitions.
* **Cons:**
  * ❌ Small added complexity of two-step processing.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Choosing the post-commit two-phase dispatch ensures strict transactional safety (no phantom notifications), robust retryability, and clean audit logs with precise error details.

## 5. Consequences & Trade-offs
* **Positive Impact:** Strong transactional alignment, robust error tracing, and clean Part 11 traceability.
* **Negative Impact / Technical Debt:** Added four columns to the alert state table to manage retry states, attempts, and error details.
* **Mitigation Strategy:** These columns are fully covered by schema migrations and populated gracefully during automated scans.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/etmf`, `apps/notifications`
* **Verification Plan:** Full test coverage under `tests/test_etmf_expiration_scanner.py` including:
  * Signed request signature validation.
  * Document owner recipient routing.
  * Fallback `"CRA"` role routing.
  * Successful dispatch state transitions.
  * Retry logic and failure tracking.
  * Atomic GxP audit trail logging.
