# ADR-2185: Unified Multi-Database Schema Lifecycle and Baseline Seeding CLI

* **Status:** Accepted
* **Date:** 2026-08-20
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Multi-engine database reset and clinical data seeding across PostgreSQL, Neo4j, and SQLite instances was fragmented across multiple standalone legacy scripts. A unified CLI tool (`scripts/db_lifecycle.py`) and workspace entry point (`pnpm db:reset`) were needed to provide concurrent multi-engine purging, schema migration re-application, safety guardrails, and YAML-driven baseline trial seeding (PRD-SYS-001).

## 2. Decision Drivers & Constraints

* Unified multi-database reset and seeding execution across PostgreSQL, Neo4j, and SQLite in < 15s.
* Fail-fast local-only connection validation to prevent accidental operations on remote or production environments.
* Support for custom YAML seed configurations for baseline clinical trial data population.
* Single-command developer recovery via `pnpm db:reset` and backward-compatible delegation from `cadence db reset`.

## 3. Options Considered

1. Option A (Selected): Implement `scripts/db_lifecycle.py` using `asyncio.gather` for concurrent engine connections, YAML-driven seeding, and local-only guardrails.
2. Option B (Alternative): Maintain separate, sequential shell and Python scripts per database engine.

## 4. Decision Outcome

Chosen option: Option A because it unifies multi-engine schema lifecycle and seeding operations while adhering to strict local safety guardrails and PRD-SYS-001 requirements.

## 5. Consequences & Trade-offs

* Positive: Single-command environment recovery in ~6 seconds with robust safety validation and custom seed file support.
* Negative: Requires maintaining a unified script interfacing with multiple async database client libraries.

## 6. Implementation & Verification

* Implemented `scripts/db_lifecycle.py` and updated `package.json`, `packages/cli/commands/db.py`, and `scripts/reset_db.py`.
* Verified with unit and integration tests in `scripts/tests/test_db_lifecycle.py` and `packages/database/tests/test_reset_db.py`.

