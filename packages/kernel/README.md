# Cadence Clinical Kernel

The core clinical domain modular monolith (`Cadence.Kernel.*`) providing the unified, shared clinical truth for the Cadence Clinical Research Software Platform.

## Architecture

As formalized in [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and specified in [`CLINICAL_KERNEL_SPECIFICATION.md`](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md), the Clinical Kernel consolidates:

- **Identity & Ownership**: Tenant, Organization, Person, Study, Site, StudySite
- **Executable Protocol**: ProtocolVersion lifecycle with content-hash freezing, attested approval and amendments (ADR-2452); a typed protocol design document with stable element ids, a structural diff and design findings (ADR-2472)
- **Clinical Subjects**: Participant, Enrollment, Cohort
- **Clinical Truth**: Observations, Assertions, Sources, Provenance
- **Authority**: Actors, Capabilities, Delegations, Attestations (21 CFR Part 11 electronic signatures bound to content hashes, ADR-2452)
- **Temporal Integrity**: PostgreSQL native bitemporal intervals (`valid_period`, `recorded_period`) via `tstzrange` and `gist` (ADR-2246)
- **Tenant Isolation**: `tenant_id` on every tenant-scoped table, composite `(tenant_id, id)` foreign keys and PostgreSQL Row-Level Security enabled and forced on every kernel table (ADR-2250)
- **Audit Ledger**: one SHA-256 hash chain per tenant over insert-only audit entries, written with provenance, events and outbox rows in each command's transaction (ADR-2449)

## Assemblies

Dependencies point inward only (spec §2.4, enforced by `tests/Cadence.ArchitectureTests/Architecture/KernelLayeringTests.cs`):

- `Cadence.Kernel.Abstractions`: strongly-typed UUIDv7 IDs (`TenantId`, `StudyId`, `ParticipantId`, `FormQueryId`, …), bitemporal value contracts (`TimeRange`, `TemporalPeriod`, `IBitemporalEntity`), `IClock`, serialization contexts, `NodeKinds` (including `form_query`), and the command contracts (`ICommand<T>`, `IKernelCommandSender`, `Actor`, `KernelErrors`, event records). No EF Core, ASP.NET Core or rendering. Application modules reference only this assembly and `Cadence.Kernel.Modules` (K-XS-2).
- `Cadence.Kernel.Domain`: kernel aggregates, state machines and temporal logic. Pure: no EF Core, ASP.NET Core or Npgsql.
- `Cadence.Kernel.Modules`: the module SDK ([ADR-2600](../../docs/adr/2026-10-19-modules-write-through-the-kernel-command-path.md), [ADR-2625](../../docs/adr/2026-11-22-bitemporal-form-query-aggregate-and-provenance.md)). The command handler contracts kernel and module handlers share (`ICommandHandler<TCommand, TResult>`, `ICommandContext`, `IKernelUnitOfWork`) and `AddCommandHandler<TCommand, TResult, THandler>()`; `ModuleDbContext`, `AddModuleCommandStore<TContext>()`, `ModuleTenantIsolationSql`, `ModuleBitemporalSql` (`KT001` trigger + `btree_gist` exclusion helper), `ModuleInsertOnlySql` (`KA001` trigger), and `HasModuleBitemporalPeriod` for a module's own records; `AddModuleMigrations<TContext>()` for its schema migrations and `AddModuleTenantContext<TContext>()` for its tenant-scoped reads; and, for its endpoints, the tenant resolution the host provides (`IKernelTenantResolver`, `KernelTenantFilter`, `KernelRequestTenant`), the signed change reason (`IKernelChangeReason`), whether the caller may see a kernel object as kernel reads decide it (`IKernelReadAccess`, implemented by `KernelReadAccess` in `Cadence.Kernel.Api`) and `KernelErrorEndpoints`; and, for signatures over its own records, `IAttestationVerifier`, `IAttestationTargetCheck` and `AddModuleAttestationTarget<TCheck>()` (ADR-2631). A module writes clinical state by implementing a handler and sending its command through `IKernelCommandSender`.
- `Cadence.Kernel.Application`: the command pipeline (`KernelCommandPipeline`), its ports (`ICurrentActor`, `IAuthorizer`, `IKernelUnitOfWorkFactory`) and the kernel's command handlers, and the authority evaluator (`AuthorityEvaluator`, ADR-2455).
- `Cadence.Kernel.Persistence`: `KernelDbContext` over the single `kernel` schema (ADR-2247), ID value converters, tenant-scoped contexts and Row-Level Security (`Tenancy/`), the per-command unit of work and audit chain (`KernelCommandTransaction`, `KernelAuditChainVerifier`), `KernelDatabase` (migrate, grant the app role) and migrations. Referenced only by `Cadence.Kernel.Api` and the host ([apps/kernel-api](../../apps/kernel-api/README.md)).
- `Cadence.Kernel.Api`: Minimal API route group (`/api/v1/kernel`) that slices add endpoints to, the read endpoints for the Clinical Canvas (`KernelReadEndpoints`: nodes, zoom path, children, command catalogue, CanI, search, protocol versions; `KernelLineageEndpoints`: Trace, dependents, amendment impact; `KernelActivityEndpoints`: the activity stream), self-service study, site, study-site and synchronous protocol draft import endpoints (`KernelStudyImportEndpoints`), observation writes (`KernelObservationEndpoints`), execution enrollment screening (`KernelExecutionEndpoints`), and proposals (`KernelProposalEndpoints`) behind `KernelTenantFilter`, which resolves the caller's kernel tenant through the host's `IKernelTenantResolver`, with every returned object masked through the host's `INodeVisibility` ([ADR-2540](../../docs/adr/2026-09-30-kernel-api-host-and-read-endpoints-for-the-canvas.md)). The host is [apps/kernel-api](../../apps/kernel-api/README.md).
- `Cadence.Standards`: reads public CDISC Library product content into a `StandardPackage` and ships checksum-verified seeds, and holds the .NET `IMappingEngine` (see [its README](src/Cadence.Standards/README.md)). References only `Cadence.Kernel.Abstractions`.
- `Cadence.Rules`: the rule expression language, rule packs and the `IRuleEvaluator` port with its .NET implementation (see [its README](src/Cadence.Rules/README.md) and [Rule packs](../../docs/kernel/RULE_PACKS.md)). References only `Cadence.Kernel.Abstractions`. Its benchmark is `benchmarks/Cadence.Rules.Benchmarks`.

Rendering lives in `packages/templating` (`Cadence.Templating`), outside the kernel.

`Cadence.Kernel.Api`, `Cadence.Standards` and `Cadence.Rules` set `CadenceRequireXmlDocs`, so every public member needs an XML
comment (CS1591 fails the build). The other kernel assemblies do not yet: on 2026-09-30 they had about 890
undocumented public members (Abstractions 609, Domain 196, Application 46, Persistence 37). Set the property in a
project once its comments are written ([CONTRIBUTING.md §3](../../CONTRIBUTING.md#3-local-checks)).

## Tenant isolation (ADR-2250)

PostgreSQL keeps tenants apart, not application code (K-ID-1, K-ID-2):

- **Composite keys.** Every tenant-scoped table has `tenant_id NOT NULL` and a unique `(tenant_id, id)` key, and every
  reference between tenant-scoped tables is a composite foreign key `(tenant_id, x_id) → (tenant_id, id)`. A row that
  names another tenant's row fails the constraint. Map new references with
  `HasForeignKey(x => new { x.TenantId, x.OtherId }).HasPrincipalKey(x => new { x.TenantId, x.Id })`;
  `KernelDbContextModelTests` fails on a plain one.
- **Row-Level Security, forced.** Each table has one policy comparing its tenant with `kernel.current_tenant_id()`,
  the value of the `cadence.tenant_id` setting. `FORCE` binds the table owner too. With no tenant set a connection
  sees no rows and can insert none.
- **One place sets the tenant.** `KernelTenantConnectionInterceptor` sets `cadence.tenant_id` when a kernel context
  opens a connection and clears it on close. Contexts come from `KernelTenantDbContextFactory.Create(tenant)`; the
  pipeline binds each command's context to the command's tenant. A context with no tenant cannot connect.
- **People (K-ID-4).** `people` is tenant-independent and has no `tenant_id`. A tenant sees a person only through its
  own `tenant_memberships` row. A person row is pseudonymous: an id and an active flag. Names and contact details
  live in the IdentityVault, per tenant.
- **Jobs that visit every tenant** (outbox relay, verify-and-seal) list tenant ids with the
  `kernel.tenant_ids()` directory function, then work through a connection scoped to each tenant in turn.
- **Resolving the gateway's tenant code** (#4780): `ITenantDirectory.FindActiveByCodeAsync(code)` returns the id of
  the active tenant with that exact code, or null, through `kernel.tenant_id_by_code(text)`, so a host can bind its
  tenant-scoped reads. The two directory functions return ids only and are the only way to read across tenants.

A new kernel table needs `migrationBuilder.EnableTenantIsolation(KernelDbContext.Schema, "<table>")` in the
migration that creates it. `KernelTenantIsolationPostgresTests` fails for any kernel table without `tenant_id`, forced
RLS or a policy.

**Deployment roles.** Run migrations as the schema owner with `KernelDatabase.MigrateAsync(ownerConnectionString)`.
Connect the application with a separate login role that is not the owner and has no `BYPASSRLS`, and grant it
`KernelDatabase.GrantAppRoleSql("cadence_app")` after every migration: read, insert and update on kernel tables,
no update on insert-only tables, no delete anywhere, and the tenant directory. A superuser bypasses RLS entirely, so
never connect the application as one.

## Concept Registry (ADR-2470)

The Knowledge engine's semantic layer (spec §6.1, ADR-2252). Types live in the `Knowledge` namespaces of each assembly.

| Concept                                                                   | Table                                         | Changes                            |
| ------------------------------------------------------------------------- | --------------------------------------------- | ---------------------------------- |
| `CodeSystem` (URI, publisher, `LicenseClass`)                             | `code_systems`                                | registered once per tenant and URI |
| `CodeSystemVersion` with its entries                                      | `code_system_versions`, `code_system_entries` | insert-only                        |
| `Concept` (current version, status) and its versions                      | `concepts`, `concept_revisions`               | revisions insert-only              |
| `ConceptMapping` (concept version → `Coding`, relation, status, evidence) | `concept_mappings`                            | review status only                 |

Every tenant loads the releases it adopts, public ones included; licensed content (MedDRA, WHODrug, SNOMED CT) is
never in this repository. To load a release, register its code system once, then load the publisher's file:

```csharp
await sender.SendAsync(new RegisterCodeSystem(tenant, ctId, KnownCodeSystems.SdtmCt, "SDTM CT", "CDISC/NCI EVS",
    LicenseClass.Open, key, "Adopt SDTM CT"));
var version = await new TerminologyPackageLoader(sender).LoadAsync(
    new TerminologyRelease(tenant, ctId, CodeSystemVersionId.New(), "2026-06-27", new DateOnly(2026, 6, 27),
        "SDTM Terminology.txt", "https://evs.nci.nih.gov/ftp1/CDISC/SDTM/", publishedSha256, "Adopt SDTM CT 2026-06-27"),
    File.ReadAllBytes(path), new CdiscControlledTerminologyParser("sdtmct"));
```

Each CDISC CT package type is its own code system, `KnownCodeSystems.CdiscCt("sdtmct")` (constants `SdtmCt`, `CdashCt`,
`AdamCt`, `SendCt`, `ProtocolCt`, `DefineXmlCt`), and the release date is the version. A standard's CT binding
`(sdtmct, 2024-09-27)` therefore resolves with `FindCodeSystemVersionAsync(tenant, KnownCodeSystems.CdiscCt("sdtmct"),
"2024-09-27")`. The C-codes inside a package are NCI Thesaurus codes.

The loader refuses a file whose SHA-256 differs from the expected one; loading the same file again replays the import.
Parsers exist for NCI EVS CDISC CT text (`CdiscControlledTerminologyParser`), `ucum-essence.xml` (`UcumEssenceParser`)
and `Loinc.csv` (`LoincTableParser`); any other source can send `ImportCodeSystemVersion` with its entries.

Read through `IConceptRegistry` (registered by `AddKernelPersistence`): every lookup names a concept version
(`ConceptRef`) or a code-system version (`ResolveManyAsync` batches distinct `(ConceptId, Version)` pairs in one query); there is no "latest". Only confirmed mappings resolve a code to a concept.

## Protocol versions and attestations (ADR-2452)

A study's protocol is a series of numbered `ProtocolVersion`s (spec §6.2). Each moves
`Draft → InReview → Approved → Superseded | Withdrawn`:

```csharp
var draft = await sender.SendAsync(new CreateProtocolDraft(tenant, study, versionId, ProtocolDesignDocument.Write(design), key1, "First draft"), cancellationToken: ct);
var review = await sender.SendAsync(new SubmitProtocolVersionForReview(tenant, versionId, key2, "Ready for review"), cancellationToken: ct);
var target = new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, review.Value!.ContentHash!);
var signature = await sender.SendAsync(new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, target, authTime, "pwd+otp", key3, "Approve v1"), cancellationToken: ct);
await sender.SendAsync(new ApproveProtocolVersion(tenant, versionId, signature.Value, key4, "Approved by sponsor"), cancellationToken: ct);
```

- **Freezing.** `SubmitProtocolVersionForReview` freezes the content and sets `ContentHash`: `sha256:` and lower-case
  hex SHA-256 of the canonical JSON (`Cadence.Kernel.Trust.ContentHash`, the same form as `CanonicalSigner`).
  `ReturnProtocolVersionToDraft` clears the hash so the draft can be revised.
- **Client-computed hashes.** `JsonCanonicalization` is RFC 8785 (JCS): members sorted by UTF-16 code units and
  numbers written as ECMAScript writes a double, so a hash matches the one a browser computes with
  `canonicalSerialize` in `packages/ui/signing.js`. Execution's form approval uses it (ADR-2513). `ContentHash` keeps
  number text as written and is unchanged.
- **Signatures.** `Attest` records an append-only `Attestation`. Only a person can sign, the authentication must be at
  most five minutes old, and the target's current hash must equal the signed one.
- **Approval and withdrawal.** `ApproveProtocolVersion` and `WithdrawProtocolVersion` name an attestation with meaning
  `Approved` or `Withdrawn`, signed by the person executing the command. `IAttestationVerifier` checks it within the
  tenant; `AddKernelPersistence` registers it, and standards governance uses the same verifier. The verifier's
  contract is in the module SDK (`Cadence.Kernel.Modules`), so module handlers require signatures the same way
  (ADR-2631).
- **Forks.** `ForkProtocolVersion` starts an amended draft from an approved version, numbered after every version in
  the study and carrying a `ProtocolAmendment`. The parent stays approved; `Superseded` is left to Effectivity (#4777).
- **Database guards.**
  - `kernel.enforce_protocol_version_lifecycle()` rejects, with SQLSTATE `KA002` and whoever connects:
    - content changes outside draft;
    - moves outside the state machine;
    - hash changes after review;
    - deletes.
  - `attestations` is insert-only (`KA001`).
- **Reading versions.** `IProtocolVersionReader` lists a study's versions newest first, or reads one, as
  `ProtocolVersionSummary` (lineage, status, hash, amendment intent, approval, withdrawal reason; no content). It
  reads under the tenant's RLS and does not authorize the actor: the host checks the read with `CanI` first.
- **Reading other aggregates.** A handler that needs another aggregate to decide (a parent version, the study's
  versions) reads it with `IKernelUnitOfWork.ReadAsync`/`ReadAllAsync`. These neither track nor claim it, so the
  command still changes one aggregate (K-TX-1).

### Protocol design document (ADR-2472)

A version's content is a `ProtocolDesign` (`Cadence.Kernel.Protocol.Design`): objectives, endpoints, estimands,
populations, eligibility criteria, arms, epochs, cohorts, interventions, planned activities, collection specifications
with their items, safety and discontinuation rules, and references to the randomization scheme and statistical design.
CDISC USDM converts in and out of it; it is not the stored shape.

```csharp
var read = ProtocolDesignDocument.Read(version.Content);            // strict: every problem listed, never re-serialized
var findings = ProtocolDesignReview.Findings(fork.Design());       // K-PROT-3 gaps; informs, never blocks
var outside = fork.Design().ExternalReferences();                   // ConceptRefs, code-system versions, artifacts
```

- **Element ids (K-PROT-1).** Unique in the design, a letter then up to 127 letters, digits and `_ . : -`, and kept by
  forks. References between elements are checked by kind, so an estimand's `populationId` must name a population.
- **Strict reading.** Unknown properties, missing or null required values, duplicate properties, undefined enums, a
  missing `schemaVersion` and references without a concept version are `K.Protocol.ContentInvalid`, with up to ten
  problems in the message.
- **References at submit (K-SEM-1).** `SubmitProtocolVersionForReview` refuses with `K.Protocol.ReferenceUnknown`
  when a ConceptRef's concept does not exist at that version, or a code-system version or artifact is not in the
  tenant. A draft may still name them before they are registered.
- **Diff.** Every element is a JSON object with a unique `id`, so the simulation engine's `ProtocolStructuralDiff`
  (ADR-2480 §4.6) compares two designs element by element. A collection item is its own element, so changing it does
  not change its specification.
- **USDM import (ADR-2473).** `UsdmDesignImporter.ImportAsync` in `packages/ddf` reads a CDISC USDM 4 study into a
  design, keeping USDM ids as element ids and resolving each Biomedical Concept in the tenant's Concept Registry. It
  fails when a concept is missing and lists everything it leaves out as notes.
- **Elements as lineage nodes (ADR-2474).** Approving a version writes its elements as `protocol_element` nodes, one
  per study and element id (`ProtocolElementId.For(study, elementId)`), versioned by the protocol version that last
  added or changed them. The approved version governs every element; changed elements supersede their previous
  version; the scientific chain and the concepts that define an element are edges. See [Lineage](#lineage-adr-2471).
- **Schedule of Activities (ADR-2475).** `ScheduleOfActivities.Compile(design)` renders the design as a matrix:
  planned activities are columns grouped by epoch and ordered by study day (event-driven ones last), collection
  specifications are rows with the columns that collect them, and footnotes (windows, locations, event-driven visits,
  operational needs, unscheduled specifications) are generated. `Write` and `Hash` give the same bytes and hash for the
  same design. `ScheduleOfActivitiesDiff.Compare(parent, amended)` returns typed directives (`VisitMoved`,
  `ActivityFrequencyChanged`, `CriterionAdded`, …) with the elements and concepts each touches, and whether the
  schedule or eligibility changes. `IProtocolVersionReader.GetScheduleOfActivitiesAsync` compiles a stored version's
  matrix; it is never stored.

## Studies, sites and study sites (#5485)

A study is owned by one sponsor organization, a site is a durable location of an institution, and a study site is
that site in that study (spec §3.1). Each is created by a command through the pipeline:

| Command           | Capability (resource)         | Event               | Refuses                                                                                               |
| ----------------- | ----------------------------- | ------------------- | ----------------------------------------------------------------------------------------------------- |
| `RegisterStudy`   | `study.register` (tenant)     | `StudyRegistered`   | an unknown sponsor, a code already used in the tenant                                                 |
| `RegisterSite`    | `site.register` (tenant)      | `SiteRegistered`    | an unknown institution, a code the institution already uses, a country that is not ISO 3166-1 alpha-2 |
| `LinkSiteToStudy` | `study_site.link` (the study) | `SiteLinkedToStudy` | an unknown study or site, a site already linked to the study                                          |

A new study site is `Selected`; `ActivateStudySite` activates it once a protocol version is in force there
([Effectivity](#effectivity-and-study-site-activation-adr-2456)). Each handler checks uniqueness under one advisory
lock (the tenant, the institution, the study), so concurrent registrations with one code run in turn.

## Participants and enrollment (ADR-2453)

A `Participant` is an identity only; each study membership is an `Enrollment` at one study site (spec §7.4):

```csharp
await sender.SendAsync(new RegisterParticipant(tenant, participantId, key1, "Consented"), cancellationToken: ct);
var screened = await sender.SendAsync(
    new ScreenParticipant(tenant, enrollmentId, participantId, studySiteId, SubjectCode.From("101-001"), screenedAt, key2, "Screening visit"),
    cancellationToken: ct);
var enrolled = await sender.SendAsync(
    new ChangeEnrollmentStatus(tenant, enrollmentId, EnrollmentStatus.Enrolled, enrolledAt, key3, "Eligible"), cancellationToken: ct);
// enrolled.Value!.EnrollmentNumber is the next number in the study.
```

- **Lifecycle.** `EnrollmentTransitions` holds the state machine. A change is effective after the current status began
  and not after it is recorded. `ChangeEnrollmentStatus` cannot withdraw: `WithdrawParticipant` names an attestation
  with meaning `Withdrawn` over the enrollment's current `ContentHash` (`Attest` accepts `enrollment` targets), signed
  by the person executing the command.
- **Capture after withdrawal (#5578).** Once the participant is withdrawn, `RecordObservation`, `OpenCollectionInstance`
  and a `RecordCollectedValues` save with a value for an item that has none are refused with 409
  `K.Observation.ParticipantWithdrawn`, explained by the producer `kernel.capture` (condition
  `participation.not-withdrawn`, evidence the enrollment) and audited as `Refused`. The spec is silent on late entry
  and corrections, so the kernel refuses all new capture whatever its observed-at time, and accepts corrections of
  values captured before the withdrawal (`CorrectObservation`, a save that only corrects); those values stay readable
  (K-CONS-2). `WithdrawParticipant` takes the enrollment's scope lock exclusively and capture shares it before it reads
  the status, so no new value commits after the withdrawal (ADR-2621). The check is
  `Evidence/CaptureConditions.cs`: with computable consent (#4777) it becomes `ConsentAllows` over the withdrawn
  scopes (K-CONS-1), not yet.
- **History.** Every change closes the current row of `enrollment_status_periods` and records the old status up to
  the change and the new status from it. `IEnrollmentDirectory.StatusAsOfAsync(tenant, enrollment, validAt, knownAt)`
  reads it, `ResolveParticipantAsync(tenant, study, code)` resolves a subject code within a known study (K-ID-5), and
  `LookupByCodeAsync(tenant, studyCode, subjectCode, siteCode)` backs `GET /api/v1/kernel/enrollments/lookup` to resolve
  pinned study, study site, and enrollment `NodeRef`s over the unique `(tenant_id, study_id, subject_code)` index with
  non-confirming `404` caller visibility masking (ADR-2471).
- **Uniqueness.** Subject codes are unique per study. A participant has at most one active enrollment at a study
  site at any valid time; rescreening after a screen failure is a new enrollment.
- **Advisory locks (K-TX-4).** `IKernelUnitOfWork.LockAsync<TAggregate>(id)` takes a PostgreSQL advisory lock on the
  command's connection until its transaction ends; a command takes at most one. Screening and enrolling take the
  Study lock, so codes and numbers are checked and assigned one at a time per study, and numbers stay gap-free.
  Protocol version numbering takes the same lock.
- **Database guards.** `kernel.enforce_enrollment_lifecycle()` rejects, with SQLSTATE `KE001` and whoever connects:
  illegal moves, identity or code changes, renumbering, withdrawal without its attestation, changes to a terminal
  enrollment, deletes and truncates. Status rows are append-only (`KT001`), and the exclusion constraint
  `ex_enrollment_status_periods_one_active` keeps one active enrollment per participant and study site.
- **Not yet:** eligibility gating (#4727), StudySite activation gating (#4777) and per-StudySite numbering.

## Evidence artifacts (ADR-2454)

An `Artifact` is versioned evidence content (spec §8.3). The bytes go to object storage first; the kernel records
what they are:

```csharp
var content = new ArtifactContent(ContentHash.OfBytes(bytes), bytes.Length, "application/pdf", storageRef);
var registered = await sender.SendAsync(
    new RegisterArtifact(tenant, artifactId, versionId, ArtifactKind.EthicsApproval, studyId, "IRB approval", content, key1, "Filed approval"),
    cancellationToken: ct);
var amended = await sender.SendAsync(
    new AddArtifactVersion(tenant, artifactId, ArtifactVersionId.New(), amendedContent, key2, "Amended approval"), cancellationToken: ct);
```

- **Versions.** Version 1 comes with registration; later versions are numbered on. Content an existing version
  already has is refused (`K.Evidence.ContentUnchanged`). `study` is null for tenant-wide evidence.
- **Content.** `ArtifactContent.Problem()` checks the `sha256:` hash, a positive size, a lower-case `type/subtype`
  media type and a storage reference. `ContentHash.OfBytes` computes the hash the storage adapter records.
- **Signing.** `Attest` accepts `artifact_version` targets with the version's content hash.
- **Exports.** `GET /api/v1/kernel/studies/{studyRef}/exports` and `GET /api/v1/kernel/enrollments/{enrollmentRef}/casebook`
  synchronously dispatch `[BaselineKernelCommand]` `ExportStudySnapshot` (`study.export`) and `ExportEnrollmentCasebook`
  (`enrollment.export`, requiring grant coverage over the target enrollment) through `IKernelCommandSender`, registering an immutable `Dataset` `Artifact`, emitting
  `StudySnapshotExported` or `EnrollmentCasebookExported`, and recording blinded versus unblinded disclosure on the
  tenant's audit chain; unauthorized export requests record a denied `ProvenanceActivity` and `AuditEntry` before reading
  clinical rows. Study exports and regulatory archive bundles filter out-of-scope (`NotPermitted`) sites and enrollments in place and report excluded scopes only as a collapsed `hidden` indicator (`more_exist`, `reasons`) and `X-Hidden-More-Exist` / `X-Hidden-Reasons` headers (ADR-2590). Historical cuts evaluate observations and governing protocol versions at `(valid_at, known_at)` while bounding lock, release, query, answer, and verification assertions up to `known_at`, anchor payload and ZIP timestamps to `known_at` so repeated cuts produce byte-identical `X-Content-Sha256` digests (with wall-clock time in `X-Generated-At`), and emit complete ODM v1.3.2 `<Study>`, `<AdminData>`, and `<StudyEventData>` casebook XML.
- **Database.** `artifacts` and `artifact_versions` are tenant-isolated; `artifact_versions` is insert-only
  (`KA001`) with check constraints on the hash, size and number.
- **Not yet:** `Source`, lab range sets and the TMF classification Assertion.

## Bitemporal tables (ADR-2246)

A bitemporal entity implements `IBitemporalEntity` (`TemporalPeriod Period`, made of two half-open `TimeRange`s).
To persist one:

1. Map it with `builder.HasBitemporalPeriod()` in its `IEntityTypeConfiguration`. This stores `Period.Valid` and
   `Period.Recorded` as `valid_period` / `recorded_period` `tstzrange` columns.
2. In the migration that creates the table, call
   `migrationBuilder.CreateBitemporalGuards(KernelDbContext.Schema, "<table>", "<entity key column>", ...)` after
   `CreateTable` (and `DropBitemporalGuards` in `Down`). This adds:
   - **K-TIME-2:** a deferred `EXCLUDE USING gist (tenant_id WITH =, <keys> WITH =, valid_period WITH &&,
recorded_period WITH &&)`, so two versions of one fact can never both be true and known at the same instant.
     Because it is deferred, a correction can close the old row and insert the new one in the same save.
   - **K-TIME-1:** triggers that reject `DELETE`, `TRUNCATE` and any `UPDATE` other than closing an open
     `recorded_period` at a later instant (SQLSTATE `KT001`).
3. Read with `AsOf(validAt, knownAt)` / `AsOfCurrent(now)`. They translate to `valid_period @> @validAt AND
recorded_period @> @knownAt`, which the GiST index serves. `TimeRange.Contains`/`Overlaps`/`Start`/`End`/`IsOpen`
   translate to `@>`, `&&`, `lower`, `upper` and `upper_inf` in any kernel query.

`KernelSchemaPostgresTests` fails if a mapped bitemporal table is missing its guards.

## Command pipeline (ADR-2449)

Every change to kernel state is a command sent through `IKernelCommandSender` (spec §5.1). `KernelDbContext`
refuses `SaveChanges` anywhere else (K-TX-2).

```csharp
var result = await sender.SendAsync(new ProvisionTenant(tenantId, "ACME", "Acme Pharma", idempotencyKey, reason), cancellationToken: ct);
if (!result.IsSuccess) return result.Error;   // KernelError: stable code + HTTP status (K-ERR-1)
```

The pipeline validates the command (tenant, idempotency key, reason for change), resolves the actor from
`ICurrentActor` (no actor, no write: K-TX-5), authorizes with `IAuthorizer` (a denial is itself audited), replays or
rejects a used idempotency key (K-TX-3), runs the handler, and commits in one PostgreSQL transaction. A handler
refusal that carries a `KernelError.Explanation` (a refusal decided by a condition, such as capture for a withdrawn
participant) is audited too: a `Refused` audit entry with the command, error code, reason and evaluated conditions, on a
provenance activity that keeps the command's permit, and nothing else is written.

| Written in the same transaction                                                                  | Table                                   | Mutable?                    |
| ------------------------------------------------------------------------------------------------ | --------------------------------------- | --------------------------- |
| The one changed aggregate, with `CreatedAt`/`CreatedBy`/`ReasonForChange`/`VersionIndex` stamped | its own table                           | per its rules               |
| One provenance activity (K-PROV-1)                                                               | `provenance_activities`                 | insert-only                 |
| Its events in the §5.2 envelope                                                                  | `domain_events`                         | insert-only                 |
| One link of the tenant's hash chain                                                              | `audit_entries` (+ `audit_chain_heads`) | insert-only (head advances) |
| One outbox row per event                                                                         | `outbox_messages`                       | delivery bookkeeping only   |
| The stored result for the key                                                                    | `idempotency_records`                   | insert-only                 |

Insert-only tables (these, plus `audit_chain_seals`, the terminology tables and `attestations`) reject `UPDATE`,
`DELETE` and `TRUNCATE` with SQLSTATE `KA001`. `KernelAuditChainVerifier` re-derives a tenant's chain and reports the
first broken link.

If the handler refuses or the commit conflicts, the pipeline checks the idempotency key again before returning the
error: a concurrent send with the same key may have committed in between, and its stored result is returned as a
replay.

### After the commit: relay and seals

`AddKernelBackgroundJobs()` runs three jobs in the host (call it in exactly one host per deployment):

- **Outbox relay** (`KernelOutboxRelay`): visits each tenant in turn and delivers up to `RelayBatchSize` of its
  events' envelopes to the host's `IKernelEventPublisher`, at least once, oldest first per tenant. A failed delivery is retried after 2^attempts seconds (capped at one hour)
  with the error in `outbox_messages.last_error`, and holds back only its own tenant's later events. Subscribers
  deduplicate on `EventId`.
- **Verify and seal** (`KernelAuditChainSealer`, hourly): verifies every tenant's chain and seals each intact one at
  its head in `audit_chain_seals`, as `svc:audit-sealer`. A broken chain is logged as critical and never sealed. A
  chain later cut back below a seal fails verification.
- **Lineage check** (`KernelLineageOrphanCheck`, hourly): reports each tenant's derived object versions that have no
  inbound lineage edge (K-LIN-1) to `ILineageOrphanSink`. The default sink logs a warning.

Intervals and batch size are set through `KernelBackgroundOptions`.

Each pass first takes a PostgreSQL session advisory lock named after its job (`KernelLeaderLock`), so with several
replicas exactly one runs each job at a time and each tenant's events keep their order; a replica that dies mid-pass
loses the lock with its connection (ADR-2601).

**Consumers (ADR-2600 §4.6).** `AddKernelInProcessEvents()` makes the relay deliver to consumers registered with
`AddKernelEventConsumer<TConsumer>("name")` (module SDK). A consumer derives from `KernelEventConsumer<TEvent>`, which
matches the event's `[KernelEvent]` type and schema version and deserializes the payload. Each delivery runs in its own
scope where `IKernelCommandSender` acts as `svc:<name>`; a consumer changes state only by sending a command keyed by
`delivery.IdempotencyKey` (`<name>:<event id>`), so the idempotency records are its inbox and a retried or redelivered
event replays instead of acting twice. A failing consumer makes the relay retry the event for every consumer. The
service account needs grants like any actor.

To add a command:

1. Declare the command record in `Cadence.Kernel.Abstractions/Commands` implementing `ICommand<TResult>`, its events
   with `[KernelEvent("Name", 1)]` in `Events/`, and any new errors as `public static readonly KernelError` fields
   with a `K.<Area>.<Name>` code. List the errors its handler can return with
   `[RaisesKernelErrors(typeof(SomeErrors), nameof(SomeErrors.X), …)]`; the pipeline's own
   (`KernelErrors.CommandPipeline`) are implied.
2. Implement `ICommandHandler<TCommand, TResult>` (from `Cadence.Kernel.Modules`) in `Cadence.Kernel.Application`: load or add **one**
   aggregate through `context.Work` (a second throws, K-TX-1), decide, `Raise` events, declare the aggregate's
   inputs with `LinkInput` (see [Lineage](#lineage-adr-2471)), return a `CommandResult`. Never save.
3. Register the handler in `AddKernelCommandPipeline()`, and a FluentValidation `AbstractValidator<TCommand>` for the
   command's shape with `AddCommandValidator<TCommand, TValidator>()`. The pipeline runs it after resolving the actor
   and before authorization, and refuses an invalid command with `K.Command.Invalid` (422) and field errors in
   `KernelError.Errors`. A command with nothing to validate beyond the pipeline-wide checks carries
   `[NoValidator("why")]`; `CommandValidationCatalog` (`Cadence.Testing`) fails a test when a command has neither or
   both. Rules that read state stay in the handler. Existing kernel commands still check their inputs in the handler
   (`NoValidatorAttribute.HandlerChecksInputs`, #5448).
4. Test it through the pipeline in `tests/Cadence.IntegrationTests/Kernel` (see `KernelCommandPipelinePostgresTests`).
   The fixture's sender is wrapped with `AddDeclaredKernelErrorsCheck()` (`Cadence.Testing`), so a command that returns
   an error it does not declare fails the test.
5. Map an endpoint for it with `.ProducesKernelCommand<TCommand>()` and answer a failure with
   `KernelErrorEndpoints.Problem(error)`. The endpoint then lists every code it can return in its OpenAPI operation
   (`x-kernel-errors`, and in each problem response's description), and `KernelErrorContractTests` fails when a kernel
   endpoint that takes a body declares none or names an unregistered code.

A module writes its own records through the same pipeline (ADR-2600):

1. Derive the module's context from `ModuleDbContext` and map each aggregate with `HasCommandAuditColumns()`, which
   makes `VersionIndex` the concurrency token. The context refuses `SaveChanges` for audited records outside a command.
2. In the module's migration, give each tenant-scoped table a `tenant_id` column and apply
   `ModuleTenantIsolationSql.EnableForTenantColumn(schema, table)`, the same forced RLS policy as kernel tables.
3. Declare the module with `AddCadenceModule("module_name", typeof(SomeCommand).Assembly)`, so the kernel authorizes
   its commands against the `[KernelCapability]` they declare (a command's resource is a kernel identifier; a command
   without a capability is denied). Register `AddModuleCommandStore<TContext>("module_name", options => new TContext(options))`, each handler with
   `AddCommandHandler<TCommand, TResult, THandler>()` and each validator with `AddCommandValidator<TCommand, TValidator>()`;
   the module's tests call `CommandValidationCatalog.Violations` for its command assembly. The context must not enable a retrying execution strategy.
4. The handler uses `context.Work` exactly as a kernel handler does. At commit the pipeline opens the module context on
   the command's tenant-scoped connection, enlists it in the kernel transaction, stamps the audit fields, writes the
   module row with the version check and audits it on the tenant's chain as `module_name.TypeName`. A stale version
   fails with `K.Aggregate.ConcurrencyConflict`; any failure rolls back the module row with every kernel row.
   `ModuleCommandPathPostgresTests` shows a complete test module.
5. Register the module's migrations with `AddModuleMigrations<TContext>("module_name", schema, options => new TContext(options), grantAppRoleSql)`.
   The host applies them after the kernel's when `Database:MigrateOnStartup` or `Database:MigrateOnly` is set, with the
   history table `__ef_migrations_history` in the module's schema (pass the same to the command store's
   `MigrationsHistoryTable`); `grantAppRoleSql` grants the application role what the module needs, for a deployment
   that migrates separately.
6. For reads outside a command, register `AddModuleTenantContext<TContext>(connectionString, options => new TContext(options))`
   and create no-tracking contexts through `IModuleTenantContextFactory<TContext>.Create(tenant)`. Each connection sets
   `cadence.tenant_id` before any statement and clears it on close, so the module's RLS policies show that tenant's
   rows only. An endpoint gets the tenant from `KernelTenantFilter` and `KernelRequestTenant`.

A host registers `AddKernelPersistence(appRoleConnectionString)`, `AddKernelCommandPipeline()`, an `IClock`, an
`ICurrentActor`, an `IAuthorizer`, an `INodeVisibility` and logging; the container fails to resolve the sender or the
lineage reader if any is missing. The host that runs `AddKernelBackgroundJobs()` also registers an
`IKernelEventPublisher`; the kernel host registers the in-process one.

## Observations and corrections (ADR-2620)

An `Observation` (`kernel.observations`) is the authoritative clinical value; an eCRF field is a representation of it.
Commands in `Commands/ObservationCommands.cs`, each authorized against the observation's enrollment:

| Command              | Does                                                                                                             |
| -------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `RecordObservation`  | Records the first value; participant and study come from the enrollment; refused (409) once it is withdrawn      |
| `CorrectObservation` | Appends a revision with the reason; refused when locked (423), stale (`ExpectedVersion`, 409) or unchanged (422) |

- Revisions (`observation_revisions`) are insert-only (`KA001`); revision _n_ is recorded until revision _n+1_.
- A correction changes one aggregate (K-TX-1). Record and correct are refused (423) while a lock covers the
  observation (see below).
- `IObservationRecords` reads an observation with its revisions, the locks covering it, the assertions about it and
  its kernel audit entries under one tenant connection lease and the composite
  `(tenant_id, aggregate_type, aggregate_id, sequence)` audit index.
- Reactions (the eCRF projection, edit checks, queries, SDV) consume `ObservationCorrected` after commit.
- The value is a typed `ObservationValue` (`Evidence/ObservationValue.cs`, ADR-2623): `quantity` (UCUM unit,
  optional comparator), `coded` (code-system version and code), `text`, `boolean`, `dateTime` (partial ISO 8601) or
  `missing` (null flavor and detail), with a `type` discriminator, stored as `jsonb`. `CollectionItem.Admits` checks a
  value against the protocol's item, and `MappingInput.From` feeds it to the SDTM mapping (`--STAT`/`--REASND` for a
  missing value).
- A form page saves its values in one command through a `CollectionInstance` (below).

## Assertions and scoped locks (ADR-2621)

An `Assertion` (`kernel.assertions`, insert-only) is a statement about an observation, another assertion or a lock
scope (spec §8.2, §10.5). Nothing about the observation changes when something is asserted about it, and an assertion
is never changed: a later assertion of the same kind retracts it (`RetractsId`, at most one retraction each). Commands
in `Commands/AssertionCommands.cs`:

| Command             | Does                                                                                                          |
| ------------------- | ------------------------------------------------------------------------------------------------------------- |
| `ApplyLock`         | `Locked` or `Frozen` at a study, study site, enrollment, form or observation (`lock.apply`), with a signature |
| `ReleaseLock`       | Releases a lock with its own reason and signature (`lock.release`, Approve)                                   |
| `VerifyObservation` | SDV of one revision, `target = (ObservationId, Revision)`; refused for a revision that is not current (409)   |
| `RaiseQuery`        | A query about an observation; it names the `ObservationId` only, so it stays open across corrections          |
| `AnswerQuery`       | Answers an open query                                                                                         |
| `RetractAssertion`  | Withdraws a verification or an answer, or closes a query; locks are released with `ReleaseLock` instead       |

- A lock's signer attests the canonical `LockStatement.Apply` (or `.Release`) for the assertion id first, meaning
  `Approved` or `Responsibility`; the attestation targets `assertion:<id>` before it exists, the way a finding
  decision is signed (K-FIND-2), and the command recomputes the hash from its own inputs.
- Both lock kinds refuse `RecordObservation`, `CorrectObservation`, `OpenCollectionInstance`, `RecordCollectedValues`,
  `RecordUnaskedCollectionItem`, `ScreenParticipant`, `ChangeEnrollmentStatus`, `WithdrawParticipant`, `LinkSiteToStudy`,
  `ActivateStudySite`, `DeclareEffectivity`, and `ChangeEffectivityStatus` in their covering scope (`423 K.Observation.Locked`);
  `Locked` also refuses new assertions (`423 K.Assertion.Locked`) and form signatures (`SignCollectionInstance`), while
  `Frozen` still allows queries, SDV verifications, retractions, and investigator form signing (`collection.sign`).
  `ApplyLock` with `LockKind.Locked` evaluates scope readiness (`LockReadinessEvaluator`) and refuses with 409
  `K.Assertion.ScopeNotReady` (carrying a structured `Explanation` naming each blocker owner, target `NodeRef` and next
  action) when any of the six readiness gates has unresolved blockers in the target scope: open queries (`OpenQuery`),
  missing required items (`MissingRequiredItem`), unverified observation revisions (`UnverifiedValue`), unsigned or
  signature-superseded forms (`UnsignedForm`), due/overdue scheduled visits without opened forms (`UnopenedVisit`,
  excluding `ScreenFailed` enrollments and post-withdrawal visits), or active `Error`-severity validation findings
  (`ErrorFinding`). `Frozen` skips these gates (`can_freeze = true`) so review, SDV, query closure, and signing can
  proceed before hard lock.
- Concurrency: a write that a lock can cover takes shared advisory locks on its covering scopes, widest first
  (`IKernelUnitOfWork.ShareScopesAsync`, `LockScopes.Covering`), then reads the locks in force; `ApplyLock` and
  `ReleaseLock` take the exclusive lock on their own scope (`LockScopeAsync`). Either the write commits before the lock
  or it is refused, which `KernelObservationPostgresTests` races at every scope. A correction first takes the observation's
  own exclusive lock, so two corrections of one value run one after another; a command that locks a scope exclusively
  shares none.
- Events: `LockApplied` and `LockReleased` (Quality), `AssertionMade` and `AssertionRetracted` (Evidence).
- The HTTP surface serves SDV (`POST /api/v1/kernel/observations/{ref}/verifications` and `POST /api/v1/kernel/assertions/{ref}/retraction`, #5465), Execution's queries (`/api/v1/execution/forms/…/queries`, #5461), signed scoped locks with `K-AUTH-4`-masked readiness checks (`GET /api/v1/kernel/studies/{studyRef}/locks/readiness`), synchronous covering write-lock queries for standalone services (`GET /api/v1/kernel/locks/in-force` and `POST /api/v1/kernel/locks/in-force/batch`, #5376, enforcing non-confirming `404` `INodeVisibility` checks for person and automation-agent callers while preserving tenant-wide lock evaluation for verified `svc:` `SYSTEM_SERVICE` consumers), and bitemporal exports (`KernelLockEndpoints`, `KernelExportEndpoints`, #5466, #5592), where study lock lists evaluate lock and release assertions up to `known_at`, omit hidden scopes behind a collapsed `hidden` indicator (ADR-2590), and mask `kind`, `scope_kind`, and `released` on blinded locks (ADR-2471).

## Form instances and page saves (ADR-2622)

A `CollectionInstance` (`kernel.collection_instances`) is the kernel side of an eCRF form instance (spec §8.1,
K-FACT-6): the observations collected together for one enrollment under one CollectionSpec of the protocol version
that governed the collection. Its members (`collection_instance_members`, insert-only) name one observation per
CollectionSpec item; an observation belongs to one instance and never leaves it. Layout, item order, workflow status
and UI state stay with the application's form. Commands in `Commands/CollectionCommands.cs`, each authorized against
the enrollment:

| Command                       | Does                                                                                                                                                                                                     |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `OpenCollectionInstance`      | Opens a form for a CollectionSpec and optional planned activity (`collection.open`) at a collection time                                                                                                 |
| `RecordCollectedValues`       | Saves a page: records new values and corrects existing ones in one commit, each against its expected revision                                                                                            |
| `RecordUnaskedCollectionItem` | Materializes a `MissingValue(NullFlavor.NotAsked)` observation at revision 1 on an unrecorded item (`collection.record_unasked`) so a missing-data query can target a kernel `(ObservationId, Revision)` |

- The kernel, not the caller, picks the governing protocol version: the highest-numbered version effective at the
  enrollment's study site at the collection time (K-TIME-8, through `EffectivityResolution`). Its design must have the
  CollectionSpec, and the planned activity must list it (422 otherwise; 409 when no version is effective). The form
  records `CollectedBy` lineage inputs from its governing `CollectionSpec` (and `PlannedActivity`, when scheduled)
  `protocol_element` nodes, and every observation version a save writes records a `CollectedBy` input from its
  governing `CollectionItem` `protocol_element` node. When the
  enrollment has an `Enrolled` period and the planned activity has a timing, `OpenCollectionInstance` also evaluates
  `ExpectedSchedule.Compute` and `ExpectedSchedule.Assess` from `CollectedAt` and the `Enrolled` start date without
  reading a wall clock (K-TIME-3, K-TIME-7) and returns the optional `VisitTimingAssessment` on
  `CollectionInstanceState` (never persisted and never blocking early or late capture; ADR-2507). Unscheduled forms,
  untimed event-driven activities and screening enrollments return `null`.
- A save names items by their CollectionSpec item id; the observation records the item's concept. The validator
  refuses a malformed save (no values, an item or observation id named twice, a negative revision) with
  `K.Command.Invalid`. The handler refuses, with an error per item, an item the spec lacks and a value the item does
  not admit (kind, UCUM unit, code-system version, and codelist or code-system membership via `IConceptRegistry`; 422). Required items and ranges are edit checks that run after commit
  (ADR-2622 §4.4).
- One stale expected revision (0 for an item with no value yet) refuses the whole save with 409 and an error per
  stale item; a value equal to the current one is left as it is. The result returns every value of the form with its
  current revision, ordered by item id, so the client marks values saved only from it.
- K-TX-1 as amended: the save takes the form's exclusive advisory lock (`IKernelUnitOfWork.LockCollectionAsync`), then
  shared locks on every scope covering the form and its values; the commit writes the form first and its changed
  member observations after it, with one audit link each, and refuses any other aggregate. Two users saving different
  items both succeed one after the other; two saving one item get one success and one 409.
- After the participant's withdrawal no form opens and a save with a new value is refused whole (409
  `K.Observation.ParticipantWithdrawn`); a save that only corrects values goes on (see Participants and enrollment).
- The form is a lock scope (spec §10.5): a lock on it refuses saves, and corrections and assertions of its values made
  one at a time. `ICollectionInstanceRecords` reads a form's items (the CollectionSpec's, in the governing version's
  order) with hydrated codelist options, its values with their revisions, the assertions about them, the signatures over the form and the locks in
  force in one call (`FindAsync`), enrollment summaries with active/answered query assertion ids (`ListForEnrollmentAsync`),
  and narrow projections for headers (`FindHeaderAsync`, `FindHeadersAsync`), batch instances (`FindManyAsync`),
  observation revisions and blinding signals (`FindObservationRevisionsAsync`, `FindObservationProjectionsAsync`),
  query targets (`FindQueryTargetAsync`, `FindQueryTargetsAsync`), attestations (`FindAttestationAsync`, `FindAttestationForReaderAsync`),
  lock/revision scope state (`FindScopeStateAsync`), and rule inputs (`ListRuleInputsForEnrollmentAsync`).
- Events: `CollectionInstanceOpened` and `CollectedValuesSaved` (Evidence), besides each value's `ObservationRecorded`
  or `ObservationCorrected`. Nothing consumes `CollectedValuesSaved` yet; edit checks will (#5368).
- `KernelCollectionPostgresTests` covers atomicity at every commit stage, per-value 409, concurrent saves, the
  lock-versus-save race at every scope and replay. The HTTP surface is the Execution module's `/api/v1/execution/forms` routes (#5460), composed into the kernel host.

## Object references and navigation (ADR-2481)

Every kernel object is referenced publicly as a `NodeRef` (`Cadence.Kernel.References`): `kind:id` or
`kind:id@version`, for example `study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3`. `NodeRef.Parse` accepts only the
canonical string, and `NodeRef.For(studySiteId)` builds one from a typed id. When you add a strongly-typed identifier to
`Cadence.Kernel.Abstractions`, register its kind in `NodeKinds`; `NodeRefTests` fails until you do.

Every event envelope carries `Subject`, the NodeRef of the aggregate the command changed at its new version.

`IKernelNavigator` (`AddKernelNavigation()` in Application, `AddKernelScopeGraph()` in Persistence) resolves a NodeRef
to a `NodeSummary`, computes its zoom path (`tenant → study → country → study_site → enrollment`, or
`tenant → organization → site`) and lists its children with counts. A participant appears in a study as its
enrollment, titled by its subject code; an enrollment with no responsible study site sits directly under its study
(`ZoomRules.EnrollmentParent`). Collection instances and standalone observations sit under their enrollment, form-bound
observations under their collection instance, concepts under their tenant, protocol versions, study standards,
validation runs and findings under their study, a protocol element under the version that last governed it, and an attestation under the object it signs. When a slice maps a new kind, add its case to `KernelScopeGraph` with the kind's zoom parent.

## Standards Registry and MDR (ADR-2460)

Standard definitions and the governed library are tenant-scoped kernel data (spec §6.3, #4809). Code lives under
`Standards/` in each layer; the read port `IStandardsRegistry` and the commands are in
`Cadence.Kernel.Abstractions` (`Cadence.Kernel.Standards` namespace), so applications can use them.

| Command                                              | Does                                                                                                                                                                          |
| ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `RegisterStandardVersion`                            | Registers a `StandardPackage` (datasets, variables, value-level definitions, CT bindings) once per `(tenant, code, version)`; optional `ExpectedContentHash` pins the content |
| `ApproveStandardVersion`, `DeprecateStandardVersion` | Draft → Approved → Deprecated, each needing an attestation (`Approved`, `Responsibility`)                                                                                     |
| `DraftLibraryItem`                                   | Adds the next version of a keyed library item (collection spec, mapping spec, rule pack, codelist subset)                                                                     |
| `ApproveLibraryItem`, `DeprecateLibraryItem`         | Same governance as standard versions                                                                                                                                          |
| `DeclareStudyStandards`                              | Sets a study's approved standard versions, CT versions and library pins (one profile per study, revised in place)                                                             |

- The content hash (`sha256:` + 64 hex) is over canonical JSON: sorted keys, datasets and variables in ordinal order.
  `IStandardsRegistry.GetStandardPackageAsync` returns the package in that order, so it re-hashes to the stored hash.
- Deprecating a standard version or library item stops new pins; it never changes a study that already pinned it.
- Dataset, variable, value-level and terminology rows are insert-only (`KA001`); the identity and content columns of
  `standard_versions` and `library_items` are immutable (`KS001`, `ImmutableColumnsSql`).
- Library content is validated per kind by any registered `ILibraryContentValidator` (in Abstractions, so other
  assemblies can provide one); kinds without one accept any JSON object. `AddCadenceRules()` registers the `RulePack`
  validator (ADR-2461); mapping specifications add theirs with #4810.
- CT bindings are `CodeSystemVersionRef(code system, version)`, naming a CDISC CT package type (`sdtmct`) or a
  code-system URI (`KnownCodeSystems.UriFor`), resolved to the tenant's Concept Registry version (ADR-2460 §4.2).
  `RegisterStandardVersion` stores the resolved `CodeSystemVersionId` when the package is imported and leaves it
  empty otherwise; `DeclareStudyStandards` refuses (`DeclarationInvalid`) any CT version the tenant has not imported.
  `StandardVersionSummary.ResolvedTerminology` and `StudyStandardsProfileView.ResolvedTerminology` return the pairs.
- Mapping specifications (ADR-2463, K028, namespace `Cadence.Kernel.Standards.Mapping`) are `MappingSpecification`
  library items, read by `MappingSpecificationJson` and documented in
  [Mapping specifications](../../docs/kernel/MAPPING_SPECIFICATIONS.md). Drafting one needs exactly one standard version
  and checks its datasets and variables, CT versions, codelists and Concepts, linking each as `DefinedBy`.
  `DeclareStudyStandards` refuses pins that `StudyMappings.Resolve` cannot combine (an override without its base, a
  mapping into an undeclared standard, a Concept mapped twice).
- K-STD-1: `SubmitProtocolVersionForReview` runs every registered `IProtocolSubmissionCheck`; `AddKernelStandards()`
  registers `MappingCoverageCheck`, which refuses (`K.Standards.MappingMissing`) a design whose collected Concepts are
  neither mapped nor accepted gaps (`MappingGap`), and links the mapping versions used as `MappedBy`. It reads findings
  through `IValidationFindings`, so a host that calls `AddKernelStandards()` also calls
  `AddKernelValidationPersistence()`.

A host that uses the registry also calls `AddKernelStandards()` (handlers) and `AddKernelStandardsPersistence()`
(`IStandardsRegistry`). Approval and deprecation name an attestation recorded with `Attest` on the `standard_version`
or `library_item` target, which the kernel's `IAttestationVerifier` checks.
Load public content with `StandardSeeds.LoadAll()` or `CdiscLibraryProductReader.Read(...)` and send each package in
a `RegisterStandardVersion` command.

## Validation runs and findings (ADR-2462)

A validation run records what a study's pinned rule packs detected at one stage (spec §10.3). Send
`RecordValidationRun`, or let `ValidationRunner` (`Cadence.Rules`) evaluate and send it:

| Command                                                  | What it does                                                                                                                      |
| -------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `RecordValidationRun`                                    | Records a run: released pack versions, stage, as-of date, standard, agency, versioned inputs, datasets covered and detections     |
| `InvestigateFinding`, `ClassifyFinding`, `ReopenFinding` | Decisions on a finding's workflow; each reads the finding's derived status first                                                  |
| `AcceptFinding`, `ExplainFinding`                        | Need a `Responsibility` or `Approved` attestation over `FindingDecisionContent.Hash(finding, last detecting run, decision, text)` |

- A finding's ID is `ValidationFindingId.For(tenant, identity)`: study, stage, pack key, rule ID and target, not the
  rule version. Targets that are kernel objects use a NodeRef without `@version`.
- Status is derived on read by `FindingLedger`: a run covering the finding's stage, pack and dataset that no longer
  detects it resolves it, and a decision made before a resolution is void if the finding recurs.
- `IValidationFindings` finds, lists (filters and `BlockingOnly`) and explains findings, and reads runs.
- Run tables are insert-only (`KA001`); a decisions record keeps its finding, study and author (`KS001`).
- A run is `[DerivedObject]`, linked `DefinedBy` its pack versions and `ValidatedBy` its inputs; decisions link
  `DerivedFrom` their run.

A host registers `AddKernelValidation()` and `AddKernelValidationPersistence()`. Attesting other kinds of record:
register an `IAttestationTargetCheck` for the kind with `AddModuleAttestationTarget<TCheck>()` (both in the module SDK
`Cadence.Kernel.Modules`, ADR-2631); `Attest` consults it after the core kinds.

## Explanations (ADR-2482)

Anything that decides also explains, with one type: `Explanation` (`Cadence.Kernel.Explanations`), holding a claim,
reasoning steps, every evaluated condition (met or unmet) with its evidence NodeRefs, and next actions. The component
that owns the rule builds it from the same evaluation that decided; applications and the web app only render it
(spec §5.5, K-EXP-1).

For authorization, return conditions from your policy and the pipeline does the rest:

```csharp
AuthorizationDecision.Deny("Protocol v3 training is not current.",
    EvaluatedCondition.Satisfied("delegation.active", "Delegated eligibility signing", delegationRef),
    EvaluatedCondition.Unsatisfied("qualification.current", "Complete protocol v3 training", trainingRef));
```

A denied command returns `K.auth.denied` with `KernelError.Explanation` set, and the `DecisionDenied` event and
audit entry record the reason and each condition with its evidence.

A handler that refuses a command on a condition explains it the same way: capture for a withdrawn participant returns
`K.Observation.ParticipantWithdrawn` with an explanation from `kernel.capture`, and the pipeline audits every handler
refusal that carries an explanation as a `Refused` audit entry (no event).

## Authorization and dry runs (ADR-2483)

Every kernel command declares the capability it needs and the property holding the object it is checked against:

```csharp
[KernelCapability("concept.revise", VerbClass.Execute, Resource = nameof(ReviseConcept.ConceptId))]
public sealed record ReviseConcept(TenantId TenantId, ConceptId ConceptId, /* … */) : ICommand<ConceptRef>;
```

`Resource` defaults to `TenantId`; a command that creates an object names the scope it is created in.
`KernelAuthorizationTests` fails for a command without the attribute, and the pipeline denies one at runtime.

`AddKernelAuthorization()` registers `KernelAuthorization` as the pipeline's `IAuthorizer` and as
`IAuthorizationProbe`. Both turn the question into an `AuthorizationRequest` for the host's
`IAuthorizationEvaluator`, so a dry run gets exactly the decision the command would.
`CanIAsync(tenant, capability, resource)` answers for the current actor, and `GetCommandsAsync(tenant, resource)`
lists the commands offered on an object with their decisions and explanations. Dry runs write nothing to the audit
chain; they are logged as event `2483 AuthorizationDryRun`.

## Grants, roles and delegation (ADR-2455)

Authority is kernel evidence, not a role string. A host registers the evaluator next to the pipeline:

```csharp
services.AddKernelAuthorization();
services.AddKernelAuthorityEvaluator(platformOperators: ["svc:provisioner"]);
```

- **Roles.** `DefineRole` creates an immutable, named bundle of capabilities. A role never bundles an unblinding
  capability (K-AUTH-4).
- **Grants.** `IssueGrant` gives an actor one capability or one role at an `AuthorityScope` (`Tenant`,
  `Organization`, `Study`, `StudyCountry` or `StudySite`) for a validity period. `WithGrantOption` lets the holder
  delegate.
- **Delegation.** `DelegateGrant` issues a child grant under one the caller holds with the grant option. It is
  refused if it exceeds the parent's capabilities, scope or validity, or if it is made to oneself.
- **Revocation.** `RevokeGrant` supersedes the grant's current validity row and records a shortened one. Every
  delegation under the grant stops counting.
- **Evaluation.** `AuthorityEvaluator` permits when a grant held by the actor carries the capability (directly or
  through its role), covers the resource's scope path and is in effect at `(at, known_at)` with every grant above
  it; when `ResolveVisibilityBatchAsync` classifies an `observation`, `validation_finding`, `assertion`, or `form_query`
  resource as blinded treatment data (K-AUTH-4), non-unblinding capabilities also require an in-effect
  `enrollment.unblind` grant and current K-AUTH-5 qualifications over the evidence scope, denying with the uniform
  `grant.active` condition otherwise. Automation agents are denied Execute and Approve. Platform operators (`svc:`
  accounts) hold only `tenant.provision`, `grant.issue` and `role.define`, which lets them bootstrap a tenant through
  the pipeline.
- **Recording.** Each decision names its conditions (`grant.active`, `grant.delegable`, `grant.delegator`,
  `actor.platform_operator`, `actor.agent_verb`). The command's provenance activity stores them in
  `authorization`.
- **Tables.** `kernel.roles` is insert-only. `kernel.grant_periods` carries the bitemporal guards. `kernel.grants`
  checks that exactly one of capability and role is set and that the scope columns fit the level, a `StudyCountry`
  scope included.
- **Visibility (ADR-2457, K-AUTH-4).** `AddKernelGrantVisibility()` registers `GrantNodeVisibility` as the host's
  `INodeVisibility`. A reader sees what a grant it holds in effect covers, the organization, study and country above
  that scope, participants and sites through the study sites they reach, and tenant reference data; blinded
  `observation`, `validation_finding`, `assertion`, and `form_query` nodes classified through
  `IBlindedTreatmentClassifier` are masked `Blinded` unless `enrollment.unblind` is permitted (including `K-AI-6`
  agent-requester intersection and `K-AUTH-5` qualifications). Everything else out of scope is masked `NotPermitted`.
- **Qualifications (ADR-2459).** `RecordQualification` files a qualification held by an actor: `Training`,
  `ProtocolTraining` (naming its protocol version), `Credential` or `Certificate`, with a lower snake_case code, a
  validity period and a `TrainingRecord` or `Credential` artifact version as evidence. `RevokeQualification`
  shortens it bitemporally. `DefineQualificationRequirement` says a capability within a scope needs a current
  qualification of a code (and protocol version); `RetireQualificationRequirement` ends it. After a grant permits,
  the evaluator adds one `qualification.current` condition per requirement in effect over the resource and denies
  when one is unmet: "Obtain gcp", or "Renew protocol_training for protocol version …; it expired on …".
  `qualification_periods` and `qualification_requirement_periods` carry the bitemporal guards.
- **Not yet:** separation of duties, and StudySite, protocol or blinding conditions (rest of #4781).

## IdentityVault (ADR-2458)

Direct identifiers never enter a kernel table (K-IDN-1). They are sealed in the `identity_vault` schema, per tenant,
for two layers: `IdentityLayer.Person` (subject: `PersonId`) and `IdentityLayer.SiteLocal` (subject: `ParticipantId`,
for example a screening log number).

```csharp
await sender.SendAsync(new RecordPersonIdentity(
    tenant, person, new IdentityDetails { GivenName = "…", FamilyName = "…", Email = "…" }, idempotencyKey, "Registration"));
var revealed = await vault.RevealAsync(
    new RevealPersonIdentity(tenant, person, idempotencyKey, "Contacting the participant about visit 3"), ct);
```

- **Commands.** Record (`identity.record.<layer>`, Execute), reveal (`identity.reveal.<layer>`, Execute), erase
  (`identity.erase.<layer>`, Approve), `LinkParticipantToPerson` (`identity.link.cross_study`, Execute) and
  `RevealPersonLinks` (`identity.reveal.links`, Execute). Reveals are Execute, so automation agents are refused
  them. Each is authorized and audited, and raises a Trust event that names the layer and subject only. The details are `[JsonIgnore]` and print no values, so they never reach the
  request hash, the audit entry or an event.
- **Reveal.** `IIdentityVault.RevealAsync` sends the reveal command and decrypts only after it has committed. A
  replayed reveal returns `K.IdentityVault.RevealReplayed` and no details; use a new idempotency key for each read.
- **Sealing.** `IdentitySealer` encrypts each record with its own AES-256-GCM data key, wrapped by the tenant's
  key-encryption key from an `IVaultKeyProvider`, and binds the tenant, layer and subject as associated data. A
  blind index (HMAC-SHA256 of the normalized e-mail or screening number) is stored for future lookups.
- **Erasure.** Erasing clears the key, ciphertext and blind index (crypto-shredding) and keeps the row as the record
  of the erasure. An erased identity cannot be recorded again, revealed or linked.
- **Residency.** `ProvisionTenant` takes an optional residency (`EU`, `US`, …). A vault serves tenants with no
  residency and tenants of its own region; any other returns `K.IdentityVault.ResidencyMismatch`.
- **Tables and role.** `identity_vault.identity_records` and `identity_vault.participant_links` force row-level
  security. `KernelDatabase.GrantAppRoleSql` grants the application role `EXECUTE` on the vault functions and no
  privilege on the tables. Staged writes are applied by the command transaction, inside the command's commit.
- **Registration.** A host opts in with `AddKernelIdentityVault()` and
  `AddKernelIdentityVaultPersistence(masterKeyBase64, region)`, where the master key is 32 bytes of base64 from the
  host's secret store. `LocalVaultKeyProvider` derives each tenant's keys from it with HKDF; production replaces it
  with a KMS-backed `IVaultKeyProvider`. A host without these registrations has no vault.
- **Not yet:** lookup by blind index, a person registration command, reveal under a study-site grant, and moving
  execution's `EncryptedDemographics` into the vault.

## Work queues (ADR-2486)

A work queue is a versioned definition keyed by a capability, never a role name.

```csharp
await sender.SendAsync(new DefineWorkQueue(
    tenant, WorkQueueDefinitionId.New(), "protocol_approvals", "Protocol approvals", "protocol.approve",
    VerbClass.Approve, KernelWorkItemSources.ProtocolVersionsInReview, idempotencyKey, "Queue for approvers"));
var queues = await workQueues.ReadAsync(tenant, new WorkQueueQuery(Within: NodeRef.For(study)), ct);
```

- **Definitions.** `DefineWorkQueue` adds the next version of a code (`work_queue.define`, Approve, at the
  tenant). A kernel command's capability must keep the verb class the command declares, and the source must be
  registered. `kernel.work_queue_definitions` is insert-only; the highest version is current.
- **Items.** An `IWorkItemSource` returns what is due, oldest first. `IWorkQueues.ReadAsync` keeps an item only
  when `IAuthorizationProbe.CanIAsync` permits the current actor the queue's capability on it now, so revoking a
  grant removes its items. Hidden objects are masked in place through `INodeVisibility`.
- **Scope.** `Within` narrows to a tenant, sponsor organization, study or one object. Without it the read spans
  every study of the tenant (the portfolio queue).
- **Sources.** `AddKernelPersistence` registers `protocol.versions_in_review` (due since submission). Applications
  register their own with `services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkItemSource, …>())`.
- Register `AddKernelWorkQueues()` for the read; the `DefineWorkQueue` handler comes with `AddKernelCommandPipeline()`.

## Effectivity and study-site activation (ADR-2456)

Approval of a protocol version is not effectiveness. An `Effectivity` says whether a version is in force in one
scope of its study, and `ActivateStudySite` checks it.

- **Scopes.** `EffectivityScope.Study()`, `InCountry("DE")` (ISO 3166-1 alpha-2) or `At(studySite)`. A version has
  at most one effectivity per scope.
- **Lifecycle.** `DeclareEffectivity` starts it `Pending`. `ChangeEffectivityStatus` moves it to
  `RegulatoryApproved`, `EthicsApproved`, `Effective` or `Withdrawn` (terminal), from `EffectiveAt` in valid time.
  An `Effective` status is only left by withdrawal.
- **Evidence (K-TIME-5).** Each move names `ArtifactVersionId`s. `EthicsApproved` takes an ethics approval,
  `RegulatoryApproved` a regulatory approval or CTA acknowledgement, and `Effective` needs at least one of the three
  and an approved version. Evidence must belong to the version's study or be tenant-wide.
- **Resolution.** For each version the most specific effectivity covering the site decides (study site, then
  country, then study), as of any `(valid, known)` pair. `IEffectivityReader.EffectiveAtAsync` returns the versions
  in force at a study site, highest number first.
- **Activation (K-TIME-4).** `ActivateStudySite` refuses a study site where no version is in force when it is
  recorded, and raises `StudySiteActivated` with the versions that were.
- **Tables.** `kernel.effectivities` checks the scope columns and holds one row per version and scope.
  `kernel.effectivity_status_periods` carries the bitemporal guards.
- **Not yet:** the other start-up conditions of §7.1 (contracts, essential documents, site initiation) and the
  authority evaluator's StudySite condition.

## Search (ADR-2484)

`IKernelSearch.SearchAsync(tenant, new SearchRequest("blood press", Kinds: [NodeKinds.Concept]))` returns `SearchHit`s
(NodeRef, title, code, detail), exact code matches first. Register `AddKernelSearch()` (Application) and
`AddKernelSearchStore()` (Persistence); search asks `IAuthorizationProbe` before searching a source that needs a reveal
capability.

Search queries the source tables with PostgreSQL full-text search (`simple` configuration, prefix words) through a
tenant-bound context, so RLS applies. A row whose code equals the search text (ignoring case) always matches, even
when the parser indexes the code differently from the search words: `CADENCE-101` is indexed as `cadence` and `-101`,
which the words `cadence` and `101` never reach (#5487). To make a new table searchable, add a `KernelSearchSource` to
`KernelSearchStore.BuiltInSources` (code and title columns of alias `t`) and a migration calling
`KernelSearchSql.CreateIndexes`. `SearchExpressionsUseTheIndexes` fails if the query cannot use the index.

## Lineage (ADR-2471)

Lineage is written by the command pipeline and read through `IKernelLineage` (`Cadence.Kernel.Lineage`).

**Writing.** In a handler, declare each object the aggregate was produced from:

```csharp
context.Work.Add(mapping);
context.Work.LinkInput(NodeRef.For(command.Concept.ConceptId, command.Concept.Version), LineageEdgeKind.DefinedBy);
```

- The edge runs from the input to the aggregate at the version the commit stamps (or to the specific changed aggregate
  named by `target` in multi-aggregate commits such as form saves). It is written in the command's transaction,
  together with a `lineage_nodes` row for that version.
- Updates get a `Supersedes` edge from the previous version automatically.
- Mark an aggregate `[DerivedObject]` when every version must have inputs. The commit then throws `K-LIN-1` if a
  handler declared none.
- Inputs must be pinned (`@version`).
- An object made of parts (a protocol version's design elements) declares each with `IncludePart(part, newVersion)`
  and the edges between them with `LinkPart`. The pipeline writes a node for each new part, `GovernedBy` from the
  aggregate to every part, and each part edge, which must touch a new part.

**Reading.**

```csharp
var graph = await lineage.TraceAsync(tenant, reader, new LineageQuery(node, LineageDirection.Backward, Depth: 10));
var dossier = await lineage.DossierAsync(tenant, reader, node);
```

- `LineageQuery` also takes edge kinds and `ValidAt`/`KnownAt`.
- Each result node has a distance from the start. `Truncated` is set when the depth or the 10,000-edge limit cut the
  walk.
- Objects the reader may not see (the host's `INodeVisibility`) come back masked: no `Ref`, a `masked:n` handle and a
  `MaskReason`. They are never dropped.

**Dependents and impact.** `DependentsAsync(tenant, reader, new DependentsQuery(node))` returns what depends on an object,
grouped by kind with counts (Canvas D2). Counts are of objects, not versions. Masked objects are counted like any
other, so every screen and every reader gets the same number. `IAmendmentImpact.ComputeAsync(tenant, reader,
amendedVersion)` is K-PROT-2 as one caller of it: the dependents of the parent version, not walking through the fork
itself, and entering only the protocol elements the amendment changes or removes (ADR-2474). `ChangedElements` lists
those elements from the structural diff, and `Schedule` the Schedule of Activities directives (ADR-2475). Forking a protocol version links the fork to its parent (`DerivedFrom`).

## Activity stream (ADR-2485)

`IActivityStream.ReadAsync(tenant, new ActivityQuery(Scope: NodeRef.For(study), Families: [EventFamily.Trust]))`
returns an `ActivityPage` of `ActivityEntry`s, newest first, with a cursor (`Next`) to pass as `Before`. Each entry
names its event type, family, actor, time and subject NodeRef. When the reader may not see the subject, the entry
stays in place with `Masked` set and `Subject` null.

- Register `AddKernelActivityStream()` (Application), `AddKernelActivityProjection()` (Persistence) and an
  `INodeVisibility`.
- The projector copies outbox envelopes into `kernel.activity_entries` every `KernelBackgroundOptions.ActivityInterval`,
  with the subject's zoom path as its scope. Run it (`runProjector: true`) in one host per deployment: today that is
  the kernel API host, which serves the stream as `GET /api/v1/kernel/activity` (ADR-2540 §4.7).
- A new kernel event type needs a family in `KernelEventFamilies`; `EveryKernelEventTypeHasAFamily` fails otherwise. Module events prefixed with `execution.` classify as `EventFamily.Execution` without polluting `KernelEventFamilies.Types`.

## Simulation (ADR-2480)

`ISimulationEngine.SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(draft)))`
compares a protocol version with the version it was forked from and returns a `SimulationResult`. It never writes
(K-SIM-1).

- `SimulationCandidate.UnsavedProtocolVersion(baseVersion, content)` compares content that is not saved anywhere with a
  version whose content is frozen. The content must read as a protocol design (ADR-2472), as a stored draft's must;
  otherwise the run fails with `K.Simulation.ContentInvalid` naming the design problems.
- The result names its scope (the study), the pinned as-of, both versions with their content hashes, the engine
  versions, an input hash and a result hash (K-SIM-3).
- `StructuralDiff` lists the elements added, removed or changed. An element is any JSON object with a string `id`
  (the USDM convention), and a changed element lists the JSON Pointers of its changed values.
- The engine checks `simulation.run` at the study through `IAuthorizationProbe`. When the host's `INodeVisibility`
  hides the study or either version, the diff is withheld in `Masked` and no content hash is returned (K-SIM-4).
- Register `AddKernelSimulation()` (Application) and `AddKernelSimulationInputs()` (Persistence).
- `SimulationBoundaryTests` keeps `Cadence.Kernel.Application.Simulation` away from the command sender, the unit of
  work and persistence, and keeps its persistence reads from calling `SaveChanges`.
- When something changed, `Dependents` lists what depends on the base version at the as-of, from the same
  dependents read as amendment impact (the stored candidate itself is left out, and only changed or removed protocol
  elements are followed; dependents engine version 2).
- `ScheduleChanges` lists the timed activities added to, removed from or moved on the expected schedule, with the
  planned visit before and after (K-TIME-7, ADR-2487). It is masked with the diff.
- Participant-level schedule impact, projection changes, the validation delta and saving a result as a Proposal
  come later, each when the function behind it exists (#4866).

## Expected schedule (ADR-2487)

`ExpectedSchedule` (`Cadence.Kernel.Protocol.Design`) is the pure schedule function of spec K-TIME-7. It reads no
clock and stores nothing.

- `Plan(design)` returns the timed planned activities as `PlannedVisit`s, ordered by study day, epoch order and
  activity id, normalizing any `Day 0` timing to `StudyDay 1`. Activities without a timing are event-driven and left
  out.
- `NormalizeStudyDay(studyDay)` maps `0` to `1` while keeping all other positive and negative study days intact, and
  `OffsetOfStudyDay(studyDay)` returns the signed calendar-day offset from `Day 1` (`0` for `0` or `1`, `studyDay - 1`
  for positive days, `studyDay` for negative pre-baseline days). `ScheduleOfActivitiesDiff`, `SimulationEngine`,
  `MatrixGapAnalyzer` and USDM `P0D` `Fixed Reference` imports share these helpers so legacy `Day 0` designs and SDTM
  `Day 1` designs never produce false epoch-regression warnings or schedule diffs, while compiled `ScheduleOfActivities`
  columns preserve raw `StudyDay` values.
- `Compute(design, referenceStart, asOf)` places them on the calendar: target date, window start and end, and
  `VisitWindowStatus` at `asOf`. Pass site-calendar dates.
- `DateOfStudyDay(referenceStart, day)` uses `OffsetOfStudyDay` for the SDTM `--DY` convention: day 1 is the reference
  start, day -1 the day before, and a day-0 timing is day 1.
- `Assess(expectedVisit, actualDate)` measures a visit that happened: calendar days from the target, `Early`,
  `InWindow` or `Late`, and days outside the window (ADR-2507). It has no severity; the study's Quality rules decide
  how important an out-of-window visit is.

## Indicators and the study Pulse (ADR-2488)

Pulse metrics, attention rules, KRIs and QTLs are versioned indicators in released, study-pinned `IndicatorSet`
library items, evaluated in `Cadence.Rules` ([docs/kernel/INDICATORS.md](../../docs/kernel/INDICATORS.md)).

- A population is read by an `IIndicatorPopulationSource` (`Cadence.Kernel.Indicators`) registered under its name.
  `AddKernelPersistence` registers `enrollments` (status at the as-of, K-TIME-2) and
  `AddKernelValidationPersistence` registers `validation_findings`. Applications register their own the same way.
- `StudyIndicators` (`Cadence.Rules`) reads a study's populations and leaves out the records the reader may not see
  (`INodeVisibility`) before counting; such a value is marked partial with the mask reasons and no hidden count.

## Proposals and automation agents (ADR-2489)

A `Proposal` is what an AI or a person suggests for a study. It has no effect on kernel state: nothing reads it to
decide anything else, and approving it does not run its plan (K-SIM-5).

- **Content.** `Propose` records the intent (1 to 2,000 characters), the plan (an `ArtifactVersionId` of the study or
  tenant-wide), 0 to 200 distinct affected `NodeRef`s, an optional simulation result hash, 1 to 20 requested
  capabilities (each an Execute or Approve command of `KernelCommandCatalog.Kernel`), and an expiry at most 90 days
  out. The acting actor is the proposer. An automation agent must name its `ModelId`, and any other actor must not
  (K-PROV-3). `ContentHash` covers all of it, and the database fixes those columns once written.
- **Lifecycle.** `Open → Approved | Rejected | Withdrawn`. An open proposal past `ExpiresAt` reads as `Expired`
  (`Proposal.StatusAt(now)`); nothing stores expiry, and an expired proposal can no longer be decided.
  - `ApproveProposal` (`proposal.approve`, Approve) names an `Approved` attestation by the acting person over
    `(proposal, id, ContentHash)`. The proposer cannot approve (`K.Proposal.SelfApproval`).
  - `RejectProposal` (`proposal.reject`, Approve) takes its reason from the reason for change.
  - `WithdrawProposal` (`proposal.withdraw`, Propose) is for the proposer only (`K.Proposal.NotProposer`).
  - The events are `ProposalCreated`, `ProposalApproved`, `ProposalRejected`, `ProposalWithdrawn` and `ProposalExecuted`, in the
    Intelligence family.
- **Agents (K-AI-1, K-AI-3, K-AI-6).** `Actor.AutomationAgent("name")` builds an `agent:name` actor, and
  `.ActingFor(person)` names the one person it acts for (ADR-2632). An agent acting for no one is refused everything
  (`actor.requester`) and sees nothing; otherwise its own grants and the person's must both permit a capability, and
  `GrantNodeVisibility` shows it only what both may see. Grants name the agent without a requester
  (`K.Authority.GrantTargetInvalid` otherwise), and only the same agent acting for the same person withdraws its
  proposal. The evaluator denies an
  agent every Execute and Approve command (`actor.agent_verb`), including approving, rejecting and signing
  attestations, so an agent can only read, propose and withdraw its own proposals. Proposing needs a grant of
  `proposal.create` in the study, and the plan and every affected object must be visible to the proposer
  (`K.Proposal.NotVisible`). A proposal is on its study's scope path, so reads, `INodeVisibility` and navigation
  treat it as a study object.
- **Provenance and lineage (K-PROV-3).** The pipeline records the agent as the actor of `Propose`, and the proposal
  keeps the model and the plan. The approval keeps the person and the attestation. Lineage records the proposal as
  `DerivedFrom` its plan version and `DefinedBy` each affected reference that names a version. An unversioned affected
  reference is kept on the proposal only.
- **Attestations.** `Attest` accepts target kind `proposal` through `ProposalAttestationCheck`: the proposal must exist
  and the hash must be its content hash.
- **Execution (K-AI-2, ADR-2632).** A command executes an approved proposal by naming it in
  `CommandMetadata.Proposal`. After authorizing the command as the acting person, the pipeline refuses it unless the
  proposal exists (`K.Proposal.ExecutionUnknown`), is approved (`NotApproved`) and unexpired (`ExecutionExpired`), was
  approved by the acting person (`NotApprover`), requested the command's capability (`CapabilityNotRequested`), and the
  command's object is in the proposal's study or in none (`OutsideStudy`). The commit records `proposal_id` on the
  provenance activity, raises `ProposalExecuted` in that activity, and writes an `ExecutedUnder` lineage edge from the
  approved proposal version to the object version written. One approved proposal may cover several commands until it
  expires; the request hash covers the proposal.
- **Reads (ADR-2635).** `IProposalReader` (`Cadence.Kernel.Intelligence`, `KernelProposalReader` in persistence) lists
  a study's proposals newest first, filtered by status with `Expired` read from the clock, and reads one with its plan's
  title and hash. The kernel API serves them and rejection; approval over HTTP waits on the signed-approval route
  (#5552).
- **Not yet:** standing grants for agents (K-AI-7, ADR-2632 §4.3), the `QueryStatus.DraftAi` migration and a proposals
  work-queue source (ADR-2489 §4.5).

## Commercial subscriptions and entitlements (ADR-2547, ADR-2604)

`Subscription` (`kernel.subscriptions`) and `ProductEntitlement` (`kernel.product_entitlements`) track commercial workspace packaging (`Individual`, `Select`, `Complete` and product families `Blueprint`, `Capture`, `Orbit`, `Engage`, `Allocate`, `Vigil`, `Lens`, `Assure`) separately from `Tenant` and clinical `Grant`s ([ADR-2547](../../docs/adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md), [ADR-2604](../../docs/adr/2026-11-21-signed-gateway-expiry-guard-and-scoped-trial-upgrades.md)). Commercial state never confers clinical visibility or bypasses `AuthorityEvaluator`. Switching tiers during an active trial (`SwitchTrialTier`, `POST /api/v1/kernel/entitlements/tier`) updates enabled product families in place while keeping `SubscriptionStatus.Trialing` and preserving the original 30-day `TrialEndsAt` window. Transitioning a `Trialing` or `Expired` workspace to `Active` (`ActivateSubscription`, `POST /api/v1/kernel/entitlements/activate`) requires validated `SubscriptionScoping` (`study_phase`, `study_duration_months`, `countries`, `site_count`, `participant_count`, `active_study_count`, and purchase-order/quote `billing_confirmation`; `422 K.Commercial.ScopingInvalid` otherwise). When a trial subscription expires, `KernelCommandPipeline` and `ExpiredSubscriptionWriteGuardMiddleware` refuse non-baseline workspace mutations with `403 K.Subscription.Expired` while keeping `[BaselineKernelCommand]` commands (`Attest`, `ExportStudySnapshot`, `ExportEnrollmentCasebook`, authority governance, IdentityVault privacy actions, scoped `ActivateSubscription`) and regulated reads and exports unrestricted.

## Migrations

```bash
pnpm cadence migrations add kernel <MigrationName>
pnpm cadence migrations script kernel   # writes TestResults/migrations/kernel.sql for DBA review
```

After adding a migration, apply it to a real PostgreSQL before opening the PR (no Docker needed; any reachable
server works, e.g. an apt-installed PostgreSQL 18, docs/TESTING.md):

```bash
CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres" \
  dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Kernel.Integration"
```

The fixture (`tests/Cadence.IntegrationTests/Kernel/KernelPostgresFixture.cs`) creates a throwaway database, applies the kernel
migrations as the superuser, creates an application login role with `KernelDatabase.GrantAppRoleSql`, wires the
command pipeline for that role as a host does (with a settable actor and authorizer), and drops the database and role
afterwards, so every pipeline test runs under Row-Level Security. `CreateContext(tenant)` reads as that role;
`CreateAdminContext()` reads every tenant as the superuser. `SeedAsync` inserts rows directly for constraint tests;
everything else goes through commands. Without `CADENCE_TEST_POSTGRES` these tests are
reported as skipped, never as passed.
