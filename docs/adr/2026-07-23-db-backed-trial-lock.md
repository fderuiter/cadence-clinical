# ADR-27: Database-Backed Transactional Trial Lock

* **Status:** Accepted
* **Date:** 2026-07-23
* **Authors:** @jules
* **Deciders:** @reviewer1, @reviewer2

---

## 1. Context & Problem Statement
The current automated trial lock mechanism stores state purely in application memory. When a security compromise is detected, the trial freeze/lock state is registered in memory, which suffers from two main shortcomings:
1. Lock states are cleared during container restarts/recycles.
2. Lock statuses fail to synchronize across horizontally scaled service instances, allowing potential unauthorized writes to other pods in a multi-pod cluster.

To protect clinical trial data integrity under GxP compliance standards, once an automatic or manual trial lock is activated, standard clinical data updates must be blocked persistently and immediately across all application instances.

## 2. Decision Drivers & Constraints
* **Compliance & Data Integrity (GxP):** Prevent unauthorized writes after a breach is detected, irrespective of container recycles or cluster scaling.
* **No External Caching Infrastructure:** The solution must not introduce external systems like Redis. It must use the existing SQL persistence layer and database connection.
* **No Native DB Triggers for Locks:** Lock enforcement must occur within the application transaction lifecycle rather than raw database-level triggers to keep schema migrations dialect-independent.
* **Read Performance Safeguard:** Lock verification queries must not execute during read-only sessions to prevent query performance degradation.
* **Operational Continuity:** Bypass lock enforcement for dedicated documentation (eTMF) and interoperability tables, ensuring their continuous operation.

## 3. Options Considered
### Option 1: In-Memory Lock with Service-to-Service Sync RPCs
* **Overview:** Keep lock status in memory and use internal API/gRPC calls to broadcast lock updates to other instances.
* **Pros:**
  * ✅ Fast read performance.
* **Cons:**
  * ❌ Complex to implement, requiring discovery of other instances.
  * ❌ Fails on container restarts unless persistent storage is also added.

### Option 2: Database-Backed Transactional Lock Enforcement (Selected)
* **Overview:** Store lock states inside a dedicated relational database table (`trial_lock_statuses`). Perform a synchronous pre-flush query on this table during active write transactions. Update a fast local cache to keep in-memory operations lightweight while ensuring multi-pod synchronization.
* **Pros:**
  * ✅ Persistent across container restarts.
  * ✅ Synchronized instantly across all parallel service instances sharing the database.
  * ✅ Easily bypasses non-restricted tables and read-only sessions (respecting performance safeguards).
* **Cons:**
  * ❌ Adds minimal database query overhead during the pre-flush phase of write transactions (under 5ms).

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Reusing the existing SQL persistence layer via the configured database connection guarantees data integrity without introducing additional infrastructure like Redis. Synchronous checks in the transaction pre-flush hook (`before_flush`) ensure 100% block of writes before any database commit occurs.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Persisted and multi-pod synchronized lock states.
  * Complete bypass of checks for read-only sessions and non-clinical data writes (eTMF/Interop).
* **Negative Impact / Technical Debt:**
  * Added query latency (under 5ms) on write transactions, which is negligible for transactional safety in clinical trials.
* **Mitigation Strategy:**
  * In-memory fast cache is updated whenever a query or lock event runs, minimizing database checks during non-write operations.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `apps/execution`
* **Verification Plan:** Verified through `pytest tests/test_trial_lock.py` containing tests for container-restart simulation, multi-pod simulation, and eTMF/Interop write bypasses.
