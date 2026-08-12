# ADR-2170: Decoupled PostgreSQL Schema Introspection and Database Triggers for GxP Compliance

* **Status:** Accepted
* **Date:** 2026-08-12
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent unauthorized, untracked, or direct mutations of clinical data within the PostgreSQL/SQLite execution databases (to meet regulatory GxP 21 CFR Part 11 requirements, specifically PRD-SYS-001), we need an out-of-band schema introspection engine and database-level triggers. The introspection tool must generate type-safe TypeScript interfaces from clinical relational schemas but exclude system/compliance-only tables to maintain clear GxP boundaries.

## 2. Decision Drivers & Constraints

* Ensure strict write protection for Electronic Data Capture (EDC) schemas so that any database modifications are attributed to a session user context (`cadence.current_user_id`, under PRD-SYS-001).
* Standardize on generating TypeScript definitions without exposing internal compliance tables.
* Environment safety: Block introspection on production environments automatically to protect sensitive patient/clinical data.

## 3. Options Considered

1. Option A (Selected): Implement an out-of-band schema introspection script using SQLAlchemy reflection alongside database-side SQL triggers for both PostgreSQL and SQLite.
2. Option B (Alternative): Keep manual tracking of TypeScript definitions and rely on application-level validations only.

## 4. Decision Outcome

Chosen option: Option A because it ensures absolute auditability at the database level and eliminates manual divergence between backend database schemas and frontend types, while maintaining strict compliance boundaries.

## 5. Consequences & Trade-offs

* Positive: Compile-time type-safety on frontend schemas, robust database-level audit context enforcement, and zero manual schema tracking.
* Negative: Maintenance overhead of custom trigger and reflection synchronization logic across PostgreSQL and SQLite database variants.

## 6. Implementation & Verification

* Modified `apps/execution/database/migrate.py` to deploy `capture_model_mutation` triggers andSQLite audit constraints.
* Created `scripts/introspect_pg_schema.py` to extract PostgreSQL/SQLite structures into type definitions.
* Validated correctness using automated verification tests added in `apps/execution/tests/test_decoupled_pg_introspection_triggers.py`.

