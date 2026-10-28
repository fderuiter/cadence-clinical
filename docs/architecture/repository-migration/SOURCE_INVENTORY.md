# Source inventory and dependency graph (Phase 0, task 1)

**Status:** read-only inventory. No source has moved, been deleted or been changed. Every destination is a *proposal* until the gates in [MIGRATION_PLAN.md](MIGRATION_PLAN.md) and the ADR in task 2 clear. Ownership marked `needs decision` is not assigned and must not be inferred from folder names.

This document is the shared source for the ADR draft (task 2) and kernel extraction preparation (task 3). Both must reference it instead of repeating the analysis.

## Revisions

| Repository | Role | Revision inspected |
| --- | --- | --- |
| `cadence-clinical` | source (active implementation) | `e3955e120e725cc976aa7b9b42e921c104fc20e3`, HEAD of `claude/project-thread-8cv98f` (`docs: register Cadence product repositories and controlled migration program`, 2026-10-28). Not separately re-checked against `main`; re-verify before any extraction PR. |
| `cadence-kernel` | destination (core) | `9466a7a1854cc86f19e68f0bf24cf27b566791d3` (default branch) |
| `cadence-study-design` | destination (Blueprint) | `b5a3df99ec97a180c46a7021cbe6fc96548ea5e4` |
| `cadence-clinical-data` | destination (Capture) | `c1ca3110019d81073c1d976c128b1fdb52790138` |
| `cadence-trial-operations` | destination (Orbit) | `ff88b70b155aec5d551984d34ab5bfb0c9c623f1` |
| `cadence-participants` | destination (Engage) | `5fbf1ce272caeabfe9ca91acf4e12027a8fd5815` |
| `cadence-interventions` | destination (Allocate) | `ec118cf0c17fbe9491c492c1b5898ffb3d90e904` |
| `cadence-safety` | destination (Vigil) | `cb004c3b4b700b7e0f93e28ffd129e42ca9ca54d` |
| `cadence-evidence` | destination (Lens) | `31f488a638c6f3b72f898f4e2e68ae3ea9c9e453` |
| `cadence-quality-regulatory` | destination (Assure) | `388a9420f6685f34053281369dcb0be49585907d` |

Destination SHAs were read from the GitHub default branch on the date above. They are recorded for traceability only.

## Method

1. Counted tracked files per `apps/*` and `packages/*` (`git ls-files`).
2. Extracted `ProjectReference` edges from every `.csproj` under `apps/` and `packages/` to build the project dependency graph.
3. Grepped `using Cadence.*` directives to check for application-to-application and application-to-kernel-internal references.
4. Read the kernel boundary ratchet (`tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt`) and grouped its entries by assembly.
5. File-level mapping for the four ambiguous classes (`apps/execution`, `apps/interop`, `apps/knowledge`, `apps/fileshare`) plus `apps/org`. The first pass is path- and name-based (rules in the generator, summarised under *Basis*). A row is `proposed` only when the path name is unambiguous. Everything else is `needs decision`. **This is not yet symbol-level review**; each `needs decision` row needs a reviewer to read the types and their callers before task 2 closes.

Not done in this revision: per-file tables, API endpoint lists, event contracts, test inventories, licence provenance, maturity classification, and acceptance evidence. These are the remaining fields in the *Next inventory format* of [CAPABILITY_OWNERSHIP_MATRIX.md](CAPABILITY_OWNERSHIP_MATRIX.md) and are listed as open work below.

## Directory inventory

Counts are tracked files; `.cs` is C# source. Migrations are included in the counts.

| Directory | Files | `.cs` | `.py` | Target per matrix | Notes |
| --- | ---: | ---: | ---: | --- | --- |
| `apps/execution` | 226 | 222 | 0 | Capture + Allocate + Core split | Ambiguous. Two projects: `src/` (standalone service) and `module/` (kernel module). Not movable as one directory. |
| `apps/intelligence` | 109 | 101 | 0 | kernel + clinical (AI policies) | Kernel module consumed by `kernel-api`. |
| `apps/safety` | 79 | 25 | 0 | Vigil | |
| `apps/subject-portal` | 87 | 0 | 0 | Engage | Vue. |
| `apps/web` | 442 | 0 | 0 | clinical (web shell) | Vue. |
| `apps/etmf` | 23 | 20 | 0 | trial-operations | |
| `apps/econsent` | 21 | 19 | 0 | participants | |
| `apps/ctms` | 21 | 17 | 0 | trial-operations | |
| `apps/fileshare` | 19 | 17 | 0 | clinical (generic) + trial-operations (TMF grants) | Ambiguous. |
| `apps/notifications` | 22 | 14 | 0 | clinical (platform) | |
| `apps/payments` | 24 | 22 | 0 | trial-operations | |
| `apps/interop` | 26 | 24 | 0 | split: clinical-data, participants, evidence | Ambiguous. |
| `apps/imaging` | 16 | 14 | 0 | evidence | |
| `apps/knowledge` | 15 | 13 | 0 | split: needs decision | Ambiguous. |
| `apps/eisf` | 10 | 8 | 0 | trial-operations | |
| `apps/org` | 12 | 10 | 0 | trial-operations; authority in kernel | Authority conflict. |
| `apps/quality` | 12 | 10 | 0 | quality-regulatory | |
| `apps/tickets` | 10 | 8 | 0 | trial-operations | |
| `apps/kernel-api` | 10 | 8 | 0 | clinical (composition host) | Only consumer of kernel internals. |
| `apps/gateway` | 8 | 6 | 0 | clinical | |
| `apps/designer` | 4 | 2 | 0 | study-design | |
| `apps/apphost` | 6 | 1 | 0 | clinical | |
| `packages/kernel` | 446 | 426 | 0 | kernel | Contains `Cadence.Rules` and `Cadence.Standards` (see below). |
| `packages/security` | 64 | 51 | 10 | platform | |
| `packages/tooling_core` | 45 | 41 | 0 | platform / CLI | |
| `packages/engine_core` | 63 | 28 | 32 | shared utility | Depended on by most apps; see graph. |
| `packages/ui` | 46 | 0 | 0 | workspace shared UI | Vue. |
| `packages/ddf` | 30 | 9 | 16 | study-design | |
| `packages/cli` | 28 | 26 | 0 | clinical (developer CLI) | |
| `packages/compliance` | 17 | 15 | 0 | shared utility | |
| `packages/testing` | 17 | 15 | 0 | shared test helpers | |
| `packages/database` | 12 | 10 | 0 | platform | |
| `packages/storage` | 12 | 6 | 3 | platform (generic storage) | |
| `packages/deid` | 8 | 6 | 0 | shared utility | |
| `packages/contracts` | 9 | 7 | 0 | shared contracts | |
| `packages/telemetry`, `templating`, `topology`, `sentinels`, `hexagonal`, `core-models`, `usdm-schemas`, `__init__.py` | small | | | platform / shared | |

Python under `packages/` (`engine_core`, `ddf`, `security`, `storage`) is a reference or legacy surface. Per [AGENTS.md](../../../AGENTS.md), do not re-add Python. Its extraction status is tracked in [PYTHON_REFERENCE_INVENTORY.md](../../PYTHON_REFERENCE_INVENTORY.md).

## Dependency graph

### Kernel packages

```
Cadence.Kernel.Abstractions          (no project refs)              public SDK surface
  ├─ Cadence.Kernel.Modules          -> Abstractions, engine_core.Domain
  ├─ Cadence.Kernel.Domain           -> Abstractions, engine_core.Domain
  ├─ Cadence.Kernel.Application      -> Abstractions, Domain, Modules
  ├─ Cadence.Kernel.Persistence      -> Abstractions, Domain, Application
  ├─ Cadence.Kernel.Api              -> Abstractions, Application, Persistence
  ├─ Cadence.Rules                   -> Abstractions
  └─ Cadence.Standards               -> Abstractions
```

### Applications and packages (direct project references)

- Every product app references `Cadence.Kernel.Abstractions` (directly or through `Cadence.Core.Domain`) and no kernel `Domain`, `Application`, `Persistence` or `Api` project. **The only exception is `apps/kernel-api`**, the composition host, which references all kernel projects. This matches K-XS-2 / ADR-2600 as intended.
- `apps/execution/module` references `Kernel.Abstractions` and `Kernel.Modules`. It is the kernel-integrated module. `apps/execution/src` is a separate service project that references `Kernel.Abstractions` only.
- `apps/kernel-api` references `apps/intelligence` and `apps/execution/module` in-process (ADR-2610 single composition host).
- `apps/apphost` references every app project (composition only).
- `packages/tooling_core` (OpenAPI and schema exporters) references every app project, which is expected for tooling but means tooling breaks on any app extraction.
- `packages/engine_core/Cadence.Core.Domain` is referenced by nearly every app and by `Kernel.Domain`. It is shared utility, not a kernel concept, and must not grow kernel types.
- `packages/database`, `security`, `telemetry`, `contracts` are platform packages referenced by most apps.

### Cross-application references

A `using Cadence.Apps.<Other>` search across `apps/*` returned **no** cross-application source references. Applications currently share only platform packages and the kernel abstractions. Extraction does not need to break an existing app-to-app source dependency. Cross-product calls are HTTP or events, so an extraction is a contract change, not a code move.

### Kernel-internal references from apps

Six files reference `Cadence.Kernel.Persistence`, `Domain`, `Application` or `Api` from `apps/` and all of them sit in `apps/kernel-api/src`. No product app reaches kernel internals.

## Forbidden redefinitions (spec §2.3, K-XS-1)

The boundary ratchet holds **496** pre-existing violations (353 `K-XS-1`, 143 `K-MOD-1`). It may only shrink. Largest contributors:

| Assembly | Entries | Relevance to extraction |
| --- | ---: | --- |
| `Cadence.Apps.Execution` | 142 | Subject, device, CGT and cec types. `ClinicalSubject`/`SubjectId` overlap kernel `Participant`. Extraction must resolve these, not move them. |
| `Cadence.Apps.Payments` | 51 | Trial-operations. |
| `Cadence.Apps.Interop` | 49 | Includes `ClinicalQuery` and `InteropAuditLog`. |
| `Cadence.Apps.Ctms` | 44 | Includes `DoaDelegationRecord`, `CtmsDelegation`. |
| `Cadence.Apps.Econsent` | 28 | Consent scope belongs to kernel `ConsentStatus`. |
| `Cadence.Core.Domain` | 16 | `Randomization.SubjectAllocationRequest` `StudyId`/`SiteId` members. Core is itself in the baseline. |
| `Cadence.Apps.Knowledge` | 24 | `DelegationRecord`, `OrgAuditLog`, `KnowledgeArticleAuditLog`. |
| `Cadence.Apps.Org` | 13 | `DelegationOfAuthority`, `OrgAuditLog`, `TrainingLog`. |
| `Cadence.Apps.Fileshare` | 6 | `FileAuditLog`, `PlaybackAuditLog`. |

Type-level redefinitions present in source (verified by grep, not only by baseline): `ClinicalSubject`, `SubjectConsent`, `DelegationRecord`, `DelegationOfAuthority`, `OrgAuditLog`, `InteropAuditLog`, `KnowledgeArticleAuditLog`, `FileAuditLog`, `PlaybackAuditLog`, `BiostatExportAuditLog`, `ClinicalQuery`. Each is either in the baseline or is a candidate for it. Extraction must not copy any of them into a destination repository under the same name. The forbidden-name rule applies to the destination too.

## Ambiguous classes: file-level

### Decisions needed before any extraction of these classes

1. **Subject and consent (`execution/src` Subjects, ConsentRepository).** Is `ClinicalSubject` a kernel `Participant` projection, a Capture enrolment record, or an Engage record? Matrix says Engage owns eConsent and the kernel owns `ConsentStatus` scope. Needs an ADR decision.
2. **Lab reference ranges and toxicity grading (`Observations`, `LabToxicity`, `StagedAdverseEventReview`).** Evidence, Vigil or Capture?
3. **CEC adjudication and CGT chain of custody.** Adjudication committee outputs and custody events are not clearly in any matrix row.
4. **Execution persistence.** `ExecutionDbContext` holds all execution tables in one context. Splitting it requires a per-table owner inventory and an ADR-2600 review. The 45 migration-related files under `apps/execution` (migrations, designer files and snapshots) are not yet mapped per owner.
5. **Shared abstractions and security.** Demographics encryption, allocation protection, audit context and `ChangeReasonHeaderFilter` may belong to kernel SDK, platform security, or a single consumer.
6. **Org delegation.** `DelegationOfAuthority` is kernel authority under spec §2.3. Org only administers people. Decide who owns the row.
7. **Knowledge split.** Training is Orbit administration with kernel qualification; protocol knowledge may be Blueprint; product help content may be the shell. Decide.
8. **Interop residue.** `ClinicalQuery`, `InteropAuditLog`, `EhrEndpointRecord`, `SmartSigningKeys`, `DeviceTokenHasher` and the SCE token signer need owner decisions. Device registration overlaps Allocate accountability.
9. **Fileshare.** Conversion and transcoding are generic platform. TMF `ShareGrant` and `GuestLink` belong to Orbit. `FileAuditLog` and `PlaybackAuditLog` conflict with the audit naming rule.

### Mapping table (first pass, path and name rules)

Each row is one `.cs` file outside `Migrations/` and `Configurations/` in the four ambiguous apps and `apps/org`. `Status` is `proposed` (unambiguous from the path) or `needs decision`.

Configuration and migration files are covered by their owning entity, not listed separately. Tests are not in this table; the test inventory is open work.

| Current path (under `apps/`) | Proposed destination | Status | Basis |
|---|---|---|---|
| `execution/module/Application/FormCommands.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Application/FormQueryCommands.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Application/FormQueryHandlers.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Application/MirrorFormQueryToKernel.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Application/OpenFormInstanceHandler.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Application/OpenFormOnCollectionOpened.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Domain/FormInstance.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Domain/FormQuery.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Endpoints/FormEndpoints.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Endpoints/FormQueryEndpoints.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Endpoints/FormQueryResponses.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Endpoints/FormResponses.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Endpoints/FormSignatureEndpoints.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/ExecutionModule.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Infrastructure/ExecutionModuleDatabase.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Infrastructure/ExecutionModuleDbContext.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Infrastructure/ExecutionModuleDbContextDesignTimeFactory.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/module/Infrastructure/ExecutionSignatureTokenDbContext.cs` | capture (cadence-clinical-data) | proposed | eCRF form engine, kernel-integrated module (ADR-2600 path) |
| `execution/src/Application/Abstractions/AuthenticatedCaller.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/ConcurrencyConflictException.cs` | needs decision | needs decision | shared abstraction; keep with its single consumer or move to kernel SDK |
| `execution/src/Application/Abstractions/IAllocationProtector.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/IAuditLogWriter.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/ICurrentAuditContextAccessor.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/IDemographicsEncryptionService.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/IDemographicsNormalizationService.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Abstractions/ILabRangeService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Abstractions/IUcumNormalizationService.cs` | needs decision | needs decision | shared abstraction; keep with its single consumer or move to kernel SDK |
| `execution/src/Application/Abstractions/ServiceAccount.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Application/Cec/CecApplicationService.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Application/Cec/ChairAdjudicateRequest.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Application/Cec/ChairAdjudicationResponse.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Application/Cec/ICecApplicationService.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Application/Cgt/CgtApplicationService.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Application/Cgt/CgtDtos.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Application/Cgt/ICgtApplicationService.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Application/Coding/ISynonymImportService.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Application/Coding/SynonymDtos.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Application/Coding/SynonymFileParser.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Application/Coding/SynonymImportService.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Application/Compliance/ComplianceStatusResponse.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Application/Compliance/EtmfCompletenessWebhookPayload.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Application/Compliance/ISiteComplianceApplicationService.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Application/Compliance/SiteActivationRequest.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Application/Compliance/SiteComplianceApplicationService.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Application/Devices/DeviceApplicationService.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Application/Devices/DeviceDtos.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Application/Devices/DeviceEvents.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Application/Devices/IDeviceApplicationService.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Application/Observations/BulkLabRangeFileParser.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Observations/ILabRangeImportService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Observations/LabRangeDtos.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Observations/LabRangeImportService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/OperationEnvelopeResponse.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Application/Randomization/IRandomizationApplicationService.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Application/Randomization/RandomizationApplicationService.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Application/Randomization/RandomizationDtos.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Application/Serialization/ExecutionJsonSerialization.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Application/Serialization/ValueObjectJsonConverters.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Application/Subjects/ISubjectApplicationService.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectApplicationService.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectAuthority.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectConsentRequest.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectConsentResponse.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectCreateRequest.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectDemographicsUpdateRequest.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectDetailResponse.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectListResponse.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectResponse.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Subjects/SubjectStateUpdateRequest.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Application/Supply/IIpKitApplicationService.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Application/Supply/IpKitApplicationService.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Application/Supply/IpKitDtos.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Application/Toxicity/ILabToxicityGovernanceService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Toxicity/LabToxicityGovernanceService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Application/Toxicity/ToxicityDtos.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Cec/CecChairAdjudication.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Domain/Cec/CecDossier.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Domain/Cec/SignatureManifest.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Domain/Cgt/CgtBedsideVerification.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Cgt/CgtChainOfCustody.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Cgt/CgtDestructionCertificate.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Cgt/CgtMilestoneEvent.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Cgt/CgtVialReturnReconciliation.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Cgt/Errors/CgtExceptions.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Domain/Coding/BulkSynonymImportJob.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Domain/Coding/SynonymRule.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Domain/Compliance/SiteComplianceCache.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Domain/Devices/DeviceDeficiency.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Devices/DeviceInventoryItem.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Devices/DeviceQuarantineRecord.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Devices/Errors/DeviceErrors.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Devices/UdiParser.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Observations/BulkLabRangeImportJob.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Observations/DomainCode.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Observations/LabReferenceRange.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Observations/TestCode.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Ports/ICecRepository.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Domain/Ports/IConsentRepository.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Ports/IDeviceRepository.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Domain/Ports/IRandomizationRepository.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Domain/Ports/ISiteComplianceRepository.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Domain/Ports/ISubjectRepository.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Randomization/RandomizationAllocation.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Domain/Randomization/RandomizationErrors.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Domain/Randomization/RandomizationScheme.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Domain/Subjects/ClinicalSubject.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/DomainErrorExtensions.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/DomainErrorHttpMapping.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/ProtocolVersionRef.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SiteCode.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/StudyCode.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectCode.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectConsent.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectConsentId.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectErrors.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectId.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectListCriteria.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectState.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectStateCodec.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Subjects/SubjectTransitionGraph.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Domain/Supply/IpKitInventoryItem.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Domain/Supply/IpKitReconciliationBatch.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Domain/Supply/IpKitReconciliationCertificate.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Domain/Toxicity/IStagedAdverseEventReviewRepository.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Domain/Toxicity/StagedAdverseEventReview.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Endpoints/CallerIdentity.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Endpoints/CecEndpoints.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Endpoints/CgtEndpoints.cs` | needs decision | needs decision | cell and gene therapy chain of custody: allocate (supply custody) vs capture |
| `execution/src/Endpoints/ConcurrencyConflictExceptionHandler.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Endpoints/DeviceEndpoints.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Endpoints/Filters/ChangeReasonHeaderFilter.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Endpoints/Filters/ValidationFilter.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Endpoints/IpKitEndpoints.cs` | interventions (cadence-interventions) | proposed | investigational supply and kit reconciliation |
| `execution/src/Endpoints/LabRangeEndpoints.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Endpoints/LabToxicityEndpoints.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Endpoints/RandomizationEndpoints.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Endpoints/SiteComplianceEndpoints.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Endpoints/SubjectEndpoints.cs` | needs decision | needs decision | no rule matched; inspect symbols |
| `execution/src/Endpoints/SynonymEndpoints.cs` | clinical-data (cadence-clinical-data) | proposed | clinical coding synonyms |
| `execution/src/Infrastructure/Consumers/EConsentCompletedConsumer.cs` | participants (cadence-participants) | proposed | consumes eConsent completion; contract must be versioned |
| `execution/src/Infrastructure/Consumers/EConsentCompletedEvent.cs` | participants (cadence-participants) | proposed | consumes eConsent completion; contract must be versioned |
| `execution/src/Infrastructure/Normalization/LabRangeService.cs` | needs decision | needs decision | lab reference ranges and toxicity grading: evidence (labs) vs safety vs capture |
| `execution/src/Infrastructure/Normalization/UcumNormalizationService.cs` | needs decision | needs decision | UCUM and demographics normalization: platform utility vs kernel |
| `execution/src/Infrastructure/OpenApi/ExecutionWireTypesOpenApiTransformer.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `execution/src/Infrastructure/Persistence/ConcurrencyTokenIncrementInterceptor.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/EfAuditLogWriter.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Persistence/ExecutionDbContext.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/ExecutionDbContextFactory.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/ExecutionDbSeeder.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/Repositories/AdvisoryLockSql.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/Repositories/CecRepository.cs` | needs decision | needs decision | clinical events committee adjudication; signatures bind to kernel signing |
| `execution/src/Infrastructure/Persistence/Repositories/ConsentRepository.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Infrastructure/Persistence/Repositories/DeviceRepository.cs` | interventions (cadence-interventions) | proposed | device accountability and quarantine; scientific telemetry belongs to evidence |
| `execution/src/Infrastructure/Persistence/Repositories/ExecutionRepositoryBase.cs` | needs decision | needs decision | shared DbContext: split only after per-owner table inventory and ADR-2600 review |
| `execution/src/Infrastructure/Persistence/Repositories/RandomizationRepository.cs` | interventions (cadence-interventions) | proposed | randomization scheme and allocation |
| `execution/src/Infrastructure/Persistence/Repositories/SiteComplianceRepository.cs` | trial-operations (cadence-trial-operations) | proposed | site activation gating driven by eTMF completeness |
| `execution/src/Infrastructure/Persistence/Repositories/StagedAdverseEventReviewRepository.cs` | needs decision | needs decision | no rule matched; inspect symbols |
| `execution/src/Infrastructure/Persistence/Repositories/SubjectRepository.cs` | needs decision | needs decision | Subject/consent identity overlaps kernel Participant/ConsentStatus (spec §2.3 forbids app-level Subject type) |
| `execution/src/Infrastructure/Security/AesGcmAllocationProtector.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Security/AesGcmDemographicsEncryptionService.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Security/AmbientAuditContextAccessor.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Security/DemographicsDecryptionFailedException.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Security/DemographicsEncryptionOptions.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Infrastructure/Security/DemographicsNormalizationService.cs` | needs decision | needs decision | field encryption and audit context: kernel (audit/authority) vs platform security |
| `execution/src/Program.cs` | clinical (platform shell) | proposed | host/platform plumbing; ChangeReasonHeaderFilter is a kernel command concern: needs decision |
| `fileshare/src/Domain/FileshareEntities.cs` | clinical (platform shell) | proposed | fileshare plumbing |
| `fileshare/src/Infrastructure/Persistence/FileshareDbContext.cs` | clinical (platform shell) | proposed | fileshare plumbing |
| `fileshare/src/Infrastructure/Persistence/FileshareDbContextFactory.cs` | clinical (platform shell) | proposed | fileshare plumbing |
| `fileshare/src/Program.cs` | clinical (platform shell) | proposed | fileshare plumbing |
| `fileshare/src/Workers/DocumentConversionWorker.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/DocumentConverterProcessor.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/FileshareWorkerOptions.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/IDocumentConverterProcessor.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/IMediaTranscoderProcessor.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/MediaTranscoderProcessor.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `fileshare/src/Workers/MediaTranscoderWorker.cs` | clinical (platform shell) | proposed | generic document conversion and media transcoding |
| `interop/src/Application/Services/FhirObservationMapper.cs` | clinical-data (cadence-clinical-data) | proposed | eSource / EHR ingestion adapter; provenance stays kernel |
| `interop/src/Application/Services/LockfileValidatorService.cs` | evidence (cadence-evidence) | proposed | statistical computing environment and biostat exports; lockfile/token authority to confirm |
| `interop/src/Application/Services/SceDockerEngineDriver.cs` | evidence (cadence-evidence) | proposed | statistical computing environment and biostat exports; lockfile/token authority to confirm |
| `interop/src/Domain/InteropEntities.cs` | needs decision | needs decision | interop residual; split by behaviour |
| `interop/src/Domain/SceEntities.cs` | evidence (cadence-evidence) | proposed | statistical computing environment and biostat exports; lockfile/token authority to confirm |
| `interop/src/Endpoints/EsourceEndpoints.cs` | clinical-data (cadence-clinical-data) | proposed | eSource / EHR ingestion adapter; provenance stays kernel |
| `interop/src/Endpoints/SceEndpoints.cs` | evidence (cadence-evidence) | proposed | statistical computing environment and biostat exports; lockfile/token authority to confirm |
| `interop/src/Infrastructure/Persistence/InteropDbContext.cs` | needs decision | needs decision | interop residual; split by behaviour |
| `interop/src/Infrastructure/Persistence/InteropDbContextFactory.cs` | needs decision | needs decision | interop residual; split by behaviour |
| `interop/src/Infrastructure/Security/DeviceTokenHasher.cs` | evidence (cadence-evidence) | needs decision | device telemetry is Lens; device registration overlaps Allocate accountability |
| `interop/src/Infrastructure/Security/SceTokenSigner.cs` | evidence (cadence-evidence) | proposed | statistical computing environment and biostat exports; lockfile/token authority to confirm |
| `interop/src/Infrastructure/Security/SmartSigningKeys.cs` | clinical (platform shell) | needs decision | SMART/token signing: platform security vs owning module |
| `interop/src/Program.cs` | needs decision | needs decision | interop residual; split by behaviour |
| `knowledge/src/Domain/KnowledgeEntities.cs` | needs decision | needs decision | knowledge residual; split by behaviour |
| `knowledge/src/Endpoints/TrainingEndpoints.cs` | trial-operations (cadence-trial-operations) | needs decision | training administration to Orbit; qualification authority stays kernel |
| `knowledge/src/Infrastructure/Persistence/KnowledgeDbContext.cs` | needs decision | needs decision | knowledge residual; split by behaviour |
| `knowledge/src/Infrastructure/Persistence/KnowledgeDbContextFactory.cs` | needs decision | needs decision | knowledge residual; split by behaviour |
| `knowledge/src/Program.cs` | needs decision | needs decision | knowledge residual; split by behaviour |
| `org/src/Domain/OrgEntities.cs` | needs decision | needs decision | org residual |
| `org/src/Infrastructure/Persistence/OrgDbContext.cs` | needs decision | needs decision | org residual |
| `org/src/Infrastructure/Persistence/OrgDbContextFactory.cs` | needs decision | needs decision | org residual |
| `org/src/Program.cs` | needs decision | needs decision | org residual |

## Open work before task 2 closes

- `docs/kernel/EXECUTION_MIGRATION_INVENTORY.md` is dated 2026-09-29 and stale (see the task 3 plan §10). Reconcile it with this file rather than keeping two execution inventories.
- Symbol-level review of every `needs decision` row (92 execution, plus 15 in interop, knowledge, fileshare and org).
- Per-file fields from the next inventory format: maturity, canonical tables, API and events, tests, risk, retirement PR.
- Test inventory per app and the PostgreSQL baseline for clinical write guarantees.
- Licence provenance for `packages/kernel` and the Python reference packages.

## Symbol-level review (read-only, second pass)

Produced by reading every needs-decision row. The facts are from file reads and word-match grep; test counts are file counts, not test-method counts. Owners are suggestions for the ADR owner to decide, not decisions. Confidence is in the suggestion. Maturity is judged from `Program.cs` wiring and endpoint maps.

Baseline facts used below: the boundary baseline has 76 member-level entries for string `StudyId`, `SiteId`, `CollectionSiteId` and `MaskedSiteId` inside `apps/execution` (CEC domain 6, CGT application 24, CGT domain 20, Observations application 13, Observations domain 8, Subjects application 3, Toxicity application 2). Those entries may only shrink.

### Non-execution rows (15)

| Row | Public types | Maturity | Suggested owner (confidence) | Decision needed |
| --- | --- | --- | --- | --- |
| `interop/src/Domain/InteropEntities.cs` | 14 classes: EPRO submission, instrument, subject assignment and notification, clinical query, EHR records, device registration, sensor records, `InteropAuditLog` | Mixed: EHR path running; ePRO, instrument, subject, device and sensor persisted but unrouted | Split by behaviour: capture (EHR), participants (ePRO and subject), evidence (device and sensor) (low) | Approve the split. Decide whether string `StudyId`, `SiteId` and `SubjectId` become kernel IDs (49 Interop baseline entries). |
| `interop/src/Infrastructure/Persistence/InteropDbContext.cs` | `InteropDbContext`, 19 DbSets including SCE tables | Running: `AddCadencePostgresDbContext`, readiness check | platform-shell (medium) | One context per module, or a shared kernel context with module schemas (ADR-2447) |
| `interop/src/Infrastructure/Persistence/InteropDbContextFactory.cs` | Design-time factory | Design-time only | platform-shell (high) | Moves with the context decision |
| `interop/src/Infrastructure/Security/DeviceTokenHasher.cs` | `DeviceTokenHasher` (keyed HMAC) | Scaffold: registered, no route calls it | evidence (medium) | Whether device registration moves to Evidence or Trust, given the Allocate overlap |
| `interop/src/Infrastructure/Security/SmartSigningKeys.cs` | `ISmartSigningKeys`, `ConfigurationSmartSigningKeys` | Running: used by eSource SMART routes | platform-shell (medium) | Whether key configuration stays in `packages/security` while the eSource flow stays with its owner |
| `interop/src/Program.cs` | Composition root | Running (eSource 14 routes, SCE 12 routes) | platform-shell (medium) | Whether composition moves into a kernel host with module registration |
| `knowledge/src/Domain/KnowledgeEntities.cs` | 11 classes: articles, help mappings, protocol chunks, training curriculum and assignment, `DelegationRecord`, `OrgAuditLog`, `TrainingCertificate` | Mixed: training routes running; article, help and chunk types unrouted | Training: trial-operations (medium). Articles and help: unclear (low) | Whether `DelegationRecord`, `OrgAuditLog` and training retire for kernel Qualification, Delegation and AuditEntry (spec §3 forbids app-local tables) |
| `knowledge/src/Endpoints/TrainingEndpoints.cs` | `TrainingEndpoints` and 10 DTOs; 7 routes under `/api/v1/training` | Running | trial-operations (medium) | Whether training administration moves to trial-operations while kernel Qualification gates batch-assign and sign-off |
| `knowledge/src/Infrastructure/Persistence/KnowledgeDbContext.cs` | 12 DbSets including kernel `AuditLogEntry` | Running | platform-shell (medium) | Same as the interop context decision (ADR-2447) |
| `knowledge/src/Infrastructure/Persistence/KnowledgeDbContextFactory.cs` | Design-time factory | Design-time only | platform-shell (high) | Moves with the context decision |
| `knowledge/src/Program.cs` | Composition root; durable e-signature token store | Running | platform-shell (medium) | Which host owns the single-use signature-token store after Knowledge merges |
| `org/src/Domain/OrgEntities.cs` | `PersonnelAssignment`, `DelegationOfAuthority`, `TrainingLog`, `OrgAuditLog` | Scaffold: persisted, no routes, almost no tests | kernel (medium) | Retire for kernel Actor, Delegation, Qualification and AuditEntry, or keep as admin records |
| `org/src/Infrastructure/Persistence/OrgDbContext.cs` | 5 DbSets including kernel `AuditLogEntry` | Running as a host only | kernel (medium) | Retire with its tables, or merge into kernel persistence |
| `org/src/Infrastructure/Persistence/OrgDbContextFactory.cs` | Design-time factory | Design-time only | kernel (medium) | Follows the context decision |
| `org/src/Program.cs` | Health and `/api/v1/org/status` only | Scaffold | platform-shell (medium) | Merge or retire org under #4787, rather than keep it as a host |
| `fileshare/src/Workers/*` (7 files) and `fileshare/src/Domain/FileshareEntities.cs` | Workers and `FileRecord`, `ShareGrant`, `GuestLink`, audit types | Workers run, but no HTTP route creates `FileRecord`s, so the app is in effect a scaffold | Workers and conversion: platform-shell (medium). `ShareGrant` and `GuestLink`: trial-operations (medium) | Generic storage versus TMF grants. `FileAuditLog` and `PlaybackAuditLog` conflict with the audit naming rule. |

Non-execution cross-row findings: none of interop, knowledge, fileshare or org references another `Cadence.Apps.*` context, publishes or consumes events, or makes outbound HTTP calls. Each app's README is partly stale (interop calls itself a scaffold but is running; knowledge says no endpoints but has training routes; fileshare says no storage code but uses `Cadence.Storage`).

### Execution rows (92)

The rows are grouped by area. Maturity is "running" unless noted. The owner shown is the suggestion with confidence in brackets.

**Subjects and consent** (rows under `execution/src/Domain/Subjects`, `Application/Subjects`, `Domain/Ports`, `Endpoints/SubjectEndpoints.cs`, `Infrastructure/Persistence/Repositories/SubjectRepository.cs` and `ConsentRepository.cs`)

- `ClinicalSubject`: aggregate root with table `clinical_subjects`. Re-base on `ParticipantId`, and decide whether the name is allowed under spec §2.3 (participants, high).
- `SubjectId`: GUID struct with `AsParticipantId()`. Keep as a surrogate that converts, or use `ParticipantId` directly (participants, high).
- `SubjectCode`: string-wrapped site-local identifier, with 42 files referencing it. Decide whether it is a view of `ParticipantId` (participants, medium). Spec §2.3 forbids string participant IDs.
- `StudyCode` and `SiteCode`: string-wrapped, with 42 and 27 referencing files. Replace with kernel `StudyId` and `SiteId` (kernel, high). Baseline string entries apply.
- `ProtocolVersionRef`: app-local protocol copy. Replace with the kernel `ProtocolVersion` reference (kernel, high). Conflicts with "no application-local protocol copies".
- `SubjectConsent` and `SubjectConsentId`: table `subject_consents`, route `POST …/consent` and an undocumented `…/consents` duplicate. `apps/econsent` also declares `SubjectConsent` with its own DbSet. Choose one owner and align with kernel `ConsentStatus` (participants, medium). Table overlap is not verified.
- `SubjectState`, `SubjectStateCodec`, `SubjectTransitionGraph`: lifecycle states and transitions. Decide whether they belong to kernel Enrollment (participants, medium).
- `SubjectErrors`, `DomainErrorExtensions`, `DomainErrorHttpMapping`: error factories and HTTP mapping. Mapping is platform-shell (medium). Factories are participants (high).
- `SubjectListCriteria`, `SubjectListResponse`, `SubjectResponse`, `SubjectDetailResponse`, `SubjectCreateRequest`, `SubjectDemographicsUpdateRequest`, `SubjectStateUpdateRequest`, `SubjectConsentRequest`, `SubjectConsentResponse`: DTOs, not persisted. Read models move to kernel participant reads (participants, medium).
- `SubjectAuthority`: role checks. Replace with Trust grants and qualifications, per #4781 (participants, medium).
- `ISubjectApplicationService`, `SubjectApplicationService`: replace with the kernel Participant, Enrollment and Consent command API (participants, high for the service, medium for timing).
- `ISubjectRepository`, `IConsentRepository`, `SubjectRepository`, `ConsentRepository`: ports and EF repositories over `ExecutionDbContext`. Owner follows the subject and consent decisions (participants, high and medium).
- `AdvisoryLockSql`: Postgres advisory-lock helper, used by subject and randomization repositories. Keep it in `packages/database`, or keep subject lock keys local (platform-shell, medium).
- `SubjectEndpoints.cs`: 7 routes under `/api/v1/execution/subjects`. Decide whether to retire the duplicate `/consents` route (participants, high).
- `DemographicsEncryption`, `IDemographicsEncryptionService` and related: see the security section below.

**Randomization, supply and devices** (`Domain/Randomization`, `Application/Randomization`, `Domain/Supply`, `Application/Supply`, `Domain/Devices`, `Application/Devices`, `Endpoints/RandomizationEndpoints.cs`, `IpKitEndpoints.cs`, `DeviceEndpoints.cs`, and the matching persistence)

These rows were mapped as proposed in the first pass, so they are not among the 92 needs-decision rows and the second pass did not review them row by row. Their proposed owner is interventions. They depend on the shared DbContext split (ADR-2447) and on the `Supply` baseline entries. A row-level review is still open.

**CGT (cell and gene therapy)** (`Domain/Cgt`, `Application/Cgt`, `Endpoints/CgtEndpoints.cs`)

- `CgtBedsideVerification`, `CgtChainOfCustody`, `CgtDestructionCertificate`, `CgtMilestoneEvent`, `CgtVialReturnReconciliation`: Guid-keyed aggregates in `cgt_*` tables. Running. Owner interventions (medium). Decide whether custody is an interventions aggregate or kernel provenance. Some carry string `StudyId` or `SiteId` (baseline).
- `CgtExceptions`: five exception types. Owner interventions (high).
- `CgtApplicationService`: 11 routes. Uses `ExecutionDbContext` directly, a layer breach against pattern 9. Owner interventions (medium). Decide whether to accept the breach until extraction.
- `CgtDtos`: 15 records. Replace string `StudyId` and `SiteId` (24 baseline entries) with kernel IDs. Owner interventions (medium).
- `ICgtApplicationService`: interface. Owner interventions (medium).
- `CgtEndpoints.cs`: no endpoint test (`CgtApplicationServiceTests` and `CgtEngineTests` exist). Owner interventions (low). The owner list has no supply owner, so this needs an ADR decision.

**CEC (clinical events committee)** (`Domain/Cec`, `Application/Cec`, `Domain/Ports/ICecRepository.cs`, `Endpoints/CecEndpoints.cs`, `Repositories/CecRepository.cs`)

- `CecDossier` and `CecChairAdjudication`: `AggregateRoot<string>` in `cec_dossiers` and `cec_chair_adjudications`, with `SignatureManifest` stored as a JSON column. Owner evidence or safety (medium). Decide whether signatures bind to kernel Attestation, and whether the key becomes a kernel ID.
- `SignatureManifest`: JSON-converted record. Owner kernel (medium). The inventory's `Domain/Forms/SignatureManifest.cs` row is stale, because Forms was retired. Point it at `Domain/Cec/SignatureManifest.cs`.
- `CecApplicationService`, `ICecApplicationService`, `ChairAdjudicateRequest`, `ChairAdjudicationResponse`: running; owner evidence (medium). Same signature decision.
- `ICecRepository` and `CecRepository`: owner safety (medium). The owner question is shared with the CEC decision.

**Lab reference ranges and toxicity** (`Domain/Observations`, `Application/Observations`, `Application/Toxicity`, `Domain/Toxicity`, `Endpoints/LabRangeEndpoints.cs`, `LabToxicityEndpoints.cs`, `Infrastructure/Normalization`)

- `LabReferenceRange`, `BulkLabRangeImportJob`: EF entities in `lab_reference_ranges` and `bulk_lab_range_import_jobs`. Running. Owner evidence (medium). Replace string study and site IDs (baseline).
- `TestCode`: SDTM-shaped code value object. Replace with a kernel `ConceptRef`, because SDTM is representation, not truth (K-TRUTH-1). Owner evidence (medium).
- `DomainCode`: SDTM domain code wire type. No standards owner in the list. Owner capture (medium) per the first-pass inventory.
- `ILabRangeService` and `LabRangeService`: range lookup. Owner evidence (medium). Decide whether the scope-factory singleton stays.
- `ILabRangeImportService`, `LabRangeImportService`, `BulkLabRangeFileParser`, `LabRangeDtos`: import and dry-run routes under `/api/v1/execution/labs/*` and lab-ranges CRUD. Owner evidence (medium). `LabRangeImportService` imports `Infrastructure.Persistence` directly, a layer breach. Decide whether capture or evidence owns bulk ingestion.
- `IUcumNormalizationService` and `UcumNormalizationService`: unit conversion. Platform utility or kernel terminology. Owner platform-shell (medium); kernel `IUcumNormalizer` port per K-FACT-2 (kernel, high for the port).
- `ILabToxicityGovernanceService` and `LabToxicityGovernanceService`: review queue and grading. Owner safety (medium). Publishes `Grade3ToxicityDetected` through the outbox, but no in-repo consumer exists. Confirm the consumer.
- `StagedAdverseEventReview`: `AggregateRoot<string>` with `staged_adverse_event_reviews`. Owner safety (medium). Replace the string key and the `StudyCode`/`SiteCode` fields.
- `IStagedAdverseEventReviewRepository` and `StagedAdverseEventReviewRepository`: no direct test. Add tests before any move (safety, medium).
- `ToxicityDtos`: owner safety (medium). `StagedAdverseEventReviewResponse` has string IDs (baseline).
- `LabToxicityEndpoints.cs`: owner safety (medium). Confirm grading is safety-owned.
- Shared engine: `packages/engine_core` already has `LabToxicityGradingEngine` and `CgtBarcodeEngine`. Decide whether the engines move with their owner.

**Security, audit and shared abstractions** (`Application/Abstractions`, `Infrastructure/Security`, `Infrastructure/Persistence/EfAuditLogWriter.cs`, `ConcurrencyTokenIncrementInterceptor.cs`, `ExecutionRepositoryBase.cs`)

- `IAuditLogWriter` and `EfAuditLogWriter`: a second audit seam writing `audit_log_entries`. Owner kernel (high). Replace with the kernel command-pipeline audit, and retire the second chain (decision D6).
- `ICurrentAuditContextAccessor`, `AmbientAuditContextAccessor`, `IAuditActorScope`: move actor and reason-for-change into kernel Actor (spec §6). Owner kernel (high for the interface, low for the accessor's HttpContext coupling).
- `AuthenticatedCaller`: verified caller identity used by CEC, toxicity, subject authority and synonym endpoints. Keep as platform identity, or fold into kernel Actor (platform-shell, medium).
- `ServiceAccount` and `ServiceAccounts`: consumer writes attributed to `svc:` accounts. Adopt kernel `ServiceAccount` Actor, or keep per-app accounts (kernel, high).
- `ConcurrencyConflictException`: 409 signal. Keep, or promote to a shared SDK error type (platform-shell, medium).
- `IAllocationProtector` and `AesGcmAllocationProtector`: allocation concealment. Platform security port, or kernel IdentityVault (K006). Key required with no default (verified). Owner platform-shell (medium).
- `IDemographicsEncryptionService`, `AesGcmDemographicsEncryptionService`, `DemographicsEncryptionOptions`, `DemographicsDecryptionFailedException`: PII encryption. Owner platform-shell (medium); kernel IdentityVault timing (K006). Startup refuses an invalid key (verified).
- `IDemographicsNormalizationService` and `DemographicsNormalizationService`: `GetSafeDemographics` has no caller, so the code is DI-registered but dormant. Retire, or re-home as kernel identity-reveal observations. Owner participants (medium).
- `ConcurrencyTokenIncrementInterceptor`: acts on all tracked entities. Move to `packages/database` only after the per-owner table inventory and an ADR-2600 review (platform-shell, medium).
- `ExecutionRepositoryBase`: base for about eight repositories. Move to `packages/database`, or keep it execution-local (platform-shell, medium).

**Persistence and composition** (`Infrastructure/Persistence/ExecutionDbContext.cs`, `ExecutionDbContextFactory.cs`, `ExecutionDbSeeder.cs`, `Program.cs`)

- `ExecutionDbContext`: about 30 DbSets across subjects, CEC, CGT, devices, IP kits, randomization, coding, observations, toxicity, audit and the outbox. Platform-shell (medium). This is the central split decision. The outbox and audit tables need owners.
- `ExecutionDbContextFactory`: design-time only. Follows the context decision.
- `ExecutionDbSeeder`: runs only with `--seed`. No test. Platform-shell (low). Seed ownership per owner.

**Endpoints and filters** (`Endpoints/Filters`, `OperationEnvelopeResponse.cs`, `ConcurrencyConflictExceptionHandler.cs`, `OpenApi`, `Serialization`)

- `ChangeReasonHeaderFilter` and `ValidationFilter`: cross-cutting. The reason-for-change filter is a kernel command concern (needs decision, kernel). Validation is platform-shell.
- Serialization and OpenAPI transformer: platform-shell (medium).

**Cross-row findings (execution)**

- **Forbidden names (K-XS-1):** no declared type in these 92 rows is exactly one of the forbidden simple names (Study, Site, Participant, Subject, Observation, Organization, Person, Role, Delegation, Specimen, Estimand, or `*AuditLog`). K-XS-1 passes on names alone.
- **Near misses under spec §2.3:** `ClinicalSubject`, `SubjectId`, `SubjectCode`, `SubjectConsent`, `SubjectState` and the other `Subject*` types form a parallel participant model. The name-based check does not catch them. The ADR should decide whether to extend the check or accept them as participant views.
- **String-wrapped IDs:** `StudyCode`, `SiteCode`, `SubjectCode`, the `AggregateRoot<string>` CEC and staged-review types. Spec §2.3 forbids string study, site and participant IDs.
- **Layer breaches:** `CgtApplicationService` and `LabRangeImportService` import `Infrastructure.Persistence`. This conflicts with pattern 9.
- **Single shared context:** all execution tables live in one `ExecutionDbContext`. Splitting it also splits migrations.
- **Dependencies:** no `Cadence.Apps.*` import from another app. The only kernel reference is `Cadence.Kernel.Time` (IClock) and `ParticipantId`. `Cadence.Core.Domain` resolves to `packages/engine_core`, which is shared utility rather than kernel. The one cross-context link is `EConsentCompletedConsumer`, which writes subjects and consents from an eConsent event.
- **Shared abstractions with several consumers:** `AuthenticatedCaller`, `IAuditLogWriter` and `ConcurrencyConflictException`. Their owner decisions should be taken together.
- **Test gaps before any move:** no direct tests for `CgtEndpoints`, `LabRangeEndpoints`, `StagedAdverseEventReviewRepository` or `ExecutionDbSeeder`. No direct tests by type name for the CGT aggregates, `BulkLabRangeFileParser`, `BulkLabRangeImportJob` or `DomainCode`.
- **Inventory gaps:** `docs/kernel/EXECUTION_MIGRATION_INVENTORY.md` has no rows for CEC, CGT, toxicity, bulk lab-range import, `IAllocationProtector`, `AuthenticatedCaller` or `ConcurrencyConflictException`. It should be replaced by this file, as agreed with the task 3 thread.
- **Positive checks:** no default secrets found. Both key options refuse startup without a valid key.

### Verification status

The row-level facts above come from two read-only Haiku review passes over the rows in the first-pass mapping. They were not re-verified line by line in this revision. The baseline counts and the csproj edges were re-counted directly from the repository in this pass. Treat the row facts as provisional until the ADR owner signs off each row. Test counts are file-based.

### Package-level answers for the ADR thread

- **PackageReference closure of `Cadence.Kernel.Modules`:** direct references are FluentValidation, Microsoft.AspNetCore.OpenApi, Microsoft.EntityFrameworkCore.Relational and Npgsql.EntityFrameworkCore.PostgreSQL. Transitively, `Cadence.Core.Domain` adds FluentValidation, and `Cadence.Security` adds Microsoft.EntityFrameworkCore, System.IdentityModel.Tokens.Jwt and Microsoft.IdentityModel.Tokens. `Cadence.Kernel.Abstractions` has no package references.
- **The 16 `Cadence.Core.Domain` baseline entries** are all `StudyId`/`SiteId` members on `Randomization`, `Supply` and `Rules` types. No file under `Kernel.Modules`, `Abstractions`, `Application` or `Domain` references those type names. The only consumers are `packages/engine_core` itself and two execution files. Excluding those namespaces from the SDK path clears the entries without renaming them.

### Open work after this pass

- Each row's owner decision belongs to the ADR thread (`cmsg_01Q5KbvG319KViDdXRuTJgSAMhnMw2gQUVPzZ1CNdpoRUt`).
- Per-file maturity, API and event contracts, and test-method counts (not file counts).
- The persistence split (ADR-2447) and the outbox and audit table owners.
- Licence provenance for `packages/kernel` and the Python reference packages.
