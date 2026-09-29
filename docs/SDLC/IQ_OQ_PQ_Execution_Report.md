---
search: false
---

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.

### Validation Result Summary

- **Automated Test Methods Reported:** 734
- **Passed:** 734 🟢
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

Qualified Requirements: 6 of 192

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
- **Cryptographic Hash (SHA-256):** ade8846bfbafd8ac62c7886d8153de62be148fa36fb998c50f6bbdbd092f1fd4
-----BEGIN CERTIFICATE-----
MIIDxTCCAn2gAwIBAgIJAKtiy5kPu5XUMD0GCSqGSIb3DQEBCjAwoA0wCwYJYIZI
AWUDBAIBoRowGAYJKoZIhvcNAQEIMAsGCWCGSAFlAwQCAaIDAgEgMGgxIjAgBgNV
BAoTGUNhZGVuY2UgQ2xpbmljYWwgU29mdHdhcmUxQjBABgNVBAMTOUNhZGVuY2Ug
R3hQIFZhbGlkYXRpb24gUnVubmVyIChjYWRlbmNlLXZhbGlkYXRpb24tcnVubmVy
KTAeFw0yNjA5MjgyMzQ1MDNaFw0yNzA5MjkyMzQ1MDNaMGgxIjAgBgNVBAoTGUNh
ZGVuY2UgQ2xpbmljYWwgU29mdHdhcmUxQjBABgNVBAMTOUNhZGVuY2UgR3hQIFZh
bGlkYXRpb24gUnVubmVyIChjYWRlbmNlLXZhbGlkYXRpb24tcnVubmVyKTCCASIw
DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAL1L8sr2gC5QolOcOPXmdkVa61eb
998hYFSmSRacKnrcNLexYI4ki79J+v3Tv/QGfxFmFnzZNKoX92IOsEHh4uKrNLZs
y5VyxszcGIBBNbWLWgvQTDIcghFTAIlz/TOM0tIkBo1DuZeakfzALBWfkerHTEnB
MHPUrgXRI/RT2NXm5CmxM3Lz9k3wOsULBlM0aafSi216U9sOA3kRHNiWkxALhIn2
j7ZEVd2P/mMywYu4rhySjnCdrow2O1/HPrC2/3Na5ktIDqfpav+We9f1qXf+bVu3
a1kZT+WhRarcEl/qfTdFCB+LioyzNmb9DxVdNmyYtG/80PFGbIjQGwJwTGECAwEA
AaMSMBAwDgYDVR0PAQH/BAQDAgbAMD0GCSqGSIb3DQEBCjAwoA0wCwYJYIZIAWUD
BAIBoRowGAYJKoZIhvcNAQEIMAsGCWCGSAFlAwQCAaIDAgEgA4IBAQAbgCtarYTM
6OuYqQqwPoanEJmfiYdLWm7/FtKwX0VZV/otwcSqLRGnv5CfhJY0U+mFhi1/4bM0
qK37RYT3Y/QtdiD+vw4hwXelbz4i9wQ0wYY9xmF5TZnF+j0sCITq9h95T1tCz9gR
xmRPWblFqyKy8ikn082Ev6z7a9OGXIEXhcu7zWC/4IHTZFJHlt1e1ADJXeMH1OTc
QcWOKY+ZBgSawUPZvwJoUhGIE95vthVCk/aepDdMdPzf3qurhuOa+YOFmOXI3P0Z
vwdpKoP6A4Z7UEGa2AifbYFfGeYLRrqRuI2fk/qEafajzntwaVEZ4aWtXMHwqtkS
sSKBSCtAhgNf
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
Jm7KUpk9LMffPQfhIJw9qqmvqVkiDBGGXxMNJXExkOdgYsmn2Hrh/uZfr6y+MiYK9rcj6CHikt3Wgl5cyQWGAOrYzRh+97RcDZCGEKCivopNOv6JBjFbot+TSt2q6+yJ16BpFKcO9sn9Hx/girfkN1y41j4LERz6RNr1YITaZPW/VSzGZtRkDwgvKsuogAZnjJ4E4KPj35ar7Tk6pIB8Wo4wPBEEzjqGaPnNr6aSyEXYR+zLypQzXDpQfVaKRCU525U6MzkMFZqJRvXWQYEmdfp1QN9fZEVPbCI/aliKEcM7yGRx3fSokp58kEeVLOdfLUOy+l2XA0SAzZZaaTvR2w==
-----END SIGNATURE-----
