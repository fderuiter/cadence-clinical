# ADR-100: Tickets-to-Notifications Integration Architecture

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** Jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform supports support ticket management in the `tickets` service and system-wide notifications via the `notifications` service. Prior to this ADR, mutations on support tickets (e.g., ticket creation, assignment, comment addition, status transition) were saved to the database but did not dispatch alerts to stakeholders.

To satisfy clinical auditability and platform usability requirements, the platform must notify appropriate users of these ticketing lifecycle events.

## 2. Decision Drivers & Constraints
* **GxP 21 CFR Part 11 Compliance:** The notification engine must be fully auditable, and dispatch failures must be separated from core database state mutations, as mapped under PRD-SYS-001.
* **Failure Isolation:** A network or notification service failure must never cause a support ticket transaction to fail or roll back after it has been successfully committed.
* **Authentication Contract:** Internal service-to-service communication must satisfy the Gateway HMAC-SHA256 V2 authentication requirements.
* **Deduplication:** Repeated event notifications must be protected against duplicates at the recipient side.

## 3. Options Considered
### Option 1: Inline Synchronous Request inside DB Transactions
* **Pros:** Simple, immediate notification.
* **Cons:** Network delays slow down user requests. If the notification service returns an error, the entire database transaction is rolled back, causing data loss in the ticketing service.

### Option 2: Post-Commit Background Dispatch (FastAPI BackgroundTasks) (Chosen)
* **Pros:**
  * Delivers high performance by executing notification dispatch asynchronously after the database transaction has committed.
  * Ensures that any network or transport failures are gracefully swallowed, logged, and isolated from the core ticketing transaction.
  * Safe and decoupled, utilizing FastAPI's native `BackgroundTasks` wrapper.
  * Allows capturing fully committed entity states (IDs, references, and version indexes).

## 4. Decision Outcome
We adopted **Option 2**. We implemented a robust ticketing notifications integration with the following key components:

1. **Foundational Client:** An async client `publish_notification` mirroring the execution service's implementation, signing requests using HMAC-SHA256 Gateway V2 headers and swallowing any transport/HTTP errors gracefully.
2. **Recipient Policy:**
   * Target `assignee_user` if specified; otherwise, fall back to `assignee_role`.
   * Also notify `reporter`.
   * Explicitly exclude the acting user (`current_user_id`) from the recipient list.
   * Generate exactly one notification payload per distinct recipient.
3. **Category & Priority Mappings:**
   * Assignment and comments are classified as `ACTION_ITEMS` with `MEDIUM` priority.
   * Status transitions are classified as `SYSTEM` with `MEDIUM` priority.
   * Channels are defaulted to `IN_APP`.
4. **Deterministic Idempotency Key:**
   * Construct `related_entity_id` as `{ticket_id}:{event_type}:{version_index}` to serve as a stable, unique idempotency token that renders duplicate deliveries harmless.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * High performance and strict transactional isolation.
  * Complete compliance with regulatory and security guidelines.
  * Reliable, deduplicated user alerts.
* **Negative Impact:**
  * Background tasks run asynchronously; therefore, in extreme network failure scenarios, a notification may fail to deliver while the ticket mutation remains successfully committed (deemed acceptable as notifications are best-effort overlays).

## 6. Implementation & Verification
* **Affected Modules:** `apps/tickets/notifications_client.py`, `apps/tickets/notification_events.py`, `apps/tickets/main.py`
* **Verification Plan:** Verified via unit and integration tests under `tests/test_tickets_notifications_client.py` and `tests/test_tickets_service.py` with mocked HTTP endpoints and stubbed client boundaries.
