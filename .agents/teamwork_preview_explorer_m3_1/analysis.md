# Execution Domain Model Analysis — Milestone M3

## 1. Overview
This document provides a comprehensive technical investigation of all domain models currently located in `packages/core-models/execution/`, `packages/core-models/sdtm/`, `packages/core-models/localization/`, and `packages/core-models/watermark.py`, and details their required relocation to `apps/execution/src/domain/`.

---

## 2. Directory & Model Breakdown

### 2.1 Core Execution Models (`packages/core-models/execution/`)

#### 1. `doa_models.py`
- **Path**: `packages/core-models/execution/doa_models.py`
- **Target**: `apps/execution/src/domain/doa_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `DOATaskRoleEnum(StrEnum)`: Site personnel roles (`PRINCIPAL_INVESTIGATOR`, `SUB_INVESTIGATOR`, `CLINICAL_RESEARCH_COORDINATOR`, `STUDY_NURSE`, `DATA_MANAGER`).
  - `DOATaskDelegationEnum(StrEnum)`: Study tasks (`SUBJECT_INFORMED_CONSENT`, `PHYSICAL_EXAMINATION`, `AE_SAE_REPORTING`, `CRF_DATA_ENTRY`, `PI_CASEBOOK_SIGNOFF`).
  - `DOAAssignmentRecord(BaseModel)`: Delegation of Authority assignment record (`record_id`, `study_id`, `site_id`, `personnel_name`, `personnel_email`, `role`, `delegated_tasks`, `start_date`, `end_date`, `is_active`, `signed_off`).

#### 2. `econsent_models.py`
- **Path**: `packages/core-models/execution/econsent_models.py`
- **Target**: `apps/execution/src/domain/econsent_models.py`
- **Classes/Enums/Schemas**:
  - `EConsentSignRequest(BaseModel)`: Interactive electronic consent sign payload (`subject_id`, `icf_version_id`, `printed_name`, `relationship_to_subject`, `signature_svg`, `otp_auth_code`, `reason_for_change`).
  - `EConsentSignResponse(BaseModel)`: E-consent response payload (`consent_record_id`, `signed_pdf_url`, `signature_timestamp_utc`, `verification_hash`).

#### 3. `eisf_models.py`
- **Path**: `packages/core-models/execution/eisf_models.py`
- **Target**: `apps/execution/src/domain/eisf_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `EISFTaxonomyCategoryEnum(StrEnum)`: DIA eISF regulatory binder taxonomy categories (`INVESTIGATOR_CV`, `MEDICAL_LICENSE`, `PROTOCOL_APPROVAL`, `IRB_IEC_APPROVAL`, `INFORMED_CONSENT`, `FINANCIAL_DISCLOSURE`, `DELEGATION_OF_AUTHORITY`, `SAFETY_REPORT`).
  - `EISFDocumentRecord(BaseModel)`: Regulatory binder document metadata (`document_id`, `study_id`, `site_id`, `category`, `title`, `version`, `file_name`, `file_size_bytes`, `sha256_hash`, `uploaded_by`, `uploaded_at`, `expiration_date`, `is_redacted`).

#### 4. `epro_transport_models.py`
- **Path**: `packages/core-models/execution/epro_transport_models.py`
- **Target**: `apps/execution/src/domain/epro_transport_models.py`
- **Classes/Enums/Schemas**:
  - `InstrumentCreate(BaseModel)`: Questionnaire/diary creation payload (`study_id`, `name`, `description`, `items`, `response_types`, `scoring_metadata`, `reason_for_change`).
  - `InstrumentResponse(BaseModel)`: Instrument response schema (`id`, `name`, `description`, `items`, `response_types`, `scoring_metadata`, `created_at`, `created_by`, `reason_for_change`, `version_index`).
  - `SubjectAssignmentCreate(BaseModel)`: Instrument assignment request (`study_id`, `subject_id`, `instrument_id`, `start_date`, `end_date`, `recurrence_pattern`, `due_at`, `reason_for_change`).
  - `SubjectAssignmentResponse(BaseModel)`: Assignment response schema (`id`, `subject_id`, `instrument_id`, `start_date`, `end_date`, `recurrence_pattern`, `due_at`, `created_at`, `created_by`, `reason_for_change`, `version_index`).
  - `AssignmentComplianceDetail(BaseModel)`: Compliance detail item (`assignment_id`, `instrument_id`, `instrument_name`, `status`, `due_at`, `end_date`, `submitted_at`).
  - `SubjectComplianceResponse(BaseModel)`: Overall compliance metric response (`subject_id`, `compliance_rate`, `completed_count`, `pending_count`, `overdue_count`, `assignments`).

#### 5. `lab_models.py`
- **Path**: `packages/core-models/execution/lab_models.py`
- **Target**: `apps/execution/src/domain/lab_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `LabSourceEnum(StrEnum)`: Testing source (`CENTRAL`, `LOCAL`).
  - `SexApplicability(StrEnum)`: Sex applicability (`M`, `F`, `ALL`).
  - `LabTestMasterRecord(BaseModel)`: Master lab catalog entry (`id`, `study_id`, `test_code`, `test_name`, `default_unit`, `normalized_unit`, `loinc_code`, `created_at`, `created_by`, `reason_for_change`, `version_index`).
  - `LabUnitConversionRecord(BaseModel)`: Unit conversion rule record (`id`, `study_id`, `test_code`, `from_unit`, `to_unit`, `factor`, `offset`, `created_at`, `created_by`, `reason_for_change`, `version_index`).

#### 6. `lab_transport_models.py`
- **Path**: `packages/core-models/execution/lab_transport_models.py`
- **Target**: `apps/execution/src/domain/lab_transport_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `LabReferenceRangeCreate(BaseModel)`
  - `LabReferenceRangeUpdate(BaseModel)`
  - `LabReferenceRangeResponse(BaseModel)`
  - `LabTestMasterCreate(BaseModel)`
  - `LabTestMasterResponse(BaseModel)`
  - `LabUnitConversionCreate(BaseModel)`
  - `LabUnitConversionResponse(BaseModel)`
  - `LabRangeRecalculateRequest(BaseModel)`
  - `LabRangeRecalculateResponse(BaseModel)`

#### 7. `lock_models.py`
- **Path**: `packages/core-models/execution/lock_models.py`
- **Target**: `apps/execution/src/domain/lock_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `LockScopeEnum(StrEnum)`: Lock scope boundaries (`FORM`, `ITEM_GROUP`, `FIELD`).
  - `LockStatusEnum(StrEnum)`: Data lock lifecycle status (`UNLOCKED`, `FROZEN`, `LOCKED`).
  - `DataLockRecord(BaseModel)`: Data lock state record (`lock_id`, `study_id`, `subject_id`, `form_id`, `item_group_id`, `field_name`, `scope`, `status`, `locked_by`, `reason_for_change`, `locked_at`).
  - `DataUnlockRecord(BaseModel)`: Audit record for data unlock overrides (`unlock_id`, `lock_id`, `unlocked_by`, `reason_for_change`, `unlocked_at`).

#### 8. `lock_transport_models.py`
- **Path**: `packages/core-models/execution/lock_transport_models.py`
- **Target**: `apps/execution/src/domain/lock_transport_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `DataLockRequest(BaseModel)`
  - `DataLockResponse(BaseModel)`

#### 9. `offline_models.py`
- **Path**: `packages/core-models/execution/offline_models.py`
- **Target**: `apps/execution/src/domain/offline_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - Type Aliases: `JsonValue`, `EPROSubmissionStatus`
  - `ConflictStrategyEnum(StrEnum)`: Conflict resolution strategy (`CLIENT_WINS`, `SERVER_WINS`, `MERGE`).
  - `OfflineDeltaItem(BaseModel)`
  - `OfflineBatchSyncRequest(BaseModel)`
  - `OfflineBatchSyncResponse(BaseModel)`
  - `EPROOfflineMarker(BaseModel)`
  - `OfflineSyncMarkers(BaseModel)`
  - `EPROOfflineEntry(BaseModel)`
  - `EPROBulkSyncRequest(BaseModel)`
  - `EPROSubmitResponse(BaseModel)`
  - `EPROBulkSyncResponse(BaseModel)`
  - `EPROPersistedEntryResponse(BaseModel)`
  - `EPROSubmissionRequest(BaseModel)`
  - `EPROSubmissionResponse(BaseModel)`
  - `SubjectNotificationResponse(BaseModel)`
  - `AcknowledgeNotificationRequest(BaseModel)`
  - `EPROScheduleItemResponse(BaseModel)`
  - `EPRODiaryFormDefinitionResponse(BaseModel)`

#### 10. `safety_models.py`
- **Path**: `packages/core-models/execution/safety_models.py`
- **Target**: `apps/execution/src/domain/safety_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `SeriousnessCriteriaEnum(StrEnum)`: ICH E2B(R3) seriousness criteria (`DEATH`, `LIFE_THREATENING`, `HOSPITALIZATION`, `DISABILITY`, `CONGENITAL_ANOMALY`, `OTHER_MEDICALLY_IMPORTANT`).
  - `CausalityEnum(StrEnum)`: WHO-UMC / ICH causality categories (`CERTAIN`, `PROBABLE`, `POSSIBLE`, `UNLIKELY`, `UNRELATED`).
  - `SAECaseRecord(BaseModel)`: Serious Adverse Event ICSR case record (`case_id`, `study_id`, `subject_id`, `safety_report_id`, `reaction_pt`, `meddra_code`, `onset_date`, `seriousness_criteria`, `causality`, `expedited_reporting_required`, `parsed_at`).

#### 11. `safety_transport_models.py`
- **Path**: `packages/core-models/execution/safety_transport_models.py`
- **Target**: `apps/execution/src/domain/safety_transport_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `SafetyDispatchRequest(BaseModel)`
  - `SafetyDispatchResponse(BaseModel)`
  - `SAEReconcileRequest(BaseModel)`

#### 12. `sdv_transport_models.py`
- **Path**: `packages/core-models/execution/sdv_transport_models.py`
- **Target**: `apps/execution/src/domain/sdv_transport_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - Helpers: `generate_audit_tx()`, `generate_identifier()`
  - `SdvFlagSeverity(enum.StrEnum)`: SDV flag severity (`MINOR`, `MAJOR`, `CRITICAL`).
  - `BulkSdvSignOffRequest(BaseModel)`
  - `BulkSdvSignOffResponse(BaseModel)`
  - `QueryTargetDescriptor(BaseModel)`
  - `BulkQueryGenerationRequest(BaseModel)`
  - `BulkQueryGenerationResponse(BaseModel)`
  - `FlagTargetDescriptor(BaseModel)`
  - `SdvFlagRequest(BaseModel)`
  - `SdvResolveRequest(BaseModel)`
  - `SdvFlagResponse(BaseModel)`
  - `SdvResolveResponse(BaseModel)`

#### 13. `signature_transport_models.py`
- **Path**: `packages/core-models/execution/signature_transport_models.py`
- **Target**: `apps/execution/src/domain/signature_transport_models.py`
- **Requirement**: `PRD-SYS-001`
- **Classes/Enums/Schemas**:
  - `BatchSignatureRequest(BaseModel)` (with `sync_target_ids` validator)
  - `BatchSignatureResponse(BaseModel)`

---

### 2.2 SDTM Submodule (`packages/core-models/sdtm/`)

#### 14. `sdtm/__init__.py` -> `apps/execution/src/domain/sdtm/__init__.py`
- Re-exports SDTM models, enums, scrubber models, dataset-json models, and terminology functions.

#### 15. `sdtm/enums.py` -> `apps/execution/src/domain/sdtm/enums.py`
- Enums: `SDTMDomain`, `Sex`, `Race`, `AESeverity`, `AESeriousness`, `AERelationship`, `AEOutcome`, `NullFlavor`.

#### 16. `sdtm/models.py` -> `apps/execution/src/domain/sdtm/models.py`
- Function: `validate_dtc_format(val)`
- Models: `AuditableModel`, `DM` (`Demographics`), `AE` (`AdverseEvent`), `VS` (`VitalSign`), `LB` (`Laboratory`), `CM` (`ConcomitantMedication`), `SUPPQUAL` (`SUPPQUALRecord`).

#### 17. `sdtm/sdtm_models.py` -> `apps/execution/src/domain/sdtm/sdtm_models.py`
- Mapped Records: `SDTMRecordDM`, `SDTMRecordAE`, `SDTMRecordVS`, `SDTMRecordLB`, `SDTMRecordSV`, `SDTMRecordCM`, `SDTMRecordDS`, `SDTMRecordMH`.

#### 18. `sdtm/dataset_json_models.py` -> `apps/execution/src/domain/sdtm/dataset_json_models.py`
- CDISC Dataset-JSON Models: `DatasetJsonItemDef`, `DatasetJsonItemGroup`, `DatasetJsonPayload`.

#### 19. `sdtm/scrubber_models.py` -> `apps/execution/src/domain/sdtm/scrubber_models.py`
- De-identification Models: `DeidentConfig`, `DeidentSummary`.

#### 20. `sdtm/terminology.py` -> `apps/execution/src/domain/sdtm/terminology.py`
- Terminology Normalizers: `normalize_sex()`, `normalize_race()`, `normalize_severity()`, `normalize_seriousness()`.

---

### 2.3 Localization Submodule (`packages/core-models/localization/`)

#### 21. `localization/__init__.py` -> `apps/execution/src/domain/localization/__init__.py`
- Package init file.

#### 22. `localization/models.py` -> `apps/execution/src/domain/localization/models.py`
- Language Code Validation: `SUPPORTED_LANGUAGE_CODES`, `validate_language_code()`.

---

### 2.4 Watermark Module (`packages/core-models/watermark.py`)

#### 23. `watermark.py` -> `apps/execution/src/domain/watermark.py`
- Format-agnostic Watermarking Function: `apply_watermark()`.

---

## 3. Relocation Map Summary Table

| # | Source Path (`packages/core-models/...`) | Destination Path (`apps/execution/src/domain/...`) | Primary Purpose |
|---|---|---|---|
| 1 | `execution/doa_models.py` | `apps/execution/src/domain/doa_models.py` | Delegation of Authority (DOA) site personnel & task models |
| 2 | `execution/econsent_models.py` | `apps/execution/src/domain/econsent_models.py` | Electronic Informed Consent sign request/response schemas |
| 3 | `execution/eisf_models.py` | `apps/execution/src/domain/eisf_models.py` | Electronic Investigator Site File DIA taxonomy & document models |
| 4 | `execution/epro_transport_models.py` | `apps/execution/src/domain/epro_transport_models.py` | ePRO/eCOA questionnaires, assignments & compliance schemas |
| 5 | `execution/lab_models.py` | `apps/execution/src/domain/lab_models.py` | Laboratory master catalog & unit conversion domain models |
| 6 | `execution/lab_transport_models.py` | `apps/execution/src/domain/lab_transport_models.py` | Laboratory reference ranges, test master & conversion transport schemas |
| 7 | `execution/lock_models.py` | `apps/execution/src/domain/lock_models.py` | Granular data locking & unlock audit models |
| 8 | `execution/lock_transport_models.py` | `apps/execution/src/domain/lock_transport_models.py` | Form, item-group & field level data lock REST request/response schemas |
| 9 | `execution/offline_models.py` | `apps/execution/src/domain/offline_models.py` | Offline batch delta sync & ePRO offline submission/reconciliation schemas |
| 10 | `execution/safety_models.py` | `apps/execution/src/domain/safety_models.py` | SAE cases, ICH E2B(R3) seriousness criteria & causality models |
| 11 | `execution/safety_transport_models.py` | `apps/execution/src/domain/safety_transport_models.py` | Safety Gateway dispatch & SAE reconciliation transport schemas |
| 12 | `execution/sdv_transport_models.py` | `apps/execution/src/domain/sdv_transport_models.py` | Bulk SDV sign-off, query generation & item-level SDV flag transport schemas |
| 13 | `execution/signature_transport_models.py` | `apps/execution/src/domain/signature_transport_models.py` | 21 CFR Part 11 Principal Investigator batch eSignature schemas |
| 14 | `sdtm/__init__.py` | `apps/execution/src/domain/sdtm/__init__.py` | SDTM package re-exports |
| 15 | `sdtm/enums.py` | `apps/execution/src/domain/sdtm/enums.py` | CDISC SDTM controlled terminology enums |
| 16 | `sdtm/models.py` | `apps/execution/src/domain/sdtm/models.py` | SDTM domain Pydantic models (DM, AE, VS, LB, CM, SUPPQUAL) |
| 17 | `sdtm/sdtm_models.py` | `apps/execution/src/domain/sdtm/sdtm_models.py` | Mapped SDTM record schemas with GxP audit metadata |
| 18 | `sdtm/dataset_json_models.py` | `apps/execution/src/domain/sdtm/dataset_json_models.py` | CDISC Dataset-JSON v1.0 specification models |
| 19 | `sdtm/scrubber_models.py` | `apps/execution/src/domain/sdtm/scrubber_models.py` | De-identification scrubber configuration & summary models |
| 20 | `sdtm/terminology.py` | `apps/execution/src/domain/sdtm/terminology.py` | CDISC controlled terminology normalizers (sex, race, severity, seriousness) |
| 21 | `localization/__init__.py` | `apps/execution/src/domain/localization/__init__.py` | Localization package init |
| 22 | `localization/models.py` | `apps/execution/src/domain/localization/models.py` | ISO 639-1 language code validation constants & functions |
| 23 | `watermark.py` | `apps/execution/src/domain/watermark.py` | Format-agnostic GxP watermarking utility function |

---

## 4. Internal Imports Adjustment Strategy

When finalizing the relocation from `packages/core-models/` to `apps/execution/src/domain/`:
1. Any intra-domain references currently pointing to `execution.<module>` or `sdtm.<module>` (e.g. `from execution.lab_models import LabSourceEnum` inside `lab_transport_models.py`) must be updated to `from apps.execution.src.domain.lab_models import LabSourceEnum`.
2. All imports must adhere to Ruff rule I001 (isort-style alphabetical ordering).
3. No direct database or domain imports from `apps.execution` are allowed in sibling services (`apps/designer`, `apps/ctms`, `apps/safety`, etc.). Cross-service communication must use Anti-Corruption Layer (ACL) DTOs.
