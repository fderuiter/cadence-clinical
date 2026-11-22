# Database Layout and Migrations Runbook

How Cadence services store data in PostgreSQL, how schema changes reach an environment, and what to do when they
fail. The decisions behind it are [ADR-2247](adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md)
(one database, schema per module) and [ADR-2447](adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)
(how the schemas and migrations are applied).

## Layout

Every service connects to the same database, `cadence`, through `ConnectionStrings__cadence` (or the legacy
`POSTGRES_CONNECTION_STRING`; see [ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md)). Each service owns exactly one
schema, including its own `__EFMigrationsHistory` table.

| Service       | Schema          | DbContext                |
| ------------- | --------------- | ------------------------ |
| ctms          | `ctms`          | `CtmsDbContext`          |
| econsent      | `econsent`      | `EconsentDbContext`      |
| eisf          | `eisf`          | `EisfDbContext`          |
| etmf          | `etmf`          | `EtmfDbContext`          |
| execution     | `edc`           | `ExecutionDbContext`     |
| fileshare     | `fileshare`     | `FileshareDbContext`     |
| imaging       | `imaging`       | `ImagingDbContext`       |
| interop       | `interop`       | `InteropDbContext`       |
| knowledge     | `knowledge`     | `KnowledgeDbContext`     |
| notifications | `notifications` | `NotificationsDbContext` |
| org           | `org`           | `OrgDbContext`           |
| payments      | `payments`      | `PaymentsDbContext`      |
| quality       | `quality`       | `QualityDbContext`       |
| safety        | `safety`        | `SafetyDbContext`        |
| tickets       | `tickets`       | `TicketsDbContext`       |

The kernel packages qualify their own tables with the `kernel` schema. `public` holds only extensions.

The schema is applied by setting the connection's search path to `<schema>,public`
(`CadenceDatabaseExtensions.WithModuleSchema`), so services and `dotnet ef` never need schema-qualified SQL. When you
query by hand, set it yourself: `SET search_path TO ctms, public;`.

## Applying migrations

| Environment                    | Setting                                                                                                    | What happens                                                                              |
| ------------------------------ | ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Local compose, single instance | `Database__MigrateOnStartup=true`                                                                          | The service migrates before its web server starts; if migration fails, it does not start. |
| Multi-replica deployment       | `Database__MigrateOnly=true` on a one-off job or init container, before the rollout; unset on the replicas | The job migrates and exits 0 (non-zero on failure). Replicas never change the schema.     |
| Regulated release              | neither; a DBA applies a reviewed script                                                                   | See "Idempotent scripts" below.                                                           |

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

## Idempotent scripts (DBA review, GxP qualification)

```bash
scripts/manage_migrations.sh script <service>     # or: pwsh scripts/manage_migrations.ps1 script <service>
```

The script is unqualified; run it with `SET search_path TO <schema>, public;` after `CREATE SCHEMA IF NOT EXISTS <schema>;`.

## Adding a migration

```bash
scripts/manage_migrations.sh add <service> <MigrationName>
```

Then run `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter FullyQualifiedName~MigrationUnitTests`. It fails when a
DbContext model has changes without a migration, which would otherwise break the next deployment with
`PendingModelChangesWarning`. Never hand-edit a generated migration to create another module's tables; each module
owns only its schema.

Then run `pnpm cadence generate-schema-doc` and commit the updated [docs/schema](schema/README.md) pages, which describe
every table, column, key and index per module. The `schema-doc` gate fails while they differ from the models.

## Troubleshooting

| Symptom                                                         | Cause                                                           | Fix                                                             |
| --------------------------------------------------------------- | --------------------------------------------------------------- | --------------------------------------------------------------- |
| `PendingModelChangesWarning` at startup                         | Model changed without a migration                               | Add a migration for that service                                |
| `relation ... already exists`                                   | A local database created before ADR-2447 has tables in `public` | Drop and recreate the local database (`docker compose down -v`) |
| Service exits with `No PostgreSQL connection string configured` | Neither connection key set outside Development                  | Set `ConnectionStrings__cadence`                                |
| `/healthz/ready` returns 503                                    | Database unreachable or credentials wrong                       | Check connectivity from the service's network                   |
