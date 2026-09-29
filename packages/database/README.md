# Database Package

## Overview

`Cadence.Database` is the shared EF Core 10 + Npgsql persistence layer for the Cadence services: DbContext
registration, the GxP audit `SaveChangesInterceptor`, shared entity configurations, readiness health checks and
migration application.

---

## Public APIs / Exports

- `AddCadencePostgresDbContext<TContext>()`: registers the service's DbContext against PostgreSQL in its own
  module schema, with transient-failure retries, a readiness health check (tag `ready`, served at `/healthz/ready`)
  and migrations.
- `WithModuleSchema(connectionString, schema)`: scopes a connection to one module schema (search path
  `<schema>,public`, ADR-2256). `ModuleSchemaFor<TContext>()` derives the default name (`CtmsDbContext` is `ctms`).
- `ResolvePostgresConnectionString(...)`: reads `ConnectionStrings:cadence`, then `POSTGRES_CONNECTION_STRING`.
  Only the Development environment falls back to the local compose database; elsewhere a missing value fails startup.
- `AddCadenceMigrations<TContext>()`: applies migrations according to the `Database` configuration section.
- `AddCadenceDbContext<TContext>()`: `AddDbContext` plus the compiled EF model when one exists.
- `GxPAuditInterceptor`: stamps `CreatedAt`, `CreatedBy`, `ReasonForChange` and `VersionIndex` (21 CFR Part 11).

---

## Applying migrations

Each service owns its migrations under `apps/<service>/src/Infrastructure/Persistence/Migrations/` and its own schema
in the shared `cadence` database. The layout and the operations runbook are in
[docs/OPERATIONAL_DATABASE.md](../../docs/OPERATIONAL_DATABASE.md).

| Setting | Behaviour | Use for |
| --- | --- | --- |
| `Database__MigrateOnStartup=true` | applies pending migrations before the web server starts; a failure stops startup | local compose, single-instance environments |
| `Database__MigrateOnly=true` | applies pending migrations, then exits with code 0 (non-zero on failure) | a deployment job or init container that runs before new replicas roll out |
| neither (default) | never touches the schema | multi-replica production, where the job above owns migrations |

For DBA review and GxP qualification, generate an idempotent script instead:

```bash
scripts/manage_migrations.sh script <service>   # or scripts/manage_migrations.ps1
```

`tests/Packages/MigrationUnitTests.cs` fails when a DbContext model has changes that no migration captures, which
would otherwise surface as `PendingModelChangesWarning` at deploy time.

---

## Local Development & Testing

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages"
```
