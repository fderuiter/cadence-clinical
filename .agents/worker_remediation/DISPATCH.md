## 2026-08-08T07:34:04Z

<USER_REQUEST>
You are Remediation Worker (teamwork_preview_worker). Your working directory is /Users/fred/Code/cadence-clinical/.agents/worker_remediation.

Your task is to resolve 2 ruff linting errors reported by the Independent Victory Auditor and re-verify all quality gates.

Context files:
1. /Users/fred/Code/cadence-clinical/.agents/auditor/audit_report.md
2. /Users/fred/Code/cadence-clinical/AGENTS.md

Specific Errors to Fix:
1. `apps/ctms/presentation/routers/doa.py:31:1`: `E402 Module level import not at top of file` -> Move module import to the top of the file in alphabetical order.
2. `apps/econsent/main.py:1:1`: `I001 Import block is un-sorted or un-formatted` -> Fix import sorting per AGENTS.md guidelines.

Verification Steps:
1. Run `uv run ruff check . --fix`
2. Run `uv run ruff format .`
3. Run `uv run ruff check .` (Must return 0 errors across entire workspace)
4. Run `uv run ruff format --check .` (Must return 0 formatting errors)
5. Run `uv run pytest packages/hexagonal/tests/test_hexagonal_architecture.py -v --no-cov` (Must pass 43/43 tests)
6. Run `uv run python scripts/validate_imports.py` (Must pass with 0 violations)
7. Run `uv run python scripts/sync_gxp.py` (Must sync and stage GxP compliance docs)

When completed:
Write handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_remediation/handoff.md` and send a summary message back to caller (parent).
</USER_REQUEST>
