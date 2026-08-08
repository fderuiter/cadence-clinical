## 2026-08-08T07:08:25Z
You are Worker 5 (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_5.

Your task is to implement comprehensive Pytest-Archon boundary tests and execute the final full-suite verification across all 14 Python microservices (R4 & Acceptance Criteria).

Read context files first:
1. /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md
3. /Users/fred/Code/cadence-clinical/docs/adr/2026-08-08-hexagonal-architecture-standard.md

Detailed Requirements:

1. **Pytest-Archon Boundary Tests (`packages/hexagonal/tests/test_hexagonal_architecture.py`)**:
   - Ensure `packages/hexagonal/tests/test_hexagonal_architecture.py` contains comprehensive `pytest-archon` boundary tests for ALL 14 microservices (`gateway`, `interop`, `notifications`, `org`, `safety`, `econsent`, `quality`, `eisf`, `etmf`, `ctms`, `execution`, `designer`, `tickets`).
   - Test rules:
     - `domain/` layer isolation (no FastAPI / SQLAlchemy / framework imports).
     - `application/` layer isolation (no presentation or raw database imports).
     - `presentation/` routers have no direct database imports.
     - `main.py` entrypoints are thin (no inline business logic or route handlers).
     - Service-specific repository ports subclass `packages.hexagonal.RepositoryPort`.

2. **Automated Verification Commands**:
   - `uv run pytest packages/hexagonal/tests/test_hexagonal_architecture.py -v --no-cov` (Must pass completely for all 14 services).
   - `uv run pytest -n auto --cov=apps --cov=packages --cov-fail-under=80` (Must pass with coverage ≥ 80%).
   - `uv run ruff check .` (0 errors).
   - `uv run ruff format --check .` (0 formatting errors).
   - `uv run python scripts/validate_imports.py` (0 cross-service import violations).

3. **Structural Verification**:
   - Confirm `apps/compliance/` directory does not exist and is replaced by `packages/compliance/`.
   - Confirm no `apps/*/src/` directories exist across any of the 14 microservices.
   - Confirm all `main.py` entrypoints in `apps/` contain only FastAPI setup and router inclusions.
   - Confirm all service repository ports inherit from `packages.hexagonal.RepositoryPort`.

4. **GxP Compliance Synchronization**:
   - Run `uv run python scripts/sync_gxp.py` to regenerate and stage RTM and execution reports.

When completed:
Write full handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_5/handoff.md` and send a summary message back to caller (parent).
