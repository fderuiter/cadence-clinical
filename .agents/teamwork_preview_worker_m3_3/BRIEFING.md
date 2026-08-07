# BRIEFING — 2026-08-07T20:56:14Z

## Mission
Execute Remediation & Fixes for Milestone M3 (Iteration 3): delete remaining legacy files in `packages/core-models/`, update legacy imports across the codebase, fix un-scoped imports in org and execution domains, run format/linting/duplication checks, run test suite, run GxP sync, write handoff, and inform parent sub_orch_m3.

## 🔒 My Identity
- Archetype: implementer
- Roles: implementer, qa, specialist
- Working directory: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_3/
- Original parent: sub_orch_m3
- Milestone: M3

## 🔒 Key Constraints
- Minimal changes
- No hardcoded test results / facade implementations
- Strict compliance with AGENTS.md, ruff format/check, detect_duplication.py, pytest, and sync_gxp.py

## Current Parent
- Conversation ID: sub_orch_m3
- Updated: 2026-08-07T20:56:14Z

## Task Summary
- **What to build**: Remediation of legacy package removal and broken import references, code formatting/linting, test execution, GxP sync.
- **Success criteria**:
  1. Removed `packages/core-models/sdtm/`, `packages/core-models/localization/`, `packages/core-models/watermark.py`, `packages/core-models/tests/`.
  2. `apps/etmf/watermark.py` and any other references updated to `apps.execution.src.domain.watermark`.
  3. `apps/org/src/domain/__init__.py` and `models.py` imports fixed to `packages.database.audit.AuditFields` and relative imports.
  4. `apps/execution/src/domain/sdtm/models.py` updated to `packages.database.datetime_helpers.AwareDatetime`.
  5. `uv run ruff format .` and `uv run ruff check .` exit 0.
  6. `python3 scripts/detect_duplication.py` exits 0.
  7. `uv run pytest -n auto` passes 100%.
  8. `uv run python scripts/sync_gxp.py` stages/commits GxP docs.
  9. Handoff report in `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_3/handoff.md`.
  10. Completion message sent to `sub_orch_m3`.

## Change Tracker
- **Files modified**: None yet
- **Build status**: Untested
- **Pending issues**: None

## Quality Status
- **Build/test result**: TBD
- **Lint status**: TBD
- **Tests added/modified**: TBD

## Loaded Skills
- None loaded explicitly

## Artifact Index
- `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_3/DISPATCH.md` — Dispatch requirements
- `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_3/BRIEFING.md` — Briefing document
