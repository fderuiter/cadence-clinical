## 2026-08-07T20:34:42Z

Task:
Remediate the Forensic Auditor's INTEGRITY VIOLATION report (/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_auditor_m2_1/audit.md) by completely eradicating `packages/core-models` from disk:

1. Relocate all remaining core models from `packages/core-models/` to `apps/execution/src/domain/`:
   - `packages/core-models/execution/*` -> `apps/execution/src/domain/`
   - `packages/core-models/sdtm/*` -> `apps/execution/src/domain/sdtm/`
   - `packages/core-models/localization/*` -> `apps/execution/src/domain/localization/`
   - `packages/core-models/watermark.py` -> `apps/execution/src/domain/watermark.py`
   - Relocate or integrate any tests from `packages/core-models/tests/` into `apps/execution/tests/`.

2. Update all import sites across `apps/`, `packages/`, `scripts/`, `tests/` that reference `packages.core_models`, `core_models`, or `sdtm` to use service domain paths (`apps.execution.src.domain...`).

3. Delete the `packages/core-models` directory completely from disk (`rm -rf packages/core-models`).

4. Clean configuration & environment references:
   - Remove `packages-core-models` from `pyproject.toml` workspace sources/dependencies.
   - Remove `_core_models_path` and `sys.path.insert` from `packages/__init__.py`.

5. Run GxP sync and verify all quality gates:
   - `export PATH="$HOME/.local/bin:$PATH" && uv run python scripts/sync_gxp.py`
   - `export PATH="$HOME/.local/bin:$PATH" && uv run ruff check .`
   - `export PATH="$HOME/.local/bin:$PATH" && uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
   - `export PATH="$HOME/.local/bin:$PATH" && uv run pytest -n auto`
   - `export PATH="$HOME/.local/bin:$PATH" && uv run python scripts/sync_gxp.py --dry-run`

Original request path: /Users/fred/Code/cadence-clinical/.agents/ORIGINAL_REQUEST.md
Audit report path: /Users/fred/Code/cadence-clinical/.agents/teamwork_preview_auditor_m2_1/audit.md

Document your changes and verification outcomes in `handoff.md` in your working directory and send a completion message when done.
