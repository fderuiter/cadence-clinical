# Database Layout and Migrations Runbook

How Cadence services store data in PostgreSQL, how schema changes reach an environment, and what to do when they
fail. The decisions behind it are [ADR-2247](adr/2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md)
(one database, schema per module) and [ADR-2447](adr/2026-09-29-module-schemas-via-search-path-and-migration-deployment.md)
(how the schemas and migrations are applied).

## Layout

Every service connects to the same database, `cadence`, through `ConnectionStrings__cadence` (or the legacy
`POSTGRES_CONNECTION_STRING`; see [ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md)). Eighteen EF Core module models
own dedicated schemas in `cadence` (each with its own `__EFMigrationsHistory` table): each standalone service owns one
schema, and the `kernel` composition host (`apps/kernel-api`) composes `kernel`, `intelligence`, and `execution`
(`executionmodule`). `public` holds only PostgreSQL extensions.

<!-- database-module-schemas:begin (generated from docs/schema/README.md by `pnpm cadence generate-cli-docs`; do not edit) -->

| Module                                       | Host                         | Schema          | DbContext                  | Tables |
| -------------------------------------------- | ---------------------------- | --------------- | -------------------------- | ------ |
| [ctms](schema/ctms.md)                       | `ctms`                       | `ctms`          | `CtmsDbContext`            | 19     |
| [econsent](schema/econsent.md)               | `econsent`                   | `econsent`      | `EconsentDbContext`        | 20     |
| [eisf](schema/eisf.md)                       | `eisf`                       | `eisf`          | `EisfDbContext`            | 5      |
| [etmf](schema/etmf.md)                       | `etmf`                       | `etmf`          | `EtmfDbContext`            | 11     |
| [execution](schema/execution.md)             | `execution`                  | `edc`           | `ExecutionDbContext`       | 29     |
| [executionmodule](schema/executionmodule.md) | `kernel` (`apps/kernel-api`) | `execution`     | `ExecutionModuleDbContext` | 3      |
| [fileshare](schema/fileshare.md)             | `fileshare`                  | `fileshare`     | `FileshareDbContext`       | 6      |
| [imaging](schema/imaging.md)                 | `imaging`                    | `imaging`       | `ImagingDbContext`         | 9      |
| [intelligence](schema/intelligence.md)       | `kernel` (`apps/kernel-api`) | `intelligence`  | `IntelligenceDbContext`    | 4      |
| [interop](schema/interop.md)                 | `interop`                    | `interop`       | `InteropDbContext`         | 19     |
| [kernel](schema/kernel.md)                   | `kernel` (`apps/kernel-api`) | `kernel`        | `KernelDbContext`          | 62     |
| [knowledge](schema/knowledge.md)             | `knowledge`                  | `knowledge`     | `KnowledgeDbContext`       | 13     |
| [notifications](schema/notifications.md)     | `notifications`              | `notifications` | `NotificationsDbContext`   | 7      |
| [org](schema/org.md)                         | `org`                        | `org`           | `OrgDbContext`             | 5      |
| [payments](schema/payments.md)               | `payments`                   | `payments`      | `PaymentsDbContext`        | 17     |
| [quality](schema/quality.md)                 | `quality`                    | `quality`       | `QualityDbContext`         | 5      |
| [safety](schema/safety.md)                   | `safety`                     | `safety`        | `SafetyDbContext`          | 16     |
| [tickets](schema/tickets.md)                 | `tickets`                    | `tickets`       | `TicketsDbContext`         | 5      |

<!-- database-module-schemas:end -->

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
execution, `DemographicsEncryption__KeyBase64` and `RANDOMIZATION_ALLOCATION_KEY`; see
[ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md)):

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
