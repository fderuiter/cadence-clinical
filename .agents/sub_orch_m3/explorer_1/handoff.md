# Handoff Report — Explorer 1 (Milestone M3: Execution Service Domain Migration)

## 1. Observation

### 1.1 Complete Inventory of `packages/core-models/execution/`
The directory `packages/core-models/execution/` contains 13 Python files (excluding `__pycache__`):

| File | Line Count | Public Classes / Enums / Schemas | Purpose & Type |
|---|---|---|---|
| `doa_models.py` | 56 | `DOATaskRoleEnum` (StrEnum), `DOATaskDelegationEnum` (StrEnum), `DOAAssignmentRecord` (BaseModel) | Delegation of Authority site staffing & task delegation log models |
| `econsent_models.py` | 38 | `EConsentSignRequest` (BaseModel), `EConsentSignResponse` (BaseModel) | eConsent signature capture request/response transport models |
| `eisf_models.py` | 52 | `EISFTaxonomyCategoryEnum` (StrEnum), `EISFDocumentRecord` (BaseModel) | Electronic Investigator Site File regulatory binder taxonomy models |
| `epro_transport_models.py` | 80 | `InstrumentCreate`, `InstrumentResponse`, `SubjectAssignmentCreate`, `SubjectAssignmentResponse`, `AssignmentComplianceDetail`, `SubjectComplianceResponse` (BaseModels) | eCOA & ePRO instrument assignment & compliance transport schemas |
| `lab_models.py` | 106 | `LabSourceEnum` (StrEnum), `SexApplicability` (StrEnum), `LabTestMasterRecord` (BaseModel), `LabUnitConversionRecord` (BaseModel) | Laboratory test master catalog and unit conversion domain records with GxP audit fields |
| `lab_transport_models.py` | 269 | `LabReferenceRangeCreate`, `LabReferenceRangeUpdate`, `LabReferenceRangeResponse`, `LabTestMasterCreate`, `LabTestMasterResponse`, `LabUnitConversionCreate`, `LabUnitConversionResponse`, `LabRangeRecalculateRequest`, `LabRangeRecalculateResponse` | Laboratory reference range & master catalog REST transport schemas |
| `lock_models.py` | 73 | `LockScopeEnum` (StrEnum), `LockStatusEnum` (StrEnum), `DataLockRecord` (BaseModel), `DataUnlockRecord` (BaseModel) | Granular form-, item-group-, and field-level eCRF data locking & unlock audit models |
| `lock_transport_models.py` | 44 | `DataLockRequest` (BaseModel), `DataLockResponse` (BaseModel) | Granular eCRF data locking REST API request/response schemas |
| `offline_models.py` | 407 | `JsonValue` (TypeAlias), `EPROSubmissionStatus` (Literal), `OfflineDeltaItem`, `OfflineBatchSyncRequest`, `OfflineBatchSyncResponse`, `ConflictStrategyEnum` (StrEnum), `EPROOfflineMarker`, `OfflineSyncMarkers`, `EPROOfflineEntry`, `EPROBulkSyncRequest`, `EPROSubmitResponse`, `EPROBulkSyncResponse`, `EPROPersistedEntryResponse`, `EPROSubmissionRequest`, `EPROSubmissionResponse`, `SubjectNotificationResponse`, `AcknowledgeNotificationRequest`, `EPROScheduleItemResponse`, `EPRODiaryFormDefinitionResponse` | Mobile offline sync batch delta ingestion & ePRO reconciliation schemas |
| `safety_models.py` | 67 | `SeriousnessCriteriaEnum` (StrEnum), `CausalityEnum` (StrEnum), `SAECaseRecord` (BaseModel) | Serious Adverse Event (SAE) cases & ICH E2B(R3) safety reporting models |
| `safety_transport_models.py` | 59 | `SafetyDispatchRequest` (BaseModel), `SafetyDispatchResponse` (BaseModel), `SAEReconcileRequest` (BaseModel) | Safety Gateway dispatch & SAE reconciliation transport schemas |
| `sdv_transport_models.py` | 282 | `generate_audit_tx()`, `generate_identifier()`, `BulkSdvSignOffRequest`, `BulkSdvSignOffResponse`, `QueryTargetDescriptor`, `BulkQueryGenerationRequest`, `BulkQueryGenerationResponse`, `SdvFlagSeverity` (StrEnum), `FlagTargetDescriptor`, `SdvFlagRequest`, `SdvResolveRequest`, `SdvFlagResponse`, `SdvResolveResponse` | Source Data Verification (SDV) bulk sign-off, query generation & flag resolution schemas |
| `signature_transport_models.py` | 67 | `BatchSignatureRequest` (BaseModel with `@model_validator`), `BatchSignatureResponse` (BaseModel) | 21 CFR Part 11 batch eSignature execution request/response schemas |

### 1.2 Target File Structure Mapping under `apps/execution/src/domain/`
Verbatim inspection and `diff -s` confirmed that all 13 files already exist under `apps/execution/src/domain/` and are byte-for-byte identical to the files in `packages/core-models/execution/`:

```
apps/execution/src/domain/
├── __init__.py                     # Package marker
├── doa_models.py                   # Identical mirror
├── econsent_models.py              # Identical mirror
├── eisf_models.py                  # Identical mirror
├── epro_transport_models.py       # Identical mirror
├── exceptions.py                   # Execution domain exception types
├── lab_models.py                   # Identical mirror
├── lab_transport_models.py         # Identical mirror (needs internal import fix)
├── lock_models.py                  # Identical mirror
├── lock_transport_models.py        # Identical mirror (needs internal import fix)
├── localization/                   # Sub-package for localization models
│   ├── __init__.py
│   └── models.py
├── models.py                       # SQLModel ORM execution database models
├── offline_models.py               # Identical mirror
├── repositories.py                 # Execution domain repository layer
├── safety_models.py                # Identical mirror
├── safety_transport_models.py      # Identical mirror
├── sdtm/                           # Sub-package for SDTM domain models & mappers
│   ├── __init__.py
│   ├── dataset_json_models.py
│   ├── enums.py
│   ├── models.py
│   ├── scrubber_models.py
│   ├── sdtm_models.py
│   └── terminology.py
├── sdv_transport_models.py         # Identical mirror
├── signature_transport_models.py  # Identical mirror
└── watermark.py                    # PDF watermarking helper
```

### 1.3 Internal and External Dependency Analysis

#### 1.3.1 Internal Dependencies within execution domain files
- `lab_transport_models.py` (Line 8):
  - Current: `from execution.lab_models import LabSourceEnum`
  - Required: `from apps.execution.src.domain.lab_models import LabSourceEnum`
- `lock_transport_models.py` (Line 6):
  - Current: `from execution.lock_models import DataLockRecord, LockScopeEnum`
  - Required: `from apps.execution.src.domain.lock_models import DataLockRecord, LockScopeEnum`

#### 1.3.2 External Call Sites Importing from `execution.<module>`
A total of 37 import statements across 35 files in `apps/`, `packages/`, and `tests/` currently import execution domain models using `from execution.<module>` due to `sys.path` injection in `packages/__init__.py`:

1. `apps/ctms/tests/test_doa_audit_suite.py:6` — `from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum`
2. `apps/ctms/tests/test_doa_models.py:6` — `from execution.doa_models import (...)`
3. `apps/designer/tests/test_granular_locking.py:6` — `from execution.lock_models import DataLockRecord, LockStatusEnum`
4. `apps/designer/tests/test_lock_enforcement.py:9` — `from execution.lock_models import (...)`
5. `apps/designer/tests/test_lock_models.py:8` — `from execution.lock_models import (...)`
6. `apps/econsent/tests/test_econsent_service.py:5` — `from execution.econsent_models import EConsentSignRequest`
7. `apps/eisf/tests/test_eisf_models.py:8` — `from execution.eisf_models import (...)`
8. `apps/eisf/tests/test_eisf_service.py:8` — `from execution.eisf_models import EISFTaxonomyCategoryEnum`
9. `apps/execution/exporters/e2b_xml_builder.py:8` — `from execution.safety_models import SAECaseRecord`
10. `apps/execution/routers/doa.py:8` — `from execution.doa_models import (...)`
11. `apps/execution/routers/eisf.py:6` — `from execution.eisf_models import (...)`
12. `apps/execution/routers/locks.py:9` — `from execution.lock_models import (...)`
13. `apps/execution/routers/locks.py:13` — `from execution.lock_transport_models import (...)`
14. `apps/execution/routers/offline.py:10` — `from execution.offline_models import (...)`
15. `apps/execution/routers/safety.py:10` — `from execution.safety_transport_models import (...)`
16. `apps/execution/routers/sdv.py:10` — `from execution.sdv_transport_models import (...)`
17. `apps/execution/routers/signatures.py:9` — `from execution.signature_transport_models import (...)`
18. `apps/execution/services/doa_service.py:9` — `from execution.doa_models import (...)`
19. `apps/execution/services/e2b_parser.py:10` — `from execution.safety_models import (...)`
20. `apps/execution/services/econsent_capture_service.py:6` — `from execution.econsent_models import (...)`
21. `apps/execution/services/eisf_service.py:10` — `from execution.eisf_models import (...)`
22. `apps/execution/services/lock_enforcement.py:8` — `from execution.lock_models import DataLockRecord, LockScopeEnum, LockStatusEnum`
23. `apps/execution/services/sae_reconciler.py:8` — `from execution.safety_models import SAECaseRecord`
24. `apps/execution/src/domain/lab_transport_models.py:8` — `from execution.lab_models import (...)`
25. `apps/execution/src/domain/lock_transport_models.py:6` — `from execution.lock_models import DataLockRecord, LockScopeEnum`
26. `apps/execution/tests/test_lab_schemas.py:9` — `from execution.lab_models import (...)`
27. `apps/execution/tests/test_lab_schemas.py:14` — `from execution.lab_transport_models import (...)`
28. `apps/execution/tests/test_sdv_item_level_rbac.py:7` — `from execution.sdv_transport_models import (...)`
29. `apps/execution/tests/test_tsdv.py:712` — `from execution.sdv_transport_models import (...)`
30. `apps/gateway/routers/ecoa.py:12` — `from execution.epro_transport_models import (...)`
31. `apps/gateway/routers/ecoa.py:19` — `from execution.offline_models import (...)`
32. `apps/interop/main.py:8` — `from execution.epro_transport_models import (...)`
33. `apps/safety/tests/test_e2b_parser.py:6` — `from execution.safety_models import CausalityEnum, SeriousnessCriteriaEnum`
34. `apps/safety/tests/test_sae_reconciler.py:8` — `from execution.safety_models import (...)`
35. `apps/safety/tests/test_safety_gateway.py:15` — `from execution.safety_models import (...)`
36. `packages/core-models/execution/lab_transport_models.py:8` — `from execution.lab_models import (...)` (Legacy dir, deleted in M3)
37. `packages/core-models/execution/lock_transport_models.py:6` — `from execution.lock_models import DataLockRecord, LockScopeEnum` (Legacy dir, deleted in M3)
38. `tests/validation/prd_compliance_traceability_suite.py:9` — `from execution.econsent_models import EConsentSignRequest`

## 2. Logic Chain

1. **Observation**: All 13 files in `packages/core-models/execution/` (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`) already exist in `apps/execution/src/domain/` with byte-for-byte identical content (`diff -s` returned code 0 for all 13 pairs).
2. **Logic Step**: Because the target domain models in `apps/execution/src/domain/` are already populated, no new file creation or schema transformation is required under `apps/execution/src/domain/`.
3. **Observation**: `apps/execution/src/domain/lab_transport_models.py` (line 8) and `apps/execution/src/domain/lock_transport_models.py` (line 6) contain imports referencing `execution.lab_models` and `execution.lock_models`.
4. **Logic Step**: In order to make `apps/execution/src/domain/` self-contained and eliminate legacy `sys.path` dependency on `packages/core-models/execution/`, these intra-domain imports must be updated to `from apps.execution.src.domain.lab_models` and `from apps.execution.src.domain.lock_models`.
5. **Observation**: 35 external call sites across `apps/`, `packages/`, and `tests/` import from `execution.<module>`.
6. **Logic Step**: All 35 call sites must be updated to import from `apps.execution.src.domain.<module>`. Ruff import ordering rule I001 must be enforced on all modified files (`uv run ruff check . --fix`).
7. **Observation**: Once all 35 call sites are updated, no active code or tests will depend on `packages/core-models/execution/`.
8. **Logic Step**: `packages/core-models/execution/` can be safely removed, completing the domain model relocation step for Milestone M3.

## 3. Caveats

- **No Caveats regarding model divergence**: The 13 files in `packages/core-models/execution/` and `apps/execution/src/domain/` are currently identical.
- **Microservice Boundary Note**: `apps/ctms`, `apps/designer`, `apps/econsent`, `apps/eisf`, `apps/gateway`, `apps/interop`, and `apps/safety` currently import directly from execution models in test files or routers. In Milestone M4 (ACL implementation), cross-service DTOs will be formalized under `apps/<service>/src/domain/acl/`. In Milestone M3, updating their import paths to `apps.execution.src.domain...` is the required step to decouple `packages/core-models`.

## 4. Conclusion & Recommended Relocation Strategy

### Recommended 5-Step Action Plan for Milestone M3

1. **Step 1 — Update Intra-Domain Imports**:
   - In `apps/execution/src/domain/lab_transport_models.py`: update `from execution.lab_models` -> `from apps.execution.src.domain.lab_models`.
   - In `apps/execution/src/domain/lock_transport_models.py`: update `from execution.lock_models` -> `from apps.execution.src.domain.lock_models`.

2. **Step 2 — Update External Import Paths across Codebase**:
   - Update all 35 external call sites in `apps/ctms/tests/`, `apps/designer/tests/`, `apps/econsent/tests/`, `apps/eisf/tests/`, `apps/execution/exporters/`, `apps/execution/routers/`, `apps/execution/services/`, `apps/execution/tests/`, `apps/gateway/routers/`, `apps/interop/`, `apps/safety/tests/`, and `tests/validation/` to import from `apps.execution.src.domain.<module>`.

3. **Step 3 — Delete Legacy Directory**:
   - Delete `packages/core-models/execution/` directory.

4. **Step 4 — Enforce Ruff Formatting & Run CI Gates**:
   - Run `uv run ruff check . --fix`
   - Run `uv run ruff format .`
   - Run `python3 scripts/detect_duplication.py`
   - Run `uv run pytest -n auto`
   - Run `uv run python scripts/sync_gxp.py`

5. **Step 5 — Update Project Milestone Status**:
   - Update `PROJECT.md` setting Milestone M3 Status to `DONE`.

## 5. Verification Method

To verify the relocation and codebase health independently:

1. **Verify No Remaining References**:
   ```bash
   grep -rn "from execution\." apps/ packages/ scripts/ tests/
   ```
   Expect output: empty (0 matches).

2. **Verify Execution Models Importability**:
   ```bash
   uv run python -c "from apps.execution.src.domain.doa_models import DOAAssignmentRecord; from apps.execution.src.domain.safety_models import SAECaseRecord; print('Execution domain models imported successfully')"
   ```
   Expect output: `Execution domain models imported successfully`.

3. **Run Code Quality & Duplication Gates**:
   ```bash
   uv run ruff check .
   uv run ruff format --check .
   python3 scripts/detect_duplication.py
   ```

4. **Run Full Pytest Suite**:
   ```bash
   uv run pytest -n auto
   ```

5. **Verify GxP Sync**:
   ```bash
   uv run python scripts/sync_gxp.py --dry-run
   ```
