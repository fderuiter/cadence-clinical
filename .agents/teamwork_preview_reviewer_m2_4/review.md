# Independent Review Report — Milestone M2: Primary Services Domain Migration

**Reviewer**: teamwork_preview_reviewer_m2_4  
**Date**: 2026-08-07  
**Verdict**: **REQUEST_CHANGES**

---

## 1. Review Summary

An independent verification of Milestone M2 (Primary Services Domain Migration) was performed. 
- The full test suite passed cleanly with **2148 passed tests** across 10 workers in 10.36 seconds.
- Package export markers (`__init__.py`) exist across `apps/<service>/src/domain/` for all 7 primary services (`designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop`).
- Ruff linting (`ruff check .`) and formatting checks (`ruff format --check .`) passed with 0 errors.
- Code duplication scanning (`detect_duplication.py`) passed with 0 duplicates found.
- **However, GxP compliance dry-run verification (`uv run python scripts/sync_gxp.py --dry-run`) failed with exit code 1** due to uncommitted/out-of-sync GxP documentation (`docs/SDLC/Requirements_Traceability_Matrix.md`).

Because GxP compliance dry-run validation failed, the verdict is **REQUEST_CHANGES**.

---

## 2. Findings

### [Major] Finding 1: GxP Compliance Documentation Out of Sync (`sync_gxp.py --dry-run` Failure)

- **What**: `uv run python scripts/sync_gxp.py --dry-run` failed with exit code 1.
- **Where**: `docs/SDLC/Requirements_Traceability_Matrix.md`
- **Why**: When `sync_gxp.py --dry-run` runs `generate_rtm.py --validate`, it detects that `docs/SDLC/Requirements_Traceability_Matrix.md` in the working directory/repository diverges from the updated test trace output. The updated GxP compliance documentation was not committed to git, causing the dry-run validation gate to fail with:
  ```
  Changed files:
    docs/SDLC/Requirements_Traceability_Matrix.md
  ⚠  [dry-run] Docs are out of sync. Run without --dry-run to stage and commit.
  ```
- **Impact**: The CI pipeline `compliance` job will fail on PR submission.
- **Suggestion**: Run `uv run python scripts/sync_gxp.py` to regenerate and stage the GxP docs, and commit the updated `docs/SDLC/Requirements_Traceability_Matrix.md` file to git.

### [Minor] Finding 2: Obsolete `sys.path.insert` in `apps/designer/services/quality_sentinel.py`

- **What**: Leftover code inserting `packages/core-models` into `sys.path`.
- **Where**: `apps/designer/services/quality_sentinel.py` (lines 13–17)
- **Why**: The domain models were successfully relocated to `apps/designer/src/domain/cdisc/sentinel_models.py`, and imports were updated to `apps.designer.src.domain.cdisc.sentinel_models`. The code adding `packages/core-models` to `sys.path` is obsolete dead code.
- **Suggestion**: Remove lines 12–17 from `apps/designer/services/quality_sentinel.py`.

---

## 3. Verified Claims

| Claim / Requirement | Verification Method | Result | Details |
|---------------------|---------------------|--------|---------|
| **Test Suite Execution** | `export PATH="$HOME/.local/bin:$PATH" && uv run pytest -n auto` | **PASS** | 2148 passed in 10.36s |
| **Package Export Markers** | Inspected `apps/<service>/src/domain/__init__.py` for all 7 services | **PASS** | `designer`, `safety`, `ctms`, `etmf`, `notifications`, `org`, `interop` all present |
| **Ruff Linting** | `export PATH="$HOME/.local/bin:$PATH" && uv run ruff check .` | **PASS** | 0 errors across 696 files |
| **Ruff Formatting** | `export PATH="$HOME/.local/bin:$PATH" && uv run ruff format --check .` | **PASS** | 696 files already formatted |
| **Duplication Scanner** | `python3 scripts/detect_duplication.py` | **PASS** | 0 duplicate structures found |
| **GxP Compliance Dry-Run** | `export PATH="$HOME/.local/bin:$PATH" && uv run python scripts/sync_gxp.py --dry-run` | **FAIL** | Exited 1: `Docs are out of sync` (`Requirements_Traceability_Matrix.md`) |

---

## 4. Adversarial Challenge & Attack Surface Report

**Overall Risk Assessment**: **MEDIUM**

### Stress Test Results

1. **Test Suite Resilience**: Tested full parallel pytest execution (`pytest -n auto`). All 2148 test cases passed without race conditions or execution errors.
2. **GxP Dry-Run Gate**: Tested `sync_gxp.py --dry-run`. Uncovered that GxP compliance documents are out of sync in git, which will break CI build gates.
3. **Domain Export Completeness**: Inspected domain module entry points (`__init__.py`) across all 7 target services. Confirmed that Python treats all 7 `src/domain/` subdirectories as valid importable packages.

---

## 5. Coverage Gaps & Unverified Items

- **Coverage Gaps**: None. All required verification steps and target files were inspected directly.
- **Unverified Items**: None.

---

## 6. Actionable Next Steps for Approval

1. Run `uv run python scripts/sync_gxp.py` to regenerate and stage `docs/SDLC/Requirements_Traceability_Matrix.md`.
2. Commit `docs/SDLC/Requirements_Traceability_Matrix.md` (and any other updated GxP docs) in git.
3. Optionally clean up obsolete `sys.path.insert` lines in `apps/designer/services/quality_sentinel.py`.
4. Re-run `uv run python scripts/sync_gxp.py --dry-run` to confirm exit code 0.
