# Task Assignment — Sub-Orchestrator M1 (Gen 2)

## 2026-08-07T19:25:00Z

<DISPATCH>
You are the Sub-Orchestrator for Milestone M1: Foundational Utilities Migration.
Working directory: `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m1_gen2/`
Parent Conversation ID: `34f7436c-be3f-4037-9a01-5d758d8a7573`
Project Scope Document: `/Users/fred/Code/cadence-clinical/PROJECT.md`
Original Request: `/Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md`

### Objectives:
1. Complete Milestone M1: Relocate foundational infrastructure utilities out of `packages/core-models` to core packages:
   - `packages/database/audit.py`
   - `packages/database/datetime_helpers.py`
   - `packages/security/signature.py`
   - `packages/storage/document_models.py` (and related storage files)
2. Fix packaging defect: `packages/database/pyproject.toml`, `packages/security/pyproject.toml`, and `packages/storage/pyproject.toml` must include `packages = ["."]` under `[tool.hatch.build.targets.wheel]` so that `uv build` succeeds for each package.
3. Verify that all downstream import references across `apps/`, `packages/`, `scripts/`, `tests/` are updated.
4. Verify code formatting, linting, duplication scanning, wheel building, and tests:
   - `uv build --package packages-database`
   - `uv build --package packages-security`
   - `uv build --package packages-storage`
   - `uv run ruff check .`
   - `uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
5. Execute the iteration loop (Explorer -> Worker -> Reviewers -> Challengers -> Forensic Auditor `teamwork_preview_auditor`).
6. STRICT CONCURRENCY CAP: Maximum 3 active subagents under your sub-orchestrator at any time (so total hierarchy stays under 5).
7. When gate passes (all reviewers APPROVE, challengers APPROVE, auditor CLEAN), write `handoff.md` and report completion back to parent (`34f7436c-be3f-4037-9a01-5d758d8a7573`).
</DISPATCH>
