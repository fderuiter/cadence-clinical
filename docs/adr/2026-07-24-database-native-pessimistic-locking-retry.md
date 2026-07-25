# ADR-056: Database-Native Pessimistic Locking & Retry

* **Status:** Accepted
* **Date:** 2026-07-24
* **Authors:** @jules
* **Deciders:** @engineering-lead, @gxp-auditor

---

## 1. Context & Problem Statement
In clinical study management, multiple clinical designers or external automated integration scripts may attempt to save study metadata configurations, amend protocols, or publish library objects simultaneously. 
Without strict write serialization, concurrent update transactions on the same root clinical study or library objects can bypass standard transaction boundaries. This concurrency hazard causes parallel version history branches, duplicate action entries, corrupted/non-linear audit trails, and version indices colliding or duplicating. To maintain absolute regulatory compliance (e.g., 21 CFR Part 11 and EU Annex 11), study configuration histories must remain a perfectly straight, linear timeline of events, and library object templates must increment versions deterministically.

## 2. Decision Drivers & Constraints
* **Absolute Audit Linearity:** The history of study saves must represent a linear, serial sequence of events without branches or orphaned nodes.
* **No Client Payload Impact:** The database concurrency safety must be completely internal and operate strictly behind existing service interfaces, with no REST application paths, payload signatures, or JSON schemas changing.
* **Database Engine lock isolation:** To preserve high concurrency on different studies and library records across the system, locking must exclusively target specific root study and library records undergoing writes, avoiding global locks.
* **No App-Layer Locks:** Process-level in-memory lock constructs (such as global thread locks or local asyncio locks in the python runtime) are prohibited to preserve seamless horizontal scalability across multiple containers.
* **Transparent Recovery:** Transaction failures arising from transient lock timeouts or deadlock detection must be handled gracefully in the persistence layer with automatic bounded backoff retries.

## 3. Options Considered

### Option 1: Optimistic Concurrency Control (OCC)
* **Overview:** Rely on a version number or updated timestamp check on write. If a collision is detected, reject the transaction and return a 409 conflict.
* **Pros:**
  * ✅ High performance under low conflict.
  * ✅ Simplistic lockless design.
* **Cons:**
  * ❌ Demands client application error-handling logic to catch and retry upon 409 conflict, violating the "No client contract changes" constraint.
  * ❌ Risk of high abort rates under heavy overlapping automated operations.

### Option 2: Application-Layer Distributed Locks (e.g., Redis/Redlock)
* **Overview:** Use a distributed lock manager such as Redis to synchronize write access per study ID in the application memory space before hitting the database.
* **Pros:**
  * ✅ Moves collision resolution outside of DB transaction failures.
* **Cons:**
  * ❌ Introduces a new system dependency (Redis), violating minimalistic infrastructure guidelines.
  * ❌ Adds complexity in handling network partitioning, lock expiration, and lease renewals.

### Option 3: Database-Native Pessimistic Locking with Transient Retry (Selected)
* **Overview:** Utilize Neo4j transaction-level write locks by executing a write action (`SET node._lock = true`) on the root study or library object node immediately upon beginning the transaction. Combine this lock with a Python-level decorator (`@with_transaction_retry()`) that intercepts transient locking/deadlock conflicts and automatically retries with exponential backoff.
* **Pros:**
  * ✅ Purely native database locking, leveraging Neo4j's transactional concurrency manager.
  * ✅ Limits lock range strictly to the target root nodes, ensuring scalable database performance.
  * ✅ Requires no external dependencies or horizontal scaling limitations.
  * ✅ Offers 100% transparent serialization and automatic transaction retry, shielding the REST API and web client from transient failures.
* **Cons:**
  * ❌ Lock acquisition incurs milliseconds of latency for overlapping operations.

---

## 4. Decision Outcome
* **Chosen Option:** Option 3
* **Justification:** Option 3 perfectly satisfies all GxP compliance constraints, ensures perfect linear audit log serialization, and implements clean error recovery entirely within the database layer with zero API contract changes.

## 5. Consequences & Trade-offs
* **Positive Impact:** Zero duplicated historical actions or orphaned properties nodes during simulated multi-user saves. Perfectly straight, sequential version chains are preserved.
* **Negative Impact:** Overlapping writes on the exact same study or library entity will block for milliseconds while waiting for the lock owner to commit.
* **Mitigation Strategy:** Keep write transactions extremely short and targeted, avoiding long-running external API requests or complex processing within the locked transaction boundary.

## 6. Implementation & Verification
* **Affected Services:** `apps/designer` (Neo4j driver and transaction layer).
* **Verification Plan:** Verified via integration tests under `tests/test_delta.py` and `tests/test_study_versions.py` utilizing the shared `concurrency_runner` fixture to run overlapping asynchronous operations.
