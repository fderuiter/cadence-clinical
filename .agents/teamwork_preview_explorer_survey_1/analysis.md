# Architectural Survey & Domain Ownership Analysis: `packages/core-models`

## Executive Summary

This report provides a comprehensive survey of `packages/core-models` across all 66 Python source files, mapping every class, Pydantic model, enum, function, and utility. It defines the canonical domain ownership, target paths under `apps/<service>/src/domain/` (or `packages/<package>/src/domain/`), and the Anti-Corruption Layer (ACL) DTO strategy for shared domain contracts across the Cadence Clinical Research Software Platform.

---

## 1. Master Inventory & Domain Ownership Mapping

The table below catalogs all 66 files in `packages/core-models`, detailing their contained classes/functions, primary domain purpose, canonical owning microservice/package, and recommended target path.

| Core-Models Module Path | Symbols Defined (Classes / Models / Enums / Functions) | Primary Domain Purpose | Owning Microservice / Package | Target Path (`src/domain/`) |
| :--- | :--- | :--- | :--- | :--- |
| `audit.py` | `Part11AuditMixin`, `AuditFields` | 21 CFR Part 11 mandatory GxP audit metadata mixins (`created_at`, `created_by`, `reason_for_change`, `version_index`). | `packages/database` | `packages/database/src/domain/audit.py` |
| `datetime_helpers.py` | `validate_timezone_aware_datetime`, `serialize_utc_z` | Timezone-aware datetime validation & UTC Z ISO-8601 serialization helpers. | `packages/security` | `packages/security/src/domain/datetime_helpers.py` |
| `document_renderer.py` | `ProtocolDocumentRenderer` | PDF and DOCX document compilation & rendering pipeline. | `apps/designer` | `apps/designer/src/domain/rendering/document_renderer.py` |
| `signature.py` | `SigningReason`, `ApprovalStatus`, `SignatureManifestation` | 21 CFR Part 11 electronic signature manifestation and reason enums. | `packages/security` | `packages/security/src/domain/signature.py` |
| `sync_engine.py` | `SignatureValidationError`, `SyncMetadata`, `SyncRecord`, `normalize_to_utc`, `get_signature_payload`, `verify_record_signature`, `reconcile_records` | Offline record HMAC-SHA256 signature verification & reconciliation engine. | `apps/ctms` | `apps/ctms/src/domain/sync/sync_engine.py` |
| `usdm_ingestion.py` | `ValidationIssue`, `USDMValidationReport`, `FieldReference`, `ExpressionNode`, `extract_field_references`, `detect_circular_dependencies`, `safe_parse_payload`, `resolve_usdm_version`, `normalize_usdm_payload`, `traverse_rules_in_payload`, `detect_stochastic_operators`, `validate_usdm_payload` | CDISC USDM payload validation, rule traversal, and version normalization. | `apps/designer` | `apps/designer/src/domain/usdm/usdm_ingestion.py` |
| `watermark.py` | `apply_watermark` | GxP status watermarking for generated protocol/document PDFs. | `packages/storage` | `packages/storage/src/domain/watermark.py` |
| `cdisc/branch_models.py` | `ProtocolBranch`, `BlockDiff`, `AmendmentComparisonResponse` | Protocol branching, version diffing, and amendment comparison. | `apps/designer` | `apps/designer/src/domain/branching/models.py` |
| `cdisc/cascade_models.py` | `CascadedFormTemplate`, `CascadeSummaryReport` | Downstream artifact cascade to eCRF forms and SoA matrix. | `apps/designer` | `apps/designer/src/domain/cascade/models.py` |
| `cdisc/cdisc_library_client.py` | `CdiscLibraryConfig`, `CdiscProductSummary`, `CdashDomainDefinition`, `SdtmDomainDefinition`, `CodelistTerm`, `CodelistDefinition`, `CdiscLibraryClient` | CDISC Library REST API client & local codelist fallback reader. | `apps/gateway` | `apps/gateway/src/domain/cdisc/client.py` |
| `cdisc/sentinel_models.py` | `QualityRuleFinding`, `ReadabilityReport`, `BurdenTraceItem`, `BurdenTraceReport`, `AmendmentImpactReport`, `AttritionStep`, `FeasibilityReport`, `ProtocolQualityScore` | Quality Sentinel rule findings, readability, patient burden, feasibility. | `apps/designer` | `apps/designer/src/domain/sentinel/models.py` |
| `cdisc/terminology_cache.py` | `CdiscTerminologyCache` | Local SQLite cache for CDISC controlled terminology. | `apps/gateway` | `apps/gateway/src/domain/cdisc/terminology_cache.py` |
| `cdisc/usdm_importer.py` | `USDMImportResult`, `USDMImporter` | USDM JSON parser and Study Designer Neo4j graph importer. | `apps/designer` | `apps/designer/src/domain/importers/usdm_importer.py` |
| `cdisc/usdm_models.py` | `Code`, `SyntaxTemplate`, `EligibilityCriterion`, `Activity`, `Encounter`, `StudyArm`, `StudyEpoch`, `StudyDesign`, `USDMStudy` | CDISC USDM v2.0/v3.0 core study architecture data models. | `apps/designer` | `apps/designer/src/domain/usdm/models.py` |
| `cdisc/usdm_transport_models.py` | `UsdmImportRequest`, `UsdmImportResponse`, `UsdmExportResponse` | USDM API request and response schemas. | `apps/gateway` | `apps/gateway/src/domain/usdm/schemas.py` |
| `ctms/doa_models.py` | `SiteStaffMemberCreate`, `SiteStaffMemberResponse`, `DOADelegationRecordCreate`, `DOADelegationRecordResponse` | Site staffing and Delegation of Authority (DOA) log domain models. | `apps/ctms` | `apps/ctms/src/domain/doa/models.py` |
| `ctms/doa_transport_models.py` | `DelegationTaskRequest`, `DOALogResponse`, `RevokeDelegationRequest`, `DOASignOffRequest` | CTMS DOA administration REST API transport schemas. | `apps/ctms` | `apps/ctms/src/domain/doa/schemas.py` |
| `designer/synopsis_transport_models.py` | `SynopsisExportRequest`, `SynopsisExportResponse` | Protocol synopsis export API schemas. | `apps/designer` | `apps/designer/src/domain/synopsis/schemas.py` |
| `eligibility/evaluator.py` | `evaluate_node`, `evaluate_eligibility`, `evaluate_structured_expression`, `evaluate_criteria_group` | Deterministic AST evaluator and aggregate eligibility calculation engine. | `apps/designer` | `apps/designer/src/domain/eligibility/evaluator.py` |
| `eligibility/models.py` | `ComparisonOperator`, `LogicalOperator`, `FieldReference`, `ExpressionNode`, `EligibilityCriterion`, `NodeEvaluation`, `CriterionEvaluation`, `AggregateEligibilityResult` | Shared AST, eligibility criteria, and evaluation domain models. | `apps/designer` | `apps/designer/src/domain/eligibility/models.py` |
| `eligibility/parser.py` | `Token`, `DSLParser`, `tokenize`, `parse_dsl` | Infix clinical DSL parser converting text criteria to AST nodes. | `apps/designer` | `apps/designer/src/domain/eligibility/parser.py` |
| `etmf/eisf_models.py` | `EISFSectionTaxonomyResponse`, `EISFDocumentRecordResponse` | eISF regulatory binder document taxonomy response schemas. | `apps/eisf` | `apps/eisf/src/domain/models.py` |
| `etmf/eisf_transport_models.py` | `EISFFolderNode`, `EISFDocumentDetail`, `EISFDocumentUploadRequest` | eISF regulatory binder folder tree and upload transport schemas. | `apps/eisf` | `apps/eisf/src/domain/schemas.py` |
| `execution/doa_models.py` | `DOATaskRoleEnum`, `DOATaskDelegationEnum`, `DOAAssignmentRecord` | EDC execution DOA site staffing and delegation assignment models. | `apps/execution` | `apps/execution/src/domain/doa/models.py` |
| `execution/econsent_models.py` | `EConsentSignRequest`, `EConsentSignResponse` | eConsent signature capture request/response schemas. | `apps/execution` | `apps/execution/src/domain/econsent/schemas.py` |
| `execution/eisf_models.py` | `EISFTaxonomyCategoryEnum`, `EISFDocumentRecord` | Site-level investigator site file document metadata in EDC. | `apps/execution` | `apps/execution/src/domain/eisf/models.py` |
| `execution/epro_transport_models.py` | `InstrumentCreate`, `InstrumentResponse`, `SubjectAssignmentCreate`, `SubjectAssignmentResponse`, `AssignmentComplianceDetail`, `SubjectComplianceResponse` | ePRO instrument and subject compliance transport schemas. | `apps/execution` | `apps/execution/src/domain/epro/schemas.py` |
| `execution/lab_models.py` | `LabSourceEnum`, `SexApplicability`, `LabTestMasterRecord`, `LabUnitConversionRecord` | Central laboratory master record and unit conversion domain models. | `apps/execution` | `apps/execution/src/domain/lab/models.py` |
| `execution/lab_transport_models.py` | `LabReferenceRangeCreate`, `LabReferenceRangeUpdate`, `LabReferenceRangeResponse`, `LabTestMasterCreate`, `LabTestMasterResponse`, `LabUnitConversionCreate`, `LabUnitConversionResponse`, `LabRangeRecalculateRequest`, `LabRangeRecalculateResponse` | Laboratory reference range & master catalog REST schemas. | `apps/execution` | `apps/execution/src/domain/lab/schemas.py` |
| `execution/lock_models.py` | `LockScopeEnum`, `LockStatusEnum`, `DataLockRecord`, `DataUnlockRecord` | Granular data locking (form, item-group, field) domain models. | `apps/execution` | `apps/execution/src/domain/locking/models.py` |
| `execution/lock_transport_models.py` | `DataLockRequest`, `DataLockResponse` | Granular data locking REST API schemas. | `apps/execution` | `apps/execution/src/domain/locking/schemas.py` |
| `execution/offline_models.py` | `OfflineDeltaItem`, `OfflineBatchSyncRequest`, `OfflineBatchSyncResponse`, `ConflictStrategyEnum`, `EPROOfflineMarker`, `OfflineSyncMarkers`, `EPROOfflineEntry`, `EPROBulkSyncRequest`, `EPROSubmitResponse`, `EPROBulkSyncResponse`, `EPROPersistedEntryResponse`, `EPROSubmissionRequest`, `EPROSubmissionResponse`, `SubjectNotificationResponse`, `AcknowledgeNotificationRequest`, `EPROScheduleItemResponse`, `EPRODiaryFormDefinitionResponse` | Offline sync batch delta ingestion, conflict resolution, ePRO queue schemas. | `apps/execution` | `apps/execution/src/domain/offline/models.py` |
| `execution/safety_models.py` | `SeriousnessCriteriaEnum`, `CausalityEnum`, `SAECaseRecord` | Serious Adverse Event (SAE) EDC case record models. | `apps/execution` | `apps/execution/src/domain/safety/models.py` |
| `execution/safety_transport_models.py` | `SafetyDispatchRequest`, `SafetyDispatchResponse`, `SAEReconcileRequest` | Safety Gateway dispatch & SAE reconciliation REST schemas. | `apps/execution` | `apps/execution/src/domain/safety/schemas.py` |
| `execution/sdv_transport_models.py` | `BulkSdvSignOffRequest`, `BulkSdvSignOffResponse`, `QueryTargetDescriptor`, `BulkQueryGenerationRequest`, `BulkQueryGenerationResponse`, `SdvFlagSeverity`, `FlagTargetDescriptor`, `SdvFlagRequest`, `SdvResolveRequest`, `SdvFlagResponse`, `SdvResolveResponse`, `generate_audit_tx`, `generate_identifier` | Source Data Verification (SDV) bulk sign-off & query schemas. | `apps/execution` | `apps/execution/src/domain/sdv/schemas.py` |
| `execution/signature_transport_models.py` | `BatchSignatureRequest`, `BatchSignatureResponse` | Batch eSignature execution API schemas. | `apps/execution` | `apps/execution/src/domain/signature/schemas.py` |
| `localization/models.py` | `validate_language_code` | BCP-47 language tag validation helper. | `apps/econsent` | `apps/econsent/src/domain/localization.py` |
| `notifications/event_models.py` | `SystemDomainEvent`, `NotificationDispatchJob` | System domain event wrapper & notification job schemas. | `apps/notifications` | `apps/notifications/src/domain/event_models.py` |
| `organization_domain/models.py` | `OrganizationType`, `ClinicalStaffRole`, `TrialDuty` | Organization Directory enums and trial duties. | `apps/org` | `apps/org/src/domain/models.py` |
| `protocol_authoring/models.py` | `BlockType`, `ProtocolBlock`, `NarrativeBlock`, `ObjectiveBlock`, `EligibilityBlock`, `SoADerivedBlock`, `ICHSection`, `SectionReviewStatus`, `Comment`, `CommentThread`, `SuggestionStatus`, `Suggestion`, `SectionReviewTransition`, `build_canonical_ich_skeleton` | ICH M11 protocol blocks, comments, suggestions, section reviews. | `apps/designer` | `apps/designer/src/domain/protocol_authoring/models.py` |
| `protocol_authoring/soa.py` | `StudyArm`, `Epoch`, `Visit`, `Procedure`, `TimingWindow`, `StudyArmProperties`, `EpochProperties`, `VisitProperties`, `ProcedureProperties`, `TimingWindowProperties`, `CreateStudyArmRequest`, `UpdateStudyArmRequest`, `CreateEpochRequest`, `UpdateEpochRequest`, `CreateVisitRequest`, `UpdateVisitRequest`, `CreateProcedureRequest`, `UpdateProcedureRequest`, `CreateTimingWindowRequest`, `UpdateTimingWindowRequest`, `LinkEpochVisitRequest`, `LinkVisitProcedureRequest`, `LinkTimingRequest`, `LinkArmApplicabilityRequest`, `SoALinkResponse`, `SoAEntityCreatedResponse`, `SoAEntityDetail`, `AuditMetadata`, `ProjectionCell`, `SoAMatrixProjectionResponse`, `VisitReorderItem`, `VisitReorderRequest`, `ActivityAssignmentRequest`, `ArmReorderItem`, `ArmReorderRequest`, `EpochReorderItem`, `EpochReorderRequest`, `ProcedureReorderItem`, `ProcedureReorderRequest`, `VisitToArmAssignmentRequest`, `VisitToEpochAssignmentRequest` | Schedule of Activities (SoA) matrix, arms, epochs, visits, procedures, timing. | `apps/designer` | `apps/designer/src/domain/soa/models.py` |
| `protocol_render/models.py` | `ExportMetadata`, `NarrativeItemView`, `NarrativeSectionView`, `SynopsisView`, `SoAHeaderArm`, `SoAHeaderEpoch`, `SoAHeaderEncounter`, `SoACellView`, `SoARowView`, `SoAMatrixView`, `RenderedProtocolDocument` | Protocol document rendering views, synopsis views, SoA views. | `apps/designer` | `apps/designer/src/domain/protocol_render/models.py` |
| `protocol_version_ref/models.py` | `ProtocolVersionStatus`, `ProtocolVersionRef` | Protocol version status and reference metadata. | `apps/designer` | `apps/designer/src/domain/protocol_version/models.py` |
| `sae_icsr/models.py` | `VersionedModel`, `MedDRACoding`, `SeriousAdverseEvent`, `ICSRHeader`, `ICSRReportIdentifiers`, `ICSRPatient`, `ICSRReactionEvent`, `ICSRSuspectDrug`, `IndividualCaseSafetyReport`, `validate_dtc_format`, `normalize_severity_val`, `normalize_seriousness_val` | ICH E2B(R3) Individual Case Safety Report (ICSR) & MedDRA models. | `apps/safety` | `apps/safety/src/domain/icsr/models.py` |
| `sdtm/dataset_json_models.py` | `DatasetJsonItemDef`, `DatasetJsonItemGroup`, `DatasetJsonPayload` | CDISC Dataset-JSON v1.0 payload schemas. | `apps/execution` | `apps/execution/src/domain/sdtm/dataset_json_models.py` |
| `sdtm/enums.py` | `SDTMDomain`, `Sex`, `Race`, `AESeverity`, `AESeriousness`, `AERelationship`, `AEOutcome`, `NullFlavor` | SDTM controlled terminology and domain enums. | `apps/execution` | `apps/execution/src/domain/sdtm/enums.py` |
| `sdtm/models.py` | `AuditableModel`, `DM`, `AE`, `VS`, `LB`, `CM`, `SUPPQUAL`, `validate_dtc_format` | SDTM core domain models (DM, AE, VS, LB, CM, SUPPQUAL). | `apps/execution` | `apps/execution/src/domain/sdtm/models.py` |
| `sdtm/scrubber_models.py` | `DeidentConfig`, `DeidentSummary` | De-identification scrubber configuration and summary models. | `apps/execution` | `apps/execution/src/domain/sdtm/scrubber_models.py` |
| `sdtm/sdtm_models.py` | `SDTMRecordDM`, `SDTMRecordAE`, `SDTMRecordVS`, `SDTMRecordLB`, `SDTMRecordSV`, `SDTMRecordCM`, `SDTMRecordDS`, `SDTMRecordMH` | SDTM mapped record models. | `apps/execution` | `apps/execution/src/domain/sdtm/sdtm_models.py` |
| `sdtm/terminology.py` | `normalize_sex`, `normalize_race`, `normalize_severity`, `normalize_seriousness` | CDISC terminology normalization & validation functions. | `apps/execution` | `apps/execution/src/domain/sdtm/terminology.py` |
| `storage/document_models.py` | `DocumentMetadataResponse`, `DocumentUploadResponse`, `ArchiveJobResponse` | Document storage metadata & archive response schemas. | `packages/storage` | `packages/storage/src/domain/document_models.py` |
| `tmf_reference_model/__init__.py` | `TaxonomyRegistry`, `build_catalog`, `get_catalog`, `get_active_catalog`, `register_catalog`, `set_active_version`, `get_registered_versions`, `resolve_artifact`, `validate_hierarchy`, `get_mandatory_artifacts` | TMF Reference Model v3.2 taxonomy registry functions. | `apps/etmf` | `apps/etmf/src/domain/tmf_reference_model/registry.py` |
| `tmf_reference_model/models.py` | `Artifact`, `Section`, `Zone`, `TaxonomyCatalog` | TMF Reference Model v3.2 Artifact, Section, Zone models. | `apps/etmf` | `apps/etmf/src/domain/tmf_reference_model/models.py` |

---

## 2. Shared Domain Models & Anti-Corruption Layer (ACL) Strategy

When `packages/core-models` is deleted, microservices MUST NOT directly import domain models from sibling services. Instead, each consuming service must implement a local Anti-Corruption Layer (ACL) using local Pydantic DTOs for deserializing HTTP responses from owner REST API endpoints.

Below is the mapping of shared domain entities and their ACL DTO specifications:

### 1. Protocol Version Reference (`ProtocolVersionRef`, `ProtocolVersionStatus`)
- **Canonical Owner**: `apps/designer` (`apps/designer/src/domain/protocol_version/models.py`)
- **Consuming Services**: `apps/etmf`, `apps/execution`
- **ACL Implementation**:
  - `apps/etmf`: Define `apps/etmf/src/domain/acl/protocol_version_dto.py`:
    ```python
    from pydantic import BaseModel, Field

    class ETMFProtocolVersionRefDTO(BaseModel):
        protocol_id: str
        version_tag: str
        status: str = Field(..., description="DRAFT, IN_REVIEW, APPROVED, AMENDED, RETIRED")
    ```
  - `apps/execution`: Define `apps/execution/src/domain/acl/protocol_version_dto.py`:
    ```python
    from pydantic import BaseModel

    class ExecutionProtocolVersionRefDTO(BaseModel):
        protocol_id: str
        version_tag: str
        status: str
    ```

### 2. Delegation of Authority (DOA) Log (`DOADelegationRecord`, `DOATaskRoleEnum`)
- **Canonical Owner**: `apps/ctms` (`apps/ctms/src/domain/doa/models.py`)
- **Consuming Services**: `apps/execution` (for eSignature & SDV authorization checks)
- **ACL Implementation**:
  - `apps/execution`: Define `apps/execution/src/domain/acl/ctms_doa_dto.py`:
    ```python
    from pydantic import BaseModel

    class ExecutionDOADelegationDTO(BaseModel):
        site_id: str
        staff_member_id: str
        role: str
        granted_tasks: list[str]
        is_active: bool
    ```

### 3. USDM Study Specification (`USDMStudy`, `EligibilityCriterion`)
- **Canonical Owner**: `apps/designer` (`apps/designer/src/domain/usdm/models.py`)
- **Consuming Services**: `apps/execution` (for USDM to eCRF translation), `apps/gateway` (for import/export API proxying)
- **ACL Implementation**:
  - `apps/execution`: Define `apps/execution/src/domain/acl/usdm_dto.py` to deserialize incoming study design payloads during eCRF generation without importing Neo4j/Designer internal models.

### 4. Safety & SAE Reporting (`SAECaseRecord`, `IndividualCaseSafetyReport`)
- **Canonical Owner**: `apps/safety` (`apps/safety/src/domain/icsr/models.py`)
- **Consuming Services**: `apps/execution` (safety case dispatch & SAE reconciliation)
- **ACL Implementation**:
  - `apps/execution`: Define `apps/execution/src/domain/acl/safety_dto.py`:
    ```python
    from pydantic import BaseModel

    class ExecutionSAEReconcileDTO(BaseModel):
        case_id: str
        subject_id: str
        serious_adverse_event_term: str
        reconciliation_status: str
    ```

### 5. Organization Directory (`OrganizationType`, `ClinicalStaffRole`, `TrialDuty`)
- **Canonical Owner**: `apps/org` (`apps/org/src/domain/models.py`)
- **Consuming Services**: `packages/security`, `apps/ctms`
- **ACL Implementation**:
  - `packages/security`: Define local string enums or DTOs in `packages/security/src/domain/acl/org_dto.py`.

---

## 3. Infrastructure & Pipeline Configuration Updates

Eradicating `packages/core-models` requires updating the following tooling & configuration files:

1. **`pyproject.toml`**:
   - Remove `packages-core-models = { workspace = true }` under `[tool.uv.sources]`.
   - Remove `"packages/core-models/sdtm/dataset_json_models.py" = ["N815"]` under `[tool.ruff.lint.per-file-ignores]`.

2. **`scripts/validate_schemas.py`**:
   - Update line 40: Remove `"core-models"` from `for name in ["core-models", "database", "deid", "security", "ui"]:` loop.

3. **`scripts/detect_duplication.py`**:
   - Clean up lines 252-254 (`packages/core-models/audit.py`, `packages/core-models/sdtm/models.py`) and lines 270-272 (`packages/core-models/sdtm/models.py`, `packages/core-models/sdtm/sdtm_models.py`) from the hardcoded whitelist `ignored` sets.
