# Original User Request

## Initial Request — 2026-08-07T15:38:50-05:00

You are Sub-Orchestrator M3 for Milestone M3: Execution Service Domain Migration.
Working directory: /Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/
Project root: /Users/fred/Code/cadence-clinical
Parent conversation ID: 46b1fc70-68c3-410e-ba5c-2336ebb72fb2

Mission:
Execute Milestone M3 (Execution Service Domain Migration).
Scope:
1. Relocate all domain models from `packages/core-models/execution/` (including offline models, ePRO, safety, SDTM, trial lock) into `apps/execution/src/domain/`.
2. Update all import paths across `apps/`, `packages/`, `scripts/`, and `tests/` to import execution domain models from `apps.execution.src.domain...` instead of `packages.core_models.execution...`.
3. Ensure no dangling imports or sys.path hacks remain.
4. Run the full iteration gate cycle (Explorer -> Worker -> Reviewer -> Challenger -> Forensic Auditor):
   - `uv run ruff check .` and `uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
   - `uv run python scripts/sync_gxp.py --dry-run`
5. Update /Users/fred/Code/cadence-clinical/PROJECT.md setting Milestone M3 Status to DONE.
6. Write handoff.md in /Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/ and send a completion message to parent (46b1fc70-68c3-410e-ba5c-2336ebb72fb2).

Constraints:
- CONCURRENCY CAP: Maximum 3 active subagents concurrently under sub_orch_m3 (total across hierarchy <= 5).
- Do NOT write or edit source code directly; use Workers.
- Follow AGENTS.md rules strictly (import ordering I001, SQLAlchemy is_ boolean filters E712, GxP sync).
