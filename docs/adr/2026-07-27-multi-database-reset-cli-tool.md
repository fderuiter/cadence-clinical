# ADR-096: Programmatic Multi-Database Reset CLI Tool with Safety Guardrails

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter, @jules

---

## 1. Context & Problem Statement
During local eClinical software development, developers frequently need to reset database states to a known clean baseline to run automated validation tests and perform manual exploratory testing. Previously, state tear-down required manual commands to stop, remove, and recreate Docker containers and clear persistent volumes. This process introduced high developer friction, suffered from state drift across 12+ database instances, and took a significant amount of time (often over 1-2 minutes). 

We need a unified, zero-downtime database reset entrypoint that purges schemas and database objects concurrently across all microservices, migrations, and databases (including PostgreSQL, Neo4j, and 10 local SQLite databases) in under 15 seconds, while strictly enforcing safety guardrails to prevent accidental destruction of production or staging environments.

## 2. Decision Drivers & Constraints
* **Driver 1 (Speed & Efficiency):** Under 15-second total reset execution time without restarting underlying Docker containers.
* **Driver 2 (Safety & Production Guardrails):** Absolute prevention of connection to non-local or cloud hosts (e.g., RDS, AWS, Azure, non-localhost IP addresses) and blocking strings with keywords suggesting production/staging states.
* **Driver 3 (Database Topology Complexity):** Clean and seed multiple database paradigms (Relational PostgreSQL, Graph-based Neo4j, and 10 distinct file/in-memory SQLite microservices) concurrently.
* **Driver 4 (Workspace Integration):** A simple `pnpm run db:reset` command integrated directly into developer workflows.

## 3. Options Considered
### Option 1: Docker Compose Tear-Down and Volume Removal
Using shell scripts or standard compose files to tear down existing containers, delete volumes, and restart them from scratch.
* **Pros:**
  * ✅ Simple to write and guarantees standard fresh states.
* **Cons:**
  * ❌ Container boot-up and volume allocation takes too long, failing our 15-second baseline constraint.
  * ❌ Requires local Docker daemon access and cannot run cleanly within mock test suites or lightweight local developer workspaces.

### Option 2: Programmatic Database Client Connections (Selected)
Writing a dedicated Python CLI script (`scripts/reset_db.py`) that connects directly to active databases, drops/re-applies relational schema tables cascadingly, detaches and purges Neo4j graph nodes, and clears/re-migrates all 10 SQLite database files concurrently.
* **Pros:**
  * ✅ Fast execution under 15 seconds (achieving zero downtime by keeping Docker containers alive).
  * ✅ Provides fine-grained programmatic control to inject strict safety checks, validating URL schemas, and blocking execution if production or remote patterns are detected.
  * ✅ Allows concurrent database resets using asyncio.
  * ✅ Facilitates programmatic seeding of clinical trial studies directly after database reset.
* **Cons:**
  * ❌ Requires maintaining database connection, migration, and teardown logic within the script.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 allows us to meet our strict 15-second reset time limit while ensuring rigorous safety guardrails. Developers can execute a fast reset, reapply schemas, and seed canonical clinical trials concurrently with a single command.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Dramatic reduction in local development loop friction, allowing database resets in under 15 seconds.
  * Unified single-command experience: `pnpm run db:reset`.
  * Safe developer environment: accidental execution against live, staging, or production servers is strictly blocked.
* **Negative Impact / Technical Debt:**
  * Maintenance of standard seed metadata and trigger definitions within the CLI script.
* **Mitigation Strategy:**
  * Keep the reset script simple, thoroughly documented, and tested with robust unit and integration tests.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * Root `package.json` to configure the unified entrypoint script.
  * `scripts/reset_db.py` to coordinate and execute multi-database connection teardown, migrations, and seeding.
* **Verification Plan:**
  * Verified via automated test cases in `tests/test_reset_db.py` ensuring guardrails block external hostnames, RDS clusters, and staging/prod connection strings.
  * Verified successful offline/local database resets without crashing when databases are unreachable.
