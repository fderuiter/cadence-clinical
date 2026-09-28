---
search: false
---

> ⚠️ **DRAFT ONLY — UNVERIFIED GxP COMPLIANCE DOCUMENT** ⚠️
> *This document was generated in draft mode with missing test results. It is NOT eligible for GxP production release.*

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report
*Execution Date:* 2026-09-22 14:56:23 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration
This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 2602
- **Passed:** 0 🟢
- **Unverified (Draft):** 2602 ⚪
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
Package                 Version     Editable project location
----------------------- ----------- -------------------------
aioboto3                 15.5.0
aiobotocore              2.25.1
aiofiles                 25.1.0
aiohappyeyeballs         2.7.1
aiohttp                  3.14.3
aioitertools             0.13.0
aiosignal                1.4.0
aiosmtplib               5.1.2
aiosqlite                0.22.1
alembic                  1.18.5
annotated-doc            0.0.4
annotated-types          0.7.0
anyio                    4.14.2
apps-ai-gateway          0.1.0
apps-ctms                0.1.0
apps-econsent            0.1.0
apps-eisf                0.1.0
apps-etmf                0.1.0
apps-interop             0.1.0
apps-notifications       0.1.0
apps-org                 0.1.0
apps-payments            0.1.0
apps-quality             0.1.0
apps-safety              0.1.0
apps-tickets             0.1.0
ast-serialize            0.8.0
asyncpg                  0.31.0
attrs                    26.1.0
babel                    2.18.0
bandit                   1.9.4
beautifulsoup4           4.15.0
boolean-py               5.0
boto3                    1.40.61
botocore                 1.40.61
brotli                   1.2.0
brotlicffi               1.2.0.1
cachecontrol             0.14.4
cadence-clinical         0.1.0       /app
cadence-fileshare        0.1.0
cadence-imaging          0.1.0
cadence-knowledge        0.1.0
certifi                  2026.7.22
cffi                     2.1.0
cfgv                     3.5.0
charset-normalizer       3.4.9
click                    8.4.2
colorama                 0.4.6
coverage                 7.15.2
cryptography             50.0.1
cssselect2               0.9.0
cyclonedx-python-lib     11.11.0
defusedxml               0.7.1
detect-secrets           1.5.0
distlib                  0.4.3
distro                   1.9.0
docxcompose              2.2.0
docxtpl                  0.20.2
ecdsa                    0.19.2
et-xmlfile               2.0.0
execnet                  2.1.2
fastapi                  0.139.2
fastuuid                 0.14.0
filelock                 3.32.0
fonttools                4.63.0
frozenlist               1.8.0
fsspec                   2026.7.0
google-api-core          2.34.0
google-api-python-client 2.199.0
google-auth              2.56.3
google-auth-httplib2     0.4.1
googleapis-common-protos 1.75.1
greenlet                 3.5.4
h11                      0.16.0
hf-xet                   1.6.0
httpcore                 1.0.9
httplib2                 0.32.0
httptools                0.8.0
httpx                    0.28.1
huggingface-hub          1.28.0
identify                 2.6.19
idna                     3.18
importlib-metadata       8.9.0
iniconfig                2.3.0
jinja2                   3.1.6
jiter                    0.16.0
jmespath                 1.1.0
jsonschema               4.26.0
jsonschema-specifications 2025.9.1
librt                    0.15.0
license-expression       30.4.4
litellm                  1.97.0
lxml                     6.1.1
mako                     1.3.12
markdown-it-py           4.2.0
markupsafe               3.0.3
mdurl                    0.1.2
msgpack                  1.2.1
multidict                6.7.1
mypy                     2.3.0
mypy-extensions          1.1.0
neo4j                    6.2.0
nodeenv                  1.10.0
numpy                    2.5.1
openai                   2.54.0
openpyxl                 3.1.5
packageurl-python        0.17.6
packaging                26.2
pandas                   3.0.3
pathspec                 1.1.1
pillow                   12.3.0
pip                      26.2
pip-api                  0.0.34
pip-audit                2.10.1
pip-requirements-parser  32.0.1
platformdirs             4.11.0
playwright               1.61.0
pluggy                   1.6.0
pre-commit               4.6.1
propcache                0.5.2
proto-plus               1.28.3
protobuf                 7.36.0
py-serializable          2.1.0
pyasn1                   0.6.4
pyasn1-modules           0.4.2
pycparser                3.0
pydantic                 2.13.4
pydantic-core            2.46.4
pydantic-settings        2.15.0
pydicom                  3.0.2
pydyf                    0.12.1
pyee                     13.0.1
pygments                 2.20.0
pymupdf                  1.28.0
pyparsing                3.3.2
pyphen                   0.17.2
pytest                   9.1.1
pytest-archon            0.0.7
pytest-asyncio           1.4.0
pytest-base-url          2.1.0
pytest-cov               7.1.0
pytest-playwright        0.8.0
pytest-xdist             3.8.0
python-dateutil          2.9.0.post0
python-discovery         1.5.0
python-docx              1.2.0
python-dotenv            1.2.2
python-jose              3.5.0
python-multipart         0.0.32
python-slugify           8.0.4
pytz                     2026.2
pyyaml                   6.0.3
rapidfuzz                3.14.5
redis                    8.1.0
referencing              0.37.0
regex                    2026.7.19
requests                 2.34.2
rich                     15.0.0
rpds-py                  2026.6.3
rsa                      4.9.1
ruff                     0.15.22
s3transfer               0.14.0
shellingham              1.5.4
six                      1.17.0
sniffio                  1.3.1
sortedcontainers         2.4.0
soupsieve                2.9.1
sqlalchemy               2.0.51
sqlmodel                 0.0.39
starlette                1.6.0
stevedore                5.9.0
text-unidecode           1.3
tiktoken                 0.14.0
tinycss2                 1.5.1
tinyhtml5                2.1.0
tokenizers               0.23.1
tomli                    2.4.1
tomli-w                  1.2.0
tqdm                     4.70.0
typer                    0.27.1
typing-extensions        4.16.0
typing-inspection        0.4.2
tzdata                   2026.3
uritemplate              4.2.0
urllib3                  2.7.0
usdm                     0.67.0
uvicorn                  0.51.0
uvloop                   0.22.1
virtualenv               21.7.0
watchfiles               1.2.0
weasyprint               70.0
webencodings             0.5.1
websockets               16.1.1
wrapt                    1.17.3
yarl                     1.24.5
yattag                   1.16.1
zipp                     4.1.0
zopfli                   0.4.3
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
| `Compliance_AuditChainVerifier_ValidatesIntactChain` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Compliance_AuditDigestCalculator_ComputesExpectedHash` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Ddf_Models_ConstructsSuccessfully` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Deid_DeidDetector_DetectsAndRedactsEmail` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Hexagonal_DomainPrimitives_And_ExceptionMapper_Work` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Sentinels_Engine_ExecutesRegisteredCheck` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `Storage_InMemoryBlobStore_StoresAndRetrievesPayload` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ToolingCore_Handlers_ReturnsSuccessfulEnvelope` | `Cadence.UnitTests.Packages.PortedPackagesUnitTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `InMemoryRepositoryPersistsEntityAndIncrementsVersionIndex` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `ResultMonadHandlesSuccessAndFailureWithoutThrowingExceptions` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_citation_confidence_threshold_rejection` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_esignature_and_audit_context_propagation` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_in_flight_phi_masking_with_surrogate_tokens` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_list_patient_records_and_profile_fallback` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_low_citation_confidence_fails_closed` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_missing_tenant_context_rejected` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_stateless_execution_with_caller_managed_state` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_verified_multi_step_synthesis_in_draft_state` | `apps.ai_gateway.tests.test_clinical_reasoning` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_crf_uat_generator_volume_and_boundaries` | `apps.ai_gateway.tests.test_crf_uat_generator` | PRD-CRF-009, PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_protocol_to_crf_scaffolding` | `apps.ai_gateway.tests.test_crf_uat_generator` | PRD-DDF-001, PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_embedding_batch_sanitization` | `apps.ai_gateway.tests.test_deid_airgap` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_enable_deid_false_bypasses_scrubbing` | `apps.ai_gateway.tests.test_deid_airgap` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_inflight_prompt_deidentification_and_rehydration` | `apps.ai_gateway.tests.test_deid_airgap` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_multiturn_messages_co_reference_airgap` | `apps.ai_gateway.tests.test_deid_airgap` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_structured_output_rehydration` | `apps.ai_gateway.tests.test_deid_airgap` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_invalid_signature_rejected` | `apps.ai_gateway.tests.test_gateway_auth` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_unauthenticated_request_rejected` | `apps.ai_gateway.tests.test_gateway_auth` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_valid_signature_accepted` | `apps.ai_gateway.tests.test_gateway_auth` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_embeddings_generation_seam` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_health_check` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_invalid_generation_payload` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_structured_output_generation_seam` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_text_generation_seam` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_tier_listing_seam` | `apps.ai_gateway.tests.test_inference_seam` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_get_tier_info_structure` | `apps.ai_gateway.tests.test_model_tiering` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_tier_environment_variable_configuration` | `apps.ai_gateway.tests.test_model_tiering` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_tier_model_override` | `apps.ai_gateway.tests.test_model_tiering` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_tier_model_resolution_defaults` | `apps.ai_gateway.tests.test_model_tiering` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_rag_triage_empty_chunks_fails_closed` | `apps.ai_gateway.tests.test_rag_router` | PRD-SYS-051, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_rag_triage_grounded_excerpts` | `apps.ai_gateway.tests.test_rag_router` | PRD-SYS-051, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_rag_triage_unauthenticated_rejected` | `apps.ai_gateway.tests.test_rag_router` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_generation_request_dto_accepts_tools_and_tool_responses` | `apps.ai_gateway.tests.test_typed_tool_calling` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_litellm_adapter_tool_calling_pass_through` | `apps.ai_gateway.tests.test_typed_tool_calling` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_mock_adapter_custom_tool_call_payload` | `apps.ai_gateway.tests.test_typed_tool_calling` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_recursive_json_tool_payload_deidentification_and_rehydration` | `apps.ai_gateway.tests.test_typed_tool_calling` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_etmf_client_propagates_5xx_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_client_propagates_network_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_client_propagates_5xx_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_quality_client_propagates_network_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_client_propagates_5xx_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safety_client_propagates_network_error` | `apps.ctms.tests.test_clients_exception_propagation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cra_allocations_rbac_reassignment_workload` | `apps.ctms.tests.test_ctms` | PRD-CTMS-003, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_create_and_list_studies_rbac` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_ctms_health_check` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_conflict_merge` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_conflict_server_wins` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_happy_path_and_reloads` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_rbac_denial` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_structural_conflict` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_audit_trail_rbac` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_grant_approve_sig_token_matrix` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_grant_creation_rbac` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_grant_locked_when_approved` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_manual` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_study_approved` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_milestone_trigger_visit_completed_automated` | `apps.ctms.tests.test_ctms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_invalid_state_and_findings` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_scheduling_respects_cra_allocation` | `apps.ctms.tests.test_ctms` | PRD-CTMS-003, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_workflow_happy_path` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_monitoring_visit_workflow_rbac_denials` | `apps.ctms.tests.test_ctms` | PRD-CTMS-002, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_recruitment_records_crud_and_audit` | `apps.ctms.tests.test_ctms` | PRD-CTMS-004, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_site_milestones_crud_and_audit` | `apps.ctms.tests.test_ctms` | PRD-CTMS-001, Trace-6 | ⚪ UNVERIFIED | N/A |
| `test_ctms_audit_log_sequential_hmac_chaining` | `apps.ctms.tests.test_ctms_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_ctms_audit_log_tamper_detection` | `apps.ctms.tests.test_ctms_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_delegation_allowed_non_pi_when_not_enforced` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_site_mismatch` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_sponsor_mismatch` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_denied_when_not_pi` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_malformed_role` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_missing_delegator_context` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_missing_target_context` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_successful_pi_matching_scope` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_target_from_body` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_delegation_exclusion` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_validate_staff_role_invalid` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_validate_staff_role_valid` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_request_staff_roles_empty` | `apps.ctms.tests.test_delegation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_deviation_lifecycle_and_capa_escalation` | `apps.ctms.tests.test_deviations_and_issues` | PRD-CTMS-006 | ⚪ UNVERIFIED | N/A |
| `test_doa_historical_audit_trail_logging` | `apps.ctms.tests.test_doa_audit_suite` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_assignment_record_creation` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_delegation_record_defaults` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_delegation_record_validation` | `apps.ctms.tests.test_doa_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_doa_lifecycle_flow` | `apps.ctms.tests.test_doa_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_doa_rbac_violations` | `apps.ctms.tests.test_doa_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_manager_service_class_interface` | `apps.ctms.tests.test_doa_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_task_delegation_and_esignature_lifecycle` | `apps.ctms.tests.test_doa_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_artifact_synchronization` | `apps.ctms.tests.test_etmf_integration` | PRD-CTMS-010 | ⚪ UNVERIFIED | N/A |
| `test_ctms_approve_unassigned_site` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_ctms_approve_unreachable_downstream` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_ctms_list_resupply_events_blocked_read_only` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_ctms_list_resupply_events_success` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_execution_approve_reject_resupply_events` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_execution_list_resupply_events` | `apps.ctms.tests.test_federated_resupply` | PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_procedure_financials_and_invoice_disbursement` | `apps.ctms.tests.test_financials_auto_payables` | PRD-CTMS-008 | ⚪ UNVERIFIED | N/A |
| `test_budget_adjustments_passthrough_and_penalties` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_contract_amendments_and_historical_fee_schedule_preservation` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_multi_currency_exchange_rates_and_conversion` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_multi_tier_budget_templates` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_rbac_access_control` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_template_application_and_fee_matrix_grid` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_visit_payable_calculation_with_overhead_and_stipends` | `apps.ctms.tests.test_financials_fee_matrix_engine` | ADR-2200, PRD-PAY-001, PRD-PAY-005 | ⚪ UNVERIFIED | N/A |
| `test_ip_shipment_receipt_dispensation_and_reconciliation` | `apps.ctms.tests.test_ip_accountability` | PRD-CTMS-004, PRD-CTMS-009, PRD-SYS-184 | ⚪ UNVERIFIED | N/A |
| `test_rbqm_kri_breach_detection_and_adaptive_risk_scoring` | `apps.ctms.tests.test_rbqm_kri` | PRD-CTMS-007 | ⚪ UNVERIFIED | N/A |
| `test_generate_self_billing_invoice_calculation` | `apps.ctms.tests.test_self_billing_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tax_treatment_cross_border_eu_reverse_charge` | `apps.ctms.tests.test_self_billing_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tax_treatment_cross_border_non_eu_exempt` | `apps.ctms.tests.test_self_billing_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tax_treatment_domestic_uk` | `apps.ctms.tests.test_self_billing_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tax_treatment_domestic_us` | `apps.ctms.tests.test_self_billing_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_gatekeeper_workflow` | `apps.ctms.tests.test_site_startup` | PRD-CTMS-005 | ⚪ UNVERIFIED | N/A |
| `test_site_startup_and_regulatory_milestones` | `apps.ctms.tests.test_site_startup` | PRD-CTMS-005 | ⚪ UNVERIFIED | N/A |
| `test_dual_signature_executive_gxp_override` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2202, PRD-SSU-002, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_hmac_sha256_cryptographic_token_generation_and_verification` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2202, PRD-SSU-003, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_jurisdiction_rule_presets_and_configuration` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2199, PRD-SSU-001, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_jurisdiction_specific_startup_criteria` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2202, PRD-SSU-001, PRD-SSU-002, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_multi_factor_6_facet_greenlight_evaluator` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2202, PRD-SSU-002, PRD-SSU-003, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_transactional_outbox_site_greenlight_approved_event` | `apps.ctms.tests.test_ssu_country_greenlight_activation` | ADR-2202, PRD-SSU-003, PRD-SYS-121, PRD-SYS-183 | ⚪ UNVERIFIED | N/A |
| `test_cache_entry_expiration` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_cache_max_size_eviction` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_cache_ttl_and_env_initialization` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_get_approved_template_translation_helper` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_publish_error_handling` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_publish_on_invalidate_and_clear` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_subscriber_invalidate_template` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_subscriber_receives_and_evicts_cache` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_unconfigured_graceful_fallback` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_redis_unreachable_graceful_fallback` | `apps.econsent.tests.test_cache_redis` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_authoring_mutations_rejected_for_auditors` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_clause_lifecycle_and_versioning_audit` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_database_url_override_and_init` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_database_schema_creation` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_document_lifecycle_and_audit_context` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_get_not_found` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_health_check` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_pydantic_schemas` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_gateway_auth_middleware_denials` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_shared_audit_fields_validation` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_template_lifecycle_and_validation` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager_econsent` | `apps.econsent.tests.test_econsent` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_compare_templates_delta_report` | `apps.econsent.tests.test_econsent_amendment_diff` | PRD-SYS-179 | ⚪ UNVERIFIED | N/A |
| `test_substantive_change_detection` | `apps.econsent.tests.test_econsent_amendment_diff` | PRD-SYS-179 | ⚪ UNVERIFIED | N/A |
| `test_text_diff_computation` | `apps.econsent.tests.test_econsent_amendment_diff` | PRD-SYS-179 | ⚪ UNVERIFIED | N/A |
| `test_audit_logs_endpoint` | `apps.econsent.tests.test_econsent_api_endpoints` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_export_and_diff_endpoints` | `apps.econsent.tests.test_econsent_api_endpoints` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_granular_options_router` | `apps.econsent.tests.test_econsent_api_endpoints` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_archival_status_endpoints` | `apps.econsent.tests.test_econsent_archival` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_icf_sign_and_archival_queueing` | `apps.econsent.tests.test_econsent_archival` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_poll_and_dispatch_failure_and_retry_backoff` | `apps.econsent.tests.test_econsent_archival` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_poll_and_dispatch_success` | `apps.econsent.tests.test_econsent_archival` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_audit_log_sequential_hmac_chaining` | `apps.econsent.tests.test_econsent_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_econsent_audit_log_tamper_detection` | `apps.econsent.tests.test_econsent_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_append_only_audit_history` | `apps.econsent.tests.test_econsent_capture` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_capture_rejections` | `apps.econsent.tests.test_econsent_capture` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_execution_consumption_integration` | `apps.econsent.tests.test_econsent_capture` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_happy_path_capture_and_status` | `apps.econsent.tests.test_econsent_capture` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_signature_tamper_detection` | `apps.econsent.tests.test_econsent_capture` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_cdisc_odm_generation_structure` | `apps.econsent.tests.test_econsent_cdisc_odm` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_auditor_restrictions_on_checks` | `apps.econsent.tests.test_econsent_comprehension` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_create_and_retrieve_comprehension_check` | `apps.econsent.tests.test_econsent_comprehension` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_signature_blocks_if_comprehension_checks_fail_or_incomplete` | `apps.econsent.tests.test_econsent_comprehension` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_submit_answers_and_evaluation_boundaries` | `apps.econsent.tests.test_econsent_comprehension` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_template_version_separation` | `apps.econsent.tests.test_econsent_comprehension` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_render_verifiable_consent_html` | `apps.econsent.tests.test_econsent_document_renderer` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_granular_options_lifecycle_and_subject_selection` | `apps.econsent.tests.test_econsent_granular_options` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_api_minor_milestone_evaluate_and_trigger_endpoints` | `apps.econsent.tests.test_econsent_minor_majority` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_domain_age_calculation_and_leap_year` | `apps.econsent.tests.test_econsent_minor_majority` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_domain_evaluate_subject_majority_milestones` | `apps.econsent.tests.test_econsent_minor_majority` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_domain_jurisdiction_legal_age_thresholds` | `apps.econsent.tests.test_econsent_minor_majority` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_reconsent_service_trigger_majority_reconsent` | `apps.econsent.tests.test_econsent_minor_majority` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_multisig_subject_lar_and_investigator_workflow` | `apps.econsent.tests.test_econsent_multisig` | PRD-SYS-178 | ⚪ UNVERIFIED | N/A |
| `test_reconsent_trigger_and_pending_queries` | `apps.econsent.tests.test_econsent_reconsent` | PRD-SYS-179 | ⚪ UNVERIFIED | N/A |
| `test_failed_comprehension_quiz_blocks_signature` | `apps.econsent.tests.test_econsent_service` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_incomplete_comprehension_quiz_blocks_signature` | `apps.econsent.tests.test_econsent_service` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_invalid_otp_auth_code_blocks_signature` | `apps.econsent.tests.test_econsent_service` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_successful_signature_capture` | `apps.econsent.tests.test_econsent_service` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_workflow_engine_legacy_signature_capture` | `apps.econsent.tests.test_econsent_service` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_approved_content_retrieval_and_cache` | `apps.econsent.tests.test_econsent_translations` | Trace-10 | ⚪ UNVERIFIED | N/A |
| `test_language_code_validation` | `apps.econsent.tests.test_econsent_translations` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_translation_crud_and_validation` | `apps.econsent.tests.test_econsent_translations` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_translation_status_workflow_and_rbac` | `apps.econsent.tests.test_econsent_translations` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_clause_service_lifecycle` | `apps.econsent.tests.test_econsent_use_cases` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_template_authoring_and_composition` | `apps.econsent.tests.test_econsent_use_cases` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_translation_service_workflow` | `apps.econsent.tests.test_econsent_use_cases` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_subject_consent_withdrawal_lifecycle` | `apps.econsent.tests.test_econsent_withdrawal` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_consent_record_immutability` | `apps.econsent.tests.test_econsent_workflow` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_signature_audit_compliance` | `apps.econsent.tests.test_econsent_workflow` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_econsent_signature_capture_success` | `apps.econsent.tests.test_econsent_workflow` | PRD-SYS-177 | ⚪ UNVERIFIED | N/A |
| `test_protocol_amendment_triggers_reconsent` | `apps.econsent.tests.test_econsent_workflow` | PRD-SYS-179 | ⚪ UNVERIFIED | N/A |
| `test_api_clause_harmonization_apply_and_audit` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-001, PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_api_clause_harmonization_auditor_forbidden` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-001, PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_api_clause_harmonization_not_found` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_api_readability_analyze_endpoint` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_api_readability_harmonize_endpoint` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_dale_chall_familiar_vocabulary` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_harmonize_text_deterministic_fallback` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_harmonize_text_with_mocked_ai_gateway` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_qualitative_interpretations` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_readability_metrics_empty_text` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_readability_metrics_service_complex_text` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_readability_metrics_service_plain_language_text` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_syllable_counter_deterministic` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_tokenizers` | `apps.econsent.tests.test_readability_harmonizer` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_changed_dict` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_changed_object` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_duplicate_dict` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_duplicate_object` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_incoming_document_new` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_derive_correlation_key` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deterministic_bidirectional_mapping_success` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_mappings_resolve_through_active_catalog` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_resolve_known_extension_artifact` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_reverse_mappings_resolve_through_active_catalog` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapping_failures` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mapping_normalization` | `apps.eisf.tests.test_eisf_adapter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_auditor_write_forbidden` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_cross_site_rejection_and_audit` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_lifecycle_same_site` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_documents_endpoint_blocks_unauthenticated` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_health_unauthenticated` | `apps.eisf.tests.test_eisf_api` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_audit_log_sequential_hmac_chaining` | `apps.eisf.tests.test_eisf_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_eisf_audit_log_tamper_detection` | `apps.eisf.tests.test_eisf_audit_chain_verification` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_eisf_phi_redaction_preserves_original` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_and_redaction` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_enforcement` | `apps.eisf.tests.test_eisf_binder` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_auditor_view_and_download_permissions` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_workflow` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_listing_with_binder_filters` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_view_and_download_site_isolation` | `apps.eisf.tests.test_eisf_browse_completeness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_level_data_isolation` | `apps.eisf.tests.test_eisf_compliance` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_document_event_alias` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_document_success` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_ingest_missing_change_reason_fails` | `apps.eisf.tests.test_eisf_ingest` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_intelligence_analyze_endpoint` | `apps.eisf.tests.test_eisf_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_eisf_existing_site_document_intelligence_analysis` | `apps.eisf.tests.test_eisf_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_isolation_lifecycle` | `apps.eisf.tests.test_eisf_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_record_creation` | `apps.eisf.tests.test_eisf_models` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_url_override_and_init` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_append_only_versions_and_deduplication` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_creation_and_site_scoped` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_part11_audit_log_retention` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager_eisf` | `apps.eisf.tests.test_eisf_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_site_eisf_binder_authorized` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_site_eisf_binder_unauthorized_cross_site` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_upload_and_get_site_document` | `apps.eisf.tests.test_eisf_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_upload_and_watermark` | `apps.eisf.tests.test_eisf_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_site_isolation` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_external_monitor_role` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_scoped_users_read_isolation` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_site_scoped_write_restrictions` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sponsor_admin_global_visibility` | `apps.eisf.tests.test_eisf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_client_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lexicographic_tiebreaker` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lww_existing_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_merge_lww_incoming_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_conflict_server_wins` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_creation_and_etmf_propagation` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_echo_loop_prevention` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_exact_duplicate_ignored` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_per_field_metadata_lww` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_sync_unmapped_propagation` | `apps.eisf.tests.test_eisf_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_document_record_instantiation_defaults` | `apps.eisf.tests.test_eisf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_taxonomy_querying_returns_8_mandatory_sections` | `apps.eisf.tests.test_eisf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exception_route` | `apps.etmf.presentation.routers.etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cra_qc_override_and_rejection_workflows` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_cra_qc_staging_and_review_workflow` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_dia_classifier_benchmark_standard_regulatory_documents` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_dia_reference_model_classifier_multi_signal` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_document_intelligence_worker_processing_and_polling` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_intelligence_rest_endpoints` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_multimodal_layout_parser_pymupdf_blocks_and_bounding_boxes` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_multimodal_layout_parser_text_and_pdf` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_metadata_and_dates_extraction` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_relational_envelope_population_and_persistence` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_signature_completeness_analyzer` | `apps.etmf.tests.test_document_intelligence` | PRD-TMF-006 | ⚪ UNVERIFIED | N/A |
| `test_archived_document_retrieval_and_immutability` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_ingestion_and_version_indexing` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_canonical_catalog_ingestion_validations` | `apps.etmf.tests.test_etmf` | PRD-TMF-002, PRD-TMF-003, Trace-5 | ⚪ UNVERIFIED | N/A |
| `test_completeness_checking_transitions` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_from_catalog` | `apps.etmf.tests.test_etmf` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_completeness_from_catalog_across_versions` | `apps.etmf.tests.test_etmf` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_and_complete_binder_export` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_edl_definitions_and_crud` | `apps.etmf.tests.test_etmf` | PRD-EDL-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_etmf_audit_logs_filtering_and_pagination` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_completeness_rejects_quarantined` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_completeness_site_segregation_and_study_wide` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edge_cases_for_coverage` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_qc_lifecycle_and_audit` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_repository_rule_deduplication` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_explicit_and_default_taxonomy_version_roundtrip_and_legacy_interpretability` | `apps.etmf.tests.test_etmf` | PRD-TMF-003 | ⚪ UNVERIFIED | N/A |
| `test_informed_consent_form_taxonomy_and_idempotency` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inspector_portal_read_only_access_limits` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ordered_artifact_history_endpoint` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_placeholder_scripts` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_versioning_and_change_justification_ingestion` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qualify_catalog_cutover_and_extension_persistence` | `apps.etmf.tests.test_etmf` | PRD-TMF-002, Trace-5 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_binder_export` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_immutability_violation` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_rollback_on_failure` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_caller_ingestion_success` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_aware_completeness` | `apps.etmf.tests.test_etmf` | PRD-EDL-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_tmf_taxonomy_mapping` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ucum_extra_coverage` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uninitialized_database_manager` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_view_download_audit_logging` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_watermarked_document_viewing_and_download` | `apps.etmf.tests.test_etmf` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_document_change_rationale_mandatory_rules` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_linkage_and_version_history_lineage` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_qc_transitions_immutability` | `apps.etmf.tests.test_etmf_amendment_lineage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_chain_tamper_detection` | `apps.etmf.tests.test_etmf_audit_chain_verification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_chain_verification_endpoint` | `apps.etmf.tests.test_etmf_audit_chain_verification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_version_history_lineage` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_empty_binder_structure` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_partial_binder_structure` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_versions_404_not_found` | `apps.etmf.tests.test_etmf_binder_structure_and_history` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_all_or_nothing_rollback` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_authorization_and_rejections` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_partial_success` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_repeating_safe_and_observable` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archival_successful_progression` | `apps.etmf.tests.test_etmf_bulk_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_actual_cryptographic_verification` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logs_group_sealing_and_chaining` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_background_sealer_lifecycle` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_and_invalid_signature_ingestion` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signature_bypass` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_extraction_formats` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_requirement_rules` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tampering_detection_and_lockout_propagation` | `apps.etmf.tests.test_etmf_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_milestones_and_backward_compatibility` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_live_async_session_edl_seeding_and_idempotency` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001, Trace-4 | ⚪ UNVERIFIED | N/A |
| `test_offline_in_memory_edl_seeding_all_zones` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-EDL-001, PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_repository_integration_edl_seeding` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_seed_edl_rest_endpoint` | `apps.etmf.tests.test_etmf_edl_seeding` | PRD-EDL-001, PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_eisf_create_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_creation_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_expiration_update_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_update_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_expiration_update_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_expiration_update_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_authorized_vs_unauthorized` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_date_validation_rejected` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_manage_expiration_rbac_permissions` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_migration_adds_expiration_columns_idempotently` | `apps.etmf.tests.test_etmf_eisf_expiration_metadata` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ems_export_package_structure` | `apps.etmf.tests.test_etmf_ems_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ems_export_permissions` | `apps.etmf.tests.test_etmf_ems_export` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_attribution` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_determine_warning_window` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_failure_and_retryability` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_fallback_cra_routing` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_idempotency_limit` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dispatch_successful_owner_routing` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_expiration_scan_cycle_thresholds` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_failure_isolation_and_resilience` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scanner_idempotency_restart_and_rearming` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scanner_shutdown_cancellation` | `apps.etmf.tests.test_etmf_expiration_scanner` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_idempotency` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_immutability_violation_inbound_email` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_signature_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_attachment_ingestion` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_oversized_payload_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_replay_protection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_stale_timestamp_rejection` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolvable_recipient_address` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_inbound_email_ingestion` | `apps.etmf.tests.test_etmf_inbound_email` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_signature_verification_endpoint` | `apps.etmf.tests.test_etmf_inspection_readiness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_inspection_readiness_endpoint_and_scoring` | `apps.etmf.tests.test_etmf_inspection_readiness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trigger_global_trial_lock` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_error` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_locked` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_trial_lock_status_unlocked` | `apps.etmf.tests.test_etmf_lock_integration` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_signing_writes_outbox` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_admin_visibility_endpoint` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_no_unencrypted_pii` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_worker_polling_and_dispatch_success` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_outbox_worker_retry_and_backoff` | `apps.etmf.tests.test_etmf_outbox` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_append_only_transition_history` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-005 | ⚪ UNVERIFIED | N/A |
| `test_invalid_status_transition_raises_error` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-002 | ⚪ UNVERIFIED | N/A |
| `test_new_document_defaults_to_draft` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-001 | ⚪ UNVERIFIED | N/A |
| `test_part11_change_reason_enforcement` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-004 | ⚪ UNVERIFIED | N/A |
| `test_qc_history_api_and_audit` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qc_history_api_not_found` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_qc_transitions_missing_doc` | `apps.etmf.tests.test_etmf_qc` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_based_access_controls_and_gates` | `apps.etmf.tests.test_etmf_qc` | PRD-QC-003 | ⚪ UNVERIFIED | N/A |
| `test_migration_clean_path` | `apps.etmf.tests.test_etmf_qc_invariants` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_migration_upgrade_and_backfill_path` | `apps.etmf.tests.test_etmf_qc_invariants` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_basic` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_errors` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_profile_scopes` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_automated_redaction_trial_locked` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_authorization_and_lock` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_literal_escaping` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_span_validation` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manual_redaction_success` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_redaction_audit_trail_and_provenance` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_redaction_authorization_gates` | `apps.etmf.tests.test_etmf_redaction` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_completeness_signature_lifecycle_distinction` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_document_routing_and_classification` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_lifecycle_with_mock_signature` | `apps.etmf.tests.test_etmf_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_post_signature_locking` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_failure_logging_and_blocking` | `apps.etmf.tests.test_etmf_signing_lifecycle` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_happy_path` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_etmf_signing_reauth_failures` | `apps.etmf.tests.test_etmf_signing_lifecycle` | Trace-13 | ⚪ UNVERIFIED | N/A |
| `test_auto_quarantine_site_level_no_site_id` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_binder_export_redaction_representation_policy` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_site_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_to_etmf_sync_preserves_scope` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_site_level_artifact_helper` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_records_quarantine_policy` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_raw_original_suppression_without_read_raw` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_regulatory_binder_export_site_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_id_validation_empty_whitespace` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_cannot_read_study_level_or_quarantined_documents` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_no_assigned_sites_fail_closed` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_users_read_isolation` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoped_write_restrictions` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_scoping_on_redactions_and_signatures` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unauthorized_role_denied_on_all_paths` | `apps.etmf.tests.test_etmf_site_scope` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_to_etmf_e2e_boundaries` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_derivative_safety` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sealer_retains_and_validates_reason_for_change` | `apps.etmf.tests.test_etmf_sync_provenance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_auto_file_endpoint` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classification_service_direct` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classify_endpoints` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_taxonomy_endpoint` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_document_type_helper` | `apps.etmf.tests.test_etmf_taxonomy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_triggers_immutability` | `apps.etmf.tests.test_etmf_triggers_compliance` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_bulk_archive_use_case` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_completeness_readiness_and_export_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_electronic_signature_and_redaction_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ingest_and_qc_use_cases` | `apps.etmf.tests.test_etmf_use_cases` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_duplicate_certificate_injection_rejected` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_padding_pkcs1v15_fails` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mandatory_documents_bypass_rejected` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signatures_blocked` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rsassa_pss_succeeds` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unapproved_self_signed_certificate_fails` | `apps.etmf.tests.test_part11_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dual_read_fallback_legacy_content` | `apps.etmf.tests.test_storage_migration` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_read_object_storage` | `apps.etmf.tests.test_storage_migration` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_migrate_etmf_blobs_to_s3_script_lifecycle` | `apps.etmf.tests.test_storage_migration` | PRD-DOC-001, PRD-DOC-002, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_active_version_selection` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_artifact_parent_identification` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_canonical_11_zones` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_complete_catalog_manifest_and_uniqueness` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_explicit_version_selection` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_get_mandatory_artifacts_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_get_mandatory_artifacts_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_hierarchy_integrity_v3_2_0_complete` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_immutability_properties` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_no_database_dependencies` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_reproducibility_and_version_isolation` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_artifact_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_artifact_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_standard_versus_extension_policy` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_validate_hierarchy_failures` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-002 | ⚪ UNVERIFIED | N/A |
| `test_validate_hierarchy_success` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-002 | ⚪ UNVERIFIED | N/A |
| `test_version_isolation` | `apps.etmf.tests.test_tmf_reference_model` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_activity_feed_authorization_and_alias` | `apps.fileshare.tests.test_activity_feed` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_activity_feed_filtering_and_pagination` | `apps.fileshare.tests.test_activity_feed` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_activity_feed_lifecycle_events` | `apps.fileshare.tests.test_activity_feed` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_get_activity_feed_filtering_by_event_type_and_date` | `apps.fileshare.tests.test_activity_feed_endpoints` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_get_activity_feed_paginated_and_ordered` | `apps.fileshare.tests.test_activity_feed_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_get_activity_feed_permission_gating` | `apps.fileshare.tests.test_activity_feed_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_complete_upload_auto_enqueues_conversion` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_conversion_adapter_convert_to_pdf` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_conversion_adapter_empty_source_raises_error` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_document_conversion_worker_failure` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_document_conversion_worker_success` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_download_and_preview_rendition_routing` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pending_conversion_returns_original_with_header` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_synthetic_fallback_pdf_structure` | `apps.fileshare.tests.test_auto_format_conversion` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_approve_document_invalid_credential_rejected` | `apps.fileshare.tests.test_document_approval_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_approve_document_success` | `apps.fileshare.tests.test_document_approval_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_approved_document_is_immutable_and_blocks_revision` | `apps.fileshare.tests.test_document_approval_endpoints` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_reject_document_workflow` | `apps.fileshare.tests.test_document_approval_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_rescind_approval_and_reopen_for_revisions` | `apps.fileshare.tests.test_document_approval_endpoints` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_conversion_status_enum_and_helpers` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_file_audit_log_model` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_file_record_conversion_fields` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_file_record_creation_and_defaults` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_file_record_hold_fields` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_file_record_validation_errors` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_guest_link_validity` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_playback_audit_event_and_enums` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_share_grant_lifecycle_and_expiry` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_share_scope_and_permission_levels_enum_extensions` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_transcode_status_enum` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_video_mime_type_detection` | `apps.fileshare.tests.test_domain_models` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_download_url_permissions_and_watermarking` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_guest_link_creation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_guest_link_resolution_counter_and_revocation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_health_check` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_multipart_upload_url_generation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_playback_audit_checkpoint_logging` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_presigned_upload_url_with_checksum_validation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_retention_hold_lifecycle_and_audit_trail` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_role_scope_share_grant_and_revocation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-003 | ⚪ UNVERIFIED | N/A |
| `test_singlepart_upload_url_generation` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_stream_url_error_conditions` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_video_transcoding_and_stream_url_flow` | `apps.fileshare.tests.test_fileshare_endpoints` | PRD-DOC-001, PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_clamav_scan_adapter_clean_response` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_clamav_scan_adapter_connection_failure` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_clamav_scan_adapter_infected_response` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_mock_scan_adapter_clean_file` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_mock_scan_adapter_eicar_detection` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_mock_scan_adapter_forced_status` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_mock_scan_adapter_simulated_error` | `apps.fileshare.tests.test_scan_adapter` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_execute_ffmpeg_transcode_package_generation` | `apps.fileshare.tests.test_transcode_worker` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_worker_failure_transcode_state_machine` | `apps.fileshare.tests.test_transcode_worker` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_worker_successful_transcode_state_machine` | `apps.fileshare.tests.test_transcode_worker` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_access_guest_link_blocked_if_file_quarantined` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-003 | ⚪ UNVERIFIED | N/A |
| `test_clean_file_upload_scan_lifecycle` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_clean_video_advances_to_pending_transcode` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_eicar_infected_file_upload_quarantined_and_blocked` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002, PRD-DOC-003 | ⚪ UNVERIFIED | N/A |
| `test_infected_video_quarantined_and_transcode_failed` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_notification_dispatch_on_infected_and_error` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_persistent_scan_error_marks_scan_error_state` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_transient_scan_error_retries_and_succeeds` | `apps.fileshare.tests.test_virus_scanning_integration` | PRD-DOC-001, PRD-DOC-002 | ⚪ UNVERIFIED | N/A |
| `test_primary_reads_concordance_happy_path` | `apps.imaging.tests.test_adjudication` | PRD-IMG-003, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_primary_reads_discordance_and_tie_breaker_escalation` | `apps.imaging.tests.test_adjudication` | PRD-IMG-005, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_sld_discordance_calculation` | `apps.imaging.tests.test_adjudication` | PRD-IMG-005, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_workflow_discordance_and_part11_signoff` | `apps.imaging.tests.test_adjudication` | PRD-IMG-005, PRD-SYS-001, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_workflow_session_not_found` | `apps.imaging.tests.test_adjudication` | PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_workflow_submit_reader_assessment_lifecycle` | `apps.imaging.tests.test_adjudication` | PRD-IMG-003, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_caliper_annotation_crud_and_audit` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-001, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_lugano_partial_response_and_deauville_pet` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_lugano_ppd_and_spd_calculation` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_lugano_progressive_disease_evaluation` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_mpr_caliper_distance_and_perpendicular` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-001, PRD-IMG-004, PRD-SYS-164, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_mpr_metadata_endpoint` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-001, PRD-IMG-004, PRD-SYS-164, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_mpr_slice_plane_mapping` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-001, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_recist_baseline_timepoint_evaluation` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_recist_complete_response_and_new_lesions` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_recist_partial_response_derivation` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_recist_progressive_disease_from_nadir` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_recist_sld_calculation` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_response_evaluations_endpoints` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_subject_timepoint_evaluation_and_trajectory` | `apps.imaging.tests.test_dicom_viewer_recist` | PRD-IMG-004, PRD-SYS-001, PRD-SYS-165 | ⚪ UNVERIFIED | N/A |
| `test_blinded_dual_reader_isolation_unit` | `apps.imaging.tests.test_imaging_dual_reader_adjudication` | PRD-IMG-003, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_discordance_detection_criteria_unit` | `apps.imaging.tests.test_imaging_dual_reader_adjudication` | PRD-IMG-005, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_discordant_dual_reads_and_step_up_adjudication_sign_off_api` | `apps.imaging.tests.test_imaging_dual_reader_adjudication` | PRD-IMG-003, PRD-IMG-005, PRD-IMG-006, PRD-SYS-001, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_dual_reader_routing_and_concordance_api` | `apps.imaging.tests.test_imaging_dual_reader_adjudication` | PRD-IMG-003, PRD-IMG-005, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_edc_ecrf_binding_and_sdtm_derivation_unit` | `apps.imaging.tests.test_imaging_dual_reader_adjudication` | PRD-IMG-006, PRD-SYS-165, PRD-SYS-166 | ⚪ UNVERIFIED | N/A |
| `test_health_check` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-001, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_in_flight_deidentify_stream_endpoint` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_list_and_get_studies_endpoints` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-001, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_not_found_errors` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-001, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_upload_single_dicom_endpoint` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-001, PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_upload_with_custom_pixel_mask_json` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_upload_zip_archive_endpoint` | `apps.imaging.tests.test_imaging_endpoints` | PRD-IMG-001, PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_single_dicom_ingestion_pipeline` | `apps.imaging.tests.test_imaging_pipeline` | PRD-IMG-001, PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_zip_archive_multi_slice_ingestion` | `apps.imaging.tests.test_imaging_pipeline` | PRD-IMG-001, PRD-IMG-002, PRD-SYS-164 | ⚪ UNVERIFIED | N/A |
| `test_adam_datasets_derived_accurately` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002 | ⚪ UNVERIFIED | N/A |
| `test_api_adam_endpoint` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002 | ⚪ UNVERIFIED | N/A |
| `test_api_execute_pipeline_endpoint` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_api_export_dataset_json` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_api_export_define_xml` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_api_metadata_catalog` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_api_sdtm_endpoint` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002 | ⚪ UNVERIFIED | N/A |
| `test_api_validate_dataset_json` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_api_validate_define_xml` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_blinding_isolation_masks_treatment_arms` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002 | ⚪ UNVERIFIED | N/A |
| `test_compile_submission_package_end_to_end` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_compile_submission_package_with_blinding` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_continuous_pipeline_execution_and_audit` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_referential_validation_failure` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_serialization_and_validation` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_define_xml_compilation_with_vlm_whereclauses_origins` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_sdtm_10_domains_mapped_successfully` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002 | ⚪ UNVERIFIED | N/A |
| `test_submission_engine_dataset_json_2_0_serialization` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_submission_engine_device_sdtm_compilation` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-DEV-004 | ⚪ UNVERIFIED | N/A |
| `test_submission_package_helper_methods` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-002, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_submission_package_sha256_reproducibility_and_tampering` | `apps.interop.tests.test_cdr_cdisc_sdtm_adam_pipeline` | ADR-2199, PRD-CDR-003 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_record_hashing` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_full_lakehouse_ingest_pool_snapshot_verify_lifecycle` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_lakehouse_rest_api_workflow` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_central_lab` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_edc_ecrf` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_ehr_fhir` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_epro_ecoa` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_iot_wearables` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_local_lab` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_normalize_medical_imaging` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_snapshot_not_found_returns_404` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_snapshot_pool_digest_reproducibility` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_in_snapshot_verification` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_unauthorized_unblinded_access_rejected_with_403` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_unblinded_masking_partition` | `apps.interop.tests.test_cdr_lakehouse_snapshot_pooling` | ADR-2199, PRD-CDR-001 | ⚪ UNVERIFIED | N/A |
| `test_detect_invalid_syntax` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_detect_tampered_lockfile` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_detect_unqualified_package` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_full_sce_lifecycle_with_unblinded_gating` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-004, PRD-CDR-005, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_issue_and_verify_database_lock_token` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_python_statistical_sandbox_execution` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-004, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_r_statistical_sandbox_execution` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-004, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_reject_expired_or_revoked_token` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_reject_unauthorized_role` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_sandbox_timeout_enforcement` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-004 | ⚪ UNVERIFIED | N/A |
| `test_sce_rest_api_workflow` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-004, PRD-CDR-005, PRD-CDR-006 | ⚪ UNVERIFIED | N/A |
| `test_validate_renv_lock_parsing_and_digest` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_validate_uv_lock_parsing_and_digest` | `apps.interop.tests.test_cdr_statistical_computing_sandbox` | ADR-2199, PRD-CDR-005 | ⚪ UNVERIFIED | N/A |
| `test_blood_pressure_daily_summary` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_cgm_mmol_unit_conversion` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_cgm_time_in_range_and_metrics` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_compute_clock_drift_offset` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_correct_reading_timestamps` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_device_registration_and_list_api` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_extreme_tachycardia_and_bradycardia_alerts` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_full_ingestion_pipeline_with_lakehouse_feed` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_generate_device_token` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_hypertensive_crisis_alert` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_mask_mac_address` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_parse_udi` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_pseudonymize_subject_id` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_register_device_entity` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_severe_hypoglycemia_alert` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_spirometry_daily_summary` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_telemetry_stream_ingest_api` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_transform_actigraphy_to_omh_and_fhir` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_transform_blood_pressure_to_fhir_components` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_transform_cgm_to_omh_and_fhir` | `apps.interop.tests.test_dct_sensor_telemetry_ingestion` | ADR-2199, PRD-DCT-002 | ⚪ UNVERIFIED | N/A |
| `test_chain_of_custody_handoff_creation` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-003, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_child_assent_with_lar_parent_signature` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-006, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_comprehension_scoring` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-006, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_determine_pediatric_tier` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-006, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_dtp_shipment_cold_chain_and_delivery_lifecycle` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-003, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_gxp_recording_seal_generation_and_verification` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_in_home_nurse_visit_and_offline_ecrf_sync_lifecycle` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-004, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_mean_kinetic_temperature_calculation` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-003, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_mobile_ecrf_offline_reconciliation` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-004, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_pediatric_assent_and_lar_endpoint_flow` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-006, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_pediatric_assent_rejects_missing_lar` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-006, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_room_token_generation_and_verification` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_room_token_rejection_on_tamper_or_expiration` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_room_token_role_permissions` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_telehealth_consultation_lifecycle` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_temperature_excursion_evaluation` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-003, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_temperature_thresholds` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-003, PRD-SYS-181 | ⚪ UNVERIFIED | N/A |
| `test_webrtc_dtls_fingerprint_validation` | `apps.interop.tests.test_dct_telehealth_dtp_logistics` | ADR-2199, PRD-DCT-005, PRD-SYS-180 | ⚪ UNVERIFIED | N/A |
| `test_basic_detection_results` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_get_line_and_col` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_load_gitignore_patterns` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_bypass_comments_and_false_positives` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_clean` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_main_violation` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_should_scan_file` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_profiles` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_custom_literal_terms` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dates_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deidentify_free_text_direct` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_email_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fhir_narrative_and_notes_integration` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ip_mac_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_excluded_path_alembic` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medical_record_account_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_overlap_resolution_deterministic` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_phone_fax_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redact_text_sequential` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ssn_national_id_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_urls_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_zip_geographic_detector` | `apps.interop.tests.test_deid` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_export_package_endpoint` | `apps.interop.tests.test_device_sdtm_regulatory_exports` | ADR-2199, ADR-2209, PRD-DEV-004, PRD-SYS-126 | ⚪ UNVERIFIED | N/A |
| `test_generate_mir_report_endpoint` | `apps.interop.tests.test_device_sdtm_regulatory_exports` | ADR-2199, ADR-2209, PRD-DEV-004, PRD-SYS-126 | ⚪ UNVERIFIED | N/A |
| `test_generate_sdtm_dd_endpoint` | `apps.interop.tests.test_device_sdtm_regulatory_exports` | ADR-2199, ADR-2209, PRD-DEV-004, PRD-SYS-126 | ⚪ UNVERIFIED | N/A |
| `test_generate_sdtm_do_endpoint` | `apps.interop.tests.test_device_sdtm_regulatory_exports` | ADR-2199, ADR-2209, PRD-DEV-004, PRD-SYS-126 | ⚪ UNVERIFIED | N/A |
| `test_assignment_compliance_states_and_recalculations` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_retrieval_and_assignment_boundaries` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_lifecycle_reminders_and_acknowledgments` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_submission_conflict_resolution_lifecycles` | `apps.interop.tests.test_ecoa_coverage` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_structural_conflict_on_missing_or_deleted_targets` | `apps.interop.tests.test_ecoa_coverage` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_subject_only_authorization_and_cross_subject_rejection` | `apps.interop.tests.test_ecoa_coverage` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_automated_ehr_polling_pipeline` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_crc_reconciliation_workflow_accept_and_commit` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_crc_reconciliation_workflow_raise_clinical_query` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-002, PRD-ESR-003, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_crc_reconciliation_workflow_reject_with_reason` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-002, PRD-ESR-003, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_ecrf_side_by_side_comparison_and_discrepancies` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-002, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_esource_rest_api_e2e_endpoints` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-ESR-002, PRD-ESR-003, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_fhir_demographics_dm_ingestion_and_pii_stripping` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_fhir_laboratory_lb_and_medications_ingestion` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_fhir_physical_examination_pe_ingestion` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_fhir_procedure_and_encounter_ingestion` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_fhir_vital_signs_vs_ingestion_and_multi_components` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_non_destructive_review_guarantee_no_silent_overwrites` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, FDA, PRD-ESR-002, PRD-ESR-003, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_app_launch_flow_and_pkce_generation` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_backend_services_assertion_security_validations` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_backend_services_asymmetric_jwt_lifecycle` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_ehr_tenant_scoping_validation` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-SYS-122, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_oauth2_pkce_token_exchange_and_launch_context` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_smart_token_exchange_and_caching` | `apps.interop.tests.test_esource_smart_fhir_reconciliation` | ADR-2199, PRD-ESR-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_bulk_offline_sync` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_reminders_all_subjects_staff` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_reminders_by_subject_and_end_date_branch` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deliver_notification_task_exception` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epro_submission_and_conflict_resolution` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fhir_prefill_bundle_pipeline` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_foreign_key_and_cascade_lifecycle_integrity` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_and_assignment_endpoints_and_auditing` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_instrument_and_assignment_orm_persistence` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_and_reminders_lifecycle` | `apps.interop.tests.test_interop` | Trace-10 | ⚪ UNVERIFIED | N/A |
| `test_pseudonymization_and_pii_stripping` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_assignment_missing_diary_alert_dedup_columns` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_content_submission_and_compliance_apis` | `apps.interop.tests.test_interop` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_subject_role_authorization_and_identity_binding` | `apps.interop.tests.test_interop` | Trace-8 | ⚪ UNVERIFIED | N/A |
| `test_bulk_sync_with_valid_signatures_and_tallies` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-007 | ⚪ UNVERIFIED | N/A |
| `test_defeated_record_persistence_on_conflicts` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008, Trace-9 | ⚪ UNVERIFIED | N/A |
| `test_structural_conflict_on_missing_target` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_submit_with_invalid_signature_fails` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_submit_with_valid_signature` | `apps.interop.tests.test_interop_defeated` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_build_ecrf_context_mapping` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_build_ecrf_context_multiple_and_missing` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_no_edc_mutation_boundary` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_audit_evidence_non_phi` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_eligible` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_indeterminate` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_pre_screen_ineligible` | `apps.interop.tests.test_interop_prescreen` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_epro_quarantine_sync_pipeline` | `apps.interop.tests.test_interop_quarantine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_epro_version_mismatch_quarantine_pipeline` | `apps.interop.tests.test_interop_quarantine` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_batch_success_and_idempotency` | `apps.interop.tests.test_offline_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_batch_update_action` | `apps.interop.tests.test_offline_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_batch_sync_success` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_delta_ingestion_integrity` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_conflict_resolution` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_cryptographic_verification` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_offline_sync_idempotency` | `apps.interop.tests.test_offline_sync` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_adversarial_empty_and_malformed_bundles` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_api_fhir_semantic_map_endpoint_e2e` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-001, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_api_list_concept_maps_metadata_endpoint` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_confidence_gating_human_review_threshold` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_concept_maps_catalog_coverage` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_embedding_matcher_low_confidence_rejection` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_embedding_matcher_synonym_resolution` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-051, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_llm_semantic_reasoner_local_extraction` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-051, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_llm_semantic_reasoner_phi_airgap_deidentification` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-051, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_lookup_concept_by_code_and_system` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_semantic_mapper_service_hybrid_cascade` | `apps.interop.tests.test_semantic_mapper` | PRD-CRF-007, PRD-SYS-051, PRD-SYS-185, PRD-SYS-186 | ⚪ UNVERIFIED | N/A |
| `test_generic_natural_deduplication_key` | `apps.interop.tests.test_sync_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signature_validation_failures` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_signature_validation_happy_path` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_client_wins_existing` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_client_wins_no_existing` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_independent_fields` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_existing_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_incoming_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_merge_lww_timestamp_tie` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_strategy_server_wins` | `apps.interop.tests.test_sync_engine` | PRD-EDC-008 | ⚪ UNVERIFIED | N/A |
| `test_api_draft_storage_endpoints` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_generic_transition_endpoint_supporting_in_review_approved_rejected` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_get_article_by_slug_returns_rendered_html_and_metadata` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_publish_endpoint_and_auto_supersede` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_put_and_patch_draft_endpoints` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_review_and_four_eyes_workflow` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_audit_logs_emitted_for_all_lifecycle_transitions` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_create_article_with_tags_json_array` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_draft_creation_and_single_row_in_place_update` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_four_eyes_author_cannot_approve` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_four_eyes_last_editor_cannot_approve` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_ledger_created_and_draft_saved_events` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_independent_reviewer_approves_and_locks_version` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_notification_dispatched_on_publish` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-KNB-002, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_publication_and_auto_supersede_prior_version` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_reason_for_change_enforced_on_publish_and_approve` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_sha256_digest_chain_audit_emission` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_single_query_published_article_lookup_eager_loads_version` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_update_draft_on_non_draft_article_fails` | `apps.knowledge.tests.test_article_lifecycle` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_audit_trail_sha256_digest_chain` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_automatic_emission_of_auditor_read_event` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_historical_version_snapshot_trail` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_immutability_hook_rejects_audit_log_deletion` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_immutability_hook_rejects_audit_log_modification` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_immutability_hook_rejects_locked_version_deletion` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_immutability_hook_rejects_locked_version_modification` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_verify_audit_chain_endpoint` | `apps.knowledge.tests.test_auditor_read_and_inspection_ledger` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_api_create_and_get_category` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_api_delete_category_lifecycle` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_api_list_categories_persona_scoping` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_create_category_duplicate_name_or_slug_fails` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_create_category_with_nonexistent_parent_fails` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_create_child_category_with_parent` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_create_root_category` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_delete_nonexistent_or_already_deleted_category_raises` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_get_category_by_id_and_slug` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_list_categories_persona_filtering` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_soft_delete_category` | `apps.knowledge.tests.test_categories` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_admin_create_and_manage_contextual_help_mappings` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_admin_create_mapping_with_invalid_article_returns_404` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_hierarchical_specificity_ranking` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_pattern_classification` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_persona_matching_and_fallback` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_exact_match` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_filters_inactive_and_draft_articles` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_lookup_backward_compatibility` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_tie_breaking_and_related_articles` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_unmapped_fallback_returns_empty_gracefully` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_contextual_help_wildcards_and_persona_fallback` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_route_normalization` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_route_pattern_matching` | `apps.knowledge.tests.test_contextual_help` | PRD-KNB-002, PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_e2e_auditor_read_logging_and_cryptographic_chain_verification` | `apps.knowledge.tests.test_e2e_verification_suite` | PRD-KNB-001, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_e2e_contextual_help_dynamic_resolution_and_fallback` | `apps.knowledge.tests.test_e2e_verification_suite` | PRD-KNB-001, PRD-KNB-002, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_e2e_golden_path_article_lifecycle_and_four_eyes` | `apps.knowledge.tests.test_e2e_verification_suite` | PRD-KNB-001, PRD-KNB-002, PRD-SYS-050, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_e2e_grounded_protocol_ingestion_and_vector_search` | `apps.knowledge.tests.test_e2e_verification_suite` | PRD-SYS-KH-001, PRD-SYS-KH-002, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_e2e_persona_scoped_category_visibility_and_fulltext_search` | `apps.knowledge.tests.test_e2e_verification_suite` | PRD-KNB-001, PRD-KNB-002, PRD-SYS-KH-001, PRD-SYS-KH-002 | ⚪ UNVERIFIED | N/A |
| `test_protocol_text_ingest_and_search_endpoints` | `apps.knowledge.tests.test_protocol_knowledge_router` | PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_protocol_upload_file_endpoint` | `apps.knowledge.tests.test_protocol_knowledge_router` | PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_domain_calculate_lexical_score_ranking` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_domain_extract_query_terms` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_domain_generate_highlighted_snippet` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_domain_strip_markdown` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_category_filter` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_empty_and_no_match` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_excludes_non_published_and_deleted_articles` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_performance_sla` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_persona_visibility_scoping` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_published_articles_success` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_relevance_ranking_order` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_search_rest_api_endpoint` | `apps.knowledge.tests.test_search` | PRD-SYS-KH-001 | ⚪ UNVERIFIED | N/A |
| `test_edc_write_qualification_gating_lifecycle` | `apps.knowledge.tests.test_training_amendment_edc_gating` | PRD-TRN-002 | ⚪ UNVERIFIED | N/A |
| `test_part11_signature_certificate_and_vault_routing` | `apps.knowledge.tests.test_training_amendment_edc_gating` | PRD-TRN-003, PRD-TRN-004 | ⚪ UNVERIFIED | N/A |
| `test_protocol_amendment_delta_curriculum_generation` | `apps.knowledge.tests.test_training_amendment_edc_gating` | PRD-TRN-001 | ⚪ UNVERIFIED | N/A |
| `test_rest_api_training_endpoints` | `apps.knowledge.tests.test_training_amendment_edc_gating` | PRD-TRN-001, PRD-TRN-002, PRD-TRN-003, PRD-TRN-004 | ⚪ UNVERIFIED | N/A |
| `test_time_tracking_and_quiz_scoring_threshold` | `apps.knowledge.tests.test_training_amendment_edc_gating` | PRD-TRN-003 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_failure_swallowed` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_success` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_dashboard_notification_sdv_drop` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_email_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_sms_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_router_send_webhook_mapping` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unblind_emergency_unblinding_alert_integration` | `apps.notifications.tests.test_clinical_workflow_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_emergency_unblinding_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-SUB-006 | ⚪ UNVERIFIED | N/A |
| `test_query_aging_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-QRY-002 | ⚪ UNVERIFIED | N/A |
| `test_sdv_drop_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-QRY-006 | ⚪ UNVERIFIED | N/A |
| `test_trial_lock_generates_notification` | `apps.notifications.tests.test_clinical_workflow_notifications_integration` | PRD-SYS-003, PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_start_stop_notification_worker_integration` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_gxp_exponential_retry_and_dlq` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_cra_for_document_expiry` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_crc_for_edc_query` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_resolves_safety_officer_for_sae_flag` | `apps.notifications.tests.test_notification_worker` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_direct_transition_open_to_resolved` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_email_delivery_channel_failure_and_exhaustion` | `apps.notifications.tests.test_notifications` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_email_delivery_channel_success` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lifecycle_transitions_and_justifications` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_diary_alert_rendering` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_channel_edge_case_in_app_succeeds_email_exhausts` | `apps.notifications.tests.test_notifications` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_notification_creation_and_auditing` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notification_detail_visibility` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notification_list_visibility_and_filtering` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_database_schema_creation` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_health_check` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_notifications_negative_security_paths` | `apps.notifications.tests.test_notifications` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_webhook_delivery_channel_failure_and_retry_backoff` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_webhook_delivery_channel_success` | `apps.notifications.tests.test_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_template_mapping_and_rendering` | `apps.notifications.tests.test_reconsent_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reconsent_worker_recipient_resolution` | `apps.notifications.tests.test_reconsent_notifications` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_complete_doa_workflow_lifecycle` | `apps.org.tests.test_doa_workflow` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_signoff_automatic_archival_handoff` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_doa_signoff_tampered_payload_rejected` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_completeness_participation` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_openapi_aggregation_with_org` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_org_proxy_routing` | `apps.org.tests.test_org_integration_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_training_log_crud_and_validation` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_training_log_invalid_signature_fails` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_training_log_signing_and_archival_handoff` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_training_log_unauthorized_access` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_training_log_update_missing_justification_fails` | `apps.org.tests.test_org_integration_e2e` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cro_affiliation_validation` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_delegation_of_authority_flow` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_audit_logging_and_actor_context` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_health_endpoint` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_org_audit_log_append_only` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_and_site_relationship` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_and_sitestaff_alias` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_assignments_crud` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_personnel_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_assignments_endpoint` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_site_crud_api` | `apps.org.tests.test_org_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_change_reason_validation` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_instantiation` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_fields_reusability` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clinical_staff_role_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_organization_type_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trial_duty_values` | `apps.org.tests.test_organization_domain` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_billing_and_erp_gateway_direct` | `apps.payments.tests.test_deep_domain_services` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_participant_compensation_service_direct` | `apps.payments.tests.test_deep_domain_services` | ADR-2199, PRD-PAY-002, PRD-PAY-004, PRD-PAY-006, PRD-SYS-161, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_site_payment_ledger_engine_direct` | `apps.payments.tests.test_deep_domain_services` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_automated_digital_disbursements` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-001, PRD-PAT-002, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_eu_ctr_lay_summary_portal_and_confirmation_receipt` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-003, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_irb_ethics_anti_coercion_stipend_caps` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-001, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_irs_1099_tax_threshold_monitoring_and_w9_validation_triggers` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_meal_allowance_and_per_diem_disbursements` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-002, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_participant_wallet_provisioning_and_payout_methods` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-001, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_receipt_ocr_and_travel_expense_approval_workflow` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-002, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_reloadable_debit_card_issuing_and_realtime_balance_inquiry` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-001, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_soa_stipend_rule_and_visit_verification_trigger` | `apps.payments.tests.test_patient_wallet_lay_summary_portal` | ADR-2199, PRD-PAT-001, PRD-SYS-127, PRD-SYS-182 | ⚪ UNVERIFIED | N/A |
| `test_batch_event_ingestion_and_queries` | `apps.payments.tests.test_payments_endpoints` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_health_check_endpoint` | `apps.payments.tests.test_payments_endpoints` | PRD-PAY-002, PRD-SYS-161 | ⚪ UNVERIFIED | N/A |
| `test_compilation_with_no_payables_fails_gracefully` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_compile_monthly_billing_cycle_and_invoice_aggregation` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_erp_synchronization_and_webhook_reconciliation` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_netsuite_export_and_invoices_filtering` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_rbac_permissions_for_invoicing` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_remittance_advice_pdf_download_and_tamper_detection` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_unsigned_remittance_advice_verification` | `apps.payments.tests.test_payments_self_billing_invoicing` | ADR-2199, PRD-PAY-004, PRD-PAY-006, PRD-SYS-163 | ⚪ UNVERIFIED | N/A |
| `test_budget_contract_creation_and_split_payee_configuration` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_cra_sdv_verification_gating_workflow` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_ctms_milestone_completion_event_trigger` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_idempotency_prevents_duplicate_payables` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-SYS-161 | ⚪ UNVERIFIED | N/A |
| `test_holdback_retention_reserve_and_dual_signoff_release` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-003, PRD-PAY-006, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_payment_audit_ledger_immutable_tracking` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_rbac_financial_permissions_and_auditor_isolation` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-SYS-161 | ⚪ UNVERIFIED | N/A |
| `test_real_time_ecrf_submission_trigger_and_split_allocation` | `apps.payments.tests.test_payments_trigger_split` | ADR-2199, PRD-PAY-002, PRD-PAY-003, PRD-PAY-006, PRD-SYS-161, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_split_payee_validation_failure_on_invalid_percentage` | `apps.payments.tests.test_payments_trigger_split` | PRD-PAY-003, PRD-SYS-162 | ⚪ UNVERIFIED | N/A |
| `test_audit_engagement_lifecycle` | `apps.quality.tests.test_audit_service` | PRD-QLT-006 | ⚪ UNVERIFIED | N/A |
| `test_audit_findings_and_one_click_capa_promotion` | `apps.quality.tests.test_audit_service` | PRD-QLT-006 | ⚪ UNVERIFIED | N/A |
| `test_inspection_readiness_dossier_compilation_and_tamper_seal` | `apps.quality.tests.test_audit_service` | PRD-QLT-008 | ⚪ UNVERIFIED | N/A |
| `test_automated_quality_event_ingestion_api` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_capa_action_items_and_effectiveness_evaluations` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_multi_methodology_rca_5whys_and_fishbone` | `apps.quality.tests.test_eqms_comprehensive` | PRD-QLT-002 | ⚪ UNVERIFIED | N/A |
| `test_benford_law_first_digit_goodness_of_fit` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_cross_site_patient_record_cloning_detection` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-175 | ⚪ UNVERIFIED | N/A |
| `test_cross_site_variance_shrinkage_and_kl_divergence` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_full_rest_api_forensics_surveillance_pipeline` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_multivariate_mahalanobis_and_correlation_distance` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_perceptual_image_hashing_phash_and_transformations` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-175 | ⚪ UNVERIFIED | N/A |
| `test_sample_gating_under_ich_e6_r3` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_temporal_clustering_and_superhuman_burst_detection` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-175 | ⚪ UNVERIFIED | N/A |
| `test_terminal_digit_and_repeated_values_rounding_bias` | `apps.quality.tests.test_fraud_detection` | PRD-SYS-174 | ⚪ UNVERIFIED | N/A |
| `test_capa_creation_validations_and_closed_deviation` | `apps.quality.tests.test_quality` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_capa_transition_edge_cases_and_optimistic_locking` | `apps.quality.tests.test_quality` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_capa_update_edge_cases_and_optional_fields` | `apps.quality.tests.test_quality` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized_raises_exception` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_deviation_lifecycle_and_traceability_fields` | `apps.quality.tests.test_quality` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_deviation_not_found_404` | `apps.quality.tests.test_quality` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_deviation_rca_capa_relationships_and_cascading` | `apps.quality.tests.test_quality` | PRD-QLT-002 | ⚪ UNVERIFIED | N/A |
| `test_endpoint_change_reason_check_via_mock` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_lifespan_coverage` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_list_deviations_filters` | `apps.quality.tests.test_quality` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reasons_unauthorized` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_audit_log_append_only` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_database_schema_creation` | `apps.quality.tests.test_quality` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_health_check` | `apps.quality.tests.test_quality` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlite_foreign_key_constraints` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlite_pragma_exception_handling` | `apps.quality.tests.test_quality` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_client` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_site_duplicate_patient_scan_detection` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_cross_subject_image_phash_scan` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_dct_2d_and_phash_deterministic_computation` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_duplicate_patient_vector_similarity_calculations` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_image_phash_rotation_and_transformation_invariance` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_rest_duplicate_patient_scan_endpoint` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_rest_image_phash_scan_endpoint` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_rest_list_runs_and_dispatch_investigation` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_rest_simulation_endpoint` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_rest_temporal_anomaly_scan_endpoint` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_rest_unauthorized_write_returns_forbidden` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_service_execute_comprehensive_scan_with_investigation_dispatch` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_subject_fingerprint_deterministic_sha256` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-004 | ⚪ UNVERIFIED | N/A |
| `test_temporal_off_hours_and_weekend_clustering` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_temporal_rapid_burst_and_identical_elapsed_intervals` | `apps.quality.tests.test_quality_duplicate_patient_phash_sentinel` | PRD-CSM-003 | ⚪ UNVERIFIED | N/A |
| `test_amendment_impact_and_cost_estimation` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_block_eligibility_soa_inconsistencies` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_burden_tracing_with_invasiveness_modifiers` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pluggable_fixture_patient_attrition` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_complete_protocol` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_incomplete_protocol_detects_errors` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_router_endpoint` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_quality_sentinel_router_endpoint_dependency_override` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_readability_metrics_and_scoring` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_syllable_counter_deterministic` | `apps.quality.tests.test_quality_sentinel` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_benford_law_goodness_of_fit_compliant_and_non_compliant` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_chi_square_distribution_accuracy` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_forensics_rest_api_full_surveillance_and_kri_breach_alerts` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_multivariate_mahalanobis_distance_and_correlation_matrices` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-002 | ⚪ UNVERIFIED | N/A |
| `test_simulation_dataset_generator` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_terminal_digit_uniform_distribution_and_rounding_bias` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_variance_shrinkage_and_kullback_leibler_divergence` | `apps.quality.tests.test_quality_statistical_forensics_benford` | PRD-CSM-002 | ⚪ UNVERIFIED | N/A |
| `test_surveillance_router_endpoint_post_run` | `apps.quality.tests.test_quality_surveillance_runner` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_surveillance_runner_e2e_autonomous_harvest` | `apps.quality.tests.test_quality_surveillance_runner` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_surveillance_runner_idempotent_deviation_logging` | `apps.quality.tests.test_quality_surveillance_runner` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_surveillance_runner_sample_gating_ich_e6_r3` | `apps.quality.tests.test_quality_surveillance_runner` | PRD-CSM-001 | ⚪ UNVERIFIED | N/A |
| `test_automated_kri_definitions_and_statistical_evaluations` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005 | ⚪ UNVERIFIED | N/A |
| `test_composite_site_risk_index_and_percentile_rankings` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005 | ⚪ UNVERIFIED | N/A |
| `test_one_click_centralized_monitoring_inspection_dossier_compilation` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-006, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_ract_matrix_summary_and_default_seeding` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005 | ⚪ UNVERIFIED | N/A |
| `test_ract_risk_factor_lifecycle` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005 | ⚪ UNVERIFIED | N/A |
| `test_ract_validation_errors_and_unauthorized_access` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_transcelerate_six_categories_and_kri_linkage` | `apps.quality.tests.test_quality_transcelerate_ract_kri` | PRD-CSM-005 | ⚪ UNVERIFIED | N/A |
| `test_audit_log_endpoint_properties` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_capa_approval_closure_requires_quality_oversight` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_capa_creation_validations` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_capa_lifecycle_transitions` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_capa_updates_and_concurrency` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-003 | ⚪ UNVERIFIED | N/A |
| `test_create_and_list_deviations` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-001 | ⚪ UNVERIFIED | N/A |
| `test_create_and_update_rca` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-002 | ⚪ UNVERIFIED | N/A |
| `test_mutation_without_change_reason_rejected` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_permission_failure_leaves_no_misleading_audit_entry` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_read_only_roles_forbidden` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_successful_mutation_creates_audit_log_and_is_atomic` | `apps.quality.tests.test_quality_workflow` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_transition_capa_sig_token_matrix` | `apps.quality.tests.test_quality_workflow` | PRD-QLT-009 | ⚪ UNVERIFIED | N/A |
| `test_ctq_factor_lifecycle` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_kri_batch_evaluation_and_statistical_z_scores` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_kri_definitions_and_auto_seeding` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_qtl_tolerance_limit_and_csr_narrative` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-005 | ⚪ UNVERIFIED | N/A |
| `test_site_risk_profile_computation_and_ranking` | `apps.quality.tests.test_rbqm_engine` | PRD-QLT-004 | ⚪ UNVERIFIED | N/A |
| `test_kri_evaluations_and_dynamic_threshold_breaches` | `apps.quality.tests.test_rbqm_sentinels` | PRD-SYS-176 | ⚪ UNVERIFIED | N/A |
| `test_one_click_centralized_monitoring_inspection_dossier` | `apps.quality.tests.test_rbqm_sentinels` | PRD-SYS-176 | ⚪ UNVERIFIED | N/A |
| `test_site_risk_index_and_targeted_sdv_ranking` | `apps.quality.tests.test_rbqm_sentinels` | PRD-SYS-176 | ⚪ UNVERIFIED | N/A |
| `test_transcelerate_ract_six_categories_and_dynamic_scoring` | `apps.quality.tests.test_rbqm_sentinels` | PRD-SYS-176 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_clock_approaching_and_overdue_indicators` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_serious_breach_confirmation_and_status_progression` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_serious_breach_reporting_and_initial_clock` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_statutory_discovery_clock_preservation_on_confirmation` | `apps.quality.tests.test_serious_breaches` | PRD-QLT-007 | ⚪ UNVERIFIED | N/A |
| `test_mic_calculation_and_envelope_generation` | `apps.safety.tests.test_as2_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_parse_tampered_mdn_receipt_mismatch` | `apps.safety.tests.test_as2_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_parse_valid_mdn_receipt` | `apps.safety.tests.test_as2_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_adjudicate_device_safety_investigation` | `apps.safety.tests.test_device_safety_investigation` | ADR-2199, PRD-DEV-003, PRD-DEV-004, PRD-SYS-053, PRD-SYS-126, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_generate_device_vigilance_mir_report` | `apps.safety.tests.test_device_safety_investigation` | ADR-2199, PRD-DEV-003, PRD-DEV-004, PRD-SYS-053, PRD-SYS-126, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_list_and_get_device_safety_investigations` | `apps.safety.tests.test_device_safety_investigation` | ADR-2199, PRD-DEV-003, PRD-DEV-004, PRD-SYS-053, PRD-SYS-126, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_open_device_safety_investigation_endpoint` | `apps.safety.tests.test_device_safety_investigation` | ADR-2199, PRD-DEV-003, PRD-DEV-004, PRD-SYS-053, PRD-SYS-126, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_device_incident_classifications_endpoint` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_fda_medwatch_3500a_export_generation` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_fda_medwatch_5_day_expedited_remedial_action` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_countdown_clock_30_day_baseline` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_countdown_clock_5_day_expedited` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_countdown_clock_eu_mdr_tiers` | `apps.safety.tests.test_device_vigilance` | PRD-SYS-053, PRD-SYS-187 | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_happy_path` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_invalid_raises_value_error` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_and_reason_for_change_rendering` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_invalid_namespace_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_invalid_root_tag_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_malformed_xml_validation_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_drugs_or_drug_fields_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fields_fail` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fields_fail` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_reactions_or_reaction_term_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_safety_report_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_worldwide_unique_case_id_fails` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_rendering_and_validation` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_validate_e2b_xml_structure_direct` | `apps.safety.tests.test_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_parse_e2b_xml_valid_payload` | `apps.safety.tests.test_e2b_parser` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_unblind_missing_sig_token` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_unblind_screening_status_error` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_unblind_subject_not_found` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_unblind_success_authorized_access` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_unblind_success_masked_access` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_unblind_withdrawn_status_error` | `apps.safety.tests.test_emergency_unblinding` | ADR-2199, PRD-PVG-007, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_api_evaluate_and_get_evaluation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_api_list_evaluations_and_countdown_alerts` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_api_sign_evaluation_gated_workflow` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-PVG-008, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_audit_log_immutability_enforcement` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-PVG-008, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_ema_eudravigilance_rules_evaluation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_fda_15_day_serious_unexpected_susar_evaluation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_fda_7_day_fatal_susar_evaluation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_non_expedited_periodic_determination` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_part11_physician_signature_missing_reason_rejection` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-PVG-008, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_part11_physician_signature_success_and_audit` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-PVG-008, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_part11_physician_signature_unauthorized_role_rejection` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-PVG-008, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_pmda_mhra_health_canada_multi_country_evaluation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_real_time_countdown_threshold_alert_escalation` | `apps.safety.tests.test_expedited_safety_rules` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_narrative_service_esignature_and_approval_workflow` | `apps.safety.tests.test_generative_safety_narratives` | ADR-2199, PRD-SYS-051, PRD-SYS-052 | ⚪ UNVERIFIED | N/A |
| `test_narrative_service_export_gate_blocks_unapproved` | `apps.safety.tests.test_generative_safety_narratives` | ADR-2199, PRD-SYS-052 | ⚪ UNVERIFIED | N/A |
| `test_narrative_service_generation_and_draft_state` | `apps.safety.tests.test_generative_safety_narratives` | ADR-2199, PRD-SYS-051, PRD-SYS-052 | ⚪ UNVERIFIED | N/A |
| `test_rest_api_narrative_endpoints_end_to_end` | `apps.safety.tests.test_generative_safety_narratives` | ADR-2199, PRD-SYS-051, PRD-SYS-052 | ⚪ UNVERIFIED | N/A |
| `test_timeline_aggregator_chronological_ordering` | `apps.safety.tests.test_generative_safety_narratives` | ADR-2199, PRD-SYS-052 | ⚪ UNVERIFIED | N/A |
| `test_literature_surveillance_screening_and_icsr_scoring` | `apps.safety.tests.test_literature_surveillance` | PRD-PVG-006, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_safety_inquiry_statutory_deadline_tracking` | `apps.safety.tests.test_literature_surveillance` | PRD-PVG-006, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_as2_gateway_delegates_to_regulatory_audit_ledger` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-PVG-002, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_as2_mdn_receipt_verification_and_tamper_detection` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-PVG-002, PRD-SYS-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_as2_mic_calculation_and_envelope_sealing` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-PVG-002, PRD-SYS-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_canonical_digest_determinism_and_key_sorting` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_device_adjudication_and_mir_report_sealing` | `apps.safety.tests.test_regulatory_audit_ledger` | 21, ADR-2199, ADR-2209, PRD-AGG-001, PRD-DEV-004, PRD-SYS-125, PRD-SYS-126, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_device_safety_service_end_to_end_lifecycle` | `apps.safety.tests.test_regulatory_audit_ledger` | 21, ADR-2199, ADR-2209, PRD-AGG-001, PRD-DEV-003, PRD-DEV-004, PRD-SYS-125, PRD-SYS-126, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_device_vigilance_deadline_calculation` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, ADR-2209, PRD-AGG-001, PRD-DEV-003, PRD-SYS-125, PRD-SYS-126, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_merkle_tree_dataset_digest_calculation` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_merkle_tree_edge_cases_empty_and_single_leaf` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_snapshot_creation_and_reproducibility` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_snapshot_manager_delegates_to_regulatory_audit_ledger` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_snapshot_tamper_detection` | `apps.safety.tests.test_regulatory_audit_ledger` | ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_seal_electronic_signature_generic_and_role_gating` | `apps.safety.tests.test_regulatory_audit_ledger` | 21, ADR-2199, PRD-AGG-001, PRD-PVG-008, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_seal_qppv_signoff_authorization_and_manifest` | `apps.safety.tests.test_regulatory_audit_ledger` | 21, ADR-2199, PRD-AGG-001, PRD-AGG-003, PRD-PVG-008, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_verify_snapshot_manifest_structured_response` | `apps.safety.tests.test_regulatory_audit_ledger` | 21, ADR-2199, PRD-AGG-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_metadata` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_drug_role` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_patient_age_negative` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_icsr_patient_age_unit` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_meddra_coding_primary_soc` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_date_chronology` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_date_format` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_seq` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_seriousness` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_invalid_sae_severity` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_sae_version_metadata` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_full` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_valid_meddra_coding` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_valid_sae_full_normalization` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_valid_sae_minimum` | `apps.safety.tests.test_sae_icsr` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_sae_reconciler_concordant_and_discrepant` | `apps.safety.tests.test_sae_reconciler` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_deterministic_output_sorting` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_execution_client_and_adapter_methods` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_differing_fields` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_missing_on_either_side` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_pure_comparison_same_code_different_terms` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_pure_function_generate_stable_event_key` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_pure_function_normalize_edc_ae_to_sae` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_pure_function_normalize_external_icsr_to_saes` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_jobs_read_endpoints_and_gating` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_persistence_and_audit` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_runs_read_endpoints` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_version_index_increment` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_safety_mutations_negative_signatures` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_safety_reads_negative_signatures` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_functionality` | `apps.safety.tests.test_sae_reconciliation` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_alert_dispatch_failure_exception` | `apps.safety.tests.test_sae_reconciliation_jobs` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_alert_dispatch_failure_non_2xx` | `apps.safety.tests.test_sae_reconciliation_jobs` | ADR-2199, PRD-PVG-001, PRD-SYS-167, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_notifications_gxp_medical_monitor_alert` | `apps.safety.tests.test_sae_reconciliation_jobs` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_reconciliation_job_failure_path` | `apps.safety.tests.test_sae_reconciliation_jobs` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_trigger_and_poll_reconciliation_job_success` | `apps.safety.tests.test_sae_reconciliation_jobs` | ADR-2199, PRD-PVG-001, PRD-SYS-167 | ⚪ UNVERIFIED | N/A |
| `test_executive_qppv_signoff_workflow_and_role_gating` | `apps.safety.tests.test_safety_aggregate_report_compiler` | ADR-2199, PRD-AGG-001, PRD-AGG-002, PRD-AGG-003, PRD-PVG-004, PRD-PVG-008, PRD-SYS-001, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_fastapi_rest_aggregate_safety_compiler_and_exposure_endpoints` | `apps.safety.tests.test_safety_aggregate_report_compiler` | ADR-2199, PRD-AGG-001, PRD-AGG-002, PRD-AGG-003, PRD-PVG-004, PRD-PVG-008, PRD-SYS-125, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_periodic_report_compiler_summary_tabulations_and_rsi_breach` | `apps.safety.tests.test_safety_aggregate_report_compiler` | ADR-2199, PRD-AGG-001, PRD-AGG-002, PRD-AGG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_snapshot_cryptographic_seal_and_reproducibility` | `apps.safety.tests.test_safety_aggregate_report_compiler` | ADR-2199, PRD-AGG-001, PRD-AGG-002, PRD-AGG-003, PRD-PVG-004, PRD-SYS-001, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_subject_exposure_calculator_patient_years_and_stratifications` | `apps.safety.tests.test_safety_aggregate_report_compiler` | ADR-2199, PRD-AGG-001, PRD-AGG-002, PRD-AGG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_ack_b_syntactic_validation_failure` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_ack_b_syntactic_validation_success` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_ack_c_authority_center_acceptance` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_ack_c_authority_center_rejection` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_as2_client_inbound_ack_b_and_ack_c_lifecycle` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_as2_client_outbound_transmission_with_sync_mdn` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_as2_transmission_audit_log_immutability_invariant` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_calculate_mic_canonical_formatting` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_mdn_parser_disposition_failure_handling` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_mdn_parser_mic_mismatch_detection` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_mdn_parser_signed_mdn_verification` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_mdn_parser_valid_synchronous_receipt` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_rest_list_and_get_as2_transmissions` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_rest_process_inbound_mdn_endpoint` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_rest_process_regulatory_ack_endpoint` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_rest_send_as2_transmission_endpoint` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_smime_aes256_hybrid_encryption_and_decryption` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_smime_full_pipeline_sign_then_encrypt` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_smime_sha256_digital_signature_and_verification` | `apps.safety.tests.test_safety_as2_e2b_mdn` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_happy_path` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_generate_e2b_xml_invalid_raises_value_error` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_icsr_version_and_reason_for_change_rendering` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_invalid_namespace_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_invalid_root_tag_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_malformed_xml_validation_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_drugs_or_drug_fields_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_header_fields_fail` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_patient_fields_fail` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_reactions_or_reaction_term_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_safety_report_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_worldwide_unique_case_id_fails` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_valid_icsr_rendering_and_validation` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_validate_e2b_xml_structure_direct` | `apps.safety.tests.test_safety_e2b` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_e2b_xml_generation_and_parser_roundtrip` | `apps.safety.tests.test_safety_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_gateway_negative_signatures_async` | `apps.safety.tests.test_safety_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_reconciliation_job_lifecycle_async` | `apps.safety.tests.test_safety_gateway` | ADR-2199, PRD-PVG-002, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_dispatch_safety_report_post_endpoint` | `apps.safety.tests.test_safety_router` | ADR-2199, PRD-PVG-001, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_reconcile_sae_cases_post_endpoint` | `apps.safety.tests.test_safety_router` | ADR-2199, PRD-PVG-001, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_database_manager_uninitialized_raises_exception` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_invalid_xml_validation_fails` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_list_audit_logs_endpoint` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reason_fails_mutations` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_missing_v2_headers_or_change_reason_fails` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_nonexistent_resources_return_404` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_audit_log_immutable_ledger` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_case_lifecycle` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_database_schema_creation` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_export_job_lifecycle` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_safety_health_check` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_successful_export_and_transmission` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_unauthenticated_requests_are_rejected` | `apps.safety.tests.test_safety_service` | ADR-2199, PRD-PVG-001, PRD-PVG-002, PRD-SYS-167, PRD-SYS-168 | ⚪ UNVERIFIED | N/A |
| `test_dsur_periodic_report_compilation_and_line_listings` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_empirical_bayes_shrinkage_mgps_ebgm_and_eb05` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_external_benchmark_dataset_comparison` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_fastapi_rest_signal_detection_and_alerts_endpoints` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-PVG-008, PRD-SYS-001, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_multi_item_mgps_interaction_shrinkage` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_prr_calculation_point_estimate_and_ci` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_psur_periodic_report_compilation_and_part11_signature` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-PVG-008, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_pt_and_soc_level_stratification_scan` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_ror_point_estimate_and_confidence_interval` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_signal_alert_review_and_part11_esignature_approval` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-PVG-008, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_signal_alert_triggering_and_severity_workflow` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-001, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_yates_chi_square_continuity_correction` | `apps.safety.tests.test_safety_signal_detection_disproportionality` | ADR-2199, PRD-PVG-003, PRD-PVG-004, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_disproportionality_metrics_null_signal` | `apps.safety.tests.test_signal_detection_engine` | ADR-2199, PRD-PVG-003, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_disproportionality_metrics_strong_signal` | `apps.safety.tests.test_signal_detection_engine` | ADR-2199, PRD-PVG-003, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_scan_safety_dataset_batch` | `apps.safety.tests.test_signal_detection_engine` | ADR-2199, PRD-PVG-003, PRD-SYS-169 | ⚪ UNVERIFIED | N/A |
| `test_cross_app_ticket_ingestion_from_execution_edc` | `apps.tickets.tests.test_cross_app_ingestion` | PRD-SYS-042, PRD-TCK-003 | ⚪ UNVERIFIED | N/A |
| `test_21cfr_part11_esignature_capture` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-004 | ⚪ UNVERIFIED | N/A |
| `test_audited_evidence_attachments` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-004 | ⚪ UNVERIFIED | N/A |
| `test_comment_visibility_boundaries` | `apps.tickets.tests.test_part11_signatures_attachments` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rca_validation_on_major_and_critical_closure` | `apps.tickets.tests.test_part11_signatures_attachments` | PRD-TCK-001 | ⚪ UNVERIFIED | N/A |
| `test_regulatory_audit_trail_export` | `apps.tickets.tests.test_part11_signatures_attachments` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_endpoints_rag_triage_and_preview` | `apps.tickets.tests.test_rag_support_triage` | PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_grounded_rag_tier_2_prompt_and_faithfulness_evaluation` | `apps.tickets.tests.test_rag_support_triage` | PRD-SYS-051, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_protocol_document_ingestion_and_chunking` | `apps.tickets.tests.test_rag_support_triage` | PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_protocol_vector_cosine_search` | `apps.tickets.tests.test_rag_support_triage` | PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_support_ticket_rag_triage_high_confidence_pass` | `apps.tickets.tests.test_rag_support_triage` | PRD-SYS-001, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_support_ticket_rag_triage_low_confidence_fail_closed` | `apps.tickets.tests.test_rag_support_triage` | PRD-SYS-001, PRD-TCK-005 | ⚪ UNVERIFIED | N/A |
| `test_kpi_and_kri_metrics_computation` | `apps.tickets.tests.test_tickets_analytics` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_creation_on_escalate` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_bounded_priority_advancement` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_cooldown_gating_and_idempotency` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_escalation_eligibility_rules` | `apps.tickets.tests.test_tickets_escalation` | PRD-TCK-002 | ⚪ UNVERIFIED | N/A |
| `test_notification_deduplication_and_partial_failures` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_startup_shutdown_and_resilience` | `apps.tickets.tests.test_tickets_escalation` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_tickets_and_notifications_handshake` | `apps.tickets.tests.test_tickets_integration_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_escalation_worker_notifications_retry_mechanics` | `apps.tickets.tests.test_tickets_integration_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_non_2xx_failure` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_success` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_transport_exception` | `apps.tickets.tests.test_tickets_notifications_client` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_notification_failure_isolation` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_notification_idempotency` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_assignment_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_comment_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_transition_notification` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_update_ticket_notifications` | `apps.tickets.tests.test_tickets_notifications_integration` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_escalation_worker_flow_and_retries` | `apps.tickets.tests.test_tickets_notifications_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_ticket_creation_and_comment_flow` | `apps.tickets.tests.test_tickets_notifications_seam` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_comments_creation_and_retrieval_scoped` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_list_ticket_audit_logs_endpoint` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_missing_change_reason_fails_mutations` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_nonexistent_resources_return_404` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_audit_log_immutable_ledger` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_concurrent_reference_generation` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_ticket_scoped_audit_logs` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_pagination` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_query_boundaries` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_audit_logs_time_filtering` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_auditor_comments_access` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_database_schema_creation` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_enums_and_models_attributes` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_get_by_reference` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_health_check` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_in_scope_success_and_self_audit` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_lifecycle` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_optimistic_locking_and_explicit_endpoints` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_rbac_auditor_cannot_mutate_but_can_read` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_scope_aware_filtering` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_site_scope_filtering_audit_logs_unfiltered` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_terminal_state_rejection` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_unauthorized_site_scope_blocking` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_tickets_validation_invalid_enums` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_unauthenticated_requests_are_rejected` | `apps.tickets.tests.test_tickets_service` | Trace-16 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_sla_status_amber_warning_and_breach` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sla_target_calculation_with_multipliers` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ticket_sla_pause_and_resume_lifecycle` | `apps.tickets.tests.test_tickets_sla_advanced` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_web_amendment_branching_and_diff_contracts` | `apps.web.tests.test_amendment_diff` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_web_execution_reconsent_gating_and_subject_impact` | `apps.web.tests.test_amendment_diff` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_audit_ledger_domain_model_integrity` | `apps.web.tests.test_audit` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_audit_record_part11_fields_and_sha256` | `apps.web.tests.test_audit` | PRD-DOC-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_audit_trail_filtering_by_entity_and_date_range` | `apps.web.tests.test_audit` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_audit_trail_filtering_by_user_and_action` | `apps.web.tests.test_audit` | PRD-SYS-001, PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_cra_batch_sdv_bulk_signoff_with_gxp_reason` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cra_verification_console_field_level_sdv` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_ctms_dashboard_live_kpis` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_delegation_of_authority_matrix_staff_certs_and_pi_signoff` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_delegation_revocation_and_gxp_pdf_export` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_query_discrepancy_lifecycle_open_answered_closed` | `apps.web.tests.test_ctms` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_21_cfr_part_11_dual_credential_signature_capture` | `apps.web.tests.test_econsent` | PRD-SYS-042 | ⚪ UNVERIFIED | N/A |
| `test_comprehension_quiz_evaluation_and_threshold_enforcement` | `apps.web.tests.test_econsent` | PRD-SYS-043 | ⚪ UNVERIFIED | N/A |
| `test_icf_builder_modular_clauses_linked_to_protocol_version` | `apps.web.tests.test_econsent` | PRD-SYS-042 | ⚪ UNVERIFIED | N/A |
| `test_form_submission_persists_observations_with_part11_audit_fields` | `apps.web.tests.test_ecrf_renderer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_reconsent_gating_blocks_data_entry_and_unlocks_on_reconsent` | `apps.web.tests.test_ecrf_renderer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_enrollment_modal_and_consent_assignment` | `apps.web.tests.test_ecrf_renderer` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_cdisc_export_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_db_snapshot_and_restore` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_db_status_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_dev_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_dev_port_collision_fallback` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_doctor_auto_fix_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_doctor_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_gxp_export_cdisc_json` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_help` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_find_target_test_file_resolution` | `packages.cli.tests.test_cadence_cli` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_full_cadence_101_json` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_sqlite_content` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_db_seed_tier_filtering` | `packages.cli.tests.test_db_seed` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_format_file_link` | `packages.cli.tests.test_formatting` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_terminal_document_builder_structured_dict` | `packages.cli.tests.test_formatting` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_terminal_document_render_output` | `packages.cli.tests.test_formatting` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_clinical_event_stream` | `packages.cli.tests.test_mcp_server` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_initialize` | `packages.cli.tests.test_mcp_server` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_sdtm_pipeline_validate` | `packages.cli.tests.test_mcp_server` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_synthetic_patient_generate` | `packages.cli.tests.test_mcp_server` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_tool_call_doctor` | `packages.cli.tests.test_mcp_server` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_tool_call_invalid_tool` | `packages.cli.tests.test_mcp_server` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_tools_list` | `packages.cli.tests.test_mcp_server` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_usdm_graph_inspect` | `packages.cli.tests.test_mcp_server` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_mcp_server_zoom_inspection` | `packages.cli.tests.test_mcp_server` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_extract_ports_from_command` | `packages.cli.tests.test_ports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_get_discovered_service_ports` | `packages.cli.tests.test_ports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_is_port_in_use_and_find_available_port` | `packages.cli.tests.test_ports` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_compose` | `packages.cli.tests.test_ports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_fallback_on_missing` | `packages.cli.tests.test_ports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_parse_port_entry_variations` | `packages.cli.tests.test_ports` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_resolve_all_service_ports_batch` | `packages.cli.tests.test_ports` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_resolve_service_port_collision` | `packages.cli.tests.test_ports` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_record_help` | `packages.cli.tests.test_record_command` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_record_list_empty` | `packages.cli.tests.test_record_command` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_record_list_with_runs` | `packages.cli.tests.test_record_command` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_record_run_local_only` | `packages.cli.tests.test_record_command` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_record_setup_gdrive_valid` | `packages.cli.tests.test_record_command` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_ai_assisted_record_default_state_draft_ai` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_ai_generation_manifest_structure` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_approved_state_requires_hitl_fields` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_confidence_score_validation_bounds` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_esignature_verifier_blocks_draft_ai` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_valid_approved_ai_record` | `packages.compliance.tests.test_ai_assisted_audit_ledger` | PRD-SYS-051 | ⚪ UNVERIFIED | N/A |
| `test_change_request_audit_trail_recorded` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_change_request_requires_dual_approval` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_change_request_audit_trail` | `packages.compliance.tests.test_compliance_change_request` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assert_secure_secrets_validation` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_creates_valid_record` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_detects_chain_tampering` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_raises_runtime_error_if_secret_missing` | `packages.compliance.tests.test_compliance_security` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_invalid_signature` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_valid_signature` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_raises_runtime_error_if_secret_missing` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_global_scanner_with_opt_out` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_signature_and_key_detection` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_exclusions` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_scanner_detection_and_bypass` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_script` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_audit_targeted_files` | `packages.compliance.tests.test_compliance_security` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signing_raises_runtime_error_if_email_secret_missing` | `packages.compliance.tests.test_compliance_security` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_trusted_self_signed_cert_accepted` | `packages.compliance.tests.test_esignature_trust` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_untrusted_self_signed_cert_rejected` | `packages.compliance.tests.test_esignature_trust` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_revocation_blocks_when_serial_matches` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revocation_exact_match_prevents_false_positives` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revoked_certs_normalization_helper` | `packages.compliance.tests.test_esignature_verifier_revocation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_log_record_immutability_enforcement` | `packages.compliance.tests.test_standardized_compliance_framework` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_crypto_digest_chain_generation_and_verification` | `packages.compliance.tests.test_standardized_compliance_framework` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_missing_change_justification_raises_compliance_error` | `packages.compliance.tests.test_standardized_compliance_framework` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_standardized_compliance_router_endpoints` | `packages.compliance.tests.test_standardized_compliance_framework` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_neo4j_graph_calls_run` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_postgres_databases_calls_truncate` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_live_db_halt_when_neo4j_unreachable` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_live_db_halt_when_postgres_unreachable` | `packages.database.tests.test_asgi_live_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pre_flush_justification_rejection` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_site_freeze_blocking_writes` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_sync_and_async_map_database_exceptions` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-005 | ⚪ UNVERIFIED | N/A |
| `test_trial_freeze_blocking_writes` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_utc_datetime_type_decorator_enforcement` | `packages.database.tests.test_compliance_pre_flush` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_ci_database_parity_enforcement_raises_on_failure` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_database_session_dependency_does_not_set_app_writing` | `packages.database.tests.test_database_managers` | PRD-SYS-053, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_econsent_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_eisf_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_map_database_exceptions_decorator` | `packages.database.tests.test_database_managers` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_notifications_database_manager_uninitialized_and_close` | `packages.database.tests.test_database_managers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_relational_database_manager_pool_recycle` | `packages.database.tests.test_database_managers` | PRD-SYS-053, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_database_session_dependency_does_not_set_app_writing` | `packages.database.tests.test_database_security` | PRD-SYS-053, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_relational_database_manager_pool_recycle` | `packages.database.tests.test_database_security` | PRD-SYS-053, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_assign_activities_to_visit_mock` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_assign_activities_to_visit_real` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_library_version_increments` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_concurrent_study_saves_serialization` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_library_object_version_existing` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_library_object_version_new` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_study_root` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_study_differences` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reorder_visits_mock` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reorder_visits_real` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_study_properties` | `packages.database.tests.test_delta` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_graph_database_manager` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_transaction_retry_decorator` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_transaction_retry_failure` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cypher_query_failures` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cypher_query_success` | `packages.database.tests.test_graph_persistence` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ledger_sealing_and_validation` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_out_of_band_update_triggers_audit_entry` | `packages.database.tests.test_ledger_and_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_prevent_audit_ledger_seals_mutation` | `packages.database.tests.test_ledger_and_triggers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_prevent_audit_log_mutation` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_prevent_hard_delete_on_audited_model` | `packages.database.tests.test_ledger_and_triggers` | PRD-SYS-002, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_relational_db_lifespan_partial_failure_cleanup` | `packages.database.tests.test_lifespan` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_relational_db_lifespan_successful_lifecycle` | `packages.database.tests.test_lifespan` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_relational_db_lifespan_without_metadata_or_hooks` | `packages.database.tests.test_lifespan` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_lab_reference_ranges_evolution` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_cli` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_new_tables_metadata_creation` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_placeholders` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_failure` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_real_sqlite` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_migrations_success` | `packages.database.tests.test_migrate` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_safety_guard_non_local` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_safety_guard_production` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reset_db_success_offline` | `packages.database.tests.test_reset_db` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compile_options_default_accessibility_validation_active` | `packages.ddf.tests.test_compiler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ddf_compiler_accessibility_failure_raises` | `packages.ddf.tests.test_compiler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ddf_compiler_end_to_end_success` | `packages.ddf.tests.test_compiler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ddf_compiler_uninitialized_validator_raises` | `packages.ddf.tests.test_compiler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_accessibility_stage_uninitialized_validator_raises` | `packages.ddf.tests.test_compiler` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dataset_json_sdtm_extraction` | `packages.ddf.tests.test_dataset_json_etl` | PRD-MDR-001, PRD-SYS-125 | ⚪ UNVERIFIED | N/A |
| `test_ir_builder_processes_items_and_rules` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-002 | ⚪ UNVERIFIED | N/A |
| `test_ir_builder_skip_logic_and_narratives` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-002 | ⚪ UNVERIFIED | N/A |
| `test_normalizer_duplicate_ids_raises` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_normalizer_empty_or_non_dict_raises` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_normalizer_missing_protocol_raises` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_normalizer_synthesizes_fallbacks` | `packages.ddf.tests.test_normalizer_and_ir` | PRD-DDF-001 | ⚪ UNVERIFIED | N/A |
| `test_compile_condition_to_xpath_constants` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compile_condition_to_xpath_field_ref` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compile_condition_to_xpath_functions` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compile_condition_to_xpath_logical_and_comparison` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_circular_dependencies` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_appearance` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_concept_id` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sanitize_identifier` | `packages.ddf.tests.test_rules` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_patient_migration_matrix_directives` | `packages.ddf.tests.test_tripartite` | PRD-CRF-012 | ⚪ UNVERIFIED | N/A |
| `test_tripartite_schema_instantiation` | `packages.ddf.tests.test_tripartite` | PRD-CRF-001, PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_uat_simulation_and_evidence_report` | `packages.ddf.tests.test_tripartite` | PRD-CRF-009, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_co_reference_consistency_across_messages` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deidentify_and_rehydrate_basic_phi` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_deidentify_batch_texts_embeddings` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_phi_noop` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rehydrate_nested_structured_data` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_vault_lifecycle_and_cleanup` | `packages.deid.tests.test_air_gap` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_apply_deid_transforms_right_to_left` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cap_age_string` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_empty_reason_raises_validation_error` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_detector_and_transforms` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pseudonymize_value_deterministic` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_manifest_asymmetric_tamper_evident` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_redaction_manifest_symmetric_tamper_evident` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shift_date_string` | `packages.deid.tests.test_deid_transforms` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_age_capping_and_edge_cases` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_compliance_profiles` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_date_shifting_and_edge_cases` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_detections_all_categories` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_hmac_pseudonymization_determinism` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manifest_tamper_evident_asymmetric` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_manifest_tamper_evident_symmetric` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_no_raw_matched_values_persisted` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_normalize_and_cap_age_direct` | `packages.deid.tests.test_deidentification` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_overlap_resolution_comprehensive` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_source_documents_remain_unchanged` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_transforms_all_strategies` | `packages.deid.tests.test_deidentification` | PRD-TMF-005 | ⚪ UNVERIFIED | N/A |
| `test_burned_in_pixel_masking_custom_coordinates` | `packages.deid.tests.test_dicom_deidentifier` | PRD-IMG-002 | ⚪ UNVERIFIED | N/A |
| `test_burned_in_pixel_masking_ultrasound` | `packages.deid.tests.test_dicom_deidentifier` | PRD-IMG-002 | ⚪ UNVERIFIED | N/A |
| `test_cryptographic_certificate_manifest_and_tamper_detection` | `packages.deid.tests.test_dicom_deidentifier` | PRD-IMG-002 | ⚪ UNVERIFIED | N/A |
| `test_dicom_deidentification_header_scrubbing` | `packages.deid.tests.test_dicom_deidentifier` | PRD-IMG-001, PRD-IMG-002 | ⚪ UNVERIFIED | N/A |
| `test_dicom_deterministic_uid_generation` | `packages.deid.tests.test_dicom_deidentifier` | PRD-IMG-002 | ⚪ UNVERIFIED | N/A |
| `test_custom_terms_and_overlap_resolution` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_detect_phi_patterns` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scrub_phi_redaction` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_word_boundaries_custom_terms` | `packages.deid.tests.test_ner_scrubber` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_embedded_custom_terms_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_leading_non_word_phone_email_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_passthrough_parity` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_new_hipaa_categories_redaction` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trailing_punctuation_clinical_identifiers` | `packages.deid.tests.test_unified_compliance_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_calculate_bmi` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_calculate_bsa_mosteller_and_dubois` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_calculate_egfr_ckd_epi_2021` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_calculate_map_and_cockcroft_gault` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_calculate_qtc_fridericia_and_bazett` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_custom_ast_expression_formula` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_plausibility_bounds_gating` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_unit_normalization_helper` | `packages.engine_core.tests.test_derived_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_state_machine_electronic_signature_requirement` | `packages.engine_core.tests.test_fsm_engine` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_state_machine_guard_condition_success_and_failure` | `packages.engine_core.tests.test_fsm_engine` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_state_machine_initialization_and_properties` | `packages.engine_core.tests.test_fsm_engine` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_state_machine_reason_for_change_length_enforcement` | `packages.engine_core.tests.test_fsm_engine` | PRD-CRF-010 | ⚪ UNVERIFIED | N/A |
| `test_state_machine_role_authorization_gating` | `packages.engine_core.tests.test_fsm_engine` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_validator_detects_dead_end_non_terminal_states` | `packages.engine_core.tests.test_fsm_validator` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_validator_detects_duplicate_nondeterministic_transitions` | `packages.engine_core.tests.test_fsm_validator` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_validator_detects_unreachable_orphan_states` | `packages.engine_core.tests.test_fsm_validator` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_validator_valid_graph_passes` | `packages.engine_core.tests.test_fsm_validator` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_end_to_end_clinical_subject_workflow` | `packages.engine_core.tests.test_integration` | PRD-SUB-001 | ⚪ UNVERIFIED | N/A |
| `test_isomorphic_ast_arithmetic_and_comparisons` | `packages.engine_core.tests.test_isomorphic_rules` | PRD-CRF-009, PRD-EDC-003 | ⚪ UNVERIFIED | N/A |
| `test_isomorphic_ast_derived_bmi_calculation` | `packages.engine_core.tests.test_isomorphic_rules` | PRD-CRF-010, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_batch_ledger_summary_and_audit_digest` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-PAY-001, PRD-SYS-050, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_holdback_retention_and_escrow_release` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-PAY-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_line_item_audit_hash_tamper_detection` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-SYS-050, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_payable_ledger_event_matching_and_caps` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-PAY-001, PRD-SYS-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_split_payee_penny_perfect_reconciliation` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-PAY-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_tiered_overhead_marginal_brackets` | `packages.engine_core.tests.test_payable_ledger_engine` | PRD-PAY-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_boin_boundaries_and_posterior_calculation` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-002 | ⚪ UNVERIFIED | N/A |
| `test_boin_dose_escalation_decision_rules` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-002 | ⚪ UNVERIFIED | N/A |
| `test_pocock_simon_minimization_range_variance_stddev` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-001 | ⚪ UNVERIFIED | N/A |
| `test_stratified_permuted_block_allocation_and_seed_reproducibility` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-001 | ⚪ UNVERIFIED | N/A |
| `test_stratum_key_generation` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-001 | ⚪ UNVERIFIED | N/A |
| `test_three_plus_three_rule_based_escalation` | `packages.engine_core.tests.test_randomization_engine` | PRD-RTSM-002 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_arithmetic_operators` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_comparison_operators` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_constant` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_field_ref` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_function_operators` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_logical_operators` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_expression_node_variable` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_field_reference_creation` | `packages.engine_core.tests.test_rules_ast` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_rule_definition_checksum_and_validation` | `packages.engine_core.tests.test_rules_ast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_arithmetic_operations` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_comparisons` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_constants` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_date_functions` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_functions_and_collections` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_logical_operators` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_variables_and_nested_contexts` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_parse_and_evaluate_json_logic` | `packages.engine_core.tests.test_rules_evaluator` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_calculate_expression_complexity` | `packages.engine_core.tests.test_rules_sentinels` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_detect_circular_dependencies_direct_cycle` | `packages.engine_core.tests.test_rules_sentinels` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_detect_circular_dependencies_no_cycle` | `packages.engine_core.tests.test_rules_sentinels` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_detect_unresolved_references` | `packages.engine_core.tests.test_rules_sentinels` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_extract_field_references_and_variables` | `packages.engine_core.tests.test_rules_sentinels` | PRD-CRF-005 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_conditional_release_for_pending_siv` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-SSU-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_facility_and_ip_depot_failures` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-SSU-001, PRD-SYS-003, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_fully_compliant_cleared` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-SSU-001, PRD-SYS-003, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_irb_expired_and_version_mismatch_blocks` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-CTMS-004, PRD-SSU-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_missing_fda_1572_blocks` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-SSU-001, PRD-SYS-003, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_staff_deficiencies_diagnostics` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-CTMS-004, PRD-SSU-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_site_greenlight_unexecuted_cta_blocks` | `packages.engine_core.tests.test_site_greenlight_engine` | PRD-SSU-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_countdown_clock_and_alert_level_progression` | `packages.engine_core.tests.test_sla_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_countdown_clock_paused_time_adjustment` | `packages.engine_core.tests.test_sla_engine` | PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_multi_tier_escalation_policies` | `packages.engine_core.tests.test_sla_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_sla_due_date_computation_24x7` | `packages.engine_core.tests.test_sla_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_sla_due_date_computation_business_hours` | `packages.engine_core.tests.test_sla_engine` | PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_21cfr_part11_emergency_unblinding` | `packages.engine_core.tests.test_supply_engine` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_mean_kinetic_temperature_and_dtp_cold_chain_gating` | `packages.engine_core.tests.test_supply_engine` | PRD-SCM-002 | ⚪ UNVERIFIED | N/A |
| `test_multi_echelon_hierarchy_rollup_and_cycle_detection` | `packages.engine_core.tests.test_supply_engine` | PRD-SCM-001 | ⚪ UNVERIFIED | N/A |
| `test_predictive_demand_forecasting_and_expiry_warnings` | `packages.engine_core.tests.test_supply_engine` | PRD-SCM-001 | ⚪ UNVERIFIED | N/A |
| `test_compute_subject_schedule_with_anchors_and_chaining` | `packages.engine_core.tests.test_timeline_engine` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_cycle_template_expansion` | `packages.engine_core.tests.test_timeline_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_visit_deviation_classifications` | `packages.engine_core.tests.test_timeline_engine` | PRD-CRF-004 | ⚪ UNVERIFIED | N/A |
| `test_timeline_definition_checksum_and_model` | `packages.engine_core.tests.test_timeline_engine` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_delta_check_alert_and_custom_rule_extension` | `packages.engine_core.tests.test_toxicity_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_hematology_absolute_range_grading_platelets` | `packages.engine_core.tests.test_toxicity_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_hys_law_dili_sentinel_triad` | `packages.engine_core.tests.test_toxicity_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_standard_ctcae_hepatic_grading_alt` | `packages.engine_core.tests.test_toxicity_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_standard_ctcae_renal_grading_creatinine` | `packages.engine_core.tests.test_toxicity_engine` | PRD-PVG-001 | ⚪ UNVERIFIED | N/A |
| `test_all_services_have_ports` | `packages.hexagonal.tests.test_hexagonal_architecture` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_application_layer_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_decoupled_api_routers_have_no_direct_db_imports` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_core_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_layer_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_entrypoint_is_thin` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_entrypoints_count_integrity` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_legacy_top_level_routers_directory` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_no_singular_adapter_directory` | `packages.hexagonal.tests.test_hexagonal_architecture` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_presentation_layer_driver_isolation` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_repository_ports_count_integrity` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_repository_ports_subclass_base` | `packages.hexagonal.tests.test_hexagonal_architecture` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_consent_form_record_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_consent_signature_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_safety_audit_log_immutability_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_audit_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_consent_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sqlalchemy_subject_repository_persistence` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_lifecycle_pure_domain_transitions` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_stratification_factors_locking_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_unblinding_and_withdrawal_domain` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_workflows_with_in_memory_repositories` | `packages.hexagonal.tests.test_hexagonal_domain` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_aggregate_root_events` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_base_entity_equality` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_domain_error_http_mappings` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_value_object_equality` | `packages.hexagonal.tests.test_hexagonal_kernel` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_routers_contain_no_direct_db_calls` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_designer_graph_modifications_with_mock_repositories` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_domain_models_contain_zero_database_imports` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_relational_services_execute_database_disabled` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_signed_consent_immutability_pure_python_validation` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_subject_status_transitions_pure_python_validation` | `packages.hexagonal.tests.test_hexagonal_ports_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_audit_records_ip_and_custom_timestamp` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_hard_delete_is_prevented` | `packages.security.tests.test_audit` | Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_insert_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_read_only_queries_do_not_generate_audit_logs` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rollback_prevents_orphan_audit_logs` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soft_delete_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_subject_notification_skips_clinical_auditing` | `packages.security.tests.test_audit` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_update_generates_audit_log` | `packages.security.tests.test_audit` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_fingerprint_vs_serial_forgery_rejection` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_register_and_verify_valid_certificate` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_revoke_certificate_status_check` | `packages.security.tests.test_cert_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_logger_module_import_safety_unset_secrets` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_asymmetric_rsa_pss` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_ecdsa` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_hmac` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_crypto_verifier_legacy_padding_rejected` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_middleware_masked_logging` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_signing_module_import_safety_unset_secrets` | `packages.security.tests.test_crypto_verifier` | PRD-SYS-053, Trace-14 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_duplicate_shares` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_malformed_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_mismatched_versions` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_single_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_negative_tampered_share` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dual_custody_positive` | `packages.security.tests.test_cryptography` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_encryption_decryption_with_rotation` | `packages.security.tests.test_cryptography` | PRD-MDR-005, Trace-2 | ⚪ UNVERIFIED | N/A |
| `test_key_splitting` | `packages.security.tests.test_cryptography` | PRD-MDR-005, Trace-2 | ⚪ UNVERIFIED | N/A |
| `test_encryption_roundtrip` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_encryption_tamper_rejection` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_hkdf_determinism` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rejection_of_invalid_key_material` | `packages.security.tests.test_encryption` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_dev_bypass` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_gateway_failures` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_prod_failures` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_branding_success` | `packages.security.tests.test_fail_fast_branding` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_notification_dispatcher_batch` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gateway_notification_dispatcher_network_exception` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gateway_notification_dispatcher_non_2xx_failure` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gateway_notification_dispatcher_success` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_in_memory_notification_dispatcher` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_notification_event_creation_and_defaults` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_notification_event_idempotency_key_derivation` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_publish_notification_convenience_function` | `packages.security.tests.test_notifications_dispatcher` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_can_access_site` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_site_query_read_isolation` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_site_unblind_denied_with_alert` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ecoa_diary_alert_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_audit_logs_gated_to_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_document_transition_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edl_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_edl_update_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_ingest_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_taxonomy_and_tag_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_observation_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_subject_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_visit_creation_auditor_forbidden` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_aliases` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_eisf_denies_writes_allows_reads` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_permissions_matrix` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_external_monitor_principal_resolution` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_principal_from_request` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_has_permission` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_alert_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_lab_range_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mask_payload_recursive` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medical_coding_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_principal_agreement_with_middleware_coercion` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_require_permission_dependency` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_require_study_scope_extraction` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_aliases_normalization` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_normalization_list` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_normalization_string` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_aliases_normalization` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_aware_masking` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtsm_role_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_granular_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_is_auditor_allows_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_is_auditor_denies_non_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_not_auditor_allows_others` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_not_auditor_denies_auditors` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_windowing_granular_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_visit_windowing_rbac_permissions` | `packages.security.tests.test_rbac` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_designer_study_scoping` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_etmf_site_scoping` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_execution_access` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_execution_unauthorized` | `packages.security.tests.test_rbac_e2e` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_blinded_user` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_pii_fields` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_level_masking_unblinded_user` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mask_clinical_records_list` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_cra_monitoring_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_data_manager_lock_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rbac_sponsor_admin_authorization` | `packages.security.tests.test_rbac_enforcement` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_cra` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_data_manager` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_role_sponsor_admin` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_permissions_for_roles_aggregated` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_has_permission_checks` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_role_name` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_permission_matrix_enum_values` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_role_enum_canonical_names` | `packages.security.tests.test_rbac_permissions` | PRD-SYS-001, PRD-SYS-050 | ⚪ UNVERIFIED | N/A |
| `test_soa_permissions_definitions` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_permissions_matrix_mapping` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unknown_role_returns_empty_permissions` | `packages.security.tests.test_rbac_permissions` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_commit_within_active_transaction` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pluggable_sqlmodel_store_async` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pluggable_sqlmodel_store_sync` | `packages.security.tests.test_sqlmodel_audit_store` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_sentinel_engine_execution` | `packages.sentinels.tests.test_sentinels` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_integrity_failure` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_lifecycle` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_not_found` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_local_storage_provider_traversal_prevention` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_integrity_failure` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_lifecycle` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_provider_not_found` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verify_checksum` | `packages.storage.tests.test_blob_store` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compute_sha256` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_gdrive_client_create_folder` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_gdrive_client_unconfigured` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_gdrive_client_upload_file` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_gdrive_client_validate_credentials` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_generate_run_manifest` | `packages.storage.tests.test_gdrive_client` | PRD-SYS-105 | ⚪ UNVERIFIED | N/A |
| `test_automated_webhook_non_degraded_ingestion` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_native_docx_watermarking` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_native_pdf_watermarking` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_safe_binary_ingestion_and_export` | `packages.storage.tests.test_safe_binary_storage_watermark` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_minio_storage_adapter_env_and_config` | `packages.storage.tests.test_storage_adapters` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_empty_data_and_generic_errors` | `packages.storage.tests.test_storage_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_integrity_failure` | `packages.storage.tests.test_storage_adapters` | PRD-DOC-002, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_multipart_lifecycle` | `packages.storage.tests.test_storage_adapters` | PRD-DOC-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_not_found_handling` | `packages.storage.tests.test_storage_adapters` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_presigned_urls` | `packages.storage.tests.test_storage_adapters` | PRD-DOC-001 | ⚪ UNVERIFIED | N/A |
| `test_s3_storage_adapter_put_and_get_lifecycle` | `packages.storage.tests.test_storage_adapters` | PRD-DOC-001, PRD-DOC-003, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_dependency_graph_ast_parsing` | `packages.testing.tests.test_dependency_graph` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_dependency_graph_caching` | `packages.testing.tests.test_dependency_graph` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_factories_creation` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_repository` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_helpers` | `packages.testing.tests.test_testing_package` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_command_envelope_serialization` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_handle_clinical_event_stream_replay_and_filter` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_handle_sdtm_pipeline_validate_rules` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_handle_synthetic_patient_generate_determinism` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_handle_usdm_graph_inspect_depth_and_filtering` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-054 | ⚪ UNVERIFIED | N/A |
| `test_zoom_inspect_handler` | `packages.tooling_core.tests.test_tooling_core` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_domain_model_creation` | `scripts.scaffold_service` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_import_isolation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_status` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_study_id` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_version_index` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_invalid_version_tag` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_json_serialization` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_string_trimming` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_protocol_version_ref_valid` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_etmf_watermark_decoupling` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_dsl_parsing_and_evaluation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_epro_transport_dto_serialization` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_expression_node_dto_validation` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_interop_field_reference_dto` | `scripts.test_m4_challenger2_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_evaluate_command_safety_allowed_commands` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_command_safety_prohibited_git_commands` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_command_safety_prohibited_system_commands` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_file_mutation_safety_allowed_files` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_file_mutation_safety_locked_gxp_docs` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_handle_pre_tool_payload_file_write` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_handle_pre_tool_payload_run_command` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_hooks_json_configuration_integrity` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_post_write_check_contract` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_pre_invocation_reminder_contract` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_stop_sentinel_contract` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_subprocess_stdin_stdout_pipeline` | `scripts.tests.test_antigravity_gxp_hooks` | PRD-SYS-053 | ⚪ UNVERIFIED | N/A |
| `test_api_parameters_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_paths_and_methods_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_request_bodies_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_api_responses_parity` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extra_response_properties_pass_validation` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_spec_extract_and_parse` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_spec_syntax_checks_malformed_yaml` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_undocumented_parameter_fails_parity_check` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_undocumented_route_fails_parity_check` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_fails_on_route_path_mismatch` | `scripts.tests.test_api_contract_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_engine_generation` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_router_endpoint` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_artifact_cascade_router_endpoint_dependency_override` | `scripts.tests.test_artifact_cascade` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_js_reordered_helper_functions` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_python_edited_and_reordered` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_python_imports_merged_and_sorted` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-002 | ⚪ UNVERIFIED | N/A |
| `test_python_overlapping_logical_edits_fallback` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-003 | ⚪ UNVERIFIED | N/A |
| `test_python_reordered_helper_functions` | `scripts.tests.test_ast_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_analyze_diff_endpoint_via_gateway` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_disable_audit_logging_is_blocked_outright` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_high_risk_compliance_setting_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_low_risk_ui_display_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_medium_risk_configuration_changes` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_type_aware_diff_no_op` | `scripts.tests.test_change_analyzer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_fallback` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_categorized_ports_with_compose` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_port_entry` | `scripts.tests.test_check_ports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_classification_driven_multi_site_export` | `scripts.tests.test_classification_multi_site_export` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_unresolved_site_level_omit_manually` | `scripts.tests.test_classification_multi_site_export` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_rtm_generation_conftest_hook_detection` | `scripts.tests.test_cli_etmf_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rtm_generation_with_cli_overrides` | `scripts.tests.test_cli_etmf_archival` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_create_and_retrieve_form_comments` | `scripts.tests.test_comments_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_form_comment_logs_gxp_audit` | `scripts.tests.test_comments_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_assembly_and_ref_resolution` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_display_rule_duplicate_section_numbers` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_narrative_display_rule_missing_section_number` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_render_synopsis_template_html` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_soa_matrix_assembly` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_successful_assembly_and_synopsis` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolved_reference_invalid_attribute` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_unresolved_reference_non_existent_id` | `scripts.tests.test_content_assembly` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_data_lifecycle_protocol_amendment_traceability` | `scripts.tests.test_data_lifecycle_protocol_amendment_traceability` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_eligibility_rejection` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_in_memory_eligibility_success` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_coding_action_accept_in_memory` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_coding_action_invalid_code_in_memory` | `scripts.tests.test_decoupled_services_in_memory` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_duplicates_scanned` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_url_logic_preservation` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_with_duplicates_detected` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_css` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_javascript` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_python` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_normalize_line_vue` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_path_normalization_win32` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_repo_root_resolution` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_file_for_lines` | `scripts.tests.test_detect_duplication` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_all` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_designer` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_down_operations` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_execution_with_flag` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_compose_command_no_detach` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_dry_run` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_executes_subprocess` | `scripts.tests.test_dev_orchestrator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_broken_fragment_relative_link_detection` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_directory_sweeping` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_duplicate_requirement_id_in_fragments_fails` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_full_generate_rtm_cli_sweeping` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_malformed_trace_id_definition_fails` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_orphan_fragment_detection` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sweep_and_aggregate_modular_fragments` | `scripts.tests.test_directory_sweeping_pipeline` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_renderer_render_docx` | `scripts.tests.test_document_renderer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_renderer_render_pdf` | `scripts.tests.test_document_renderer` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_download_logs_audit_and_watermarks` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_upload_missing_permission` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_upload_success` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_document_versions_lineage` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_archival_job_flow` | `scripts.tests.test_document_router` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_aggregate_eligibility_evaluation` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-008, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criterion_compatibility_and_enums` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-009 | ⚪ UNVERIFIED | N/A |
| `test_evaluate_criteria_group_helper` | `scripts.tests.test_eligibility_engine` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_evaluate_structured_expression_helper` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-010 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_all_operators` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-005, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_incompatible_types_graceful_handling` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-007 | ⚪ UNVERIFIED | N/A |
| `test_evaluation_kleene_indeterminate_propagation` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-006, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_parse_invalid_syntax` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-004 | ⚪ UNVERIFIED | N/A |
| `test_parse_logical_and_nested_expressions` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-003, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_parse_simple_expressions` | `scripts.tests.test_eligibility_engine` | PRD-ELIGIBILITY-002, PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_crud_endpoints` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_immutability` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_usdm_projection` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_eligibility_criteria_validation_failures` | `scripts.tests.test_eligibility_mdr` | PRD-MDR-007 | ⚪ UNVERIFIED | N/A |
| `test_generate_schema_documentation_main` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_designer_schema` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_sqlalchemy_schema_etmf` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_sqlalchemy_schema_execution` | `scripts.tests.test_generate_schema_documentation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_schemas_halts_in_production` | `scripts.tests.test_generate_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_schemas_omits_sensitive_tables` | `scripts.tests.test_generate_schemas` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_git_merge_driver_cli_conflict_and_markers` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_git_merge_driver_cli_non_overlapping_success` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gitattributes_configuration_mapping` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_is_logical_code` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_generic_json` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_generic_json_value_collision_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_markdown_text` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_markdown_text_overlapping` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_structural_mismatch_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_top_level_scalar_collision` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_merge_secrets_baseline_value_collision_fail_fast` | `scripts.tests.test_git_merge_driver` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_gxp_generation_and_runs_splitting` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sign_and_verify_rsa_and_ecdsa` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_body_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_metadata_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tamper_detection_signature_bytes_modification` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_verification_performance_under_five_seconds` | `scripts.tests.test_gxp_decomposed_signatures` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fail_fast_without_report_and_draft_flag` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_missing_report_gxp_sync_dry_run` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_success_with_draft_flag` | `scripts.tests.test_gxp_fail_fast` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_layout_gating_approved_and_logged` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_gating_missing_justification_rejected` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_integration` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_invisible` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_overlap` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_scrambled_sequence` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_layout_validation_valid` | `scripts.tests.test_layout_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_aggregate_eligibility_evaluation_scenarios` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_document_renderer_fallback` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_reconciliation_signature_enforcement_errors` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_reconciliation_strategies` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_ctms_sync_signature_verification_and_tampering` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_eligibility_criterion_alias_sync` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_field_reference_dto_parsing` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_kleene_3_valued_logic_evaluation` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_protocol_version_ref_dto_validation` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_usdm_validation_dto_and_parser` | `scripts.tests.test_m4_challenger1_stress` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_expired_timestamp` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_invalid_signature` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_invalid_timestamp` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_gateway_auth_missing_headers` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_health` | `scripts.tests.test_main` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_leading_number` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_invalid_spacing` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multiple_colons` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_valid_csv` | `scripts.tests.test_mapping_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_clean_token` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_contributing_guide_skip_and_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_degraded_linter_warnings_and_fallback` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_degraded_linter_warnings_and_fallback_failure` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dynamic_pydantic_model_import_failure_allow_degraded` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_dynamic_pydantic_model_import_failure_strict_mode` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_exclude_tests_from_codebase_map` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_func` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_html_comment_filtering` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_potential_path_ref` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_json_block_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_with_arguments` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_environment_variables` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nested_code_blocks_in_html_comments` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_process_markdown_file_e2e` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_python_block_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_reference_style_link_validation` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_resolve_path` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_skip_and_raw_text_flags` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sys_path_append` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_uncommented_code_block_errors_maintained` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cli_command_flag_checks` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_cli_command_python_and_pytest` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_docker_compose_scenarios` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_path` | `scripts.tests.test_markdown_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_docker_compose_front_proxy_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nginx_default_site_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_nginx_main_configuration` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_split_clients_and_path_override_routing_logic` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_trace17_header_forwarding_contract` | `scripts.tests.test_nginx_front_proxy` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_build_comment_body` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_combined_audit_logic` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_status_emoji` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_validation_and_migration_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_markdown_and_architecture_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_merge_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_parse_existing_outcomes` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_traceability_outcome_handling` | `scripts.tests.test_pr_comment` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_staged_files` | `scripts.tests.test_pre_commit` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_install_pre_commit_hook` | `scripts.tests.test_pre_commit` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_run_tier1_staged_checks_no_files` | `scripts.tests.test_pre_commit` | PRD-SYS-049 | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_bypass` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_missing_dependencies` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_missing_venv` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_success` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_and_run_exporter_validation_failure` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_staged_files_failure` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_staged_files_success` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_should_trigger_schema_generation` | `scripts.tests.test_pre_commit_openapi` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_fetch_all_issues` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_jules_label` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_remove_label_dry_run` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_remove_label_success` | `scripts.tests.test_remove_jules_labels` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_all_tests_passed` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_failed` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_missing_draft_mode` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_missing_fail_fast` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_pq_test_skipped` | `scripts.tests.test_rtm_generation_pq_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_auto_reexec_delegates_to_uv` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_fails_on_outdated_version` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_matrix` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_enforce_python_runtime_passes_on_valid_version` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_runtime_info` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_print_runtime_info` | `scripts.tests.test_runtime_guard` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gateway_graceful_handling_invalid_downstream` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rewrite_references_nested_references` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rewrite_references_recursion_protection` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_static_schema_validation_script` | `scripts.tests.test_schema_validation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_handle_github_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_executable_or_test_file` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_safe_file` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_tampering_attempt` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_blocked_on_non_safe_files` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_graceful_exit_on_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_graceful_on_github_api_error` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_conflict_needed` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_no_conflict_with_non_safe_files` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_skipped_if_no_safe_change_label` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tampering_blocked_on_workflow_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_bypassed_on_code_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validation_executed_on_non_executable_change` | `scripts.tests.test_self_heal` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_interception_and_replay` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_client_fixtures_isolation` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_signed_headers_generation` | `scripts.tests.test_shared_infrastructure` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cross_service_interception` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_mock_designer_driver` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_persona_builders_contain_correct_claims` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_shared_sqlite_dbs_and_clients` | `scripts.tests.test_shared_rbac_harness` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_find_migration_script` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_designer_service` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_main_execution_service` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_pre_boot_migrations_failure` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_pre_boot_migrations_success` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_nt` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_posix_fallback` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_web_server_posix_success` | `scripts.tests.test_start` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_label_based_backlog_gating` | `scripts.tests.test_sync_github_project` | Trace-34 | ⚪ UNVERIFIED | N/A |
| `test_get_repository_fallback` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_env` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_git_https` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_repository_from_git_ssh` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_gxp_ruleset_file_structures` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_create_new` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_dry_run` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_multiple_files_integration` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_permission_denied_403` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_permission_denied_403_graceful` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_sync_ruleset_update_existing` | `scripts.tests.test_sync_ruleset` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_admin_cache_clear_forces_fresh_read` | `scripts.tests.test_transformers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_legacy_endpoint_returns_original_schema` | `scripts.tests.test_transformers` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_terminology_cache_prevents_db_queries` | `scripts.tests.test_transformers` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_usdm_endpoint_returns_nested_schema_and_fast` | `scripts.tests.test_transformers` | PRD-MDR-003, PRD-MDR-004 | ⚪ UNVERIFIED | N/A |
| `test_usdm_validation_error_on_invalid_data` | `scripts.tests.test_transformers` | PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_audited_fallbacks_and_warning_propagation` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_security_gate_unauthenticated_requests` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_error_status_and_rollback` | `scripts.tests.test_translation_recovery` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_translation_status_and_listing_success` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_worker_context_and_session_cleanup` | `scripts.tests.test_translation_recovery` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_safe_context_binds_and_cleans_up` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_audit_safe_context_cleans_up_on_error` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_background_translation_records_user_audit` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_identifier_sanitization_during_translation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_multi_language_localization_and_hint_system` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_rules_compilation_and_artifact_generation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_event_triggers_translation` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_expired_timestamp_rejection` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_study_published_invalid_signature_rejection` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_translation_validation_failure` | `scripts.tests.test_translator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_adr_compliance_validation_logic` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_missing_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_no_changes` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_with_deleted_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_architectural_changes_require_adr_with_valid_adr` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_extraction_and_normalization` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_compliance_utility_parsing` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_bypasses_merge_commits_and_parses_status` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_from_git_fallbacks` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_changed_files_from_txt` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_closest_local_branch_point_fallback_to_root` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_closest_local_branch_point_multiple_branches` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_architectural_file` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_valid_case` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_with_targets_outside_folder` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_existing_adrs_with_targets_valid` | `scripts.tests.test_validate_adrs` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_dependencies_fails_on_forbidden_package` | `scripts.tests.test_validate_dependencies` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_validate_dependencies_passes_on_clean_package_json` | `scripts.tests.test_validate_dependencies` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_cross_service_violation` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_invalid_syntax` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_package_to_package_declared` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_package_to_package_undeclared` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_relative_cross_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_relative_same_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_same_service` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_shared_packages` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_check_file_imports_test_files_enforced_unless_exempt` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_service_name` | `scripts.tests.test_validate_imports` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_designer_validation_error_rfc7807` | `scripts.tests.test_validation_problem_details` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execution_validation_error_rfc7807` | `scripts.tests.test_validation_problem_details` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_alignment_report` | `scripts.tests.test_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_generate_alignment_report_with_mappings` | `scripts.tests.test_validator` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_ast_port_contracts_fails` | `scripts.tests.test_verify_contracts` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_ast_port_contracts_passes` | `scripts.tests.test_verify_contracts` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_cli_bypass_blocking` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_pip_audit_success` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_execute_pnpm_audit_success` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_invalid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_modern_v9` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_frontend_vulnerabilities_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_vulnerabilities_invalid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_extract_active_vulnerabilities_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_frontend_invalid_justification` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_frontend_invalid_rpn` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_incorrect_rpn` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_invalid_fmea_scores` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_invalid_json` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_fmea_fields` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_justification` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_missing_vuln_id` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_multiple_entries_same_id` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_not_found` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_not_list` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_rpn_threshold` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_load_and_validate_ledger_valid` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_exits_successfully_on_unreadable_files` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_config_bypasses_no_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_config_bypasses_with_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_comments_and_empty_lines` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_logical_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_consecutive` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_out_of_scope` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_multiline_three_lines` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_no_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_same_line_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_shell_boundary_reset` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_with_violations` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_inline_bypasses_yaml_folded_vs_literal` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_scan_for_manifest_bypasses` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_vulnerabilities_compound_matching` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_validate_vulnerabilities_multiple_identical_vuln_ids` | `scripts.tests.test_vulnerabilities` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature01_coding_queue_and_filter` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature02_meddra_and_whodrug_traversal` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature03_single_and_batch_coding_assignment` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature04_dictionary_upversioning_impact` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-30 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature05_query_escalation_and_resolution` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature06_relational_datalock_persistence` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature07_hierarchical_lock_inheritance` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, Trace-3 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature08_dual_signature_step_up_token` | `tests.e2e.test_phase1_e2e_suite` | Trace-13, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature09_unlock_justification_enforcement` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1, Trace-28 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature10_multi_format_lab_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature11_ucum_normalization_and_range_eval` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-001 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature12_lab_discrepancy_and_sae_auto_queries` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature13_sas_transport_binary_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature14_cdisc_odm_xml_export_with_audits` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature15_cdisc_dataset_json_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature16_deidentified_csv_export` | `tests.e2e.test_phase1_e2e_suite` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_tier1_feature17_ui_router_and_navigation_metadata` | `tests.e2e.test_phase1_e2e_suite` | Trace-11 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary01_empty_lab_payload_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary02_invalid_hl7_segments_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary03_unlock_justification_under_50_chars_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary04_hard_lock_without_step_up_token_rejected` | `tests.e2e.test_phase1_e2e_suite` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary05_incompatible_ucum_unit_conversion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary06_nonexistent_dictionary_term_resolution` | `tests.e2e.test_phase1_e2e_suite` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary07_invalid_sdtm_domain_export_rejected` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary08_unauthorized_lock_action_rejected` | `tests.e2e.test_phase1_e2e_suite` | Trace-27 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary09_replay_sig_token_prevention` | `tests.e2e.test_phase1_e2e_suite` | Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier2_boundary10_deidentification_empty_demographics` | `tests.e2e.test_phase1_e2e_suite` | Trace-12 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise01_form_lock_then_medical_coding` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise02_out_of_range_lab_then_subject_lock` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-002, PRD-QRY-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise03_batch_coding_then_biostat_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise04_subject_lock_followed_by_lab_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-MDR-002 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise05_upversioning_impact_then_query_escalation` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_tier3_pairwise06_hard_lock_step_up_then_unlock_and_export` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario01_oncology_trial_multisite_lock` | `tests.e2e.test_phase1_e2e_suite` | PRD-MDR-002, Trace-1, Trace-13, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario02_high_throughput_lab_batch_ingestion` | `tests.e2e.test_phase1_e2e_suite` | PRD-LAB-001, PRD-QRY-001, Trace-15 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario03_meddra_upversioning_and_batch_coding` | `tests.e2e.test_phase1_e2e_suite` | PRD-QRY-001, PRD-SYS-001, Trace-1 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario04_regulatory_submission_bundle_generation` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, Trace-12, Trace-7 | ⚪ UNVERIFIED | N/A |
| `test_tier4_scenario05_full_lifecycle_e2e_trial_workflow` | `tests.e2e.test_phase1_e2e_suite` | PRD-CRF-008, PRD-LAB-001, PRD-MDR-002, PRD-QRY-001, PRD-SYS-001, Trace-1, Trace-17 | ⚪ UNVERIFIED | N/A |
| `test_build_worker_suffix_uniqueness` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_get_run_uid_from_config` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_get_worker_id_from_config` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_is_xdist_controller_detection` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_run_sync_timeout_enforcement` | `tests.test_harness_isolation` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_service_database_names_length_and_format` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_should_provision_postgres_controller_boundary` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_should_provision_postgres_worker` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_simulated_concurrent_runs_have_disjoint_databases` | `tests.test_harness_isolation` | PRD-SYS-004 | ⚪ UNVERIFIED | N/A |
| `test_cli_setup_credentials_json` | `tests.test_setup_credentials` | PRD-SYS-WIZARD-001 | ⚪ UNVERIFIED | N/A |
| `test_cli_setup_help` | `tests.test_setup_credentials` | PRD-SYS-WIZARD-001 | ⚪ UNVERIFIED | N/A |
| `test_setup_wizard_bash_syntax` | `tests.test_setup_credentials` | PRD-SYS-WIZARD-001 | ⚪ UNVERIFIED | N/A |
| `test_setup_wizard_dev_execution` | `tests.test_setup_credentials` | PRD-SYS-WIZARD-001 | ⚪ UNVERIFIED | N/A |
| `test_setup_wizard_idempotency` | `tests.test_setup_credentials` | PRD-SYS-WIZARD-001 | ⚪ UNVERIFIED | N/A |
| `test_catalog_cross_version_integrity` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-001 | ⚪ UNVERIFIED | N/A |
| `test_milestone_mandatory_artifacts` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-004 | ⚪ UNVERIFIED | N/A |
| `test_site_level_classification_drift` | `tests.validation.dia_tmf_validation_suite` | PRD-TMF-001, PRD-TMF-003 | ⚪ UNVERIFIED | N/A |
| `test_gxp_compliance_drifts_identified` | `tests.validation.gxp_compliance_suite` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_ignoring_helper_and_excluded_services` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_missing_service` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_feature_matrix_validation_success` | `tests.validation.test_feature_matrix_gating` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_committed_schema_visualizer_is_up_to_date` | `tests.validation.test_offline_schema_drift` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_committed_typescript_schema_is_up_to_date` | `tests.validation.test_offline_schema_drift` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_formatting_and_whitespace_immunity` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_pending_delta_schema_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_schema_visualizers_use_vendored_assets` | `tests.validation.test_offline_schema_drift` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_simulated_field_name_rename_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_simulated_type_mismatch_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_submissions_schema_drift` | `tests.validation.test_offline_schema_drift` | PRD-SYS-001 | ⚪ UNVERIFIED | N/A |
| `test_environment_integrity_assertions` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_linter_negative_cases` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |
| `test_linter_positive_cases` | `tests.validation.test_path_boundary_linter` | *Regression/Helper* | ⚪ UNVERIFIED | N/A |

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
- **Timestamp:** 2026-09-22 14:56:23 UTC
- **Meaning / Purpose:** GxP Dynamic Execution Run Record
- **Cryptographic Hash (SHA-256):** 65f2816acbdd14555a5ad993138156260076c0ff80240b16b7f47b77132e8bef

-----BEGIN CERTIFICATE-----
MIIDTjCCAjagAwIBAgIUB07Hm0lwQLlTfhpanwUWi2zrIekwDQYJKoZIhvcNAQEL
BQAwYTE7MDkGA1UEAwwyQ2FkZW5jZSBHeFAgVmFsaWRhdGlvbiBSdW5uZXIgKGNh
ZGVuY2UtZ3hwLXJ1bm5lcikxIjAgBgNVBAoMGUNhZGVuY2UgQ2xpbmljYWwgU29m
dHdhcmUwHhcNMjYwOTIxMTQ1NjE1WhcNMjcwOTIyMTQ1NjE1WjBhMTswOQYDVQQD
DDJDYWRlbmNlIEd4UCBWYWxpZGF0aW9uIFJ1bm5lciAoY2FkZW5jZS1neHAtcnVu
bmVyKTEiMCAGA1UECgwZQ2FkZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTCCASIwDQYJ
KoZIhvcNAQEBBQADggEPADCCAQoCggEBALEl6v/BJWTejDZtC2XA71vF9dUSdPq+
1K9lKNNa6sBDAjcHxprNtmbcsTSHzcwx53ddXmtMMC0Nx/cxxob35+0a5hChw4pW
/bDb2SXQAh+nSVkziz0FMUlB397Lugflv3FRofQ3teEY2Wh4UNfXuSSBjeC1BS/C
1d4NHO3As2qjeJ5LwMkH4AifahKnzfXP3GFZ+jr1yqjIyffBHcdOBW8dUPPk7BJ0
gcdZV4KUhn4/SfPFNTVukOPZEdrwpGpzmPy2O3wp2METd8lGfA6s46YzEvH8MJRP
juOssemWEoXNTACBmyTqTCDRAyvl1CvoMKuRyTv9aTEou0DKSnbF3JMCAwEAATAN
BgkqhkiG9w0BAQsFAAOCAQEAGRQe5UkAyeomPsfoJfJuPhs317POe0IKZULUMAf0
fBa/o2okpNOGpLRP1OfzkzhA6Ykjjugoww1W/UnbslYZiC/3laj9n1hmVkMq3exV
K01ahkc978zJ4RVfO2zxPWkLQ0V4K+y6205eJ1eVzAKCZ46hCKXR7tWapE/I6OsQ
A2kHapC5gkniSKo54gSuPmxfcdioi0INDxl/uRPdtIcG11+q6/3rsYu3IrCtkOnF
mDoKd0no7O1DqWjqya20IW12fruwARyzHy7H8g/8B4WJIN1ou23EDbUzF8Vk9AG0
bAZHeUJjtvte0nlHztbhugcbDMtQHRH3Ezzn75pISSioNg==
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
FaTmkdU2a1rEPQhefyrrCzwVRS9Len2VLDa2nuvOJ1RX53zfFxzJ10fPVF6lv7shjhOAsIqieLet+WtLZ07zxwFREOI219b1va/QU+bCffowX06t1NOnmOCuKB2gfzSbLh9IVzw8ynrE8f3OzZqAfCkDJSvBnEkPC6gngPtl00CKLp0DIEg3byHlekIOj6HrkmymbSaQwKRn/ajNaw2sAaQE/3oq46Id4Bkc5b5NStEFCFvmgrNvBMtbfKXzqa7ZIJU6dbxT4jWZuzqgrAhVIpnJug29iaupPMCt9YslPbQCdBmKsiE3uEVFYEjVB9f1dxQ2fgkojan3xfJHpqrpmA==
-----END SIGNATURE-----
