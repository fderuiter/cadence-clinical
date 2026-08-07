## 2026-08-07T15:40:11Z

Perform the complete Execution Service Domain Migration:
1. Relocate all 13 domain models from `packages/core-models/execution/` into `apps/execution/src/domain/`:
   - `doa_models.py`
   - `econsent_models.py`
   - `eisf_models.py`
   - `epro_transport_models.py`
   - `lab_models.py`
   - `lab_transport_models.py`
   - `lock_models.py`
   - `lock_transport_models.py`
   - `offline_models.py`
   - `safety_models.py`
   - `safety_transport_models.py`
   - `sdv_transport_models.py`
   - `signature_transport_models.py`
2. Update internal imports inside `apps/execution/src/domain/lab_transport_models.py` and `apps/execution/src/domain/lock_transport_models.py` from `from execution...` to `from apps.execution.src.domain...`.
3. Completely delete the `packages/core-models/execution/` directory and all 13 legacy `.py` files inside it.
4. Update `packages/core-models/pyproject.toml`: remove `"execution"` from `[tool.hatch.build.targets.wheel]` `packages`, leaving `packages = ["localization", "sdtm"]`.
5. Update all 38 import statements across the 33 cataloged files in `apps/`, `packages/`, `scripts/`, and `tests/` from `from execution.<module>` to `from apps.execution.src.domain.<module>`.
6. Follow AGENTS.md rules strictly:
   - Import ordering I001: Run `uv run ruff check . --fix` and `uv run ruff format .`
   - Ensure no bare boolean equality in SQLAlchemy ORM queries (`.is_(True)` / `.is_(False)`).
7. Run the verification suite:
   - `uv run ruff check .`
   - `uv run ruff format --check .`
   - `python3 scripts/detect_duplication.py`
   - `uv run pytest -n auto`
   - `uv run python scripts/sync_gxp.py --dry-run`
8. Write a comprehensive report in `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/worker_1/handoff.md`.
9. Send a completion message back to parent.
