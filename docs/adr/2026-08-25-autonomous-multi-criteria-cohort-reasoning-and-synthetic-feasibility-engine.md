# ADR-2397: Autonomous Multi Criteria Cohort Reasoning and Synthetic Feasibility Engine

Also known as: ADR-2218 (former number)

- **Status:** Accepted
- **Date:** 2026-08-25
- **Authors:** @google-labs-jules[bot]
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Designing clinical trial protocol cohorts requires evaluating complex multi-criteria eligibility AST expressions against population profiles while simulating recruitment feasibility under complete, partial, and missing observation data scenarios. Prior to this design, cohort feasibility analysis lacked dedicated background worker execution, PostgreSQL advisory locking, de-identification scrubbing, and Kleene 3-valued logic (3VL) missing-data simulation. Requirement ID: PRD-SYS-051.

## 2. Decision Drivers & Constraints

* Asynchronous execution for heavy feasibility simulations without blocking HTTP endpoints (PRD-SYS-051).
* PostgreSQL transaction advisory locks (`pg_try_advisory_xact_lock`) with in-memory lock fallbacks to prevent duplicate compute tasks for identical study cohorts.
* Strict GxP and Part 11 de-identification scrubbing (`packages/deid`) for evaluation prompts and patient records.
* Support for Kleene 3VL (`TRUE`, `FALSE`, `INDETERMINATE`) to assess missing data risk.
* Granular node-level AST evaluation traces and multi-criteria sub-cohort recommendations.

## 3. Options Considered

### Option 1: **Option A (Selected): Dedicated Asynchronous Cohort Feasibility Engine in Designer with PostgreSQL Advisory Locking and Kleene 3VL Simulation.**

- **Overview:** **Option A (Selected): Dedicated Asynchronous Cohort Feasibility Engine in Designer with PostgreSQL Advisory Locking and Kleene 3VL Simulation.**
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B: Synchronous inline evaluation without advisory locks or background worker queuing.**

- **Overview:** **Option B: Synchronous inline evaluation without advisory locks or background worker queuing.**
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

- **Chosen Option:** Option A because it provides non-blocking API endpoints, prevents duplicate background jobs across concurrent users via advisory locks, enforces air-gapped de-identification, and quantifies missing data risks using Kleene 3VL logic.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

* **Positive:** Non-blocking API calls, robust advisory locks preventing duplicate execution, full de-identification air-gapping, and detailed node-level AST fit explanations.
* **Negative:** Requires maintaining asynchronous worker job state in memory/database.

## 6. Implementation & Verification

* `apps/designer/domain/cohort_feasibility_models.py`: Domain contracts and DTO models.
* `apps/designer/workers/cohort_feasibility_worker.py`: Advisory lock coordinator and worker status store.
* `apps/designer/application/services/cohort_feasibility_engine.py`: Core feasibility reasoning engine.
* `apps/designer/presentation/routers/cohort_feasibility.py`: REST endpoints (`POST /evaluate`, `GET /jobs/{job_id}`, `POST /synthetic-simulate`).
* `apps/designer/tests/test_cohort_feasibility_engine.py`: Unit and integration test suite referencing `@req:PRD-SYS-051`.
