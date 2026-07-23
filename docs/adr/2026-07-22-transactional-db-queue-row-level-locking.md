# ADR-2026-07-22-transactional-db-queue-row-level-locking: Transactional DB Queue with Row-Level Locking

* **Status:** Accepted
* **Date:** 2026-07-22
* **Authors:** @jules
* **Deciders:** @engineering_leads

---

## 1. Context & Problem Statement
The translation engine originally relied on simple in-memory background tasks (`fastapi.BackgroundTasks` running unpersisted workflows). If the application container crashed or restarted mid-execution, active translation jobs were permanently lost and left stuck in processing/pending states with no automatic retries or recovery. To ensure zero-loss processing of clinical study translation jobs, we need a resilient, PostgreSQL-backed transactional background queue that guarantees task processing safety, automatic error recovery, dead worker task sweeping, and concurrent task safety.

## 2. Decision Drivers & Constraints
* **Driver 1:** Resiliency & Zero Job Loss - Translation tasks must be recorded and persisted before returning a response to the client. If a worker dies, tasks must be recovered.
* **Driver 2:** Zero-dependency Queue Broker - The system must operate entirely within the existing PostgreSQL database to avoid introducing external dependencies like Redis or Celery.
* **Driver 3:** Concurrency Control - Multiple server instances must query the queue concurrently without duplication of effort or double claiming.
* **Driver 4:** Compliance & Auditing - State transitions must adhere to strict clinical auditing standards (21 CFR Part 11).

## 3. Options Considered
### Option 1: In-memory/Celery with Redis
* **Overview:** Use Celery with Redis for robust background queuing.
* **Pros:**
  * ✅ High throughput and native support for retries.
* **Cons:**
  * ❌ Introduces a new third-party infrastructure dependency (Redis), violating the existing backend constraints.

### Option 2: PostgreSQL transactional queue with row-level locking (Selected)
* **Overview:** Persist jobs as `PENDING` records in a relational database, using `SELECT ... FOR UPDATE SKIP LOCKED` for concurrent locking and a heartbeat loop with a stale sweeper.
* **Pros:**
  * ✅ Zero external dependencies - runs inside the existing PostgreSQL backend.
  * ✅ Row-level concurrency - `SKIP LOCKED` prevents multiple workers from claiming the same job.
  * ✅ Deep resiliency - crashed or dead workers stop their heartbeat, and the sweeper automatically reschedules the job.
* **Cons:**
  * ❌ Polling introduces some database query overhead, which is mitigated using exponential backoff on empty queues.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 fits perfectly within our PostgreSQL datastore constraints without introducing Redis, while still achieving robust transactional task safety, heartbeat-based dead worker recovery, and multi-node concurrent execution safety.

## 5. Consequences & Trade-offs
* **Positive Impact:** Zero job loss, automatic crash recovery within the timeout window, safe concurrent scaling, and automatic clinical audit logs of all queue state changes.
* **Negative Impact / Technical Debt:** Added database polling query load.
* **Mitigation Strategy:** Applied an exponential backoff strategy (up to 30.0s) and a wakeup event listener to sleep when the queue is empty, eliminating CPU spikes.

## 6. Implementation & Verification
* **Affected Services:** `apps/execution` (main.py, models.py, translator.py, queue.py)
* **Verification Plan:**
  * Created automated unit and integration tests under `tests/test_transactional_queue.py` verifying pending insertion, retries, dead worker heartbeats/sweeping, concurrent row-level locking syntax, and database disconnection recovery.
  * Successfully verified all 66 tests passing locally under Python 3.12.
