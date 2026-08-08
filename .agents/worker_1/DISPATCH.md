## 2026-08-08T06:17:18Z
You are Worker 1 (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_1.

Your task is to execute Phase 0 Foundation Fixes (R1) and shared library extraction for compliance (part of R3).

Read the following context files first:
1. /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md

Detailed Requirements:
1. Remove `sqlalchemy` dependency from `packages/hexagonal/__init__.py`. Ensure `packages/hexagonal` has zero direct dependencies on `sqlalchemy`.
2. Move `map_database_exceptions` to `packages/database/`. Update all import references across the codebase from `packages.hexagonal` (or wherever `map_database_exceptions` was imported from) to `packages.database`.
3. Verify `pyproject.toml` ruff exclusions for `apps/execution/database/models.py` per AGENTS.md rules.
4. Scaffold and complete an Architecture Decision Record (ADR) for the Hexagonal Architecture Standard:
   Run `python3 scripts/create_adr.py --title "Hexagonal Architecture Standard" --domain "core-platform" --req "PRD-SYS-001"`
   Fill out the created ADR file under `docs/adr/` detailing the 4-layer flat Hexagonal Architecture structure (`domain/`, `application/`, `infrastructure/`, `presentation/`) and repository ports inheriting from `packages.hexagonal.RepositoryPort`.
5. Move `apps/compliance/` to `packages/compliance/`:
   - Move the entire code from `apps/compliance/` into `packages/compliance/`.
   - Update all import statements across `apps/` and `packages/` from `apps.compliance...` to `packages.compliance...`.
   - Delete `apps/compliance/` directory completely.
6. Integrity & Standards Warning:
   DO NOT CHEAT. All implementations must be genuine. Follow AGENTS.md rules (ruff check/format, import sorting, SQLAlchemy boolean filter pattern `col.is_(True)`).
7. Verification:
   Run `uv run ruff check .`
   Run `uv run ruff format .`
   Run `uv run pytest packages/`
   If tests or docstrings changed, run `uv run python scripts/sync_gxp.py` to keep GxP docs synced.

When completed:
Write your handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_1/handoff.md` and send a summary message back to the caller (parent).
