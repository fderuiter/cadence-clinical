# Handoff Report: Safety, CTMS DOA, and eTMF Domain Models Mapping

## 1. Observation

Direct file system exploration and code search revealed the exact locations, exported symbols, target paths, and import sites for all source files in `packages/core-models/` corresponding to Safety, CTMS DOA, and eTMF domain models:

### Safety Domain Models
- **Source Files**:
  - `packages/core-models/sae_icsr/__init__.py`
  - `packages/core-models/sae_icsr/models.py`
- **Defined Symbols**:
  - `DTC_REGEX`, `validate_dtc_format`, `normalize_severity_val`, `normalize_seriousness_val`
  - `VersionedModel`, `MedDRACoding`, `SeriousAdverseEvent`
  - `ICSRHeader`, `ICSRReportIdentifiers`, `ICSRPatient`, `ICSRReactionEvent`, `ICSRSuspectDrug`, `IndividualCaseSafetyReport`
- **Import Sites**:
  - `apps/safety/main.py:10` (`from sae_icsr import IndividualCaseSafetyReport`)
  - `apps/safety/reconciliation.py:7` (`from sae_icsr import IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent`)
  - `apps/safety/renderer.py:4` (`from sae_icsr import IndividualCaseSafetyReport`)
  - `apps/safety/tests/test_e2b.py:2` (`from sae_icsr import (...)`)
  - `apps/safety/tests/test_sae_icsr.py:3` (`from sae_icsr import (...)`)
  - `apps/safety/tests/test_sae_reconciliation.py:8` (`from sae_icsr import MedDRACoding, SeriousAdverseEvent`)
  - `apps/safety/tests/test_safety_e2b.py:2` (`from sae_icsr import (...)`)
  - `packages/core-models/pyproject.toml:24` (`"sae_icsr"`)

### CTMS Domain Models
- **Source Files**:
  - `packages/core-models/ctms/__init__.py`
  - `packages/core-models/ctms/doa_models.py`
  - `packages/core-models/ctms/doa_transport_models.py`
- **Defined Symbols**:
  - `SiteStaffMemberCreate`, `SiteStaffMemberResponse`, `DOADelegationRecordCreate`, `DOADelegationRecordResponse`
  - `DelegationTaskRequest`, `DOALogResponse`, `RevokeDelegationRequest`, `DOASignOffRequest`
- **Import Sites**:
  - `apps/ctms/routers/doa.py:11` (`from ctms.doa_transport_models import (DelegationTaskRequest, DOALogResponse, DOASignOffRequest, RevokeDelegationRequest)`)
  - `apps/ctms/tests/test_doa_service.py:32` (`from ctms.doa_transport_models import (...)`)
  - `packages/core-models/ctms/__init__.py:3` (`from ctms.doa_models import (...)`)

### eTMF Domain Models
- **Source Files**:
  - `packages/core-models/etmf/__init__.py`
  - `packages/core-models/etmf/eisf_models.py`
  - `packages/core-models/etmf/eisf_transport_models.py`
  - `packages/core-models/tmf_reference_model/__init__.py`
  - `packages/core-models/tmf_reference_model/models.py`
  - `packages/core-models/tmf_reference_model/README.md`
- **Defined Symbols**:
  - `EISFSectionTaxonomyResponse`, `EISFDocumentRecordResponse`, `EISFFolderNode`, `EISFDocumentDetail`, `EISFDocumentUploadRequest`
  - `Artifact`, `Section`, `Zone`, `TaxonomyCatalog`, `TaxonomyRegistry`
  - `build_catalog`, `get_catalog`, `get_active_catalog`, `register_catalog`, `set_active_version`, `get_registered_versions`, `resolve_artifact`, `validate_hierarchy`, `get_mandatory_artifacts`
  - `DIA_V3_2_0_RAW`, `DIA_V3_2_0_COMPLETE_RAW`, `CADENCE_EXTENSIONS_RAW`, `MILESTONE_MANDATORY_ARTIFACTS`
- **Import Sites**:
  - `apps/etmf/classification_service.py:2` (`from tmf_reference_model import (...)`)
  - `apps/etmf/ingestion_service.py:12` (`from tmf_reference_model import (...)`)
  - `apps/etmf/main.py:22, 2780` (`from tmf_reference_model import (...)`)
  - `apps/etmf/routers/taxonomy.py:3` (`from tmf_reference_model import get_active_catalog, get_catalog`)
  - `apps/etmf/tests/test_etmf.py:898, 1900, 2612` (`from tmf_reference_model import (...)`)
  - `apps/etmf/tests/test_tmf_reference_model.py:3, 204, 237, 288, 308, 360, 392` (`from tmf_reference_model import (...)`)
  - `tests/validation/dia_tmf_validation_suite.py:5` (`from tmf_reference_model import (...)`)
  - `apps/eisf/routers/eisf.py:9` (`from etmf.eisf_transport_models import (...)`)
  - `apps/eisf/tests/test_eisf_adapter.py:281, 320, 353` (`from tmf_reference_model import resolve_artifact`)

---

## 2. Logic Chain

1. **Safety Domain Models**:
   - Observations show `sae_icsr` models are referenced exclusively by `apps/safety/` main application code and test files.
   - Moving `packages/core-models/sae_icsr/` to `apps/safety/src/domain/sae_icsr/` consolidates ownership inside `apps/safety` with zero risk to other services.

2. **CTMS Domain Models**:
   - Observations show `ctms/doa_models.py` and `ctms/doa_transport_models.py` are referenced inside `apps/ctms/routers/doa.py` and `apps/ctms/tests/test_doa_service.py`.
   - Moving these to `apps/ctms/src/domain/` satisfies microservice ownership under `apps/ctms`.
   - CTMS tests that import from `execution.doa_models` will be updated in M3 when `execution` models move to `apps/execution/src/domain/`.

3. **eTMF Domain Models**:
   - Observations show `etmf` models and `tmf_reference_model` catalog code are primarily consumed by `apps/etmf/` and `tests/validation/dia_tmf_validation_suite.py`.
   - Cross-service consumers (`apps/eisf/routers/eisf.py` and `apps/eisf/tests/test_eisf_adapter.py`) must update their imports to point to `apps.etmf.src.domain.etmf` and `apps.etmf.src.domain.tmf_reference_model`.
   - Internal imports within `tmf_reference_model/__init__.py` must be converted from `from tmf_reference_model.models import ...` to relative import `from .models import ...`.

---

## 3. Caveats

- `apps/eisf` is a separate service folder in `apps/`. Its direct imports of `etmf` transport models and `tmf_reference_model` functions represent cross-service dependencies that should be refactored to `apps.etmf.src.domain` in M2, and subsequently reviewed for ACL DTOs in M4 if strict decoupling is required between `eisf` and `etmf`.
- Inline function-level imports (e.g. `apps/etmf/main.py:2780`, `apps/etmf/tests/test_etmf.py:898`, `apps/eisf/tests/test_eisf_adapter.py:281`) must be updated in place during implementation to prevent missing deferred imports at runtime.

---

## 4. Conclusion

All files, classes, functions, and import sites for Safety, CTMS DOA, and eTMF domain models are fully mapped. Relocating these files to `apps/safety/src/domain/`, `apps/ctms/src/domain/`, and `apps/etmf/src/domain/` is clean, actionable, and ready for worker implementation.

---

## 5. Verification Method

To verify these findings independently:
1. Inspect the detailed analysis report at `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m2_2/analysis.md`.
2. Confirm source file contents in `packages/core-models/sae_icsr/`, `packages/core-models/ctms/`, `packages/core-models/etmf/`, and `packages/core-models/tmf_reference_model/`.
3. Verify test pass status prior to migration using `uv run pytest apps/safety apps/ctms apps/etmf apps/eisf tests/validation/dia_tmf_validation_suite.py`.
