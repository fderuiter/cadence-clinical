# Detailed Plan: Hexagonal Architecture Migration (14 Microservices)

## Overview
Migrate 14 Python microservices in `apps/` to Hexagonal Architecture under a strict limit of 5 subagent dispatches total.

## Execution Schedule (5 Subagents Max)

### Subagent 1: Phase 0 Foundation & Library Extraction
- **Role**: `teamwork_preview_worker`
- **Scope**:
  1. Remove `sqlalchemy` dependency from `packages/hexagonal/__init__.py`.
  2. Move `map_database_exceptions` to `packages/database/`.
  3. Update `pyproject.toml` ruff exclusion for `apps/execution/database/models.py` if needed.
  4. Create ADR `2026-08-08-hexagonal-architecture-standard.md` in `docs/adr/` using `python3 scripts/create_adr.py --title "Hexagonal Architecture Standard" --domain "core-platform" --req "PRD-SYS-001"`.
  5. Move `apps/compliance/` to `packages/compliance/`. Update all imports across the codebase from `apps.compliance` to `packages.compliance`. Remove `apps/compliance/`.
- **Verification**: Run `uv run pytest packages/` and `uv run ruff check .`

### Subagent 2: Thin & Medium Microservices (9 services)
- **Role**: `teamwork_preview_worker`
- **Scope**:
  Migrate 9 services: `gateway`, `interop`, `notifications`, `org`, `safety`, `econsent`, `quality`, `eisf`, `etmf`.
  For each service:
  - Reorganize into `domain/`, `application/`, `infrastructure/`, `presentation/`.
  - Extract routes from `main.py` into `presentation/routers/`. Ensure `main.py` contains ONLY FastAPI app setup and router inclusions.
  - Consolidate HTTP proxy logic, workers, and flat modules into appropriate layers.
  - Make service repository ports inherit from `packages.hexagonal.RepositoryPort`.
  - Delete any remaining `src/` directories.
- **Verification**: Run unit/integration tests for these 9 services, `uv run ruff check .`, `uv run ruff format --check .`, `uv run python scripts/validate_imports.py`.

### Subagent 3: Complex Microservices (CTMS & Execution)
- **Role**: `teamwork_preview_worker`
- **Scope**:
  Migrate `ctms` and `execution`.
  - For `ctms`: Reorganize to 4-layer flat structure. Split massive 236KB repository file iteratively into `infrastructure/repositories/`. Extract aggregate repository classes one by one.
  - For `execution`: Reorganize to 4-layer flat structure. Resolve domain duplication in `execution`. Ensure repository ports inherit from `packages.hexagonal.RepositoryPort`.
  - Clean `main.py` in both services (FastAPI setup & router inclusions only).
- **Verification**: Run `execution` and `ctms` tests, ruff, import validator.

### Subagent 4: High Complexity Microservices (Designer & Tickets)
- **Role**: `teamwork_preview_worker`
- **Scope**:
  Migrate `designer` and `tickets`.
  - For `designer`: Reorganize to 4-layer flat structure. Extract 5,788-line `main.py` iteratively into `presentation/routers/`, `application/`, `domain/`, `infrastructure/`. Split massive repository files iteratively into `infrastructure/repositories/`. Ensure `main.py` has no business logic.
  - For `tickets`: Reorganize to 4-layer flat structure, extract routes, clean `main.py`.
  - Ensure all repository ports inherit from `packages.hexagonal.RepositoryPort`.
- **Verification**: Run `designer` and `tickets` tests, ruff, import validator.

### Subagent 5: Archon Tests, Full Verification & GxP Sync
- **Role**: `teamwork_preview_worker`
- **Scope**:
  1. Implement comprehensive `pytest-archon` boundary tests in `packages/hexagonal/tests/test_hexagonal_architecture.py` checking all 14 services for strict 4-layer boundary isolation.
  2. Run `uv run pytest packages/hexagonal/tests/test_hexagonal_architecture.py -v --no-cov`.
  3. Run full test suite with coverage: `uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80`.
  4. Run `uv run ruff check .` and `uv run ruff format --check .`.
  5. Run `uv run python scripts/validate_imports.py`.
  6. If tests or requirements changed, run `uv run python scripts/sync_gxp.py` to sync GxP docs.
- **Verification**: All acceptance criteria passing.
