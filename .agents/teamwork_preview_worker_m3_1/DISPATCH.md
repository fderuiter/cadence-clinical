## 2026-08-07T20:42:57Z
You are teamwork_preview_worker_m3_1.
Working directory: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_1/
Parent Conversation ID: sub_orch_m3

Mission: Execute Milestone M3 Implementation (Execution Service Domain Migration).

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Detailed Tasks:
1. Update import statements across `apps/`, `packages/`, `scripts/`, and `tests/` that reference `execution.<module>`, `sdtm.<module>`, `localization.<module>`, `watermark`:
   - Update 34 `from execution.<module> import ...` statements across 31 files to `from apps.execution.src.domain.<module> import ...`.
   - Update legacy imports in `apps/econsent/main.py:9` (`from apps.execution.src.domain.localization.models import validate_language_code`), `apps/econsent/tests/test_econsent_translations.py:7` (`from apps.execution.src.domain.localization.models import validate_language_code`), `apps/execution/routers/documents.py:22` (`from apps.execution.src.domain.watermark import apply_watermark`), `apps/execution/tests/test_sdtm_foundation.py` (`from apps.execution.src.domain.sdtm...`), `apps/execution/tests/test_sdtm_mapper.py` (`from apps.execution.src.domain.sdtm...`).
2. Safely remove legacy execution, sdtm, localization, watermark models and duplicate tests from `packages/core-models/`:
   - Remove `packages/core-models/execution/`
   - Remove `packages/core-models/sdtm/`
   - Remove `packages/core-models/localization/`
   - Remove `packages/core-models/watermark.py`
   - Remove `packages/core-models/tests/` (stale duplicate test files causing import file mismatch)
3. Ensure CDISC Dataset-JSON fields in `apps/execution/src/domain/sdtm/dataset_json_models.py` have `# noqa: N815` directives so ruff N815 checks pass.
4. Formatting & Linting:
   - Run `uv run ruff format .`
   - Run `uv run ruff check . --fix`
   - Verify I001 import sorting and AGENTS.md rules (e.g. E712 `.is_(True)`/`.is_(False)` filters).
5. Duplication Check:
   - Run `python3 scripts/detect_duplication.py`
6. Test Suite Run:
   - Run `uv run pytest -n auto`
7. GxP Compliance Sync:
   - Run `uv run python scripts/sync_gxp.py`
   - Stage and commit updated GxP docs if modified.
8. Write detailed handoff report to `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_worker_m3_1/handoff.md`.
9. Send a message to sub_orch_m3 with your findings, build/test results, and handoff link.
