# Execution → Clinical Kernel Migration Inventory

| | |
| --- | --- |
| **Status** | Draft |
| **Date** | 2026-09-29 |
| **Spec version** | Cadence Clinical Kernel Specification v0.1 (`docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md`) |
| **Base commit** | `dd6686e` |
| **Scope** | Every `.cs` file under `apps/execution/src` (excluding `obj/`; EF `Migrations/` listed as one row) |
| **Related ADRs** | ADR-2244 (modular monolith), ADR-2245 (ProtocolVersion snapshots), ADR-2246 (bitemporal `tstzrange`), ADR-2247 (single DB + sentinels) |
| **Resolution** | Spec v0.2 answers §3 in its Appendix A and §4 in its Appendix B. Where they differ, the spec wins. |

Dispositions: **KEEP** (unchanged in the EDC module) · **MIGRATE→KERNEL** (the logic moves into `packages/kernel`) ·
**REMAIN EDC-SPECIFIC** (stays as EDC workflow/projection but is refactored to use kernel types) · **REPLACE**
(superseded by a kernel concept) · **DELETE** (dead code, duplicate or shim).

"Kernel: exists" means a type is already present in `packages/kernel/src`. "Kernel: missing" means it has to be built.
Today the kernel ships only IDs (`TenantId`, `OrganizationId`, `PersonId`, `StudyId`, `SiteId`, `StudySiteId`,
`ParticipantId`, `ObservationId`, `AttestationId`, `ProtocolVersionId`), thin `Tenant/Organization/Person/Study/Site/
StudySite/Participant/Observation/Attestation/ProtocolVersion` entities, `TemporalPeriod` and `AsOf()`. It has no
Enrollment, Effectivity, Applicability, Consent, Assertion, Source, Artifact, Provenance, Grant/Capability,
command pipeline, AuditEntry or outbox yet. The only kernel type that `apps/execution` references today is
`ParticipantId`, and only through the dead shim `SubjectId.AsParticipantId()`.

---

## 1. Summary

Counts are per declared type, with one row per type. The Migrations folder counts as one row.

| Disposition | Types | % |
| --- | ---: | ---: |
| KEEP | 15 | 10% |
| MIGRATE→KERNEL | 30 | 20% |
| REMAIN EDC-SPECIFIC | 43 | 28% |
| REPLACE | 61 | 40% |
| DELETE | 3 | 2% |
| **Total** | **152** | |

What the table shows: the substantial behaviour to carry forward is the subject state machine, the
enrollment-index allocation, the UCUM/range derivations, the audit hash-chain writer and the identity encryption.
All of it moves into the kernel. Forms, queries and edit checks remain in EDC and must be re-keyed. Almost
everything else is a code-keyed (`StudyCode`/`SubjectCode` string) duplicate of kernel truth, and kernel IDs
replace it.

### Pre-existing defects to fix before (or during) slice 1

These defects exist today, independent of the kernel. They affect what can be migrated safely.

| # | Defect | Evidence |
| --- | --- | --- |
| D1 | **Lost updates.** `GetByIdAsync` returns an `AsNoTracking()` entity. The services mutate it and call `SaveChangesAsync`, which persists nothing. SDV verify/flag, form update/complete/approve/unlock and every query transition are silently dropped. No `Attach`/`Update` exists anywhere in `apps/`. | `Infrastructure/Persistence/Repositories/ObservationRepository.cs:27`, `FormSubmissionRepository.cs:28`, `ClinicalQueryRepository.cs:37` |
| D2 | **Fail-open authorization.** The query close/reopen/cancel role check returns `true` when the role is empty. The role comes from the spoofable `X-User-Role` request header. | `Application/Queries/ClinicalQueryApplicationService.cs:360-364`, `Endpoints/QueryEndpoints.cs:172` |
| D3 | **Self-asserted signer.** The PI-approval role check reads `manifest.SignerRole` from the request body, and `SignerId` is also taken from the client. | `Application/Forms/FormSubmissionApplicationService.cs:253,281` |
| D4 | **Unaudited mutations.** `DeleteDemographicsAsync` saves without an explicit audit entry, and the eTMF webhook deliberately writes no audit. **Fixed in #4818:** they write `DELETE_DEMOGRAPHICS` and `ETMF_COMPLIANCE_UPDATE`. | `Application/Subjects/SubjectApplicationService.cs:291-308`, `Application/Compliance/SiteComplianceApplicationService.cs:74` |
| D5 | **Anonymous attribution.** The audit user falls back to `"system"` when there is no `sub` claim. **Fixed in #4818:** no actor throws; background work runs as a named `svc:` service account (ADR-2257). | `Infrastructure/Security/HttpCurrentAuditContextAccessor.cs:19,23` |
| D6 | **Two audit chains per entity.** `EfAuditLogWriter` keys by table name (`clinical_subjects`). `GxPAuditInterceptor` keys by CLR name (`ClinicalSubject`). Both write to the same `AuditLogEntry` table. **Fixed for #4768:** the interceptor is the only writer, one entry per change on one chain per CLR type name, labelled with the semantic action; `AuditLogChainVerifier` verifies the chains (ADR-2446). | `Infrastructure/Persistence/EfAuditLogWriter.cs:53`, `Program.cs:144`, `packages/database/src/GxPAuditInterceptor.cs` |
| D7 | **Fabricated genesis hash** in the seeder breaks chain verification. | `Infrastructure/Persistence/ExecutionDbSeeder.cs:76-77` |
| D8 | **Wrong response.** `SubjectResponse.Underpopulated` always reports `Status: "SCREENING"`, even after a transition. **Fixed in #4804:** every write returns `FromAggregate`. | `Application/Subjects/SubjectResponse.cs:61` |
| D9 | **Last-writer-wins retry.** On `DbUpdateConcurrencyException` the retry pipeline overwrites the original values with database values and replays, which silently defeats optimistic concurrency. **Fixed for #4766:** conflicts are rejected with 409 `CONCURRENCY_CONFLICT`; only transient failures are retried (ADR-2450). | `Infrastructure/Persistence/Repositories/SaveChangesResiliencePipelineFactory.cs:74` |
| D10 | **Valid time collapsed into recorded time.** Observation date defaults to "now", and eConsent `icfSignedDate` is the time of consumption. Historical valid time is unrecoverable for those rows. **Fixed for #4769:** observations require `observation_date` (API and form payloads), and the consumer takes the event's `SignedAt` or rejects the message to the error queue. **Fixed for #4824:** the consent API rejects a signed consent with no `icf_signed_date` and no date already on file (400 `ICF_SIGNED_DATE_REQUIRED`); `SubjectConsent` never defaults the date. | `Domain/Observations/ClinicalObservation.cs:144`, `Application/Forms/FormSubmissionApplicationService.cs:365,410`, `Infrastructure/Consumers/EConsentCompletedConsumer.cs:130`, `Application/Subjects/SubjectApplicationService.cs` (`RecordConsentAsync`) |

---

## 2. Inventory

All paths are relative to `apps/execution/src/` unless they start with `packages/` or `tests/`.

### 2.1 `Application/Abstractions`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Abstractions/IAuditLogWriter.cs` | `IAuditLogWriter` | REPLACE | Kernel command pipeline writes AuditEntry + ProvenanceActivity + outbox in one tx (§8.1, K-TX-2, K-PROV-1). Kernel: missing. | Explicit per-call-site audit writes are optional and were forgotten in places (D4). The pipeline makes audit non-optional. |
| `Application/Abstractions/IClock.cs` | `IClock` | MIGRATE→KERNEL | `Cadence.Kernel.Abstractions` clock port, or BCL `TimeProvider` (K-TIME-3). Kernel: exists (`Cadence.Kernel.Time.IClock`, #4784; execution now uses it). | Domain types import it from Application (`Domain/Subjects/ClinicalSubject.cs:4`, `SubjectConsent.cs:3`), which is a layer inversion. |
| `Application/Abstractions/ICurrentAuditContextAccessor.cs` | `ICurrentAuditContextAccessor` | REPLACE | Kernel `Actor` (§6) and a reason-for-change field on each command (§8.1, §8.3.4). Kernel: missing. | Ambient string user id and reason. D5. |
| `Application/Abstractions/IDemographicsEncryptionService.cs` | `IDemographicsEncryptionService` | MIGRATE→KERNEL | IdentityVault port (§5.1, K006). Kernel: missing. | Subsumes `ClinicalSubject.EncryptedDemographics`. |
| `Application/Abstractions/IDemographicsNormalizationService.cs` | `IDemographicsNormalizationService` | REPLACE | Sex and birth date become kernel Observations revealed via the `identity.reveal` capability. Age becomes a derived Observation (K-PROV-2). | No production consumer: only DI (`Program.cs:114`) and tests. `ObservationApplicationService.cs:115` passes `sex: null, ageYears: null` to the range evaluator. |
| ″ | `SafeDemographics` | REPLACE | As above. | Only produced by the service above. |
| `Application/Abstractions/ILabRangeService.cs` | `ILabRangeService` | MIGRATE→KERNEL | Kernel derivation port (reference-range evaluator) that emits a derived Observation/Assertion with a versioned algorithm (K-FACT-2, K-PROV-2, §10 hot-path port). Kernel: missing. | Must be re-keyed from `TestCode` (SDTM) to `ConceptRef` (K-PROJ-1, K005). |
| ″ | `LabAssessmentResult` | MIGRATE→KERNEL | Becomes the derived-fact payload of the port above. | |
| `Application/Abstractions/IOutlierDetectionService.cs` | `IOutlierDetectionService` | DELETE | Recreate later in a central-monitoring module if needed. It is not kernel truth. | No production consumer: only `Program.cs:117` DI. `RecalculateOutliers` is referenced only by its own implementation. |
| `Application/Abstractions/IUcumNormalizationService.cs` | `IUcumNormalizationService` | MIGRATE→KERNEL | Kernel `IUcumNormalizer` port (§10, K-FACT-2). Kernel: missing. | Output must become a derived Observation, not columns (K-FACT-2). |

### 2.2 `Application/Compliance`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Compliance/ComplianceStatusResponse.cs` | `ComplianceStatusResponse` | REPLACE | Kernel StudySite lifecycle + Effectivity status query (§4.3). | Carries a `StudyCode`-typed member named `StudyId`. |
| `Application/Compliance/EtmfCompletenessWebhookPayload.cs` | `EtmfCompletenessWebhookPayload` | REPLACE | eTMF module emits `ArtifactClassified`/`ArtifactVersioned` (§5.5, §8.2). Evidence then feeds Effectivity (K-TIME-5) and K-AUTH-5 conditions. | **K-XS-1**: public `string StudyId`, `string? SiteId`. |
| ″ | `EtmfCompletenessWebhookPayloadValidator` | REPLACE | Replaced together with the payload. | |
| `Application/Compliance/ISiteComplianceApplicationService.cs` | `ISiteComplianceApplicationService` | REPLACE | Kernel StudySite commands. | |
| `Application/Compliance/SiteActivationRequest.cs` | `SiteActivationRequest` | REPLACE | Kernel `ActivateStudySite(StudySiteId)` command guarded by K-TIME-4. Kernel: `StudySite.Activate()` exists but is unguarded. | **K-XS-1**: `string StudyId`. |
| `Application/Compliance/SiteComplianceApplicationService.cs` | `SiteComplianceApplicationService` | REPLACE | Kernel StudySite activation (K-TIME-4/5) and eTMF evidence Assertions. | `ActivateSiteAsync` changes no state and only writes audit (lines 80-105). The webhook is unaudited (D4). **K-MOD-1**: caches eTMF truth in EDC. |

### 2.3 `Application/EditChecks`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/EditChecks/BatchEvaluationContext.cs` | `BatchEvaluationContext` | REMAIN EDC-SPECIFIC | Build from kernel Observations (via Kernel.Abstractions queries) keyed by `ParticipantId`/`ClinicalEventId`, not `VisitId` strings. | **K-XS-1**: `string? StudyId`. No production caller (tests only). |
| `Application/EditChecks/IBatchEvaluationContextLoader.cs` | `IBatchEvaluationContextLoader` | REMAIN EDC-SPECIFIC | Re-key to `ParticipantId`. Wire it into form completion, which it is not today. | Only a DI registration (`Program.cs:118`). `FormSubmission.Complete()` is called without blocking errors (`FormSubmissionApplicationService.cs:208`). |
| `Application/EditChecks/IEditCheckEngine.cs` | `IEditCheckEngine` | REMAIN EDC-SPECIFIC | §10 hot-path port. Retype `ClinicalObservation` to a kernel Observation view. Violations become candidate queries / `Queried` Assertions. | |
| ″ | `EditCheckViolation` | REMAIN EDC-SPECIFIC | Replace `FormId`/`VisitId` strings with EDC/kernel IDs. | |

### 2.4 `Application/Forms`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Forms/FormSubmissionApplicationService.cs` | `FormSubmissionApplicationService` | REMAIN EDC-SPECIFIC | Keep the form workflow. Payload→observation extraction issues kernel `RecordObservation` commands with `Source=ManualEntry(actor, form)` as a saga (K-TX-1). Approval calls kernel `Authorize(form.approve)` and creates an `Attestation(Approved)` bound to the payload hash (§7.5). | **K-TX-1**: form + N observations in one tx (lines 127, 187). **K-AUTH-1**: string role list (line 281, D3). **K-FACT-2**: stores normalised values. `protocolVersionIndex: 1` hard-coded (369, 414), **K-TIME-8**. Swallowed all exceptions, so numeric key-value fields never produced observations; fixed for #4769, which also requires `observation_date`. D1, D10 (fixed). |
| `Application/Forms/FormSubmissionApproveRequest.cs` | `FormSubmissionApproveRequest` | REMAIN EDC-SPECIFIC | Drop client-supplied `SignerId`/`SignerRole`/`SignedAt`. The signer is the authenticated Actor plus a step-up fact (§6, §7.5). | D3. |
| `Application/Forms/FormSubmissionCreateRequest.cs` | `FormSubmissionCreateRequest` | REMAIN EDC-SPECIFIC | `ParticipantId` replaces `StudyId`/`SubjectId`/`SiteId` codes (StudySite is derived). Drop `ProtocolVersion` (K-TIME-8). `VisitId` becomes a `ClinicalEventId`. | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| `Application/Forms/FormSubmissionResponse.cs` | `FormSubmissionResponse` | REMAIN EDC-SPECIFIC | Same re-keying. The signature becomes an `AttestationId`. | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| `Application/Forms/IFormSubmissionApplicationService.cs` | `IFormSubmissionApplicationService` | REMAIN EDC-SPECIFIC | Signature re-keying only. | |

### 2.5 `Application/Observations`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Observations/IObservationApplicationService.cs` | `IObservationApplicationService` | REPLACE | Kernel Observation commands (`RecordObservation`, `CorrectObservation`) on `Cadence.Kernel.Api`, plus EDC SDV commands that emit `Verified(SDV)` Assertions (§5.4). | |
| `Application/Observations/ObservationApplicationService.cs` | `ObservationApplicationService` | MIGRATE→KERNEL | The record path (value validation, UCUM derivation, range derivation, inline fatal check) moves to a kernel `RecordObservation` handler that writes provenance (K-PROV-1). SDV moves to EDC Assertion commands. Kernel: `Observation` exists but has no ConceptRef/ObservationValue/Source/Status/Event. | **K-TIME-8**: `ActiveProtocolVersion` fallback (line 145). **K-FACT-2**: range results written onto the row. Resolving a subject by code bypasses StudySite scoping. D1 (SDV). |
| `Application/Observations/ObservationCreateRequest.cs` | `ObservationCreateRequest` | REPLACE | Kernel `RecordObservation(ParticipantId, ClinicalEventId?, ConceptRef, ObservationValue, Valid, SourceRef)`. `PageId`/`VisitId` go to an EDC projection (K-FACT-4). | **K-XS-1**: `string? StudyId`, `SiteId`, `LabSiteId`. |
| `Application/Observations/ObservationResponse.cs` | `ObservationResponse` | REPLACE | Kernel Observation read model plus an EDC projection, where SDV/range flags come from Assertions and derived Observations. | **K-XS-1**: `string StudyId`, `SiteId`, `LabSiteId`. |

### 2.6 `Application` (root) and `Application/Queries`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/OperationEnvelopeResponse.cs` | `OperationEnvelopeResponse` | KEEP | Generic wire envelope. | Used by compliance endpoints and tests. It becomes unused if those endpoints are removed. |
| `Application/Queries/ClinicalQueryApplicationService.cs` | `ClinicalQueryApplicationService` | REMAIN EDC-SPECIFIC | Workflow stays (§5.4). Each transition emits a kernel Assertion (`Queried`, `QueryAnswered`, retraction). Authorization moves to the kernel `Authorize(query.close …)`. Target is an `ObservationId`, not coordinates. | **K-AUTH-1** and D2: fail-open `IsAuthorizedCraOrDm` (360-364). Responder/resolver identity taken from the request body. |
| `Application/Queries/ClinicalQueryResponse.cs` | `ClinicalQueryResponse` | REMAIN EDC-SPECIFIC | Re-key to `ParticipantId`/`ObservationId`. | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| `Application/Queries/IClinicalQueryApplicationService.cs` | `IClinicalQueryApplicationService` | REMAIN EDC-SPECIFIC | Remove the `userRole` parameters, because authorization happens in the kernel pipeline. | |
| `Application/Queries/QueryCancelRequest.cs` | `QueryCancelRequest` | KEEP | | Reason only. |
| `Application/Queries/QueryCloseRequest.cs` | `QueryCloseRequest` | REMAIN EDC-SPECIFIC | Remove client-asserted `Resolver`; the resolver is the Actor. | |
| `Application/Queries/QueryCreateRequest.cs` | `QueryCreateRequest` | REMAIN EDC-SPECIFIC | Target becomes `ObservationId` (or `ParticipantId` + `ConceptRef` + `ClinicalEventId`). `Origin="AI"` becomes a kernel Proposal (§7.6). | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| `Application/Queries/QueryReopenRequest.cs` | `QueryReopenRequest` | KEEP | | Reason only. |
| `Application/Queries/QueryRespondRequest.cs` | `QueryRespondRequest` | REMAIN EDC-SPECIFIC | Remove client-asserted `Responder`. | |

### 2.7 `Application/Serialization`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Serialization/ExecutionJsonSerialization.cs` | `ExecutionJsonSerialization` | REMAIN EDC-SPECIFIC | Register kernel converters (`KernelJsonSerializerContext`, `EntityIdJsonConverter<T>`). Drop converters for replaced types. | |
| `Application/Serialization/ValueObjectJsonConverters.cs` | `SubjectIdJsonConverter` | REPLACE | `Cadence.Kernel.ParticipantIdJsonConverter`. Kernel: exists. | |
| ″ | `SubjectConsentIdJsonConverter` | REPLACE | Kernel ConsentId converter. Kernel: missing. | |
| ″ | `SubjectCodeJsonConverter` | MIGRATE→KERNEL | Moves with `SubjectCode` to Kernel.Abstractions. | |
| ″ | `StudyCodeJsonConverter` | MIGRATE→KERNEL | Moves with `StudyCode`. | |
| ″ | `SiteCodeJsonConverter` | MIGRATE→KERNEL | Moves with `SiteCode`. | |
| ″ | `ObservationIdJsonConverter` | REPLACE | `Cadence.Kernel.ObservationIdJsonConverter`. Kernel: exists. | |
| ″ | `DomainCodeJsonConverter` | REMAIN EDC-SPECIFIC | Goes wherever `DomainCode` lands (the SDTM projection side). | |
| ″ | `TestCodeJsonConverter` | REMAIN EDC-SPECIFIC | As above. | |
| ″ | `FormSubmissionIdJsonConverter` | REPLACE | Generic `EntityIdJsonConverter<FormSubmissionId>` once the ID implements `IEntityId<T>`. Kernel: generic exists. | |
| ″ | `FormIdJsonConverter` | KEEP | | |
| ″ | `QueryIdJsonConverter` | REPLACE | Generic `EntityIdJsonConverter<QueryId>`. Kernel: generic exists. | |
| ″ | `FormSubmissionStatusJsonConverter` | KEEP | | |
| ″ | `QueryStatusJsonConverter` | KEEP | | |

### 2.8 `Application/Subjects`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Application/Subjects/ISubjectApplicationService.cs` | `ISubjectApplicationService` | REPLACE | Kernel Participant/Enrollment/Consent command API (`Cadence.Kernel.Application`). | |
| `Application/Subjects/SubjectApplicationService.cs` | `SubjectApplicationService` | MIGRATE→KERNEL | The enrollment-number allocation, SITE_ACTIVATION gate (becomes K-TIME-4 on StudySite), consent recording and re-consent cascade (become ConsentStatus + TransitionPolicy, §4.4) and transitions (become Enrollment) move into kernel handlers. Kernel: `Participant` exists and is thin; Enrollment and Consent are missing. | **K-TX-1**: `RecordConsentAsync` mutates several consent aggregates in one tx (line 164). **K-AUTH-4**: blinding passed as a bool (339). D4, D8. `EvaluateAndTransitionScreeningAsync` has no route. |
| `Application/Subjects/SubjectConsentRequest.cs` | `SubjectConsentRequest` | REPLACE | Kernel `RecordConsent(ParticipantId, consent-form ArtifactId, ProtocolVersionId, valid_from)` plus `Attestation(Consented)`. | |
| ″ | `ProtocolVersionPayload` | REPLACE | Kernel `ProtocolVersionId` (exists). | **K-XS-1**: `string StudyId`. |
| ″ | `SubjectConsentRequestValidator` | REPLACE | Replaced together with the request. | |
| `Application/Subjects/SubjectConsentResponse.cs` | `SubjectConsentResponse` | REPLACE | Kernel ConsentStatus read model. | |
| `Application/Subjects/SubjectCreateRequest.cs` | `SubjectCreateRequest` | REPLACE | Kernel `RegisterParticipant(StudySiteId, SubjectCode)`. `ArmId` goes to RTSM. | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| ″ | `DemographicsPayload` | REPLACE | IdentityVault record command (§5.1). | |
| ″ | `SubjectCreateRequestValidator` | REPLACE | Replaced together with the request. | |
| `Application/Subjects/SubjectDemographicsUpdateRequest.cs` | `SubjectDemographicsUpdateRequest` | REPLACE | IdentityVault update plus RTSM stratification-factor command. | |
| `Application/Subjects/SubjectDetailResponse.cs` | `SubjectDetailResponse` | REPLACE | Kernel participant read. The treatment group comes from RTSM gated by `randomization.read_assignment` (K-AUTH-4). | |
| `Application/Subjects/SubjectResponse.cs` | `SubjectResponse` | REPLACE | Kernel participant/enrollment read model. | Returns `EncryptedDemographics` ciphertext to clients (§10 privacy). D8. |
| `Application/Subjects/SubjectStateUpdateRequest.cs` | `SubjectStateUpdateRequest` | REPLACE | Explicit Enrollment commands (Enroll, Randomize, Complete, Withdraw) instead of a generic "set state". | |
| ″ | `SubjectStateUpdateRequestValidator` | REPLACE | Replaced together with the request. | |

### 2.9 `Domain/Compliance`, `Domain/Forms`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Domain/Compliance/SiteComplianceCache.cs` | `SiteComplianceCache` | REPLACE | StudySite lifecycle (bitemporal, §4.1), Effectivity with evidence Artifacts (K-TIME-5), and K-AUTH-5 conditions. | **K-MOD-1**: duplicates eTMF truth. `AggregateRoot<Guid>` with `Guid.NewGuid()` (line 44), **§3.2**. |
| `Domain/Forms/Errors/FormErrors.cs` | `FormErrors` | REMAIN EDC-SPECIFIC | Remove `UnauthorizedSignerRole`; the kernel `Deny(reason)` replaces it. | `TrialLocked` and `NotApproved` are unused outside tests and definitions. |
| `Domain/Forms/FormId.cs` | `FormId` | KEEP | Later it may reference a `PlannedActivity` (§5.2). | |
| `Domain/Forms/FormSubmission.cs` | `FormSubmission` | REMAIN EDC-SPECIFIC | Re-key to `ParticipantId`/`ClinicalEventId`. `SignatureManifest` becomes an `AttestationId`. Unlock records an Attestation retraction instead of nulling the signature. Drop `ProtocolVersion` (K-TIME-8). | **§7.5 / K-TIME-1**: `Unlock` erases the signature (lines 179-181). `Reopen` and `Deactivate` have no callers. |
| `Domain/Forms/FormSubmissionId.cs` | `FormSubmissionId` | REMAIN EDC-SPECIFIC | Implement `IEntityId<T>` with UUIDv7. | `Guid.NewGuid()` (line 26), **§3.2**. |
| `Domain/Forms/FormSubmissionStatus.cs` | `FormSubmissionStatus` | KEEP | | |
| `Domain/Forms/FormSubmissionStatusCodec.cs` | `FormSubmissionStatusCodec` | KEEP | | |
| `Domain/Forms/SignatureManifest.cs` | `SignatureManifest` | REPLACE | Kernel `Attestation` (§7.5). Kernel: exists, but has no target(kind,id,version,content_hash), method or authenticated_at. | String `SignerId`, self-asserted `SignerRole`, client `SignedAt`. |

### 2.10 `Domain/Observations`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Domain/Observations/ClinicalObservation.cs` | `ClinicalObservation` | MIGRATE→KERNEL | Split per §5.3. Core goes to kernel `Observation` (exists, thin). SDV/flag become Assertions. Normalised/outlier/range values become derived Observations. `PageId`/`VisitId` go to an EDC projection. | **K-FACT-1/K-TIME-1**: `VerifySdv`/`FlagSdv` mutate in place (173). **K-FACT-2**: `NormalizedValue`/`IsOutlier`/`ReferenceRange*`. **K-FACT-4**: `PageId`, `VisitId`, `IsSdvVerified`. **K-TIME-8**: `ProtocolVersionTag/Index`. **K-PROJ-1**: `DomainCode`/`TestCode`. **K-TIME-3**: `DateTime.UtcNow` (144). **K-XS-1**: `string? LabSiteId`. |
| `Domain/Observations/DomainCode.cs` | `DomainCode` | REMAIN EDC-SPECIFIC | SDTM domain belongs to the projection mapping, not the kernel (K-PROJ-1). | |
| `Domain/Observations/Errors/ObservationErrors.cs` | `ObservationErrors` | MIGRATE→KERNEL | Kernel observation error codes. `AlreadyVerified` goes to the EDC SDV assertion. | `ObservationLocked` and `InvalidCoordinates` are unused. |
| `Domain/Observations/ObservationAdditionalProperties.cs` | `ObservationAdditionalProperties` | REPLACE | Lab fields become `Source=LabFeed(vendor,batch)` (§5.5) plus derived range Observations. `CustomProperties` has no kernel home (spec gap G8). | **K-XS-1**: `string? LabSiteId`. |
| `Domain/Observations/ObservationId.cs` | `ObservationId` | REPLACE | `Cadence.Kernel.ObservationId`. Kernel: exists. | Duplicate of a kernel type name. `Guid.NewGuid()` (26). |
| `Domain/Observations/TestCode.cs` | `TestCode` | REMAIN EDC-SPECIFIC | Maps to `ConceptRef` (K005). SDTM TESTCD stays on the projection side. | |

### 2.11 `Domain/Ports`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Domain/Ports/IClinicalQueryRepository.cs` | `IClinicalQueryRepository` | REMAIN EDC-SPECIFIC | Re-key coordinates to `ParticipantId`/`ObservationId`. | |
| `Domain/Ports/IConsentRepository.cs` | `IConsentRepository` | REPLACE | Internal to Kernel.Persistence (K-XS-2). | |
| `Domain/Ports/IFormSubmissionRepository.cs` | `IFormSubmissionRepository` | REMAIN EDC-SPECIFIC | Re-key. Return tracked entities for mutation (D1). | `GetByCoordinatesAsync` and `ListByVisitAsync` have no production caller (tests/fakes only). |
| `Domain/Ports/IObservationRepository.cs` | `IObservationRepository` | REPLACE | Kernel Observation store (internal). EDC reads through Kernel.Abstractions queries. | `ListByCoordinatesAsync` has no production caller. |
| `Domain/Ports/ISiteComplianceRepository.cs` | `ISiteComplianceRepository` | REPLACE | Replaced together with `SiteComplianceCache`. | |
| `Domain/Ports/ISubjectRepository.cs` | `ISubjectRepository` | MIGRATE→KERNEL | Kernel Participant/Enrollment repository. `EnrollNextIndexAsync` becomes the kernel enrollment-number allocator (K-TX-4). | |

### 2.12 `Domain/Queries`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Domain/Queries/ClinicalQuery.cs` | `ClinicalQuery` | REMAIN EDC-SPECIFIC | Workflow stays and emits Assertions (§5.4). `ObservationId` becomes required and uses the kernel type. Re-key off codes. | `Open` overwrites the audit field `CreatedBy` (105). **K-TIME-3**: `DateTime.UtcNow` (141). `Escalate` has no caller. |
| `Domain/Queries/Errors/QueryErrors.cs` | `QueryErrors` | REMAIN EDC-SPECIFIC | Drop `UnauthorizedRole`; the kernel `Deny` replaces it. | |
| `Domain/Queries/QueryId.cs` | `QueryId` | REMAIN EDC-SPECIFIC | `IEntityId<T>` with UUIDv7. | `Guid.NewGuid()` (26). |
| `Domain/Queries/QueryStatus.cs` | `QueryStatus` | REMAIN EDC-SPECIFIC | Replace `DraftAi` with a kernel Proposal (§7.6). | `Resolved` is never assigned by any transition. |
| `Domain/Queries/QueryStatusCodec.cs` | `QueryStatusCodec` | KEEP | | |

### 2.13 `Domain/Subjects`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Domain/Subjects/ClinicalSubject.cs` | `ClinicalSubject` | MIGRATE→KERNEL | Split: kernel `Participant` (exists, thin) and `Enrollment` (missing). The post-randomization lock rule moves to Enrollment. `EncryptedDemographics` goes to the IdentityVault. `StratFactors`/`TreatmentGroupCiphertext`/`RandomizationId`/`KitReference` go to RTSM. `Unblind` becomes an Attestation-bearing event (§5.1). | **K-TIME-8**: `ActiveProtocolVersion` is never assigned beyond `"1.0.0"` (26). **K-AUTH-4**: blinding modelled as a state/flag. `Unblind`/`Withdraw` have no production caller (tests only). Domain depends on `Application.Abstractions` (4). |
| `Domain/Subjects/DomainErrorExtensions.cs` | `DomainErrorExtensions` | REMAIN EDC-SPECIFIC | Move to `Endpoints/`. The generic part goes to `Cadence.Kernel.Api`. | ASP.NET in `Domain/`, which violates AGENTS §8. Explicitly exempted at `tests/Cadence.ArchitectureTests/ArchitectureSentinelTests.cs:132`. |
| `Domain/Subjects/DomainErrorHttpMapping.cs` | `DomainErrorHttpMapping` | REMAIN EDC-SPECIFIC | As above. Split the code table into kernel codes and EDC codes. | As above. |
| `Domain/Subjects/ProtocolVersionRef.cs` | `ProtocolVersionRef` | REPLACE | `ProtocolVersionId` (exists) referenced from ConsentStatus, not from Applicability (see §3, item 3). | Code-keyed `(StudyCode, VersionTag, VersionIndex)`. |
| `Domain/Subjects/SiteCode.cs` | `SiteCode` | MIGRATE→KERNEL | Kernel.Abstractions attribute value object (§3.2). Kernel: missing, because the kernel `Site.SiteCode` is a raw `string`. | |
| `Domain/Subjects/StudyCode.cs` | `StudyCode` | MIGRATE→KERNEL | As above (kernel `Study.StudyCode` is a `string`). | |
| `Domain/Subjects/SubjectCode.cs` | `SubjectCode` | MIGRATE→KERNEL | As above (kernel `Participant.SubjectCode` is a `string`). | Uniqueness is global today (`ClinicalSubjectConfiguration.cs:34`), not per study. |
| `Domain/Subjects/SubjectConsent.cs` | `SubjectConsent` | REPLACE | Kernel ConsentStatus (bitemporal, §4.1), `Attestation(Consented)` and the ICF Artifact. | **K-TIME-1**: `MergeResubmission` overwrites in place (42-54). No valid/recorded period. |
| `Domain/Subjects/SubjectConsentId.cs` | `SubjectConsentId` | REPLACE | Kernel ConsentId. Kernel: missing. | `Guid.NewGuid()` (11). |
| `Domain/Subjects/SubjectErrors.cs` | `SubjectErrors` | MIGRATE→KERNEL | Enrollment error codes. `BlockedActivation` goes to StudySite errors. | `ReconsentRequired` is never raised in production (tests only). Rule 3 "block data entry until re-consent" is therefore unenforced. |
| `Domain/Subjects/SubjectId.cs` | `SubjectId` | REPLACE | `Cadence.Kernel.ParticipantId`. Kernel: exists. | `AsParticipantId()` (15) has **0 callers**, so the shim is dead. `Guid.NewGuid()`. |
| `Domain/Subjects/SubjectState.cs` | `SubjectState` | MIGRATE→KERNEL | Kernel Enrollment status enum without `ReconsentRequired` and `Unblinded` (see §3, item 1). | Kernel `Participant.Status` is an unguarded `string` (`Participant.cs:15`). |
| `Domain/Subjects/SubjectStateCodec.cs` | `SubjectStateCodec` | MIGRATE→KERNEL | Wire codec for the Enrollment status. | |
| `Domain/Subjects/SubjectTransitionGraph.cs` | `SubjectTransitionGraph` | MIGRATE→KERNEL | Kernel Enrollment state machine (§5.1). | See §3, item 1 for the actual graph. |

### 2.14 `Endpoints`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Endpoints/Filters/ChangeReasonHeaderFilter.cs` | `ChangeReasonHeaderFilter` | REMAIN EDC-SPECIFIC | Stays an edge translator (K-AUTH-1 allows that). Bind the header onto the kernel command. The pipeline enforces §8.3.4. | Not applied to most mutating routes: observation create/SDV, form create/complete/approve, query create/respond/close, subject create/consent. |
| `Endpoints/Filters/ValidationFilter.cs` | `ValidationFilter<TRequest>` | KEEP | | |
| `Endpoints/FormEndpoints.cs` | `FormEndpoints` | REMAIN EDC-SPECIFIC | Re-keyed routes. Delete the `/api/v1/forms` legacy alias after clients migrate. | |
| ″ | `FormPayloadUpdateRequest` | KEEP | | |
| ″ | `FormUnlockRequest` | KEEP | | |
| `Endpoints/ObservationEndpoints.cs` | `ObservationEndpoints` | REPLACE | `Cadence.Kernel.Api` observation endpoints plus EDC SDV-assertion endpoints. | |
| ″ | `FlagObservationRequest` | REPLACE | EDC SDV-flag Assertion request. | |
| `Endpoints/QueryEndpoints.cs` | `QueryEndpoints` | REMAIN EDC-SPECIFIC | Delete `GetCallerRole`. Authorization moves into the pipeline. | **K-AUTH-1**: trusts `X-User-Role` (172), D2. |
| `Endpoints/SiteComplianceEndpoints.cs` | `SiteComplianceEndpoints` | REPLACE | Kernel StudySite endpoints. The eTMF webhook is replaced by in-process eTMF events. | |
| `Endpoints/SubjectEndpoints.cs` | `SubjectEndpoints` | REPLACE | `Cadence.Kernel.Api` participant/enrollment/consent endpoints. | **K-AUTH-4**: `IsInRole("Blinded")` (249, 306), so blinding is modelled as a role. |

### 2.15 `Infrastructure/Consumers`, `Infrastructure/EditChecks`, `Infrastructure/Normalization`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Infrastructure/Consumers/EConsentCompletedConsumer.cs` | `EConsentCompletedConsumer` | REPLACE | The eConsent module issues kernel `RegisterParticipant` and `RecordConsent` commands in-process, as a saga (K-TX-1). Keep as an anti-corruption layer only while eConsent is out-of-process. | **K-TX-1**: creates a subject and a consent in one tx (147). Natural-key idempotency rather than K-TX-3 keys. D10 fixed for #4769: consent valid time is the event's `SignedAt`. |
| ″ | `EConsentCompletedConsumerLoggingExtensions` | REPLACE | Replaced together with the consumer. | |
| `Infrastructure/Consumers/EConsentCompletedEvent.cs` | `EConsentCompletedEvent` | REPLACE | Kernel `ConsentGiven` event (§8.2). | **K-XS-1**: `string StudyId`, `string? SiteId`. |
| `Infrastructure/EditChecks/AstEditCheckEngine.cs` | `AstEditCheckEngine` | REMAIN EDC-SPECIFIC | Keep behind `IEditCheckEngine` (first Rust candidate, §10). Retype inputs to a kernel Observation view. Hard-coded `SYSBP`/`VSSBP` codes become ConceptRefs. | `EvaluateBatchRules` has no production caller (tests only). |
| `Infrastructure/EditChecks/BatchEvaluationContextLoader.cs` | `BatchEvaluationContextLoader` | REMAIN EDC-SPECIFIC | Read observations through Kernel.Abstractions queries, not kernel tables (§9 module MUST NOT (b)). | Registered (`Program.cs:118`) but never resolved. |
| `Infrastructure/Normalization/LabRangeService.cs` | `LabRangeService` | MIGRATE→KERNEL | Implementation of the kernel range-derivation port. Version the catalogue (K-PROV-2). | The catalogue is keyed by SDTM codes and ignores sex/age in practice (see `ObservationApplicationService.cs:115`). |
| `Infrastructure/Normalization/OutlierDetectionService.cs` | `OutlierDetectionService` | DELETE | Recreate later in a central-monitoring/RBQM module if needed. | No production consumer (see `IOutlierDetectionService`). |
| `Infrastructure/Normalization/UcumNormalizationService.cs` | `UcumNormalizationService` | MIGRATE→KERNEL | Implementation of the kernel `IUcumNormalizer` (K-FACT-2). | |

### 2.16 `Infrastructure/Persistence`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Infrastructure/Persistence/ConcurrencyTokenIncrementInterceptor.cs` | `ConcurrencyTokenIncrementInterceptor` | KEEP | EDC schema still needs it. The kernel needs its own equivalent in Kernel.Persistence, which modules cannot reference (K-XS-2). | |
| `Infrastructure/Persistence/Configurations/ClinicalObservationConfiguration.cs` | `ClinicalObservationConfiguration` | REPLACE | Kernel Observation mapping with `valid_period`/`recorded_period` `tstzrange` + GiST (ADR-2246), partitioned by `(tenant_id, study_id)` (§10). | |
| `Infrastructure/Persistence/Configurations/ClinicalQueryConfiguration.cs` | `ClinicalQueryConfiguration` | REMAIN EDC-SPECIFIC | `edc` schema. Kernel-ID columns plus `tenant_id`. | |
| `Infrastructure/Persistence/Configurations/ClinicalSubjectConfiguration.cs` | `ClinicalSubjectConfiguration` | REPLACE | Kernel Participant/Enrollment mappings. | Global unique `SubjectCode` (34). |
| `Infrastructure/Persistence/Configurations/FormSubmissionConfiguration.cs` | `FormSubmissionConfiguration` | REMAIN EDC-SPECIFIC | `edc` schema, re-keyed. Drop the `signature_manifest` jsonb column. | |
| `Infrastructure/Persistence/Configurations/SiteComplianceCacheConfiguration.cs` | `SiteComplianceCacheConfiguration` | REPLACE | Replaced together with the aggregate. | |
| `Infrastructure/Persistence/Configurations/SubjectConsentConfiguration.cs` | `SubjectConsentConfiguration` | REPLACE | Kernel ConsentStatus mapping (bitemporal). | |
| `Infrastructure/Persistence/EfAuditLogWriter.cs` | `EfAuditLogWriter` | MIGRATE→KERNEL | Kernel AuditEntry writer inside the command pipeline, with a per-**tenant** hash chain (§8.3.3). Kernel: `KernelCommandTransaction` + `KernelAuditChainVerifier` (ADR-2449); the module still has to move onto it. `AuditLogEntry` itself lives in `packages/engine_core/src/Audit/AuditLogEntry.cs` and has no `tenant_id`. | D6 fixed for #4768: labels the next save; `GxPAuditInterceptor` writes the only entry, chained per CLR type name. |
| `Infrastructure/Persistence/ExecutionDbContext.cs` | `ExecutionDbContext` | REMAIN EDC-SPECIFIC | Becomes `EdcDbContext` with `HasDefaultSchema("edc")`. Keeps only FormSubmissions, ClinicalQueries and projections. Remove `ClinicalSubjects`, `SubjectConsents`, `SiteComplianceCaches`, `ClinicalObservations` and `AuditLogEntries` (K-MOD-1, §9). | The outbox tables (54) move to the kernel. |
| `Infrastructure/Persistence/ExecutionDbContextFactory.cs` | `ExecutionDbContextFactory` | REMAIN EDC-SPECIFIC | Single `cadence` database, `edc` schema (ADR-2247). | `Database=cadence_edc` (37) violates ADR-2247. |
| `Infrastructure/Persistence/ExecutionDbSeeder.cs` | `ExecutionDbSeeder` | REPLACE | Kernel fixtures via `dotnet cadence db seed`. | Fabricated genesis `BlockHash` (76-77, D7). `DateTime.UtcNow` (71). |
| `Infrastructure/Persistence/Repositories/AdvisoryLockSql.cs` | `AdvisoryLockSql` | MIGRATE→KERNEL | Kernel per-aggregate lock helper (K-TX-4). | Currently keyed per **study** code (`hashtext(studyCode)`), not per aggregate. |
| `Infrastructure/Persistence/Repositories/ClinicalQueryRepository.cs` | `ClinicalQueryRepository` | REMAIN EDC-SPECIFIC | Re-key. Tracked load for mutation. | D1 (37). |
| `Infrastructure/Persistence/Repositories/ConsentRepository.cs` | `ConsentRepository` | REPLACE | Kernel.Persistence. | |
| `Infrastructure/Persistence/Repositories/ExecutionRepositoryBase.cs` | `ExecutionRepositoryBase` | KEEP | EDC repository base. | |
| `Infrastructure/Persistence/Repositories/FormSubmissionRepository.cs` | `FormSubmissionRepository` | REMAIN EDC-SPECIFIC | Re-key. Tracked load for mutation. | D1 (28). |
| `Infrastructure/Persistence/Repositories/ObservationRepository.cs` | `ObservationRepository` | REPLACE | Kernel.Persistence Observation store. | D1 (27). |
| `Infrastructure/Persistence/Repositories/SaveChangesResiliencePipelineFactory.cs` | `SaveChangesResiliencePipelineFactory` | DELETE | Surface concurrency conflicts as `DomainError.Conflict` instead. Under K-TX-4 a lock is held per aggregate, so replaying is not needed. | D9 fixed for #4766: conflicts surface as 409 `CONCURRENCY_CONFLICT`; the pipeline now retries only transient failures. |
| `Infrastructure/Persistence/Repositories/SiteComplianceRepository.cs` | `SiteComplianceRepository` | REPLACE | Replaced together with the aggregate. | |
| `Infrastructure/Persistence/Repositories/SubjectRepository.cs` | `SubjectRepository` | MIGRATE→KERNEL | Kernel Participant/Enrollment repository and enrollment-number allocator. | |
| `Infrastructure/Persistence/Migrations/` (4 migrations + snapshot, 9 files) | EF migrations | REPLACE | New kernel migrations (`kernel` schema) and an `edc` baseline, plus a one-off data migration: codes → kernel IDs, `observation_date` → `valid_period`, consent rows → ConsentStatus. | `20260905174215` … `20260924160223`. |

### 2.17 `Infrastructure/Security`, `Infrastructure` (root), `Program.cs`

| File | Type(s) | Disposition | Target / change | Evidence / notes |
| --- | --- | --- | --- | --- |
| `Infrastructure/Security/AesGcmDemographicsEncryptionService.cs` | `AesGcmDemographicsEncryptionService` | MIGRATE→KERNEL | IdentityVault implementation with an envelope-encrypted key hierarchy (§5.1, K006). | A single static key from configuration. The default is an empty key (`DemographicsEncryptionOptions.cs:11`). |
| `Infrastructure/Security/DemographicsDecryptionFailedException.cs` | `DemographicsDecryptionFailedException` | MIGRATE→KERNEL | IdentityVault error. | |
| `Infrastructure/Security/DemographicsEncryptionOptions.cs` | `DemographicsEncryptionOptions` | MIGRATE→KERNEL | IdentityVault key configuration. | |
| `Infrastructure/Security/DemographicsNormalizationService.cs` | `DemographicsNormalizationService` | REPLACE | See `IDemographicsNormalizationService`. | No production consumer. |
| `Infrastructure/Security/AmbientAuditContextAccessor.cs` | `AmbientAuditContextAccessor` (was `HttpCurrentAuditContextAccessor`) | REPLACE | Kernel Actor resolution from the authenticated principal (§6). Fails closed when there is no principal. | D5 fixed in #4818: throws without a caller or service account. |
| `Application/Abstractions/ServiceAccount.cs` | `ServiceAccount`, `ServiceAccounts` (replaced `SystemAuditContextAccessor`) | REPLACE | Kernel `ServiceAccount` Actor (§6). | #4818: named `svc:` accounts; the consumer runs as `svc:econsent-consumer`. |
| `Infrastructure/SystemClock.cs` | `SystemClock` | MIGRATE→KERNEL | Kernel clock implementation, or `TimeProvider.System`. Kernel: exists (`Cadence.Kernel.Time.SystemClock`, #4784; execution file deleted). | |
| `Program.cs` | top-level statements, `Program` | REMAIN EDC-SPECIFIC | EDC module composition. Register kernel modules. Use one DB connection (ADR-2247). Remove replaced services and the MassTransit in-memory bus. | The outbox is configured (231-242) but nothing is published; see §3, item 6. |

---

## 3. Disagreements with §11 (seed mapping)

1. **`SubjectTransitionGraph` is not the graph §5.1/§11 describes.** The spec says `Screening → Randomized → Active →
   Completed | Withdrawn | Unblinded`. The code (`Domain/Subjects/SubjectTransitionGraph.cs:16-24`) has 9 states,
   including `ScreenFailed`, `Enrolled` and `ReconsentRequired`, and it forbids `Screening → Randomized`.
   Recommendation: keep `Screening/ScreenFailed/Enrolled/Randomized/Active/Completed/Withdrawn` as the Enrollment
   machine and remove two states:
   - `ReconsentRequired` becomes a pending Applicability *Transition* (§4.4 `RequiresReconsent`).
   - `Unblinded` becomes an Attestation-bearing event plus an RTSM Assertion (§5.1, K-AUTH-4).

   Today `Unblinded` counts as a lifecycle state in `IsPostRandomization()` (`ClinicalSubject.cs:112-113`).
2. **§11 lists unblinding and withdrawal as reachable logic ("`Unblind`/`Withdraw` … keep logic").** No production
   code path calls them. Their only callers are `tests/Cadence.UnitTests/Execution/Subjects/ClinicalSubjectTests.cs` and
   `tests/Cadence.IntegrationTests/Execution/SubjectGoldenMasterTests.cs`. The generic `PATCH /state` can reach `WITHDRAWN`/
   `UNBLINDED` without setting `WithdrawnAt`/`UnblindedBy`. Only the guard logic is worth migrating; the fields are not.
3. **§11 says to replace `ProtocolVersionRef` "with Applicability".** `ProtocolVersionRef` is used only by `SubjectConsent`
   (`Domain/Subjects/SubjectConsent.cs:17`). It records *which protocol version a consent covers*, which is the
   input to §4.4 step 2. It maps to a `ProtocolVersionId` on ConsentStatus, not to Applicability. Only
   `ClinicalSubject.ActiveProtocolVersion`, `ClinicalObservation.ProtocolVersionTag/Index` and
   `FormSubmission.ProtocolVersion` are replaced by Applicability (K-TIME-8). §11 omits `FormSubmission.ProtocolVersion`.
4. **§11 says to migrate consent to "Assertion + Artifact + Attestation", but §4.1 lists `ConsentStatus` as a bitemporal entity.**
   The spec is internally inconsistent here. The code's merge-in-place semantics (`SubjectConsent.cs:42-54`) and the
   cascade that clears the re-consent flag (`SubjectApplicationService.cs:164`) are state, not assertions. This inventory
   targets a **bitemporal ConsentStatus** plus `Attestation(Consented)` plus the ICF Artifact.
5. **§11 says `EConsentCompletedConsumer` "Migrate".** The consumer is an integration adapter, not domain logic, and it
   violates K-TX-1 by creating the participant and the consent in one tx (`EConsentCompletedConsumer.cs:97-147`).
   Disposition here is REPLACE: the eConsent module issues kernel commands as a saga.
6. **§11 says to replace `SiteComplianceCache` "by authority conditions (K-AUTH-5)".** The cache gates *participant enrollment at a
   site* (`SubjectApplicationService.cs`, `TransitionSubjectAsync` SITE_ACTIVATION check) and a no-op site
   activation (`SiteComplianceApplicationService.ActivateSiteAsync`). That is StudySite lifecycle / Effectivity
   (K-TIME-4, K-TIME-5), not an actor capability condition. K-AUTH-5 applies only to actor-level prerequisites
   such as training.
7. **§11 treats `AuditLogEntry`/`EfAuditLogWriter`/`AuditChainVerifier` as `apps/execution` items.** Only `EfAuditLogWriter` is in
   `apps/execution`. `AuditLogEntry` is in `packages/engine_core/src/Audit/AuditLogEntry.cs` and
   `AuditChainVerifier` is in `packages/compliance/src/AuditChainVerifier.cs`. §1.3 and §8.3 also describe the existing chain as
   "per-entity", but it is per entity *type*. There used to be two independent chains per entity (D6); #4768
   unified them into one per CLR type name, verified by `AuditLogChainVerifier` in `packages/engine_core`. The
   kernel still has to move that chain to per tenant (§8.3.3).
8. **§11 says "MassTransit outbox → Keep as kernel outbox".** `apps/execution/src` publishes nothing: there is no `Publish`/
   `IPublishEndpoint` outside `tests/`. `Program.cs:231-242` wires the outbox only so that the consumer works. No domain events
   exist to carry over, so K-TX-2 event emission is net-new work. Only the plumbing is reusable.
9. **§11 says to keep `AstEditCheckEngine` and the UCUM/lab-range/outlier services behind ports.** This inventory splits them
   three ways:
   - (a) UCUM and lab-range become **kernel** derivation ports (K-FACT-2, K-PROV-2), because their outputs are kernel facts.
   - (b) The edit-check engine stays **EDC** (edit checks are an EDC workflow). Its port must be retyped away from
     `ClinicalObservation` and SDTM codes (K-PROJ-1).
   - (c) The outlier service is **dead** (`Program.cs:117` registration is its only production reference) and should
     be deleted rather than kept.
10. **§11 says `ClinicalQuery` "Remain EDC workflow; emits Assertions".** Agreed. However, an Assertion needs a target
    fact, and a query targets coordinates (`StudyCode/SubjectCode/VisitId/DomainCode/TestCode`) with `ObservationId`
    optional (`ClinicalQuery.cs:28`). The query must be re-targeted to `ObservationId`. `QueryStatus.DraftAi`
    (`QueryStatus.cs:12`) must become a kernel Proposal (§7.6), not a workflow status.
11. **§11 omits the code value objects and IDs.** `StudyCode`/`SiteCode`/`SubjectCode` (spec §3.2: "kept") currently live in
    execution and must move to Kernel.Abstractions. Kernel entities use raw `string` codes today. The execution
    `ObservationId` and `SubjectId` are duplicates of existing kernel IDs, and `SubjectId.AsParticipantId()` is a dead shim.

---

## 4. Spec gaps (in execution, not covered by the spec)

| # | Gap | Where in execution | Suggested resolution |
| --- | --- | --- | --- |
| G1 | Sequential per-study enrollment numbering under an advisory lock | `SubjectRepository.EnrollNextIndexAsync`, `AdvisoryLockSql` | Add numbering to the Enrollment definition (scope, gap-free or not, K-TX-4 lock key). |
| G2 | Scope of `SubjectCode` uniqueness. It is globally unique today. | `ClinicalSubjectConfiguration.cs:34` | §3.2 should state the scope (per Study) and define a code→ID resolution query (the API accepts "id-or-code"). |
| G3 | Screening eligibility evaluation (I/E criteria → ScreenFailed/Enrolled) | `SubjectApplicationService.EvaluateAndTransitionScreeningAsync` (injected `Func` seam) | Model eligibility as Assertions over Observations against PlannedActivity criteria. |
| G4 | Whether an EDC edit check may **reject** a kernel write, and whether fatal batch checks block form completion | `ObservationApplicationService.cs:160`, `FormSubmission.Complete(blockingErrors)` | State whether module validators run pre-commit in the kernel pipeline or only produce post-commit Assertions. Rule sets need a versioned home (a ProtocolVersion-derived Artifact?). |
| G5 | Form/casebook locking, data freeze and database lock | `FormSubmission.Approve/Unlock/IsReadonly`, unused `FormErrors.TrialLocked` | Define Lock/Freeze as kernel Assertions (plus conditions blocking Execute) or as EDC-only state. |
| G6 | Re-consent cascade clearance and paper-consent override | `SubjectConsent.ClearReconsentRequirement`, `IsPaperOverride` | Extend §4.4 TransitionPolicy for paper consent, and specify how a newer consent resolves older pending transitions. |
| G7 | Enrollment-time arm assignment for non-randomized studies (`ArmId` → `TreatmentGroupCiphertext`, stored as plaintext) | `ClinicalSubject.cs:55-70` | Spec defers arms to RTSM. Define where a non-randomized arm assignment lives and its blinding class. |
| G8 | Free-form JSON extension properties on observations (ADR-2332) | `ObservationAdditionalProperties.CustomProperties` | Decide whether the kernel allows extension attributes or forces them into Concepts. |
| G9 | Local-lab reference ranges and `LabSource`/`LabSiteId` | `ObservationAdditionalProperties`, `LabRangeService` | Extend `Source=LabFeed` with the lab organization (an `OrganizationId` of kind Lab) and range-set version. |
| G10 | Cohort statistical outlier detection | `OutlierDetectionService` (dead) | Assign it to a central-monitoring/RBQM module, not the kernel. |
| G11 | Query metadata (priority, origin, escalation, action required) and AI-drafted queries | `ClinicalQuery`, `QueryStatus.DraftAi` | Map AI drafts to §7.6 Proposals. Keep the rest as EDC workflow fields. |
| G12 | Cross-store invariant: demographics and stratification factors lock post-randomization (vault + RTSM gated by Enrollment state) | `ClinicalSubject.UpdateDemographics/UpdateStratFactors` | K-TX-1 forbids one-tx enforcement. Specify the condition-based guard (Enrollment state read at command time) or a saga. |
| G13 | Public error-code contract (stable codes, status overrides 422/423) | `DomainErrorHttpMapping` | Add a kernel error-code registry to §8.1. |
| G14 | Anonymous and system identity fallbacks | Removed in #4818 (ADR-2257); `AuditAttributionSentinelTests` blocks new `"system"` actor defaults. | Add a rule, for example: "no kernel write without a resolved Actor; system work runs as a named ServiceAccount". |
| G15 | Natural-key idempotency on consumed events | `EConsentCompletedConsumer` (subject code / consent version) | K-TX-3 requires idempotency keys. State how natural keys map during migration. |
| G16 | Legacy route aliases and API version retirement | `/api/v1/forms`, `/api/v1/observations`, `/api/v1/queries`, `/consents` | Out of kernel scope, but needs a deprecation policy. |

**Kernel readiness blockers.** The kernel code in `packages/kernel/src` contradicts the spec in six places, and these
block migration into it:

- `Observation.Supersede` overwrites `Value` in place. That breaks K-FACT-1/K-TIME-1
  (`Cadence.Kernel.Domain/Entities/Observation.cs:46-48`).
- `ProtocolVersion.ForkAmendment` marks the parent `Superseded` on fork, contrary to §4.2 (`ProtocolVersion.cs:111`).
- `ProtocolApprovalStatus` has no `Withdrawn`.
- `Attestation` lacks a target, content hash and method (§7.5).
- `Participant` carries `SubjectCode`/`Status` strings and `StudySiteId`. §5.1 separates Participant from Enrollment.
- `Cadence.Kernel.Domain` references `Microsoft.EntityFrameworkCore` (csproj line 14), but §9 requires Domain to be pure.

The implemented K-XS-1 sentinel (`tests/Cadence.ArchitectureTests/ArchitectureSentinelTests.cs:223-228`) checks only 5 type names and none of the
`string *StudyId|*SiteId|*ParticipantId` member rule. It therefore passes `apps/execution` despite the ~20
K-XS-1 member violations flagged above.

---

## 5. Proposed migration order (dependency-aware slices)

Each slice ships green with ArchUnit, golden-master and time-travel tests before the next one starts.

| Slice | Content | Depends on | Execution items retired or moved |
| --- | --- | --- | --- |
| **S0 — Defect fixes** | D1–D10. Keep behaviour parity tests. Add the full K-XS-1 member rule to the sentinel (expected to fail, quarantined). | — | none (fixes in place) |
| **S1 — Kernel foundations** | Kernel.Abstractions: clock, `StudyCode`/`SiteCode`/`SubjectCode`, `IEntityId<T>` for EDC IDs, kernel `Result`/error registry. Fix kernel readiness blockers. Swap `SubjectId`→`ParticipantId` and `ObservationId`→kernel `ObservationId` (same GUID values, type-only change). | S0 | `IClock`, `SystemClock`, code value objects + converters, `SubjectId`, `ObservationId`, ID converters |
| **S2 — Identity and tenancy** | Tenant/Organization/Study/Site/StudySite rows backfilled from distinct `StudyCode`/`SiteCode` values in execution tables. `tenant_id` + RLS (K-ID-1/2). Code→ID resolution query (G2). Single `cadence` DB, `kernel`/`edc` schemas (ADR-2247). | S1 | `ExecutionDbContextFactory`, `Program.cs` connection |
| **S3 — Command pipeline, audit, authority** | Actor resolution, `Authorize()` with Grants/Capabilities (K004), AuditEntry with per-tenant chain (unifies D6), ProvenanceActivity, outbox (K-TX-2), full Attestation (§7.5). | S2 | `IAuditLogWriter`, `EfAuditLogWriter`, `ICurrentAuditContextAccessor` + impls, `ChangeReasonHeaderFilter` binding, role strings in forms and queries, `SaveChangesResiliencePipelineFactory` |
| **S4 — Participant, Enrollment, IdentityVault** | Enrollment state machine (§3, item 1), numbering (G1), vault (K006). RTSM fields parked in an `rtsm` schema. Blinding becomes a condition (K-AUTH-4). | S3 | `ClinicalSubject`, `SubjectState*`, `SubjectTransitionGraph`, `SubjectRepository`, `AdvisoryLockSql`, AES-GCM services, subject endpoints/DTOs |
| **S5 — Protocol, Effectivity, StudySite activation** | Effectivity with evidence Artifacts. K-TIME-4/5 guard on `StudySite.Activate`. eTMF emits Artifact events. | S3 (S4 in parallel) | `SiteComplianceCache` + repository/config/service/endpoints/webhook |
| **S6 — Consent and Applicability** | Bitemporal ConsentStatus + `Attestation(Consented)` + ICF Artifact. §4.4 initial assignment and TransitionPolicy (G6). eConsent issues kernel commands. | S4, S5 | `SubjectConsent*`, `ProtocolVersionRef`, `ConsentRepository`, `EConsentCompleted*`, `ActiveProtocolVersion` |
| **S7 — Observation, Assertion, Source, Provenance** | Full kernel Observation (ConceptRef via K005, ObservationValue, bitemporal, Source). UCUM and range derivations as derived Observations. Data migration of `clinical_observations` (valid = `observation_date`, flagged where D10 applies). | S6 (Applicability for K-TIME-8) | `ClinicalObservation`, `ObservationAdditionalProperties`, observation service/repo/config/endpoints, `LabRangeService`, `UcumNormalizationService` |
| **S8 — EDC re-key** | FormSubmission, ClinicalQuery, edit checks moved onto kernel IDs, ClinicalEvent/Visit and `AttestationId`. Queries and SDV emit Assertions. AI query drafts become Proposals. Wire batch edit checks into completion (G4). | S7 | EDC items marked REMAIN EDC-SPECIFIC |
| **S9 — Cleanup** | Delete legacy route aliases, `OutlierDetectionService`, dead members (`Escalate`, `Deactivate`, `Reopen`, unused error factories). Squash migrations. Un-quarantine K-XS-1 and K-MOD-1. | S8 | DELETE rows and legacy migrations |
