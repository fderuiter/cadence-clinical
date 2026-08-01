# ADR-190: Explicit Background Task Cancellation in Ledger Sealer

* **Status:** Accepted
* **Date:** 2026-08-01
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

During backend test execution, fast parallelized test sweeps (such as under `pytest -n auto`) can trigger event loop teardown conflicts. When testing, the FastAPI app lifespan handles startup/shutdown of background loops, including the ledger sealer and query escalation loop. However, during teardown, the event loop would close while background tasks were still active, causing unhandled `RuntimeError: Event loop is closed` errors. To support continuous integration (CI) pipelines and ensure high-reliability GxP execution under `PRD-SYS-001` and `PRD-SYS-003`, we need to guarantee that active background tasks are explicitly and cleanly cancelled before the loop closes.

## 2. Decision Drivers & Constraints

* **CI/CD Reliability:** Prevent flaky test failures during parallelized teardown.
* **GxP Compliance:** Keep the cryptographic audit trail ledger sealer and query escalation loop completely robust and synchronized under `PRD-SYS-003`.
* **Resource Leaks:** Ensure no background tasks are left orphaned or running across test boundaries.

## 3. Options Considered

### Option 1: Inline exception suppression in tasks
* **Overview:** Wrap background loop bodies in try-except blocks to catch closed loop errors.
* **Pros:**
  * ✅ Simple to implement.
* **Cons:**
  * ❌ Does not solve the root cause of task teardown timing, leaving resources or connections open.

### Option 2: Explicit Task Cancellation and Teardown Handling (Selected)
* **Overview:** Track task handles globally and explicitly invoke `cancel()` and await the completion of the background tasks within `stop_background_sealer` and `stop_background_query_escalation` during the FastAPI application lifecycle shutdown.
* **Pros:**
  * ✅ Cleanly cancels the task and waits for cancellation completion, preventing closed event loop errors.
  * ✅ Highly deterministic and reliable for fast pytest parallel execution.
* **Cons:**
  * ❌ Requires stateful global task references.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Option 2 solves the event loop closed errors deterministically by cleanly cancelling and awaiting tasks before teardown finishes. This maintains absolute stability and complies with `PRD-SYS-001` and `PRD-SYS-003`.

## 5. Consequences & Trade-offs

* **Positive Impact:** Tests run 100% reliably in CI/CD without intermittent event loop teardown crashes.
* **Negative Impact / Technical Debt:** Requires tracking tasks using global variables.
* **Mitigation Strategy:** Keep global task references strictly isolated inside their respective lifecycle management modules.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution/`
* **Verification Plan:** Verified through successful execution of all unit and E2E tests under `pytest -n auto` without event loop teardown errors.
