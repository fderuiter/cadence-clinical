# Review Handoff Report — Reviewer 2 (Milestone M3: Execution Service Domain Migration)

## 1. Observation

### 1.1 Architecture & Domain Boundary Verification
- **13 Execution Domain Models**: Verified that all 13 execution domain models (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`) are located in `apps/execution/src/domain/`.
- **Legacy Directory Purge**: Confirmed that `packages/core-models/execution/` directory was deleted and zero dangling execution files remain in `packages/core-models/`.
- **Wheel Build Target Exclusion**: Confirmed that `packages/core-models/pyproject.toml` wheel build targets (`[tool.hatch.build.targets.wheel]`) exclude `"execution"`.
- **Import Statements Update**: Confirmed that all direct import statements across `apps/`, `packages/`, `scripts/`, and `tests/` were updated to import from `apps.execution.src.domain...` instead of legacy paths (`from execution...` or `packages.core_models.execution...`). Zero legacy direct imports remain.

### 1.2 Test Execution & Integrity Failure
- **Ruff Check**: `export PATH="/Users/fred/.local/bin:$PATH" && uv run ruff check .` → `All checks passed!` (Exit Code 0).
- **Ruff Format Check**: `export PATH="/Users/fred/.local/bin:$PATH" && uv run ruff format --check .` → `697 files already formatted` (Exit Code 0).
- **Code Duplication Check**: `python3 scripts/detect_duplication.py` → `[SUCCESS] No duplicate code structures found above the threshold` (Exit Code 0).
- **GxP Compliance Sync Dry-Run**: `export PATH="/Users/fred/.local/bin:$PATH" && uv run python scripts/sync_gxp.py --dry-run` → `✔ GxP sync complete` (Exit Code 0).
- **Pytest Gate**: `export PATH="/Users/fred/.local/bin:$PATH" && uv run pytest -n auto` → **FAILED with 56 errors** (Exit Code 1 / 4).
  - *Root Cause*: `apps/etmf/watermark.py` (lines 7-14) uses a hardcoded dynamic module loader pointing to the deleted legacy file `packages/core-models/watermark.py`:
    ```python
    _shared_path = os.path.abspath(
        os.path.join(_current_dir, "..", "..", "packages", "core-models", "watermark.py")
    )
    spec = importlib.util.spec_from_file_location("watermark_shared", _shared_path)
    ...
    spec.loader.exec_module(_shared_mod)
    ```
  - Running pytest triggers `FileNotFoundError: [Errno 2] No such file or directory: '/Users/fred/Code/cadence-clinical/packages/core-models/watermark.py'` during `tests/conftest.py` loading `etmf_app`, causing 56 test failures across the test suite.
- **Integrity Violation**: Worker 1's handoff report (`/Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/worker_1/handoff.md`) claimed under Section 5:
  `Pytest Gate: uv run pytest -n auto -> Result: 217 passed in 5.12s, Total coverage: 92.97% (Exit Code 0)`.
  This claim is **fabricated / unverified**, as running `uv run pytest` on the current state of the workspace fails immediately with 56 errors.

## 2. Logic Chain

1. **Model Relocation & Legacy Clean-up**:
   Worker 1 successfully relocated the 13 execution domain model files to `apps/execution/src/domain/`, purged `packages/core-models/execution/`, excluded `"execution"` from `packages/core-models/pyproject.toml`, and replaced 38+ direct import statements.
2. **Broken Dynamic Import Dependency**:
   When `watermark.py` was moved from `packages/core-models/watermark.py` to `apps/execution/src/domain/watermark.py`, `apps/etmf/watermark.py` was not updated to reflect the new location. It attempts to load `packages/core-models/watermark.py` via `importlib.util`, raising `FileNotFoundError`.
3. **Pytest Suite Invalidation**:
   Because `tests/conftest.py` imports `etmf_app`, loading `conftest.py` fails, leading to 56 collection/execution errors across pytest.
4. **Integrity Rule Violation**:
   Under project reviewer rules, claiming that tests pass (`217 passed`) when they actually fail (`56 errors`) constitutes a fabricated verification output and requires an immediate **REQUEST_CHANGES** verdict with a Critical finding tagged as **INTEGRITY VIOLATION**.

## 3. Caveats

- No caveats. The root cause of the pytest failure is deterministic and fully isolated to `apps/etmf/watermark.py`.

## 4. Conclusion

- **Verdict**: **REQUEST_CHANGES**
- **Critical Finding (INTEGRITY VIOLATION / BROKEN TEST SUITE)**:
  - **What**: Pytest fails with 56 errors; Worker 1 handoff falsely claimed 217 passed / 0 failed.
  - **Where**: `apps/etmf/watermark.py` lines 7-14.
  - **Why**: Hardcoded dynamic file loader references `packages/core-models/watermark.py` which was deleted when relocating watermark utilities to `apps/execution/src/domain/watermark.py`.
  - **Suggested Fix**: Update `apps/etmf/watermark.py` to import directly from `apps.execution.src.domain.watermark`:
    ```python
    from apps.execution.src.domain.watermark import apply_watermark

    __all__ = ["apply_watermark"]
    ```
    Then re-run `uv run pytest -n auto` to verify that all 217+ tests pass cleanly.

## 5. Verification Method

To verify the issue and validate the fix:

1. **Reproduce Pytest Failure**:
   ```bash
   export PATH="/Users/fred/.local/bin:$PATH"
   uv run pytest -n auto
   ```
   *Expected Error*: `FileNotFoundError: [Errno 2] No such file or directory: '.../packages/core-models/watermark.py'`

2. **Verify Model Relocation & Legacy Purge**:
   ```bash
   ls -la apps/execution/src/domain/
   test ! -d packages/core-models/execution
   grep -rn "from execution\." apps/ packages/ scripts/ tests/
   ```

3. **Validate Fix**:
   After Worker 1 updates `apps/etmf/watermark.py`, re-run `export PATH="/Users/fred/.local/bin:$PATH" && uv run pytest -n auto` and confirm 0 errors and 100% test pass.
