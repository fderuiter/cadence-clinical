# Cadence Clinical Kernel

The core clinical domain modular monolith (`Cadence.Kernel.*`) providing the unified, shared clinical truth for the Cadence Clinical Research Software Platform.

## Architecture

As formalized in [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and specified in [`CLINICAL_KERNEL_SPECIFICATION.md`](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md), the Clinical Kernel consolidates:
- **Identity & Ownership**: Tenant, Organization, Person, Study, Site, StudySite
- **Executable Protocol**: ProtocolVersion lifecycle with content-hash freezing, attested approval and amendments (ADR-2452)
- **Clinical Subjects**: Participant, Enrollment, Cohort
- **Clinical Truth**: Observations, Assertions, Sources, Provenance
- **Authority**: Actors, Capabilities, Delegations, Attestations (21 CFR Part 11 electronic signatures bound to content hashes, ADR-2452)
- **Temporal Integrity**: PostgreSQL native bitemporal intervals (`valid_period`, `recorded_period`) via `tstzrange` and `gist` (ADR-2246)
- **Tenant Isolation**: `tenant_id` on every tenant-scoped table, composite `(tenant_id, id)` foreign keys and PostgreSQL Row-Level Security enabled and forced on every kernel table (ADR-2250)
- **Audit Ledger**: one SHA-256 hash chain per tenant over insert-only audit entries, written with provenance, events and outbox rows in each command's transaction (ADR-2449)

## Assemblies

Dependencies point inward only (spec §2.4, enforced by `tests/Architecture/KernelLayeringTests.cs`):

- `Cadence.Kernel.Abstractions`: strongly-typed UUIDv7 IDs (`TenantId`, `StudyId`, `ParticipantId`, …), `IClock`, serialization contexts, and the command contracts (`ICommand<T>`, `IKernelCommandSender`, `Actor`, `KernelErrors`, event records). No EF Core, ASP.NET Core or rendering. Application modules reference only this assembly (K-XS-2).
- `Cadence.Kernel.Domain`: kernel aggregates, state machines and temporal logic. Pure: no EF Core, ASP.NET Core or Npgsql.
- `Cadence.Kernel.Application`: the command pipeline (`KernelCommandPipeline`), its ports (`ICurrentActor`, `IAuthorizer`, `IKernelUnitOfWorkFactory`) and command handlers. Policy evaluation arrives with #4781.
- `Cadence.Kernel.Persistence`: `KernelDbContext` over the single `kernel` schema (ADR-2247), ID value converters, tenant-scoped contexts and Row-Level Security (`Tenancy/`), the per-command unit of work and audit chain (`KernelCommandTransaction`, `KernelAuditChainVerifier`), `KernelDatabase` (migrate, grant the app role) and migrations. Referenced only by `Cadence.Kernel.Api` and the host.
- `Cadence.Kernel.Api`: Minimal API route group (`/api/v1/kernel`) that slices add endpoints to.
- `Cadence.Standards`: reads public CDISC Library product content into a `StandardPackage` and ships checksum-verified seeds (see [its README](src/Cadence.Standards/README.md)). References only `Cadence.Kernel.Abstractions`.

Rendering lives in `packages/templating` (`Cadence.Templating`), outside the kernel.

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
  own `tenant_memberships` row.
- **Jobs that visit every tenant** (outbox relay, verify-and-seal) list tenant ids with the
  `kernel.tenant_ids()` directory function, then work through a connection scoped to each tenant in turn. The
  function returns ids only and is the only way to read across tenants.

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

| Concept | Table | Changes |
| --- | --- | --- |
| `CodeSystem` (URI, publisher, `LicenseClass`) | `code_systems` | registered once per tenant and URI |
| `CodeSystemVersion` with its entries | `code_system_versions`, `code_system_entries` | insert-only |
| `Concept` (current version, status) and its versions | `concepts`, `concept_revisions` | revisions insert-only |
| `ConceptMapping` (concept version → `Coding`, relation, status, evidence) | `concept_mappings` | review status only |

Every tenant loads the releases it adopts, public ones included; licensed content (MedDRA, WHODrug, SNOMED CT) is
never in this repository. To load a release, register its code system once, then load the publisher's file:

```csharp
await sender.SendAsync(new RegisterCodeSystem(tenant, ctId, KnownCodeSystems.NciThesaurus, "CDISC CT", "CDISC/NCI EVS",
    LicenseClass.Open, key, "Adopt CDISC CT"));
var version = await new TerminologyPackageLoader(sender).LoadAsync(
    new TerminologyRelease(tenant, ctId, CodeSystemVersionId.New(), "2026-06-27", new DateOnly(2026, 6, 27),
        "SDTM Terminology.txt", "https://evs.nci.nih.gov/ftp1/CDISC/SDTM/", publishedSha256, "Adopt SDTM CT 2026-06-27"),
    File.ReadAllBytes(path), new CdiscControlledTerminologyParser());
```

The loader refuses a file whose SHA-256 differs from the expected one; loading the same file again replays the import.
Parsers exist for NCI EVS CDISC CT text (`CdiscControlledTerminologyParser`), `ucum-essence.xml` (`UcumEssenceParser`)
and `Loinc.csv` (`LoincTableParser`); any other source can send `ImportCodeSystemVersion` with its entries.

Read through `IConceptRegistry` (registered by `AddKernelPersistence`): every lookup names a concept version
(`ConceptRef`) or a code-system version; there is no "latest". Only confirmed mappings resolve a code to a concept.

## Protocol versions and attestations (ADR-2452)

A study's protocol is a series of numbered `ProtocolVersion`s (spec §6.2). Each moves
`Draft → InReview → Approved → Superseded | Withdrawn`:

```csharp
var draft = await sender.SendAsync(new CreateProtocolDraft(tenant, study, versionId, usdmJson, key1, "First draft"), cancellationToken: ct);
var review = await sender.SendAsync(new SubmitProtocolVersionForReview(tenant, versionId, key2, "Ready for review"), cancellationToken: ct);
var target = new AttestationTarget(AttestationTarget.ProtocolVersionKind, versionId.Value, review.Value!.ContentHash!);
var signature = await sender.SendAsync(new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, target, authTime, "pwd+otp", key3, "Approve v1"), cancellationToken: ct);
await sender.SendAsync(new ApproveProtocolVersion(tenant, versionId, signature.Value, key4, "Approved by sponsor"), cancellationToken: ct);
```

- **Freezing.** `SubmitProtocolVersionForReview` freezes the content and sets `ContentHash`: `sha256:` and lower-case
  hex SHA-256 of the canonical JSON (`Cadence.Kernel.Trust.ContentHash`, the same form as `CanonicalSigner`).
  `ReturnProtocolVersionToDraft` clears the hash so the draft can be revised.
- **Signatures.** `Attest` records an append-only `Attestation`. Only a person can sign, the authentication must be at
  most five minutes old, and the target's current hash must equal the signed one.
- **Approval and withdrawal.** `ApproveProtocolVersion` and `WithdrawProtocolVersion` name an attestation with meaning
  `Approved` or `Withdrawn`, signed by the person executing the command. `IAttestationVerifier` checks it within the
  tenant; `AddKernelPersistence` registers it, and standards governance uses the same verifier.
- **Forks.** `ForkProtocolVersion` starts an amended draft from an approved version, numbered after every version in
  the study and carrying a `ProtocolAmendment`. The parent stays approved; `Superseded` is left to Effectivity (#4777).
- **Database guards.**
  - `kernel.enforce_protocol_version_lifecycle()` rejects, with SQLSTATE `KA002` and whoever connects:
    - content changes outside draft;
    - moves outside the state machine;
    - hash changes after review;
    - deletes.
  - `attestations` is insert-only (`KA001`).
- **Reading other aggregates.** A handler that needs another aggregate to decide (a parent version, the study's
  versions) reads it with `IKernelUnitOfWork.ReadAsync`/`ReadAllAsync`. These neither track nor claim it, so the
  command still changes one aggregate (K-TX-1).

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
rejects a used idempotency key (K-TX-3), runs the handler, and commits in one PostgreSQL transaction:

| Written in the same transaction | Table | Mutable? |
| --- | --- | --- |
| The one changed aggregate, with `CreatedAt`/`CreatedBy`/`ReasonForChange`/`VersionIndex` stamped | its own table | per its rules |
| One provenance activity (K-PROV-1) | `provenance_activities` | insert-only |
| Its events in the §5.2 envelope | `domain_events` | insert-only |
| One link of the tenant's hash chain | `audit_entries` (+ `audit_chain_heads`) | insert-only (head advances) |
| One outbox row per event | `outbox_messages` | delivery bookkeeping only |
| The stored result for the key | `idempotency_records` | insert-only |

Insert-only tables (these, plus `audit_chain_seals`, the terminology tables and `attestations`) reject `UPDATE`,
`DELETE` and `TRUNCATE` with SQLSTATE `KA001`. `KernelAuditChainVerifier` re-derives a tenant's chain and reports the
first broken link.

If the handler refuses or the commit conflicts, the pipeline checks the idempotency key again before returning the
error: a concurrent send with the same key may have committed in between, and its stored result is returned as a
replay.

### After the commit: relay and seals

`AddKernelBackgroundJobs()` runs two jobs in the host (call it in exactly one host per deployment):

- **Outbox relay** (`KernelOutboxRelay`): visits each tenant in turn and delivers up to `RelayBatchSize` of its
  events' envelopes to the host's `IKernelEventPublisher`, at least once, oldest first per tenant. A failed delivery is retried after 2^attempts seconds (capped at one hour)
  with the error in `outbox_messages.last_error`, and holds back only its own tenant's later events. Subscribers
  deduplicate on `EventId`.
- **Verify and seal** (`KernelAuditChainSealer`, hourly): verifies every tenant's chain and seals each intact one at
  its head in `audit_chain_seals`, as `svc:audit-sealer`. A broken chain is logged as critical and never sealed. A
  chain later cut back below a seal fails verification.

Intervals and batch size are set through `KernelBackgroundOptions`.

To add a command:

1. Declare the command record in `Cadence.Kernel.Abstractions/Commands` implementing `ICommand<TResult>`, its events
   with `[KernelEvent("Name", 1)]` in `Events/`, and any new errors as `public static readonly KernelError` fields
   with a `K.<Area>.<Name>` code.
2. Implement `ICommandHandler<TCommand, TResult>` in `Cadence.Kernel.Application/Commands`: load or add **one**
   aggregate through `context.Work` (a second throws, K-TX-1), decide, `Raise` events, return a `CommandResult`.
   Never save.
3. Register the handler in `AddKernelCommandPipeline()`.
4. Test it through the pipeline in `tests/Kernel/Integration` (see `KernelCommandPipelinePostgresTests`).

A host registers `AddKernelPersistence(appRoleConnectionString)`, `AddKernelCommandPipeline()`, an `IClock`, an
`ICurrentActor`, an `IAuthorizer` and logging; the container fails to resolve the sender if any is missing. The host
that runs `AddKernelBackgroundJobs()` also registers an `IKernelEventPublisher`.

## Object references and navigation (ADR-2481)

Every kernel object is referenced publicly as a `NodeRef` (`Cadence.Kernel.References`): `kind:id` or
`kind:id@version`, for example `study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3`. `NodeRef.Parse` accepts only the
canonical string, and `NodeRef.For(studySiteId)` builds one from a typed id. When you add a strongly-typed identifier to
`Cadence.Kernel.Abstractions`, register its kind in `NodeKinds`; `NodeRefTests` fails until you do.

Every event envelope carries `Subject`, the NodeRef of the aggregate the command changed at its new version.

`IKernelNavigator` (`AddKernelNavigation()` in Application, `AddKernelScopeGraph()` in Persistence) resolves a NodeRef
to a `NodeSummary`, computes its zoom path (`tenant → study → country → study_site → participant`, or
`tenant → organization → site`) and lists its children with counts. A participant with no responsible study site
sits directly under its study (`ZoomRules.ParticipantParent`). When a slice maps a new kind, add its case to
`KernelScopeGraph` with the kind's zoom parent.

## Standards Registry and MDR (ADR-2460)

Standard definitions and the governed library are tenant-scoped kernel data (spec §6.3, #4809). Code lives under
`Standards/` in each layer; the read port `IStandardsRegistry` and the commands are in
`Cadence.Kernel.Abstractions` (`Cadence.Kernel.Standards` namespace), so applications can use them.

| Command | Does |
| --- | --- |
| `RegisterStandardVersion` | Registers a `StandardPackage` (datasets, variables, value-level definitions, CT bindings) once per `(tenant, code, version)`; optional `ExpectedContentHash` pins the content |
| `ApproveStandardVersion`, `DeprecateStandardVersion` | Draft → Approved → Deprecated, each needing an attestation (`Approved`, `Responsibility`) |
| `DraftLibraryItem` | Adds the next version of a keyed library item (collection spec, mapping spec, rule pack, codelist subset) |
| `ApproveLibraryItem`, `DeprecateLibraryItem` | Same governance as standard versions |
| `DeclareStudyStandards` | Sets a study's approved standard versions, CT versions and library pins (one profile per study, revised in place) |

- The content hash (`sha256:` + 64 hex) is over canonical JSON: sorted keys, datasets and variables in ordinal order.
  `IStandardsRegistry.GetStandardPackageAsync` returns the package in that order, so it re-hashes to the stored hash.
- Deprecating a standard version or library item stops new pins; it never changes a study that already pinned it.
- Dataset, variable, value-level and terminology rows are insert-only (`KA001`); the identity and content columns of
  `standard_versions` and `library_items` are immutable (`KS001`, `ImmutableColumnsSql`).
- Library content is validated per kind by any registered `ILibraryContentValidator`; kinds without one accept any
  JSON object. Rule packs and mapping specifications add their validators with #4811 and #4810.
- CT bindings are `CodeSystemVersionRef(code system, version)` until the Concept Registry (#4807) resolves them.

A host that uses the registry also calls `AddKernelStandards()` (handlers) and `AddKernelStandardsPersistence()`
(`IStandardsRegistry`). Approval and deprecation name an attestation recorded with `Attest` on the `standard_version`
or `library_item` target, which the kernel's `IAttestationVerifier` checks.
Load public content with `StandardSeeds.LoadAll()` or `CdiscLibraryProductReader.Read(...)` and send each package in
a `RegisterStandardVersion` command.

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
`IAuthorizationEvaluator` (the authority slice, #4781), so a dry run gets exactly the decision the command would.
`CanIAsync(tenant, capability, resource)` answers for the current actor, and `GetCommandsAsync(tenant, resource)`
lists the commands offered on an object with their decisions and explanations. Dry runs write nothing to the audit
chain; they are logged as event `2483 AuthorizationDryRun`.

## Search (ADR-2484)

`IKernelSearch.SearchAsync(tenant, new SearchRequest("blood press", Kinds: [NodeKinds.Concept]))` returns `SearchHit`s
(NodeRef, title, code, detail), exact code matches first. Register `AddKernelSearch()` (Application) and
`AddKernelSearchStore()` (Persistence); search asks `IAuthorizationProbe` before searching a source that needs a reveal
capability.

Search queries the source tables with PostgreSQL full-text search (`simple` configuration, prefix words) through a
tenant-bound context, so RLS applies. To make a new table searchable, add a `KernelSearchSource` to
`KernelSearchStore.BuiltInSources` (code and title columns of alias `t`) and a migration calling
`KernelSearchSql.CreateIndexes`. `SearchExpressionsUseTheIndexes` fails if the query cannot use the index.

## Migrations

```bash
scripts/manage_migrations.sh add kernel <MigrationName>
scripts/manage_migrations.sh script kernel   # idempotent SQL for DBA review
```

After adding a migration, apply it to a real PostgreSQL before opening the PR (no Docker needed; any reachable
server works, e.g. an apt-installed PostgreSQL 16):

```bash
CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres" \
  dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Kernel.Integration"
```

The fixture (`tests/Kernel/Integration/KernelPostgresFixture.cs`) creates a throwaway database, applies the kernel
migrations as the superuser, creates an application login role with `KernelDatabase.GrantAppRoleSql`, wires the
command pipeline for that role as a host does (with a settable actor and authorizer), and drops the database and role
afterwards, so every pipeline test runs under Row-Level Security. `CreateContext(tenant)` reads as that role;
`CreateAdminContext()` reads every tenant as the superuser. `SeedAsync` inserts rows directly for constraint tests;
everything else goes through commands. Without `CADENCE_TEST_POSTGRES` these tests are
reported as skipped, never as passed.
