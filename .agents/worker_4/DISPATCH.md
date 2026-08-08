## 2026-08-08T01:49:13Z

You are Worker 4 (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_4.

Your task is to execute Hexagonal Architecture migration and high-complexity refactoring for `designer` and `tickets` microservices (R4):

Read context files first:
1. /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md
3. /Users/fred/Code/cadence-clinical/docs/adr/2026-08-08-hexagonal-architecture-standard.md

Detailed Requirements:

1. **Designer Refactoring (`apps/designer/`)**:
   - Reorganize into 4 flat layers: `domain/`, `application/`, `infrastructure/`, `presentation/`.
   - All repository ports in `domain/` MUST inherit from `packages.hexagonal.RepositoryPort`.
   - CRUCIAL (Iterative Extraction of 5,788-line `main.py`): Extract the massive `apps/designer/main.py` iteratively:
     - Extract routes into `presentation/routers/` (e.g., studies, forms, items, codelists, rules, export, etc.).
     - Move domain logic into `domain/`, application services into `application/`, repository implementations into `infrastructure/repositories/`.
     - Prune `apps/designer/main.py` so it contains ONLY FastAPI app setup, Neo4j / DB driver lifecycle management, middleware, and router inclusions (`app.include_router(...)`).
   - Split massive repository files iteratively into `infrastructure/repositories/`.
   - Delete any legacy `src/` directory.

2. **Tickets Refactoring (`apps/tickets/`)**:
   - Reorganize into 4 flat layers: `domain/`, `application/`, `infrastructure/`, `presentation/`.
   - All repository ports in `domain/` MUST inherit from `packages.hexagonal.RepositoryPort`.
   - Extract routes into `presentation/routers/`. Prune `main.py` so it contains ONLY FastAPI app setup, middleware, and router inclusions.
   - Delete any legacy `src/` directory.

3. **AGENTS.md Compliance & Verification**:
   - `uv run ruff check .` and `uv run ruff format --check .` (0 errors).
   - Import sorting (I001).
   - SQLAlchemy boolean filter pattern (`col.is_(True)` / `col.is_(False)`).
   - Zero cross-service sibling database imports.
   - `uv run pytest apps/designer apps/tickets --no-cov`
   - `uv run python scripts/validate_imports.py`
   - If tests or requirement docstrings changed, run `uv run python scripts/sync_gxp.py`.

When completed:
Write handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_4/handoff.md` and send a summary message back to caller (parent).
