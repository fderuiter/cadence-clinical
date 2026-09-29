# Cadence Clinical Kernel

The core clinical domain modular monolith (`Cadence.Kernel.*`) providing the unified, shared clinical truth for the Cadence Clinical Research Software Platform.

## Architecture

As formalized in [ADR-2244](../../docs/adr/2026-09-29-clinical-kernel-modular-monolith-architecture.md) and specified in [`CLINICAL_KERNEL_SPECIFICATION.md`](../../docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md), the Clinical Kernel consolidates:
- **Identity & Ownership**: Tenant, Organization, Person, Study, Site, StudySite
- **Clinical Subjects**: Participant, Enrollment, Cohort
- **Clinical Truth**: Observations, Assertions, Sources, Provenance
- **Authority**: Actors, Capabilities, Delegations, Attestations (21 CFR Part 11 electronic signatures)
- **Temporal Integrity**: PostgreSQL native bitemporal intervals (`valid_period`, `recorded_period`) via `tstzrange` and `gist` (ADR-2246)
- **Audit Ledger**: Monotonic cryptographic Merkle chain

## Assemblies

Dependencies point inward only (spec §2.4, enforced by `tests/Architecture/KernelLayeringTests.cs`):

- `Cadence.Kernel.Abstractions`: strongly-typed UUIDv7 IDs (`TenantId`, `StudyId`, `ParticipantId`, …), `IClock` and serialization contexts. No EF Core, ASP.NET Core or rendering. Application modules reference only this assembly (K-XS-2).
- `Cadence.Kernel.Domain`: kernel aggregates, state machines and temporal logic. Pure: no EF Core, ASP.NET Core or Npgsql.
- `Cadence.Kernel.Application`: command pipeline, authorization and policy evaluation (#4779, #4781).
- `Cadence.Kernel.Persistence`: `KernelDbContext` over the single `kernel` schema (ADR-2247), ID value converters, GxP audit interceptor and migrations. Referenced only by `Cadence.Kernel.Api` and the host.
- `Cadence.Kernel.Api`: Minimal API route group (`/api/v1/kernel`) that slices add endpoints to.

Rendering lives in `packages/templating` (`Cadence.Templating`), outside the kernel.

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
migrations through `AddKernelPersistence`, and drops it afterwards. Without `CADENCE_TEST_POSTGRES` these tests are
reported as skipped, never as passed.
