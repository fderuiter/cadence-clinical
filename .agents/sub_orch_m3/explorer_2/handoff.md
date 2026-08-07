# Handoff Report — Explorer 2: Execution Domain Import Statements Investigation

**Milestone**: M3 (Execution Service Domain Migration)  
**Agent**: Explorer 2  
**Working Directory**: `/Users/fred/Code/cadence-clinical/.agents/sub_orch_m3/explorer_2/`  
**Project Root**: `/Users/fred/Code/cadence-clinical`  

---

## 1. Observation

### 1.1 Relocated Execution Domain Modules
Investigation of `/Users/fred/Code/cadence-clinical/packages/core-models/execution/` identified **13 domain model files** target for relocation to `/Users/fred/Code/cadence-clinical/apps/execution/src/domain/`:
1. `doa_models.py`
2. `econsent_models.py`
3. `eisf_models.py`
4. `epro_transport_models.py`
5. `lab_models.py`
6. `lab_transport_models.py`
7. `lock_models.py`
8. `lock_transport_models.py`
9. `offline_models.py`
10. `safety_models.py`
11. `safety_transport_models.py`
12. `sdv_transport_models.py`
13. `signature_transport_models.py`

### 1.2 Import Investigation Results
An exhaustive AST and regex scan across all Python files in `apps/`, `packages/`, `scripts/`, and `tests/` identified exactly **38 import statements** across **33 distinct files** referencing execution domain models using legacy path syntax (`from execution.<module>` or `from packages.core_models.execution.<module>`).

Below is the verbatim list of observed import lines and locations:

```
1. apps/ctms/tests/test_doa_audit_suite.py:6
   Verbatim: from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum

2. apps/ctms/tests/test_doa_models.py:6
   Verbatim: from execution.doa_models import (

3. apps/designer/tests/test_granular_locking.py:6
   Verbatim: from execution.lock_models import DataLockRecord, LockStatusEnum

4. apps/designer/tests/test_lock_enforcement.py:9
   Verbatim: from execution.lock_models import (

5. apps/designer/tests/test_lock_models.py:8
   Verbatim: from execution.lock_models import (

6. apps/econsent/tests/test_econsent_service.py:5
   Verbatim: from execution.econsent_models import EConsentSignRequest

7. apps/eisf/tests/test_eisf_models.py:8
   Verbatim: from execution.eisf_models import (

8. apps/eisf/tests/test_eisf_service.py:8
   Verbatim: from execution.eisf_models import EISFTaxonomyCategoryEnum

9. apps/execution/exporters/e2b_xml_builder.py:8
   Verbatim: from execution.safety_models import SAECaseRecord

10. apps/execution/routers/doa.py:8
    Verbatim: from execution.doa_models import (

11. apps/execution/routers/eisf.py:6
    Verbatim: from execution.eisf_models import (

12. apps/execution/routers/locks.py:9
    Verbatim: from execution.lock_models import (

13. apps/execution/routers/locks.py:13
    Verbatim: from execution.lock_transport_models import (

14. apps/execution/routers/offline.py:10
    Verbatim: from execution.offline_models import (

15. apps/execution/routers/safety.py:10
    Verbatim: from execution.safety_transport_models import (

16. apps/execution/routers/sdv.py:10
    Verbatim: from execution.sdv_transport_models import (

17. apps/execution/routers/signatures.py:9
    Verbatim: from execution.signature_transport_models import (

18. apps/execution/services/doa_service.py:9
    Verbatim: from execution.doa_models import (

19. apps/execution/services/e2b_parser.py:10
    Verbatim: from execution.safety_models import (

20. apps/execution/services/econsent_capture_service.py:6
    Verbatim: from execution.econsent_models import (

21. apps/execution/services/eisf_service.py:10
    Verbatim: from execution.eisf_models import (

22. apps/execution/services/lock_enforcement.py:8
    Verbatim: from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum

23. apps/execution/services/sae_reconciler.py:8
    Verbatim: from execution.safety_models import SAECaseRecord

24. apps/execution/src/domain/lab_transport_models.py:8
    Verbatim: from execution.lab_models import (

25. apps/execution/src/domain/lock_transport_models.py:6
    Verbatim: from execution.lock_models import DataLockRecord, LockScopeEnum

26. apps/execution/tests/test_lab_schemas.py:9
    Verbatim: from execution.lab_models import (

27. apps/execution/tests/test_lab_schemas.py:14
    Verbatim: from execution.lab_transport_models import (

28. apps/execution/tests/test_sdv_item_level_rbac.py:7
    Verbatim: from execution.sdv_transport_models import (

29. apps/execution/tests/test_tsdv.py:712
    Verbatim: from execution.sdv_transport_models import (

30. apps/gateway/routers/ecoa.py:12
    Verbatim: from execution.epro_transport_models import (

31. apps/gateway/routers/ecoa.py:19
    Verbatim: from execution.offline_models import (

32. apps/interop/main.py:8
    Verbatim: from execution.epro_transport_models import (

33. apps/safety/tests/test_e2b_parser.py:6
    Verbatim: from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum

34. apps/safety/tests/test_sae_reconciler.py:8
    Verbatim: from execution.safety_models import (

35. apps/safety/tests/test_safety_gateway.py:15
    Verbatim: from execution.safety_models import (

36. packages/core-models/execution/lab_transport_models.py:8
    Verbatim: from execution.lab_models import (

37. packages/core-models/execution/lock_transport_models.py:6
    Verbatim: from execution.lock_models import DataLockRecord, LockScopeEnum

38. tests/validation/prd_compliance_traceability_suite.py:9
    Verbatim: from execution.econsent_models import EConsentSignRequest
```

---

## 2. Logic Chain

1. **Relocation Context**:
   In Milestone M3, all 13 domain models owned by the Execution service are moved from `packages/core-models/execution/` to `apps/execution/src/domain/`.

2. **Path Transformation**:
   Legacy imports resolved via `sys.path.insert(0, ...core-models)` took the form `from execution.<module> import ...`. To enforce microservice domain ownership, all such imports must be rewritten to `from apps.execution.src.domain.<module> import ...`.

3. **Systematic Mapping Table**:

| Category | File Path | Line | Old Import Statement | New Target Import Path |
|---|---|---|---|---|
| **CTMS Tests** | `apps/ctms/tests/test_doa_audit_suite.py` | 6 | `from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` | `from apps.execution.src.domain.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum` |
| **CTMS Tests** | `apps/ctms/tests/test_doa_models.py` | 6 | `from execution.doa_models import (` | `from apps.execution.src.domain.doa_models import (` |
| **Designer Tests** | `apps/designer/tests/test_granular_locking.py` | 6 | `from execution.lock_models import DataLockRecord, LockStatusEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockStatusEnum` |
| **Designer Tests** | `apps/designer/tests/test_lock_enforcement.py` | 9 | `from execution.lock_models import (` | `from apps.execution.src.domain.lock_models import (` |
| **Designer Tests** | `apps/designer/tests/test_lock_models.py` | 8 | `from execution.lock_models import (` | `from apps.execution.src.domain.lock_models import (` |
| **eConsent Tests** | `apps/econsent/tests/test_econsent_service.py` | 5 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |
| **eISF Tests** | `apps/eisf/tests/test_eisf_models.py` | 8 | `from execution.eisf_models import (` | `from apps.execution.src.domain.eisf_models import (` |
| **eISF Tests** | `apps/eisf/tests/test_eisf_service.py` | 8 | `from execution.eisf_models import EISFTaxonomyCategoryEnum` | `from apps.execution.src.domain.eisf_models import EISFTaxonomyCategoryEnum` |
| **Execution Service** | `apps/execution/exporters/e2b_xml_builder.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| **Execution Service** | `apps/execution/routers/doa.py` | 8 | `from execution.doa_models import (` | `from apps.execution.src.domain.doa_models import (` |
| **Execution Service** | `apps/execution/routers/eisf.py` | 6 | `from execution.eisf_models import (` | `from apps.execution.src.domain.eisf_models import (` |
| **Execution Service** | `apps/execution/routers/locks.py` | 9 | `from execution.lock_models import (` | `from apps.execution.src.domain.lock_models import (` |
| **Execution Service** | `apps/execution/routers/locks.py` | 13 | `from execution.lock_transport_models import (` | `from apps.execution.src.domain.lock_transport_models import (` |
| **Execution Service** | `apps/execution/routers/offline.py` | 10 | `from execution.offline_models import (` | `from apps.execution.src.domain.offline_models import (` |
| **Execution Service** | `apps/execution/routers/safety.py` | 10 | `from execution.safety_transport_models import (` | `from apps.execution.src.domain.safety_transport_models import (` |
| **Execution Service** | `apps/execution/routers/sdv.py` | 10 | `from execution.sdv_transport_models import (` | `from apps.execution.src.domain.sdv_transport_models import (` |
| **Execution Service** | `apps/execution/routers/signatures.py` | 9 | `from execution.signature_transport_models import (` | `from apps.execution.src.domain.signature_transport_models import (` |
| **Execution Service** | `apps/execution/services/doa_service.py` | 9 | `from execution.doa_models import (` | `from apps.execution.src.domain.doa_models import (` |
| **Execution Service** | `apps/execution/services/e2b_parser.py` | 10 | `from execution.safety_models import (` | `from apps.execution.src.domain.safety_models import (` |
| **Execution Service** | `apps/execution/services/econsent_capture_service.py` | 6 | `from execution.econsent_models import (` | `from apps.execution.src.domain.econsent_models import (` |
| **Execution Service** | `apps/execution/services/eisf_service.py` | 10 | `from execution.eisf_models import (` | `from apps.execution.src.domain.eisf_models import (` |
| **Execution Service** | `apps/execution/services/lock_enforcement.py` | 8 | `from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum` |
| **Execution Service** | `apps/execution/services/sae_reconciler.py` | 8 | `from execution.safety_models import SAECaseRecord` | `from apps.execution.src.domain.safety_models import SAECaseRecord` |
| **Execution Domain** | `apps/execution/src/domain/lab_transport_models.py` | 8 | `from execution.lab_models import (` | `from apps.execution.src.domain.lab_models import (` |
| **Execution Domain** | `apps/execution/src/domain/lock_transport_models.py` | 6 | `from execution.lock_models import DataLockRecord, LockScopeEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum` |
| **Execution Tests** | `apps/execution/tests/test_lab_schemas.py` | 9 | `from execution.lab_models import (` | `from apps.execution.src.domain.lab_models import (` |
| **Execution Tests** | `apps/execution/tests/test_lab_schemas.py` | 14 | `from execution.lab_transport_models import (` | `from apps.execution.src.domain.lab_transport_models import (` |
| **Execution Tests** | `apps/execution/tests/test_sdv_item_level_rbac.py` | 7 | `from execution.sdv_transport_models import (` | `from apps.execution.src.domain.sdv_transport_models import (` |
| **Execution Tests** | `apps/execution/tests/test_tsdv.py` | 712 | `from execution.sdv_transport_models import (` | `from apps.execution.src.domain.sdv_transport_models import (` |
| **Gateway Service** | `apps/gateway/routers/ecoa.py` | 12 | `from execution.epro_transport_models import (` | `from apps.execution.src.domain.epro_transport_models import (` |
| **Gateway Service** | `apps/gateway/routers/ecoa.py` | 19 | `from execution.offline_models import (` | `from apps.execution.src.domain.offline_models import (` |
| **Interop Service** | `apps/interop/main.py` | 8 | `from execution.epro_transport_models import (` | `from apps.execution.src.domain.epro_transport_models import (` |
| **Safety Tests** | `apps/safety/tests/test_e2b_parser.py` | 6 | `from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum` | `from apps.execution.src.domain.safety_models import CausalityEnum, SeriousnessCriteriaEnum` |
| **Safety Tests** | `apps/safety/tests/test_sae_reconciler.py` | 8 | `from execution.safety_models import (` | `from apps.execution.src.domain.safety_models import (` |
| **Safety Tests** | `apps/safety/tests/test_safety_gateway.py` | 15 | `from execution.safety_models import (` | `from apps.execution.src.domain.safety_models import (` |
| **Legacy Package** | `packages/core-models/execution/lab_transport_models.py` | 8 | `from execution.lab_models import (` | `from apps.execution.src.domain.lab_models import (` |
| **Legacy Package** | `packages/core-models/execution/lock_transport_models.py` | 6 | `from execution.lock_models import DataLockRecord, LockScopeEnum` | `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum` |
| **Validation Tests** | `tests/validation/prd_compliance_traceability_suite.py` | 9 | `from execution.econsent_models import EConsentSignRequest` | `from apps.execution.src.domain.econsent_models import EConsentSignRequest` |

4. **Import Formatting & Linting (I001 & Ruff Rules)**:
   - **Ruff Rule I001 (isort-style import ordering)**: Standard library imports -> Third-party imports -> First-party imports (`apps.*`, `packages.*`).
   - Changing `from execution.<module>` to `from apps.execution.src.domain.<module>` converts implicit top-level/sys.path imports into explicit first-party imports.
   - **Alphabetical Sorting**: In first-party import blocks, `from apps.execution.src.domain...` must be inserted in strict alphabetical order relative to other `apps...` or `packages...` imports.
   - **Alphabetical Symbol Ordering**: Imported symbols within multiline parentheses (e.g. `from apps.execution.src.domain.lock_models import (DataLockRecord, LockScopeEnum, LockStatusEnum)`) must be alphabetically ordered.
   - **Automated Fix Command**: Running `uv run ruff check . --fix` followed by `uv run ruff format .` will format and sort all updated import blocks according to I001 without manual formatting errors.

---

## 3. Caveats

1. **Cross-Service Direct Imports (M4 ACL Target)**:
   Updating cross-service imports (e.g. `apps/gateway/routers/ecoa.py` or `apps/interop/main.py` importing `apps.execution.src.domain.epro_transport_models`) completes the M3 domain relocation. However, direct cross-service imports will trigger `scripts/validate_imports.py` warnings until Milestone M4, where Anti-Corruption Layer (ACL) DTOs (`apps/<service>/src/domain/acl/`) decouple inter-service boundaries.
2. **Legacy `packages/core-models/execution/` Directory**:
   Until Milestone M5 (Eradication & Pipeline Cleanup), `packages/core-models/execution/` files remain on disk. Updates to imports in `packages/core-models/execution/lab_transport_models.py` and `lock_transport_models.py` ensure internal consistency if legacy files are imported during transition.
3. **`import packages # noqa: F401` Side-Effect Imports**:
   Many test and router files contain `import packages # noqa: F401` to inject `packages/core-models` into `sys.path`. These side-effect imports remain benign in M3 and will be pruned during M5 cleanup.

---

## 4. Conclusion

- A total of **38 import statements** across **33 files** currently reference execution domain models via legacy `from execution.<module>` syntax.
- All 38 import statements must be updated to target `from apps.execution.src.domain.<module>`.
- Following import updates, Workers must execute `uv run ruff check . --fix` and `uv run ruff format .` to maintain 100% compliance with Ruff I001 import ordering and formatting rules.

---

## 5. Verification Method

To independently verify the import mappings and check for any remaining legacy execution imports:

1. **Search for Remaining Legacy Execution Imports**:
   ```bash
   python3 -c '
   import os, re
   modules = [
       "doa_models", "econsent_models", "eisf_models", "epro_transport_models",
       "lab_models", "lab_transport_models", "lock_models", "lock_transport_models",
       "offline_models", "safety_models", "safety_transport_models",
       "sdv_transport_models", "signature_transport_models"
   ]
   found = []
   for d in ["apps", "packages", "scripts", "tests"]:
       for root, _, files in os.walk(d):
           for f in files:
               if f.endswith(".py"):
                   path = os.path.join(root, f)
                   with open(path, "r", encoding="utf-8", errors="ignore") as fp:
                       for idx, line in enumerate(fp, 1):
                           if line.strip().startswith(("from ", "import ")):
                               if any(f"execution.{m}" in line or f"core_models.execution.{m}" in line for m in modules):
                                   found.append(f"{path}:{idx}:{line.strip()}")
   print(f"Remaining legacy execution imports: {len(found)}")
   for item in found:
       print("  ", item)
   '
   ```
   *Expected Result before Workers run*: 38 matches.  
   *Expected Result after Workers run*: 0 matches.

2. **Verify Ruff Import Formatting (I001)**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   ```
   *Expected Result*: 0 lint errors, 0 format violations.
