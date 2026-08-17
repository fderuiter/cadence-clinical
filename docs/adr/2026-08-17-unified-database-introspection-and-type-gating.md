# ADR-2174: Unified Database Schema Introspection and Automated Type Synchronization Gating

- **Status:** Accepted
- **Date:** 2026-08-17
- **Authors:** @google-labs-jules-bot
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In multi-service systems with distinct databases—such as Clinical Trial Management System (CTMS), Electronic Investigator Site File (eISF), and Electronic Data Capture (EDC) core execution engines—maintaining client-side TypeScript type definitions in perfect sync with Python SQLModel database models is critical. 

Previously, we established an offline schema generator. However, we lacked:
1. **Consolidation:** Aggregating schemas across CTMS, eISF, and execution services into a single unified client types file (`apps/web/src/types/db_schemas.ts`).
2. **Automated Gating:** A mechanism in the continuous integration (CI) pipeline to ensure that developers do not modify SQLAlchemy/SQLModel backend schemas without also committing the updated client-side TypeScript definitions.
3. **Collision Resilience:** Gracefully handling matching model/table names (e.g., `lab_test_master` vs `LabTestMaster`) across decoupled microservice domain boundaries.

This decision addresses requirements under Trace-8 and PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Type Parity Guarantee:** Avoid manual and error-prone sync steps, ensuring the frontend type checker is completely aware of backend model updates.
- **Fail-Fast CI/CD Gating:** Prevent PR merges where schema modifications are missing corresponding TypeScript file updates.
- **Microservice Autonomy & Collision Handling:** Keep database model structures distinct in Python, but compile them into a conflict-free representation for the client application.
- **No Parallel Leakage:** Prevent transient test-only tables (created during parallel, asynchronous execution of `pytest` suites) from contaminating generated schemas.

## 3. Options Considered

### Option 1: On-demand Frontend Compilation with Live Database (Legacy)

- **Overview:** Rely on developers to manually run introspection against a local running database during development and update typescript files when needed.
- **Pros:**
  - ✅ Simple structure.
- **Cons:**
  - ❌ Lacks enforcement: developers frequently forget to commit updated TypeScript files, leading to runtime failures on staging.
  - ❌ Demands a running PG instance in CI.

### Option 2: Unified Static Generator with Automated Drift Verification Gate (Selected)

- **Overview:** 
  1. Extend the offline introspection generator (`scripts/introspect_pg_schema.py`) to parse, compile, and output a single consolidated `db_schemas.ts` representing CTMS, eISF, and core execution databases.
  2. Implement collision-resolution mappings (e.g., mapping duplicate structures to specific legacy variants).
  3. Introduce a robust CI integration test (`test_committed_typescript_schema_is_up_to_date`) in `tests/validation/test_offline_schema_drift.py` that compares the committed `db_schemas.ts` file against a freshly generated on-the-fly instance.
- **Pros:**
  - ✅ Zero manual tracking needed: CI automatically catches and blocks out-of-sync schemas.
  - ✅ High collision resilience through mapping tables.
  - ✅ Safe and fast run time, running instantly under standard unit/validation pytest.
- **Cons:**
  - ❌ Requires importing all service modules into the script context, which must be kept mock-friendly.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 eliminates any risk of schema drift between backend SQLModel records and client Vue 3 state management, while keeping compile and test pipelines fast, automated, and hermetic.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Developers receive immediate feedback on schema drifts.
  - Client application achieves 100% type safety regarding backend payloads.
- **Negative Impact / Technical Debt:**
  - Requires developers to commit updated TypeScript schemas when modifying any SQLModel database structures.
  - Python schema introspector must exclude transient parallel test-runner tables.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `scripts/introspect_pg_schema.py`
  - `apps/web/src/types/db_schemas.ts`
  - `tests/validation/test_offline_schema_drift.py`
- **Verification Plan:**
  - Verified via `uv run pytest tests/validation/test_offline_schema_drift.py` which executes the schema drift gate check.
  - Standardized ADR validation check runs clean.
