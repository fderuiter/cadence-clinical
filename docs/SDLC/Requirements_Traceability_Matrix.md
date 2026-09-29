---
search: false
---

# Requirements Traceability Matrix (RTM)

*Generated on:* 2026-07-23 22:38:25 UTC
*Regulatory Compliance Standards:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5, IEC 62304 Section 5.7 & 5.8

## 1. Traceability Summary

- **Total Documented Requirements (PRD):** 192
- **Qualified Requirements (Verified in Current Milestone):** 5
- **Planned Requirements (Sprint 2/3 Roadmap):** 187
- **Legacy Traceability Aliases (Trace-1..34):** 34
- **Current Milestone Traceability Verification:** 2.6%

## 2. Qualified Requirements (Verified)

| Requirement ID | Source Document | Title / Description | Mapped Test Cases | Status |
| :--- | :--- | :--- | :--- | :--- |
| PRD-DDF-001 | PRD | **AI-Native USDM Protocol Digitization & Automated eCRF/SoA Synthesis Engine** | `UsdmNormalizerStageSynthesizesMissingMetadataAndEmitsWarnings` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`UsdmNormalizerStageDetectsDuplicateIdsAndThrows` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`RuleCompilerTranslatesConditionTreesToOpenRosaXPath` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`RuleCompilerSanitizesIdentifiersAndExtractsAppearance` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`CircularDependencyDetectorDetectsCyclesInSkipLogic` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`SmartBlocksCatalogInstantiatesDomainDefinitions` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`DatasetJsonEtlProcessorExtractsSdtmDomainRecords` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢<br>`DdfCompilerPipelineAsyncExecutesWithAccessibilityValidationFailure` (tests/Packages/DdfPipelineAndEtlTests.cs) 🟢 | ✅ **Passed** |
| PRD-SUB-001 | PRD | **State Transition Matrix & Enforcements** | `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` (tests/Execution/Compliance/SiteComplianceApplicationServiceTests.cs) 🟢<br>`Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`ApplyPreconditionsAsync` (tests/Execution/GoldenMaster/SubjectGoldenMasterTests.cs) 🟢<br>`CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates` (tests/Execution/Integration/EnrollmentIndexConcurrencyTests.cs) 🟢<br>`EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates` (tests/Execution/Integration/EnrollmentIndexConcurrencyTests.cs) 🟢<br>`CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices` (tests/Execution/Integration/EnrollmentIndexConcurrencyTests.cs) 🟢<br>`CreateAsync_ConcurrentCalls_PropagatesTaskFailures` (tests/Execution/Integration/EnrollmentIndexConcurrencyTests.cs) 🟢<br>`ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`GetAsync_GivenNoMatchingRow_ReturnsNull` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` (tests/Execution/Persistence/ConsentRepositoryTests.cs) 🟢<br>`WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`NormalizeGender_GivenNullInput_ReturnsU` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`CalculateAge_GivenNullBirthDate_ReturnsNull` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`CalculateAge_GivenNullObservationDate_ReturnsNull` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` (tests/Execution/Security/DemographicsNormalizationServiceTests.cs) 🟢<br>`Create_ProducesSubjectInScreeningWithSuppliedFields` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Create_TwoSubjects_ProduceDistinctIds` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`UpdateStratFactors_PreRandomization_Succeeds` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`UpdateDemographics_PreRandomization_Succeeds` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`IsPostRandomization_ForLockedStates_ReturnsTrue` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`IsPostRandomization_ForUnlockedStates_ReturnsFalse` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`ClearDemographics_PreRandomization_Succeeds` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`TransitionTo_SameState_NoOpSucceedsViaGuard` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` (tests/Execution/Subjects/ClinicalSubjectTests.cs) 🟢<br>`RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` (tests/Execution/Subjects/SubjectApplicationServiceConsentTests.cs) 🟢<br>`CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` (tests/Execution/Subjects/SubjectApplicationServiceCreateTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` (tests/Execution/Subjects/SubjectApplicationServiceDemographicsTests.cs) 🟢<br>`GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` (tests/Execution/Subjects/SubjectApplicationServiceDetailTests.cs) 🟢<br>`AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢<br>`EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` (tests/Execution/Subjects/SubjectApplicationServiceStateTests.cs) 🟢 | ✅ **Passed** |
| PRD-SYS-001 | PRD | **Standard Audit Logging (21 CFR Part 11 § 11.10(e))** | `CreateObservation_Handler_ReturnsCreatedResultWithLocation` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`InitializeForm_CreatesFormInDraftState` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`CreateObservation_WithValidParameters_InitializesCorrectly` (tests/Execution/Observations/ObservationUnitTests.cs) 🟢<br>`OutlierDetection_IdentifiesStatisticalOutliers` (tests/Execution/Observations/ObservationUnitTests.cs) 🟢<br>`ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` (tests/Execution/Observations/ObservationUnitTests.cs) 🟢 | ✅ **Passed** |
| PRD-SYS-002 | PRD | **Soft-Delete Enforcement and Shadow Schema Preservation** | `CreateObservation_Handler_ReturnsCreatedResultWithLocation` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`CreateFormSubmission_Handler_ReturnsCreatedResult` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`InitializeForm_CreatesFormInDraftState` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`CompleteAndApprove_LocksFormInReadOnlyState` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`FormApplicationService_Approve_ValidatesSha256PayloadHash` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢 | ✅ **Passed** |
| PRD-SYS-053 | PRD | **Antigravity GxP Lifecycle Hooks & AI Coding Guardrails** | `Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` (tests/Execution/Consumers/EConsentCompletedConsumerTests.cs) 🟢<br>`CreateObservation_Handler_ReturnsCreatedResultWithLocation` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` (tests/Execution/Endpoints/ObservationAndFormEndpointsTests.cs) 🟢<br>`InitializeForm_CreatesFormInDraftState` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`UnlockForm_WithReason_RevertsToDraftAndUnlocks` (tests/Execution/Forms/FormSubmissionStateTests.cs) 🟢<br>`WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`WriteAsync_GivenCallsWithIdenticalTimestamps_ProducesValidMerkleChain` (tests/Execution/Persistence/EfAuditLogWriterTests.cs) 🟢<br>`SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainIntegrationTests.cs) 🟢<br>`SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainIntegrationTests.cs) 🟢<br>`SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainIntegrationTests.cs) 🟢<br>`SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainIntegrationTests.cs) 🟢<br>`SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`SaveChangesAsync_GivenSequentialSavesWithIdenticalTimestamps_ProducesValidMerkleChain` (tests/Execution/Persistence/GxPAuditInterceptorMerkleChainTests.cs) 🟢<br>`RaiseQuery_CreatesQueryInOpenState` (tests/Execution/Queries/QueryLifecycleTests.cs) 🟢<br>`UserId_GivenNoHttpContext_ReturnsSystemDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`UserId_GivenNoSubClaim_ReturnsSystemDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`UserId_GivenEmptySubClaim_ReturnsSystemDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢<br>`SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` (tests/Execution/Security/CurrentAuditContextAccessorTests.cs) 🟢 | ✅ **Passed** |

## 3. Planned Requirements (Sprint 2/3 Roadmap)

| Requirement ID | Source Document | Title / Description | Scheduled Milestone |
| :--- | :--- | :--- | :--- |
| PRD-AGG-001 | PRD | **Immutable Point-in-Time Regulatory Safety Snapshot** | Sprint Roadmap |
| PRD-AGG-002 | PRD | **Cumulative & Interval Exposure Calculations** | Sprint Roadmap |
| PRD-AGG-003 | PRD | **Automated MedDRA SOC/PT Summary Tabulations & QPPV Sign-off** | Sprint Roadmap |
| PRD-CDR-001 | PRD | **Multi-Stream Clinical Lakehouse & Version-Pinned Data Pooling** | Sprint Roadmap |
| PRD-CDR-002 | PRD | **Automated CDISC SDTM & ADaM Data Pipeline Engine** | Sprint Roadmap |
| PRD-CDR-003 | PRD | **CDISC Dataset-JSON 1.1 / 2.0 & Define-XML 2.1 Compiler** | Sprint Roadmap |
| PRD-CDR-004 | PRD | **Validated Containerized Statistical Computing Environment (R & Python)** | Sprint Roadmap |
| PRD-CDR-005 | PRD | **Package Lock Integrity, Script Versioning & Reproducible Run Manifests** | Sprint Roadmap |
| PRD-CDR-006 | PRD | **Biostatistical Export Audit Trail & Blinded Interim Analysis Gating** | Sprint Roadmap |
| PRD-CEC-001 | PRD | **Automated Patient Event Dossier Compilation & PHI Redaction** | Sprint Roadmap |
| PRD-CEC-002 | PRD | **2+1 Independent Blinded Reader & Tie-Breaker Workflow** | Sprint Roadmap |
| PRD-CEC-003 | PRD | **Bi-Directional EDC Feedback & CDISC SDTM CE Domain Export** | Sprint Roadmap |
| PRD-CRF-001 | PRD | **CRF Authoring & Global Library Instantiation** | Sprint Roadmap |
| PRD-CRF-002 | PRD | **Real-time Contextual Preview** | Sprint Roadmap |
| PRD-CRF-003 | PRD | **Collaborative Workspace Review Workflow** | Sprint Roadmap |
| PRD-CRF-004 | PRD | **Declarative Rule Generation & Multi-Layer Edit Checks** | Sprint Roadmap |
| PRD-CRF-005 | PRD | **Simulation and Dry-Run Cycle Detection** | Sprint Roadmap |
| PRD-CRF-006 | PRD | **CDASH, USDM, and CSV Mapping Fidelity** | Sprint Roadmap |
| PRD-CRF-007 | PRD | **FHIR eSource Readiness & CDASH Pre-fill** | Sprint Roadmap |
| PRD-CRF-008 | PRD | **Regulatory & Protocol Document Export** | Sprint Roadmap |
| PRD-CRF-009 | PRD | **Role-Based Authorization Gates** | Sprint Roadmap |
| PRD-CRF-010 | PRD | **GxP Change-Reason Justification** | Sprint Roadmap |
| PRD-CRF-011 | PRD | **Immutable Audit Attribution** | Sprint Roadmap |
| PRD-CRF-012 | PRD | **Version Pinning and Lock Enforcement** | Sprint Roadmap |
| PRD-CRF-013 | PRD | **Site & Tenant Data Isolation** | Sprint Roadmap |
| PRD-CRF-014 | PRD | **Failure Recovery & High Availability** | Sprint Roadmap |
| PRD-CRF-015 | PRD | **In-Memory Accessibility Auditing** | Sprint Roadmap |
| PRD-CSM-001 | PRD | **Cross-Site Digit Preference (Benford's Law) & Rounding Bias Analysis** | Sprint Roadmap |
| PRD-CSM-002 | PRD | **Multi-Variate Correlation Matrix & Variance Shrinkage Anomaly Detection** | Sprint Roadmap |
| PRD-CSM-003 | PRD | **Calendar & Timestamp Pattern Clustering (Weekend/Holiday Entry Inconsistencies)** | Sprint Roadmap |
| PRD-CSM-004 | PRD | **Duplicate Patient Record & Anatomical Fabrication Detection** | Sprint Roadmap |
| PRD-CSM-005 | PRD | **Integrated TransCelerate RACT Matrix & Automated KRI Linkage** | Sprint Roadmap |
| PRD-CSM-006 | PRD | **Automated Centralized Monitoring Anomaly Dossier & Inspection Snapshot** | Sprint Roadmap |
| PRD-CTMS-001 | PRD | **Operational Site and Milestone Tracking** | Sprint Roadmap |
| PRD-CTMS-002 | PRD | **Monitoring Visit Reports (MVR) Correspondence Lifecycle** | Sprint Roadmap |
| PRD-CTMS-003 | PRD | **CRA Allocation and Workload Summaries** | Sprint Roadmap |
| PRD-CTMS-004 | PRD | **Standard GxP Site Audit trail** | Sprint Roadmap |
| PRD-CTMS-005 | PRD | **Site Regulatory Greenlight & Essential Document Gatekeeper** | Sprint Roadmap |
| PRD-CTMS-006 | PRD | **Protocol Deviation Lifecycle & Quality CAPA Escalation** | Sprint Roadmap |
| PRD-CTMS-007 | PRD | **Risk-Based Quality Management (RBQM) & KRI Engine** | Sprint Roadmap |
| PRD-CTMS-008 | PRD | **Procedure-Based Financials & EDC Auto-Payables** | Sprint Roadmap |
| PRD-CTMS-009 | PRD | **Investigational Product (IP) Accountability & Temperature Excursions** | Sprint Roadmap |
| PRD-CTMS-010 | PRD | **Automated DIA TMF Reference Model Synchronization** | Sprint Roadmap |
| PRD-DCT-001 | PRD | **Universal BYOD / PWA Offline-First Patient Diary Architecture** | Sprint Roadmap |
| PRD-DCT-002 | PRD | **Continuous Biometric IoT Sensor Stream Ingestion (CGM, Actigraphy, Spirometry)** | Sprint Roadmap |
| PRD-DCT-003 | PRD | **Direct-to-Patient (DTP) Investigational Product Dispatch Tracking** | Sprint Roadmap |
| PRD-DCT-004 | PRD | **Decentralized In-Home Healthcare Nurse Visit Coordination** | Sprint Roadmap |
| PRD-DCT-005 | PRD | **GxP-Compliant Telehealth Video Consultation & Encrypted Recording** | Sprint Roadmap |
| PRD-DCT-006 | PRD | **Tiered Pediatric Assent & Legally Authorized Representative (LAR) Consent** | Sprint Roadmap |
| PRD-DEV-001 | PRD | **Unique Device Identifier (UDI) & Serialized Inventory Tracking** | Sprint Roadmap |
| PRD-DEV-002 | PRD | **Device Deficiency & Malfunction eCRF Capture** | Sprint Roadmap |
| PRD-DEV-003 | PRD | **Automated Adverse Device Effect (ADE) & RTSM Quarantine Triggers** | Sprint Roadmap |
| PRD-DEV-004 | PRD | **Regulatory Incident Reports & CDISC SDTM DD/DO Dataset Exports** | Sprint Roadmap |
| PRD-DOC-001 | PRD | **Object Storage Adapter & Metadata Envelope Pattern** | Sprint Roadmap |
| PRD-DOC-002 | PRD | **Cryptographic File Integrity & SHA-256 Checksum Verification** | Sprint Roadmap |
| PRD-DOC-003 | PRD | **21 CFR Part 11 Electronic Signatures on Binary Attachments & Legal Retention Hold** | Sprint Roadmap |
| PRD-EDC-001 | PRD | **Spreadsheet Ingestion Sheet Structure** | Sprint Roadmap |
| PRD-EDC-002 | PRD | **Field-Level Ingestion Validations** | Sprint Roadmap |
| PRD-EDC-003 | PRD | **Dynamic Skip Logic Evaluation** | Sprint Roadmap |
| PRD-EDC-004 | PRD | **Cascading Dependent Nullification (Orphan Data Safeguard)** | Sprint Roadmap |
| PRD-EDC-005 | PRD | **Real-Time Row Ingestion and Index Tracking** | Sprint Roadmap |
| PRD-EDC-006 | PRD | **Advanced Inputs (VAS and Interactive Body Maps)** | Sprint Roadmap |
| PRD-EDC-007 | PRD | **Local IndexedDB Security & State Preservation** | Sprint Roadmap |
| PRD-EDC-008 | PRD | **Conflict Resolution and Sync Reconciliation** | Sprint Roadmap |
| PRD-EDC-009 | PRD | **Visual Analog Scale (VAS) Slider Specifications** | Sprint Roadmap |
| PRD-EDC-010 | PRD | **Interactive Body Map Coordinates and Schema Mapping** | Sprint Roadmap |
| PRD-EDL-001 | PRD | **Data-Driven Expected Document Lists (EDLs) & Completeness Tracking** | Sprint Roadmap |
| PRD-ESR-001 | PRD | **SMART Backend Services Automated Ingestion** | Sprint Roadmap |
| PRD-ESR-002 | PRD | **Clinical Research Coordinator (CRC) Reconciliation Drawer** | Sprint Roadmap |
| PRD-ESR-003 | PRD | **Immutable Source Data Provenance & Part 11 Audit Attribution** | Sprint Roadmap |
| PRD-EVT-001 | PRD | **Transactional Outbox Pattern & Zero Data Loss** | Sprint Roadmap |
| PRD-EVT-002 | PRD | **Deterministic Idempotency & Replay Auditability** | Sprint Roadmap |
| PRD-IMG-001 | PRD | **Zero-Footprint High-Performance Web DICOM & Multi-Planar Viewer** | Sprint Roadmap |
| PRD-IMG-002 | PRD | **In-Flight Pixel & Header De-Identification and PHI Scrubbing** | Sprint Roadmap |
| PRD-IMG-003 | PRD | **Independent Review Charter (IRC) & Blinded Dual-Reader Workflow** | Sprint Roadmap |
| PRD-IMG-004 | PRD | **Automated RECIST 1.1, Cheson, Lugano & RANO Measurement Derivations** | Sprint Roadmap |
| PRD-IMG-005 | PRD | **Adjudication Tie-Breaker Committee Escalation & Electronic Sign-off** | Sprint Roadmap |
| PRD-IMG-006 | PRD | **Direct EDC eCRF Measurement Binding & SDTM TU/TR/RS Auto-Population** | Sprint Roadmap |
| PRD-KNB-001 | PRD | **Knowledge Article Lifecycle, Working Draft Storage & Two-Tier Immutability** | Sprint Roadmap |
| PRD-KNB-002 | PRD | **Multi-Channel Notifications and Contextual Guidance** | Sprint Roadmap |
| PRD-LAB-001 | PRD | **Laboratory Reference Models and Validation Runs** | Sprint Roadmap |
| PRD-LBM-001 | PRD | **Multi-Format Lab Ingestion & LOINC Standardization** | Sprint Roadmap |
| PRD-LBM-002 | PRD | **Age/Sex Reference Range Governance & Uncalibrated Safeguards** | Sprint Roadmap |
| PRD-LBM-003 | PRD | **Automated CTCAE / DAIDS Toxicity Grading & AE Auto-Drafting** | Sprint Roadmap |
| PRD-MDR-001 | PRD | **Value-Level Metadata Constraint Propagation** | Sprint Roadmap |
| PRD-MDR-002 | PRD | **Biomedical Concept Lock State during Active Studies** | Sprint Roadmap |
| PRD-MDR-003 | PRD | **Dynamic Cohort Opening & Closing Rules** | Sprint Roadmap |
| PRD-MDR-004 | PRD | **Crossover Timeline Mapping & Arm Interventions** | Sprint Roadmap |
| PRD-MDR-005 | PRD | **Dual-Key Blinding Security** | Sprint Roadmap |
| PRD-MDR-006 | PRD | **Blinding Constraints on UI Data Rendering** | Sprint Roadmap |
| PRD-MDR-007 | PRD | **Logical Mapping of I/E Criteria to eCRF Fields** | Sprint Roadmap |
| PRD-PAT-001 | PRD | **SoA-Linked Stipend Matrix & Automated Payable Credits** | Sprint Roadmap |
| PRD-PAT-002 | PRD | **Mobile Travel Expense Submission & Receipt OCR** | Sprint Roadmap |
| PRD-PAT-003 | PRD | **Plain-Language Lay Summary Portal** | Sprint Roadmap |
| PRD-PAY-001 | PRD | **Procedure-Based Fee Matrix & Investigator Grant Contract Engine** | Sprint Roadmap |
| PRD-PAY-002 | PRD | **Real-Time EDC & Monitoring Trigger-to-Payable Automation** | Sprint Roadmap |
| PRD-PAY-003 | PRD | **Multi-Party Split Payee & Withholding Holdback Governance** | Sprint Roadmap |
| PRD-PAY-004 | PRD | **Automated Self-Billing Invoicing & Remittance Advice Dispatch** | Sprint Roadmap |
| PRD-PAY-005 | PRD | **Multi-Currency Conversion & Budget Adjustment Amendments** | Sprint Roadmap |
| PRD-PAY-006 | PRD | **21 CFR Part 11 Financial Disbursement Audit Ledger** | Sprint Roadmap |
| PRD-PVG-001 | PRD | **Deterministic Multi-Country Expedited Regulatory Reporting Rules Engine** | Sprint Roadmap |
| PRD-PVG-002 | PRD | **Secure Applicability Statement 2 (AS2) B2B Gateway & MDN Verification** | Sprint Roadmap |
| PRD-PVG-003 | PRD | **Disproportionality Signal Detection Engine (PRR, ROR, EBGM / MGPS)** | Sprint Roadmap |
| PRD-PVG-004 | PRD | **Automated Periodic Aggregate Safety Reporting (DSUR, PSUR, PBRER)** | Sprint Roadmap |
| PRD-PVG-005 | PRD | **MedDRA / WHODrug Automated Auto-Coding & Upversioning Impact Analysis** | Sprint Roadmap |
| PRD-PVG-006 | PRD | **Literature Safety Surveillance & Regulatory Inquiry Tracking** | Sprint Roadmap |
| PRD-PVG-007 | PRD | **Blinded Safety Review & Unblinded Safety Officer Air-Gap** | Sprint Roadmap |
| PRD-PVG-008 | PRD | **21 CFR Part 11 Physician Electronic Signatures on ICSR Packages** | Sprint Roadmap |
| PRD-QLT-001 | PRD | **Protocol Deviation Classification, Severity Grading, and Ingestion Hooks** | Sprint Roadmap |
| PRD-QLT-002 | PRD | **Multi-Methodology Root Cause Analysis (RCA) Engine** | Sprint Roadmap |
| PRD-QLT-003 | PRD | **6-Stage Gate CAPA Lifecycle with Scheduled Effectiveness Checks** | Sprint Roadmap |
| PRD-QLT-004 | PRD | **TransCelerate & ICH E6(R3) RBQM, KRI Statistical Anomaly Scoring, and Site Risk Index** | Sprint Roadmap |
| PRD-QLT-005 | PRD | **Study-Level Quality Tolerance Limits (QTLs) and CSR Summary Generation** | Sprint Roadmap |
| PRD-QLT-006 | PRD | **Clinical Quality Audits, Finding Grading, and Direct CAPA Promotion** | Sprint Roadmap |
| PRD-QLT-007 | PRD | **Serious Breach & Regulatory Notification Clock Management** | Sprint Roadmap |
| PRD-QLT-008 | PRD | **1-Click GxP Inspection Readiness Dossier Packaging** | Sprint Roadmap |
| PRD-QLT-009 | PRD | **21 CFR Part 11 Electronic Signatures & Step-Up Verification for Quality Actions** | Sprint Roadmap |
| PRD-QRY-001 | PRD | **Query State Transitions and Constraints** | Sprint Roadmap |
| PRD-QRY-002 | PRD | **Query Escalation Rules** | Sprint Roadmap |
| PRD-QRY-003 | PRD | **Cross-Form Edit Check Execution** | Sprint Roadmap |
| PRD-QRY-004 | PRD | **Longitudinal Validation and Repeat-Visit Logic** | Sprint Roadmap |
| PRD-QRY-005 | PRD | **Field-Level SDV Flags and Audit Retention** | Sprint Roadmap |
| PRD-QRY-006 | PRD | **Automatic Verification Drop upon Data Modification** | Sprint Roadmap |
| PRD-QRY-007 | PRD | **Targeted SDV (tSDV) Sampling Algorithm** | Sprint Roadmap |
| PRD-QRY-008 | PRD | **Asynchronous Cross-Domain eCRF Anomaly Detection and Candidate Query Staging** | Sprint Roadmap |
| PRD-SCM-001 | PRD | **Multi-Echelon Depot Supply Chain Tracking & Predictive Resupply** | Sprint Roadmap |
| PRD-SCM-002 | PRD | **Continuous Data Logger Ingestion & Mean Kinetic Temperature (MKT) Engine** | Sprint Roadmap |
| PRD-SCM-003 | PRD | **Automated Lot Expiration Management, Extension & Re-Labeling** | Sprint Roadmap |
| PRD-SCM-004 | PRD | **Dose-Escalation Bayesian BOIN / CRM Adaptive Trial Algorithms** | Sprint Roadmap |
| PRD-SCM-005 | PRD | **Cell & Gene Therapy Vein-to-Vein Chain of Custody / Identity (COC/COI)** | Sprint Roadmap |
| PRD-SCM-006 | PRD | **Investigational Product Returns, Accountability & Reconciliation Destruction** | Sprint Roadmap |
| PRD-SCM-007 | PRD | **Real-Time Blinding Verification & Drug Dispensation Gating** | Sprint Roadmap |
| PRD-SSU-001 | PRD | **Dynamic Country-Specific Prerequisite Rules Matrix** | Sprint Roadmap |
| PRD-SSU-002 | PRD | **Multi-Factor Greenlight Site Activation Evaluator** | Sprint Roadmap |
| PRD-SSU-003 | PRD | **Programmatic RTSM & EDC Execution Gating** | Sprint Roadmap |
| PRD-SUB-002 | PRD | **Partial Visit Query Capability on Withdrawn Subjects** | Sprint Roadmap |
| PRD-SUB-003 | PRD | **Stratified Block Randomization** | Sprint Roadmap |
| PRD-SUB-004 | PRD | **Dynamic Minimization Algorithm** | Sprint Roadmap |
| PRD-SUB-005 | PRD | **Triggering and Authorizing Emergency Unblinding** | Sprint Roadmap |
| PRD-SUB-006 | PRD | **Immediate Unblinding State Mutation & System Actions** | Sprint Roadmap |
| PRD-SUB-007 | PRD | **Re-Consent Gating on Visits** | Sprint Roadmap |
| PRD-SYS-003 | PRD | **Cryptographic Ledger Hashing & Chain Validation** | Sprint Roadmap |
| PRD-SYS-004 | PRD | **Universal Site Isolation Constraint** | Sprint Roadmap |
| PRD-SYS-050 | PRD | **Multi-Persona Context Switching & End-to-End Golden Path Verification** | Sprint Roadmap |
| PRD-SYS-051 | PRD | **AI Gateway Microservice and Three-Tier Clinical Intelligence Architecture** | Sprint Roadmap |
| PRD-SYS-052 | PRD | **Generative Pharmacovigilance Safety Narratives with 21 CFR Part 11 Electronic Signature** | Sprint Roadmap |
| PRD-SYS-054 | PRD | **Domain-Specific MCP Tool Extensions, Graph Introspection & Autonomous Agent Sidecars** | Sprint Roadmap |
| PRD-SYS-102 | PRD | **Centralized Database-level Background Loop Coordination** | Sprint Roadmap |
| PRD-SYS-103 | PRD | **Asynchronous and Non-blocking Cryptographic Integrity Verification** | Sprint Roadmap |
| PRD-SYS-125 | PRD | **Immutable Point-in-Time Regulatory Dataset Snapshots & Executive Electronic Signatures** | Sprint Roadmap |
| PRD-SYS-126 | PRD | **Medical Device & In-Vitro Diagnostic (IVD) Investigation Suite** | Sprint Roadmap |
| PRD-SYS-161 | PRD | **Site Payments Budget Contracts, Fee Schedule Rate Matrices & Trigger-to-Payable Engine** | Sprint Roadmap |
| PRD-SYS-162 | PRD | **Site Payments Multi-Party Split Payee Allocations & Holdback Retention Governance** | Sprint Roadmap |
| PRD-SYS-163 | PRD | **Site Payments Self-Billing Invoicing, Cryptographic Remittance Advice & ERP Settlement Reconciliation** | Sprint Roadmap |
| PRD-SYS-167 | PRD | **Multi-Country Expedited ICSR Regulatory Reporting Clocks & Rules Engine** | Sprint Roadmap |
| PRD-SYS-168 | PRD | **AS2 S/MIME B2B XML Gateway Transmission & Signed MDN Receipt Validation** | Sprint Roadmap |
| PRD-SYS-169 | PRD | **Vectorized Disproportionality Signal Detection Engine & Aggregate Safety Reporting (DSUR/PSUR)** | Sprint Roadmap |
| PRD-SYS-170 | PRD | **Central & Local Laboratory Batch Ingestion, Reference Range Governance & LOINC Unit Conversion** | Sprint Roadmap |
| PRD-SYS-171 | PRD | **NCI-CTCAE v5.0 / DAIDS Toxicity Grading Engine, Baseline Delta Checks & Automated AE Review Queue** | Sprint Roadmap |
| PRD-SYS-172 | PRD | **Blinded Clinical Endpoint Adjudication Committee (CEC) Dossier Compilation & Dual-Reader Engine** | Sprint Roadmap |
| PRD-SYS-173 | PRD | **CEC Committee Governance, Consensus Determination & 21 CFR Part 11 Electronic Signatures** | Sprint Roadmap |
| PRD-SYS-174 | PRD | **Centralized Statistical Monitoring & Forensic Fraud Detection Engine** | Sprint Roadmap |
| PRD-SYS-175 | PRD | **Forensic Data Integrity Sentinels & Perceptual Image Hashing Tracer** | Sprint Roadmap |
| PRD-SYS-176 | PRD | **TransCelerate RACT Dynamic Risk Scoring & Automated KRI Surveillance** | Sprint Roadmap |
| PRD-SYS-177 | PRD | **Dynamic Multilingual Informed Consent Form (ICF) Builder & Tiered Modules** | Sprint Roadmap |
| PRD-SYS-178 | PRD | **Automated Minor Age-of-Majority Milestone Detection & Adult Re-Consent Engine** | Sprint Roadmap |
| PRD-SYS-179 | PRD | **Automated Protocol Amendment Re-Consent Distribution, Differential Redlines & Execution Gating** | Sprint Roadmap |
| PRD-SYS-180 | PRD | **GxP-Compliant WebRTC Telehealth Video Consultations & Encrypted Recording Archival** | Sprint Roadmap |
| PRD-SYS-181 | PRD | **Direct-to-Patient (DTP) Clinical Supply Tracer & Cold-Chain IoT Ingestion** | Sprint Roadmap |
| PRD-SYS-182 | PRD | **Participant Wallet & Automated Stipend Micro-Ledger Tracer** | Sprint Roadmap |
| PRD-SYS-183 | PRD | **Greenlight Site Activation Multi-Factor Engine Tracer** | Sprint Roadmap |
| PRD-SYS-184 | PRD | **Next-Gen RTSM, Bayesian BOIN & Cold Chain MKT Tracer** | Sprint Roadmap |
| PRD-TCK-001 | PRD | **Unified Clinical Issue Management & Operations Taxonomy** | Sprint Roadmap |
| PRD-TCK-002 | PRD | **Multi-Tier Clinical SLA Engine & Pause States** | Sprint Roadmap |
| PRD-TCK-003 | PRD | **Cross-Service REST Ingestion & Context Injection** | Sprint Roadmap |
| PRD-TCK-004 | PRD | **21 CFR Part 11 Electronic Signatures & Audited Evidence Attachments** | Sprint Roadmap |
| PRD-TCK-005 | PRD | **Grounded Protocol Knowledge Hub & Support Ticket RAG Triage** | Sprint Roadmap |
| PRD-TMF-001 | PRD | **TMF Taxonomy Catalog Hierarchy, Authoritative Inventory, and Extension Policy** | Sprint Roadmap |
| PRD-TMF-002 | PRD | **Strict Taxonomy Validation, Ingestion Rejection, and Cutover Qualification** | Sprint Roadmap |
| PRD-TMF-003 | PRD | **Taxonomy Version and Artifact Persistence** | Sprint Roadmap |
| PRD-TMF-004 | PRD | **Catalog-Driven Completeness and Milestone Alignment** | Sprint Roadmap |
| PRD-TMF-005 | PRD | **Automated and Manual Document Redaction & Integrity Verification** | Sprint Roadmap |
| PRD-TMF-006 | PRD | **Multimodal eTMF/eISF Document Intelligence and DIA Reference Model Classifier** | Sprint Roadmap |
| PRD-TRN-001 | PRD | **Protocol Amendment & SOP Curriculum Mapping** | Sprint Roadmap |
| PRD-TRN-002 | PRD | **Automated EDC Write-Permission Gating Based on Training Compliance** | Sprint Roadmap |
| PRD-TRN-003 | PRD | **Interactive Training Modules, Comprehension Quizzes & Electronic Signatures** | Sprint Roadmap |
| PRD-TRN-004 | PRD | **Automated TMF Zone 5 Training Certificate Routing** | Sprint Roadmap |

## 4. Legacy Traceability Aliases (Trace-1 through Trace-34)

| Legacy Trace ID | Canonical Parent PRD | Requirement Summary | Status |
| :--- | :--- | :--- | :--- |
| `Trace-1` | `PRD-SYS-002` | Shadow Schema Retention | ✅ **Verified via Parent** |
| `Trace-10` | `PRD-EVT-001` | Simulated Multi-Channel Notification Dispatcher | 📋 **Scheduled via Parent** |
| `Trace-11` | `PRD-MDR-004` | Native Vue 3 Rules Designer and GxP Ledger Sync | 📋 **Scheduled via Parent** |
| `Trace-12` | `PRD-TMF-005` | eTMF Document Redaction & Regulatory Privacy Controls | 📋 **Scheduled via Parent** |
| `Trace-13` | `PRD-SYS-001` | Native Part 11 eSignature Workflow & Post-Signature Mutation Rejection | ✅ **Verified via Parent** |
| `Trace-14` | `PRD-EDC-003` | PI Batch Electronic Sign-Off | 📋 **Scheduled via Parent** |
| `Trace-15` | `PRD-SAF-001` | SAE Reconciliation | 📋 **Scheduled via Parent** |
| `Trace-16` | `PRD-TCK-001` | Support Tickets & Query Escalation | 📋 **Scheduled via Parent** |
| `Trace-17` | `PRD-SYS-050` | Gateway Step-Up Re-Authentication and Signature Token Issuance | 📋 **Scheduled via Parent** |
| `Trace-18` | `PRD-ISF-001` | eISF Gateway Integration & Document Management | 📋 **Scheduled via Parent** |
| `Trace-19` | `PRD-CRF-001` | CRF Authoring & Global Library Instantiation | 📋 **Scheduled via Parent** |
| `Trace-2` | `PRD-SUB-003` | Cryptographic Key Multi-Sharing & Rotation | 📋 **Scheduled via Parent** |
| `Trace-20` | `PRD-CRF-002` | Real-time Contextual Preview | 📋 **Scheduled via Parent** |
| `Trace-21` | `PRD-CRF-003` | Collaborative Workspace Review Workflow | 📋 **Scheduled via Parent** |
| `Trace-22` | `PRD-CRF-004` | Declarative Rule Generation & Multi-Layer Edit Checks | 📋 **Scheduled via Parent** |
| `Trace-23` | `PRD-CRF-005` | Simulation and Dry-Run Cycle Detection | 📋 **Scheduled via Parent** |
| `Trace-24` | `PRD-CRF-006` | CDASH, USDM, and CSV Mapping Fidelity | 📋 **Scheduled via Parent** |
| `Trace-25` | `PRD-CRF-007` | FHIR eSource Readiness & CDASH Pre-fill | 📋 **Scheduled via Parent** |
| `Trace-26` | `PRD-CRF-008` | Regulatory & Protocol Document Export | 📋 **Scheduled via Parent** |
| `Trace-27` | `PRD-CRF-009` | Role-Based Authorization Gates | 📋 **Scheduled via Parent** |
| `Trace-28` | `PRD-CRF-010` | GxP Change-Reason Justification | 📋 **Scheduled via Parent** |
| `Trace-29` | `PRD-CRF-011` | Immutable Audit Attribution | 📋 **Scheduled via Parent** |
| `Trace-3` | `PRD-EDC-005` | Read-Only Trial Locks & Alert Routing | 📋 **Scheduled via Parent** |
| `Trace-30` | `PRD-CRF-012` | Version Pinning and Lock Enforcement | 📋 **Scheduled via Parent** |
| `Trace-31` | `PRD-CRF-013` | Site & Tenant Data Isolation | 📋 **Scheduled via Parent** |
| `Trace-32` | `PRD-CRF-014` | Failure Recovery & High Availability | 📋 **Scheduled via Parent** |
| `Trace-33` | `PRD-CRF-015` | In-Memory Accessibility Auditing | 📋 **Scheduled via Parent** |
| `Trace-34` | `PRD-SYS-041` | Label-Based Backlog Gating | 📋 **Scheduled via Parent** |
| `Trace-4` | `PRD-EDL-001` | Data-Driven Expected Document Lists (EDLs) | 📋 **Scheduled via Parent** |
| `Trace-5` | `PRD-TMF-002` | TMF Taxonomy Validation, Extension Policy & Cutover Qualification | 📋 **Scheduled via Parent** |
| `Trace-6` | `PRD-CTMS-001` | CTMS Monitoring and Site Operations Tracking | 📋 **Scheduled via Parent** |
| `Trace-7` | `PRD-QLT-001` | Quality & CAPA Traceability and Validation Assurance | 📋 **Scheduled via Parent** |
| `Trace-8` | `PRD-PAT-001` | eCOA Subject Identity & Gateway Boundary | 📋 **Scheduled via Parent** |
| `Trace-9` | `PRD-PAT-002` | ePRO Offline Sync & Part 11 Reconciliation | 📋 **Scheduled via Parent** |

---

## Electronic Signature Block

- **Signer Identity:** cadence-validation-runner
- **Timestamp:** 2026-07-23 22:38:25 UTC
- **Meaning / Purpose:** GxP Qualification Execution Sign-Off
- **Cryptographic Hash (SHA-256):** 4b74b5f42bd733d6120ab9660dfc083aa3fbd988bc0b76eddfd900c7f4bf9ca4
-----BEGIN CERTIFICATE-----
MIIDxTCCAn2gAwIBAgIJALwfRsM/SnmzMD0GCSqGSIb3DQEBCjAwoA0wCwYJYIZI
AWUDBAIBoRowGAYJKoZIhvcNAQEIMAsGCWCGSAFlAwQCAaIDAgEgMGgxIjAgBgNV
BAoTGUNhZGVuY2UgQ2xpbmljYWwgU29mdHdhcmUxQjBABgNVBAMTOUNhZGVuY2Ug
R3hQIFZhbGlkYXRpb24gUnVubmVyIChjYWRlbmNlLXZhbGlkYXRpb24tcnVubmVy
KTAeFw0yNjA5MjgxNjMwMzZaFw0yNzA5MjkxNjMwMzZaMGgxIjAgBgNVBAoTGUNh
ZGVuY2UgQ2xpbmljYWwgU29mdHdhcmUxQjBABgNVBAMTOUNhZGVuY2UgR3hQIFZh
bGlkYXRpb24gUnVubmVyIChjYWRlbmNlLXZhbGlkYXRpb24tcnVubmVyKTCCASIw
DQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAPFv1u1DXOVCB+6MPKYMEM7gX0yq
KA+pHxq9dBZ1ZjNKV37jym3veEAenV/F3AcIbu4gjRIdiTK0hb058qrdb0KoX2wx
Arr7e9rzYtkIm8Ge3lmIZ26YKxM05FEhrOLA3MH7TDHFRHD4UFXRqlQZSBBN5Xmz
oYIPRc+hRxkT80Q5Rw//IfgWplW4a220RjAr1xfhm6Y/mI6LKpW2JNypRHRn7kAn
+/VePtseZ2fUQQHTYTR1a4od/sbXENZu3zTk2sYsG77CaofPkW5ZmeZZv11prrm0
eqoDfRU0emP0f6Nwr9CIdT77UNDYanRlvqY5R3Tw7Nh+OwHH9hBA5blMb58CAwEA
AaMSMBAwDgYDVR0PAQH/BAQDAgbAMD0GCSqGSIb3DQEBCjAwoA0wCwYJYIZIAWUD
BAIBoRowGAYJKoZIhvcNAQEIMAsGCWCGSAFlAwQCAaIDAgEgA4IBAQBY6X0i9S5B
0MnM7cSqZuz5SN2DBeVosg6Ff+IU0W6NZLatEJ+qbJK+JkqTz4LWOdCn7u/l1XAM
VofbmY0G75lcsLsl5TfVO2Y5terYeB/uVRRXE4MD6i5FYOtCC6ug2NeDBODYd+C4
o8H4n8pgZ/6QBBjXLsvU2EL619GncdT/D8/oCyeA5hFdYd1luhz5lkRahCzuopj+
+p2Ti3jjT+eWRSW4lTq5XYXCicnml2kaSPbFfZ6MzRLlPlcWe2fzetzqodi96kYA
XygjaQoA+bDk38YfL/JdEgtERH/CdBuviKU0Hj96vSAXliaO0toSfMg4T2yCHGKY
f8bxMMVKqlIQ
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
3I2NKBP9uAB887GM4JvPJhA/xFMLbJfxzD7vLioTrPiW0Tk8SYx5f6niiAnsei9F44IRR0f4UQGQUbWm9YnpTrh1xHVRwmXbYGzNNoZQK5LbfYaEDTfkAah+ZrODEuD83Ial133aQzFC5mn9QmzBdJb2qXtLalAisQ4CQFeOSBAlsyKdHqnUQULDwUdTpedqAjq+860GDvieHP0hCJFt8fVn69/IB1S6rzk7oOHSITIZhVMMn8NtzpJxnwd6CR0jfQozBWqV7X2z0PPK18eiBxOwI/qW2iX9TF3pPasy5xA7yeib5FFPQ7wwu2PM60qfKIzJYEvDTRSIZTLKl4zFKQ==
-----END SIGNATURE-----
