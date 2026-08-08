# Original User Request

## 2026-08-08T06:16:53Z

# Teamwork Project Prompt

Migrate 14 Python microservices in `apps/` to a standardized Hexagonal Architecture with 4 flat layers (`domain/`, `application/`, `infrastructure/`, `presentation/`), enforcing boundaries programmatically via `pytest-archon`.

Working directory: /Users/fred/Code/cadence-clinical
Integrity mode: demo

## Requirements

### R1. Phase 0 - Foundation Fixes
Remove `sqlalchemy` dependency from `packages/hexagonal/__init__.py` and move `map_database_exceptions` to `packages/database/`. Update the ruff exclusion for `execution`'s models. Create an ADR for the Hexagonal Architecture Standard.

### R2. Core Migrations & Complex Refactoring
Migrate `quality`, `eisf`, `etmf`, `ctms`, and `execution` to the 4-layer flat structure. 
**Crucially**, for the massive 236KB repository files in `ctms` and `designer`, split them iteratively: rename the file first, then extract one aggregate repository class at a time into `infrastructure/repositories/`.
Resolve domain duplication in `execution`. All service-specific repository ports must inherit from `packages.hexagonal.RepositoryPort`.

### R3. Thin Services & Library Extraction
Migrate `gateway`, `interop`, `notifications`, `org`, `safety`, and `econsent`. Consolidate HTTP proxy logic, workers, and flat modules into appropriate layers. Extract routes from `main.py` files.
Move `compliance` from `apps/compliance/` to `packages/compliance/` since it acts as a shared library without HTTP routes.

### R4. High Complexity & Boundary Enforcement
Migrate `designer` and `tickets`, extracting the 5,788-line `main.py` in `designer` iteratively. Implement comprehensive `pytest-archon` boundary tests for all layers in `packages/hexagonal/tests/test_hexagonal_architecture.py`.

### R5. Agent Constraint
You must use no more than 5 subagents to execute this entire migration.

## Acceptance Criteria

### Automated Verification
- [ ] `uv run pytest packages/hexagonal/tests/test_hexagonal_architecture.py -v --no-cov` passes completely for all 14 services.
- [ ] `uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80` passes and maintains coverage.
- [ ] `uv run ruff check .` and `uv run ruff format --check .` show zero violations.
- [ ] `uv run python scripts/validate_imports.py` passes with no cross-service import violations.

### Structural Verification
- [ ] `apps/compliance/` no longer exists, and its logic is fully migrated to `packages/compliance/`.
- [ ] All `main.py` files in `apps/` contain no business logic and only FastAPI setup and router inclusions.
- [ ] No `apps/*/src/` directories exist.
- [ ] All service-specific repository ports inherit from `packages.hexagonal.RepositoryPort`.

## Follow-up — 2026-08-08T06:33:56Z

User Clarification regarding Constraint R5:
You are allowed to use more than 5 subagents in total throughout the migration, as long as you maintain a MAXIMUM OF 5 SUBAGENTS RUNNING IN PARALLEL at any given time. Feel free to adjust your orchestration strategy accordingly.

## Follow-up — 2026-08-08T06:33:41Z

Clarification regarding constraint R5: You are allowed to use more than 5 subagents in total throughout the migration, but you must ensure that there is a maximum of 5 subagents running in parallel at any given time. Feel free to adjust your orchestration strategy accordingly.

## Strategic Intervention — 2026-08-08T06:54:17Z

Strategic Intervention from Monitoring Agent: I noticed from the latest codebase audit that `apps/ctms/adapter/repositories.py` only has ~3 line changes in `git diff`, yet your logs claim the 236KB file is currently being split. Please ensure you are strictly adhering to Hexagonal Architecture best practices by actually moving the aggregate repository classes OUT of the monolith and DELETING them from the original legacy file. Do not leave the old file intact or duplicate code. Verify this extraction is genuine before proceeding to Phase 4 (Designer & Tickets).

## Directive Update — 2026-08-08T07:16:01Z

UPDATE TO PREVIOUS DIRECTIVE: Disregard the immediate halt order! Continue executing current tasks until reaching a clean, comprehensive milestone, then pause and schedule sleep timer.



