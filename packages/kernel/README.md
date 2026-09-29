# Cadence Clinical Kernel

The core clinical domain modular monolith (`Cadence.Kernel.*`) providing the unified, shared clinical truth for the Cadence Clinical Research Software Platform.

## Architecture

As formalized in [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and specified in [`CLINICAL_KERNEL_SPECIFICATION.md`](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md), the Clinical Kernel consolidates:
- **Identity & Ownership**: Tenant, Organization, Person, Study, Site, StudySite
- **Clinical Subjects**: Participant, Enrollment, Cohort
- **Clinical Truth**: Observations, Assertions, Sources, Provenance
- **Authority**: Actors, Capabilities, Delegations, Attestations (21 CFR Part 11 electronic signatures)
- **Temporal Integrity**: PostgreSQL native bitemporal intervals (`valid_period`, `recorded_period`) via `tstzrange` and `gist` (ADR-2246)
- **Audit Ledger**: one SHA-256 hash chain per tenant over insert-only audit entries, written with provenance, events and outbox rows in each command's transaction (ADR-2449)

## Assemblies

Dependencies point inward only (spec §2.4, enforced by `tests/Architecture/KernelLayeringTests.cs`):

- `Cadence.Kernel.Abstractions`: strongly-typed UUIDv7 IDs (`TenantId`, `StudyId`, `ParticipantId`, …), `IClock`, serialization contexts, and the command contracts (`ICommand<T>`, `IKernelCommandSender`, `Actor`, `KernelErrors`, event records). No EF Core, ASP.NET Core or rendering. Application modules reference only this assembly (K-XS-2).
- `Cadence.Kernel.Domain`: kernel aggregates, state machines and temporal logic. Pure: no EF Core, ASP.NET Core or Npgsql.
- `Cadence.Kernel.Application`: the command pipeline (`KernelCommandPipeline`), its ports (`ICurrentActor`, `IAuthorizer`, `IKernelUnitOfWorkFactory`) and command handlers. Policy evaluation arrives with #4781.
- `Cadence.Kernel.Persistence`: `KernelDbContext` over the single `kernel` schema (ADR-2247), ID value converters, the per-command unit of work and audit chain (`KernelCommandTransaction`, `KernelAuditChainVerifier`) and migrations. Referenced only by `Cadence.Kernel.Api` and the host.
- `Cadence.Kernel.Api`: Minimal API route group (`/api/v1/kernel`) that slices add endpoints to.

Rendering lives in `packages/templating` (`Cadence.Templating`), outside the kernel.

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
| One outbox row per event | `outbox_messages` | `dispatched_at` only |
| The stored result for the key | `idempotency_records` | insert-only |

Insert-only tables reject `UPDATE`, `DELETE` and `TRUNCATE` with SQLSTATE `KA001`. `KernelAuditChainVerifier`
re-derives a tenant's chain and reports the first broken link.

To add a command:

1. Declare the command record in `Cadence.Kernel.Abstractions/Commands` implementing `ICommand<TResult>`, its events
   with `[KernelEvent("Name", 1)]` in `Events/`, and any new errors as `public static readonly KernelError` fields
   with a `K.<Area>.<Name>` code.
2. Implement `ICommandHandler<TCommand, TResult>` in `Cadence.Kernel.Application/Commands`: load or add **one**
   aggregate through `context.Work` (a second throws, K-TX-1), decide, `Raise` events, return a `CommandResult`.
   Never save.
3. Register the handler in `AddKernelCommandPipeline()`.
4. Test it through the pipeline in `tests/Kernel/Integration` (see `KernelCommandPipelinePostgresTests`).

A host registers `AddKernelPersistence(connectionString)`, `AddKernelCommandPipeline()`, an `IClock`, an
`ICurrentActor` and an `IAuthorizer`; the container fails to resolve the sender if any is missing.

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
migrations, wires the command pipeline as a host does (with a settable actor and authorizer), and drops the database
afterwards. `SeedAsync` inserts rows directly for constraint tests; everything else goes through commands. Without `CADENCE_TEST_POSTGRES` these tests are
reported as skipped, never as passed.
