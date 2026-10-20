---
search: false
---

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

<!-- evidence-context:start -->
- **Tested revision:** `d5da8691d192cbd8f10bf698fa55959a339acb25` (clean working tree)
- **Generated (UTC):** 2026-10-20 10:04:40
- **Database:** kernel fixture: PostgreSQL 18.6 (Ubuntu 18.6-1.pgdg24.04+2) from CADENCE_TEST_POSTGRES, extensions btree_gist 1.8, plpgsql 1.0, migrations through `20261020031614_AgentRequesterAndProposalExecution`, tests connected as the application role (NOSUPERUSER NOBYPASSRLS)
- **Runner:** `pnpm cadence gxp snapshot` on Ubuntu 24.04.5 LTS, .NET 10.0.12
<!-- evidence-context:end -->

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.

### Validation Result Summary

Counts cover the requirement-tagged test methods in section 3. Whole-suite totals are in the TRX files under
`TestResults/gxp/`, so this report only changes when a tagged test or its result changes.

- **Requirement-Tagged Test Methods:** 295
- **Passed:** 295 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Not Executed:** 0
- **Pass Rate (of executed):** 100.00%

## 2. Installation Qualification (IQ)

The Installation Qualification records the environment the tests ran in: the tested revision, the database and the
runner are in the evidence block at the top of this report.

## 3. Operational Qualification (OQ)

Test methods that declare at least one requirement. Theory cases are merged per method. Durations are left
out so the report only changes when results change; they are in the TRX files under `TestResults/gxp/`.

| Test Case Name | Class | Target Req | Status |
| :--- | :--- | :--- | :--- |
| `AuditTrailEntitiesMustNotBeDependentOfCascadeDelete` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | PRD-SYS-001 | 🟢 PASSED |
| `ConsentToExport_EnrollCaptureQuerySdvLock_OneParticipantAndEveryWriteAuditedAsItsCaller` | `Cadence.E2ETests.Journeys.CrossModuleJourneyTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaeToSafety_CapturedAndCorrectedInEdc_PublishedUnderTheSameParticipantAndAuditedAsItsCaller` | `Cadence.E2ETests.Journeys.CrossModuleJourneyTests` | PRD-SYS-001 | 🟢 PASSED |
| `Withdrawal_DataCapturedBeforeStaysReadable_OneParticipantAndEveryWriteAuditedAsItsCaller` | `Cadence.E2ETests.Journeys.CrossModuleJourneyTests` | PRD-SYS-001 | 🟢 PASSED |
| `Econsent_TokenUsedOnOneInstance_IsRefusedByAFreshInstance` | `Cadence.IntegrationTests.Database.SignatureTokenSingleUsePostgresTests` | SRS-SYS-005 | 🟢 PASSED |
| `Execution_TokenUsedOnOneInstance_IsRefusedByAFreshInstance` | `Cadence.IntegrationTests.Database.SignatureTokenSingleUsePostgresTests` | SRS-SYS-005 | 🟢 PASSED |
| `Knowledge_TokenUsedOnOneInstance_IsRefusedByAFreshInstance` | `Cadence.IntegrationTests.Database.SignatureTokenSingleUsePostgresTests` | SRS-SYS-005 | 🟢 PASSED |
| `ChainWrittenToPostgres_VerifiesWhenReadBack` | `Cadence.IntegrationTests.Execution.AuditChainPostgresRoundTripTests` | PRD-SYS-003 | 🟢 PASSED |
| `EtmfComplianceUpdate_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | 🟢 PASSED |
| `SubjectStateChange_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | 🟢 PASSED |
| `QueryWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `SiteComplianceWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `SubjectWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | 🟢 PASSED |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | 🟢 PASSED |
| `StateChange_AlongAForbiddenPath_Returns422_AndLeavesTheStateUnchanged` | `Cadence.IntegrationTests.Execution.SubjectRouteAuthorityTests` | PRD-SUB-001 | 🟢 PASSED |
| `AgentRunIsRecordedThroughTheKernelPipelineWithVerifiableHashAndAudit` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `BudgetWhenSpentRefusesTheNextRun` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `ChangedConfigurationIsRefusedAtQualificationAndRunStart` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `CompleteAgentRunBySomeoneElseIsRefused` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `EvaluationRunIsStoredOnlyWhenItIsEvidenceForItsConfiguration` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `KillSwitchRefusesRunsInTheRuntimeAndInTheStartCommand` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `QualificationNeedsTheQualifiersSignatureAcceptedEvidenceAndACompleteConfiguration` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `SuspendedConfigurationIsRefusedUntilQualifiedAgain` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `UnqualifiedConfigurationIsRefusedByTheRuntimeAndTheStartCommand` | `Cadence.IntegrationTests.Intelligence.IntelligenceModulePostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `Accept_CommitsTheValueAndTheProvenanceViewShowsSourceAndAudit` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002, PRD-ESR-003 | 🟢 PASSED |
| `BatchAccept_CommitsEveryCandidateWithSignatureAndProvenanceAudit` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002, PRD-ESR-003 | 🟢 PASSED |
| `BatchAccept_WithAnUnknownCandidate_ChangesNothing` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002 | 🟢 PASSED |
| `BatchReject_RejectsEveryCandidateWithRationale` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002 | 🟢 PASSED |
| `RaiseQuery_OpensAQueryAndHoldsTheCandidate` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002 | 🟢 PASSED |
| `Reject_NeedsARationaleAndKeepsTheSourcePayload` | `Cadence.IntegrationTests.Interop.EsourceReconciliationPostgresTests` | PRD-ESR-002, PRD-ESR-003 | 🟢 PASSED |
| `ACallerWithNoStudyAssignment_ReachesNoStudy` | `Cadence.IntegrationTests.Interop.SceStudyScopeTests` | PRD-CDR-006 | 🟢 PASSED |
| `ADataManagerOnStudyA_CannotListMintOrRevokeStudyBLockTokens` | `Cadence.IntegrationTests.Interop.SceStudyScopeTests` | PRD-CDR-006 | 🟢 PASSED |
| `AStatisticianOnStudyA_CannotListOpenVerifyOrReadArtifactsOfStudyBRuns` | `Cadence.IntegrationTests.Interop.SceStudyScopeTests` | PRD-CDR-006 | 🟢 PASSED |
| `AStatisticianOnStudyA_CannotListReadCreateOrExecuteStudyBPrograms` | `Cadence.IntegrationTests.Interop.SceStudyScopeTests` | PRD-CDR-006 | 🟢 PASSED |
| `Execute_ByTheRecipient_RunsUnblindedAndOnlyUnblindedReadersSeeIt` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `Execute_WithATokenIssuedToSomeoneElse_Returns403` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `Execute_WithATokenSignedWithTheOldLiteralKey_Returns403` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `IssueLockToken_ByACallerWithNoRole_Returns403AndMintsNothing` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `IssueLockToken_ByACoordinator_Returns403AndMintsNothing` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `IssueLockToken_IgnoresRolesInTheBody` | `Cadence.IntegrationTests.Interop.SceUnblindingAuthorizationTests` | PRD-CDR-006 | 🟢 PASSED |
| `CdiscCtPackageLoadsAsOnePinnedVersionWithAuditedDigest` | `Cadence.IntegrationTests.Kernel.ConceptRegistryPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `IndicatorSet_ReleasedAndPinned_ComputesValuesAndFlagsABreachedLimit` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | 🟢 PASSED |
| `StudyPulse_ReadsLiveKernelPopulationsAndCountsOnlyWhatTheReaderMaySee` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | 🟢 PASSED |
| `ReadNeverReturnsAnotherTenantsActivity` | `Cadence.IntegrationTests.Kernel.KernelActivityPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `Scenario3ActOutsideDelegationDeniedAuditedRetroactiveDoaDoesNotValidate` | `Cadence.IntegrationTests.Kernel.KernelAuthorityPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `StudySiteActivationNeedsAnEffectiveVersionOnApprovalEvidence` | `Cadence.IntegrationTests.Kernel.KernelEffectivityPostgresTests` | PRD-CTMS-005 | 🟢 PASSED |
| `ChangeStatusRefusesIllegalMovesAndTimes` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | 🟢 PASSED |
| `EnrollmentsDatabaseGuardsEnforceTheLifecycleForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | 🟢 PASSED |
| `DenialIsExplainedAndAuditedWithItsConditions` | `Cadence.IntegrationTests.Kernel.KernelExplanationPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplicationRoleCannotReadTheVaultTablesAndReachesThemOnlyThroughItsFunctions` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `RecordAndRevealDisclosesOnlyAfterAnAuditedRevealAndNoKernelTableHoldsIdentifiers` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `RevealWithoutTheLayersCapabilityIsDeniedAuditedAndDisclosesNothing` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `VaultRecordsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `NavigationNeverCrossesTenants` | `Cadence.IntegrationTests.Kernel.KernelNavigationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `HostCorrectionRecordsCorrectsAndAuditsTheReasonAndRefusesStaleMissingReasonAndLocked` | `Cadence.IntegrationTests.Kernel.KernelObservationHostPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `CorrectionCommitsValueRevisionAuditEventAndOutboxAsOneActivity` | `Cadence.IntegrationTests.Kernel.KernelObservationPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `AgentReadsAndProposesOnlyWithinTheRequestingPersonsScope` | `Cadence.IntegrationTests.Kernel.KernelProposalExecutionPostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `ProposalExecutionApproverExecutesLinksProposalApprovalActivityAndEvents` | `Cadence.IntegrationTests.Kernel.KernelProposalExecutionPostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `ProposalExecutionRefusesEveryUnlinkedExecution` | `Cadence.IntegrationTests.Kernel.KernelProposalExecutionPostgresTests` | PRD-SYS-051 | 🟢 PASSED |
| `ForkKeepsTheParentApprovedNumbersTheDraftAndStoresTheAmendment` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | 🟢 PASSED |
| `ProtocolVersionsTriggerFreezesApprovedContentForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | 🟢 PASSED |
| `SearchNeverReturnsAnotherTenantsObjects` | `Cadence.IntegrationTests.Kernel.KernelSearchPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `SimulateCannotReachAnotherTenantsVersions` | `Cadence.IntegrationTests.Kernel.KernelSimulationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `StudySiteAndLinkAreRegisteredThroughThePipelineWithEventsAndAudit` | `Cadence.IntegrationTests.Kernel.KernelStudyRegistrationPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplicationRoleIsNotOwnerAndCannotBypassRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `CrossTenantForeignKeyAndForeignTenantRowAreRejectedByTheDatabase` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `CrossTenantReadReturnsNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `EveryKernelTableHasTenantIdAndForcedRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `NoTenantCannotConnectAndSeesNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `PersonIsVisibleOnlyToTenantsWithAMembership` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `PipelineCommandCommitsUnderRowLevelSecurityVisibleToItsTenantOnly` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `TenantDirectoryListsIdsOnlyThroughTheDirectoryFunction` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `WorkQueueCannotReachAnotherTenant` | `Cadence.IntegrationTests.Kernel.KernelWorkQueuePostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `TraceFromAnotherTenantSeesNoEdges` | `Cadence.IntegrationTests.Kernel.LineagePostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `ModuleCommandCommitsModuleRowWithKernelAuditProvenanceEventAndIdempotency` | `Cadence.IntegrationTests.Kernel.ModuleCommandPathPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `RulePackDraftedAndReleasedAtRuntimeLoadsAndEvaluates` | `Cadence.IntegrationTests.Kernel.RulePackPostgresTests` | PRD-CRF-004 | 🟢 PASSED |
| `RegisterAndApproveStandardVersionAreAuditedAndReadBackExactly` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `TerminologyResolutionIsTenantScoped` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `FindingsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-013 | 🟢 PASSED |
| `RerunAfterMappingFixResolvesTheFindingInsteadOfDuplicatingIt` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-004 | 🟢 PASSED |
| `ACallerOutsideTheStudy_SeesNoRows_AndGetsNotFoundOnItsIds` | `Cadence.IntegrationTests.Payments.PaymentsStudySiteScopeTests` | PRD-SYS-192 | 🟢 PASSED |
| `CompileThenApprove_OnPostgres_AuditsTheVerifiedCallers` | `Cadence.IntegrationTests.Safety.DsurApprovalPostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `CompileUpdateApprove_StoresUtcInstantsAndTheVerifiedCallerOfEachWrite` | `Cadence.IntegrationTests.Safety.SafetyPersistencePostgresTests` | PRD-SYS-001 | 🟢 PASSED |
| `CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChanges_WithAnAuditedChangeAndNoActor_FailsWithoutWriting` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `BatchCertify_AllowsOverriddenSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-002 | 🟢 PASSED |
| `BatchCertify_CertifiesCompliantSites_WithIndividualTokensAndAuditLogs` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-003 | 🟢 PASSED |
| `BatchCertify_Performance_CompletesUnder5SecondsFor50Sites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-001 | 🟢 PASSED |
| `BatchCertify_RejectsNonCompliantSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-002 | 🟢 PASSED |
| `CertifySite_WithACompliantRecordedGate_IssuesATokenSignedByTheCaller` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-003 | 🟢 PASSED |
| `GetCountryMatrix_ReturnsSixFacetReadinessForRecordedSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-001 | 🟢 PASSED |
| `CompileEndpoint_FinalizesAndFilesToEtmf_AndDispatchesEmail` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `ConfirmationLetter_RendersHtmlTemplate_AndCompilesDraftPdf` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `FinalizedLetter_AppliesPart11Pkcs7Signature_AndPassesVerification` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `FollowUpLetter_WithFindings_RendersHtmlTable` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `SigTokenVerifier_ReusedTokenForEconsent_FailsClosedWithReauthenticationRequired` | `Cadence.UnitTests.Econsent.EconsentEndpointsTests` | SRS-SYS-005 | 🟢 PASSED |
| `SigTokenVerifier_TokenForDifferentActionUrl_FailsClosedWithReauthenticationRequired` | `Cadence.UnitTests.Econsent.EconsentEndpointsTests` | SRS-SYS-005 | 🟢 PASSED |
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndAuditsTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `DispenseDeviceAsync_ExpiredDevice_ThrowsExpiredError` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-003 | 🟢 PASSED |
| `DispenseDeviceAsync_QuarantinedDevice_ThrowsQuarantinedError` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-003 | 🟢 PASSED |
| `DispenseDeviceAsync_ValidAvailableDevice_DispensesSuccessfully` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-003 | 🟢 PASSED |
| `GetInvestigationSummaryAsync_CalculatesMetricsCorrectly` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-002 | 🟢 PASSED |
| `LogDeficiencyAsync_TriggersQuarantineAndSafetyCaseAndOutboxEvents` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-002 | 🟢 PASSED |
| `RegisterInventoryAsync_BulkQuantity_RegistersSerializedDevices` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-001 | 🟢 PASSED |
| `RegisterInventoryAsync_WithRawBarcode_ParsesAndStoresAttributes` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-001 | 🟢 PASSED |
| `ReleaseQuarantineAsync_ReleasesDeviceAndQuarantineRecord` | `Cadence.UnitTests.Execution.Devices.DeviceApplicationServiceTests` | PRD-DEV-003 | 🟢 PASSED |
| `DispenseDevice_Returns200OK` | `Cadence.UnitTests.Execution.Devices.DeviceEndpointsTests` | PRD-DEV-003 | 🟢 PASSED |
| `GetInvestigationSummary_Returns200OK` | `Cadence.UnitTests.Execution.Devices.DeviceEndpointsTests` | PRD-DEV-003 | 🟢 PASSED |
| `LogDeficiency_Returns201Created` | `Cadence.UnitTests.Execution.Devices.DeviceEndpointsTests` | PRD-DEV-002 | 🟢 PASSED |
| `ParseUdi_Returns200WithParsedResult` | `Cadence.UnitTests.Execution.Devices.DeviceEndpointsTests` | PRD-DEV-001 | 🟢 PASSED |
| `RegisterInventory_Returns201Created` | `Cadence.UnitTests.Execution.Devices.DeviceEndpointsTests` | PRD-DEV-001 | 🟢 PASSED |
| `IsHibccMod43Valid_ValidAndInvalidCodes_EvaluatesCorrectly` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_Gs1128SequentialWithoutParentheses_ParsesFields` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_Gs1128WithParentheses_ReturnsParsedAttributes` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_Gs1DataMatrixPrefix_ReturnsDataMatrixCarrierFormat` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_Hibcc128PrimaryAndSecondary_ParsesComponents` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_HibccDataMatrixWithPrefix_ParsesComponentsAndCarrierFormat` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_HibccSecondarySerialNumber_ParsesSerialNumber` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `Parse_NullOrEmptyBarcode_ReturnsInvalidResult` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `ValidateGtinMod10_ValidAndInvalidGtins_EvaluatesCorrectly` | `Cadence.UnitTests.Execution.Devices.UdiParserTests` | PRD-DEV-001 | 🟢 PASSED |
| `ChangingRoute_WithoutAChangeReason_Returns400` | `Cadence.UnitTests.Execution.Endpoints.LegacyAliasChangeReasonTests` | PRD-SYS-001 | 🟢 PASSED |
| `AfterARestart_TheFirstEvaluationUsesThePersistedCustomRanges` | `Cadence.UnitTests.Execution.Observations.LabRangeStudyScopeTests` | PRD-SYS-170 | 🟢 PASSED |
| `OneStudysCustomRange_NeverAppliesToAnotherStudy` | `Cadence.UnitTests.Execution.Observations.LabRangeStudyScopeTests` | PRD-SYS-170 | 🟢 PASSED |
| `ActionWithNoEntityChange_IsWrittenAsAnActionOnlyEntryOnTheSameChain` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `ChangeWithoutARecordedAction_IsStillAudited_WithTheEntityStateAsAction` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `SubjectUpdate_WritesOneEntryPerChange_AndTheChainVerifies` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-001 | 🟢 PASSED |
| `Verifier_DetectsAFork` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-003 | 🟢 PASSED |
| `Verifier_DetectsATamperedEntry` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-003 | 🟢 PASSED |
| `Verifier_VerifiesLegacyTableNameChainsAlongsideNewOnes_WithoutRewritingThem` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | PRD-SYS-003 | 🟢 PASSED |
| `SaveChangesAsync_AfterATransientFailure_RetriesAndWritesEachAuditEntryOnce` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | 🟢 PASSED |
| `TwoConcurrentQueryAnswers_OneSucceeds_TheOtherConflicts_AndTheAuditShowsOneChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChangesAsync_GivenSequentialSavesWithIdenticalTimestamps_ProducesValidMerkleChain` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-003 | 🟢 PASSED |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-003 | 🟢 PASSED |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-003 | 🟢 PASSED |
| `SaveChanges_Synchronously_WithAnAuditedChange_FailsInsteadOfSkippingTheAudit` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | PRD-SYS-001 | 🟢 PASSED |
| `RunAs_OverridesTheAmbientCaller` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `RunAs_WithNoHttpContext_AttributesToTheServiceAccount` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `ServiceAccount_WithoutANamedSvcId_IsRejected` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenEmptySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenNoHttpContext_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenNoSubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `UserId_GivenWhitespaceOnlySubClaim_Throws` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | PRD-SYS-001 | 🟢 PASSED |
| `HostStartup_WithoutAValidDemographicsKey_FailsFast` | `Cadence.UnitTests.Execution.Security.DemographicsKeyStartupValidationTests` | PRD-SYS-190 | 🟢 PASSED |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | PRD-SUB-001 | 🟢 PASSED |
| `DeleteDemographicsAsync_OnSuccess_WritesDeleteDemographicsAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | PRD-SYS-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseAndStoredStatusAreEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | PRD-SUB-001 | 🟢 PASSED |
| `ProcessBatchReconciliationAsync_UpdatesKitStatuses_AndGeneratesCertificate` | `Cadence.UnitTests.Execution.Supply.IpKitApplicationServiceTests` | PRD-SCM-006 | 🟢 PASSED |
| `HostStartup_OutsideDevelopment_WithoutOidcAudience_FailsFast` | `Cadence.UnitTests.Gateway.GatewayAudienceTests` | PRD-SYS-190 | 🟢 PASSED |
| `CallerOverTheLimit_Gets429WithRetryAfter_WhileOtherCallersAreServed` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | 🟢 PASSED |
| `HealthProbes_AreNeverRateLimited` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | 🟢 PASSED |
| `EveryCluster_HasTimeoutAndActiveAndPassiveHealthChecks` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `ServiceThatIsNotReady_Gets503WithoutForwarding_UntilItIsReadyAgain` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `ServiceThatStopsResponding_Gets504AfterTheProxyTimeout` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `EveryRoute_LeadsToMappedEndpoints_AndEveryServiceApiIsRouted` | `Cadence.UnitTests.Gateway.GatewayRouteCoverageTests` | PRD-SYS-191 | 🟢 PASSED |
| `HostStartup_OutsideDevelopment_WithoutServiceUrls_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | 🟢 PASSED |
| `HostStartup_WithARelativeServiceUrl_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | 🟢 PASSED |
| `TheStudyAssignmentsInTheToken_AreForwardedAndSigned` | `Cadence.UnitTests.Gateway.GatewayStudyAssignmentTests` | PRD-SYS-192 | 🟢 PASSED |
| `KeycloakVerifier_MapsIdentityProviderAnswers` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `KeycloakVerifier_UsesADirectGrantThenEndsThatSession` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_ForAnAuditor_Returns403WithoutCheckingCredentials` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_NamingAnotherUser_Returns403` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_RepeatedAttempts_AreRateLimitedPerSigner` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_WithAWrongPassword_Returns401WithoutAToken` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_WithTheCallersPassword_IssuesAVerifiableSingleUseToken` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_WithoutABoundAction_Returns400` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `SignatureVerification_WithoutSignIn_Returns401` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `TokenEndpoint_IsDerivedFromTheOidcSettings` | `Cadence.UnitTests.Gateway.SignatureTokenEndpointTests` | SRS-SYS-005 | 🟢 PASSED |
| `IngestBatchAsync_AbortsDatabasePersistence_WhenDeidentificationFails` | `Cadence.UnitTests.Imaging.DicomIngestionServiceTests` | PRD-IMG-002 | 🟢 PASSED |
| `IngestInstanceAsync_AbortsDatabasePersistence_WhenDeidentificationFails` | `Cadence.UnitTests.Imaging.DicomIngestionServiceTests` | PRD-IMG-002 | 🟢 PASSED |
| `GetCertificateByIdAsync_VerifiesWithTheConfiguredKey_AndRejectsAlteredCertificates` | `Cadence.UnitTests.Imaging.DicomWebEndpointsTests` | PRD-IMG-002 | 🟢 PASSED |
| `StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes` | `Cadence.UnitTests.Imaging.DicomWebEndpointsTests` | PRD-IMG-002 | 🟢 PASSED |
| `StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes` | `Cadence.UnitTests.Imaging.DicomWebEndpointsTests` | PRD-IMG-002 | 🟢 PASSED |
| `RunAsync_RecordsStartAndCompletion_WithConfigurationVersionAndUsage` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `RunAsync_WhenBudgetIsSpent_Refuses` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `RunAsync_WhenKillSwitchIsOff_RefusesWithoutCallingTheModel` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `RunAsync_WhenTheConfigurationChangedSinceQualification_Refuses` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `RunAsync_WhenTheConfigurationIsNotQualified_RefusesWithoutCallingTheModel` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `RunAsync_WhenTheConfigurationIsSuspended_Refuses` | `Cadence.UnitTests.Intelligence.AgentRunnerTests` | PRD-SYS-051 | 🟢 PASSED |
| `AgentRun_CompletesOnce_AndItsHashDetectsChanges` | `Cadence.UnitTests.Intelligence.IntelligenceDomainTests` | PRD-SYS-051 | 🟢 PASSED |
| `ContentHash_ChangesWhenAnyFieldChanges` | `Cadence.UnitTests.Intelligence.IntelligenceDomainTests` | PRD-SYS-051 | 🟢 PASSED |
| `QualificationGaps_NameWhatIsMissing` | `Cadence.UnitTests.Intelligence.IntelligenceDomainTests` | PRD-SYS-051 | 🟢 PASSED |
| `TaskQualification_UsableOnlyWhileQualifiedAndUnchanged` | `Cadence.UnitTests.Intelligence.IntelligenceDomainTests` | PRD-SYS-051 | 🟢 PASSED |
| `AcceptCandidate_CommitsValueAndWritesGxPAuditAndESignature` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-SYS-001 | 🟢 PASSED |
| `BatchAcceptCandidates_CommitsAllValidCandidatesAndWritesAuditLogs` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-SYS-001 | 🟢 PASSED |
| `BatchRejectCandidates_RequiresRationaleAndUpdatesStatus` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-SYS-001 | 🟢 PASSED |
| `GenerateAndVerifySmartAssertion_SignsWithTheEndpointsServerSideKey` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `GetProvenanceAudit_ReturnsRawFhirPayloadAndAuditTrail` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-SYS-001 | 🟢 PASSED |
| `IngestFhirBundle_MapsEachSupportedObservationFromTheBundle` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `RegisterAndListEndpoints_SuccessfullyManagesEhrEndpoints` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `RejectCandidate_RequiresRationaleAndPreservesOriginalPayload` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-SYS-001 | 🟢 PASSED |
| `ValidateLockfile_TamperedDigest_ReturnsTamperedStatus` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-CDR-005 | 🟢 PASSED |
| `EveryRead_IsRefused_WhenTheSignedTenantIsNotAKernelTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | 🟢 PASSED |
| `Navigation_ReadsInTheCallersTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | 🟢 PASSED |
| `EvaluateAutomationAgentIsBoundedByTheRequestingPerson` | `Cadence.UnitTests.Kernel.AuthorityTests` | PRD-SYS-051 | 🟢 PASSED |
| `EvaluateAutomationAgentOnlyReadsAndProposes` | `Cadence.UnitTests.Kernel.AuthorityTests` | PRD-SYS-051 | 🟢 PASSED |
| `EnrollmentTransitionsFollowTheLifecycle` | `Cadence.UnitTests.Kernel.EnrollmentTests` | PRD-SUB-001 | 🟢 PASSED |
| `KernelDbContextTenantScopedEntitiesUseCompositeTenantForeignKeys` | `Cadence.UnitTests.Kernel.KernelDbContextModelTests` | PRD-CRF-013 | 🟢 PASSED |
| `CheckExecution_IsForTheApproverWithARequestedCapabilityBeforeExpiry` | `Cadence.UnitTests.Kernel.ProposalTests` | PRD-SYS-051 | 🟢 PASSED |
| `ForkLeavesTheParentApprovedAndStartsAnAmendedDraft` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `LifecycleAllowsOnlyTheSpecifiedTransitions` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `SubmitForReviewSetsTheContentHashReturnToDraftClearsIt` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `EvaluatorIsDeterministicOverGeneratedPacksAndRecords` | `Cadence.UnitTests.Kernel.Rules.RulePackTests` | PRD-CRF-004 | 🟢 PASSED |
| `Map_GlucoseInConventionalUnits_ConvertsToSi` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-LBM-001 | 🟢 PASSED |
| `Map_SystolicBloodPressure_WritesOriginalAndStandardResults` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-CDR-002 | 🟢 PASSED |
| `LedgerGeneratedHistoriesResolveExactlyWhenTheLatestRunMissesTheFinding` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | 🟢 PASSED |
| `LedgerRerunWithoutDetectionResolvesWithTheRunAsEvidence` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | 🟢 PASSED |
| `BulkSignOff_WithATokenForOtherDelegations_Returns401` | `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` | SRS-SYS-005 | 🟢 PASSED |
| `BulkSignOff_WithAnUnknownDelegationId_Returns404AndCreatesNothing` | `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` | PRD-TRN-003 | 🟢 PASSED |
| `BulkSignOff_WithTokenForTheDelegations_SignsAsTheVerifiedCaller` | `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` | PRD-TRN-003 | 🟢 PASSED |
| `BulkSignOff_WithoutDelegationSignPermission_Returns403` | `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` | PRD-TRN-003 | 🟢 PASSED |
| `BulkSignOff_WithoutSigToken_Returns401AndSignsNothing` | `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` | SRS-SYS-005 | 🟢 PASSED |
| `Consume_ValidEconsentReconsentRequired_CreatesNotificationRecordDeliveriesAndAuditLogs` | `Cadence.UnitTests.Notifications.Consumers.EconsentReconsentConsumerTests` | PRD-SYS-001 | 🟢 PASSED |
| `PreTool_RefusesDestructiveCommands` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | 🟢 PASSED |
| `PreTool_RefusesPayloads` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | 🟢 PASSED |
| `StudyAccessEvaluator_FailsClosed` | `Cadence.UnitTests.Packages.CadenceSecurityUnitTests` | PRD-SYS-192 | 🟢 PASSED |
| `Deidentify_DoesNotGenerateOrSignCertificate_WhenPixelMaskingFails` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-IMG-002 | 🟢 PASSED |
| `MaskBurnedInPixels_ThrowsDicomPixelDeidException_WhenPixelMaskingDecodingFails` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-IMG-002 | 🟢 PASSED |
| `ForwardedHttps_FromATrustedProxyOnly_GetsHsts` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `HttpsResponses_CarryHsts_OutsideDevelopment` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `TrustedProxies_AcceptAddressesAndCidrRanges` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `TrustedProxies_WithAnInvalidEntry_Throw` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `PathsThatMatchNoRoute_AnswerNotFoundOnlyAfterAuthentication` | `Cadence.UnitTests.Packages.PermissionPolicyTests` | PRD-SYS-192 | 🟢 PASSED |
| `RoutesThatDeclareNothing_AreDeniedByDefault` | `Cadence.UnitTests.Packages.PermissionPolicyTests` | PRD-SYS-192 | 🟢 PASSED |
| `StudyScope_AdmitsOnlyCallersAssignedToTheRouteStudy` | `Cadence.UnitTests.Packages.PermissionPolicyTests` | PRD-SYS-192 | 🟢 PASSED |
| `Issue_ForAnAuditor_IsRefused` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Issue_WritesTheV3ContractClaims_WithASixtySecondLifetime` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_AcceptsABoundTokenOnce` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_AcceptsATokenOnlyWithinItsWindow` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_ExpiredToken_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_ReplayInTheSkewAfterExpiry_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_SingleUseSpansExactlyTheSharedStore` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_TokenSignedWithAnotherSecret_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_TokensOutsideTheV3Contract_AreRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_WithAnotherSignerActionOrBatch_IsRejectedWithoutConsumingTheToken` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `ExtractReceiptOcr_ParsesMetadataAndRecordsAudit` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_Exceeding100Threshold_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_MealSurpassingPerDiemCap_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_Under100WithHighConfidence_AutoApprovesAndDispatchesACH` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002, PRD-PAY-006, PRD-SYS-182 | 🟢 PASSED |
| `SubmitExpenseClaim_UnverifiedW9Over600Ytd_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-SYS-182 | 🟢 PASSED |
| `Approve_ALockedReport_Returns409` | `Cadence.UnitTests.Safety.DsurApprovalAuthorityTests` | PRD-PVG-004 | 🟢 PASSED |
| `Approve_WithTokenAndReason_SignsAsTheVerifiedCaller` | `Cadence.UnitTests.Safety.DsurApprovalAuthorityTests` | PRD-PVG-008 | 🟢 PASSED |
| `Approve_WithoutATokenForThisReport_Returns401ReauthenticationRequired` | `Cadence.UnitTests.Safety.DsurApprovalAuthorityTests` | PRD-PVG-008 | 🟢 PASSED |
| `Approve_WithoutTheApprovePermission_Returns403` | `Cadence.UnitTests.Safety.DsurApprovalAuthorityTests` | PRD-PVG-008 | 🟢 PASSED |
| `Approve_Twice_Returns409AndKeepsTheFirstSignature` | `Cadence.UnitTests.Safety.DsurCompilationTests` | PRD-PVG-004 | 🟢 PASSED |
| `Compile_PatientYearsAndMedDraCrossTabulation_MatchTheHandComputedFixture` | `Cadence.UnitTests.Safety.DsurCompilationTests` | PRD-PVG-004 | 🟢 PASSED |
| `Update_AfterApproval_Returns409AndChangesNothing` | `Cadence.UnitTests.Safety.DsurCompilationTests` | PRD-PVG-004 | 🟢 PASSED |
| `ApproveDsur_AppliesPart11SignatureBlock_AndLocksRecord` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-008 | 🟢 PASSED |
| `ApproveDsur_WritesAuditEntryNamingTheCallerAndReason` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-SYS-001 | 🟢 PASSED |
| `CompileDsur_CalculatesPatientYearsExposureAndMedDraCrossTabulation` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `Endpoints_CompileAndApprove_ReturnCreatedAndOkWithValidSignature` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `UpdateDsur_GivenLockedRecord_Returns409Conflict` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `EverySafetyRoute_Refuses401Unsigned_And403WithoutThePermission` | `Cadence.UnitTests.Safety.SafetyRouteAuthorityTests` | PRD-SYS-192 | 🟢 PASSED |
| `ACallerHoldingTheRoutePermission_IsAdmitted` | `Cadence.UnitTests.Security.RouteAuthorizationTests` | PRD-SYS-192 | 🟢 PASSED |
| `ACallerWithoutTheRoutePermission_IsRefusedBeforeTheHandlerRuns` | `Cadence.UnitTests.Security.RouteAuthorizationTests` | PRD-SYS-192 | 🟢 PASSED |
| `EveryListedSignedInCallerRoute_StillExists` | `Cadence.UnitTests.Security.RouteAuthorizationTests` | PRD-SYS-192 | 🟢 PASSED |
| `EveryRoute_DeclaresWhoMayCallIt` | `Cadence.UnitTests.Security.RouteAuthorizationTests` | PRD-SYS-192 | 🟢 PASSED |
| `EveryHostWithASignedRoute_RegistersADurableTokenUseStore` | `Cadence.UnitTests.Security.SigTokenUseStoreWiringTests` | SRS-SYS-005 | 🟢 PASSED |
| `StartupCheck_InProductionWithASignedRouteAndTheProcessLocalStore_RefusesToStart` | `Cadence.UnitTests.Security.SigTokenUseStoreWiringTests` | SRS-SYS-005 | 🟢 PASSED |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | PRD-SYS-003 | 🟢 PASSED |

## 4. Performance Qualification (PQ) & Scenario Validation

Qualified Requirements: 35 of 196

## 5. Qualification Review & Authorization

```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Integrity Stamp

- **Status:** Unsigned working copy, generated by `pnpm cadence gxp snapshot`. Not an electronic signature.
- **Content Hash (SHA-256):** 2fd974da042604be8271f4aeda6826b001ea512294172ee9d0f6f4b527ba463c
