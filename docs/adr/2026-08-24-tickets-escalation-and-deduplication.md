# ADR-101: Support Tickets SLA/Overdue Escalation and Notification De-Duplication

* **Status:** Accepted
* **Date:** 2026-08-24
* **Authors:** Jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
The Cadence Clinical platform supports support ticket management in the `tickets` service. Support tickets that remain unresolved past their designated due date need to be prioritized and escalated automatically over time. In a clinical trial context (regulated under GxP and 21 CFR Part 11 guidelines, as mapped under PRD-SYS-001), overdue actions must be handled with extreme reliability.

We must implement a programmatic background escalation worker that:
1. Automatically advances a ticket's priority step-wise towards `CRITICAL` when overdue.
2. Integrates with the platform notifications service to alert the appropriate assigned roles and users.
3. Guarantees that notifications are de-duplicated and never lost across system restarts or network partitions.
4. Prevents duplicate concurrent escalations across multi-instance deployments.

## 2. Decision Drivers & Constraints
* **Step-wise Bounded Priority Policy:** Support ticket priorities must be advanced sequentially (`LOW` -> `MEDIUM` -> `HIGH` -> `CRITICAL`) and bounded strictly at `CRITICAL`.
* **Idempotency & Notification De-duplication:** Dispatches must not be duplicated, and must be safely persisted across service restarts.
* **Concurrency Safety:** Multiple concurrent instances of the background worker must not race to escalate or notify the same ticket.
* **GxP 21 CFR Part 11 Auditing:** All priority changes must write immutable audit entries.

## 3. Options Considered
### Option 1: Inline Synchronous Request inside DB Transactions
* **Pros:** Simple, immediate notification.
* **Cons:** High latency on requests, lacks retry resilience, and risks rolling back core database state due to transient network failures.

### Option 2: Post-Commit Background Poller with Two-Step Transactions (Chosen)
* **Pros:**
  * Background worker is decoupled from the user request threads.
  * Multi-instance safe through the use of database-level pessimistic locking (`.with_for_update()`).
  * Persists state for tracking and deduplication (`last_escalated_at`, `last_escalation_notified_at`, and `escalation_count`).
  * Employs a strict two-commit design: the priority escalation and its immutable `TICKET_ESCALATE` audit log are committed first, followed by the notification dispatch, followed by committing the notification timestamp.

## 4. Decision Outcome
We adopted **Option 2**. We implemented the overdue escalation system as follows:

1. **State Persistence:** Added `last_escalated_at`, `last_escalation_notified_at`, and `escalation_count` to the `Ticket` model in `apps/tickets/models.py`.
2. **Pessimistic Concurrency Locking:** The background poller locks each ticket under `.with_for_update()` before re-verifying constraints, mirroring the pattern in `apps/notifications/main.py`.
3. **Post-Commit Delivery Order:**
   * **Commit 1:** Priority advanced, `last_escalated_at` set to `now`, version index incremented, and `TICKET_ESCALATE` audit log written.
   * **Dispatch:** Notification dispatched via the tickets notifications client (`apps/tickets/notifications_client.py`).
   * **Commit 2:** Upon success, `last_escalation_notified_at` is set to `now` and committed separately.
4. **Notification Owed Invariant:** A notification is owed only when `last_escalated_at` is set and is newer than `last_escalation_notified_at`.

## 5. Consequences & Trade-offs
### Alternatives Considered
We evaluated event-driven brokers but preferred a lightweight direct poller to keep the stack simple and maintain immediate consistency.

### Trade-offs
* **Pros:**
  * Bulletproof delivery guarantee: any crash between Commit 1 and Commit 2 simply results in a safe notification retry in the next cycle, with no double-escalations.
  * Zero external metrics library overhead; structured logging provides complete telemetry.
* **Cons:**
  * Short polling cycles consume database connections, which is mitigated by tunable environmental interval options.

## 6. Implementation & Verification
* **Affected Files:** `apps/tickets/models.py`, `apps/tickets/escalation.py`, `apps/tickets/main.py`
* **Verification Plan:**
  * Automated unit and integration tests written in `tests/test_tickets_escalation.py`.
  * Verified eligibility, stepwise priority cap, cooldown gating, idempotency, and gap-retry resilience.
