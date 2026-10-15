---
search: false
---

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report

*Execution Date:* 2026-07-23 22:38:25 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration

This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.

### Validation Result Summary

Counts cover the requirement-tagged test methods in section 3. Whole-suite totals are in the TRX files under
`TestResults/gxp/`, so this report only changes when a tagged test or its result changes.

- **Requirement-Tagged Test Methods:** 257
- **Passed:** 208 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 49 ⚪
- **Not Run:** 0
- **Pass Rate (of executed):** 100.00%

## 2. Installation Qualification (IQ)

The Installation Qualification verifies that the software execution environment and static quality checks are fully compliant.

## 3. Operational Qualification (OQ)

Test methods that declare at least one requirement. Theory cases are merged per method. Durations are left
out so the report only changes when results change; they are in the TRX files under `TestResults/gxp/`.

| Test Case Name | Class | Target Req | Status |
| :--- | :--- | :--- | :--- |
| `AuditTrailEntitiesMustNotBeDependentOfCascadeDelete` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | PRD-SYS-001 | 🟢 PASSED |
| `ChainWrittenToPostgres_VerifiesWhenReadBack` | `Cadence.IntegrationTests.Execution.AuditChainPostgresRoundTripTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `EtmfComplianceUpdate_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `SubjectStateChange_StoresTheTypedReasonInTheAuditEntry` | `Cadence.IntegrationTests.Execution.ChangeReasonEncodingTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `FormWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `ObservationWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `QueryWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `SiteComplianceWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `SubjectWrites_AreEachAuditedAgainstTheCaller` | `Cadence.IntegrationTests.Execution.ExecutionAuditAttributionTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `ApproveForm_ReplayingAUsedToken_Returns401` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (SKIPPED) SKIPPED |
| `ApproveForm_WithATokenIssuedForSomethingElse_Returns401` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (SKIPPED) SKIPPED |
| `ApproveForm_WithoutESignatureToken_Returns401AndLeavesTheFormUnsigned` | `Cadence.IntegrationTests.Execution.ExecutionWriteAuthorizationTests` | SRS-SYS-005 | ⚪ (SKIPPED) SKIPPED |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.IntegrationTests.Execution.SubjectGoldenMasterTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `StateChange_AlongAForbiddenPath_Returns422_AndLeavesTheStateUnchanged` | `Cadence.IntegrationTests.Execution.SubjectRouteAuthorityTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `CdiscCtPackageLoadsAsOnePinnedVersionWithAuditedDigest` | `Cadence.IntegrationTests.Kernel.ConceptRegistryPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `IndicatorSet_ReleasedAndPinned_ComputesValuesAndFlagsABreachedLimit` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | ⚪ (SKIPPED) SKIPPED |
| `StudyPulse_ReadsLiveKernelPopulationsAndCountsOnlyWhatTheReaderMaySee` | `Cadence.IntegrationTests.Kernel.IndicatorSetPostgresTests` | PRD-CTMS-007 | ⚪ (SKIPPED) SKIPPED |
| `ReadNeverReturnsAnotherTenantsActivity` | `Cadence.IntegrationTests.Kernel.KernelActivityPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `Scenario3ActOutsideDelegationDeniedAuditedRetroactiveDoaDoesNotValidate` | `Cadence.IntegrationTests.Kernel.KernelAuthorityPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `StudySiteActivationNeedsAnEffectiveVersionOnApprovalEvidence` | `Cadence.IntegrationTests.Kernel.KernelEffectivityPostgresTests` | PRD-CTMS-005 | ⚪ (SKIPPED) SKIPPED |
| `ChangeStatusRefusesIllegalMovesAndTimes` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `EnrollmentsDatabaseGuardsEnforceTheLifecycleForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelEnrollmentPostgresTests` | PRD-SUB-001 | ⚪ (SKIPPED) SKIPPED |
| `DenialIsExplainedAndAuditedWithItsConditions` | `Cadence.IntegrationTests.Kernel.KernelExplanationPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `ApplicationRoleCannotReadTheVaultTablesAndReachesThemOnlyThroughItsFunctions` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `RecordAndRevealDisclosesOnlyAfterAnAuditedRevealAndNoKernelTableHoldsIdentifiers` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `RevealByAnAutomationAgentIsDeniedEvenWithTheGrant` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `RevealWithoutTheLayersCapabilityIsDeniedAuditedAndDisclosesNothing` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `VaultRecordsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.KernelIdentityVaultPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `NavigationNeverCrossesTenants` | `Cadence.IntegrationTests.Kernel.KernelNavigationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `ForkKeepsTheParentApprovedNumbersTheDraftAndStoresTheAmendment` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | ⚪ (SKIPPED) SKIPPED |
| `ProtocolVersionsTriggerFreezesApprovedContentForEveryDatabaseUser` | `Cadence.IntegrationTests.Kernel.KernelProtocolVersionPostgresTests` | PRD-CRF-012 | ⚪ (SKIPPED) SKIPPED |
| `SearchNeverReturnsAnotherTenantsObjects` | `Cadence.IntegrationTests.Kernel.KernelSearchPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `SimulateCannotReachAnotherTenantsVersions` | `Cadence.IntegrationTests.Kernel.KernelSimulationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `ApplicationRoleIsNotOwnerAndCannotBypassRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `CrossTenantForeignKeyAndForeignTenantRowAreRejectedByTheDatabase` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `CrossTenantReadReturnsNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `EveryKernelTableHasTenantIdAndForcedRowLevelSecurity` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `NoTenantCannotConnectAndSeesNothing` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `PersonIsVisibleOnlyToTenantsWithAMembership` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `PipelineCommandCommitsUnderRowLevelSecurityVisibleToItsTenantOnly` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `TenantDirectoryListsIdsOnlyThroughTheDirectoryFunction` | `Cadence.IntegrationTests.Kernel.KernelTenantIsolationPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `WorkQueueCannotReachAnotherTenant` | `Cadence.IntegrationTests.Kernel.KernelWorkQueuePostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `TraceFromAnotherTenantSeesNoEdges` | `Cadence.IntegrationTests.Kernel.LineagePostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `RulePackDraftedAndReleasedAtRuntimeLoadsAndEvaluates` | `Cadence.IntegrationTests.Kernel.RulePackPostgresTests` | PRD-CRF-004 | ⚪ (SKIPPED) SKIPPED |
| `RegisterAndApproveStandardVersionAreAuditedAndReadBackExactly` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-SYS-001 | ⚪ (SKIPPED) SKIPPED |
| `TerminologyResolutionIsTenantScoped` | `Cadence.IntegrationTests.Kernel.StandardsRegistryPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `FindingsAreIsolatedByTenant` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-013 | ⚪ (SKIPPED) SKIPPED |
| `RerunAfterMappingFixResolvesTheFindingInsteadOfDuplicatingIt` | `Cadence.IntegrationTests.Kernel.ValidationFindingPostgresTests` | PRD-CRF-004 | ⚪ (SKIPPED) SKIPPED |
| `CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `SaveChanges_WithAnAuditedChangeAndNoActor_FailsWithoutWriting` | `Cadence.UnitTests.Audit.GxPAuditTrailGenerationTests` | PRD-SYS-001 | 🟢 PASSED |
| `BatchCertify_AllowsOverriddenSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-002 | 🟢 PASSED |
| `BatchCertify_CertifiesCompliantSites_WithIndividualTokensAndAuditLogs` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-003 | 🟢 PASSED |
| `BatchCertify_Performance_CompletesUnder5SecondsFor50Sites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-001 | 🟢 PASSED |
| `BatchCertify_RejectsNonCompliantSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-002 | 🟢 PASSED |
| `GetCountryMatrix_ReturnsSixFacetReadinessForAllSites` | `Cadence.UnitTests.Ctms.StartupGreenlightEndpointsTests` | PRD-SSU-001 | 🟢 PASSED |
| `CompileEndpoint_FinalizesAndFilesToEtmf_AndDispatchesEmail` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `ConfirmationLetter_RendersHtmlTemplate_AndCompilesDraftPdf` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `FinalizedLetter_AppliesPart11Pkcs7Signature_AndPassesVerification` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `FollowUpLetter_WithFindings_RendersHtmlTable` | `Cadence.UnitTests.Ctms.VisitLetterPdfPipelineTests` | PRD-CTMS-002 | 🟢 PASSED |
| `SigTokenVerifier_ReusedTokenForEconsent_FailsClosedWithReauthenticationRequired` | `Cadence.UnitTests.Econsent.EconsentEndpointsTests` | PRD-SYS-053 | 🟢 PASSED |
| `SigTokenVerifier_TokenForDifferentActionUrl_FailsClosedWithReauthenticationRequired` | `Cadence.UnitTests.Econsent.EconsentEndpointsTests` | PRD-SYS-053 | 🟢 PASSED |
| `SigTokenVerifier_ValidTokenForEconsent_VerifiesAndConsumesSuccessfully` | `Cadence.UnitTests.Econsent.EconsentEndpointsTests` | PRD-SYS-053 | 🟢 PASSED |
| `Consume_EConsentCompleted_FilesICFDocumentInEtmf` | `Cadence.UnitTests.Etmf.Consumers.EtmfConsumersTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_EConsentCompleted_IsIdempotent` | `Cadence.UnitTests.Etmf.Consumers.EtmfConsumersTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_EtmfDocumentFilingRequested_FilesDocument` | `Cadence.UnitTests.Etmf.Consumers.EtmfConsumersTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_EtmfDocumentFilingRequested_IsIdempotent` | `Cadence.UnitTests.Etmf.Consumers.EtmfConsumersTests` | PRD-SYS-001 | 🟢 PASSED |
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
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | SRS-EDC-002 | 🟢 PASSED |
| `BatchSignOff_Exceeds100SubjectsCap_ReturnsBadRequest` | `Cadence.UnitTests.Execution.Endpoints.BatchSignOffEndpointsTests` | PRD-SYS-002 | 🟢 PASSED |
| `BatchSignOff_NonPiRole_RejectsWithForbidden` | `Cadence.UnitTests.Execution.Endpoints.BatchSignOffEndpointsTests` | PRD-SYS-002 | 🟢 PASSED |
| `BatchSignOff_SubjectLevel_ApprovesCompletedSubmissionsAndReturnsSummary` | `Cadence.UnitTests.Execution.Endpoints.BatchSignOffEndpointsTests` | PRD-SYS-002 | 🟢 PASSED |
| `ChangingRoute_WithoutAChangeReason_Returns400` | `Cadence.UnitTests.Execution.Endpoints.LegacyAliasChangeReasonTests` | PRD-SYS-001 | 🟢 PASSED |
| `ApproveFormSubmission_Handler_WithInvalidHmac_ReturnsBadRequest` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `GetFieldAuditHistory_Handler_WhenNotFound_ReturnsNotFoundResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | PRD-SYS-001 | 🟢 PASSED |
| `GetFieldAuditHistory_Handler_WhenSubmissionExists_ReturnsOkWithRevisionHistory` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | PRD-SYS-001 | 🟢 PASSED |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | SRS-EDC-003 | 🟢 PASSED |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | SRS-EDC-003 | 🟢 PASSED |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_WithUnconfiguredConfigurationSecret_ThrowsInvalidOperationException` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `FormApplicationService_Approve_WithUnconfiguredSecret_ThrowsInvalidOperationException` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | SRS-EDC-003 | 🟢 PASSED |
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
| `CreateAsync_WhenQueryRaised_PublishesClinicalQueryRaisedEvent` | `Cadence.UnitTests.Execution.Queries.ClinicalQueryApplicationServiceOutboxTests` | PRD-SYS-001 | 🟢 PASSED |
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
| `ValidateScannedBarcodeAsync_DetectsLotMismatch` | `Cadence.UnitTests.Execution.Supply.IpKitApplicationServiceTests` | PRD-SYS-001 | 🟢 PASSED |
| `HostStartup_OutsideDevelopment_WithoutOidcAudience_FailsFast` | `Cadence.UnitTests.Gateway.GatewayAudienceTests` | PRD-SYS-190 | 🟢 PASSED |
| `CallerOverTheLimit_Gets429WithRetryAfter_WhileOtherCallersAreServed` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | 🟢 PASSED |
| `HealthProbes_AreNeverRateLimited` | `Cadence.UnitTests.Gateway.GatewayRateLimitingTests` | PRD-SYS-189 | 🟢 PASSED |
| `EveryCluster_HasTimeoutAndActiveAndPassiveHealthChecks` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `ServiceThatIsNotReady_Gets503WithoutForwarding_UntilItIsReadyAgain` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `ServiceThatStopsResponding_Gets504AfterTheProxyTimeout` | `Cadence.UnitTests.Gateway.GatewayResilienceTests` | PRD-SYS-191 | 🟢 PASSED |
| `EveryRoute_LeadsToMappedEndpoints_AndEveryServiceApiIsRouted` | `Cadence.UnitTests.Gateway.GatewayRouteCoverageTests` | PRD-SYS-191 | 🟢 PASSED |
| `HostStartup_OutsideDevelopment_WithoutServiceUrls_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | 🟢 PASSED |
| `HostStartup_WithARelativeServiceUrl_FailsFast` | `Cadence.UnitTests.Gateway.GatewayServiceUrlValidationTests` | PRD-SYS-190 | 🟢 PASSED |
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
| `StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes` | `Cadence.UnitTests.Imaging.DicomWebEndpointsTests` | PRD-IMG-002 | 🟢 PASSED |
| `StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes` | `Cadence.UnitTests.Imaging.DicomWebEndpointsTests` | PRD-IMG-002 | 🟢 PASSED |
| `AcceptCandidate_CommitsValueAndWritesGxPAuditAndESignature` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002, PRD-SYS-001 | 🟢 PASSED |
| `BatchAcceptCandidates_CommitsAllValidCandidatesAndWritesAuditLogs` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002, PRD-SYS-001 | 🟢 PASSED |
| `BatchAcceptCandidates_ExceedingLimit_ReturnsBadRequest` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002 | 🟢 PASSED |
| `BatchOperation_WithNonExistentCandidateId_RollsBackTransaction` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002 | 🟢 PASSED |
| `BatchRejectCandidates_RequiresRationaleAndUpdatesStatus` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002, PRD-ESR-003, PRD-SYS-001 | 🟢 PASSED |
| `GenerateAndVerifySmartAssertion_ValidatesOAuth2AssertionToken` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `GetProvenanceAudit_ReturnsRawFhirPayloadAndAuditTrail` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-003, PRD-SYS-001 | 🟢 PASSED |
| `PollHospitalEhr_IngestsCandidatesWithDiscrepancyClassification` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `RaiseClinicalQuery_RegistersQueryAndUpdatesCandidateStatus` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002 | 🟢 PASSED |
| `RegisterAndListEndpoints_SuccessfullyManagesEhrEndpoints` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-001 | 🟢 PASSED |
| `RejectCandidate_RequiresRationaleAndPreservesOriginalPayload` | `Cadence.UnitTests.Interop.EsourceReconciliationTests` | PRD-ESR-002, PRD-ESR-003, PRD-SYS-001 | 🟢 PASSED |
| `DatabaseLockToken_IssueAndRevoke_UpdatesTokenStateAndAuditTrail` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ExecuteInSandbox_MockedDriver_ExecutesProgramAndGeneratesArtifactsAndManifestDigest` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ExecuteInSandbox_WhenContainerUnavailable_FailsClosedWithNonZeroExitCodeAndErrorOutput` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ExecuteInSandbox_WhenRuntimeFails_ReturnsNonZeroExitCodeStderrAndNoSyntheticData` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ExecutionManifest_VerifySeal_ReturnsValidWhenUntampered` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ExecutionRun_WhenExitCodeNonZero_SetsStatusFailedAndSuppressesSignatureAndSeal` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ValidateLockfile_TamperedDigest_ReturnsTamperedStatus` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ValidateLockfile_UnqualifiedPackageWithStrictQualification_RejectsPackage` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ValidateLockfile_ValidRenvLock_ParsesRPackages` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `ValidateLockfile_ValidUvLock_ReturnsValidStatusAndParsedPackages` | `Cadence.UnitTests.Interop.StatisticalComputingEnvironmentTests` | PRD-SYS-053 | 🟢 PASSED |
| `EveryRead_IsRefused_WhenTheSignedTenantIsNotAKernelTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | 🟢 PASSED |
| `Navigation_ReadsInTheCallersTenant` | `Cadence.UnitTests.Kernel.Api.KernelApiHostTests` | PRD-CRF-013 | 🟢 PASSED |
| `EnrollmentTransitionsFollowTheLifecycle` | `Cadence.UnitTests.Kernel.EnrollmentTests` | PRD-SUB-001 | 🟢 PASSED |
| `KernelDbContextTenantScopedEntitiesUseCompositeTenantForeignKeys` | `Cadence.UnitTests.Kernel.KernelDbContextModelTests` | PRD-CRF-013 | 🟢 PASSED |
| `ForkLeavesTheParentApprovedAndStartsAnAmendedDraft` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `LifecycleAllowsOnlyTheSpecifiedTransitions` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `SubmitForReviewSetsTheContentHashReturnToDraftClearsIt` | `Cadence.UnitTests.Kernel.ProtocolVersionTests` | PRD-CRF-012 | 🟢 PASSED |
| `EvaluatorIsDeterministicOverGeneratedPacksAndRecords` | `Cadence.UnitTests.Kernel.Rules.RulePackTests` | PRD-CRF-004 | 🟢 PASSED |
| `Map_GlucoseInConventionalUnits_ConvertsToSi` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-LBM-001 | 🟢 PASSED |
| `Map_SystolicBloodPressure_WritesOriginalAndStandardResults` | `Cadence.UnitTests.Kernel.Standards.MappingSpecificationTests` | PRD-CDR-002 | 🟢 PASSED |
| `LedgerGeneratedHistoriesResolveExactlyWhenTheLatestRunMissesTheFinding` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | 🟢 PASSED |
| `LedgerRerunWithoutDetectionResolvesWithTheRunAsEvidence` | `Cadence.UnitTests.Kernel.Validation.ValidationFindingTests` | PRD-CRF-004 | 🟢 PASSED |
| `Consume_DuplicateClinicalQueryRaised_IsIdempotent` | `Cadence.UnitTests.Notifications.Consumers.ClinicalQueryRaisedConsumerTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_ValidClinicalQueryRaised_CreatesNotificationRecord` | `Cadence.UnitTests.Notifications.Consumers.ClinicalQueryRaisedConsumerTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_DuplicateEconsentReconsentRequired_IsIdempotent` | `Cadence.UnitTests.Notifications.Consumers.EconsentReconsentConsumerTests` | PRD-SYS-001 | 🟢 PASSED |
| `Consume_ValidEconsentReconsentRequired_CreatesNotificationRecordDeliveriesAndAuditLogs` | `Cadence.UnitTests.Notifications.Consumers.EconsentReconsentConsumerTests` | PRD-SYS-001 | 🟢 PASSED |
| `PreTool_RefusesDestructiveCommands` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | 🟢 PASSED |
| `PreTool_RefusesPayloads` | `Cadence.UnitTests.Packages.AgentHookGuardTests` | PRD-SYS-053 | 🟢 PASSED |
| `Deidentify_DoesNotGenerateOrSignCertificate_WhenPixelMaskingFails` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-IMG-002 | 🟢 PASSED |
| `MaskBurnedInPixels_ThrowsDicomPixelDeidException_WhenPixelMaskingDecodingFails` | `Cadence.UnitTests.Packages.DeidEngineUnitTests` | PRD-IMG-002 | 🟢 PASSED |
| `ForwardedHttps_FromATrustedProxyOnly_GetsHsts` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `HttpsResponses_CarryHsts_OutsideDevelopment` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `TrustedProxies_AcceptAddressesAndCidrRanges` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `TrustedProxies_WithAnInvalidEntry_Throw` | `Cadence.UnitTests.Packages.HttpHardeningTests` | PRD-SYS-189 | 🟢 PASSED |
| `Issue_ForAnAuditor_IsRefused` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Issue_WritesTheV3ContractClaims_WithASixtySecondLifetime` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_AcceptsABoundTokenOnce` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_ExpiredToken_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_TokenSignedWithAnotherSecret_IsRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_TokensOutsideTheV3Contract_AreRejected` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `Verify_WithAnotherSignerActionOrBatch_IsRejectedWithoutConsumingTheToken` | `Cadence.UnitTests.Packages.SignatureTokenTests` | SRS-SYS-005 | 🟢 PASSED |
| `ExtractReceiptOcr_ParsesMetadataAndRecordsAudit` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_Exceeding100Threshold_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_MealSurpassingPerDiemCap_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002 | 🟢 PASSED |
| `SubmitExpenseClaim_Under100WithHighConfidence_AutoApprovesAndDispatchesACH` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-PAT-002, PRD-PAY-006, PRD-SYS-182 | 🟢 PASSED |
| `SubmitExpenseClaim_UnverifiedW9Over600Ytd_RoutesToPendingReview` | `Cadence.UnitTests.Payments.ExpenseClaimAutoApprovalTests` | PRD-SYS-182 | 🟢 PASSED |
| `ApproveDsur_AppliesPart11SignatureBlock_AndLocksRecord` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-008 | 🟢 PASSED |
| `CompileDsur_CalculatesPatientYearsExposureAndMedDraCrossTabulation` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `Endpoints_CompileAndApprove_ReturnCreatedAndOkWithValidSignature` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `UpdateDsur_GivenLockedRecord_Returns409Conflict` | `Cadence.UnitTests.Safety.DsurCompilerEngineTests` | PRD-PVG-004 | 🟢 PASSED |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | PRD-SYS-001 | 🟢 PASSED |

## 4. Performance Qualification (PQ) & Scenario Validation

Qualified Requirements: 29 of 195

## 5. Qualification Review & Authorization

```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Integrity Stamp

- **Status:** Unsigned working copy, generated by `dotnet cadence gxp sync`. Not an electronic signature.
- **Content Hash (SHA-256):** c25b77313cbd9c04610bb93dc6b94c00444e9cbac37019595b8c586bac2354ce
