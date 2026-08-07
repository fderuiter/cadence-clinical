# Analysis Report: Safety, CTMS DOA, and eTMF Domain Models Mapping

## 1. Executive Summary & Scope Overview

This investigation maps the source files, model classes, validator functions, and import sites for the following three domain model groups currently residing in `packages/core-models/`:
1. **Safety Domain Models**: `sae_icsr` and ICSR models -> Target: `apps/safety/src/domain/`
2. **CTMS Domain Models**: `ctms` Delegation of Authority (DOA) models -> Target: `apps/ctms/src/domain/`
3. **eTMF Domain Models**: TMF Reference Model and `etmf` models -> Target: `apps/etmf/src/domain/`

All source files are strictly read-only analyzed. Detailed target paths, symbol lists, exact import locations, and potential circular dependency/cross-service conflict risks have been identified to enable seamless migration during Milestone M2.

---

## 2. Safety Domain Models (`sae_icsr` & ICSR)

### 2.1 Source Files in `packages/core-models/`
- `packages/core-models/sae_icsr/__init__.py`
- `packages/core-models/sae_icsr/models.py`

### 2.2 Inventory of Classes, Functions, and Constants Defined
Defined in `packages/core-models/sae_icsr/models.py`:
- `DTC_REGEX`: `re.Pattern` — Regular expression validating ISO 8601 / CDISC DTC date-time format with partial date support.
- `validate_dtc_format(val: str | None) -> str | None`: Helper function validating format of date/time strings against `DTC_REGEX`.
- `normalize_severity_val(val: Any) -> str`: Normalizes severity inputs (`"GRADE 1"`, `"1"`, `"LOW"`, etc.) to `"MILD"`, `"MODERATE"`, or `"SEVERE"`.
- `normalize_seriousness_val(val: Any) -> str`: Normalizes seriousness booleans/strings (`True`, `"YES"`, `"1"`) to `"Y"` or `"N"`.
- `VersionedModel(BaseModel)`: Abstract base model adding GxP versioning metadata (`version_index: int`, `reason_for_change: str | None`) with validation.
- `MedDRACoding(BaseModel)`: MedDRA coding hierarchy representation (`llt_code`, `llt_name`, `pt_code`, `pt_name`, `hlt_code`, `hlt_name`, `hlgt_code`, `hlgt_name`, `soc_code`, `soc_name`, `primary_soc_flag`, `score`).
- `SeriousAdverseEvent(VersionedModel)`: SDTM AE/SAE record model (`subject_key`, `AETERM`, `AESTDTC`, `AEENDTC`, `AESEV`, `AESER`, `AEREL`, `AEOUT`, `AESEQ`, `meddra_coding`).
- `ICSRHeader(BaseModel)`: ICH E2B(R3) safety report transmission header (`sender_organization`, `receiver_organization`, `message_type`, `transmission_date`, `message_id`).
- `ICSRReportIdentifiers(BaseModel)`: Report identification metadata (`worldwide_unique_case_id`, `local_report_id`, `first_sender_type`).
- `ICSRPatient(BaseModel)`: Patient demographics block (`patient_id`, `sex`, `age`, `age_unit`, `birth_date`).
- `ICSRReactionEvent(BaseModel)`: Adverse reaction block (`reaction_term`, `meddra_coding`, `start_date`, `end_date`, `outcome`, `seriousness_death`, `seriousness_life_threatening`, `seriousness_hospitalization`, `seriousness_disability`, `seriousness_congenital_anomaly`, `seriousness_other_medically_important`).
- `ICSRSuspectDrug(BaseModel)`: Suspect drug details (`drug_name`, `active_substance_name`, `dosage_text`, `route_of_administration`, `action_taken_with_drug`, `drug_role`).
- `IndividualCaseSafetyReport(VersionedModel)`: Root ICH E2B(R3) ICSR document model (`header`, `report_identifiers`, `patient`, `reactions`, `suspect_drugs`).

Exported in `packages/core-models/sae_icsr/__init__.py`:
- `MedDRACoding`, `SeriousAdverseEvent`, `ICSRHeader`, `ICSRReportIdentifiers`, `ICSRPatient`, `ICSRReactionEvent`, `ICSRSuspectDrug`, `IndividualCaseSafetyReport`.

### 2.3 Import Site Catalog
- `apps/safety/main.py` (line 10): `from sae_icsr import IndividualCaseSafetyReport`
- `apps/safety/reconciliation.py` (line 7): `from sae_icsr import IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent`
- `apps/safety/renderer.py` (line 4): `from sae_icsr import IndividualCaseSafetyReport`
- `apps/safety/tests/test_e2b.py` (line 2): `from sae_icsr import (ICSRHeader, ICSRPatient, ICSRReactionEvent, ICSRReportIdentifiers, ICSRSuspectDrug, IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent)`
- `apps/safety/tests/test_sae_icsr.py` (line 3): `from sae_icsr import (ICSRHeader, ICSRPatient, ICSRReactionEvent, ICSRReportIdentifiers, ICSRSuspectDrug, IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent)`
- `apps/safety/tests/test_sae_reconciliation.py` (line 8): `from sae_icsr import MedDRACoding, SeriousAdverseEvent`
- `apps/safety/tests/test_safety_e2b.py` (line 2): `from sae_icsr import (ICSRHeader, ICSRPatient, ICSRReactionEvent, ICSRReportIdentifiers, ICSRSuspectDrug, IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent)`
- `packages/core-models/pyproject.toml` (line 24): `"sae_icsr"` package entry.

### 2.4 Target Destination Path
- Package folder: `apps/safety/src/domain/sae_icsr/`
  - `apps/safety/src/domain/sae_icsr/__init__.py`
  - `apps/safety/src/domain/sae_icsr/models.py`
- Target import format: `from apps.safety.src.domain.sae_icsr import IndividualCaseSafetyReport, MedDRACoding, SeriousAdverseEvent`

### 2.5 Conflict and Circular Dependency Risk Assessment
- **Risk Level**: Zero / Minimal.
- **Rationale**: `sae_icsr` models have no dependencies on other core-models packages. All consumer import sites are isolated within the `apps/safety` service directory.

---

## 3. CTMS Domain Models (`ctms` DOA)

### 3.1 Source Files in `packages/core-models/`
- `packages/core-models/ctms/__init__.py`
- `packages/core-models/ctms/doa_models.py`
- `packages/core-models/ctms/doa_transport_models.py`

### 3.2 Inventory of Classes, Functions, and Constants Defined
Defined in `packages/core-models/ctms/doa_models.py`:
- `SiteStaffMemberCreate(BaseModel)`: Creation payload for site staff member (`id`, `site_id`, `user_id`, `first_name`, `last_name`, `email`, `primary_role`, `license_number`, `gcp_certified`, `created_by`, `reason_for_change`).
- `SiteStaffMemberResponse(BaseModel)`: Response payload for site staff member (`id`, `site_id`, `user_id`, `first_name`, `last_name`, `email`, `primary_role`, `license_number`, `gcp_certified`, `created_at`, `created_by`, `reason_for_change`, `version_index`, `is_active`, `is_deleted`).
- `DOADelegationRecordCreate(BaseModel)`: Creation payload for Delegation of Authority log entry (`id`, `site_id`, `staff_user_id`, `task_code`, `start_date`, `end_date`, `status`, `pi_signature_hash`, `pi_approved_at`, `created_by`, `reason_for_change`).
- `DOADelegationRecordResponse(BaseModel)`: Response payload for Delegation of Authority log entry (`id`, `site_id`, `staff_user_id`, `task_code`, `start_date`, `end_date`, `status`, `pi_signature_hash`, `pi_approved_at`, `created_at`, `created_by`, `reason_for_change`, `version_index`, `is_active`, `is_deleted`).

Defined in `packages/core-models/ctms/doa_transport_models.py`:
- `DelegationTaskRequest(BaseModel)`: API request payload to assign trial duties to staff (`site_id`, `staff_user_id`, `task_codes`, `start_date`, `reason_for_change`).
- `DOALogResponse(BaseModel)`: Response payload containing full DOA log matrix for a site (`site_id`, `pi_name`, `delegated_staff`, `audit_history`).
- `RevokeDelegationRequest(BaseModel)`: API request payload to revoke task delegation (`record_id`, `reason_for_change`).
- `DOASignOffRequest(BaseModel)`: API request payload for Principal Investigator eSignature endorsement (`record_id`, `reason_for_change`).

Exported in `packages/core-models/ctms/__init__.py`:
- `SiteStaffMemberCreate`, `SiteStaffMemberResponse`, `DOADelegationRecordCreate`, `DOADelegationRecordResponse`.

### 3.3 Import Site Catalog
- `apps/ctms/routers/doa.py` (line 11): `from ctms.doa_transport_models import (DelegationTaskRequest, DOALogResponse, DOASignOffRequest, RevokeDelegationRequest)`
- `apps/ctms/tests/test_doa_service.py` (line 32): `from ctms.doa_transport_models import (DelegationTaskRequest, DOALogResponse, DOASignOffRequest, RevokeDelegationRequest)`
- `packages/core-models/ctms/__init__.py` (line 3): `from ctms.doa_models import (DOADelegationRecordCreate, DOADelegationRecordResponse, SiteStaffMemberCreate, SiteStaffMemberResponse)`

*Cross-service distinction note*:
`apps/ctms/tests/test_doa_audit_suite.py` (line 6) and `apps/ctms/tests/test_doa_models.py` (line 6) currently import `DOATaskDelegationEnum`, `DOATaskRoleEnum`, `DOAAssignmentRecord` from `execution.doa_models` (which belongs to `execution` service, M3).

### 3.4 Target Destination Path
- Package folder: `apps/ctms/src/domain/doa/` (or directly under `apps/ctms/src/domain/`)
  - `apps/ctms/src/domain/doa_models.py`
  - `apps/ctms/src/domain/doa_transport_models.py`
  - `apps/ctms/src/domain/__init__.py`
- Target import format:
  - `from apps.ctms.src.domain.doa_transport_models import DelegationTaskRequest, DOALogResponse, DOASignOffRequest, RevokeDelegationRequest`
  - `from apps.ctms.src.domain.doa_models import SiteStaffMemberCreate, SiteStaffMemberResponse, DOADelegationRecordCreate, DOADelegationRecordResponse`

### 3.5 Conflict and Circular Dependency Risk Assessment
- **Risk Level**: Low.
- **Rationale**: `ctms/doa_models.py` and `doa_transport_models.py` have zero dependencies on other packages. `apps/ctms` tests that import from `execution.doa_models` test Execution EDC DOA entities; those imports will be updated in Milestone M3 when `execution` domain models move to `apps/execution/src/domain/`.

---

## 4. eTMF Domain Models (TMF Reference Model & `etmf`)

### 4.1 Source Files in `packages/core-models/`
- `packages/core-models/etmf/__init__.py`
- `packages/core-models/etmf/eisf_models.py`
- `packages/core-models/etmf/eisf_transport_models.py`
- `packages/core-models/tmf_reference_model/__init__.py`
- `packages/core-models/tmf_reference_model/models.py`
- `packages/core-models/tmf_reference_model/README.md`

### 4.2 Inventory of Classes, Functions, and Constants Defined
Defined in `packages/core-models/etmf/eisf_models.py`:
- `EISFSectionTaxonomyResponse(BaseModel)`: Pydantic response schema for EISF section taxonomy (`section_code`, `section_number`, `title`, `description`, `is_mandatory`).
- `EISFDocumentRecordResponse(BaseModel)`: Pydantic response schema for document record (`id`, `site_id`, `study_id`, `section_code`, `filename`, `file_path`, `sha256_checksum`, `version_major`, `version_minor`, `status`, `expiration_date`, `created_at`, `created_by`, `reason_for_change`, `version_index`, `is_active`, `is_deleted`).

Defined in `packages/core-models/etmf/eisf_transport_models.py`:
- `EISFFolderNode(BaseModel)`: Tree folder representation (`section_code`, `title`, `document_count`, `subfolders`).
- `EISFDocumentDetail(BaseModel)`: Document detail DTO with streaming download URL (`id`, `site_id`, `section_code`, `filename`, `version`, `expiration_date`, `created_at`, `created_by`, `download_url`).
- `EISFDocumentUploadRequest(BaseModel)`: Document upload request payload (`study_id`, `section_code`, `filename`, `content`, `mime_type`, `reason_for_change`, `expiration_date`).

Defined in `packages/core-models/tmf_reference_model/models.py`:
- `Artifact(BaseModel)`: Immutable Pydantic model for DIA TMF artifact (`code`, `name`, `section_code`, `zone_code`, `is_extension`).
- `Section(BaseModel)`: Immutable section model (`code`, `name`, `zone_code`, `artifacts`).
- `Zone(BaseModel)`: Immutable zone model (`code`, `name`, `sections`).
- `TaxonomyCatalog(BaseModel)`: Immutable taxonomy catalog model (`version`, `zones`, `artifact_map`, `get_artifact()`, `get_section()`, `get_zone()`).

Defined in `packages/core-models/tmf_reference_model/__init__.py`:
- Constants:
  - `DIA_V3_2_0_RAW`: Raw dictionary representation of DIA TMF v3.2.0.
  - `DIA_V3_2_0_COMPLETE_RAW`: Complete raw dictionary of DIA TMF v3.2.0.
  - `CADENCE_EXTENSIONS_RAW`: Dictionary of Cadence custom extensions (e.g. `05.02.98 Medical License`).
  - `MILESTONE_MANDATORY_ARTIFACTS`: Mapping of clinical milestone (`INITIATION`, `CONDUCT`, `CLOSEOUT`) to mandatory artifact codes.
- Classes:
  - `TaxonomyRegistry`: Thread-safe registry maintaining catalog versions (`register_catalog`, `set_active_version`, `get_catalog`, `get_active_catalog`, `get_registered_versions`).
- Functions:
  - `build_catalog(version, raw_data, extensions)`: Constructs a `TaxonomyCatalog`.
  - `get_catalog(version)`: Returns catalog by version name.
  - `get_active_catalog()`: Returns default active catalog.
  - `register_catalog(catalog)`: Registers catalog instance.
  - `set_active_version(version)`: Sets default active version.
  - `get_registered_versions()`: Lists registered version strings.
  - `resolve_artifact(version, code, name)`: Resolves artifact by code/name.
  - `validate_hierarchy(version, zone_code, section_code, artifact_code)`: Validates taxonomy hierarchy.
  - `get_mandatory_artifacts(milestone, version)`: Returns mandatory artifacts for milestone.

### 4.3 Import Site Catalog
- `apps/etmf/classification_service.py` (line 2): `from tmf_reference_model import (Artifact, Section, Zone, get_active_catalog, get_catalog, resolve_artifact, validate_hierarchy)`
- `apps/etmf/ingestion_service.py` (line 12): `from tmf_reference_model import (get_active_catalog, resolve_artifact)`
- `apps/etmf/main.py` (line 22): `from tmf_reference_model import (Artifact, Section, TaxonomyCatalog, Zone, get_active_catalog, get_catalog, get_mandatory_artifacts, resolve_artifact, validate_hierarchy)`
- `apps/etmf/main.py` (line 2780, inline): `from tmf_reference_model import get_active_catalog, resolve_artifact`
- `apps/etmf/routers/taxonomy.py` (line 3): `from tmf_reference_model import get_active_catalog, get_catalog`
- `apps/etmf/tests/test_etmf.py`:
  - Line 898 (inline): `from tmf_reference_model import MILESTONE_MANDATORY_ARTIFACTS`
  - Line 1900 (inline): `from tmf_reference_model import resolve_artifact`
  - Line 2612 (inline): `from tmf_reference_model import get_active_catalog, resolve_artifact`
- `apps/etmf/tests/test_tmf_reference_model.py`:
  - Line 3: `from tmf_reference_model import (Artifact, Section, TaxonomyCatalog, Zone, get_active_catalog, get_catalog, get_registered_versions)`
  - Line 204, 237, 288, 308, 360, 392 (inline): `from tmf_reference_model import ...`
- `tests/validation/dia_tmf_validation_suite.py` (line 5): `from tmf_reference_model import (MILESTONE_MANDATORY_ARTIFACTS, get_active_catalog, get_catalog, get_mandatory_artifacts, resolve_artifact, validate_hierarchy)`
- `apps/eisf/routers/eisf.py` (line 9): `from etmf.eisf_transport_models import (EISFDocumentDetail, EISFDocumentUploadRequest, EISFFolderNode)`
- `apps/eisf/tests/test_eisf_adapter.py` (lines 281, 320, 353, inline): `from tmf_reference_model import resolve_artifact`

### 4.4 Target Destination Path
- eTMF eISF models: `apps/etmf/src/domain/etmf/` (containing `eisf_models.py`, `eisf_transport_models.py`, `__init__.py`)
- TMF Reference Model: `apps/etmf/src/domain/tmf_reference_model/` (containing `__init__.py`, `models.py`, `README.md`)
- Target import format:
  - `from apps.etmf.src.domain.tmf_reference_model import ...`
  - `from apps.etmf.src.domain.etmf import ...` (or `from apps.etmf.src.domain.etmf.eisf_transport_models import ...`)

### 4.5 Conflict and Circular Dependency Risk Assessment
- **Risk Level**: Medium (requires careful handling of cross-service imports and internal relative imports).
- **Key Risks & Mitigation Strategies**:
  1. **Internal Module Import Resolution**: `packages/core-models/tmf_reference_model/__init__.py` currently imports via `from tmf_reference_model.models import ...`. When moved to `apps/etmf/src/domain/tmf_reference_model/__init__.py`, this must be refactored to a relative import (`from .models import ...`) or explicit package path (`from apps.etmf.src.domain.tmf_reference_model.models import ...`).
  2. **Cross-Service Import from `apps/eisf`**: `apps/eisf/routers/eisf.py` and `apps/eisf/tests/test_eisf_adapter.py` import models from `etmf` and `tmf_reference_model`. During M2, these imports must be updated to `from apps.etmf.src.domain.etmf.eisf_transport_models import ...` and `from apps.etmf.src.domain.tmf_reference_model import resolve_artifact`. (In M4, if `apps/eisf` is treated as a distinct microservice, local ACL DTOs can be introduced).
  3. **Global Validation Suite**: `tests/validation/dia_tmf_validation_suite.py` imports `tmf_reference_model`. Updating line 5 to `from apps.etmf.src.domain.tmf_reference_model import ...` will ensure the validation suite passes without needing `packages/core-models`.

---

## 5. Comprehensive Mapping & Import Transformation Matrix

| Source File in `packages/core-models/` | Key Classes / Functions / Symbols Defined | Target Path in `apps/<service>/src/domain/` | Primary Import Sites to Update |
|---|---|---|---|
| `sae_icsr/models.py`<br>`sae_icsr/__init__.py` | `IndividualCaseSafetyReport`, `SeriousAdverseEvent`, `MedDRACoding`, `ICSRHeader`, `ICSRReportIdentifiers`, `ICSRPatient`, `ICSRReactionEvent`, `ICSRSuspectDrug`, `VersionedModel`, `validate_dtc_format`, `normalize_severity_val`, `normalize_seriousness_val` | `apps/safety/src/domain/sae_icsr/` | `apps/safety/main.py`<br>`apps/safety/reconciliation.py`<br>`apps/safety/renderer.py`<br>`apps/safety/tests/test_e2b.py`<br>`apps/safety/tests/test_sae_icsr.py`<br>`apps/safety/tests/test_sae_reconciliation.py`<br>`apps/safety/tests/test_safety_e2b.py` |
| `ctms/doa_models.py`<br>`ctms/doa_transport_models.py`<br>`ctms/__init__.py` | `SiteStaffMemberCreate`, `SiteStaffMemberResponse`, `DOADelegationRecordCreate`, `DOADelegationRecordResponse`, `DelegationTaskRequest`, `DOALogResponse`, `RevokeDelegationRequest`, `DOASignOffRequest` | `apps/ctms/src/domain/` (or `apps/ctms/src/domain/doa/`) | `apps/ctms/routers/doa.py`<br>`apps/ctms/tests/test_doa_service.py` |
| `etmf/eisf_models.py`<br>`etmf/eisf_transport_models.py`<br>`etmf/__init__.py` | `EISFSectionTaxonomyResponse`, `EISFDocumentRecordResponse`, `EISFFolderNode`, `EISFDocumentDetail`, `EISFDocumentUploadRequest` | `apps/etmf/src/domain/etmf/` | `apps/eisf/routers/eisf.py` |
| `tmf_reference_model/models.py`<br>`tmf_reference_model/__init__.py` | `Artifact`, `Section`, `Zone`, `TaxonomyCatalog`, `TaxonomyRegistry`, `build_catalog`, `get_catalog`, `get_active_catalog`, `register_catalog`, `set_active_version`, `get_registered_versions`, `resolve_artifact`, `validate_hierarchy`, `get_mandatory_artifacts`, `MILESTONE_MANDATORY_ARTIFACTS` | `apps/etmf/src/domain/tmf_reference_model/` | `apps/etmf/classification_service.py`<br>`apps/etmf/ingestion_service.py`<br>`apps/etmf/main.py`<br>`apps/etmf/routers/taxonomy.py`<br>`apps/etmf/tests/test_etmf.py`<br>`apps/etmf/tests/test_tmf_reference_model.py`<br>`apps/eisf/tests/test_eisf_adapter.py`<br>`tests/validation/dia_tmf_validation_suite.py` |
