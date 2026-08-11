# ADR-2169: Relational Outbox System across Execution and eTMF Services

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To support robust, fault-tolerant, and transactionally secure inter-service integration across Cadence Clinical, we require a guaranteed message delivery mechanism. Specifically, when an event occurs in one service (for example, signing a trial lock in the Execution service), this event must be atomically persisted alongside the state mutation in the same database transaction. A separate, decoupled background process should then handle reliable delivery (such as dispatching a lock notification to the eTMF service). This ensures atomicity and consistency under the GxP 21 CFR Part 11 and HIPAA auditing requirements (traced under Trace-13).

## 2. Decision Drivers & Constraints

* **Transactional Atomicity:** Local database state changes and outbox event persistence must succeed or fail together within a single ACID transaction block.
* **Failure Resilience & Retry Reliability:** Background pollers must support automated retry attempts with exponential backoff and a hard configurable ceiling on failed attempts.
* **Traceability and HIPAA Compliance:** Message payload structures must strictly avoid unencrypted PII logs and support explicit metadata tracking, such as `correlation_id`, `created_by`, and `reason_for_change`.
* **Coupling Isolation:** Direct HTTP calls from within service request handlers are blocked to avoid cascading transaction failures and high latency.

## 3. Options Considered

### Option 1: Direct Synchronous HTTP Dispatches within API Route Handlers
* **Pros:** Simpler codebase, lower initial effort.
* **Cons:** Introduces distributed transaction vulnerability, blocks HTTP worker threads during downstream delays, violates strict fault isolation.

### Option 2: Database-Level Relational Outbox Pattern (Selected)
* **Pros:** Assures transactional safety (ACID) locally. Guarantees eventually consistent state updates downstream via asynchronous polling and idempotent retries. Satisfies 21 CFR Part 11 audit guidelines fully.
* **Cons:** Slightly increased implementation complexity and a brief eventual-consistency delay.

## 4. Decision Outcome

Chosen option: **Option 2 (Relational Outbox Pattern)** because it is the only pattern that ensures atomic local writes alongside event tracking, guaranteeing GxP trace integrity and robust failure recovery under any database or network partition scenarios.

## 5. Consequences & Trade-offs

* **Positive:** Decoupled service boundaries, zero risk of partial failures during cross-service updates, clear auditing trail of outbox attempts and processing status.
* **Negative:** Slight eventual consistency delay, requires lightweight polling worker tasks per service.

## 6. Implementation & Verification

* **Core Interface:** Standardized `IntegrationOutboxMixin` declared in `packages/database/__init__.py`.
* **Service Models:** Concrete `IntegrationOutbox` model added in `apps/execution/database/models.py` and `apps/etmf/infrastructure/models.py`.
* **Polling Loop:** Dedicated background pollers implemented in `apps/execution/workers/outbox_worker.py` and `apps/etmf/workers/outbox_worker.py`.
* **Testing & Verification:** Comprehensive test cases verify atomic creation, correct state transitions (`PENDING` -> `SUCCESS`/`FAILED`), and exponential backoff boundaries in `apps/execution/tests/test_relational_outbox.py` and `apps/etmf/tests/test_etmf_outbox.py`.
