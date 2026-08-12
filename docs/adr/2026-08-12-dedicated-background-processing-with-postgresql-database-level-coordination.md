# ADR-2171: Dedicated Background Processing with PostgreSQL Database-level Coordination

- **Status:** Accepted
- **Date:** 2026-08-12
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To run long-running, background, or scheduled tasks—such as daily query escalations, cryptographic ledger block sealing, and integration outbox event dispatching—the platform needs dedicated background loops. Running these tasks on the main clinical execution threads or utilizing the same connection pools risks starving user-facing clinical APIs of database connections. Additionally, running tasks concurrently across multiple application instances can cause race conditions or duplicate task executions, violating clinical data consistency standards under GxP compliance guidelines.

## 2. Decision Drivers & Constraints

- **Connection Pool Isolation:** Background tasks must run within isolated environments and use a separate PostgreSQL connection pool to prevent connection starvation of active clinical API endpoints.
- **Mutual Exclusion & Coordination:** Coordination of background workers across multiple service replicas must use safe, centralized locking to guarantee that exactly one worker instance executes a task loop at any given time (traced under **PRD-SYS-102**).
- **Resource Efficiency:** Avoid polling or thread overhead in non-production environments like SQLite where background workers are disabled or run in single-connection mode.
- **Performance Preservation:** Cryptographic block verification and processing should occur asynchronously or via background tasks rather than blocking active HTTP request threads (traced under **PRD-SYS-103**).

## 3. Options Considered

### Option 1: Main Connection Pool with Thread Locks (FastAPI / BackgroundTasks)

- **Pros:** Simpler configuration, no need for secondary database pools or distributed locks.
- **Cons:** Main clinical API connection pool can be fully starved under heavy background task loads. Does not coordinate tasks across multiple service instances/replicas.

### Option 2: Dedicated Background Database Pool & PostgreSQL Advisory Locks (Selected)

- **Pros:**
  - ✅ **Database Isolation:** An isolated `bg_db_manager` with a dedicated pool (size: 5) guarantees user-facing APIs always have adequate connection capacity.
  - ✅ **Database-Level Mutual Exclusion:** PostgreSQL advisory locks (`pg_try_advisory_xact_lock`) with dedicated lock IDs (`42001`, `42002`, `42003`) coordinate cross-instance execution.
  - ✅ **Asynchronous Verification:** Offloads CPU-intensive operations (such as validation of ledger integrity) safely to FastAPI background tasks.
- **Cons:** Slightly increased code complexity in connection management and startup orchestration.

## 4. Decision Outcome

Chosen Option: **Option 2 (Dedicated Background Database Pool & PostgreSQL Advisory Locks)** because it satisfies connection limits and GxP high-availability constraints while cleanly decoupling heavy processing workloads from user-facing routes.

## 5. Consequences & Trade-offs

- **Positive Impact:** User-facing execution API performance is highly consistent. Concurrency is robustly coordinated in clustered environments.
- **Negative Impact:** SQLite tests require dialect-aware fallbacks since SQLite does not support PostgreSQL transaction-level advisory locks.
- **Mitigation Strategy:** Dialect-aware helpers fall back gracefully to immediate execution or skip lock checking under SQLite, ensuring perfect unit/integration test compatibility.

## 6. Implementation & Verification

- **Managers & Loops:** Added `bg_db_manager` in `apps/execution/database/core.py`.
- **Advisory Locks:** Implemented `pg_try_advisory_xact_lock` for background sealer (`42001`), unresolved queries escalation (`42002`), and outbox dispatcher (`42003`).
- **Async Execution:** `/api/v1/execution/audit/integrity` refactored to schedule ledger verification via FastAPI's `BackgroundTasks`.
- **Testing:** Comprehensive concurrent locking and validation tests implemented in `apps/execution/tests/test_bg_processing_coordination.py`.
