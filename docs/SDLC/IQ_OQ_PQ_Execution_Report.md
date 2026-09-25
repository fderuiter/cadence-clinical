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

- **Total Automated Test Cases Run:** 470
- **Passed:** 0 🟢
- **Failed/Errors:** 0 🔴
- **Overall Operational Pass Rate:** 0.00%

## 2. Installation Qualification (IQ)

The Installation Qualification verifies that the software execution environment and static quality checks are fully compliant.

## 3. Operational Qualification (OQ)

| Test Case Name | Classname / Suite | Target Req | Status | Duration |
| :--- | :--- | :--- | :--- | :--- |
| `Cadence.E2ETests.E2E.ObservationAndFormE2ETests.GetFormSubmissionDetail_WhenFormNotFound_Returns404NotFound` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.E2ETests.E2E.SiteComplianceE2ETests.GetSiteComplianceSummary_WhenSiteNotFound_Returns404NotFound` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.E2ETests.E2E.SubjectE2ETests.CreateSubject_WithValidPayload_Returns201Created` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.E2ETests.E2E.SubjectE2ETests.GetSubjectDetail_WhenSubjectNotFound_Returns404NotFound` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests.CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests.EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests.FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests.QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.FakeClock.GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.FakeClock.Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_Comparisons_EvaluatesInequalities` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.CreateFormSubmission_Handler_ReturnsCreatedResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.CreateQuery_Handler_ReturnsCreatedResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.FakeAuditContextAccessor.VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.InitializeForm_CreatesFormInDraftState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FakeAuditContextAccessor.UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests.NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.DiagnosticLogger.EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.DiagnosticLogger.ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.DiagnosticLogger.NominalScenario_MatchesGoldenMaster` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.DiagnosticLoggerProvider.Dispose` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenMismatchedArrayLength_Fails` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.is.ApplyPreconditionsAsync` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.CreateObservation_WithValidParameters_InitializesCorrectly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.FlagSdv_WithReason_ResetsSdvVerification` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.OutlierDetection_IdentifiesStatisticalOutliers` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.UcumNormalization_ConvertsUnitsCorrectly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.VerifySdv_WhenAlreadyVerified_ReturnsError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.FakeAuditContextAccessor.VerifySdv_WhenUnverified_TransitionsToVerified` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasUniqueIndexOnSubjectCode` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStatusWithMaxLength32` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.FakeClock.WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.MerkleChainTestDbContext.SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.MerkleChainTestDbContext.SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.MerkleChainTestDbContext.SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.MerkleChainTestDbContext.SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.MerkleChainTestDbContext.SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_IgnoresComputedMissingDocumentsPropertyItself` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsSubjectConsentToSubjectConsentsTable` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_ProtocolVersionIsMappedAsComplexProperty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.FakeAuditContextAccessor.ApplicationService_Close_ValidatesCraOrDmRole` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.FakeAuditContextAccessor.ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.FakeAuditContextAccessor.CancelQuery_FromOpen_SucceedsWithReason` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.FakeAuditContextAccessor.QueryLifecycle_FullTransitionSequence_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.FakeAuditContextAccessor.RaiseQuery_CreatesQueryInOpenState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedTag_ThrowsDecryptionFailed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.EncryptThenDecrypt_RoundTripsExactly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserId_GivenNoHttpContext_ReturnsSystemDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserId_GivenNoSubClaim_ReturnsSystemDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.FakeHttpContextAccessor.UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.CalculateAge_GivenNullBirthDate_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.CalculateAge_GivenNullObservationDate_ReturnsNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.NormalizeGender_GivenNullInput_ReturnsU` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.SubjectWithEncryptedDemographics.NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.ClearDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Create_TwoSubjects_ProduceDistinctIds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.IsPostRandomization_ForLockedStates_ReturnsTrue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.IsPostRandomization_ForUnlockedStates_ReturnsFalse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.UpdateDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.UpdateStratFactors_PreRandomization_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.FakeClock.Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.Harness.UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SiteActivationRequest_ValueEquality_ComparesByStudyId` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_ValidRequest_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.ClearReconsentRequirement_SetsRequiresReconsentFalse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_VersionTag_AlwaysReplacedUnconditionally` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.AllFactoryMethods_ProduceDistinctCodes` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedActivation_ReturnsFixedCodeAndType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedEnrollment_ReturnsFixedCodeAndType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.NotFound_ReturnsFixedCodeAndType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_AsNullableParameter_MayBeAbsent` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_ValidValue_IsAccepted` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_Equality_IsValueBased` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ToString_ReturnsValue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectConsentId_New_ProducesUniqueValues` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_New_ProducesUniqueValues` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_ToString_ReturnsGuidValue` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.Config_All23ServiceOptions_InstantiateWithDefaults` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.Config_CommonOptions_HasDefaultValuesMatchingPython` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.Config_Options_BindsFromConfigurationDictionary` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.ProtocolRAG_EvaluateFaithfulness_DetectsHallucinatedCitations` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.ProtocolRAG_EvaluateFaithfulness_FailsClosedForInsufficientInformation` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.ProtocolRAG_EvaluateFaithfulness_ValidatesGroundedAnswer` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.ProtocolRAG_FormatCitationMarker_ProducesCanonicalFormat` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.ConfigAndCoreModelsUnitTests.ProtocolRAG_FormatGroundedRagPrompt_FormatsPromptWithExcerpts` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.BlockRandomizationEngine_Allocate_AssignsArmWithChecksum` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.BlockRandomizationEngine_GeneratesCanonicalStratumKey` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.BoinEngine_CalculateBoinBoundaries_ReturnsExpectedBoundaries` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.BoinEngine_EvaluateDoseLevel_EscalatesWhenLowDltRate` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.BoinEngine_EvaluateDoseLevel_StopsTrialWhenExcessiveToxicityAtLowestDose` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.DTPLogisticsEngine_CalculateMeanKineticTemperature_ComputesHaynesMkt` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.DTPLogisticsEngine_EvaluateDtpDispatch_GeneratesTamperSealWhenEligible` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.LabToxicityGradingEngine_EvaluateHysLaw_DetectsCriticalHysLawTriad` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.LabToxicityGradingEngine_EvaluatesGrade3Alt_AndGeneratesAeDraft` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.MinimizationEngine_Allocate_CalculatesImbalanceAndAllocates` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.DomainEngineUnitTests.RuleEvaluator_EvaluatesAstAndJsonLogic` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Compliance_AuditChainVerifier_ValidatesIntactChain` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Compliance_AuditDigestCalculator_ComputesExpectedHash` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Compliance_RtmGeneratorService_SyncsInProcessWithoutPython` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Ddf_Models_ConstructsSuccessfully` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Deid_DeidDetector_DetectsAndRedactsEmail` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Hexagonal_DomainPrimitives_And_ExceptionMapper_Work` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Sentinels_Engine_ExecutesRegisteredCheck` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.Storage_InMemoryBlobStore_StoresAndRetrievesPayload` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.ToolingCore_Handlers_ReturnsSuccessfulEnvelope` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.ToolingCore_OpenApiSpecExporter_GeneratesValidJsonSpecs` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.PortedPackagesUnitTests.ToolingCore_OpenApiToolingService_ValidatesDriftInProcess` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.AddCadenceLogging_ConfiguresJsonConsoleOptions` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.AddCadenceTelemetry_RegistersOpenTelemetryServices` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.AddCadenceWebDefaults_RegistersLoggingTelemetryAndOpenApi` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.ExtensionMethods_ThrowOnNullArguments` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.MapCadenceApiDocumentation_RoutesConfiguredInDevelopment` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Packages.TelemetryUnitTests.UseCadenceTraceparent_InjectsW3CTraceparentHeaderWhenActivityActive` | `Cadence.UnitTests` | PRD-SYS-053 | 🟢 PASSED | < 1s |

## 4. Performance Qualification (PQ) & Scenario Validation

Total Verified Requirements: 226

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
- **Cryptographic Hash (SHA-256):** de287563c617b7aa4cfbcc4025ae5b7255b231377a227286bd353d0c46ad3e35

-----BEGIN CERTIFICATE-----
MIIC...DUMMY
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
ZGUyODc1NjNjNjE3YjdhYTRjZmJjYzQwMjVhZTViNzI1NWIyMzEzNzdhMjI3Mjg2YmQzNTNkMGM0NmFkM2UzNQ==
-----END SIGNATURE-----
