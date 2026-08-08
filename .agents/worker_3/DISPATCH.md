## 2026-08-08T06:45:00Z
Task: Execute Hexagonal Architecture migration and complex refactoring for `ctms` and `execution` microservices (R2).

1. CTMS Refactoring (`apps/ctms/`):
   - Reorganize into 4 flat layers: `domain/`, `application/`, `infrastructure/`, `presentation/`.
   - All repository ports in `domain/` MUST inherit from `packages.hexagonal.RepositoryPort`.
   - CRUCIAL (Repo Splitting): Split the massive repository file in `ctms` iteratively. Rename the file first, then extract one aggregate repository class at a time into `apps/ctms/infrastructure/repositories/`.
   - Thin `main.py`: Extract all routes into `presentation/routers/`. `main.py` must contain ONLY FastAPI setup, middleware, lifespan, and router inclusions.
   - Delete any legacy `src/` directory.

2. Execution Refactoring (`apps/execution/`):
   - Reorganize into 4 flat layers: `domain/`, `application/`, `infrastructure/`, `presentation/`.
   - All repository ports in `domain/` MUST inherit from `packages.hexagonal.RepositoryPort`.
   - CRUCIAL (Domain Deduplication): Resolve domain duplication in `execution`.
   - Preserve GxP audit fields (`created_at`, `created_by`, `reason_for_change`, `version_index`) and ORM models in `apps/execution/database/models.py`.
   - Thin `main.py`: Extract all routes into `presentation/routers/`. `main.py` must contain ONLY FastAPI setup, middleware, lifespan, and router inclusions.
   - Delete any legacy `src/` directory.

3. AGENTS.md Compliance & Verification:
   - `uv run ruff check .` and `uv run ruff format --check .` (0 errors).
   - Import sorting (I001).
   - SQLAlchemy boolean filter pattern (`col.is_(True)` / `col.is_(False)`).
   - Zero cross-service sibling database imports.
   - `uv run pytest apps/ctms apps/execution --no-cov`
   - `uv run python scripts/validate_imports.py`
   - If tests or docstrings changed, run `uv run python scripts/sync_gxp.py`.
