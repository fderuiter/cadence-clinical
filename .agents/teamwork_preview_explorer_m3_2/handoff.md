# Handoff Report — Milestone M3 Technical Investigation (Execution Service Domain Migration)

## 1. Observation

Direct observations from searching the codebase:

1. **Relocated Files in Domain Target**:
   Running `find_by_name` on `/Users/fred/Code/cadence-clinical/apps/execution/src/domain` confirmed the presence of all 13 execution domain model files:
   - `apps/execution/src/domain/doa_models.py`
   - `apps/execution/src/domain/econsent_models.py`
   - `apps/execution/src/domain/eisf_models.py`
   - `apps/execution/src/domain/epro_transport_models.py`
   - `apps/execution/src/domain/lab_models.py`
   - `apps/execution/src/domain/lab_transport_models.py`
   - `apps/execution/src/domain/lock_models.py`
   - `apps/execution/src/domain/lock_transport_models.py`
   - `apps/execution/src/domain/offline_models.py`
   - `apps/execution/src/domain/safety_models.py`
   - `apps/execution/src/domain/safety_transport_models.py`
   - `apps/execution/src/domain/sdv_transport_models.py`
   - `apps/execution/src/domain/signature_transport_models.py`

2. **Absence of Legacy Directory**:
   `packages/core-models/execution` directory no longer exists on disk.

3. **Active Import Statements Target Listing**:
   Running `grep_search` across `apps/`, `packages/`, `scripts/`, and `tests/` identified **31 files containing 34 import statements** that reference `execution.<module>`:

   - `apps/ctms/tests/test_doa_audit_suite.py:6`: `from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum`
   - `apps/ctms/tests/test_doa_models.py:6`: `from execution.doa_models import (DOAAssignmentRecord, DOATaskDelegationEnum, DOATaskRoleEnum)`
   - `apps/designer/tests/test_granular_locking.py:6`: `from execution.lock_models import DataLockRecord, LockStatusEnum`
   - `apps/designer/tests/test_lock_enforcement.py:9`: `from execution.lock_models import (DataLockRecord, LockScopeEnum, LockStatusEnum)`
   - `apps/designer/tests/test_lock_models.py:8`: `from execution.lock_models import (DataLockRecord, DataUnlockRecord, LockScopeEnum, LockStatusEnum)`
   - `apps/econsent/tests/test_econsent_service.py:5`: `from execution.econsent_models import EConsentSignRequest`
   - `apps/eisf/tests/test_eisf_models.py:8`: `from execution.eisf_models import (EISFDocumentRecord, EISFTaxonomyCategoryEnum)`
   - `apps/eisf/tests/test_eisf_service.py:8`: `from execution.eisf_models import EISFTaxonomyCategoryEnum`
   - `apps/execution/exporters/e2b_xml_builder.py:8`: `from execution.safety_models import SAECaseRecord`
   - `apps/execution/routers/doa.py:8`: `from execution.doa_models import (DOAAssignmentRecord, DOATaskDelegationEnum, DOATaskRoleEnum)`
   - `apps/execution/routers/eisf.py:6`: `from execution.eisf_models import (EISFDocumentRecord, EISFTaxonomyCategoryEnum)`
   - `apps/execution/routers/locks.py:9`: `from execution.lock_models import (DataLockRecord, LockStatusEnum)`
   - `apps/execution/routers/locks.py:13`: `from execution.lock_transport_models import (DataLockRequest, DataLockResponse)`
   - `apps/execution/routers/offline.py:10`: `from execution.offline_models import (OfflineBatchSyncRequest, OfflineBatchSyncResponse, OfflineDeltaItem)`
   - `apps/execution/routers/safety.py:10`: `from execution.safety_transport_models import (SAEReconcileRequest, SafetyDispatchRequest, SafetyDispatchResponse)`
   - `apps/execution/routers/sdv.py:10`: `from execution.sdv_transport_models import (BulkQueryGenerationRequest, BulkQueryGenerationResponse, BulkSdvSignOffRequest, BulkSdvSignOffResponse)`
   - `apps/execution/routers/signatures.py:9`: `from execution.signature_transport_models import (BatchSignatureRequest, BatchSignatureResponse)`
   - `apps/execution/services/doa_service.py:9`: `from execution.doa_models import (DOAAssignmentRecord, DOATaskDelegationEnum, DOATaskRoleEnum)`
   - `apps/execution/services/e2b_parser.py:10`: `from execution.safety_models import (CausalityEnum, SAECaseRecord, SeriousnessCriteriaEnum)`
   - `apps/execution/services/econsent_capture_service.py:6`: `from execution.econsent_models import (EConsentSignRequest, EConsentSignResponse)`
   - `apps/execution/services/eisf_service.py:10`: `from execution.eisf_models import (EISFDocumentRecord, EISFTaxonomyCategoryEnum)`
   - `apps/execution/services/lock_enforcement.py:8`: `from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum`
   - `apps/execution/services/sae_reconciler.py:8`: `from execution.safety_models import SAECaseRecord`
   - `apps/execution/tests/test_lab_schemas.py:9`: `from execution.lab_models import (LabSourceEnum, LabTestMasterRecord, LabUnitConversionRecord)`
   - `apps/execution/tests/test_lab_schemas.py:14`: `from execution.lab_transport_models import (LabRangeRecalculateRequest, ...)`
   - `apps/execution/tests/test_sdv_item_level_rbac.py:7`: `from execution.sdv_transport_models import (FlagTargetDescriptor, SdvFlagRequest, ...)`
   - `apps/execution/tests/test_tsdv.py:712`: `from execution.sdv_transport_models import (BulkQueryGenerationRequest, ...)`
   - `apps/gateway/routers/ecoa.py:12`: `from execution.epro_transport_models import (InstrumentCreate, ...)`
   - `apps/gateway/routers/ecoa.py:19`: `from execution.offline_models import (AcknowledgeNotificationRequest, ...)`
   - `apps/interop/main.py:8`: `from execution.epro_transport_models import (AssignmentComplianceDetail, ...)`
   - `apps/safety/tests/test_e2b_parser.py:6`: `from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum`
   - `apps/safety/tests/test_sae_reconciler.py:8`: `from execution.safety_models import (CausalityEnum, SAECaseRecord, SeriousnessCriteriaEnum)`
   - `apps/safety/tests/test_safety_gateway.py:15`: `from execution.safety_models import (CausalityEnum, SAECaseRecord, SeriousnessCriteriaEnum)`
   - `tests/validation/prd_compliance_traceability_suite.py:9`: `from execution.econsent_models import EConsentSignRequest`

4. **Package `__init__.py` Audit**:
   - `apps/execution/src/domain/__init__.py` contains package docstring (`"""Execution domain models package."""`).
   - `packages/__init__.py` contains `_core_models_path` sys.path injection (to be updated/removed in M5).

---

## 2. Logic Chain

1. **Step 1**: `packages/core-models/execution` has been relocated to `apps/execution/src/domain/`.
2. **Step 2**: Existing code imports execution domain models using top-level module syntax (`from execution.<module> import ...`), enabled by legacy `sys.path.insert(0, _core_models_path)`.
3. **Step 3**: To complete Milestone M3 domain migration and prepare for `packages/core-models` eradication in M5, all 34 import statements across 31 files must be updated to `from apps.execution.src.domain.<module> import ...`.
4. **Step 4**: Updating these imports will shift them to first-party (`apps.*`) imports, which must comply with Ruff I001 import ordering (alphabetical ordering of module paths and imported names).

---

## 3. Caveats

- **Cross-service imports**: Note that `apps/gateway/routers/ecoa.py`, `apps/interop/main.py`, `apps/ctms/tests/*`, `apps/designer/tests/*`, and `apps/safety/tests/*` currently import execution domain/transport models directly. Milestone M3 updates their import paths to `apps.execution.src.domain...`. In Milestone M4, cross-service interactions will be refactored to use local consumer-owned Anti-Corruption Layer (ACL) DTOs.

---

## 4. Conclusion

Milestone M3 implementation requires updating 34 import statements across 31 files to import from `apps.execution.src.domain.<module>`. Detailed mapping is documented in `analysis.md`.

---

## 5. Verification Method

To independently verify these findings:
1. Run `grep -rn "from execution\." apps/ packages/ scripts/ tests/` and verify all 34 occurrences match the inventory.
2. Run `find apps/execution/src/domain/ -maxdepth 1 -name "*.py"` and verify all 13 model files exist.
3. After updating imports in an implementation turn, run:
   - `uv run ruff check .`
   - `uv run ruff format .`
   - `uv run pytest -n auto`
