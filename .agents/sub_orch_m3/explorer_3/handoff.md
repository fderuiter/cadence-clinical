# Handoff Report: Explorer 3 — Project Configuration, sys.path & Cleanup Strategy for M3

## 1. Observation

### 1.1 `sys.path` Modifications, `PYTHONPATH` References & Dynamic Imports
Direct examination of Python package entry points and configuration files revealed the following `sys.path` and `PYTHONPATH` mechanisms:

1. **Root Package `sys.path` Injection (`packages/__init__.py`)**:
   - File: `packages/__init__.py:6-10`
   ```python
   _core_models_path = os.path.abspath(
       os.path.join(os.path.dirname(__file__), "core-models")
   )
   if _core_models_path not in sys.path:
       sys.path.insert(0, _core_models_path)
   ```
   *Impact*: Importing `packages` injects `packages/core-models` directly into `sys.path`. This allowed files to import `execution` domain models directly via `from execution.<module> import ...` (e.g. `from execution.doa_models import ...`).

2. **Template Regeneration Bootstrap (`scripts/regenerate_templates.py`)**:
   - File: `scripts/regenerate_templates.py:13-16`
   ```python
   sys.path.insert(
       0,
       os.path.join(os.path.dirname(os.path.dirname(__file__)), "packages", "core-models"),
   )
   ```
   *Impact*: Utility script injects `packages/core-models` onto `sys.path` for template building.

3. **Application Root Test Fixtures (`apps/conftest.py`)**:
   - File: `apps/conftest.py:4-7`
   ```python
   if repo_root not in sys.path:
       sys.path.insert(0, repo_root)
   ```

4. **Side-effect Package Imports (`import packages`)**:
   - Multiple files (e.g. `apps/execution/tests/test_cdisc_library_client.py:9`, `apps/ctms/tests/test_doa_models.py:12`) contain `import packages  # noqa: F401` specifically to trigger `packages/__init__.py`'s `sys.path` injection.

5. **Dynamic Imports & `PYTHONPATH` Usage**:
   - No dynamic imports (`importlib`, `__import__`, `exec`) targeting `packages/core-models/execution` were found in active source code.
   - `PYTHONPATH` references exist in documentation (`docs/OPERATIONAL_INBOUND_EMAIL.md:160`), UI test runner (`packages/ui/tests/signing.test.js:187`), and script comments (`scripts/regenerate_templates.py:12`).

---

### 1.2 Configuration Files Audit

1. **Root `pyproject.toml`**:
   - File: `pyproject.toml:22-42`
   - Configured with `[tool.uv.sources]` mapping `packages-core-models` and `apps-execution` as workspace packages.
   - Includes `apps` and `packages` in coverage and testpaths.
   - Requires no changes for M3, as `packages-core-models` remains in workspace until M5.

2. **`packages/core-models/pyproject.toml`**:
   - File: `packages/core-models/pyproject.toml:22-27`
   ```toml
   [tool.hatch.build.targets.wheel]
   packages = [
       "execution",
       "localization",
       "sdtm",
   ]
   ```
   *Action required for M3*: `"execution"` must be removed from `packages = [...]`, leaving `["localization", "sdtm"]`.

3. **`apps/execution/pyproject.toml`**:
   - File: `apps/execution/pyproject.toml:27-32`
   ```toml
   dependencies = [
       "packages-database",
       "packages-security",
       "packages-deid",
       "packages-core-models",
       "packages-storage",
   ]
   ```
   - Retains `"packages-core-models"` dependency for now (until M5) because `sdtm` or other shared assets still reside in `packages/core-models`.

---

### 1.3 File Inventory in `packages/core-models/execution/` and Target Paths

1. **Files in `packages/core-models/execution/` (13 domain files)**:
   - `doa_models.py` (2,025 bytes)
   - `econsent_models.py` (1,381 bytes)
   - `eisf_models.py` (1,976 bytes)
   - `epro_transport_models.py` (2,634 bytes)
   - `lab_models.py` (3,196 bytes)
   - `lab_transport_models.py` (10,523 bytes)
   - `lock_models.py` (2,280 bytes)
   - `lock_transport_models.py` (1,560 bytes)
   - `offline_models.py` (14,185 bytes)
   - `safety_models.py` (2,058 bytes)
   - `safety_transport_models.py` (1,967 bytes)
   - `sdv_transport_models.py` (10,033 bytes)
   - `signature_transport_models.py` (2,341 bytes)
   - Note: There is NO `__init__.py` inside `packages/core-models/execution/`.

2. **Target Relocation Location (`apps/execution/src/domain/`)**:
   - Destination directory: `apps/execution/src/domain/`
   - Files are copied/relocated to `apps/execution/src/domain/<module>.py`.
   - **Internal Cross-Model Imports To Fix in `apps/execution/src/domain/`**:
     - `apps/execution/src/domain/lab_transport_models.py:8`: `from execution.lab_models import (...)` -> `from apps.execution.src.domain.lab_models import (...)`
     - `apps/execution/src/domain/lock_transport_models.py:6`: `from execution.lock_models import DataLockRecord, LockScopeEnum` -> `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum`

3. **Cleanup Strategy for `packages/core-models/execution/`**:
   - Delete all 13 `.py` files and the `packages/core-models/execution/` directory (along with `__pycache__`).
   - Per mandate, DO NOT leave re-export wrappers in `packages/core-models/execution/`; relocate all domain models cleanly and update all import statements across the project.

---

### 1.4 Comprehensive Audit of Import Paths to Update (33 Files)

Every file across `apps/`, `packages/`, `scripts/`, and `tests/` currently importing from `execution.<module>` has been cataloged:

| # | File Path | Line # | Existing Import Statement | Target Updated Import Statement |
|---|-----------|--------|---------------------------|----------------------------------|
| 1 | `apps/ctms/tests/test_doa_audit_suite.py` | 6 | `from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` | `from apps.execution.src.domain.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` |
| 2 | `apps/ctms/tests/test_doa_models.py` | 6 | `from execution.doa_models import (...)` | `from apps.execution.src.domain.doa_models import (...)` |
| 3 | `apps/designer/tests/test_granular_locking.py` | 6 | `from execution.lock_models import DataLockRecord, LockStatusEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockStatusEnum` |
| 4 | `apps/designer/tests/test_lock_enforcement.py` | 9 | `from execution.lock_models import (...)` | `from apps.execution.src.domain.lock_models import (...)` |
| 5 | `apps/designer/tests/test_lock_models.py` | 8 | `from execution.lock_models import (...)` | `from apps.execution.src.domain.lock_models import (...)` |
| 6 | `apps/econsent/tests/test_econsent_service.py` | 5 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |
| 7 | `apps/eisf/tests/test_eisf_models.py` | 8 | `from execution.eisf_models import (...)` | `from apps.execution.src.domain.eisf_models import (...)` |
| 8 | `apps/eisf/tests/test_eisf_service.py` | 8 | `from execution.eisf_models import EISFTaxonomyCategoryEnum` | `from apps.execution.src.domain.eisf_models import EISFTaxonomyCategoryEnum` |
| 9 | `apps/execution/exporters/e2b_xml_builder.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| 10 | `apps/execution/routers/doa.py` | 8 | `from execution.doa_models import (...)` | `from apps.execution.src.domain.doa_models import (...)` |
| 11 | `apps/execution/routers/eisf.py` | 6 | `from execution.eisf_models import (...)` | `from apps.execution.src.domain.eisf_models import (...)` |
| 12 | `apps/execution/routers/locks.py` | 9, 13 | `from execution.lock_models import (...)`<br>`from execution.lock_transport_models import (...)` | `from apps.execution.src.domain.lock_models import (...)`<br>`from apps.execution.src.domain.lock_transport_models import (...)` |
| 13 | `apps/execution/routers/offline.py` | 10 | `from execution.offline_models import (...)` | `from apps.execution.src.domain.offline_models import (...)` |
| 14 | `apps/execution/routers/safety.py` | 10 | `from execution.safety_transport_models import (...)` | `from apps.execution.src.domain.safety_transport_models import (...)` |
| 15 | `apps/execution/routers/sdv.py` | 10 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 16 | `apps/execution/routers/signatures.py` | 9 | `from execution.signature_transport_models import (...)` | `from apps.execution.src.domain.signature_transport_models import (...)` |
| 17 | `apps/execution/services/doa_service.py` | 9 | `from execution.doa_models import (...)` | `from apps.execution.src.domain.doa_models import (...)` |
| 18 | `apps/execution/services/e2b_parser.py` | 10 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |
| 19 | `apps/execution/services/econsent_capture_service.py` | 6 | `from execution.econsent_models import (...)` | `from apps.execution.src.domain.econsent_models import (...)` |
| 20 | `apps/execution/services/eisf_service.py` | 10 | `from execution.eisf_models import (...)` | `from apps.execution.src.domain.eisf_models import (...)` |
| 21 | `apps/execution/services/lock_enforcement.py` | 8 | `from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum` |
| 22 | `apps/execution/services/sae_reconciler.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| 23 | `apps/execution/src/domain/lab_transport_models.py` | 8 | `from execution.lab_models import (...)` | `from apps.execution.src.domain.lab_models import (...)` |
| 24 | `apps/execution/src/domain/lock_transport_models.py` | 6 | `from execution.lock_models import DataLockRecord, LockScopeEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum` |
| 25 | `apps/execution/tests/test_lab_schemas.py` | 9, 14 | `from execution.lab_models import (...)`<br>`from execution.lab_transport_models import (...)` | `from apps.execution.src.domain.lab_models import (...)`<br>`from apps.execution.src.domain.lab_transport_models import (...)` |
| 26 | `apps/execution/tests/test_sdv_item_level_rbac.py` | 7 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 27 | `apps/execution/tests/test_tsdv.py` | 712 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 28 | `apps/gateway/routers/ecoa.py` | 12, 19 | `from execution.epro_transport_models import (...)`<br>`from execution.offline_models import (...)` | `from apps.execution.src.domain.epro_transport_models import (...)`<br>`from apps.execution.src.domain.offline_models import (...)` |
| 29 | `apps/interop/main.py` | 8 | `from execution.epro_transport_models import (...)` | `from apps.execution.src.domain.epro_transport_models import (...)` |
| 30 | `apps/safety/tests/test_e2b_parser.py` | 6 | `from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum` | `from apps.execution.src.domain.safety_models import CausalityEnum, SeriousnessCriteriaEnum` |
| 31 | `apps/safety/tests/test_sae_reconciler.py` | 8 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |
| 32 | `apps/safety/tests/test_safety_gateway.py` | 15 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |
| 33 | `tests/validation/prd_compliance_traceability_suite.py` | 9 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |

---

## 2. Logic Chain

1. **`sys.path` Injection Mechanism**:
   `packages/__init__.py` injects `packages/core-models` into Python's `sys.path`. When code executed `from execution.<module> import ...`, Python resolved `execution` relative to `packages/core-models/execution/`.
2. **Relocation & Ownership**:
   Under PROJECT.md's domain ownership rules, microservices strictly own their domain models under `apps/<service>/src/domain/`. Execution service domain models (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`) must reside exclusively under `apps/execution/src/domain/`.
3. **Directory Cleanup & Build Target Update**:
   - `packages/core-models/execution/` must be deleted in its entirety so no duplicate files or stale legacy modules remain in `packages/core-models`.
   - `packages/core-models/pyproject.toml` must update `[tool.hatch.build.targets.wheel]` `packages` list from `["execution", "localization", "sdtm"]` to `["localization", "sdtm"]`.
4. **Import Standard Alignment**:
   Updating all 33 files to import from `apps.execution.src.domain.<module>` ensures explicit, fully-qualified imports that adhere to Ruff I001 import sorting and eliminates reliance on implicit `sys.path` hacks.

---

## 3. Caveats

1. **`packages/__init__.py` and `apps/execution/pyproject.toml`**:
   `packages/__init__.py` retains its `sys.path` injection for remaining core-models (`sdtm`, `localization`), and `apps/execution/pyproject.toml` retains `"packages-core-models"` in its `dependencies`. These will be cleaned up in Milestone M5 ("Eradicate `packages/core-models`").
2. **Import Sorting (I001)**:
   When replacing `from execution.<module>` with `from apps.execution.src.domain.<module>`, the position of the import block will change from top-level / standard / third-party group to the first-party group (`apps.execution...`). Running `uv run ruff check . --fix` will automatically position the imports alphabetically per `AGENTS.md`.

---

## 4. Conclusion

The investigation into project configuration, `sys.path` hacks, pyproject.toml settings, and file inventory for Execution Service Domain Migration (M3) is complete:
- **0 dynamic imports** target `packages/core-models/execution`.
- **1 configuration file** (`packages/core-models/pyproject.toml`) requires editing to remove `"execution"` from wheel build packages.
- **1 directory** (`packages/core-models/execution/`) containing 13 files must be completely removed post-migration.
- **33 Python source and test files** across the repository must have their import statements updated to `apps.execution.src.domain.<module>`.

---

## 5. Verification Method

To independently verify post-migration status:

### Step 1: Code Base Verification Commands
Run the complete iteration gate suite:
```bash
# 1. Lint and Import Ordering Check
uv run ruff check .

# 2. Code Formatting Check
uv run ruff format --check .

# 3. Code Duplication Scanner
python3 scripts/detect_duplication.py

# 4. Full Pytest Test Suite
uv run pytest -n auto

# 5. GxP Compliance Dry-Run
uv run python scripts/sync_gxp.py --dry-run
```

### Step 2: Stale Import & Module Invalidation Test
Verify that no `from execution.` imports remain and attempting to import legacy modules fails cleanly:
```bash
# Verify 0 files contain 'from execution.' imports
grep -rn "from execution\." apps/ packages/ scripts/ tests/

# Empirical verification that importing legacy execution module raises ModuleNotFoundError
python3 -c "import pytest; pytest.raises(ModuleNotFoundError, __import__, 'execution.doa_models')"
```

### Step 3: Filesystem & Configuration Verification
Inspect:
1. `packages/core-models/execution/` directory is absent.
2. `packages/core-models/pyproject.toml` contains `packages = ["localization", "sdtm"]`.
3. `apps/execution/src/domain/` contains all 13 relocated domain models.
