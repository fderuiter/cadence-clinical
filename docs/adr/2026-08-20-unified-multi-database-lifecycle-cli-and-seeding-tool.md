# ADR-106: Unified Multi-Database Lifecycle CLI and Baseline Seeding Tool

- **Status:** Accepted
- **Date:** 2026-08-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

The Cadence Clinical Platform consists of multiple database engines—PostgreSQL, Neo4j, and 10 isolated SQLite microservice databases (eTMF, CTMS, Quality, Interop, Tickets, Notifications, eConsent, Safety, Organization, eISF). Managing developer workspace database resets, migration re-applications, and baseline seed data required multiple disparate scripts and manual setup steps, creating friction and developer environment state drift.

## 2. Decision Drivers & Constraints

- **Driver 1:** Provide a single unified command (`pnpm db:reset` / `uv run cadence db reset`) to purge, re-migrate, and seed all databases in under 15 seconds.
- **Driver 2:** Prevent accidental execution against non-local or production environment database endpoints.
- **Driver 3:** Support offline execution (`--allow-offline`) when network-bound databases (Neo4j/PostgreSQL) are unreachable.

## 3. Options Considered

### Option 1: Maintain separate per-service database reset scripts

- **Overview:** Rely on existing per-service scripts without a central orchestrator.
- **Pros:**
  - ✅ Simple individual scripts.
- **Cons:**
  - ❌ Inconsistent reset states and higher developer setup time.

### Option 2: Unified Python database lifecycle tool with safety guards (Selected)

- **Overview:** Implement `scripts/db_lifecycle.py` to concurrently orchestrate purging, migration re-application, and YAML-driven baseline seeding across PostgreSQL, Neo4j, and SQLite microservices.
- **Pros:**
  - ✅ Fast concurrent execution (~9s total runtime).
  - ✅ Programmatic safety guards (`validate_local_only`) aborting on non-local connection strings.
  - ✅ Centralized baseline seeding from `data/seeds/baseline_clinical_trial.yaml`.
- **Cons:**
  - ❌ Central script must maintain awareness of all microservice database connections.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing multi-database lifecycle operations inside `scripts/db_lifecycle.py` and linking it to `package.json` (`pnpm db:reset`) and Cadence CLI (`cadence db reset`) ensures reproducible workspace environments and strict production safety guards.

## 5. Consequences & Trade-offs

- **Positive Impact:** Single-command workspace reset and seeding completes reliably in ~9 seconds with full offline fallback support.
- **Negative Impact / Technical Debt:** New microservice databases must be registered in `scripts/db_lifecycle.py`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `scripts/db_lifecycle.py`, `scripts/reset_db.py`, `packages/cli/commands/db.py`, `package.json`, `data/seeds/baseline_clinical_trial.yaml`.
- **Verification Plan:** Validated via unit tests in `packages/database/tests/test_db_lifecycle.py` and running `pnpm db:reset --allow-offline`.
