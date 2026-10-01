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

### Validation Result Summary

Counts cover the requirement-tagged test methods in section 3. Whole-suite totals are in the TRX files under
`TestResults/gxp/`, so this report only changes when a tagged test or its result changes.

- **Requirement-Tagged Test Methods:** 171
- **Passed:** 0 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Not Run:** 171
- **Pass Rate (of executed):** 0.00%

## 2. Installation Qualification (IQ)

The Installation Qualification verifies that the software execution environment and static quality checks are fully compliant.

## 3. Operational Qualification (OQ)

Test methods that declare at least one requirement. Theory cases are merged per method. Durations are left
out so the report only changes when results change; they are in the TRX files under `TestResults/gxp/`.

| Test Case Name | Class | Target Req | Status |
| :--- | :--- | :--- | :--- |
| `AuditTrailEntitiesMustNotBeDependentOfCascadeDelete` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ChainWrittenToPostgres_VerifiesWhenReadBack` | `Cadence.IntegrationTests.Execution.AuditChainPostgresRoundTripTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EtmfComplianceUpdate_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SubjectStateChange_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `FormWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ObservationWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `QueryWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SiteComplianceWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SubjectWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveForm_ReplayingAUsedToken_Returns401` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveForm_WithATokenIssuedForSomethingElse_Returns401` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveForm_WithoutESignatureToken_Returns401AndLeavesTheFormUnsigned` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CdiscCtPackageLoadsAsOnePinnedVersionWithAuditedDigest` | `Cadence.IntegrationTests.Kernel.ConceptRegistryPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `IndicatorSet_ReleasedAndPinned_ComputesValuesAndFlagsABreachedLimit` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | ⚪ (UNVERIFIED) UNVERIFIED |
| `StudyPulse_ReadsLiveKernelPopulationsAndCountsOnlyWhatTheReaderMaySee` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ReadNeverReturnsAnotherTenantsActivity` | `Cadence.IntegrationTests.Kernel.KernelActivityPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Scenario3ActOutsideDelegationDeniedAuditedRetroactiveDoaDoesNotValidate` | `Cadence.IntegrationTests.Kernel.KernelAuthorityPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `StudySiteActivationNeedsAnEffectiveVersionOnApprovalEvidence` | `Cadence.IntegrationTests.Kernel.KernelEffectivityPostgresTests` | PRD-CTMS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ChangeStatusRefusesIllegalMovesAndTimes` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EnrollmentsDatabaseGuardsEnforceTheLifecycleForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `DenialIsExplainedAndAuditedWithItsConditions` | `Cadence.IntegrationTests.Kernel.KernelExplanationPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApplicationRoleCannotReadTheVaultTablesAndReachesThemOnlyThroughItsFunctions` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RecordAndRevealDisclosesOnlyAfterAnAuditedRevealAndNoKernelTableHoldsIdentifiers` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RevealByAnAutomationAgentIsDeniedEvenWithTheGrant` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RevealWithoutTheLayersCapabilityIsDeniedAuditedAndDisclosesNothing` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `VaultRecordsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `NavigationNeverCrossesTenants` | `Cadence.IntegrationTests.Kernel.KernelNavigationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ForkKeepsTheParentApprovedNumbersTheDraftAndStoresTheAmendment` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ProtocolVersionsTriggerFreezesApprovedContentForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SearchNeverReturnsAnotherTenantsObjects` | `Cadence.IntegrationTests.Kernel.KernelSearchPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SimulateCannotReachAnotherTenantsVersions` | `Cadence.IntegrationTests.Kernel.KernelSimulationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApplicationRoleIsNotOwnerAndCannotBypassRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CrossTenantForeignKeyAndForeignTenantRowAreRejectedByTheDatabase` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CrossTenantReadReturnsNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EveryKernelTableHasTenantIdAndForcedRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `NoTenantCannotConnectAndSeesNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `PersonIsVisibleOnlyToTenantsWithAMembership` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `PipelineCommandCommitsUnderRowLevelSecurityVisibleToItsTenantOnly` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TenantDirectoryListsIdsOnlyThroughTheDirectoryFunction` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `WorkQueueCannotReachAnotherTenant` | `Cadence.IntegrationTests.Kernel.KernelWorkQueuePostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TraceFromAnotherTenantSeesNoEdges` | `Cadence.IntegrationTests.Kernel.LineagePostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RulePackDraftedAndReleasedAtRuntimeLoadsAndEvaluates` | `Cadence.IntegrationTests.Kernel.RulePackPostgresTests` | PRD-CRF-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RegisterAndApproveStandardVersionAreAuditedAndReadBackExactly` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TerminologyResolutionIsTenantScoped` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `FindingsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RerunAfterMappingFixResolvesTheFindingInsteadOfDuplicatingIt` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChanges_WithAnAuditedChangeAndNoActor_FailsWithoutWriting` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | SRS-EDC-002 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ChangingRoute_WithoutAChangeReason_Returns400` | `Cadence.UnitTests.Execution.Endpoints.LegacyAliasChangeReasonTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ActionWithNoEntityChange_IsWrittenAsAnActionOnlyEntryOnTheSameChain` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ChangeWithoutARecordedAction_IsStillAudited_WithTheEntityStateAsAction` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SubjectUpdate_WritesOneEntryPerChange_AndTheChainVerifies` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verifier_DetectsAFork` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verifier_DetectsATamperedEntry` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verifier_VerifiesLegacyTableNameChainsAlongsideNewOnes_WithoutRewritingThem` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `WriteAsync_DoesNotItselfSave` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_AfterATransientFailure_RetriesAndWritesEachAuditEntryOnce` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TwoConcurrentFormUpdates_OneSucceeds_TheOtherConflicts_AndTheAuditShowsOneChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenSequentialSavesWithIdenticalTimestamps_ProducesValidMerkleChain` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SaveChanges_Synchronously_WithAnAuditedChange_FailsInsteadOfSkippingTheAudit` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RunAs_OverridesTheAmbientCaller` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `RunAs_WithNoHttpContext_AttributesToTheServiceAccount` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ServiceAccount_WithoutANamedSvcId_IsRejected` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UserId_GivenEmptySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UserId_GivenNoHttpContext_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UserId_GivenNoSubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UserId_GivenWhitespaceOnlySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HostStartup_WithoutAValidDemographicsKey_FailsFast` | `Cadence.UnitTests.Execution.Security.DemographicsKeyStartupValidationTests` | PRD-SYS-190 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `DeleteDemographicsAsync_OnSuccess_WritesDeleteDemographicsAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseAndStoredStatusAreEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HostStartup_OutsideDevelopment_WithoutOidcAudience_FailsFast` | `Cadence.UnitTests.Gateway.GatewayAudienceTests` | PRD-SYS-190 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CallerOverTheLimit_Gets429WithRetryAfter_WhileOtherCallersAreServed` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HealthProbes_AreNeverRateLimited` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EveryCluster_HasTimeoutAndActiveAndPassiveHealthChecks` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ServiceThatIsNotReady_Gets503WithoutForwarding_UntilItIsReadyAgain` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ServiceThatStopsResponding_Gets504AfterTheProxyTimeout` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EveryRoute_LeadsToMappedEndpoints_AndEveryServiceApiIsRouted` | `Cadence.UnitTests.Gateway.GatewayRouteCoverageTests` | PRD-SYS-191 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HostStartup_OutsideDevelopment_WithoutServiceUrls_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HostStartup_WithARelativeServiceUrl_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | ⚪ (UNVERIFIED) UNVERIFIED |
| `KeycloakVerifier_MapsIdentityProviderAnswers` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `KeycloakVerifier_UsesADirectGrantThenEndsThatSession` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_ForAnAuditor_Returns403WithoutCheckingCredentials` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_NamingAnotherUser_Returns403` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_RepeatedAttempts_AreRateLimitedPerSigner` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_WithAWrongPassword_Returns401WithoutAToken` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_WithTheCallersPassword_IssuesAVerifiableSingleUseToken` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_WithoutABoundAction_Returns400` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SignatureVerification_WithoutSignIn_Returns401` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TokenEndpoint_IsDerivedFromTheOidcSettings` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EveryRead_IsRefused_WhenTheSignedTenantIsNotAKernelTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Navigation_ReadsInTheCallersTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EnrollmentTransitionsFollowTheLifecycle` | `Cadence.UnitTests.Kernel.EnrollmentTests` | PRD-SUB-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `KernelDbContextTenantScopedEntitiesUseCompositeTenantForeignKeys` | `Cadence.UnitTests.Kernel.KernelDbContextModelTests` | PRD-CRF-013 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ForkLeavesTheParentApprovedAndStartsAnAmendedDraft` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | ⚪ (UNVERIFIED) UNVERIFIED |
| `LifecycleAllowsOnlyTheSpecifiedTransitions` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | ⚪ (UNVERIFIED) UNVERIFIED |
| `SubmitForReviewSetsTheContentHashReturnToDraftClearsIt` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | ⚪ (UNVERIFIED) UNVERIFIED |
| `EvaluatorIsDeterministicOverGeneratedPacksAndRecords` | `Cadence.UnitTests.Kernel.Rules.RulePackTests` | PRD-CRF-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Map_GlucoseInConventionalUnits_ConvertsToSi` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-LBM-001 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Map_SystolicBloodPressure_WritesOriginalAndStandardResults` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-CDR-002 | ⚪ (UNVERIFIED) UNVERIFIED |
| `LedgerGeneratedHistoriesResolveExactlyWhenTheLatestRunMissesTheFinding` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `LedgerRerunWithoutDetectionResolvesWithTheRunAsEvidence` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `PreTool_RefusesDestructiveCommands` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | ⚪ (UNVERIFIED) UNVERIFIED |
| `PreTool_RefusesPayloads` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ForwardedHttps_FromATrustedProxyOnly_GetsHsts` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `HttpsResponses_CarryHsts_OutsideDevelopment` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TrustedProxies_AcceptAddressesAndCidrRanges` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `TrustedProxies_WithAnInvalidEntry_Throw` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Issue_ForAnAuditor_IsRefused` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Issue_WritesTheV3ContractClaims_WithASixtySecondLifetime` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verify_AcceptsABoundTokenOnce` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verify_ExpiredToken_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verify_TokenSignedWithAnotherSecret_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verify_TokensOutsideTheV3Contract_AreRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Verify_WithAnotherSignerActionOrBatch_IsRejectedWithoutConsumingTheToken` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | ⚪ (UNVERIFIED) UNVERIFIED |
| `ApproveDsur_AppliesPart11SignatureBlock_AndLocksRecord` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-008 | ⚪ (UNVERIFIED) UNVERIFIED |
| `CompileDsur_CalculatesPatientYearsExposureAndMedDraCrossTabulation` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `Endpoints_CompileAndApprove_ReturnCreatedAndOkWithValidSignature` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `UpdateDsur_GivenLockedRecord_Returns409Conflict` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | ⚪ (UNVERIFIED) UNVERIFIED |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | PRD-SYS-001 | ⚪ (UNVERIFIED) UNVERIFIED |

## 4. Performance Qualification (PQ) & Scenario Validation

Qualified Requirements: 0 of 195

## 5. Qualification Review & Authorization

```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Integrity Stamp

- **Status:** Unsigned working copy, generated by `dotnet cadence gxp sync`. Not an electronic signature.
- **Content Hash (SHA-256):** 8d12d6e7e2183fb2231541b14ba82492ee2b3e4adc9615b6936e7f71e807420f
