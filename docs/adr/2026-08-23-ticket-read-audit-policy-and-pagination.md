# ADR-099: Ticket Read Audit Policy and Paginated Audit Log Retrieval

* **Status:** Accepted
* **Date:** 2026-08-23
* **Authors:** @jules
* **Deciders:** @jules

---

## 1. Context & Problem Statement
Under FDA 21 CFR Part 11 and clinical trial GxP requirements, all security-sensitive and record-access events within helpdesk ticketing operations must be auditable. In addition to write mutations, read events (such as viewing a ticket, listing tickets, viewing ticket comments, or listing the ticket audit trail logs) must be tracked within the append-only `TicketAuditLog` ledger to preserve complete chain-of-custody.

This decision implements security and compliance requirements under PRD-SYS-001.

However, logging every read event results in a growing audit trail. Returning the full list of audit logs in a single unpaginated API request could lead to performance bottlenecks, high memory consumption, and potential denial-of-service vectors as the system ages.

## 2. Decision Drivers & Constraints
* **Driver 1:** 21 CFR Part 11 compliance requiring self-auditing entries for ticket read, comment view, and audit log list operations.
* **Driver 2:** Hardening of security and authorization across read paths to ensure site-scoped users only query details relevant to their assigned sites.
* **Driver 3:** Robust system performance by putting hard limits on the response sizes of audit trails.

## 3. Options Considered
### Option 1: Unbounded Full-Table Retrieval with Self-Auditing
* **Pros:** Simpler implementation.
* **Cons:** Memory exhaustion, high network load, and poor query performance as ledger records scale.

### Option 2: Paginated and Bounded Retrieval with Self-Auditing
* **Pros:** Highly scalable, supports fine-grained time-range filtering (`start_time` and `end_time`), enforces strict request bounds (e.g. `limit` parameter gated to `[1, 250]`), and complies with GxP auditability.
* **Cons:** Client consumers must adapt to structured `PaginatedTicketAuditLogResponse` paging envelopes.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Implementing paginated, bounded retrieval ensures the platform can support heavy helpdesk operations without performance degradation, while strictly adhering to Part 11 compliance by retaining the self-audited logging behavior for list operations.

## 5. Consequences & Trade-offs
* **Security & Compliance:** Site-scoped roles (CRC, Investigator, CRA, Monitor, External Monitor) can only fetch audit logs corresponding to their site-assigned parent tickets.
* **Ledger Discovery:** The self-audit list log entry is written *before* executing the paginated count and retrieval queries. This guarantees that the caller's own query event is immediately discoverable within the returned dataset.
* **API Uniformity:** Mirroring the pagination pattern used in the eTMF audit log allows for a consistent client integration design across all Cadence microservices.

## 6. Implementation & Verification
* **Affected Files:** `apps/tickets/main.py`, `tests/test_tickets_service.py`
* **Verification Plan:** Verified via integration tests checking page boundaries, invalid parameter handling (422), inclusive time-range filters, and site-scoped access control.
