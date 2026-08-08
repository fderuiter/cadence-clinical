## 2026-08-08T06:54:33Z
You are CTMS Remediation Worker (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_ctms_fix.

Your task is to fix `apps/ctms/` to ensure genuine repository extraction per Hexagonal Architecture best practices.

Read context files first:
1. /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md
3. /Users/fred/Code/cadence-clinical/docs/adr/2026-08-08-hexagonal-architecture-standard.md

Detailed Requirements:
1. Inspect `apps/ctms/adapter/repositories.py` (or legacy repository files) and `apps/ctms/infrastructure/repositories/`.
2. Move all aggregate repository classes and logic OUT of `apps/ctms/adapter/repositories.py` into modular files under `apps/ctms/infrastructure/repositories/`.
3. PRUNE and DELETE all repository implementation code from `apps/ctms/adapter/repositories.py`. Delete the legacy file entirely or replace its content with thin re-exports if required for external imports, but do NOT leave the 236KB monolith intact or duplicate repository logic.
4. Ensure all CTMS repository ports in `apps/ctms/domain/` inherit from `packages.hexagonal.RepositoryPort`.
5. Verify `apps/ctms/main.py` is thin (FastAPI setup and router inclusions ONLY).
6. Run verification:
   - `uv run pytest apps/ctms --no-cov`
   - `uv run ruff check .`
   - `uv run ruff format --check .`
   - `uv run python scripts/validate_imports.py`
   - `uv run python scripts/sync_gxp.py`

When completed:
Write handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_ctms_fix/handoff.md` and send completion message to parent.
