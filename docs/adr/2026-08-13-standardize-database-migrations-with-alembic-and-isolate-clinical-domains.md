# ADR-2173: Standardize Database Migrations with Alembic and Isolate Clinical Domains

* **Status:** Accepted
* **Date:** 2026-08-13
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The clinical trial execution service historically coupled custom manual relational migrations and domain schemas, leading to a complex schema progression and high maintenance overhead. To ensure robust GxP compliance and clean engineering boundaries, we need to transition the `execution` service from monolith-style manual migrations to a standard, version-controlled Alembic migration strategy, and fully isolate clinical domains into sub-routers and packages with automated boundary checks. This traces and satisfies requirements under **PRD-SYS-001**, **Trace-1**, and **Trace-2**.

## 2. Decision Drivers & Constraints

* **Driver 1:** Predictable, repeatable, and traceable schema state progression.
* **Driver 2:** Strict enforcement of Hexagonal architecture and separation of concerns across clinical domains.
* **Driver 3:** High compliance with GxP auditing rules by securing a chronological versioned database history.

## 3. Options Considered

### Option 1: Custom Ad-hoc SQL Migrations
* **Overview:** Keep using individual custom SQL scripts loaded during startup.
* **Pros:**
  * ✅ No additional third-party dependencies required.
* **Cons:**
  * ❌ Manual orchestration is highly error-prone, untyped, and lacks systematic rollback support.
  * ❌ No baseline revision tracking or detection of schema drift.

### Option 2: Standardized Migrations with Alembic & Domain Packages (Selected)
* **Overview:** Introduce Alembic to manage execution schema state and isolate clinical sub-routers and packages with explicit boundary gates.
* **Pros:**
  * ✅ Declarative, standard, and version-controlled database migrations with full revision hashes.
  * ✅ Clear package and presentation-level encapsulation prevents direct coupling between clinical workflows.
  * ✅ Robust rollback capabilities and support for conditional postgres schema/table assertions.
* **Cons:**
  * ❌ Requires maintaining migration metadata and Alembic's environment configuration.

## 4. Decision Outcome

* **Chosen Option:** Option 2
* **Justification:** Choosing Option 2 guarantees structured database versioning. Programmatic baselines and clear sub-router domain packages ensure safety-critical clinical trial logic remains clean, isolated, testable, and compliant with GxP standards.

## 5. Consequences & Trade-offs

* **Positive Impact:** Programmatic schema migrations, automated drift/boundary tests, and seamless schema rollbacks for local and CI environments.
* **Negative Impact / Technical Debt:** Added Alembic metadata files that must be maintained along with revision hashes.
* **Mitigation Strategy:** Automated validation pipelines and local pytest execution to ensure database setup and domain rules remain synchronized on every change.

## 6. Implementation & Verification

* **Affected Repositories / Services:** `apps/execution`
* **Verification Plan:** Validated by running the extensive execution test suite `pytest apps/execution/tests/` and checking ADR correctness via `python3 scripts/validate_adrs.py`.

