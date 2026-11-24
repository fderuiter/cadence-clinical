# ADR-2536: Unify 18-Module Migration Tooling, Fail-Closed Metadata Verification, and Count-Free Schema Docs

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter, Stitch Agent
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails and deterministic GxP verification evidence), PRD-EVT-001

---

## 1. Context & Problem Statement

Cadence persists clinical state across 18 EF Core `DbContext` modules sharing the PostgreSQL 18 `cadence` database: 15 standalone clinical services (`ctms` through `tickets`), `kernel` (`KernelDbContext`), and two host-mounted kernel modules (`intelligence` and `executionmodule`). Four structural sources of cross-branch merge friction and silent migration drift remained:

1. `packages/kernel/src/Cadence.Kernel.Persistence/Migrations/idempotent_migration.sql` was a tracked 4,445-line generated SQL dump excluded from secret scanning (`SecretsBaselineCleanerService.CanonicalExcludePattern`), and `scripts/manage_migrations.{sh,ps1}` wrote `idempotent_migration.sql` directly inside source `Migrations/` directories while knowing only 15 of the 18 modules (omitting `intelligence` and `executionmodule`).
2. `docs/schema/README.md` included a `Tables` count column on every row, and `SchemaDocumentation.Render` emitted a top-of-file `| Table | Entity | Columns |` summary table in every `docs/schema/<module>.md` file. Adding a table or column on two parallel branches guaranteed textual Git merge conflicts even when the branches touched unrelated entities.
3. `Cadence.SchemaDocExporter` rendered `docs/schema` from live EF Core models without checking `context.Database.HasPendingModelChanges()`, migration attributes (`[DbContext]`, `[Migration]`), `.Designer.cs` companions, or duplicate `CreateTable`/`AddColumn` operations across merged migrations. A hand-authored migration in `ctms` (`20261001180000_AddVisitLetterPdfAndDispatchFields.cs`) lacked its `.Designer.cs` companion, and `MigrationUnitTests` did not test `IntelligenceDbContextDesignTimeFactory` or `ExecutionModuleDbContextDesignTimeFactory`.

## 2. Decision Drivers & Constraints

- Keep all clinical runtime endpoints, UI screens, and PostgreSQL 18 schema definitions unchanged.
- Preserve `Cadence.SchemaDocExporter` as the sole build-time assembly referencing `Cadence.Kernel.Persistence` (`K-XS-2` in `KernelBoundaryRules.cs`), and keep `Cadence.Cli` 100% Native AOT-safe (`PublishAot=true` with zero `IL2xxx`/`IL3xxx` suppressions).
- Do not introduce custom Git merge drivers or `.gitattributes` `merge=union` rules; `docs/schema` must merge cleanly under standard three-way `git merge-file`.
- Keep all 18 `docs/schema/<module>.md` pages and `docs/schema/README.md` indexed for VitePress sidebar checks (`docs/.vitepress/navigation.test.mjs`).
- Splitting EF Core `*ModelSnapshot.cs` files into per-entity partial files was out of scope in this ADR and is superseded by [ADR-2824](2026-11-23-split-model-snapshots-and-compact-designer-metadata.md).

## 3. Options Considered

### Option 1: Centralize the 18-module catalog in `Cadence.ToolingCore`, add `cadence migrations`, strip count summaries from `docs/schema`, and enforce migration invariants in `Cadence.SchemaDocExporter` (Selected)

- **Overview:** Delete the tracked `idempotent_migration.sql` dump and its secret-baseline filter; define `PersistenceModuleCatalog` (all 18 modules) and `MigrationToolingService` (`cadence migrations add|script|list|verify`, writing SQL review scripts to untracked `TestResults/migrations/`) in `Cadence.ToolingCore`; route `scripts/manage_migrations.{sh,ps1}` through `cadence migrations`; remove volatile table/column count summaries from `docs/schema/README.md` and `SchemaDocumentation.Render`; backfill `20261001180000_AddVisitLetterPdfAndDispatchFields.Designer.cs`; enforce `HasPendingModelChanges()`, `[DbContext]`, `[Migration]`, `.Designer.cs` presence, and `CreateTable`/`AddColumn` uniqueness in `Cadence.SchemaDocExporter`; and add `HasPendingModelChanges()` unit tests for `IntelligenceDbContext` and `ExecutionModuleDbContext`.
- **Pros:**
  - ✅ Single 18-module catalog shared by CLI commands, shell scripts, and `Cadence.SchemaDocExporter` without violating `K-XS-2` or Native AOT constraints.
  - ✅ Parallel table creations across modules never touch `docs/schema/README.md`, and parallel column additions on distinct tables in the same module merge cleanly in `docs/schema/<module>.md` without custom Git merge drivers.
  - ✅ `pnpm cadence check schema-doc` and `pnpm cadence migrations verify` fail closed offline whenever a model diverges from its snapshot, a `.Designer.cs` companion or attribute is missing, or merged migrations duplicate `CreateTable`/`AddColumn`.
- **Cons:**
  - ❌ `docs/schema` omits aggregate table/column count tables at the top of each document in favor of deterministic sorted `## <schema>.<table>` sections.

### Option 2: Keep count tables in `docs/schema` and configure custom Git merge drivers

- **Cons:**
  - ❌ GitHub's server-side merge check (`conflict-check.yml` / PR mergeability) ignores local `.git/config` merge drivers, leaving PRs conflicted on GitHub whenever two branches add tables or columns.

## 4. Decision Outcome

**Chosen option: Option 1.**

1. Retired `packages/kernel/src/Cadence.Kernel.Persistence/Migrations/idempotent_migration.sql` and removed `Migrations/idempotent_migration\.sql$|` from `SecretsBaselineCleanerService.CanonicalExcludePattern` and `.secrets.baseline`.
2. Added `PersistenceModuleCatalog` and `MigrationToolingService` in `packages/tooling_core/src/Services/`, registered `cadence migrations` (`add`, `script`, `list`, `verify`) in `packages/cli`, and routed `scripts/manage_migrations.sh` and `scripts/manage_migrations.ps1` through `cadence migrations`, writing SQL review scripts to `TestResults/migrations/<module>.sql`.
3. Removed the `Tables` count column from `Cadence.SchemaDocExporter.Program.Index` (`docs/schema/README.md`) and removed the top `| Table | Entity | Columns |` summary table from `SchemaDocumentation.Render`, preserving sorted `## <schema>.<table>` sections.
4. Added `SchemaMigrationValidator` in `Cadence.SchemaDocExporter` and wired it into both `generate-schema-doc` (`check schema-doc`) and `cadence migrations verify` (`--verify-only`), failing closed on `HasPendingModelChanges()`, missing `[DbContext]`/`[Migration]`, missing `.Designer.cs` companions, and duplicate `CreateTable`/`AddColumn` targets. _(Note: the `BuildTargetModel` requirement and table/column-only scope are superseded by [ADR-2824](2026-11-23-split-model-snapshots-and-compact-designer-metadata.md), which compacts `*.Designer.cs` files to attribute-only partial classes, splits `*ModelSnapshot.cs` per entity under `Migrations/Snapshots/`, and enforces index, foreign-key, unique, and check constraint uniqueness.)_
5. Backfilled `apps/ctms/src/Infrastructure/Persistence/Migrations/20261001180000_AddVisitLetterPdfAndDispatchFields.Designer.cs` and moved `[DbContext]`/`[Migration]` onto the `.Designer.cs` partial class.
6. Added `IntelligenceDbContext_HasNoModelChangesMissingAMigration`, `ExecutionModuleDbContext_HasNoModelChangesMissingAMigration`, and 18-module catalog assertions to `MigrationUnitTests.cs`, and updated `docs/OPERATIONAL_DATABASE.md` to list all 18 module schemas and `cadence migrations` workflows.

## 5. Consequences & Trade-offs

- DBA and GxP qualification SQL scripts are generated on demand into `TestResults/migrations/<module>.sql` instead of bloating commits or bypassing secret scanning under `Migrations/`.
- Parallel feature branches adding tables in separate modules or adding columns to separate tables in the same module merge cleanly under standard Git three-way merge.
- Any hand-edited migration missing `.Designer.cs`, missing `[Migration]`/`[DbContext]`, duplicating table/column creation after a branch merge, or leaving pending model changes fails `pnpm cadence check schema-doc`, `pnpm cadence migrations verify`, and `Cadence.UnitTests` before merge.

## 6. Implementation & Verification

- `packages/tooling_core/src/Services/PersistenceModuleCatalog.cs`, `MigrationToolingService.cs`, `SecretsBaselineCleanerService.cs`, and `SchemaDocGeneratorService.cs`.
- `packages/tooling_core/src/Cadence.SchemaDocExporter/Program.cs` and `SchemaMigrationValidator.cs`.
- `packages/database/src/SchemaDocumentation.cs`.
- `packages/cli/src/Commands/MigrationCommands.cs`, `packages/cli/src/Program.cs`, `scripts/manage_migrations.sh`, and `scripts/manage_migrations.ps1`.
- `apps/ctms/src/Infrastructure/Persistence/Migrations/20261001180000_AddVisitLetterPdfAndDispatchFields.Designer.cs`.
- `tests/Cadence.UnitTests/Packages/MigrationUnitTests.cs`, `SchemaDocumentationTests.cs`, `BuildDocsScriptTests.cs`, `PreCommitGuardrailsTests.cs`, and `CliCommandLineTests.cs`.
- `docs/OPERATIONAL_DATABASE.md`, `docs/cli.md`, `docs/CI.md`, and regenerated `docs/schema/*.md`.
