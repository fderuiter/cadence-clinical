# ADR-189: In-Process ASGI Routing with Live Databases

* **Status:** Accepted
* **Date:** 2026-08-01
* **Authors:** @google-labs-jules[bot], @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Currently, our end-to-end integration tests rely on mock drivers and in-memory Neo4j simulators to validate the digital thread from Metadata Registry (MDR) to Electronic Data Capture (EDC). While fast, this abstraction lets query syntax errors, schema constraint violations, and transaction rollback anomalies escape into staging and production environments.

To bridge this gap without introducing the performance overhead of spawning separate background processes or dynamic Docker testcontainers, we are integrating live database engines directly into our in-process ASGI test execution loop. This approach ensures full-fidelity schema and constraint validation while maintaining local execution times under 30 seconds.

## 2. Decision Drivers & Constraints

* **High-Fidelity Validation (PRD-SYS-001):** Test execution must run against live PostgreSQL and Neo4j database engines to detect syntax errors, schema constraint violations, and transaction/rollback failures, aligning with our GxP clinical platform compliance and auditing standards.
* **In-Process Performance & Simplicity:** Service-to-service requests must remain in-process using memory-based routing (`intercept_cross_service_calls`) rather than spawning separate background web servers or orchestrating dynamic Docker containers, keeping local suite execution times under 30 seconds.
* **Isolation & Repeatability:** Tests must run in complete isolation. We need clean, automated database state resets before and after each test without violating relational foreign-key/trigger constraints or clinical GxP compliance rules on data immutability.
* **Fail-Fast Safety Boundary:** If live databases are unreachable or misconfigured when running with `USE_LIVE_DB=true`, the test execution must immediately halt with a clear error message instead of silently falling back to mocked or incomplete states.

## 3. Options Considered

### Option 1: Live Databases with In-Process ASGI Routing & Autouse Resets (Selected)
* **Overview:** Swap mock drivers with connections to live PostgreSQL and Neo4j databases during test startup when `USE_LIVE_DB=true` is set. Clean state before and after each test using autouse fixtures (truncating PostgreSQL tables under temporary replication mode bypasses triggers and foreign key locks cleanly, and detachment deletes all Neo4j nodes). Keep all S-to-S calls fully in-process.
* **Pros:**
  * ✅ Full-fidelity database schema validation with no mock drift.
  * ✅ Extremely fast local execution (under 30s) as S-to-S requests bypass the network stack.
  * ✅ Clear fail-fast boundaries with connection assertions at startup.
  * ✅ High reliability with autouse GxP-compliant transactional teardown.
* **Cons:**
  * ❌ Requires local running PostgreSQL and Neo4j instances for the live execution mode.

### Option 2: Spawning Background Web Server Processes & Dynamic Testcontainers
* **Overview:** Launch separate background API gateway and microservice processes, or spin up disposable containerized environments on every test run.
* **Pros:**
  * ✅ True production parity with network stack traversal.
* **Cons:**
  * ❌ Massive performance penalty, raising suite execution times well beyond the 30-second target.
  * ❌ High complexity in handling process lifecycles, port collisions, and async state synchronization.

## 4. Decision Outcome

* **Chosen Option:** Option 1
* **Justification:** Option 1 delivers the required high-fidelity schema validation and strict isolation without the unacceptable performance overhead or process-coordination complexity of Option 2. It directly aligns with our mission to have an automated, fast, yet highly rigorous testing loop.

## 5. Consequences & Trade-offs

* **Positive Impact:**
  * Syntax, schema, constraint, and multi-service transactional issues are detected instantly in local dev environments.
  * High-fidelity end-to-end integration tests run in under 30 seconds.
  * Automated and bulletproof cleanup routines guarantee perfect test isolation.
* **Negative Impact / Technical Debt:**
  * Local development environments must have access to functional PostgreSQL and Neo4j instances when executing in live mode.
* **Mitigation Strategy:**
  * The startup boundary automatically validates connections, halting the suite with friendly setup diagnostics if either database is unreachable.

## 6. Implementation & Verification

* **Affected Repositories / Services:**
  * `tests/conftest.py` (Database schema creation, connection validation, mock bypass logic, database teardown/cleanup autouse fixtures)
  * `tests/test_asgi_live_db.py` (Verification suite confirming fail-fast behavior, connection failure assertions, database resets)
* **Verification Plan:**
  * Run `uv run pytest tests/test_asgi_live_db.py --no-cov` to verify connectivity checks, rollback behaviors, and cleanup routines.
