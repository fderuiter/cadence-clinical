# Handoff Report — Milestone M3 (Execution Service Domain Migration)

## 1. Observation
- **Explored Source Paths**:
  - `packages/core-models/execution/`: 13 Python files (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`).
  - `packages/core-models/sdtm/`: 7 Python files (`__init__.py`, `dataset_json_models.py`, `enums.py`, `models.py`, `scrubber_models.py`, `sdtm_models.py`, `terminology.py`).
  - `packages/core-models/localization/`: 2 Python files (`__init__.py`, `models.py`).
  - `packages/core-models/watermark.py`: 1 Python file.
- **Destination Path**:
  - `apps/execution/src/domain/` currently exists and houses 20 files/subdirectories, including domain entities (`ClinicalSubjectDomain`, `ConsentSignatureDomain`, `ConsentFormRecordDomain`, `AuditLogDomain`, `ExecutionStaffEntity`, `ExecutionDelegationEntity`, `ExecutionAuditLogEntity`), exceptions (`exceptions.py`), repositories (`repositories.py`), as well as existing models and submodules (`sdtm/`, `localization/`, `watermark.py`).
- **Verbatim Model & Class Catalog**:
  - `doa_models.py`: `DOATaskRoleEnum`, `DOATaskDelegationEnum`, `DOAAssignmentRecord`.
  - `econsent_models.py`: `EConsentSignRequest`, `EConsentSignResponse`.
  - `eisf_models.py`: `EISFTaxonomyCategoryEnum`, `EISFDocumentRecord`.
  - `epro_transport_models.py`: `InstrumentCreate`, `InstrumentResponse`, `SubjectAssignmentCreate`, `SubjectAssignmentResponse`, `AssignmentComplianceDetail`, `SubjectComplianceResponse`.
  - `lab_models.py`: `LabSourceEnum`, `SexApplicability`, `LabTestMasterRecord`, `LabUnitConversionRecord`.
  - `lab_transport_models.py`: `LabReferenceRangeCreate`, `LabReferenceRangeUpdate`, `LabReferenceRangeResponse`, `LabTestMasterCreate`, `LabTestMasterResponse`, `LabUnitConversionCreate`, `LabUnitConversionResponse`, `LabRangeRecalculateRequest`, `LabRangeRecalculateResponse`.
  - `lock_models.py`: `LockScopeEnum`, `LockStatusEnum`, `DataLockRecord`, `DataUnlockRecord`.
  - `lock_transport_models.py`: `DataLockRequest`, `DataLockResponse`.
  - `offline_models.py`: `JsonValue`, `EPROSubmissionStatus`, `OfflineDeltaItem`, `OfflineBatchSyncRequest`, `OfflineBatchSyncResponse`, `ConflictStrategyEnum`, `EPROOfflineMarker`, `OfflineSyncMarkers`, `EPROOfflineEntry`, `EPROBulkSyncRequest`, `EPROSubmitResponse`, `EPROBulkSyncResponse`, `EPROPersistedEntryResponse`, `EPROSubmissionRequest`, `EPROSubmissionResponse`, `SubjectNotificationResponse`, `AcknowledgeNotificationRequest`, `EPROScheduleItemResponse`, `EPRODiaryFormDefinitionResponse`.
  - `safety_models.py`: `SeriousnessCriteriaEnum`, `CausalityEnum`, `SAECaseRecord`.
  - `safety_transport_models.py`: `SafetyDispatchRequest`, `SafetyDispatchResponse`, `SAEReconcileRequest`.
  - `sdv_transport_models.py`: `generate_audit_tx`, `generate_identifier`, `BulkSdvSignOffRequest`, `BulkSdvSignOffResponse`, `QueryTargetDescriptor`, `BulkQueryGenerationRequest`, `BulkQueryGenerationResponse`, `SdvFlagSeverity`, `FlagTargetDescriptor`, `SdvFlagRequest`, `SdvResolveRequest`, `SdvFlagResponse`, `SdvResolveResponse`.
  - `signature_transport_models.py`: `BatchSignatureRequest`, `BatchSignatureResponse`.
  - `sdtm/`: `SDTMDomain`, `Sex`, `Race`, `AESeverity`, `AESeriousness`, `AERelationship`, `AEOutcome`, `NullFlavor`, `validate_dtc_format`, `AuditableModel`, `DM` (`Demographics`), `AE` (`AdverseEvent`), `VS` (`VitalSign`), `LB` (`Laboratory`), `CM` (`ConcomitantMedication`), `SUPPQUAL` (`SUPPQUALRecord`), `SDTMRecordDM`, `SDTMRecordAE`, `SDTMRecordVS`, `SDTMRecordLB`, `SDTMRecordSV`, `SDTMRecordCM`, `SDTMRecordDS`, `SDTMRecordMH`, `DatasetJsonItemDef`, `DatasetJsonItemGroup`, `DatasetJsonPayload`, `DeidentConfig`, `DeidentSummary`, `normalize_sex`, `normalize_race`, `normalize_severity`, `normalize_seriousness`.
  - `localization/`: `SUPPORTED_LANGUAGE_CODES`, `validate_language_code`.
  - `watermark.py`: `apply_watermark`.
- **Target Rules & Formatting Standards (AGENTS.md & PROJECT.md)**:
  - Data models & CDISC schemas owned by Execution Service must reside under `apps/execution/src/domain/`.
  - Import ordering must strictly follow Ruff I001 (isort-style alphabetical ordering).
  - Cross-service database and domain model imports across microservices are prohibited; cross-service data flow must go through ACL DTOs in `apps/<service>/src/domain/acl/`.

---

## 2. Logic Chain
1. **Observation 1**: The user request and `PROJECT.md` require eradicating `packages/core-models` by moving execution domain models (offline models, ePRO, safety, SDTM, trial lock, etc.) into `apps/execution/src/domain/`.
2. **Observation 2**: Inspection of `packages/core-models/` shows 13 execution files under `packages/core-models/execution/`, 7 SDTM files under `packages/core-models/sdtm/`, 2 localization files under `packages/core-models/localization/`, and 1 watermark file (`watermark.py`).
3. **Observation 3**: Inspection of `apps/execution/src/domain/` shows that `apps/execution/src/domain/` is already structured with matching filenames (`doa_models.py`, `econsent_models.py`, `eisf_models.py`, `epro_transport_models.py`, `lab_models.py`, `lab_transport_models.py`, `lock_models.py`, `lock_transport_models.py`, `offline_models.py`, `safety_models.py`, `safety_transport_models.py`, `sdv_transport_models.py`, `signature_transport_models.py`, `sdtm/`, `localization/`, `watermark.py`).
4. **Deduction**: The relocation map requires removing `packages/core-models/execution/`, `packages/core-models/sdtm/`, `packages/core-models/localization/`, and `packages/core-models/watermark.py` and redirecting all repository imports from `packages.core_models.execution...` / `execution...` / `sdtm...` / `localization...` / `watermark...` to `apps.execution.src.domain...`.

---

## 3. Caveats
- No code modifications were performed in `packages/` or `apps/` during this read-only exploration task.
- When implementers execute file removal from `packages/core-models/`, internal imports inside `apps/execution/src/domain/` (e.g. `lab_transport_models.py` importing `execution.lab_models`) must be updated to reference `apps.execution.src.domain.lab_models` to prevent broken module imports.

---

## 4. Conclusion
The technical investigation for Milestone M3 Execution Service Domain Model Migration is complete:
- 13 execution model files, 7 SDTM files, 2 localization files, and 1 watermark file have been completely cataloged with all defined classes, enums, schemas, and helper functions.
- The exact file relocation map from `packages/core-models/...` to `apps/execution/src/domain/...` has been documented in `analysis.md` and this `handoff.md`.
- `apps/execution/src/domain/` is properly structured and ready to serve as the single source of truth for all execution domain models once `packages/core-models` references are updated.

---

## 5. Verification Method
1. Inspect `analysis.md` in `/Users/fred/Code/cadence-clinical/.agents/teamwork_preview_explorer_m3_1/analysis.md` to confirm the model catalog and relocation table.
2. Verify file presence in `packages/core-models/execution/` vs `apps/execution/src/domain/` using:
   ```bash
   find packages/core-models/execution -type f
   find apps/execution/src/domain -type f
   ```
