# ADR-[NUMBER]: Programmatic Multi-Database Reset CLI with Safety Guardrails

* **Status:** Accepted
* **Date:** 2026-07-27
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Local clinical development workflows suffered from high setup friction and state drift. Developers had to manually tear down Docker containers, clear volumes, and restart services to reset their database environments, which disrupted flow and caused sync issues. We need a programmatic multi-database CLI tool to clean, migrate, and seed our local multi-database topology in under 15 seconds, all without requiring Docker container restarts.

## 2. Decision Drivers & Constraints
* **No Container Restarts (Zero Downtime):** Connect directly to active database instances to purge existing schemas, tables, and graph nodes.
* **Production Guardrails:** Prevent accidental data loss in staging or production environments through strict safety checks.
* **Unified entrypoint:** Root-level workspace runner `pnpm run db:reset` mapping Python CLI via the `uv` toolchain.

## 3. Options Considered
### Option 1: Docker Volume Purge and Recreate
* **Overview:** Wipe physical Docker volumes and restart container services.
* **Pros:**
  * ✅ Simplest mechanism, completely clean filesystem.
* **Cons:**
  * ❌ Too slow (takes over 1 minute).
  * ❌ Requires restarting Docker containers, breaking connection sessions.

### Option 2: Direct Database Purge and Programmatic Reset (Selected)
* **Overview:** Implement a programmatic multi-database CLI tool (`scripts/reset_db.py`) that uses native SQL and Cypher commands to clean and reinitialize PostgreSQL, Neo4j, and SQLite microservices directly.
* **Pros:**
  * ✅ Extremely fast (under 15 seconds).
  * ✅ No container restarts needed.
  * ✅ Standardized clinical trial seeding built-in.
* **Cons:**
  * ❌ Complex schema dropping and cypher deletion logic.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 achieves the performance requirement of under 15-second local environment resets without container teardowns. It connects directly to PostgreSQL to cascade-drop schemas, runs Cypher detach deletes on Neo4j, and concurrently re-creates SQLite files, and seeds mock clinical trial study data.

## 5. Consequences & Trade-offs
* **Positive Impact:** Seamless developer experience and rapid schema prototyping without service disruption.
* **Negative Impact / Technical Debt:** Requires maintenance of the custom python clean script and mock data generators as schemas change.
* **Mitigation Strategy:** Couple database schema updates with matching updates to the reset script and validate via automated tests.

## 6. Implementation & Verification
* **Affected Repositories / Services:** `scripts/reset_db.py`, `package.json`
* **Verification Plan:** Validated using automated unit/integration tests in `tests/test_reset_db.py` checking connection guardrails, schema recreation, and offline execution.
