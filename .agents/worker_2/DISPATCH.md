## 2026-08-08T01:23:12Z
You are Worker 2 (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_2.

Your task is to execute the Hexagonal Architecture migration for 9 thin & medium microservices (R2 and R3):
Services: `gateway`, `interop`, `notifications`, `org`, `safety`, `econsent`, `quality`, `eisf`, `etmf`.

Read the following context files first:
1. /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md
3. /Users/fred/Code/cadence-clinical/docs/adr/2026-08-08-hexagonal-architecture-standard.md

Detailed Requirements for each of the 9 services (`apps/<service>/`):
1. **4-Layer Flat Structure**:
   Reorganize/refactor each service into 4 flat layers:
   - `domain/`: Entities, value objects, domain exceptions, repository ports. ALL repository ports MUST inherit from `packages.hexagonal.RepositoryPort`.
   - `application/`: Application services, use cases, DTOs.
   - `infrastructure/`: Repositories (implementing domain ports), ORM models, HTTP proxy clients, background workers, external adapters.
   - `presentation/`: FastAPI routers (placed in `presentation/routers/`), request/response schemas.
2. **Thin `main.py`**:
   Extract all routes and business logic from `main.py` into `presentation/routers/`. Ensure `main.py` contains ONLY FastAPI app instantiation, middleware setup, and router inclusions (`app.include_router(...)`).
3. **Clean Up Directory Layout**:
   Remove any `src/` directories if present (e.g. `apps/<service>/src/`). All code must live directly under `apps/<service>/domain/`, `application/`, `infrastructure/`, `presentation/`, or `main.py`.
4. **Proxy & Worker Consolidation**:
   Consolidate proxy logic (e.g. in `gateway` and `interop`), background workers, and flat modules into their appropriate layers (`infrastructure/` or `application/`).
5. **AGENTS.md Compliance**:
   - Ruff lint & format clean (`uv run ruff check .` and `uv run ruff format .`).
   - Import sorting (I001 standard -> 3rd party -> 1st party).
   - SQLAlchemy boolean filter pattern (`col.is_(True)` / `col.is_(False)`).
   - Zero cross-service sibling database imports.
6. **Verification**:
   - Run tests: `uv run pytest apps/gateway apps/interop apps/notifications apps/org apps/safety apps/econsent apps/quality apps/eisf apps/etmf --no-cov`
   - Run `uv run ruff check .`
   - Run `uv run ruff format --check .`
   - Run `uv run python scripts/validate_imports.py`
   - If tests or requirement docstrings changed, run `uv run python scripts/sync_gxp.py`

When completed:
Write your handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_2/handoff.md` and send a summary message back to the caller (parent).
