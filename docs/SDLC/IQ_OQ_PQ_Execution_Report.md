---
search: false
---

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.

### Validation Result Summary

- **Automated Test Methods Reported:** 827
- **Passed:** 827 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Overall Operational Pass Rate (of executed):** 100.00%

## 2. Installation Qualification (IQ)

The Installation Qualification verifies that the software execution environment and static quality checks are fully compliant.

## 3. Operational Qualification (OQ)

Test methods that declare at least one requirement. Theory cases are merged per method. Durations are left
out so the report only changes when results change; they are in the TRX files under `TestResults/gxp/`.

| Test Case Name | Class | Target Req | Status |
| :--- | :--- | :--- | :--- |
| `AuditTrailEntitiesMustNotBeDependentOfCascadeDelete` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | PRD-SYS-001 | 🟢 PASSED |
| `CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChanges_WithAnAuditedChangeAndNoActor_FailsWithoutWriting` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | SRS-EDC-002 | 🟢 PASSED |
| `FormWrites_AreEachAuditedAgainstTheCaller` | `Cadence.UnitTests.Execution.Endpoints.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `ObservationWrites_AreEachAuditedAgainstTheCaller` | `Cadence.UnitTests.Execution.Endpoints.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `QueryWrites_AreEachAuditedAgainstTheCaller` | `Cadence.UnitTests.Execution.Endpoints.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `SiteComplianceWrites_AreEachAuditedAgainstTheCaller` | `Cadence.UnitTests.Execution.Endpoints.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `SubjectWrites_AreEachAuditedAgainstTheCaller` | `Cadence.UnitTests.Execution.Endpoints.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | SRS-EDC-003 | 🟢 PASSED |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `ChainWrittenToPostgres_VerifiesWhenReadBack` | `Cadence.UnitTests.Execution.Integration.AuditChainPostgresRoundTripTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActionWithNoEntityChange_IsWrittenAsAnActionOnlyEntryOnTheSameChain` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `ChangeWithoutARecordedAction_IsStillAudited_WithTheEntityStateAsAction` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `SubjectUpdate_WritesOneEntryPerChange_AndTheChainVerifies` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `Verifier_DetectsAFork` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `Verifier_DetectsATamperedEntry` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `Verifier_VerifiesLegacyTableNameChainsAlongsideNewOnes_WithoutRewritingThem` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `WriteAsync_DoesNotItselfSave` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_AfterATransientFailure_RetriesAndWritesEachAuditEntryOnce` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | 🟢 PASSED |
| `TwoConcurrentFormUpdates_OneSucceeds_TheOtherConflicts_AndTheAuditShowsOneChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSequentialSavesWithIdenticalTimestamps_ProducesValidMerkleChain` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChanges_Synchronously_WithAnAuditedChange_FailsInsteadOfSkippingTheAudit` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `RunAs_OverridesTheAmbientCaller` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `RunAs_WithNoHttpContext_AttributesToTheServiceAccount` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `ServiceAccount_WithoutANamedSvcId_IsRejected` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenEmptySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenNoHttpContext_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenNoSubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenWhitespaceOnlySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `ClearDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Create_TwoSubjects_ProduceDistinctIds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `IsPostRandomization_ForLockedStates_ReturnsTrue` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `IsPostRandomization_ForUnlockedStates_ReturnsFalse` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `UpdateDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `UpdateStratFactors_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | PRD-SUB-001 | 🟢 PASSED |
| `DeleteDemographicsAsync_OnSuccess_WritesDeleteDemographicsAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | PRD-SYS-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseAndStoredStatusAreEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `ToCode_RoundTripsThroughTryParse` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | PRD-SUB-001 | 🟢 PASSED |
| `TryParse_NormalizesCasingWhitespaceAndSpaces` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | PRD-SUB-001 | 🟢 PASSED |
| `TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `CdiscCtPackageLoadsAsOnePinnedVersionWithAuditedDigest` | `Cadence.UnitTests.Kernel.Integration.ConceptRegistryPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `NavigationNeverCrossesTenants` | `Cadence.UnitTests.Kernel.Integration.KernelNavigationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `ApplicationRoleIsNotOwnerAndCannotBypassRowLevelSecurity` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `CrossTenantForeignKeyAndForeignTenantRowAreRejectedByTheDatabase` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `CrossTenantReadReturnsNothing` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `EveryKernelTableHasTenantIdAndForcedRowLevelSecurity` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `NoTenantCannotConnectAndSeesNothing` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `PersonIsVisibleOnlyToTenantsWithAMembership` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `PipelineCommandCommitsUnderRowLevelSecurityVisibleToItsTenantOnly` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `TenantDirectoryListsIdsOnlyThroughTheDirectoryFunction` | `Cadence.UnitTests.Kernel.Integration.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `KernelDbContextTenantScopedEntitiesUseCompositeTenantForeignKeys` | `Cadence.UnitTests.Kernel.KernelDbContextModelTests` | PRD-CRF-013 | 🟢 PASSED |
| `ProtocolRagService_EvaluateFaithfulness_MatchesProtocolRAGEvaluator` | `Cadence.UnitTests.Packages.ComplianceUnitTests` | PRD-SYS-051, PRD-TCK-005 | 🟢 PASSED |
| `ProtocolRAG_EvaluateFaithfulness_DetectsHallucinatedCitations` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | PRD-SYS-051, PRD-TCK-005 | 🟢 PASSED |
| `ProtocolRAG_EvaluateFaithfulness_FailsClosedForInsufficientInformation` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | PRD-SYS-051, PRD-TCK-005 | 🟢 PASSED |
| `ProtocolRAG_EvaluateFaithfulness_ValidatesGroundedAnswer` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | PRD-SYS-051, PRD-TCK-005 | 🟢 PASSED |
| `ProtocolRAG_FormatCitationMarker_ProducesCanonicalFormat` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | PRD-TCK-005 | 🟢 PASSED |
| `ProtocolRAG_FormatGroundedRagPrompt_FormatsPromptWithExcerpts` | `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests` | PRD-TCK-005 | 🟢 PASSED |
| `UsdmCompilerCompileFromProtocolVersionSucceeds` | `Cadence.UnitTests.Packages.DdfCompilerTests` | PRD-DDF-001 | 🟢 PASSED |
| `UsdmCompilerCompileUsdmGeneratesCompleteArtifacts` | `Cadence.UnitTests.Packages.DdfCompilerTests` | PRD-DDF-001 | 🟢 PASSED |
| `UsdmCompilerCompileWithAccessibilityValidationProducesReport` | `Cadence.UnitTests.Packages.DdfCompilerTests` | PRD-DDF-001 | 🟢 PASSED |
| `DeidAirGapVault_DeidentifiesAndRehydratesTextStructuredDataAndChatMessages` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DeidAirGapVault_IDisposable_PurgesInMemoryMappings` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DeidTransforms_AppliesMaskPseudonymizeDateShiftAndAgeCap` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DeidTransforms_ScrubErrorMessage_StripsIdentifiersAndQuotedValues` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DeidTransforms_ShiftDateString_PreservesFormats` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DeidTransforms_SubjectDateShift_And_CapAge` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-SYS-051 | 🟢 PASSED |
| `DicomDeidentifier_DeidentifiesDicomStream_StripsHeaderTags_ShiftsDates_MasksPixels_ReturnsSignedCertificate` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-IMG-001 | 🟢 PASSED |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | PRD-SYS-001 | 🟢 PASSED |

## 4. Performance Qualification (PQ) & Scenario Validation

Qualified Requirements: 7 of 192

## 5. Qualification Review & Authorization

```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Electronic Signature Block

- **Signer Identity:** cadence-validation-runner
- **Timestamp:** 2026-07-23 22:38:25 UTC
- **Meaning / Purpose:** GxP Qualification Execution Sign-Off
- **Cryptographic Hash (SHA-256):** 2583f3e56f54fc89ab59658417d1e466efc7df71ab9460e9a83bd69fdad649d6
-----BEGIN CERTIFICATE-----
MIIDxDCCAnygAwIBAgIIBmWeY8nAu60wPQYJKoZIhvcNAQEKMDCgDTALBglghkgB
ZQMEAgGhGjAYBgkqhkiG9w0BAQgwCwYJYIZIAWUDBAIBogMCASAwaDEiMCAGA1UE
ChMZQ2FkZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTFCMEAGA1UEAxM5Q2FkZW5jZSBH
eFAgVmFsaWRhdGlvbiBSdW5uZXIgKGNhZGVuY2UtdmFsaWRhdGlvbi1ydW5uZXIp
MB4XDTI2MDkyOTAxMzYzMVoXDTI3MDkzMDAxMzYzMVowaDEiMCAGA1UEChMZQ2Fk
ZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTFCMEAGA1UEAxM5Q2FkZW5jZSBHeFAgVmFs
aWRhdGlvbiBSdW5uZXIgKGNhZGVuY2UtdmFsaWRhdGlvbi1ydW5uZXIpMIIBIjAN
BgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0lPadgs51WbtySAKt21oKpwQE9gI
1vAh0/hkpbi8acxy0q5vX9x9wF5SwLjR1dMWrJj2jaeXVtM7zt2dgb53g5YLRBzZ
KGEB8jeHTMy0m1IQgKuVl3gtQgW+B2oyVQYLHJxGYed2CZMBtP0dxyG0uzpx9v04
xOYVaDka7pq16YcZULkUyzLe7+WmeLu0Id2wEo5TdykSHrxbUrfUlqQRPfojOneF
L32Jy91iJXZ3sRciJ7LGGbju7jQMjLy7FsjaSG7NL9hYYunmqpkjlzmFA5GalT18
e6AYP5TmFty+cquuD9JaNNkD2X9OB7gGLmxx+sFxB/mj8lfCMuupak0I9QIDAQAB
oxIwEDAOBgNVHQ8BAf8EBAMCBsAwPQYJKoZIhvcNAQEKMDCgDTALBglghkgBZQME
AgGhGjAYBgkqhkiG9w0BAQgwCwYJYIZIAWUDBAIBogMCASADggEBAHBawasF6V6/
yMCjaZie7EkrqAPnM4BCS+jvtrrY4jZWAR6IKJVbnzf4b34pM/49YX9jWX3UnT7C
Rtxw7JDOPIQ/iBwPdUbLHB6ZWvqgE4OSr0GDirrdsNa3FKp18GK6Uns7KR4C88qO
4Y2iqGgBEWqOVp4OBcxKYD38GGHoTdiI4fh94J2hSq0LO/knBXr9TMTOg3FplGCo
1tqVBBKSD0iYXWw2nbgWs9OdjmL/zsxQj6LHdOOT0YqO7cZ+w5xQcgRQODt1hdrC
OAPdyy+hsyX9iA03xqkdeZgWCfJQfzSKmoNncvt/pWlObImyByfvs2pq55afkNXr
8bSZwqDSP4E=
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
R9TR09LMy3sSLqqYWyQHjTH+FvD45qkqGD8arZm3y9enaOGPyBgSw29ZpqtKtmkFgmGu0jz88jsx96BL2chukD75buXFeOiR//y85hxgHQt3zn+1DXzdnvgZJJm40QyVqp1aqX5nZR6SedJoLjcxWdRKjtVaLmFt9aIk6N7CdG7/c5W/aKTTpmk50gWGbi2EY/GFIHGHNLKgo7h8jwidk/fIEaBsL6Yf9Tu3ZCu6IjBa3MrFwEfF5kwCuDpC3oBM+nMc45St2JdDG6pru1BHtWMNzOSyGSf9Bf4IcXoj/lCDMp8rS0un7zKHeEFu28m4arZKm3T5MMs6isJDkZpURA==
-----END SIGNATURE-----
