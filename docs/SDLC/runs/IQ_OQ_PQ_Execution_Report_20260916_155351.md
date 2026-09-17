---
search: false
---

# GxP Installation & Operational Qualification (IQ/OQ/PQ) Execution Report
*Execution Date:* 2026-09-16 15:53:51 UTC
*Regulatory Protocol:* FDA 21 CFR Part 11, EU Annex 11, GAMP 5 Category 4/5, IEC 62304 Class B

## 1. Executive Summary & Verification Declaration
This report documents the Installation Qualification (IQ) and Operational Qualification (OQ) for the Cadence Clinical platform.
Based on the executed automated verification suite, the platform meets all predefined structural, functional, and security compliance constraints.

### Validation Result Summary
- **Total Automated Test Cases Run:** 2214
- **Passed:** 2214 🟢
- **Failed/Errors:** 0 🔴
- **Skipped:** 0 ⚪
- **Overall Operational Pass Rate:** 100.00%

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
apps-designer            0.1.0
apps-econsent            0.1.0
apps-eisf                0.1.0
apps-etmf                0.1.0
apps-execution           0.1.0
apps-gateway             0.1.0
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
packages-cli             0.1.0
packages-compliance      0.1.0
packages-database        0.1.0
packages-ddf             0.1.0
packages-deid            0.1.0
packages-engine-core     0.1.0
packages-hexagonal       0.1.0
packages-security        0.1.0
packages-sentinels       0.1.0
packages-storage         0.1.0
packages-testing         0.1.0
packages-tooling-core    0.1.0
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
weasyprint               69.0
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
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AggregateRootsMustImplementIGxPAuditableContract` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `AggregateRoots_MustImplement_IGxPAuditableContract` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AllFactoryMethods_ProduceDistinctCodes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplicationService_Close_ValidatesCraOrDmRole` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `` | PRD-SYS-002, PRD-SYS-053, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `` | PRD-SYS-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `` | PRD-SYS-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `AuditLogEntry_ComputesDeterministicMerkleHash_ChainedToPredecessor` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `` | PRD-SYS-004, SRS-EDC-002, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `BlockedActivation_ReturnsFixedCodeAndType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `BlockedEnrollment_ReturnsFixedCodeAndType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenMismatchedArrayLength_Fails` | `` | PRD-SUB-001, PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "   ", studyId: "STUDY-MALFORMED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "", studyId: "STUDY-MALFORMED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "SUBJ-MALFORMED", studyId: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "SUBJ-MALFORMED", studyId: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_Comparisons_EvaluatesInequalities` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext(headerValue: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext(headerValue: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateFormSubmission_Handler_ReturnsCreatedResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateQuery_Handler_ReturnsCreatedResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests.NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.CompleteAndApprove_LocksFormInReadOnlyState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.FormApplicationService_Approve_ValidatesSha256PayloadHash` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.InitializeForm_CreatesFormInDraftState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenMismatchedArrayLength_Fails` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-001)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-002)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-003)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-004)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-005)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-C)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-007)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-008)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-010)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-001)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-002)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-003)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-004)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-005)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-006)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-007)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-008)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-009)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-010)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-011)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-012)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-013)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-014)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-015)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-016)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-017)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-018)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-019)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-020)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-021)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-001-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-001-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-002-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-002-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-003-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-004-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-C)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-D)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-006-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-007-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-008-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-008-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-009-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-009-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-A)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-B)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-C)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 180, age: 30, sex: "M", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 55, age: 30, sex: "M", expectedOutOfRange: True, expectedIndicator: "LOW")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 90, age: 30, sex: "M", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 2.5, age: 50, sex: "F", expectedOutOfRange: True, expectedIndicator: "LOW")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 4, age: 50, sex: "F", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 6.5, age: 50, sex: "F", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 16, age: 25, sex: "M", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 2, age: 25, sex: "M", expectedOutOfRange: True, expectedIndicator: "LOW")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 6.5, age: 25, sex: "M", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 100.40000000000001, inputUnit: "[degF]", expectedNormValue: 38, expectedNormUnit: "Cel")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 120, inputUnit: "mm[Hg]", expectedNormValue: 120, expectedNormUnit: "mm[Hg]")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 154.31999999999999, inputUnit: "[lb_av]", expectedNormValue: 70, expectedNormUnit: "kg")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 37, inputUnit: "Cel", expectedNormValue: 37, expectedNormUnit: "Cel")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 70, inputUnit: "kg", expectedNormValue: 70, expectedNormUnit: "kg")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 98.599999999999994, inputUnit: "[degF]", expectedNormValue: 37, expectedNormUnit: "Cel")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.CreateObservation_WithValidParameters_InitializesCorrectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.FlagSdv_WithReason_ResetsSdvVerification` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.OutlierDetection_IdentifiesStatisticalOutliers` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.UcumNormalization_ConvertsUnitsCorrectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.VerifySdv_WhenAlreadyVerified_ReturnsError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.VerifySdv_WhenUnverified_TransitionsToVerified` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasUniqueIndexOnSubjectCode` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStatusWithMaxLength32` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetAsync_GivenNoMatchingRow_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_IgnoresComputedMissingDocumentsPropertyItself` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNoMatchingRow_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsSubjectConsentToSubjectConsentsTable` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_ProtocolVersionIsMappedAsComplexProperty` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.ApplicationService_Close_ValidatesCraOrDmRole` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.CancelQuery_FromOpen_SucceedsWithReason` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.QueryLifecycle_FullTransitionSequence_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.RaiseQuery_CreatesQueryInOpenState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedTag_ThrowsDecryptionFailed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.EncryptThenDecrypt_RoundTripsExactly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenNoHttpContext_ReturnsSystemDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenNoSubClaim_ReturnsSystemDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenNullBirthDate_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenNullObservationDate_ReturnsNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "   ", expected: "U")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "  male  ", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: " Woman ", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "", expected: "U")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "BOY", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "F", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "FEMALE", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "GIRL", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "M", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "MALE", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "MAN", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "U", expected: "U")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "UNK", expected: "U")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "UNKNOWN", expected: "U")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "WOMAN", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "f", expected: "F")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "m", expected: "M")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenNullInput_ReturnsU` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PreRandomization_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_ProducesSubjectInScreeningWithSuppliedFields` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_TwoSubjects_ProduceDistinctIds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForUnlockedStates_ReturnsFalse(state: Enrolled)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForUnlockedStates_ReturnsFalse(state: Screening)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_SameState_NoOpSucceedsViaGuard` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PreRandomization_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PreRandomization_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "BlockedActivation", expectedStatus: 400)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "BlockedEnrollment", expectedStatus: 400)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "InvalidStateTransition", expectedStatus: 400)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "LockedFactorMutation", expectedStatus: 422)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "NotFound", expectedStatus: 404)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "ReconsentRequired", expectedStatus: 423)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "SoftDeleteBlocked", expectedStatus: 403)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty(studyId: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty(studyId: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SiteActivationRequest_ValueEquality_ComparesByStudyId` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_ValidRequest_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: "SITE-001")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: null)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity(length: 255, expectedValid: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity(length: 256, expectedValid: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "   ", state: null)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "  ", state: "  ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "", state: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: null, state: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.ClearReconsentRequirement_SetsRequiresReconsentFalse` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(existingTag: "v1.0", incomingTag: "v1.0")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(existingTag: "v1.0", incomingTag: "v2.0")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.AllFactoryMethods_ProduceDistinctCodes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedActivation_ReturnsFixedCodeAndType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedEnrollment_ReturnsFixedCodeAndType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.NotFound_ReturnsFixedCodeAndType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs(subjectCode: SUBJ-A, studyCode: STUDY-A, encryptedDemographics: "cipher-a")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs(subjectCode: SUBJ-B, studyCode: STUDY-B, encryptedDemographics: null)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Active, expectedCode: "ACTIVE")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Completed, expectedCode: "COMPLETED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Enrolled, expectedCode: "ENROLLED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Randomized, expectedCode: "RANDOMIZED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: ReconsentRequired, expectedCode: "RECONSENT_REQUIRED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: ScreenFailed, expectedCode: "SCREEN_FAILED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Screening, expectedCode: "SCREENING")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Unblinded, expectedCode: "UNBLINDED")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Withdrawn, expectedCode: "WITHDRAWN")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "  SCREENING  ", expected: Screening)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "Reconsent Required", expected: ReconsentRequired)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "screen failed", expected: ScreenFailed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "screening", expected: Screening)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "IN_PROGRESS")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "SCREENIN")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "in progress")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: null)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Active, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Completed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Unblinded, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Completed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: ReconsentRequired, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Unblinded, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Withdrawn, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Completed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Enrolled, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Randomized, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Unblinded, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Active, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Completed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Randomized, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Unblinded, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Active, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Completed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Enrolled, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Randomized, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Screening, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Unblinded, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Completed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: ReconsentRequired, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: ScreenFailed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Unblinded, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Withdrawn, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Completed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Enrolled, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: ScreenFailed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Screening, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Unblinded, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Completed, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: ReconsentRequired, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Unblinded, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Active, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Completed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Enrolled, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Randomized, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: ReconsentRequired, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: ScreenFailed, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Screening, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Unblinded, expectedSuccess: False)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Withdrawn, expectedSuccess: True)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Enrolled)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: ReconsentRequired)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: ScreenFailed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingScreening_Succeeds` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Active)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Completed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Enrolled)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Randomized)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: ReconsentRequired)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: ScreenFailed)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Screening)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Unblinded)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Withdrawn)` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_AsNullableParameter_MayBeAbsent` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_ValidValue_IsAccepted` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_WhitespaceOrEmpty_Throws(value: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_WhitespaceOrEmpty_Throws(value: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_AtMaxLength_IsAccepted` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_ExceedsMaxLength_Throws` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_WhitespaceOrEmpty_Throws(value: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_WhitespaceOrEmpty_Throws(value: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_AtMaxLength_IsAccepted` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_Equality_IsValueBased` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ExceedsMaxLength_Throws` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ToString_ReturnsValue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_WhitespaceOrEmpty_Throws(value: "   ")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_WhitespaceOrEmpty_Throws(value: "")` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectConsentId_New_ProducesUniqueValues` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_New_ProducesUniqueValues` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_ToString_ReturnsGuidValue` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CalculateAge_GivenNullBirthDate_ReturnsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CalculateAge_GivenNullObservationDate_ReturnsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CancelQuery_FromOpen_SucceedsWithReason` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClearDemographics_PreRandomization_Succeeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClearReconsentRequirement_SetsRequiresReconsentFalse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `` | PRD-SYS-002, PRD-SYS-007, SRS-EDC-003, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `` | PRD-SYS-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_HasUniqueIndexOnSubjectCode` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_IgnoresComputedMissingDocumentsPropertyItself` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `` | PRD-SUB-001, PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsStatusWithMaxLength32` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsSubjectConsentToSubjectConsentsTable` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Configure_ProtocolVersionIsMappedAsComplexProperty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `` | PRD-SUB-001, PRD-SYS-053 | 🟢 PASSED | < 1s |
| `Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCalls_PropagatesTaskFailures` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateFormSubmission_Handler_ReturnsCreatedResult` | `` | PRD-SYS-002, PRD-SYS-007, SRS-EDC-002, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `` | PRD-SYS-001, PRD-SYS-002, PRD-SYS-007, PRD-SYS-042, PRD-SYS-053, SRS-EDC-001, SRS-EDC-002, SRS-EDC-003, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `CreateObservation_WithValidParameters_InitializesCorrectly` | `` | PRD-SYS-001, PRD-SYS-042, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `CreateQuery_Handler_ReturnsCreatedResult` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_TwoSubjects_ProduceDistinctIds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_TamperedTag_ThrowsDecryptionFailed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `DomainLayerMustNotReferenceInfrastructureOrPresentationAssemblies` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `DomainLayer_MustNotReference_InfrastructureOrPresentationAssemblies` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EdgeCaseScenario_MatchesGoldenMaster` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EncryptThenDecrypt_RoundTripsExactly` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `` | PRD-SYS-004, SRS-EDC-002, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_Comparisons_EvaluatesInequalities` | `` | PRD-SYS-004, SRS-EDC-002, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `` | PRD-SYS-004, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `` | PRD-SYS-004, SRS-EDC-002, SRS-EDC-005 | 🟢 PASSED | < 1s |
| `ExecutionDomainLayerMustNotReferenceAspNetCoreOrMassTransit` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ExecutionDomainLayer_MustNotReference_AspNetCoreOrMassTransit` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `FlagSdv_WithReason_ResetsSdvVerification` | `` | PRD-SYS-042, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `` | PRD-SYS-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `` | PRD-SYS-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_GivenNoMatchingRow_ReturnsNull` | `` | PRD-SUB-001, PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `HmacSigner_GeneratesDeterministicSignature_AndVerifiesCorrectly` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InMemoryRepositoryPersistsEntityAndIncrementsVersionIndex` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `InMemoryRepository_PersistsEntity_AndIncrementsVersionIndex` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InitializeForm_CreatesFormInDraftState` | `` | PRD-SYS-001, PRD-SYS-002, PRD-SYS-053, SRS-EDC-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `IsPostRandomization_ForLockedStates_ReturnsTrue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `IsPostRandomization_ForUnlockedStates_ReturnsFalse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators` | `` | PRD-SYS-042, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `` | PRD-SYS-001, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MergeResubmission_VersionTag_AlwaysReplacedUnconditionally` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `` | PRD-SYS-001, PRD-SYS-002, PRD-SYS-053, SRS-EDC-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `NominalScenario_MatchesGoldenMaster` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenNullInput_ReturnsU` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `NotFound_ReturnsFixedCodeAndType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `` | PRD-SYS-001, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `OutlierDetection_IdentifiesStatisticalOutliers` | `` | PRD-SYS-001, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `QueryLifecycle_FullTransitionSequence_Succeeds` | `` | PRD-SYS-007, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `RaiseQuery_CreatesQueryInOpenState` | `` | PRD-SYS-007, PRD-SYS-053, SRS-EDC-004 | 🟢 PASSED | < 1s |
| `ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `RegulatedTestSuitesMustFeatureGxPRequirementAttributes` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `RegulatedTestSuites_MustFeature_GxPRequirementAttributes` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ResultMonadHandlesSuccessAndFailureWithoutThrowingExceptions` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ResultMonad_HandlesSuccessAndFailure_WithoutThrowingExceptions` | `` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `` | PRD-SUB-001, PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `` | PRD-SYS-042, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `SiteActivationRequest_ValueEquality_ComparesByStudyId` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteCode_AsNullableParameter_MayBeAbsent` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteCode_ValidValue_IsAccepted` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteCode_WhitespaceOrEmpty_Throws` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `StudyCode_AtMaxLength_IsAccepted` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `StudyCode_ExceedsMaxLength_Throws` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `StudyCode_WhitespaceOrEmpty_Throws` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCode_AtMaxLength_IsAccepted` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCode_Equality_IsValueBased` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCode_ExceedsMaxLength_Throws` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCode_ToString_ReturnsValue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCode_WhitespaceOrEmpty_Throws` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentId_New_ProducesUniqueValues` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_ValidRequest_Passes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectId_New_ProducesUniqueValues` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectId_ToString_ReturnsGuidValue` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `ToCode_RoundTripsThroughTryParse` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `TryParse_NormalizesCasingWhitespaceAndSpaces` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UcumNormalization_ConvertsUnitsCorrectly` | `` | PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs` | `` | PRD-SYS-001, PRD-SYS-042, PRD-SYS-121, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `` | PRD-SYS-053, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `` | PRD-SYS-002, PRD-SYS-053, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateDemographics_PreRandomization_Succeeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `` | PRD-SYS-002, SRS-EDC-002, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `` | PRD-SYS-002, PRD-SYS-053, SRS-EDC-003 | 🟢 PASSED | < 1s |
| `UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateStratFactors_PreRandomization_Succeeds` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserId_GivenNoHttpContext_ReturnsSystemDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserId_GivenNoSubClaim_ReturnsSystemDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `` | PRD-SYS-002, PRD-SYS-042, SRS-EDC-001, SRS-EDC-002 | 🟢 PASSED | < 1s |
| `VerifySdv_WhenAlreadyVerified_ReturnsError` | `` | PRD-SYS-042, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `VerifySdv_WhenUnverified_TransitionsToVerified` | `` | PRD-SYS-042, SRS-EDC-001 | 🟢 PASSED | < 1s |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `` | PRD-SUB-001 | 🟢 PASSED | < 1s |
| `WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `` | PRD-SUB-001, PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `` | PRD-SYS-053 | 🟢 PASSED | < 1s |
| `AggregateRootsMustImplementIGxPAuditableContract` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AggregateRoots_MustImplement_IGxPAuditableContract` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DomainLayerMustNotReferenceInfrastructureOrPresentationAssemblies` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DomainLayer_MustNotReference_InfrastructureOrPresentationAssemblies` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ExecutionDomainLayerMustNotReferenceAspNetCoreOrMassTransit` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ExecutionDomainLayer_MustNotReference_AspNetCoreOrMassTransit` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RegulatedTestSuitesMustFeatureGxPRequirementAttributes` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RegulatedTestSuites_MustFeature_GxPRequirementAttributes` | `Cadence.ArchitectureTests.ArchitectureSentinelTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_BlockedPath_AuditEntryIsDurablyCommittedBeforeTheFailureIsObservedByTheCaller` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenCompliantSite_SucceedsWritesSiteActivationAuditEntryAndDoesNotFlipIsComplete` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenIncompleteComplianceRow_FailsWithBlockedActivationAndWritesAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ActivateSiteAsync_GivenNoComplianceRow_FailsWithBlockedActivationAndWritesAuditEntryFirst` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenExistingRowForSameKey_IncrementsVersionAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNoExistingRowAndNullMissingArtifacts_CreatesRowWithEmptyMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNoExistingRow_CreatesNewRowWithVersionOneAndWritesNoAuditEntry` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.ApplyEtmfWebhookAsync_GivenNullSiteIdWithNoExistingRow_SucceedsAsNoOpSinceAggregateCannotRepresentASitelessRow` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests.GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_GivenExistingRow_ReturnsItsCurrentState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_GivenNoExistingRow_ReturnsIncompleteWithEmptyMissingDocumentsAndNoException` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatusAsync_MethodSignature_HasNoFailureReturnPath` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceApplicationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_AlwaysOverwrites_NeverMergesWithPriorContents` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_CalledTwiceInSequence_IncrementsVersionByOneEachCallWithNoRejection` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.ApplyWebhookUpdate_EmptyMissingDocuments_FullyClearsPriorNonEmptyList` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests.MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_InitialState_HasVersionOneAndSuppliedMissingDocuments` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MissingDocuments_CastToMutableListAndMutated_DoesNotAffectAggregateState` | `Cadence.UnitTests.Execution.Compliance.SiteComplianceCacheTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "   ", studyId: "STUDY-MALFORMED")` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "", studyId: "STUDY-MALFORMED")` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "SUBJ-MALFORMED", studyId: "   ")` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing(subjectId: "SUBJ-MALFORMED", studyId: "")` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests.Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenEmptyOrWhitespaceOnlySubjectIdOrStudyId_DropsSilentlyWithoutCreatingAnythingOrThrowing` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenNoExistingSubject_AssignsTheEnrollmentIndexViaTheSingleSharedRepositoryMethod` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenNoExistingSubject_CreatesScreeningSubjectAtIndexZeroWithAuditEntryAndConsentCacheRow` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenSaveChangesThrowsADbUpdateException_PropagatesToMassTransitsFaultPipelineRatherThanBeingSwallowed` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenSubjectAlreadyExistsWithNoMatchingConsentRow_BackfillsConsentWithoutRecreatingSubject` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Consume_GivenTheSameEventDeliveredTwice_ResultsInExactlyOneClinicalSubjectNotTwo` | `Cadence.UnitTests.Execution.Consumers.EConsentCompletedConsumerTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_Comparisons_EvaluatesInequalities` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests.EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_ArithmeticExpressions_EvaluatesCorrectly` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_Comparisons_EvaluatesInequalities` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_IndexedRepeat_ResolvesNestedArrayReference` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_KleeneLogic_EvaluatesThreeValuedTruthTables` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_MixedTypeComparisons_HandlesIntDoubleAndNullSafely` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_RecursionDepthLimit_ProtectsAgainstStackOverflow` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAst_RelativePathResolution_ResolvesBareAndSubjectPrefixedPaths` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateBatchRules_FlagsSystolicLowerThanDiastolicPhysiologyViolation` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateInlineConstraints_FlagsReferenceRangeViolations` | `Cadence.UnitTests.Execution.EditChecks.AstEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests.BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits` | `Cadence.UnitTests.Execution.EditChecks.BatchEditCheckTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext(headerValue: "   ")` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests.InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext(headerValue: "")` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderAbsent_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderPresentAndPopulated_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_HeaderPresentButWhitespaceOnly_ShortCircuitsWith400AndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ChangeReasonHeaderFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ActivateSite_EndToEnd_NonCompliantSite_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.AllDIRegistrationsAddedByThisTicket_ResolveSuccessfullyFromARealRequestScope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests.Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ComplianceStatus_EndToEnd_NoCacheRowExists_Returns200WithFalseAndEmptyDefaults` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateClient_BootingTheRealProgramCompositionRoot_DoesNotThrow` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfWebhook_EndToEnd_NewMilestone_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Healthz_GivenTheFullyWiredCompositionRoot_StillReturnsTheUnchangedPreExistingResponse` | `Cadence.UnitTests.Execution.Endpoints.ExecutionProgramStartupTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.ApproveFormSubmission_Handler_WithUnauthorizedRole_ReturnsForbiddenResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.ApproveFormSubmission_Handler_WithValidSignature_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateFormSubmission_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.CreateQuery_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests.VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CloseQuery_Handler_WithCraRole_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CloseQuery_Handler_WithUnauthorizedRole_ReturnsForbiddenProblemResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateFormSubmission_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateObservation_Handler_ReturnsCreatedResultWithLocation` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateQuery_Handler_ReturnsCreatedResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UnlockFormSubmission_Handler_WithAuditReason_ReturnsOkResult` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `VerifyObservationSdv_Handler_ReturnsOkWithVerifiedState` | `Cadence.UnitTests.Execution.Endpoints.ObservationAndFormEndpointsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSiteAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenNonCompliantSiteResult_Returns400WithExactBlockedActivationDetail` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ActivateSite_GivenValidRequest_Returns200WithExactSuccessEnvelopeNamingTheSite` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.ApplyEtmfWebhookAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests.GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfWebhook_GivenInvalidPayload_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfWebhook_GivenValidPayload_Returns200WithExactSuccessEnvelope` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenNoCacheRowResult_StillReturns200NeverAProblemResponse` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenNoMilestoneQueryParam_DefaultsToSiteActivation` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetComplianceStatus_GivenStudyIdAndMilestoneQueryParams_ThreadsBothThroughAsSnakeCaseKeys` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSiteComplianceStatusAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SiteComplianceEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.AdvanceSubjectStateAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests.UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateSubjectAsync_GivenPreCancelledToken_ThrowsOperationCanceledExceptionRatherThanCompleting` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateSubject_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateSubject_GivenValidRequest_Returns201WithLocationHeaderPointingAtCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenCallerWithBlindedRoleClaim_ResolvesIsBlindedCallerTrue_AndSerializesTreatmentGroupAsNull` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenCallerWithoutBlindedRoleClaim_ResolvesIsBlindedCallerFalse_AndDoesNotRedact` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSubjectDetailAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSubjectDetail_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSubjectDetail_GivenValidRequest_Returns200WithDetailBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_GivenInvalidRequest_AlsoShortCircuitsWith400_MatchingTheCanonicalRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_GivenValidRequest_BehavesIdenticallyToTheCanonicalConsentRoute` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAlias_IsExcludedFromGeneratedOpenApiDocument_WhileCanonicalRouteIsIncluded` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsentAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenInvalidRequest_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordSubjectConsent_GivenValidRequest_Returns200WithConsentResponseBody` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographicsAsync_GivenPreCancelledToken_ThrowsOperationCanceledException` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectDemographics_GivenValidRequestAndHeader_Returns200` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenInvalidBody_ShortCircuitsWith400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenMissingChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenSubjectNotFound_Returns404WithStandardizedDetail` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenValidRequestAndHeader_Returns200AndThreadsTheHeaderValueThrough` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateSubjectState_GivenWhitespaceOnlyChangeReasonHeader_Returns400AndNeverInvokesApplicationService` | `Cadence.UnitTests.Execution.Endpoints.SubjectEndpointsRoutingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests.InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidRequestArgumentNotAtIndexZero_StillShortCircuitsWith400` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidSubjectConsentRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_InvalidSubjectCreateRequest_ShortCircuitsWith400ValidationProblemAndNeverInvokesNext` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_NoArgumentOfRequestType_ThrowsInvalidOperationException` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_RequestArgumentNotAtIndexZero_StillLocatesAndValidatesIt` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_ValidSubjectConsentRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvokeAsync_ValidSubjectCreateRequest_InvokesNextAndReturnsItsResultUnchanged` | `Cadence.UnitTests.Execution.Endpoints.ValidationFilterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests.NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity` | `Cadence.UnitTests.Execution.Forms.FormSubmissionGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.InitializeForm_CreatesFormInDraftState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests.UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CompleteAndApprove_LocksFormInReadOnlyState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FormApplicationService_Approve_ValidatesSha256PayloadHash` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InitializeForm_CreatesFormInDraftState` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UnlockForm_WithReason_RevertsToDraftAndUnlocks` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdatePayload_InDraftState_UpdatesPayloadSuccessfully` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError` | `Cadence.UnitTests.Execution.Forms.FormSubmissionStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenMismatchedArrayLength_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenLiteralExpectedValue_ComparesDirectly` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenMismatchedArrayLength_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenNestedObjectAndArrayFields_RecursesCorrectlyAndIgnoresExtraActualFields` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueNonNullSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNonNullActual_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenOpaqueSentinelAndNullActual_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndDivergingPostState_Fails` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelAndMatchingPreState_Passes` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests.BodyMatches_GivenUnchangedSentinelWithNoPreStateSupplied_ThrowsExplainingWhy` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertBodyMatchesTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenAMatchingEntry_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.AuditLogContains_GivenNoMatchingEntry_ThrowsWithScenarioIdAndRecordedEntries` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests.DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DbStateMatches_GivenAssertionThatDoesNotHold_ThrowsWithScenarioIdAndFieldDetail` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DbStateMatches_GivenAssertionThatHolds_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterAssertDbAndAuditTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests.LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenAMismatchedFixturePair_ThrowsRatherThanSilentlyOmittingTheOrphanedScenario` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenAnInputEntryWithNoCounterpart_DoesNotSilentlyDropIt` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenOutputEntriesWithNoInputCounterpart_DoesNotThrow` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealEdgeCasePair_ReturnsAllElevenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealNominalFixturePair_ReturnsAllNineteenScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_GivenTheRealValidationErrorPair_ReturnsAllTwentyOneScenarioIds` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LoadScenarios_PreservesInputFileOrderForMultiStepScenarios` | `Cadence.UnitTests.Execution.GoldenMaster.GoldenMasterFixtureLoaderTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-001)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-002)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-003)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-004)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-005)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-006-C)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-007)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-008)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.EdgeCaseScenario_MatchesGoldenMaster(scenario: EDGE-010)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-001)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-002)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-003)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-004)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-005)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-006)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-007)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-008)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-009)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-010)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-011)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-012)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-013)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-014)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-015)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-016)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-017)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-018)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-019)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-020)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.ErrorScenario_MatchesExpectedStatusAndDetail(scenario: INV-021)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-001-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-001-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-002-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-002-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-003-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-004-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-C)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-005-D)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-006-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-007-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-008-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-008-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-009-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-009-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-A)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-B)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests.NominalScenario_MatchesGoldenMaster(scenario: SCN-010-C)` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EdgeCaseScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ErrorScenario_MatchesExpectedStatusAndDetail` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NominalScenario_MatchesGoldenMaster` | `Cadence.UnitTests.Execution.GoldenMaster.SubjectGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_ConcurrentCalls_PropagatesTaskFailures` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates` | `Cadence.UnitTests.Execution.Integration.EnrollmentIndexConcurrencyTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 180, age: 30, sex: "M", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 55, age: 30, sex: "M", expectedOutOfRange: True, expectedIndicator: "LOW")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "GLUC", value: 90, age: 30, sex: "M", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 2.5, age: 50, sex: "F", expectedOutOfRange: True, expectedIndicator: "LOW")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 4, age: 50, sex: "F", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "K", value: 6.5, age: 50, sex: "F", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 16, age: 25, sex: "M", expectedOutOfRange: True, expectedIndicator: "HIGH")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 2, age: 25, sex: "M", expectedOutOfRange: True, expectedIndicator: "LOW")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators(testCode: "WBC", value: 6.5, age: 25, sex: "M", expectedOutOfRange: False, expectedIndicator: "NORMAL")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 100.40000000000001, inputUnit: "[degF]", expectedNormValue: 38, expectedNormUnit: "Cel")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 120, inputUnit: "mm[Hg]", expectedNormValue: 120, expectedNormUnit: "mm[Hg]")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 154.31999999999999, inputUnit: "[lb_av]", expectedNormValue: 70, expectedNormUnit: "kg")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 37, inputUnit: "Cel", expectedNormValue: 37, expectedNormUnit: "Cel")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 70, inputUnit: "kg", expectedNormValue: 70, expectedNormUnit: "kg")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests.UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs(inputValue: 98.599999999999994, inputUnit: "[degF]", expectedNormValue: 37, expectedNormUnit: "Cel")` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LabRangeEvaluation_GoldenMasterVectors_MatchExpectedIndicators` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SdvVerification_StateTransitions_MaintainStrictPart11Audit` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UcumNormalization_GoldenMasterVectors_MatchExpectedOutputs` | `Cadence.UnitTests.Execution.Observations.ObservationGoldenMasterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.CreateObservation_WithValidParameters_InitializesCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.FlagSdv_WithReason_ResetsSdvVerification` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.OutlierDetection_IdentifiesStatisticalOutliers` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.UcumNormalization_ConvertsUnitsCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.VerifySdv_WhenAlreadyVerified_ReturnsError` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Observations.ObservationUnitTests.VerifySdv_WhenUnverified_TransitionsToVerified` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateObservation_WithValidParameters_InitializesCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FlagSdv_WithReason_ResetsSdvVerification` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LabRangeService_EvaluatesNormalAndOutOfRangeValues` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ObservationApplicationService_Create_PersistsAndReturnsNormalizedObservation` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `OutlierDetection_IdentifiesStatisticalOutliers` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UcumNormalization_ConvertsUnitsCorrectly` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `VerifySdv_WhenAlreadyVerified_ReturnsError` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `VerifySdv_WhenUnverified_TransitionsToVerified` | `Cadence.UnitTests.Execution.Observations.ObservationUnitTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_HasUniqueIndexOnSubjectCode` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStatusWithMaxLength32` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests.SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_HasUniqueIndexOnSubjectCode` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsClinicalSubjectToClinicalSubjectsTable` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsEncryptedDemographicsWithUnboundedMaxLength` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsStatusWithMaxLength32` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsStratFactorsBackingFieldToJsonbColumn` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex` | `Cadence.UnitTests.Execution.Persistence.ClinicalSubjectConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests.SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClinicalSubjectAndSubjectConsentShadowVersionProperties_AreConfiguredAsPlainConcurrencyTokensNotRowVersions` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoContextsLoadTheSameRowAndTheFirstSavesFirst_TheSecondsStaleSaveThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenAModifiedEntity_IncrementsShadowVersionByExactlyOne` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenNewlyAddedEntity_LeavesShadowVersionAtItsDefaultStartingValue` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SavingChangesAsync_GivenTwoDomainMutationsBeforeOneSaveChangesAsync_IncrementsShadowVersionOnlyOnce` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SavingChangesAsync_NeverIncrementsSiteComplianceCacheVersionDomainCounter` | `Cadence.UnitTests.Execution.Persistence.ConcurrencyTokenIncrementInterceptorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests.GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ExistsAsync_GivenNoMatchingConsent_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent` | `Cadence.UnitTests.Execution.Persistence.ConsentRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests.WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_DoesNotItselfCallSaveChanges_EntryOnlyVisibleAfterCallerSaves` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenDifferentTableNames_ChainsEachIndependently` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenFirstEntryForATableName_PersistsWithEmptyPredecessorHashAndVersionIndexOne` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenGxPAuditInterceptorAlsoRegistered_BothAuditTrailsCoexistIndependentlyInOneSaveChangesCall` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenNoOldOrNewValues_ChangesJsonIsNull` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenOldAndNewValues_SerializesBothIntoChangesJson` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `WriteAsync_GivenTwoCallsForSameTableNameAcrossSeparateSaves_ChainsSecondPredecessorHashToFirstBlockHash` | `Cadence.UnitTests.Execution.Persistence.EfAuditLogWriterTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests.SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenARowDeletedOutFromUnderTheRetryingWriter_ExhaustsRetriesAndStillThrowsDbUpdateConcurrencyException` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenAStaleWriteThatAnotherWriterAlreadyCommitted_ResolvesTheConflictAndPersistsThisCallersChange` | `Cadence.UnitTests.Execution.Persistence.ExecutionRepositoryBaseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests.SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainIntegrationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests.SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenIGxPAuditableEntityNotInheritingAggregateRootBase_WritesAuditLogEntryWithoutFieldStamping` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenModifiedAggregate_IncrementsVersionIndexWithoutOverwritingCreatedBy` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenNonStringTIdAggregate_StampsCreatedFields` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoEntityTypesModifiedInSameCall_ChainsEachEntityTypeIndependently` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SaveChangesAsync_GivenTwoSequentialSaves_ChainsPredecessorHashForSameEntityType` | `Cadence.UnitTests.Execution.Persistence.GxPAuditInterceptorMerkleChainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_IgnoresComputedMissingDocumentsPropertyItself` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests.SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_IgnoresComputedMissingDocumentsPropertyItself` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceCacheConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.AddAsync_ThenSaveChangesAsync_PersistsANewRowRetrievableByGetAsync` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests.GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenMatchingStudySiteAndMilestone_ReturnsTheRow` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenNullSiteCode_ReturnsNullEvenWhenARowExistsForTheStudyAndMilestone` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_GivenRowForADifferentMilestone_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetAsync_ReturnsATrackedEntity_MutatingItAndCallingSaveChangesPersistsTheUpdate` | `Cadence.UnitTests.Execution.Persistence.SiteComplianceRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_MapsSubjectConsentToSubjectConsentsTable` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.Configure_ProtocolVersionIsMappedAsComplexProperty` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests.SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsShadowVersionPropertyAsConcurrencyToken` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_MapsSubjectConsentToSubjectConsentsTable` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Configure_ProtocolVersionIsMappedAsComplexProperty` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion` | `Cadence.UnitTests.Execution.Persistence.SubjectConsentConfigurationTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests.GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByCodeAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetByIdAsync_GivenNoMatchingRow_ReturnsNull` | `Cadence.UnitTests.Execution.Persistence.SubjectRepositoryTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplicationService_Close_ValidatesCraOrDmRole` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.ApplicationService_Close_ValidatesCraOrDmRole` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.ApplicationService_PreventsDuplicateActiveQueryOnSameCoordinates` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.CancelQuery_FromOpen_SucceedsWithReason` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.QueryLifecycle_FullTransitionSequence_Succeeds` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests.RaiseQuery_CreatesQueryInOpenState` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CancelQuery_FromOpen_SucceedsWithReason` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `QueryLifecycle_FullTransitionSequence_Succeeds` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RaiseQuery_CreatesQueryInOpenState` | `Cadence.UnitTests.Execution.Queries.QueryLifecycleTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_TamperedTag_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests.EncryptThenDecrypt_RoundTripsExactly` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_MalformedBase64_ThrowsDecryptionFailedWrappingFormatException` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_OnFailure_ExceptionMessageDoesNotLeakPlaintextOrCiphertext` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_TamperedCiphertextBody_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_TamperedNonce_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_TamperedTag_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_ValidJsonButNotAnObject_ThrowsWithSpecificMessage` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Decrypt_WithDifferentKey_ThrowsDecryptionFailed` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EncryptThenDecrypt_RoundTripsExactly` | `Cadence.UnitTests.Execution.Security.AesGcmDemographicsEncryptionServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenNoHttpContext_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenNoSubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests.UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserId_GivenEmptySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserId_GivenNoHttpContext_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserId_GivenNoSubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault` | `Cadence.UnitTests.Execution.Security.CurrentAuditContextAccessorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenNullBirthDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenNullObservationDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "   ", expected: "U")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "  male  ", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: " Woman ", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "", expected: "U")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "BOY", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "F", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "FEMALE", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "GIRL", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "M", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "MALE", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "MAN", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "U", expected: "U")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "UNK", expected: "U")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "UNKNOWN", expected: "U")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "WOMAN", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "f", expected: "F")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom(input: "m", expected: "M")` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenNullInput_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests.NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CalculateAge_GivenNullBirthDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CalculateAge_GivenNullObservationDate_ReturnsNull` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CalculateAge_GivenOneDaySpan_ReturnsExactFixedDivisorValue` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CalculateAge_GivenTwentyYearSpan_ReturnsExactComputedValueNotApproximation` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenBirthDateFailingBothParseAttempts_ReturnsNullAgeWithoutThrowing` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDateWithTrailingGarbageAfterWhitespace_FallsBackToLeadingDateToken` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDecryptFailure_ReturnsDefaultShapeAndDoesNotThrow` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenDictionaryShapeWithEncryptedDemographicsKey_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenEmptyCiphertextString_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenGenderKeyAbsent_FallsBackToSexKey` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenIso8601BirthDateWithTAndZOffset_ParsesDateComponentCorrectly` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenNoBirthDateKeyPresent_ReturnsNullAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenNullSubject_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenObjectShapeWithEncryptedDemographicsProperty_DecryptsAndDerivesResult` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenObjectShapeWithNullEncryptedDemographicsProperty_ReturnsDefaultShape` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenPreserveCustomTrue_ForwardsFlagToNormalizeGender` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetSafeDemographics_GivenRawCiphertextStringShape_DecryptsAndDerivesGenderAndAge` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenMappedInput_ReturnsExpectedCodeRegardlessOfPreserveCustom` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenNullInput_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomFalse_ReturnsU` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NormalizeGender_GivenUnmappedInputWithPreserveCustomTrue_ReturnsTrimmedUppercasedVerbatim` | `Cadence.UnitTests.Execution.Security.DemographicsNormalizationServiceTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Active)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Active)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.ClearDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_TwoSubjects_ProduceDistinctIds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Active)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForLockedStates_ReturnsTrue(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForUnlockedStates_ReturnsFalse(state: Enrolled)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.IsPostRandomization_ForUnlockedStates_ReturnsFalse(state: Screening)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Active)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Active)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.UpdateStratFactors_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests.Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClearDemographics_And_UpdateStratFactors_ProduceDistinctErrorCodes` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClearDemographics_PostRandomization_FailsWithSoftDeleteBlocked` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClearDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_ProducesSubjectInScreeningWithSuppliedFields` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_TwoSubjects_ProduceDistinctIds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_WithNullSiteCodeAndNullStratFactors_LeavesThemEmpty` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `IsPostRandomization_ForLockedStates_ReturnsTrue` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `IsPostRandomization_ForUnlockedStates_ReturnsFalse` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `TransitionTo_IllegalTransition_LeavesStatusUnchangedAndReturnsGuardFailure` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `TransitionTo_LegalTransition_UpdatesStatusAndReturnsSuccess` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `TransitionTo_SameState_NoOpSucceedsViaGuard` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Unblind_OnRandomizedSubject_SetsAllUnblindFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Unblind_OnScreeningSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographics_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographics_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateStratFactors_PostRandomization_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateStratFactors_PreRandomization_Succeeds` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Withdraw_AfterUnblinding_SetsWithdrawalFieldsFromClock` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Withdraw_CalledTwiceOnAlreadyWithdrawnSubject_SucceedsSilentlyAsNoOp` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Withdraw_OnScreenFailedSubject_ReturnsFailureWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.ClinicalSubjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_NoCodeExtensionPresent_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_SubjectNotFoundCode_ForcesStatusTo404` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ApplyKnownCodeStatus_UnrecognizedCodeExtension_LeavesStatusUnchanged` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "BlockedActivation", expectedStatus: 400)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "BlockedEnrollment", expectedStatus: 400)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "InvalidStateTransition", expectedStatus: 400)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "LockedFactorMutation", expectedStatus: 422)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "NotFound", expectedStatus: 404)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "ReconsentRequired", expectedStatus: 423)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus(factory: "SoftDeleteBlocked", expectedStatus: 403)` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests.ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetStatusCode_HypotheticalConflictTypedCodeOtherThanLockedFactorMutation_MapsToDefault409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ToProblemResult_LockedFactorMutation_Returns422NotTheConflictDefaultOf409` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ToProblemResult_MapsEachSubjectErrorsFactoryToExpectedStatus` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ToProblemResult_ReconsentRequired_Returns423NotTheForbiddenDefaultOf403` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ToProblemResult_SoftDeleteBlocked_Returns403WithNoOverrideNeeded` | `Cadence.UnitTests.Execution.Subjects.DomainErrorHttpMappingTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty(studyId: "   ")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty(studyId: "")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SiteActivationRequest_ValueEquality_ComparesByStudyId` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectConsentRequestValidator_ValidRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: "")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: "SITE-001")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes(siteId: null)` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity(length: 255, expectedValid: True)` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity(length: 256, expectedValid: False)` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "   ", state: null)` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "  ", state: "  ")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: "", state: "")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode(status: null, state: "   ")` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests.SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_BlankStudyId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_EmptyMilestone_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_NullSiteIdAndMissingArtifacts_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EtmfCompletenessWebhookPayloadValidator_ValidPayload_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteActivationRequest_ValueEquality_ComparesByStudyId` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_EmptyProtocolVersionStudyId_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_EmptyProtocolVersionVersionTag_FailsWithSingleError` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_NullProtocolVersion_FailsWithSingleErrorWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentRequestValidator_ValidRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_EmptyStudyId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_EmptySubjectId_FailsNotEmpty` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_FullyPopulatedRequest_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SiteIdExceedsMaxLength_FailsMaximumLength` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SiteIdNullEmptyOrPopulated_Passes` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCreateRequestValidator_SubjectIdAndStudyIdLengthBoundary_MatchesExpectedValidity` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectDemographicsUpdateRequest_AllFieldsOmitted_ConstructsWithNulls` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectDemographicsUpdateRequest_PopulatedStratFactors_RoundTrips` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequestValidator_BothBlankOrWhitespace_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequestValidator_BothNull_FailsWithMissingTargetStateErrorCode` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_EmptyStatusWithPopulatedState_ValidatorPassesButTargetPrefersEmptyStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_StatusEnrolledStateNull_ValidatesAndTargetResolvesToStatus` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectStateUpdateRequest_StatusNullStateEnrolled_ValidatesAndTargetResolvesToState` | `Cadence.UnitTests.Execution.Subjects.RequestValidatorTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests.RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_CascadeClearance_DoesNotTouchADifferentSubjectsConsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenExistingRowForSameVersionIndex_MergesIntoExistingRowRatherThanCreatingNew` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenIncomingNeitherSignedNorPaperOverride_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenIncomingRequiresReconsentTrue_DoesNotCascadeClearOtherRows` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNewConsentSignedWithoutRequiringReconsent_CascadesClearanceToOtherRowsForSameSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNewConsent_WritesRecordConsentAuditEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenNoExistingRow_CreatesNewConsentNotAMerge` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `RecordConsentAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests.CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenEachEnrollment_WritesEnrollSubjectAuditEntryForThatSubject` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenEmptyStudy_EnrollsSequentialSubjectsWithZeroBasedIndexAndScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenExplicitlyEmptySiteId_ThrowsRatherThanTreatingItAsAbsent` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenNoDemographics_EncryptedDemographicsIsNullAndEncryptionIsNeverInvoked` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenPopulatedDemographics_PersistsEncryptedCiphertextNeverRawPlaintext` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `CreateAsync_GivenSiteId_ReturnsFromAggregateWithSiteIdPopulated_NotUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceCreateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests.UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenPostRandomizationSubject_FailsWithSoftDeleteBlocked_DistinctFromLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenPreRandomizationSubject_SucceedsAndReturnsUnderpopulatedWithClearedDemographics` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `DeleteDemographicsAsync_OnSuccess_WritesNoAuditLogEntry` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithBothFieldsSupplied_StratFactorsFailureShortCircuitsBeforeDemographicsIsTouched` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubjectWithCorruptedExistingCiphertext_ThrowsDecryptionFailure_NotLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_ChangedDemographicsWithDecryptableExistingCiphertext_FailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPostRandomizationSubject_StratFactorsUpdateFailsWithLockedFactorMutation` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubjectWithCorruptedExistingCiphertext_SucceedsWithoutEverDecrypting` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_DemographicsUpdateSucceeds_ReEncryptsAndAudits` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenPreRandomizationSubject_StratFactorsUpdateSucceedsAndPersists` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `UpdateDemographicsAsync_ReturnsUnderpopulatedResponse_NotFromAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDemographicsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests.GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerFalse_ReturnsTheRealTreatmentGroup_NotRedacted` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAllThreeRtsmFieldsToNull` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenBlindedCallerTrue_RedactsAnActuallyPopulatedTreatmentGroup_NotJustAnAlreadyNullOne` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_IsFullyPopulated_ReflectingTheAggregatesRealCurrentState_UnlikeSubjectResponseUnderpopulated` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_RandomizationSeedAndInvestigationalProductId_AreAlwaysNull_RegardlessOfBlinding` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `GetDetailAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceDetailTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenCompliantSite_TargetingEnrolled_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenForbiddenTransition_ReturnsInvalidStateTransitionAndDoesNotMutate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenLegalTransition_SucceedsAndPersistsNewStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenLegalTransition_WritesTransitionAuditEntryNamedForTargetState` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNeitherStatusNorState_ReturnsInvalidStateTransitionRatherThanThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNoComplianceRowAtAll_TargetingEnrolled_IsTreatedAsNonCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNoSiteId_TargetingEnrolled_IsBlockedRatherThanAutoCompliant` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenNonCompliantSite_TargetingEnrolled_FailsWithBlockedEnrollmentAndWritesBlockedAudit` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSubjectNotFoundByIdOrCode_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSuccessfulTransitionToEnrolled_ResponseStatusIsHardcodedScreening_NotTheRealValue` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenSurrogateId_ResolvesTheSameSubjectAsTheDomainCodeWould` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_GivenUnmappedStateString_ReturnsInvalidStateTransition_SameCodeAsForbiddenTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.AdvanceStateAsync_TargetingStateOtherThanEnrolled_NeverConsultsComplianceGate` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests.EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenEligibleAtNonCompliantSite_FailsIdenticallyToAdvanceStateAsync` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenEligible_TransitionsToEnrolled` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenIneligible_TransitionsToScreenFailed` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenNoEvaluatorConfigured_ThrowsRatherThanGuessingAVerdict` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_GivenSubjectNotFound_ReturnsNotFoundError` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_ResolvesSubjectByDomainCode_NotSurrogateId` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `EvaluateAndTransitionScreeningAsync_UsesAuditContextUserIdAndChangeReason_NotALiteralFallback` | `Cadence.UnitTests.Execution.Subjects.SubjectApplicationServiceStateTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.ClearReconsentRequirement_SetsRequiresReconsentFalse` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: False, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: False, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: True, incoming: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(existing: True, incoming: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(existingTag: "v1.0", incomingTag: "v1.0")` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests.MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(existingTag: "v1.0", incomingTag: "v2.0")` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ClearReconsentRequirement_SetsRequiresReconsentFalse` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_IcfSignedDateOmitted_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSignedDateNull_ExistingDatePreserved` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `MergeResubmission_VersionTag_AlwaysReplacedUnconditionally` | `Cadence.UnitTests.Execution.Subjects.SubjectConsentTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AllFactoryMethods_ProduceDistinctCodes` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BlockedActivation_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `BlockedEnrollment_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.AllFactoryMethods_ProduceDistinctCodes` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedActivation_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.BlockedEnrollment_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.NotFound_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests.SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InvalidStateTransition_ReturnsFixedCodeAndFreeTextMessage` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `LockedFactorMutation_ReturnsFixedCodeAndConflictType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `NotFound_ReturnsFixedCodeAndType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ReconsentRequired_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SoftDeleteBlocked_ReturnsFixedCodeAndForbiddenType` | `Cadence.UnitTests.Execution.Subjects.SubjectErrorsTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs(subjectCode: SUBJ-A, studyCode: STUDY-A, encryptedDemographics: "cipher-a")` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs(subjectCode: SUBJ-B, studyCode: STUDY-B, encryptedDemographics: null)` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests.Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ComplianceStatusResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FromAggregate_And_Underpopulated_DivergeOnStatus_ForTheIdenticalAggregate` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FromAggregate_EnrolledSubjectWithSite_ReflectsAggregatesActualState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `FromAggregate_ScreeningSubject_ReportsScreeningStatus` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `OperationEnvelopeResponse_SerializesWithSpecFieldNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectDetailResponse_SerializesWithSpecSnakeCasePropertyNames` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectResponse_SerializesWithSpecSnakeCasePropertyNamesAndPlainScalarValues` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Underpopulated_AlwaysHardcodesStatusAndActiveProtocolVersion_RegardlessOfInputs` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Underpopulated_ForEnrolledSubjectWithSite_HardcodesScreeningDefaultsRegardlessOfRealState` | `Cadence.UnitTests.Execution.Subjects.SubjectResponseTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Active, expectedCode: "ACTIVE")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Completed, expectedCode: "COMPLETED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Enrolled, expectedCode: "ENROLLED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Randomized, expectedCode: "RANDOMIZED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: ReconsentRequired, expectedCode: "RECONSENT_REQUIRED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: ScreenFailed, expectedCode: "SCREEN_FAILED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Screening, expectedCode: "SCREENING")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Unblinded, expectedCode: "UNBLINDED")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.ToCode_RoundTripsThroughTryParse(state: Withdrawn, expectedCode: "WITHDRAWN")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "  SCREENING  ", expected: Screening)` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "Reconsent Required", expected: ReconsentRequired)` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "screen failed", expected: ScreenFailed)` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_NormalizesCasingWhitespaceAndSpaces(raw: "screening", expected: Screening)` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "   ")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "IN_PROGRESS")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "SCREENIN")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: "in progress")` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests.TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing(raw: null)` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ToCode_RoundTripsThroughTryParse` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `TryParse_NormalizesCasingWhitespaceAndSpaces` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `TryParse_UnmappedOrEmptyInput_ReturnsFalseWithoutThrowing` | `Cadence.UnitTests.Execution.Subjects.SubjectStateCodecTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Active, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Completed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Unblinded, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Active, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Completed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: ReconsentRequired, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Unblinded, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Completed, target: Withdrawn, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Completed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Enrolled, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Randomized, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Unblinded, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Enrolled, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Active, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Completed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Randomized, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Unblinded, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Randomized, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Active, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Completed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Enrolled, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Randomized, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Screening, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Unblinded, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ReconsentRequired, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Completed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: ReconsentRequired, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: ScreenFailed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Unblinded, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: ScreenFailed, target: Withdrawn, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Completed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Enrolled, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: ScreenFailed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Screening, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Unblinded, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Screening, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Completed, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: ReconsentRequired, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Unblinded, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Unblinded, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Active, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Completed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Enrolled, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Randomized, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: ReconsentRequired, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: ScreenFailed, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Screening, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Unblinded, expectedSuccess: False)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_FullTransitionMatrix_MatchesSpecGraph(current: Withdrawn, target: Withdrawn, expectedSuccess: True)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Active)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Completed)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Enrolled)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Randomized)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: ReconsentRequired)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: ScreenFailed)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition(target: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Active)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Completed)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Enrolled)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Randomized)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: ReconsentRequired)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: ScreenFailed)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Screening)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Unblinded)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests.Guard_SameStateNoOp_AlwaysSucceeds(state: Withdrawn)` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Guard_FullTransitionMatrix_MatchesSpecGraph` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Guard_NullCurrentTargetingNonScreening_FailsWithInvalidStateTransition` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Guard_NullCurrentTargetingScreening_Succeeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Guard_ReconsentRequiredFanOut_CoversEveryOtherState` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Guard_SameStateNoOp_AlwaysSucceeds` | `Cadence.UnitTests.Execution.Subjects.SubjectTransitionGraphTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_AsNullableParameter_MayBeAbsent` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_ValidValue_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_WhitespaceOrEmpty_Throws(value: "   ")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SiteCode_WhitespaceOrEmpty_Throws(value: "")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_WhitespaceOrEmpty_Throws(value: "   ")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.StudyCode_WhitespaceOrEmpty_Throws(value: "")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_Equality_IsValueBased` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_ToString_ReturnsValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_WhitespaceOrEmpty_Throws(value: "   ")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectCode_WhitespaceOrEmpty_Throws(value: "")` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectConsentId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `Cadence.UnitTests.Execution.Subjects.ValueObjectTests.SubjectId_ToString_ReturnsGuidValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteCode_AsNullableParameter_MayBeAbsent` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteCode_ValidValue_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SiteCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `StudyCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `StudyCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `StudyCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCode_AtMaxLength_IsAccepted` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCode_Equality_IsValueBased` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCode_ExceedsMaxLength_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCode_ToString_ReturnsValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectCode_WhitespaceOrEmpty_Throws` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectConsentId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectId_New_ProducesUniqueValues` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `SubjectId_ToString_ReturnsGuidValue` | `Cadence.UnitTests.Execution.Subjects.ValueObjectTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `AuditLogEntry_ComputesDeterministicMerkleHash_ChainedToPredecessor` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `HmacSigner_GeneratesDeterministicSignature_AndVerifiesCorrectly` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InMemoryRepositoryPersistsEntityAndIncrementsVersionIndex` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `InMemoryRepository_PersistsEntity_AndIncrementsVersionIndex` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ResultMonadHandlesSuccessAndFailureWithoutThrowingExceptions` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |
| `ResultMonad_HandlesSuccessAndFailure_WithoutThrowingExceptions` | `Cadence.UnitTests.SecurityAndDomainTests` | *Regression/Helper* | 🟢 PASSED | < 1s |

## 4. Performance Qualification (PQ) & Scenario Validation
Performance Qualification documents the verification of end-to-end clinical workflow scenarios defined in Section 5 of the QA & Validation Plan.

### TC-VAL-LOG-001: Protocol Version Locking & Immutability Rejection
- **Target Requirements:** PRD-MDR-001, PRD-UNI-003
- **Description:** Verifies that locked study version nodes in Neo4j are completely immutable, and direct database manipulations are rejected.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-002: Stratification Factor Re-randomization Rejections
- **Target Requirements:** PRD-SUB-002, PRD-SUB-001
- **Description:** Verifies that stratification factor modifications and backward state machine updates are strictly forbidden once randomized.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-003: Offline Mode Data Entry, Sync Collision & Conflict Resolution
- **Target Requirements:** PRD-EDC-004, PRD-UNI-002
- **Description:** Verifies that offline data entries are synchronized accurately, conflict resolution runs deterministically, and the audit ledger captures all states.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

### TC-VAL-LOG-004: Re-authentication Enforcement during Emergency Unblinding
- **Target Requirements:** PRD-MDR-003, PRD-UNI-002
- **Description:** Verifies that unblinding requests require strict multi-factor re-authentication, trigger immediate unblinded state transition, lock the trial on tampering, and dispatch security alerts.
- **Verification Status:** ✅ Verified Compliant via Automated Integration Suite

## 5. Qualification Review & Authorization
This GxP computerized system validation log is compiled with mathematical determinism directly from the execution runners of the build system.
```
Lead Systems Validation Engineer:   ___________________________   Date: _______________
Director of Clinical Quality Assurance: ___________________________   Date: _______________
```

---

## Electronic Signature Block

- **Signer Identity:** jules
- **Timestamp:** 2026-09-16 15:53:51 UTC
- **Meaning / Purpose:** GxP Dynamic Execution Run Record
- **Cryptographic Hash (SHA-256):** f70b5c84fd5e4ebe708dfad967e0e3df91dd4966042bad3b5394e733bd66797a

-----BEGIN CERTIFICATE-----
MIIDNDCCAhygAwIBAgIUIzEsF3cDL2ZTWaJSmZ3MSDX7QWIwDQYJKoZIhvcNAQEL
BQAwVDEuMCwGA1UEAwwlQ2FkZW5jZSBHeFAgVmFsaWRhdGlvbiBSdW5uZXIgKGp1
bGVzKTEiMCAGA1UECgwZQ2FkZW5jZSBDbGluaWNhbCBTb2Z0d2FyZTAeFw0yNjA5
MTUxNTUzNTJaFw0yNzA5MTYxNTUzNTJaMFQxLjAsBgNVBAMMJUNhZGVuY2UgR3hQ
IFZhbGlkYXRpb24gUnVubmVyIChqdWxlcykxIjAgBgNVBAoMGUNhZGVuY2UgQ2xp
bmljYWwgU29mdHdhcmUwggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCl
rxaZmmlkm66ymv1oTwCoZ3VX3+EItalv8nVHAYpY7UEIh4RjWUMyBl7PAkCqVHKV
CtRA/pr7ZcKL/gnmHnrFLxVZ3RkWkc0J5ckMkmXLDSRfioaJLIXDQSBm3G+dJWrq
X57bEWDyhPkdrl809lKzscHalLWzMRwsrXwuyutYJTfpXjVGXCSm44R95JmylyEx
Vtl2Y+QBW/0LfK/jqlQbJjSnkaPvI4cpbiJmNDXQ1FwDdsZ+Wh7sDi692rrFMZJ9
uo0L9vm8MUCzhMdA3kRpoC/VYx3dD7z5qGsbQxRRHHS0Sk0n94w3vkBXKAuwyj11
UGiNd/O4d4zgS0Gq5gb1AgMBAAEwDQYJKoZIhvcNAQELBQADggEBAAHl7xAkZs7M
80E3OgghpkI10DEqV3vpcrT2jIZ5pyrSjTcIhmYHNU3ehmgtUYZooor9f6iv0xyD
wlQaaVQB3ilQvIdgU2MWVL0fdkRdrV5d15XUfJvrrhCq6v8WPvJytWJfTyVzYGTr
aaGB2nNnjN4ho3dWzHHyoM71Lk/BVRVDBP/UKcpcYaTb85qYLmpBHwE66GRdyJi7
5An02LIguecXamqP6r/0u6DyBJKap16t8zVKoa67DzA+b/q4YjS4OpLYjW2vCJ5f
H2qHQVe5N0oISihv/q5rsB1M8LTcQWuzYaQD6y5qfRL8HjsoEIemha5aKs7U2FWx
dzEcQWCr6uE=
-----END CERTIFICATE-----
-----BEGIN SIGNATURE-----
W8LNikRg3tcuVExYd4aQeb6gfhMtFgelMvKV6ygykFv3ambV9o1CtmFHuVZTeBvu9LfC5THpk2C5uRBYmqhv+Rt7VjR+KGfdER0WmeM6VrD+HC/x95Ux3OuYTTVgFdcaLGhQugQ9e6Y7N/HZgxcSnwtxGZ8w50b/K/OFDcBCBLXYhq8jHeFQfyf3LE7VE+WRJ3L39L+iJ6D2ypJUpVlb09dNxmSLUuCXAKDVvyeMQhWPehFOsWa3qehaHnpuLS9BY7YUyIPDSiZH9Kf3yWCqvIrBjd0MlY7IfKdUWVFM/ZMzG6Pr9wZZzW1rvmmtKgPvZMxn6M02K0PYA0HtPtRlpA==
-----END SIGNATURE-----
