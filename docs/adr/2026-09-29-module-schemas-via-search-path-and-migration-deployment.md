# ADR-2447: Module Schemas via Search Path and Migration Deployment Modes

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Implements:** ADR-2247 (single database with schema-level isolation)
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

ADR-2247 decided that every module uses one PostgreSQL database (`cadence`) with schema-level isolation. When it was written, all 15 service DbContexts created their tables in `public` and shared one `__EFMigrationsHistory`. No service applied migrations. We applied every service's migrations to a real PostgreSQL 16 database and found the following:

- In one shared database, ctms, econsent and interop failed on table-name collisions. For example, interop and execution both define a `clinical_queries` table, with different columns.
- ctms and org could not migrate anywhere. #4733 removed entities without adding a migration, so `MigrateAsync` throws `PendingModelChangesWarning`.
- interop's `InitialCreate` hand-wrote `CREATE TABLE IF NOT EXISTS clinical_queries` using execution's column set, which is a workaround for the shared `public` schema. The next statement in that migration, which indexes `StudyId`, then failed.
- Nothing applied migrations at deploy time. `manage_migrations.ps1` printed `[SUCCESS]` even when `dotnet ef` failed.

## 2. Decision Drivers & Constraints

- **ADR-2247:** one database, and one schema per module.
- **Migration history:** migrations already committed must keep working. A migration that moves tables out of `public` would not help, because on a fresh install `InitialCreate` still runs first and still collides in `public` before any move could happen.
- **Deployability:** a service must never serve traffic on an out-of-date schema, and a multi-replica deployment must not race to migrate.
- **Least change to module code:** no changes to entity models, and no changes to execution's domain semantics.

## 3. Options Considered

### Option 1: `HasDefaultSchema` in every DbContext, plus regenerated migrations

- **Pros:**
  - ✅ The schema is written into the model and into every generated SQL statement.
- **Cons:**
  - ❌ Every `InitialCreate` would have to be rewritten or regenerated, including execution's four-migration chain with its MassTransit outbox. That throws away committed migration history.
  - ❌ An additive "move" migration does not fix fresh installs, as described under the drivers above.

### Option 2: Per-module search path on the connection (Selected)

- **Overview:** Each service connects with `Search Path=<module>,public`.
  - Unqualified DDL and DML, the migrations history table included, then resolve to the module's schema.
  - The migration runner runs `CREATE SCHEMA IF NOT EXISTS <module>` before `MigrateAsync`.
- **Pros:**
  - ✅ No migration or model rewrites. The existing migrations create their tables in the module schema.
  - ✅ Each module has its own `__EFMigrationsHistory`.
  - ✅ `public` stays last on the path, so extension types and functions installed there still resolve.
- **Cons:**
  - ❌ Isolation depends on connection configuration. A connection without the search path, such as a hand-run `psql` session, sees `public`.
  - ❌ Unqualified lookups fall back to `public`, so a stray table there could be read by mistake.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
  - `CadenceDatabaseExtensions.WithModuleSchema(connectionString, schema)` sets the search path. It accepts only schema names matching `^[a-z][a-z0-9_]{0,62}$`.
  - `AddCadencePostgresDbContext<T>` derives the schema from the context name (`CtmsDbContext` becomes `ctms`).
  - Execution uses `edc`, as ADR-2247 names it.
  - Each design-time factory exposes a `Schema` constant and applies the same search path, so `dotnet ef` targets the same schema as the running service.
- **Migration deployment modes**, bound from the `Database` configuration section:
  - `MigrateOnStartup`: migrate in `IHostedLifecycleService.StartingAsync`, before the web server starts. A failure stops startup. Used for local compose and single-instance environments.
  - `MigrateOnly`: migrate, then exit with code 0. Run it as a job or init container before new replicas roll out. Multi-replica production uses this, with `MigrateOnStartup` off.
  - Neither flag set (the default): the service never changes the schema.
- **Justification:** This delivers ADR-2247's isolation today without rewriting committed migration history. It was verified end to end on PostgreSQL 16:
  - All 15 services migrated into one database.
  - Each service ended up with its own schema: `ctms`, `econsent`, `edc`, `eisf`, `etmf`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`.
  - No tables were created in `public`.
  - Re-running the migrations was a no-op.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One database and one connection string for every environment, matching the Aspire AppHost.
  - Services migrate safely at startup or as a job.
  - A unit test fails whenever a model changes without a migration.
- **Negative Impact / Technical Debt:**
  - Existing local databases hold their tables in `public`. Drop and recreate them (`docker compose down -v`); nothing had been deployed.
  - The ctms and org `SyncModelWithSnapshot` migrations drop the tables whose entities #4733 moved to the kernel.
- **Mitigation Strategy:**
  - Later, give each module its own database role that owns only its schema. That makes the isolation enforced by PostgreSQL rather than by connection configuration.
  - The kernel's own schema (`kernel`) is qualified explicitly by the kernel packages and is unaffected by this decision.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/database`: `WithModuleSchema`, `ModuleSchemaFor`, `CadenceMigrationHostedService`.
  - All 15 `apps/*/src/Infrastructure/Persistence/*DbContextFactory.cs`.
  - `apps/execution/src/Program.cs`.
  - Migrations: ctms and org (`SyncModelWithSnapshot`), interop (`InitialCreate` fix).
  - `scripts/manage_migrations.ps1`.
- **Verification Plan:**
  - `tests/Packages/MigrationUnitTests.cs`:
    - No DbContext has model changes without a migration.
    - Each factory and its service use the same schema.
    - Invalid schema names are rejected.
    - The migration flags register the runner.
  - Manual run against PostgreSQL 16, recorded in the PR: all 15 migrations into one database, then a second run as a no-op.
  - Operations runbook: [docs/OPERATIONAL_DATABASE.md](../OPERATIONAL_DATABASE.md).
