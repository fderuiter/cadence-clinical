## 2026-08-07T19:27:07Z
You are Worker 1 for Milestone M1 (Round 2).
Your working directory is `/Users/fred/Code/cadence-clinical/.agents/worker_m1_r2_1/`.

MANDATORY INSTRUCTION: You MUST read `/Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md` before starting work.

Also read:
- Project Scope: `/Users/fred/Code/cadence-clinical/PROJECT.md`
- Explorer Handoff: `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r2_1/handoff.md`
- Sub-Orchestrator Dispatch: `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m1_gen2/DISPATCH.md`

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Objective:
1. Fix packaging build configuration in:
   - `packages/database/pyproject.toml`
   - `packages/security/pyproject.toml`
   - `packages/storage/pyproject.toml`
   (and `packages/deid/pyproject.toml`, `packages/hexagonal/pyproject.toml` if needed)
   Add `packages = ["."]` under `[tool.hatch.build.targets.wheel]` so Hatchling includes the top-level modules when building wheels.
2. Run build verification:
   - `export PATH=$HOME/.local/bin:$HOME/.cargo/bin:$PATH`
   - `uv build --package packages-database`
   - `uv build --package packages-security`
   - `uv build --package packages-storage`
   - `uv build --package packages-core-models`
3. Run linting, formatting, duplication scanning, unit tests, and GxP compliance sync:
   - `uv run ruff check .`
   - `uv run ruff format .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
   - `uv run python scripts/sync_gxp.py`
4. Document all changes made, commands executed, and their exact outputs in `/Users/fred/Code/cadence-clinical/.agents/worker_m1_r2_1/handoff.md` and report completion back to the sub-orchestrator.
