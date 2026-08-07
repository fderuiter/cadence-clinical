# Investigation Report: Packaging Defect & Foundational Utilities Verification (M1 R2)

**Author**: Explorer 1 (`explorer_m1_r2_1`)  
**Target Milestone**: Milestone M1: Foundational Utilities Migration (Round 2)  
**Working Directory**: `/Users/fred/Code/cadence-clinical/.agents/explorer_m1_r2_1/`  
**Date**: 2026-08-07  

---

## 1. Executive Summary

Milestone M1 successfully relocated all four foundational infrastructure utility components out of `packages/core-models` into core packages (`packages/database`, `packages/security`, `packages/storage`). All legacy files were purged, all downstream import references across `apps/`, `packages/`, `scripts/`, and `tests/` were cleanly updated, and the test suite passes without error.

However, **wheel build verification** fails for `packages-database`, `packages-security`, and `packages-storage` when invoking `uv build --package <pkg>`. The failure is caused by missing `packages = ["."]` declarations under `[tool.hatch.build.targets.wheel]` in `pyproject.toml` files for those packages. Hatchling requires explicit package file selection declarations when Python modules reside directly at the package root level.

This report documents the exact root cause, provides verified evidence for code relocation and downstream imports, and outlines step-by-step instructions for the Worker agent to resolve the packaging defect and verify total compliance.

---

## 2. Packaging Build Failure Analysis

### 2.1 Failure Reproduction & Diagnostic Error Logs

Executing `uv build` for each core package yields the following fatal error from Hatchling:

```
$ export PATH=$HOME/.local/bin:$HOME/.cargo/bin:$PATH
$ uv build --package packages-database

Building source distribution...
Building wheel from source distribution...
Traceback (most recent call last):
  ...
  File ".../site-packages/hatchling/builders/wheel.py", line 281, in default_file_selection_options
    raise ValueError(message)
ValueError: Unable to determine which files to ship inside the wheel using the following heuristics: https://hatch.pypa.io/latest/plugins/builder/wheel/#default-file-selection

The most likely cause of this is that there is no directory that matches the name of your project (packages_database).

At least one file selection option must be defined in the `tool.hatch.build.targets.wheel` table, see: https://hatch.pypa.io/latest/config/build/
```

Identical errors occur when executing:
- `uv build --package packages-security`
- `uv build --package packages-storage`

### 2.2 Mechanism & Root Cause

1. **Package Directory Layout**: The source files for `packages/database`, `packages/security`, and `packages/storage` reside directly in their package root directories:
   - `packages/database/__init__.py`, `audit.py`, `datetime_helpers.py`
   - `packages/security/__init__.py`, `signature.py`, `middleware.py`, etc.
   - `packages/storage/__init__.py`, `document_models.py`, `blob_store.py`, etc.

2. **Hatchling Heuristics Breakdown**:
   - By default, Hatchling expects to find a subdirectory matching the project name (e.g. `packages_database/` inside `packages/database/`).
   - `packages/database/pyproject.toml` currently defines:
     ```toml
     [tool.hatch.build.targets.wheel.sources]
     "" = "packages/database"
     ```
   - `sources` maps relative repository paths, but when Hatchling builds a wheel package standalone within its directory, `sources` alone does not satisfy Hatchling's file selection requirement.

3. **Comparison with Working Package (`packages/core-models`)**:
   - `packages/core-models/pyproject.toml` contains:
     ```toml
     [tool.hatch.build.targets.wheel]
     packages = [
         "sae_icsr",
         "ctms",
         "eligibility",
         ...
     ]
     ```
   - Executing `uv build --package packages-core-models` succeeds immediately:
     `Successfully built dist/packages_core_models-0.1.0-py3-none-any.whl`

### 2.3 Required Fix Configuration

To allow Hatchling to build wheels for root-level package layouts, each `pyproject.toml` must explicitly declare `packages = ["."]` under `[tool.hatch.build.targets.wheel]`:

```toml
[tool.hatch.build.targets.wheel]
packages = ["."]

[tool.hatch.build.targets.wheel.sources]
"" = "packages/database"
```

This configuration explicitly informs Hatchling to include top-level modules and subpackages from the current directory (`.`) into the wheel distribution.

---

## 3. Relocation Verification of Foundational Utility Components

All four foundational utility components have been verified as properly relocated:

| Component | Class / Symbol | Old Location | New Location | Status |
|---|---|---|---|---|
| GxP Audit Mixin | `Part11AuditMixin`, `AuditFields`, `validate_reason_for_change` | `packages/core-models/audit.py` | `packages/database/audit.py` | Verified relocated & old file purged |
| Date/Time Helpers | `AwareDatetime`, `serialize_utc_z`, `validate_timezone_aware_datetime` | `packages/core-models/datetime_helpers.py` | `packages/database/datetime_helpers.py` | Verified relocated & old file purged |
| Signature Models | `SigningReason`, `ApprovalStatus`, `SignatureManifestation` | `packages/core-models/signature.py` | `packages/security/signature.py` | Verified relocated & old file purged |
| Document Storage DTOs | `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse` | `packages/core-models/storage/` | `packages/storage/document_models.py` | Verified relocated & old directory purged |

### File Purge Evidence:
- `test ! -f packages/core-models/audit.py` -> PASS (0 files found)
- `test ! -f packages/core-models/datetime_helpers.py` -> PASS (0 files found)
- `test ! -f packages/core-models/signature.py` -> PASS (0 files found)
- `test ! -d packages/core-models/storage` -> PASS (0 directories found)

---

## 4. Downstream Reference & Script Verification

### 4.1 Downstream Import Audit
Search across `apps/`, `packages/`, `scripts/`, and `tests/` confirmed:
- **0 occurrences** of legacy imports (`from audit import`, `from datetime_helpers import`, `from signature import`, `from storage.document_models import`, or `packages.core_models.audit`).
- **19 downstream files** successfully reference updated first-party module paths:
  - `packages.database.audit`: `apps/econsent/main.py`, `apps/econsent/tests/test_econsent.py`, `apps/execution/tests/test_soa_persistence.py`, `packages/core-models/eligibility/models.py`, `packages/core-models/organization_domain/models.py`, `packages/core-models/protocol_authoring/models.py`, `packages/database/audit.py`.
  - `packages.database.datetime_helpers`: `packages/database/audit.py`, `packages/security/signature.py`, `packages/core-models/protocol_authoring/models.py`, `packages/core-models/protocol_render/models.py`, `packages/core-models/sdtm/models.py`.
  - `packages.security.signature`: `apps/designer/main.py`, `apps/econsent/main.py`, `apps/etmf/ingestion_service.py`, `apps/etmf/main.py`, `apps/etmf/tests/test_etmf_signing_lifecycle.py`, `apps/execution/tests/test_signature_manifestation.py`.
  - `packages.storage.document_models`: `apps/etmf/routers/archive.py`, `apps/execution/routers/documents.py`, `packages/storage/__init__.py`.

### 4.2 Duplication Scanner Script Audit
- File `scripts/detect_duplication.py` (lines 252-253) contains `"packages/database/audit.py"`.
- Execution of `python3 scripts/detect_duplication.py` exited code 0: `[SUCCESS] No duplicate code structures found above the threshold.`

### 4.3 Code Quality & Test Suite Runs
- `uv run ruff check .`: Exited code 0 (`All checks passed!`).
- `uv run ruff format --check .`: Exited code 0 (`681 files already formatted`).
- `uv run pytest packages/database packages/security packages/storage`: Ran 134 tests — 134 passed in 7.02s.

---

## 5. Concrete Action Plan for Worker Agent

The Worker agent must perform the following explicit changes to complete Milestone M1:

1. **Update Build Configurations**:
   - Edit `packages/database/pyproject.toml` to add `[tool.hatch.build.targets.wheel] packages = ["."]`.
   - Edit `packages/security/pyproject.toml` to add `[tool.hatch.build.targets.wheel] packages = ["."]`.
   - Edit `packages/storage/pyproject.toml` to add `[tool.hatch.build.targets.wheel] packages = ["."]`.
   - (Recommended) Update `packages/deid/pyproject.toml` and `packages/hexagonal/pyproject.toml` with the same configuration.

2. **Verify Wheel Builds**:
   ```bash
   export PATH=$HOME/.local/bin:$HOME/.cargo/bin:$PATH
   uv build --package packages-database
   uv build --package packages-security
   uv build --package packages-storage
   ```
   Ensure build output reports `Successfully built dist/...whl` for all three packages.

3. **Execute Full Suite Verification**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   python3 scripts/detect_duplication.py
   uv run pytest -n auto
   ```

4. **Sync GxP Compliance Documentation**:
   ```bash
   uv run python scripts/sync_gxp.py
   ```
