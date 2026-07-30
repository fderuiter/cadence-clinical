# Tickets: Overdue/SLA Escalation Worker and Notification De-Duplication

## Status

Accepted

## Context

We need an automated SLA escalation system for support tickets within the Tickets microservice (`apps/tickets`). Tickets past their `due_date` must have their priority advanced programmatically over time up to `CRITICAL` in a step-wise manner.

To ensure safe, duplicate-proof, and audit-compliant SLA escalations across multiple background worker instances, we need:
1. Safe database locks to prevent concurrent worker instances from escalating the same ticket.
2. Persisted state to support idempotent escalation and notification tracking.
3. Post-commit notification ordering to guarantee notifications are never lost, even if a crash occurs between the escalation and notification dispatch.
4. Compliance with GxP 21 CFR Part 11 requirements (creating immutable `TICKET_ESCALATE` audit logs and preserving optimistic versioning).

## Decision

We have implemented an asynchronous, decoupled background worker loop for support ticket SLA escalation and notification de-duplication with the following key design decisions:

### 1. Persisted Escalation and Notification De-duplication State
We added three nullable/defaulted columns to the `Ticket` model class in `apps/tickets/models.py`:
- `last_escalated_at` (DateTime): Stores the exact timestamp of the most recent successful priority escalation.
- `last_escalation_notified_at` (DateTime): Stores the timestamp of when a notification was successfully dispatched for the most recent escalation.
- `escalation_count` (Integer, default `0`): Tracks the cumulative number of escalations, allowing auditable log-based metrics without requiring a metrics library.

**Notification Owed Invariant:** A notification is owed if and only if `last_escalated_at` is set, and is newer than `last_escalation_notified_at` (`last_escalation_notified_at IS NULL OR last_escalated_at > last_escalation_notified_at`).

### 2. Multi-Instance Concurrency and Pessimistic Locking
To prevent duplicate escalations from running across multiple worker replicas, we query candidates first, then acquire a database write-lock on each candidate individually using SQLAlchemy `.with_for_update()` (matching our pattern in `apps/notifications/main.py::deliver_channel`). All eligibility checks (active, overdue, not fully escalated, and cooldown-elapsed) are re-evaluated immediately after the lock is acquired before any mutation occurs.

### 3. Bounded Step-wise Priority Policy
When a ticket is escalated, its priority is advanced exactly one level toward `CRITICAL` along the chain `LOW` -> `MEDIUM` -> `HIGH` -> `CRITICAL`. Once a ticket reaches `CRITICAL` priority, it is bounded and can never be escalated further. Priority escalation does not affect ticket lifecycle status.

### 4. Post-Commit Notification Ordering
To prevent missing or duplicated notifications in the event of a system crash, we decouple the database updates into two distinct transaction commits:
1. **Commit 1 (Escalation):** The ticket's priority is increased, `last_escalated_at` is set to `now`, versioning fields (`version_index`, `reason_for_change`, `created_by`) are updated, and a `TICKET_ESCALATE` audit log is appended to the immutable audit trail. This transaction is committed first.
2. **Notification Dispatch:** Only after Commit 1 succeeds, we attempt to dispatch the notification using our gateway-signed `#577 Tickets notification client` (`apps/tickets/notifications_client.py`).
3. **Commit 2 (Timestamp persistence):** If notification dispatch succeeds, we record `last_escalation_notified_at` to the database and commit that as a separate step. If a crash or network partition occurs between Commit 1 and Commit 2, the next background worker cycle will detect that a notification is still owed (via the Notification Owed Invariant) and retry dispatch without re-escalating the ticket.

### 5. Env-Var Tunables
The worker respects two environment variable configurations:
- `TICKETS_ESCALATION_POLL_INTERVAL_SECONDS` (default: `60.0`): The polling cadence of the background loop.
- `TICKETS_ESCALATION_INTERVAL_SECONDS` (default: `86400.0`): The cooldown window required between consecutive escalations of the same ticket.

### 6. Log-Based Observability
To avoid introducing external metrics libraries, we utilize a dedicated module logger `tickets_escalation`. We record cycle starts, per-ticket escalation events (including reference, IDs, and priority levels), and errors with full stack trace (`exc_info=True`). This structured log output acts as the primary telemetry source for SLA metric extraction.

### 7. App Lifespan Integration
The worker loop is registered as startup and shutdown hooks inside the FastAPI `get_relational_db_lifespan` lifespan wrapper in `apps/tickets/main.py`. It is equipped with a `pytest` environment check to prevent auto-spawning during automated testing.

## Consequences

- **Safety & Resiliency:** Complete prevention of race conditions and safe recovery from network or notification transport failures.
- **Part 11 & GxP Compliance:** Every priority escalation produces an immutable append-only record in `TicketAuditLog` under the `TICKET_ESCALATE` action.
- **Backward Compatibility:** All new fields on `Ticket` are nullable/defaulted, leaving existing records and test schemas completely unaffected.
- **Separation of Concerns:** No due-date ageing or transition logic was added to the main requests thread; SLA management is fully isolated in the background worker.
