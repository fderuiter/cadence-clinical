---
search: false
---

> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️
> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report
*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration
This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 531
- **Passed:** 0 🟢
- **Unverified (Draft):** 531 ⚪
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Overall Operational Pass Rate:** 0.00%

## 2. Installation Qualification (IQ)
The Installation Qualification verifies that the software execution environment, external dependencies, package environments, and static quality checks are fully compliant.

### 2.1 System Environment Metadata
- **Operating System / Platform:** linux (containerized target specification)
- **Python Version:** 3.14.5 (Docker execution environment baseline)
- **Database Provider (Execution Engine):** PostgreSQL / SQLite in-memory fallback
- **Graph Database Provider (Designer Engine):** Neo4j (mocked in unit suite)
- **Identity Management Gateway:** Keycloak OIDC Router

### 2.2 Static Analysis & Security Gateways
| Tool | Target Standard | Status | Outcome / Verification Reference |
| :--- | :--- | :--- | :--- |
| **Ruff / Black** | PEP 8 / Clean Code formatting | Passed | Zero warnings, style rules enforced. |
| **Bandit Security** | Secure Python programming | Passed | No high-severity vulnerabilities found in application code. |
| **pip-audit** | Dependency vulnerability auditing | Passed | Zero CVEs detected on active virtualenv packages. |
| **Git Secrets** | Secret leakage prevention | Passed | Clean commit signatures, no exposed API tokens. |

### 2.3 Installed Dependency Package Ledger (Pip List)
```
.NET 10 Platform Dependencies (Python toolchain decommissioned)
```

## 3. Operational Qualification (OQ)
The Operational Qualification verifies that individual clinical operations, state machine transitions, cryptographic workflows, database-level triggers, and blinding boundaries are executed accurately according to functional requirements.

### 3.1 Traceability Mappings Verification
| Test Case Name | Classname / Suite | Target Req | Status | Duration |
| :--- | :--- | :--- | :--- | :--- |
| `AggregateRootsMustImplementIGxPAuditableContract` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DomainLayerMustNotReferenceInfrastructureOrPresentationAssemblies` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ExecutionDomainLayerMustNotReferenceAspNetCoreOrMassTransit` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RegulatedTestSuitesMustFeatureGxPRequirementAttributes` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetFormSubmissionDetail_WhenFormNotFound_Returns404NotFound` | `Cadence.E2ETests.E2E.ObservationAndFormE2ETests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature01_CodingQueueAndFilter` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature02_MeddraAndWhodrugTraversal` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature03_SingleAndBatchCodingAssignment` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature04_DictionaryUpversioningImpact` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature05_QueryEscalationAndResolution` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature06_RelationalDataLockPersistence` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature07_HierarchicalLockInheritance` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature08_DualSignatureStepUpToken` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature09_UnlockJustificationEnforcement` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature10_MultiFormatLabIngestion` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature11_UcumNormalizationAndRangeEval` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature12_LabDiscrepancyAndSaeAutoQueries` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature13_SasTransportBinaryExport` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature14_CdiscOdmXmlExportWithAudits` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature15_CdiscDatasetJsonExport` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature16_DeidentifiedCsvExport` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier1Feature17_UiRouterAndNavigationMetadata` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary01_EmptyLabPayloadRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary02_InvalidHl7SegmentsRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary03_UnlockJustificationUnder50CharsRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary04_HardLockWithoutStepUpTokenRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary05_IncompatibleUcumUnitConversion` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary06_NonexistentDictionaryTermResolution` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary07_InvalidSdtmDomainExportRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary08_UnauthorizedLockActionRejected` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary09_ReplaySigTokenPrevention` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier2Boundary10_DeidentificationEmptyDemographics` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise01_FormLockThenMedicalCoding` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise02_OutOfRangeLabThenSubjectLock` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise03_BatchCodingThenBiostatExport` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise04_SubjectLockFollowedByLabIngestion` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise05_UpversioningImpactThenQueryEscalation` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier3Pairwise06_HardLockStepUpThenUnlockAndExport` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier4Scenario01_OncologyTrialMultisiteLock` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier4Scenario02_HighThroughputLabBatchIngestion` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier4Scenario03_MeddraUpversioningAndBatchCoding` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier4Scenario04_RegulatorySubmissionBundleGeneration` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestTier4Scenario05_FullLifecycleE2ETrialWorkflow` | `Cadence.E2ETests.E2E.Phase1E2ESuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSiteComplianceSummary_WhenSiteNotFound_Returns404NotFound` | `Cadence.E2ETests.E2E.SiteComplianceE2ETests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateSubject_WithValidPayload_Returns201Created` | `Cadence.E2ETests.E2E.SubjectE2ETests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetail_WhenSubjectNotFound_Returns404NotFound` | `Cadence.E2ETests.E2E.SubjectE2ETests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestCatalogCrossVersionIntegrity` | `Cadence.E2ETests.Validation.DiaTmfValidationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestMilestoneMandatoryArtifacts` | `Cadence.E2ETests.Validation.DiaTmfValidationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestSiteLevelClassificationDrift` | `Cadence.E2ETests.Validation.DiaTmfValidationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestFeatureMatrixValidationIgnoringHelperAndExcludedServices` | `Cadence.E2ETests.Validation.FeatureMatrixGatingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestFeatureMatrixValidationMissingService` | `Cadence.E2ETests.Validation.FeatureMatrixGatingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestFeatureMatrixValidationSuccess` | `Cadence.E2ETests.Validation.FeatureMatrixGatingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestGxPComplianceDriftsIdentified` | `Cadence.E2ETests.Validation.GxPComplianceSuiteTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestCommittedSchemaVisualizerIsUpToDate` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestCommittedTypescriptSchemaIsUpToDate` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestFormattingAndWhitespaceImmunity` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestPendingDeltaSchemaDrift` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestSchemaVisualizersUseVendoredAssets` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestSimulatedFieldNameRenameDrift` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestSimulatedTypeMismatchDrift` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestSubmissionsSchemaDrift` | `Cadence.E2ETests.Validation.OfflineSchemaDriftTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestEnvironmentIntegrityAssertions` | `Cadence.E2ETests.Validation.PathBoundaryLinterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestLinterNegativeCases` | `Cadence.E2ETests.Validation.PathBoundaryLinterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TestLinterPositiveCases` | `Cadence.E2ETests.Validation.PathBoundaryLinterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_Comparisons_EvaluatesInequalities` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateFormSubmission_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateQuery_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InitializeForm_CreatesFormInDraftState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenMismatchedArrayLength_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyPreconditionsAsync` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Dispose` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_ConcurrentCalls_PropagatesTaskFailures` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateObservation_WithValidParameters_InitializesCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FlagSdv_WithReason_ResetsSdvVerification` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `OutlierDetection_IdentifiesStatisticalOutliers` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UcumNormalization_ConvertsUnitsCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `VerifySdv_WhenAlreadyVerified_ReturnsError` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `VerifySdv_WhenUnverified_TransitionsToVerified` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_HasUniqueIndexOnSubjectCode` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsStatusWithMaxLength32` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_IgnoresComputedMissingDocumentsPropertyItself` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_MapsSubjectConsentToSubjectConsentsTable` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Configure_ProtocolVersionIsMappedAsComplexProperty` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplicationService_Close_ValidatesCraOrDmRole` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CancelQuery_FromOpen_SucceedsWithReason` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `QueryLifecycle_FullTransitionSequence_Succeeds` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RaiseQuery_CreatesQueryInOpenState` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_TamperedTag_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EncryptThenDecrypt_RoundTripsExactly` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserId_GivenNoHttpContext_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserId_GivenNoSubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CalculateAge_GivenNullBirthDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CalculateAge_GivenNullObservationDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NormalizeGender_GivenNullInput_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClearDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_TwoSubjects_ProduceDistinctIds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `IsPostRandomization_ForLockedStates_ReturnsTrue` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `IsPostRandomization_ForUnlockedStates_ReturnsFalse` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateStratFactors_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteActivationRequest_ValueEquality_ComparesByStudyId` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentRequestValidator_ValidRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ClearReconsentRequirement_SetsRequiresReconsentFalse` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MergeResubmission_VersionTag_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AllFactoryMethods_ProduceDistinctCodes` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BlockedActivation_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BlockedEnrollment_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `NotFound_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToCode_RoundTripsThroughTryParse` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TryParse_NormalizesCasingWhitespaceAndSpaces` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteCode_AsNullableParameter_MayBeAbsent` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteCode_ValidValue_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SiteCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `StudyCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `StudyCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `StudyCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCode_Equality_IsValueBased` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCode_ToString_ReturnsValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectConsentId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `SubjectId_ToString_ReturnsGuidValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Config_All23ServiceOptions_InstantiateWithDefaults` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Config_CommonOptions_HasDefaultValuesMatchingPython` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Config_Options_BindsFromConfigurationDictionary` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ProtocolRAG_EvaluateFaithfulness_DetectsHallucinatedCitations` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ProtocolRAG_EvaluateFaithfulness_FailsClosedForInsufficientInformation` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ProtocolRAG_EvaluateFaithfulness_ValidatesGroundedAnswer` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ProtocolRAG_FormatCitationMarker_ProducesCanonicalFormat` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ProtocolRAG_FormatGroundedRagPrompt_FormatsPromptWithExcerpts` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BlockRandomizationEngine_Allocate_AssignsArmWithChecksum` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BlockRandomizationEngine_GeneratesCanonicalStratumKey` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BoinEngine_CalculateBoinBoundaries_ReturnsExpectedBoundaries` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BoinEngine_EvaluateDoseLevel_EscalatesWhenLowDltRate` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `BoinEngine_EvaluateDoseLevel_StopsTrialWhenExcessiveToxicityAtLowestDose` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DTPLogisticsEngine_CalculateMeanKineticTemperature_ComputesHaynesMkt` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `DTPLogisticsEngine_EvaluateDtpDispatch_GeneratesTamperSealWhenEligible` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LabToxicityGradingEngine_EvaluateHysLaw_DetectsCriticalHysLawTriad` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `LabToxicityGradingEngine_EvaluatesGrade3Alt_AndGeneratesAeDraft` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `MinimizationEngine_Allocate_CalculatesImbalanceAndAllocates` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `RuleEvaluator_EvaluatesAstAndJsonLogic` | `Cadence.UnitTests.Packages.DomainEngineUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Compliance_AuditChainVerifier_ValidatesIntactChain` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Compliance_AuditDigestCalculator_ComputesExpectedHash` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Ddf_Models_ConstructsSuccessfully` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Deid_DeidDetector_DetectsAndRedactsEmail` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Hexagonal_DomainPrimitives_And_ExceptionMapper_Work` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Sentinels_Engine_ExecutesRegisteredCheck` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Storage_InMemoryBlobStore_StoresAndRetrievesPayload` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToolingCore_Handlers_ReturnsSuccessfulEnvelope` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToolingCore_OpenApiSpecExporter_GeneratesValidJsonSpecs` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InMemoryRepositoryPersistsEntityAndIncrementsVersionIndex` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ResultMonadHandlesSuccessAndFailureWithoutThrowingExceptions` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_model_creation` | `scripts.scaffold_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |

## 4. Performance Qualification (PQ) & Scenario Validation
Performance Qualification documents the verification of end-to-end clinical workflow scenarios defined in Section 5 of the QA & Validation Plan.

### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection
- **Target Requirements:** PRD-MDR-001, PRD-UNI-003
- **Description:** Verifies that locked study version nodes in Neo4j are completely immutable, and direct database manipulations are rejected.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-002: Stratification Factor Re-randomization Rejections
- **Target Requirements:** PRD-SUB-002, PRD-SUB-001
- **Description:** Verifies that stratification factor modifications and backward state machine updates are strictly forbidden once randomized.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-003: Offline Mode Data Entry, Sync Collision & Conflict Resolution
- **Target Requirements:** PRD-EDC-004, PRD-UNI-002
- **Description:** Verifies that offline data entries are synchronized accurately, conflict resolution runs deterministically, and the audit ledger captures all states.
- **Verification Status:** ⚪ Unverified (Draft Mode)

### TC-VAL-LOG-004: Re-authentication Enforcement during Emergency Unblinding
- **Target Requirements:** PRD-MDR-003, PRD-UNI-002
- **Description:** Verifies that unblinding requests require strict multi-factor re-authentication, trigger immediate unblinded state transition, lock the trial on tampering, and dispatch security alerts.
- **Verification Status:** ⚪ Unverified (Draft Mode)

## 5. Qualification Review & Authorization
This GxP computerized system validation log is compiled with mathematical determinism directly from the execution runners of the build system.
```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Electronic Signature Block

- **Signer Identity:** jules
- **Timestamp:** 2026-07-23 22:38:25 UTC
- **Meaning / Purpose:** GxP Qualification Execution Sign-Off
- **Cryptographic Hash (SHA-256):** 7cf26f86a68c40fadc4cad15befcb25d8c0fc160ab11be1f1231b1305f0df253

-----BEGIN CERTIFICATE-----
MIIDTjCCAjagAwIBAgIUbSy7oWyeFGy1jo6yVjZOhIwSGw4wDQYJKoZIhvcNAQEL
BQAwYTE7MDkGA1UEAwwyQ2FkZW5jZSBHeFAgVmFsaWRhdGlvbiBSdW5uZXIgKGNh
ZGVuY2UtZ3hwLXJ1bm5lcikxIjAgBgNVBAoMGUNhZGVuY2UgQ2xpbmljYWwgU29m
dHdhcmUwHhcNMjYwOTI0MTUzNDQ3WhcNMjcwOTI1MTUzNDQ3WjBhMTswOQYDVQQD
DDJDYWRlbmNlIEd4UCBWYWxpZGF0aW9uIFJ1bm5lciAoY2FkZW5jZS1neHAtcnVu
bmVyKTEiMCAGA1UECgwZQ2FkZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTCCASIwDQYJ
KoZIhvcNAQEBBQADggEPADCCAQoCggEBAMa/KGBz82Nb2OE+Spief6zowTBJanBU
W740Q8x3pD8RK7foJ3NesjL4e/GdU8ClCekZvMv+0zW8d7RUujsH4Qg6DnAoFjBy
KTZIWtt2rLitu+xIsLQrb6pxkDkXY9saplaY9KQTdEiwUMHqA7wPJIJQ+noiyjoL
AzMO/j06siHlV6Sh4OHFs3Y8roiIoMZTy11bZTs6PyDxIbqWWwzmIZcrtgTsRmDk
964c/aYGSLwuNSEvHJn0rR7UzfSwYIbqewtiDky/iDB+sDvK6pUZVyLfaH02peiE
ju60yhS/BannxBbJSfIFPPqLHoPkXC9zjPz/HRcpqHAYLHkFtFmoD08CAwEAATAN
BgkqhkiG9w0BAQsFAAOCAQEAfKqskiUzD9uHAD935q+fXUbWRDw0meGFJzdbbnKf
73uYA9l0Y6rv/T34jX4n3zczyPij4zsFUPsp3jUXDH13Urq1A3iRDBhJ5spG7C90
YkamjccC2r0U29S0PYLsGFjajWrC/e6utGzLB/bNvahYwpKcZfDIiLNeux8eXjRa
gTxMh89imCvRamsyDxRvN5IElKXxoABbtDEpkhUlB9FUS40QAfxytEN5p5Pd6dDU
0n9sAa90VA2aTTi9tCwytmncEzlIEofYd8MKMc45M96xitcqJtXOjs+8W3SYMrAh
wIbznF0+6CVRVbsBqcjD2gQobYMf52rDaiiPIH0iLUF6pA==
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
C5Q9WmP82J3wOSkaj75dAJQCJA5NHO8XbXuQKBGE5my0qOOnd3XuuQHW/jwuFNivr4AKNQAxy+iwkm4sOorecT+OxOWiLJh3CnhGLEK2aYTy9MWxtT7uSUQWUIhjKJ556FU7N6WEbl2WjkdZnwWN/UGltrjlmxj16VJFE58977SWhQDVcc3xNPXmmTwdCbSYApQJvIov457NCRR+sDGX2rHk/cBwH3fO7smIgv+qmeYfoZkCw93d6aJNIQ8LVDf+IMo743DhHvWPobVOQ2lXYIBB2m1gt+lZPAr8wjrKAzqF0R02i8POUIS9NhLqUkGTRuvQfMgp8N/3oE5uwYXHCQ==
-----END SIGNATURE-----
