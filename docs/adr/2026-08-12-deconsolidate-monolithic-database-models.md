# ADR-2171: Deconsolidate monolithic database models

* **Status:** Accepted
* **Date:** 2026-08-12
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The clinical execution database models (`apps/execution/database/models.py`) were originally consolidated in a single monolithic python file defining 49 database models and enums across various domains (subjects, consent, visits, labs, rtsm, etc.). This monolithic structure is difficult to maintain, leads to high risk of merge conflicts, complicates selective or modular imports, and violates clean domain separation. We need to split this monolithic models file into domain-specific submodules within a new package structure under `apps/execution/database/models/` while maintaining full backward-compatibility and zero breaking changes to existing service imports.

This refactoring supports system maintainability under **PRD-SYS-001** (Standard Audit Logging (21 CFR Part 11 § 11.10(e))) and other regulatory-bound clinical tracking modules by decoupling domains and preventing side effects across database entity schemas during schema discovery.

## 2. Decision Drivers & Constraints

* **Strict Backward-Compatibility**: Must not break any existing imports from `apps.execution.database.models` across hundreds of files in multiple microservices.
* **Maintain GxP Compliance (PRD-SYS-001)**: Immutability triggers and compliance event listeners on GxP models (such as `ConsentFormRecord` and `ConsentSignature`) must remain fully active and correctly linked.
* **Schema Discovery**: Automatic metadata collection for standard SQLAlchemy/Alembic tools (`Base.metadata.create_all`, etc.) must continue to discover all models correctly without manual registering.
* **Audit Isolation**: Systems like system logging must resolve core audit structures without needlessly loading all 49 clinical models or tables.

## 3. Options Considered

1. **Option A (Selected): Deconsolidate models.py into domain submodules under a Python package with a lazy-loading __init__.py shim**
   * Segregate the 49 models and enums into 17 business-isolated submodules (`audit.py`, `subject.py`, `consent.py`, etc.).
   * Use custom dynamic `__getattr__` and `__dir__` implementation in `apps/execution/database/models/__init__.py` to lazy-load objects on demand.
   * Provide a programmatic `discover_models()` helper to load all submodules when `Base.metadata` schema discovery is needed.

2. **Option B (Alternative): Move models but manually update all imports across all services**
   * Physically move the files and rewrite all active imports in hundreds of files across the codebase.
   * Extremely high risk, tedious, and prone to introducing regression bugs.

## 4. Decision Outcome

Chosen option: **Option A** because it achieves clean domain isolation and segregates clinical execution database models into cohesive submodules, while guaranteeing 100% backward-compatibility via python package lazy loading and zero code churn in existing services.

### Key Highlights
* Deconsolidated models mapped into 17 business-isolated submodules.
* Custom dynamic `__getattr__` dynamic module loader resolves imports dynamically on demand.
* GxP trigger and listener registration are carefully preserved.
* Programmatic discovery helper ensures standard schema discovery works cleanly.

## 5. Consequences & Trade-offs

* **Positive**: Cohesive domain boundaries, reduced merge conflict risk, easier schema evolution, and cleaner GxP audit-trail isolation.
* **Positive**: Zero edits required to existing codebase files importing from `models.py`.
* **Negative**: Slightly increased runtime dynamic lookup overhead during first-time resolution of lazy imports (negligible).

## 6. Implementation & Verification

* **Files Modified**:
  * Created `apps/execution/database/models/` package and its submodules.
  * Replaced `apps/execution/database/models.py` with dynamic lazy-loading package entrypoint.
  * Verified import stability in `apps/execution/database/audit.py`.
* **Verification**:
  * Verified ruff linting/formatting pass cleanly.
  * All 708 execution unit tests run and pass without a single import error.
  * Path boundary validation checks run and pass.
