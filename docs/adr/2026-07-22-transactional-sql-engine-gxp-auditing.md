# ADR 2026-07-22: Transactional SQL Engine with Standard GxP Auditing

## Status
Accepted

## Context
The clinical execution system lacked database models or validation rules for cohorts, subjects, and randomization, preventing clinical operations from deactivating active allocation paths when a cohort closes. We needed a way to manage cohort state (e.g., closing a completed cohort) in real-time, under 50ms, while maintaining strict GxP audit tracking (capturing user identity, timestamps, and justifications for changes) directly within the relational database boundaries.

## Decision
We introduced `Cohort`, `Subject`, and `AllocationPath` data models inheriting from `AuditedModel` to persist cohort configurations in indexed relational PostgreSQL tables.
We created dedicated API endpoints (`/cohorts/{cohort_id}/status` and `/subjects/enroll`) that enforce gateway authentication headers and change justifications (`X-Change-Reason`), leveraging standard database-level hooks for automated GxP shadow ledger logging without external caching.

## Alternatives Considered
- **External caching layers (e.g., Redis):** Rejected because we require strict atomicity, and cohort state checks must happen within the same transactional boundary as subject enrollment to avoid race conditions.
- **Application-level audit logging:** Rejected in favor of automated SQLAlchemy database hooks (via `AuditedModel`), which guarantee that all mutations generate an immutable change log in the shadow ledger.

## Trade-offs
- **Positive:** Ensures high-speed (<50ms) state evaluations directly against the database. Guarantees GxP compliance by enforcing justification headers and automatically rolling back the audit log alongside any failed transaction.
- **Negative:** Slightly increases the load on the relational database due to synchronously capturing pre/post states, though this overhead is negligible for typical clinical transactional loads.
