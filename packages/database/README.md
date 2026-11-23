# Database Package

## Overview

`Cadence.Database` is the shared EF Core 10 + Npgsql persistence layer for the
Cadence services: DbContext registration, the GxP audit
`SaveChangesInterceptor`, shared entity configurations, readiness health checks
and migration application.

---

## Public APIs / Exports

- `AddCadencePostgresDbContext<TContext>()`: registers the service's DbContext
  against PostgreSQL in its own module schema, with transient-failure retries, a
  readiness health check (tag `ready`, served at `/healthz/ready`) and
  migrations. For standalone tenant-isolated schemas
  (`enableStandaloneTenancy: true`, default), it also wires
  `ICurrentTenantAccessor` (`HttpContextCurrentTenantAccessor` +
  `ITenantScope`), `ITenantCodeResolver` (`PostgresKernelTenantCodeResolver` via
  `kernel.tenant_id_by_code(text)`), `StandaloneTenantConnectionInterceptor`
  (sets `cadence.tenant_id` on open and resets to `''` on close), and
  `StandaloneTenantSaveChangesInterceptor` (stamps verified `tenant_id` shadow
  columns on insert and blocks cross-tenant mutation), backing PostgreSQL
  `ENABLE` + `FORCE ROW LEVEL SECURITY` (`tenant_isolation` using
  `kernel.current_tenant_id()`,
  [ADR-2548](../../docs/adr/2026-11-20-standalone-postgres-rls-and-shared-scope-guards.md)).
  The optional `configureOptions(serviceProvider, options)` runs per scope after
  the provider is set, so a host can add a `GxPAuditInterceptor` whose actor is
  the request's verified caller (the safety service does).
- `ApplyStandaloneTenancy()` (`StandaloneTenantModelExtensions`) and
  `StandaloneTenantIsolationSql`: configure the `tenant_id uuid NOT NULL` shadow
  property across entity types (plus matching non-relational EF Core query
  filters for in-memory test hosts) and emit idempotent schema migration DDL for
  `ENABLE` / `FORCE ROW LEVEL SECURITY`.
- `WithModuleSchema(connectionString, schema)`: scopes a connection to one
  module schema (search path `<schema>,public`, ADR-2447).
  `ModuleSchemaFor<TContext>()` derives the default name (`CtmsDbContext` is
  `ctms`).
- `ResolvePostgresConnectionString(...)`: reads `ConnectionStrings:cadence`,
  then `POSTGRES_CONNECTION_STRING`. There is no fallback in any environment: a
  missing value fails startup with a message naming both keys (ADR-2260).
- `CadenceDesignTimeDbContextFactory<TDbContext>`: abstract
  `IDesignTimeDbContextFactory<TDbContext>` base class for service design-time
  factories (`dotnet ef` and `Cadence.SchemaDocExporter`). Resolves the
  connection string fail-closed via `ResolveDesignTimeConnectionString(args)`
  (ADR-2260), scopes Npgsql to the module's schema
  (`ModuleSchemaFor<TDbContext>()` by default or an explicit schema such as
  `"edc"`, ADR-2447), and invokes a typed constructor delegate, with a second
  constructor overload supplying `DesignTimeFieldEncryptor.Instance` for
  field-encrypting contexts (ADR-2494).
- `ResolveDesignTimeConnectionString(args)`: used by
  `CadenceDesignTimeDbContextFactory<TDbContext>` (`dotnet ef`). Reads
  `--connection "..."` passed after `--`, then `POSTGRES_CONNECTION_STRING`, and
  throws when neither is set.
- `AddCadenceMigrations<TContext>()`: applies migrations according to the
  `Database` configuration section.
- `AddCadenceDurableSigTokenStore<TContext>()` (`Cadence.Database.Signatures`):
  replaces the process-local e-signature `ReplayCache` with
  `PostgresSigTokenUseStore<TContext>`, which records each used token id in the
  context's `consumed_signature_tokens` table
  (`ConsumedSignatureTokenConfiguration`), so a token is single use across
  instances and restarts. Every host that maps a `RequireElectronicSignature`
  route calls it and maps the table with a migration
  ([ADR-2515](../../docs/adr/2026-10-15-step-up-tokens-are-single-use-across-instances-and-restarts.md),
  #5434).
- `AddCadenceDbContext<TContext>()`: `AddDbContext` plus the compiled EF model
  when one exists.
- `ExecuteInTransactionAsync(unitOfWork)` (`CadenceTransactions`): the only way
  to run an explicit transaction. It runs the delegate in one transaction inside
  the context's execution strategy, commits when it returns, and on a transient
  failure clears the change tracker and runs the delegate again.
  `BeginTransaction` and `BeginTransactionAsync` are banned everywhere else
  (`BannedSymbols.txt`, ADR-2523). A single `SaveChangesAsync` needs no explicit
  transaction.
- `GxPAuditInterceptor`: stamps `CreatedAt`, `CreatedBy`, `ReasonForChange` and
  `VersionIndex` (21 CFR Part 11), and is the only writer of audit entries: one
  Merkle-chained `AuditLogEntry` per changed entity, one chain per aggregate
  type (ADR-2446). Audited changes must be saved with `SaveChangesAsync`; a
  synchronous save of them throws.
- `SchemaDocumentation`: renders a DbContext's EF Core model as Markdown
  (tables, columns, keys, foreign keys, indexes, check constraints) without a
  database. `Cadence.SchemaDocExporter` uses it to generate
  [docs/schema](../../docs/schema/README.md).
- `AuditActions`: lets application code label the next save with a semantic
  action, actor and reason; the interceptor puts it on the matching entry, or
  writes an action-only entry when no row changed.

---

## Applying migrations

Each service owns its migrations under
`apps/<service>/src/Infrastructure/Persistence/Migrations/` and its own schema
in the shared `cadence` database. The layout and the operations runbook are in
[docs/OPERATIONAL_DATABASE.md](../../docs/OPERATIONAL_DATABASE.md).

| Setting                           | Behaviour                                                                        | Use for                                                                   |
| --------------------------------- | -------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `Database__MigrateOnStartup=true` | applies pending migrations before the web server starts; a failure stops startup | local compose, single-instance environments                               |
| `Database__MigrateOnly=true`      | applies pending migrations, then exits with code 0 (non-zero on failure)         | a deployment job or init container that runs before new replicas roll out |
| neither (default)                 | never touches the schema                                                         | multi-replica production, where the job above owns migrations             |

For DBA review and GxP qualification, generate an idempotent script instead:

```bash
scripts/manage_migrations.sh script <service>   # or scripts/manage_migrations.ps1
```

After changing an entity or its configuration, run
`pnpm cadence generate-schema-doc` and commit `docs/schema`; the `schema-doc`
gate in `pnpm cadence check` and CI fails while it is out of date.

`tests/Cadence.UnitTests/Packages/MigrationUnitTests.cs` fails when a DbContext
model has changes that no migration captures, which would otherwise surface as
`PendingModelChangesWarning` at deploy time.

---

## Local Development & Testing

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages"
```
