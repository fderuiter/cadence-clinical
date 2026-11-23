# Database Layout and Migrations Runbook

How Cadence persistence modules store data in PostgreSQL, how schema changes reach an environment, and what to do when
they fail. The decisions behind it are [ADR-2247](adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md)
(one database, schema per module), [ADR-2447](adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)
(how the schemas and migrations are applied), [ADR-2601](adr/2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md)
(composition-host module schemas), [ADR-2498](adr/2026-11-22-unify-cli-database-workflows-and-design-time-fallback.md)
(unified `cadence db` workflows and credential-free offline design-time fallback), and
[ADR-2536](adr/2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md)
(fail-closed 18-module migration verification, untracked SQL review scripts, and count-free schema documentation).

## Layout

Every host and composition-host module connects to the same database, `cadence`, through `ConnectionStrings__cadence`
(or the legacy `POSTGRES_CONNECTION_STRING`; see [ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md)). Each of the 18
persistence modules (`PersistenceModuleRegistry` / `PersistenceModuleCatalog`) owns its own PostgreSQL schema and EF
Core migrations history table:

| CLI Module                             | Schema          | DbContext                  | Project                                                                            |
| -------------------------------------- | --------------- | -------------------------- | ---------------------------------------------------------------------------------- |
| `kernel`                               | `kernel`        | `KernelDbContext`          | `packages/kernel/src/Cadence.Kernel.Persistence/Cadence.Kernel.Persistence.csproj` |
| `intelligence`                         | `intelligence`  | `IntelligenceDbContext`    | `apps/intelligence/src/Cadence.Modules.Intelligence.csproj`                        |
| `execution-module` (`executionmodule`) | `execution`     | `ExecutionModuleDbContext` | `apps/execution/module/Cadence.Modules.Execution.csproj`                           |
| `execution`                            | `edc`           | `ExecutionDbContext`       | `apps/execution/src/Cadence.Apps.Execution.csproj`                                 |
| `ctms`                                 | `ctms`          | `CtmsDbContext`            | `apps/ctms/src/Cadence.Apps.Ctms.csproj`                                           |
| `econsent`                             | `econsent`      | `EconsentDbContext`        | `apps/econsent/src/Cadence.Apps.Econsent.csproj`                                   |
| `eisf`                                 | `eisf`          | `EisfDbContext`            | `apps/eisf/src/Cadence.Apps.Eisf.csproj`                                           |
| `etmf`                                 | `etmf`          | `EtmfDbContext`            | `apps/etmf/src/Cadence.Apps.Etmf.csproj`                                           |
| `fileshare`                            | `fileshare`     | `FileshareDbContext`       | `apps/fileshare/src/Cadence.Apps.Fileshare.csproj`                                 |
| `imaging`                              | `imaging`       | `ImagingDbContext`         | `apps/imaging/src/Cadence.Apps.Imaging.csproj`                                     |
| `interop`                              | `interop`       | `InteropDbContext`         | `apps/interop/src/Cadence.Apps.Interop.csproj`                                     |
| `knowledge`                            | `knowledge`     | `KnowledgeDbContext`       | `apps/knowledge/src/Cadence.Apps.Knowledge.csproj`                                 |
| `notifications`                        | `notifications` | `NotificationsDbContext`   | `apps/notifications/src/Cadence.Apps.Notifications.csproj`                         |
| `org`                                  | `org`           | `OrgDbContext`             | `apps/org/src/Cadence.Apps.Org.csproj`                                             |
| `payments`                             | `payments`      | `PaymentsDbContext`        | `apps/payments/src/Cadence.Apps.Payments.csproj`                                   |
| `quality`                              | `quality`       | `QualityDbContext`         | `apps/quality/src/Cadence.Apps.Quality.csproj`                                     |
| `safety`                               | `safety`        | `SafetyDbContext`          | `apps/safety/src/Cadence.Apps.Safety.csproj`                                       |
| `tickets`                              | `tickets`       | `TicketsDbContext`         | `apps/tickets/src/Cadence.Apps.Tickets.csproj`                                     |

`kernel` (`KernelDbContext`, plus `identity_vault` sealed privacy tables) and the two host-mounted kernel modules
(`intelligence` and `execution-module`) qualify their tables explicitly and store `__ef_migrations_history` inside their
schema while running inside `apps/kernel-api`. Standalone services apply their schema by setting the Npgsql search path
to `<schema>,public` (`CadenceDatabaseExtensions.WithModuleSchema`), so unqualified tables and `__EFMigrationsHistory`
live inside `<schema>`. `public` holds only PostgreSQL extensions such as `btree_gist` (never application tables). When
you query a standalone service schema by hand, set the search path yourself: `SET search_path TO ctms, public;`.

## Applying migrations

| Environment                    | Setting                                                                                                       | What happens                                                                                                                            |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Local developer CLI            | `pnpm cadence db migrate [<module\|all>]` (with `ConnectionStrings__cadence` or `POSTGRES_CONNECTION_STRING`) | Creates each target schema, migrates `kernel` first when running `all`, and applies pending migrations via `dotnet tool run dotnet-ef`. |
| Local compose, single instance | `Database__MigrateOnStartup=true`                                                                             | The service migrates before its web server starts; if migration fails, it does not start.                                               |
| Multi-replica deployment       | `Database__MigrateOnly=true` on a one-off job or init container, before the rollout; unset on the replicas    | The job migrates and exits 0 (non-zero on failure). Replicas never change the schema.                                                   |
| Regulated release              | neither; a DBA applies a reviewed script                                                                      | See "Idempotent scripts" below.                                                                                                         |

A migrate-only job runs the normal service image with the same connection string and secrets as the service.
Services refuse to start without their required settings (`GATEWAY_SECRET`, `AUDIT_LOG_SECRET_KEY` and, for
execution, `DemographicsEncryption__KeyBase64`; see [ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md)):

```bash
docker run --rm \
  -e ConnectionStrings__cadence="Host=db;Database=cadence;Username=...;Password=..." \
  -e GATEWAY_SECRET=... \
  -e AUDIT_LOG_SECRET_KEY=... \
  -e Database__MigrateOnly=true \
  cadence/ctms:<version>
```

Readiness (`/healthz/ready`) fails while the database is unreachable, so orchestrators hold traffic until the
database is back.

## Idempotent scripts and bundles (DBA review, GxP qualification)

```bash
pnpm cadence db script <module|all>               # writes TestResults/migrations/<module>.sql
pnpm cadence db bundle <module|all>               # writes TestResults/migrations/<module>_migration_bundle
# Shell wrappers delegate to the same CLI workflow:
scripts/manage_migrations.sh script <module|all>
pwsh scripts/manage_migrations.ps1 script <module|all>
```

Both commands run offline using a credential-free design-time placeholder (`Host=localhost;Database=cadence_design`) when
no connection string is exported ([ADR-2498](adr/2026-11-22-unify-cli-database-workflows-and-design-time-fallback.md)).
Generated SQL review scripts and bundles are written to `TestResults/migrations/` (gitignored) rather than checked into
`Migrations/` ([ADR-2536](adr/2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md)). Standalone service
scripts are unqualified; run them with `SET search_path TO <schema>, public;` after
`CREATE SCHEMA IF NOT EXISTS <schema>;`.

## Adding and verifying migrations

Ensure local tools are restored (`dotnet tool restore`, or `pnpm cadence doctor --auto-fix`), then run:

```bash
pnpm cadence db add <module> <MigrationName>      # auto-formats generated C# files for IDE0161 and whitespace
pnpm cadence migrations list [module|all]
pnpm cadence migrations verify [module|all]
```

`pnpm cadence migrations verify` (and `pnpm cadence check schema-doc`) runs `Cadence.SchemaDocExporter` across all 18
persistence modules without opening a database connection, failing closed when:

- any `DbContext` reports `HasPendingModelChanges() == true` against its `*ModelSnapshot`;
- any migration lacks `[DbContext]`, `[Migration]`, `BuildTargetModel`, or its companion `.Designer.cs` file;
- two migrations in a module duplicate a `CreateTable` or `AddColumn` target.

`tests/Cadence.UnitTests/Packages/MigrationUnitTests.cs` and `tests/Cadence.UnitTests/Kernel/KernelDbContextModelTests.cs`
enforce the same `HasPendingModelChanges()` parity in the fast unit test suite across standalone services, `kernel`,
`intelligence`, and `executionmodule`. Never hand-edit a generated migration without keeping its `.Designer.cs` and
`*ModelSnapshot.cs` in sync, and never create another module's tables; each module owns only its schema.

Then run `pnpm cadence generate-schema-doc` and commit the updated [docs/schema](schema/README.md) pages, which describe
every table, column, key, index, and check constraint per module without volatile table or column count matrices.

## Troubleshooting

| Symptom                                                         | Cause                                                                     | Fix                                                                       |
| --------------------------------------------------------------- | ------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `PendingModelChangesWarning` at startup or `schema-doc` failure | Model changed without a migration                                         | Run `pnpm cadence migrations add <module> <MigrationName>`                |
| `missing its '*.Designer.cs' companion`                         | Hand-authored migration committed without EF Core `.Designer.cs` metadata | Scaffold via `pnpm cadence migrations add` so `.Designer.cs` is generated |
| `duplicates CreateTable` / `duplicates AddColumn`               | Parallel branches added migrations creating the same table or column      | Re-scaffold the later branch's migration on top of the merged snapshot    |
| `relation ... already exists`                                   | A local database created before ADR-2447 has tables in `public`           | Drop and recreate the local database (`docker compose down -v`)           |
| Service exits with `No PostgreSQL connection string configured` | Neither connection key set outside Development                            | Set `ConnectionStrings__cadence`                                          |
| `/healthz/ready` returns 503                                    | Database unreachable or credentials wrong                                 | Check connectivity from the service's network                             |
