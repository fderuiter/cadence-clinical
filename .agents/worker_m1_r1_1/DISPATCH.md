## 2026-08-07T18:35:35Z
<USER_REQUEST>
You are Worker 1 (teamwork_preview_worker) for Milestone M1: Foundational Core Utilities Migration.
Your working directory is: /Users/fred/Code/cadence-clinical/.agents/worker_m1_r1_1/
Project root: /Users/fred/Code/cadence-clinical/

MANDATORY INPUT FILES:
- Original Request: /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- Master Plan: /Users/fred/Code/cadence-clinical/PROJECT.md
- Scope Document: /Users/fred/Code/cadence-clinical/.agents/sub_orch_m1/SCOPE.md
- Synthesis & Plan: /Users/fred/Code/cadence-clinical/.agents/sub_orch_m1/synthesis.md
- Explorer Handoff 1: /Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_1/handoff.md
- Explorer Handoff 2: /Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_2/handoff.md
- Explorer Handoff 3: /Users/fred/Code/cadence-clinical/.agents/explorer_m1_r1_3/handoff.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

YOUR TASK:
1. Relocate shared infrastructure/GxP utilities out of `packages/core-models`:
   - `audit.py` (`Part11AuditMixin`, `AuditFields`) -> `packages/database/audit.py`
   - `datetime_helpers.py` -> `packages/database/datetime_helpers.py`
   - `signature.py` (`SigningReason`, `ApprovalStatus`, `SignatureManifestation`) -> `packages/security/signature.py`
   - `storage/` -> `packages/storage/`
   Remove the old files from `packages/core-models/` once moved.
2. Update all import statements across `apps/`, `packages/`, `scripts/` (e.g. `scripts/detect_duplication.py`), and test suites (`tests/`, `apps/*/tests/`, `packages/*/tests/`) to reference the new target module paths.
3. Check package `pyproject.toml` files (e.g. `packages/core-models/pyproject.toml`) and `__init__.py` files to ensure exports and package definitions are clean and valid.
4. Run `uv run ruff check . --fix` and `uv run ruff format .` to ensure import sorting and code formatting pass cleanly.
5. Run affected test suites (`uv run pytest -n auto` or relevant pytest paths) to ensure 100% passing tests.
6. If test outputs or RTM docs are updated, run `uv run python scripts/sync_gxp.py` to sync GxP compliance docs.

OUTPUT REQUIREMENTS:
- Save details of changes to `/Users/fred/Code/cadence-clinical/.agents/worker_m1_r1_1/changes.md`
- Save complete 5-component handoff report to `/Users/fred/Code/cadence-clinical/.agents/worker_m1_r1_1/handoff.md` including build/lint/test execution commands and outputs.
- Send a message back to parent orchestrator when complete.
</USER_REQUEST>
