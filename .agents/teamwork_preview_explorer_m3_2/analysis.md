# Comprehensive Analysis — Execution Service Domain Migration (M3 Import Mapping)

## Executive Summary

This investigation cataloged all import statements and references across `apps/`, `packages/`, `scripts/`, and `tests/` that target execution core models (`packages.core_models.execution` or `execution.*`).

All 13 execution domain model files (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`) are located under `apps/execution/src/domain/`.

A total of **31 files** containing **34 import statements** across microservices and tests require updating from `from execution.<module>` to `from apps.execution.src.domain.<module>`.

---

## 1. Domain Models Location

The target directory for execution domain models is:
`apps/execution/src/domain/`

The 13 relocated execution model files:
1. `apps/execution/src/domain/doa_models.py`
2. `apps/execution/src/domain/econsent_models.py`
3. `apps/execution/src/domain/eisf_models.py`
4. `apps/execution/src/domain/epro_transport_models.py`
5. `apps/execution/src/domain/lab_models.py`
6. `apps/execution/src/domain/lab_transport_models.py`
7. `apps/execution/src/domain/lock_models.py`
8. `apps/execution/src/domain/lock_transport_models.py`
9. `apps/execution/src/domain/offline_models.py`
10. `apps/execution/src/domain/safety_models.py`
11. `apps/execution/src/domain/safety_transport_models.py`
12. `apps/execution/src/domain/sdv_transport_models.py`
13. `apps/execution/src/domain/signature_transport_models.py`

---

## 2. Complete Import Update Inventory (31 Files / 34 Import Statements)

### Microservices Codebase (`apps/`)

| # | File Path | Line # | Current Import | Target Import |
|---|-----------|--------|----------------|---------------|
| 1 | `apps/ctms/tests/test_doa_audit_suite.py` | 6 | `from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` | `from apps.execution.src.domain.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` |
| 2 | `apps/ctms/tests/test_doa_models.py` | 6 | `from execution.doa_models import (DOAAssignmentRecord, ...)` | `from apps.execution.src.domain.doa_models import (DOAAssignmentRecord, ...)` |
| 3 | `apps/designer/tests/test_granular_locking.py` | 6 | `from execution.lock_models import DataLockRecord, LockStatusEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockStatusEnum` |
| 4 | `apps/designer/tests/test_lock_enforcement.py` | 9 | `from execution.lock_models import (DataLockRecord, ...)` | `from apps.execution.src.domain.lock_models import (DataLockRecord, ...)` |
| 5 | `apps/designer/tests/test_lock_models.py` | 8 | `from execution.lock_models import (DataLockRecord, ...)` | `from apps.execution.src.domain.lock_models import (DataLockRecord, ...)` |
| 6 | `apps/econsent/tests/test_econsent_service.py` | 5 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |
| 7 | `apps/eisf/tests/test_eisf_models.py` | 8 | `from execution.eisf_models import (EISFDocumentRecord, ...)` | `from apps.execution.src.domain.eisf_models import (EISFDocumentRecord, ...)` |
| 8 | `apps/eisf/tests/test_eisf_service.py` | 8 | `from execution.eisf_models import EISFTaxonomyCategoryEnum` | `from apps.execution.src.domain.eisf_models import EISFTaxonomyCategoryEnum` |
| 9 | `apps/execution/exporters/e2b_xml_builder.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| 10 | `apps/execution/routers/doa.py` | 8 | `from execution.doa_models import (DOAAssignmentRecord, ...)` | `from apps.execution.src.domain.doa_models import (DOAAssignmentRecord, ...)` |
| 11 | `apps/execution/routers/eisf.py` | 6 | `from execution.eisf_models import (EISFDocumentRecord, ...)` | `from apps.execution.src.domain.eisf_models import (EISFDocumentRecord, ...)` |
| 12 | `apps/execution/routers/locks.py` | 9, 13 | `from execution.lock_models import (...)`<br>`from execution.lock_transport_models import (...)` | `from apps.execution.src.domain.lock_models import (...)`<br>`from apps.execution.src.domain.lock_transport_models import (...)` |
| 13 | `apps/execution/routers/offline.py` | 10 | `from execution.offline_models import (...)` | `from apps.execution.src.domain.offline_models import (...)` |
| 14 | `apps/execution/routers/safety.py` | 10 | `from execution.safety_transport_models import (...)` | `from apps.execution.src.domain.safety_transport_models import (...)` |
| 15 | `apps/execution/routers/sdv.py` | 10 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 16 | `apps/execution/routers/signatures.py` | 9 | `from execution.signature_transport_models import (...)` | `from apps.execution.src.domain.signature_transport_models import (...)` |
| 17 | `apps/execution/services/doa_service.py` | 9 | `from execution.doa_models import (...)` | `from apps.execution.src.domain.doa_models import (...)` |
| 18 | `apps/execution/services/e2b_parser.py` | 10 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |
| 19 | `apps/execution/services/econsent_capture_service.py` | 6 | `from execution.econsent_models import (...)` | `from apps.execution.src.domain.econsent_models import (...)` |
| 20 | `apps/execution/services/eisf_service.py` | 10 | `from execution.eisf_models import (...)` | `from apps.execution.src.domain.eisf_models import (...)` |
| 21 | `apps/execution/services/lock_enforcement.py` | 8 | `from execution.lock_models import DataLockRecord, ...` | `from apps.execution.src.domain.lock_models import DataLockRecord, ...` |
| 22 | `apps/execution/services/sae_reconciler.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| 23 | `apps/execution/tests/test_lab_schemas.py` | 9, 14 | `from execution.lab_models import (...)`<br>`from execution.lab_transport_models import (...)` | `from apps.execution.src.domain.lab_models import (...)`<br>`from apps.execution.src.domain.lab_transport_models import (...)` |
| 24 | `apps/execution/tests/test_sdv_item_level_rbac.py` | 7 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 25 | `apps/execution/tests/test_tsdv.py` | 712 | `from execution.sdv_transport_models import (...)` | `from apps.execution.src.domain.sdv_transport_models import (...)` |
| 26 | `apps/gateway/routers/ecoa.py` | 12, 19 | `from execution.epro_transport_models import (...)`<br>`from execution.offline_models import (...)` | `from apps.execution.src.domain.epro_transport_models import (...)`<br>`from apps.execution.src.domain.offline_models import (...)` |
| 27 | `apps/interop/main.py` | 8 | `from execution.epro_transport_models import (...)` | `from apps.execution.src.domain.epro_transport_models import (...)` |
| 28 | `apps/safety/tests/test_e2b_parser.py` | 6 | `from execution.safety_models import CausalityEnum, ...` | `from apps.execution.src.domain.safety_models import CausalityEnum, ...` |
| 29 | `apps/safety/tests/test_sae_reconciler.py` | 8 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |
| 30 | `apps/safety/tests/test_safety_gateway.py` | 15 | `from execution.safety_models import (...)` | `from apps.execution.src.domain.safety_models import (...)` |

### Global Validation Tests (`tests/`)

| # | File Path | Line # | Current Import | Target Import |
|---|-----------|--------|----------------|---------------|
| 31 | `tests/validation/prd_compliance_traceability_suite.py` | 9 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |

---

## 3. Package & Re-export Analysis (`__init__.py`)

1. **`apps/execution/src/domain/__init__.py`**: Contains `"""Execution domain models package."""`. Re-exports can be added if desired, but explicit module imports (`from apps.execution.src.domain.<module> import ...`) are preferred to maintain explicit dependency tracking.
2. **`apps/execution/domain/__init__.py`**: Re-exports `models.py` and `repositories.py` (`ClinicalSubjectDomain`, etc.). Untouched by M3.
3. **`packages/core-models/execution/`**: The legacy directory has been removed/relocated.
4. **`packages/__init__.py`**: Contains `sys.path.insert(0, _core_models_path)` pointing to `packages/core-models`. Standard cleanup scheduled in M5.

---

## 4. Import Ordering Rules (Ruff I001)

When replacing `from execution.<module>` with `from apps.execution.src.domain.<module>`:
- The import statement shifts from top-level namespace into Group 3 (First-party imports).
- Group 3 imports must be sorted alphabetically by full module path (e.g. `from apps.execution...` comes before `from packages...`).
- Symbols inside multi-line parentheses must be sorted alphabetically.
- Auto-formatting command post-update: `uv run ruff check . --fix && uv run ruff format .`.
