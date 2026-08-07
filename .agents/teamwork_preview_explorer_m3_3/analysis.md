# Technical Analysis: Milestone M3 Execution Service Domain Migration

## Executive Summary
Milestone M3 requires completing the migration of Execution Service domain models (including `execution/`, `sdtm/`, `localization/`, and `watermark.py`) to `apps/execution/src/domain/`, purging legacy models and tests from `packages/core-models/`, fixing 8 legacy import statements across 5 files, resolving ruff lint/format errors, and verifying test suite and GxP compliance.

All 24 domain modules under `apps.execution.src.domain` have already been staged and import with **zero circular dependencies**.

---

## 1. Initial Baseline Checks Results

### 1.1 `uv run ruff check .`
- **Status**: FAILED (Exit Code 1)
- **Error Count**: 7 errors
- **Details**:
  1. `apps/execution/src/domain/sdtm/dataset_json_models.py:32:5`: N815 Variable `itemData` in class scope should not be mixedCase
  2. `apps/execution/src/domain/sdtm/dataset_json_models.py:41:5`: N815 Variable `creationDateTime` in class scope should not be mixedCase
  3. `apps/execution/src/domain/sdtm/dataset_json_models.py:42:5`: N815 Variable `datasetJSONVersion` in class scope should not be mixedCase
  4. `apps/execution/src/domain/sdtm/dataset_json_models.py:45:5`: N815 Variable `fileOID` in class scope should not be mixedCase
  5. `apps/execution/src/domain/sdtm/dataset_json_models.py:48:5`: N815 Variable `clinicalData` in class scope should not be mixedCase
  6. `apps/execution/src/domain/sdtm/models.py:10:1`: I001 [*] Import block is un-sorted or un-formatted
  7. `apps/execution/src/domain/sdtm/sdtm_models.py:8:1`: I001 [*] Import block is un-sorted or un-formatted

### 1.2 `uv run ruff format --check .`
- **Status**: FAILED (Exit Code 1)
- **Details**: 1 file needs reformatting: `apps/execution/domain/__init__.py`

### 1.3 `python3 scripts/detect_duplication.py`
- **Status**: FAILED (Exit Code 1)
- **Details**: 15+ line duplicate blocks detected between `apps/execution/src/domain/sdtm/dataset_json_models.py` and `packages/core-models/sdtm/dataset_json_models.py` (and related sdtm files) because models exist in both locations.

### 1.4 `uv run pytest -n auto`
- **Status**: FAILED (2148 passed, 689 warnings, 22 errors)
- **Details**: The 22 errors are all `import file mismatch` in `packages/core-models/tests/` (e.g. `test_adae.py`, `test_adsl.py`, `test_sdtm_foundation.py`, `test_usdm_serialization.py`) due to duplicate test module names colliding with relocated tests in `apps/designer/tests/` and `apps/execution/tests/`.

### 1.5 `uv run python scripts/sync_gxp.py --dry-run`
- **Status**: FAILED (Exit Code 1, expected in dry-run mode when RTM docs diverge)
- **Details**: `⚠ [dry-run] Docs are out of sync. Run without --dry-run to stage and commit. Changed files: docs/SDLC/Requirements_Traceability_Matrix.md`.

---

## 2. Technical Investigation & Edge Cases

### 2.1 Circular Import Analysis
An import test across all 24 execution domain modules was executed via Python 3.14 (`uv run python`):
```python
domain_modules = [
    "apps.execution.src.domain.doa_models",
    "apps.execution.src.domain.econsent_models",
    "apps.execution.src.domain.eisf_models",
    "apps.execution.src.domain.epro_transport_models",
    "apps.execution.src.domain.exceptions",
    "apps.execution.src.domain.lab_models",
    "apps.execution.src.domain.lab_transport_models",
    "apps.execution.src.domain.localization.models",
    "apps.execution.src.domain.lock_models",
    "apps.execution.src.domain.lock_transport_models",
    "apps.execution.src.domain.models",
    "apps.execution.src.domain.offline_models",
    "apps.execution.src.domain.repositories",
    "apps.execution.src.domain.safety_models",
    "apps.execution.src.domain.safety_transport_models",
    "apps.execution.src.domain.sdtm.dataset_json_models",
    "apps.execution.src.domain.sdtm.enums",
    "apps.execution.src.domain.sdtm.models",
    "apps.execution.src.domain.sdtm.scrubber_models",
    "apps.execution.src.domain.sdtm.sdtm_models",
    "apps.execution.src.domain.sdtm.terminology",
    "apps.execution.src.domain.sdv_transport_models",
    "apps.execution.src.domain.signature_transport_models",
    "apps.execution.src.domain.watermark",
]
```
**Result**: 24 out of 24 modules imported successfully with **0 circular import errors**.

### 2.2 Legacy Import AST Scan
An AST scan of the entire repository outside `packages/core-models` identified exactly **8 legacy import statements across 5 files**:

1. `apps/econsent/main.py:9`
   - Current: `from localization import validate_language_code`
   - Replace with: `from apps.execution.src.domain.localization.models import validate_language_code`
2. `apps/econsent/tests/test_econsent_translations.py:7`
   - Current: `from localization import validate_language_code`
   - Replace with: `from apps.execution.src.domain.localization.models import validate_language_code`
3. `apps/execution/routers/documents.py:22`
   - Current: `from watermark import apply_watermark`
   - Replace with: `from apps.execution.src.domain.watermark import apply_watermark`
4. `apps/execution/tests/test_sdtm_foundation.py:5, 15, 30`
   - Current: `from sdtm.enums import ...`, `from sdtm.models import ...`, `from sdtm.terminology import ...`
   - Replace with: `from apps.execution.src.domain.sdtm.enums import ...`, `from apps.execution.src.domain.sdtm.models import ...`, `from apps.execution.src.domain.sdtm.terminology import ...`
5. `apps/execution/tests/test_sdtm_mapper.py:10, 18`
   - Current: `from sdtm.enums import ...`, `from sdtm.sdtm_models import ...`
   - Replace with: `from apps.execution.src.domain.sdtm.enums import ...`, `from apps.execution.src.domain.sdtm.sdtm_models import ...`

### 2.3 AGENTS.md Rules Verification
- **I001 (Import Sorting)**: All updated import statements must be alphabetically sorted inside group 3 (first-party imports).
- **N815 (Pydantic Field Names)**: `apps/execution/src/domain/sdtm/dataset_json_models.py` uses CDISC Dataset-JSON 1.0 standard camelCase fields (`itemData`, `creationDateTime`, `datasetJSONVersion`, `fileOID`, `clinicalData`). Add `# noqa: N815` or add per-file ignore in `pyproject.toml`.
- **E712 (SQLAlchemy Boolean Filters)**: Grep search across `apps/execution` confirmed **0 E712 violations**.

---

## 3. Concrete Step-by-Step Implementation Strategy for Worker

1. **Fix 8 Legacy Import Statements Across 5 Files**:
   - Update `apps/econsent/main.py`, `apps/econsent/tests/test_econsent_translations.py`, `apps/execution/routers/documents.py`, `apps/execution/tests/test_sdtm_foundation.py`, and `apps/execution/tests/test_sdtm_mapper.py` to use explicit `apps.execution.src.domain...` imports.
2. **Purge Relocated Models & Stale Tests in `packages/core-models/`**:
   - Remove `packages/core-models/execution/`
   - Remove `packages/core-models/sdtm/`
   - Remove `packages/core-models/localization/`
   - Remove `packages/core-models/watermark.py`
   - Remove `packages/core-models/tests/`
3. **Resolve Ruff Linting & Formatting Errors**:
   - Add `# noqa: N815` or per-file ignore for Dataset-JSON fields in `apps/execution/src/domain/sdtm/dataset_json_models.py`.
   - Run `uv run ruff format .` (fixes `apps/execution/domain/__init__.py`).
   - Run `uv run ruff check . --fix`.
4. **Execute Baseline Checks & GxP Sync**:
   - `python3 scripts/detect_duplication.py` (Must pass code duplication check).
   - `uv run ruff check .` (Must pass cleanly).
   - `uv run ruff format --check .` (Must pass cleanly).
   - `uv run pytest -n auto` (Must pass 100% clean).
   - `uv run python scripts/sync_gxp.py` (Regenerates and stages GxP RTM docs).
