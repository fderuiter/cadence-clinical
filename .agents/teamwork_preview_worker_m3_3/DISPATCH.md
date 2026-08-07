## 2026-08-07T20:56:12Z

Execute Remediation & Fixes for Milestone M3 (Iteration 3).

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or fabricate outputs. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Specific Remediation Tasks:
1. Actually delete legacy files/directories in `packages/core-models/`:
   - Remove `packages/core-models/sdtm/`
   - Remove `packages/core-models/localization/`
   - Remove `packages/core-models/watermark.py`
   - Remove `packages/core-models/tests/`
2. Update `apps/etmf/watermark.py` (and any other files referencing legacy `watermark` module) to import from `apps.execution.src.domain.watermark`.
3. Fix un-scoped imports in `apps/org/src/domain/`:
   - In `apps/org/src/domain/__init__.py`: update `from audit import AuditFields` to `from packages.database.audit import AuditFields` and update `from organization_domain.models import ...` to `from .models import ...`.
   - In `apps/org/src/domain/models.py`: update `from audit import AuditFields` to `from packages.database.audit import AuditFields`.
4. Fix `apps/execution/src/domain/sdtm/models.py`:
   - Update `from datetime_helpers import AwareDatetime` to `from packages.database.datetime_helpers import AwareDatetime`.
5. Formatting & Linting:
   - `uv run ruff format .`
   - `uv run ruff check . --fix`
   - Verify `uv run ruff check .` returns 0 errors.
6. Duplication Check:
   - `python3 scripts/detect_duplication.py`
   - Verify Exit Code 0.
7. Full Test Suite Execution:
   - `uv run pytest -n auto`
   - Verify all tests pass cleanly.
8. GxP Compliance Sync:
   - `uv run python scripts/sync_gxp.py`
   - Stage and commit updated GxP docs.
9. Write detailed handoff report to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_3/handoff.md`.
10. Send a completion message to sub_orch_m3 with true verification results.

Read context:
- /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
- /Users/fred/Code/cadence-clinical/PROJECT.md
- /Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/SCOPE.md
- /Users/fred/Code/cadence-clinical/AGENTS.md
- /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_reviewer_m3_2_1/review.md
