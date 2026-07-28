# ADR-104: PostgreSQL-Native Parallel Testing and Unified CI Caching

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Previously, our backend test suite executed sequentially. In addition, the lack of robust caching for Python dependencies, pre-commit environments, and browser binaries delayed feedback loops in continuous integration (CI) workflows. As a result, pipeline times exceeded acceptable developer-feedback limits. To optimize performance without compromising production dialect parity, we needed a parallelized testing approach that runs against isolated PostgreSQL databases while caching necessary dependencies.

## 2. Decision Drivers & Constraints
* **Developer Velocity:** Reduce build feedback loops to under three minutes.
* **Dialect Parity:** Maintain 100% parity with production PostgreSQL instead of falling back to SQLite or in-memory mocks.
* **Test Isolation:** Prevent parallel test workers from executing against overlapping database state.
* **GxP Compliance:** Ensure that single-run artifacts (like Traceability Matrix documentation) are generated exactly once without race conditions, preserving audit trail integrity (PRD-SYS-001) and ensuring dynamic test isolation matches universal isolation requirements (PRD-SYS-004).

## 3. Options Considered
### Option 1: Sequential Testing on Single Shared Database
* **Overview:** Keep tests sequential and shared on a single database.
* **Pros:**
  * ✅ No orchestration complexity.
* **Cons:**
  * ❌ Long feedback loops as test suite grows.

### Option 2: Parallel Testing with SQLite/In-Memory Mock Fallback
* **Overview:** Run parallel tests locally and in CI using SQLite.
* **Pros:**
  * ✅ Very fast setup.
* **Cons:**
  * ❌ Violates dialect parity with PostgreSQL, masking dialect-specific SQL errors and constraint behaviors.

### Option 3: PostgreSQL-Native Parallel Worker Routing with Caching (Selected)
* **Overview:** Integrate `pytest-xdist` to provision dynamically isolated database partitions per test worker, implement unified caching layers, and restrict GxP compliance documentation hooks to a single coordinator process.
* **Pros:**
  * ✅ Dramatically reduces feedback loop duration.
  * ✅ Fully maintains 100% PostgreSQL dialect parity.
  * ✅ Guarantees transaction isolation between parallel workers.
* **Cons:**
  * ❌ Adds minor complexity to pytest setup and teardown.

## 4. Decision Outcome
* **Chosen Option:** Option 3
* **Justification:** Choosing Option 3 allows the platform to achieve fast feedback times under three minutes while preserving 100% dialect and constraint parity with our production PostgreSQL database.

## 5. Consequences & Trade-offs
* **Positive Impact:** Builds execute over 3x faster, with fully isolated test transactions.
* **Negative Impact / Technical Debt:** Requires managing separate database schemas or database partitions per worker process.
* **Mitigation Strategy:** Setup clean teardown rules inside `tests/conftest.py` to drop test databases cleanly upon session completion.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `pyproject.toml`, `tests/conftest.py`, `.github/workflows/ci.yml`.
* **Verification Plan:** Verify by running `uv run pytest -n auto` and verifying that all tests run concurrently against individual isolated schemas/databases.
