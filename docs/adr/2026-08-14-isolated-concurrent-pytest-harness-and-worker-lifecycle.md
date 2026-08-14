# ADR-2176: Isolated Concurrent Pytest Harness and Worker Lifecycle

* **Status:** Accepted
* **Date:** 2026-08-14
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

During local quality-gate verification and CI testing for large test suites (>2,500 test items), `uv run pytest -n auto` exhibited tail stalls, hangs at 96–99% completion, and unclosed xdist controller/worker processes. Multiple active pytest runs against the same PostgreSQL service caused cross-worker database collisions, connection pool exhaustion, and orphaned database sets.

Investigation revealed four primary root causes:
1. **Module-Import Database Provisioning Side Effects:** `tests/conftest.py` created and migrated 11 PostgreSQL databases at Python import time across every process, including the xdist controller process (`_main`), leading to redundant schemas and race conditions.
2. **Deterministic & Collision-Prone Suffixes:** Suffixes were constructed purely from worker IDs (`_gw0`, `_gw1`) without run-level scoping, causing two concurrent pytest runs on the same developer machine to collide and corrupt each other's databases.
3. **Unbounded Database & Subprocess Operations:** Database creation, teardown, and background RTM generation (`scripts/generate_rtm.py`) ran as untracked subprocesses or unbounded coroutines, blocking worker teardown indefinitely on network drops or locks.
4. **Subscriber Loop Mock Hangs:** Mocked Redis pub/sub tests did not signal the underlying stop event in the retry loop, causing worker processes to loop indefinitely on teardown.

This change addresses requirement `PRD-SYS-001` (Unified System Architecture & Operational Isolation).

---

## 2. Decision Drivers & Constraints

* **Strict Process & Database Isolation (PRD-SYS-001):** Simultaneous local or CI test runs must use completely disjoint database names and never block or corrupt each other.
* **Controller Clean Boundaries:** The xdist controller coordinates test distribution across workers and executes 0 tests; it must not create or migrate any live database schemas.
* **Fail-Fast Connectivity Validation:** When `USE_LIVE_DB="true"` is explicitly specified, missing PostgreSQL or Neo4j connections must halt execution immediately with clear error messages.
* **Bounded Operational Lifecycles:** All database connections, schema migrations, and teardown routines must be strictly bounded with timeouts.
* **Tail Diagnostics:** Long-running test cases (>15s) or worker tail stalls must be diagnosed and surfaced proactively to developers.

---

## 3. Options Considered

1. **Option A: Test-Run-UID Scoped Provisioning with Explicit Lifecycle Hooks (Selected)**
   - Derive a unique 8-character alphanumeric run ID (`PYTEST_XDIST_TESTRUNUID` / `CADENCE_TEST_RUN_ID`) and propagate it via `pytest_configure_node(node)`.
   - Construct collision-free suffixes `_{run_uid}_{worker_id}` bounded within PostgreSQL's 63-byte identifier limit.
   - Restrict database schema creation strictly to worker processes and single-process runners inside `pytest_configure`.
   - Wrap all database operations with bounded timeouts (5s connection, 15–30s operation) via `asyncio.wait_for`.
   - Provide a background diagnostic tail monitor `_TestSessionMonitor` reporting slow test nodes (>15s).
   - Provide a standalone CLI tool `scripts/clean_test_dbs.py` to inspect and drop orphaned worker databases.

2. **Option B: Docker-in-Docker Ephemeral Containers per Run**
   - Spin up dedicated ephemeral PostgreSQL containers per test run.
   - *Rejected:* Adds significant CPU/memory container startup overhead and platform-specific complexity on macOS/Linux hosts.

3. **Option C: Pure In-Memory SQLite Mocking Globally**
   - Eliminate live PostgreSQL testing entirely during unit/integration suites.
   - *Rejected:* Violates GxP qualification and relational schema parity requirements for complex PostgreSQL-specific constraints, triggers, and Alembic migrations.

---

## 4. Decision Outcome

Chosen option: **Option A** because it ensures absolute multi-run isolation, zero controller overhead, bounded teardowns, and rapid sub-30-second execution of the entire 2,600+ test catalog while preserving GxP qualification standards.

---

## 5. Consequences & Trade-offs

* **Positive:**
  - Concurrent pytest sessions can execute in parallel on the same host with zero database collisions.
  - Test run completion time dropped from >374s to ~28s.
  - Zero zombie or lingering processes after test completion.
  - Standalone utility `scripts/clean_test_dbs.py` allows easy cleanup of stale worker databases.
* **Negative / Considerations:**
  - Requires maintaining `scripts/clean_test_dbs.py` and hook propagation in `tests/conftest.py`.
  - Developers using live database testing must ensure PostgreSQL names stay within the 63-byte limit.

---

## 6. Implementation & Verification

* **Target files modified:**
  - `tests/conftest.py`: Refactored lifecycle hooks, removed import-time DB setup, added test run UIDs, bounded timeouts, tail monitor, and clean session finish.
  - `scripts/clean_test_dbs.py`: Standalone CLI tool to list, filter by `--run-id`, or drop all stale worker test databases.
  - `tests/test_harness_isolation.py`: Regression test suite covering suffix uniqueness, PostgreSQL naming limits, controller exclusion from DB provisioning, worker provisioning gating, `run_sync` timeout enforcement, and simulated concurrent multi-run isolation.
  - `apps/econsent/tests/test_cache_redis.py`: Fixed mocked subscriber loop stop event handling.
  - `packages/database/tests/test_asgi_live_db.py`: Updated test path to decentralized `test_reset_db.py`.
* **Verification:**
  - `uv run pytest tests/test_harness_isolation.py --no-cov -v` (9 passed).
  - Three consecutive full test suite executions (`uv run pytest -n auto --no-cov -q`) passed 2,599 tests in ~28s with status 0.
  - Two simultaneous pytest invocations launched concurrently completed cleanly with status 0.
