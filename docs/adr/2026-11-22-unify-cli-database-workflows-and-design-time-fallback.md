# ADR-2498: Unify CLI Database Workflows Across All 18 Persistence Modules and Offline Design-Time Fallback

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053
- **Relates to:** [ADR-2247](2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md), [ADR-2260](2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md), [ADR-2447](2026-09-29-module-schemas-via-search-path-and-migration-deployment.md), [ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2536](2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md), [ADR-2601](2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md)

---

## 1. Context & Problem Statement

Database setup, resets, migrations, model scripting, and seeding drifted across five inconsistent mechanisms in the repository:

1. **Global vs. pinned local CLI tools:** Setup scripts and wizards (`scripts/setup_dotnet_env.{sh,ps1}`, `scripts/setup_dotnet_wizard.{sh,ps1}`, `scripts/setup_wizard.{sh,ps1}`) ran `dotnet tool update --global dotnet-ef` and `dotnet-format`, risking `.NET 11` preview tool installs that break `net10.0` hosts despite `.config/dotnet-tools.json` pinning `dotnet-ef` `10.0.15` (`rollForward: false`, [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md)). Meanwhile, `cadence doctor` did not check local `dotnet-ef` availability and ignored `--auto-fix`.
2. **Incomplete module lists and split entrypoints:** `cadence db migrate` hardcoded only `apps/execution/src/Cadence.Apps.Execution.csproj`, while `scripts/manage_migrations.{sh,ps1}` duplicated loops over 15 standalone services plus a special-case `kernel` branch, omitting `intelligence` (`apps/intelligence/src/Cadence.Modules.Intelligence.csproj`) and `execution-module` (`apps/execution/module/Cadence.Modules.Execution.csproj`). Running `manage_migrations seed all` executed `dotnet run -- --seed` against 14 web hosts that lack a `--seed` branch, booting Kestrel indefinitely instead of exiting.
3. **Destructive `public` reset and retired SQLite/DDL:** `DatabaseResetService` referenced `Microsoft.EntityFrameworkCore.Sqlite`, deleted `.db`/`.sqlite` files, ignored `ConnectionStrings__cadence`, defaulted `allowOffline` to `true` (returning `0` on PostgreSQL errors), ran `DROP SCHEMA IF EXISTS public CASCADE` (destroying `btree_gist` and other extensions installed in `public`), and recreated `audit_schema` plus seven retired pre-[ADR-2447](2026-09-29-module-schemas-via-search-path-and-migration-deployment.md) tables in `public`.
4. **Offline design-time friction:** `CadenceDatabaseExtensions.ResolveDesignTimeConnectionString`, `KernelDbContextDesignTimeFactory`, and `ExecutionModuleDbContextDesignTimeFactory` threw when `POSTGRES_CONNECTION_STRING` was unset (and ignored `ConnectionStrings__cadence`), whereas `IntelligenceDbContextDesignTimeFactory` hardcoded `Host=localhost;Database=cadence_design` and ignored `--connection` and environment variables during online `database update` calls.

## 2. Decision Drivers & Constraints

- **Single Native AOT Registry ([ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md)):** `Cadence.Cli` publishes with `<PublishAot>true</PublishAot>` and zero trim/AOT suppressions, so persistence module metadata must be static and reflection-free while staying locked to `Cadence.SchemaDocExporter`.
- **Zero Default Credentials ([ADR-2260](2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md)):** Source code must never embed a default username or password, and online commands (`migrate`, `list`, `seed`, `reset`) must fail closed unless a connection string is explicitly configured.
- **Offline Model Productivity (PRD-SYS-053):** Model-only EF Core commands (`migrations add`, `migrations script`, `migrations bundle`, `dbcontext optimize`, `generate-schema-doc`) do not open a network socket and should work in offline sandboxes without dummy secrets, while automatically formatting generated migrations to satisfy `IDE0161` and whitespace gates.
- **Schema Isolation ([ADR-2247](2026-09-29-clinical-kernel-single-database-schema-and-architecture-sentinel-hardening.md), [ADR-2447](2026-09-29-module-schemas-via-search-path-and-migration-deployment.md), [ADR-2601](2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md)):** Every persistence module owns its dedicated schema; `public` holds extensions (`btree_gist`) and zero application tables.

## 3. Options Considered

### Option 1: Maintain Separate Shell and PowerShell Loops

- **Overview:** Keep `scripts/manage_migrations.{sh,ps1}` as the primary orchestrator and patch missing modules into both shell and PowerShell scripts.
- **Pros:**
  - Minimal C# CLI changes.
- **Cons:**
  - Preserves duplicated module lists across Bash, PowerShell, `Cadence.Cli`, and `Cadence.SchemaDocExporter`, and leaves `cadence db` out of sync.

### Option 2: Unified Native AOT Registry, `cadence db` Subcommands, and Credential-Free Offline Design-Time Fallback (Selected)

- **Overview:** Centralize `PersistenceModuleRegistry` in `Cadence.ToolingCore`, implement `reset`, `migrate`, `add`, `script`, `bundle`, `list`, `optimize`, and `seed` in `Cadence.Cli` via `dotnet tool run dotnet-ef`, reduce `scripts/manage_migrations.{sh,ps1}` to thin wrappers, restore local tools via `dotnet tool restore`, and use `"Host=localhost;Database=cadence_design"` as an offline fallback in all design-time factories.
- **Pros:**
  - Single static source of truth across CLI, scripts, and `Cadence.SchemaDocExporter`.
  - Enforces `.config/dotnet-tools.json` (`dotnet-ef` `10.0.15`) and fail-closed online checks while enabling zero-credential offline scaffolding.
- **Cons:**
  - Adding a new persistence module requires registering static metadata in `PersistenceModuleRegistry` (enforced by unit tests).

## 4. Decision Outcome

- **Chosen Option:** Option 2 — Unified Native AOT Registry, `cadence db` Subcommands, and Credential-Free Offline Design-Time Fallback.
- **Summary of Changes:**
  1. **Local Tool Manifest Enforcement:** Setup scripts and onboarding wizards run `dotnet tool restore` against `.config/dotnet-tools.json` (`dotnet-ef` `10.0.15`) rather than installing global tools. `cadence doctor` verifies the restored local `dotnet-ef` version against `.config/dotnet-tools.json` and runs `dotnet tool restore` when `--auto-fix` (`-f`) is passed.
  2. **Native AOT `PersistenceModuleRegistry`:** `Cadence.ToolingCore.Services.PersistenceModuleRegistry` (`PersistenceModuleDescriptor`) defines all 18 persistence modules (`kernel`, `intelligence`, `execution-module` with alias `executionmodule`, `execution`, and the 14 standalone services `ctms`, `econsent`, `eisf`, `etmf`, `fileshare`, `imaging`, `interop`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`), ordered with `kernel` first and explicit `DbContextName` metadata in lockstep with `PersistenceModuleCatalog` ([ADR-2536](2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md)).
  3. **Schema-Scoped `cadence db reset` & SQLite Removal:** `Microsoft.EntityFrameworkCore.Sqlite` and `ResetSQLite` are removed from `Cadence.Cli`. `DatabaseResetService` resolves `ConnectionStrings__cadence`, `POSTGRES_CONNECTION_STRING`, and `DATABASE_URL` in order, fails closed (`exit 1`) on missing or unreachable PostgreSQL unless `--allow-offline` is passed (`make db-reset-offline`), preserves `public` extensions (`btree_gist`), drops and recreates all 18 module schemas, drops `audit_schema`, and removes any non-extension base tables in `public`.
  4. **Unified `cadence db` Subcommands & Thin Script Wrappers:** `cadence db` provides `reset`, `migrate`, `add`, `script`, `bundle`, `list`, `optimize`, and `seed` across `<module|all>`. Online commands (`migrate`, `list`, `seed`) require `ConnectionStrings__cadence` or `POSTGRES_CONNECTION_STRING` before running; `migrate` creates each target schema (`CREATE SCHEMA IF NOT EXISTS "<schema>"`, plus `btree_gist` for `kernel`) before invoking `dotnet tool run dotnet-ef database update`; `add` runs scoped `dotnet format whitespace` and `dotnet format style` on generated migration files; `script` and `bundle` write artifacts to gitignored `TestResults/migrations/` ([ADR-2536](2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md)); `seed` invokes `--seed` only on `execution` and prints `DevelopmentTenantBootstrap` guidance for `kernel`. `scripts/manage_migrations.{sh,ps1}` forward directly to `cadence db` and `cadence migrations verify`.
  5. **Credential-Free Design-Time Fallback:** `CadenceDatabaseExtensions.ResolveDesignTimeConnectionString`, `KernelDbContextDesignTimeFactory`, `ExecutionModuleDbContextDesignTimeFactory`, and `IntelligenceDbContextDesignTimeFactory` resolve `--connection`, then `ConnectionStrings__cadence`, then `POSTGRES_CONNECTION_STRING`, falling back to `"Host=localhost;Database=cadence_design"` (zero username or password, preserving [ADR-2260](2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md)).

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - One AOT-safe source of truth governs schema documentation, local resets, migrations, scripts, bundles, compiled models, and seeding across all 18 schemas on Linux, macOS, and Windows.
  - Offline migration authoring and SQL script generation succeed without dummy secrets while online commands and runtime hosts remain strictly fail-closed.
  - Preserving `public` extensions during `cadence db reset` prevents `btree_gist` exclusion-constraint failures on subsequent kernel migrations.
- **Negative Impact / Technical Debt:**
  - Adding a 19th persistence module requires registering static entries in `PersistenceModuleRegistry` and `PersistenceModuleCatalog` alongside its `ProjectReference` in `Cadence.SchemaDocExporter.csproj`.
- **Mitigation Strategy:**
  - `CliDatabaseWorkflowTests` asserts parity across `PersistenceModuleRegistry.All`, `PersistenceModuleCatalog.All`, `Cadence.SchemaDocExporter.csproj` references, and each module's `<DbContextName>ModelSnapshot.cs`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/tooling_core`, `packages/database`, `packages/kernel/src/Cadence.Kernel.Modules`, `packages/kernel/src/Cadence.Kernel.Persistence`, `apps/execution/module`, `apps/execution/src`, `apps/intelligence/src`, `packages/cli`, `scripts/`, `Makefile`, `tests/Cadence.UnitTests`, `docs/`.
- **Verification Plan:** Unit and architecture suites (`DatabaseUnitTests`, `CliCommandLineTests`, `CliDatabaseWorkflowTests`, `Cadence.ArchitectureTests`, `tests/test_installer_checksums.sh`), Native AOT publish of `Cadence.Cli`, offline `cadence db script` and `cadence db add` formatting verification, live PostgreSQL 18 `cadence doctor` + `cadence db reset` + `cadence db migrate` + `manage_migrations.sh apply/list/seed` verification across all 18 schemas, and full `cadence check` quality sentinels.
