# Cadence.ToolingCore

Implementation library behind the Cadence CLI: repository validators, generators
and gate runners, plus two standalone exporters (OpenAPI specs and database
schema docs).

## Projects

- `packages/tooling_core/src/Cadence.ToolingCore.csproj` (assembly
  `Cadence.ToolingCore`):
  - `Cadence.ToolingCore.Services`: one static service per tool, plus
    `PosixProcessLauncher` (cross-platform `UseShellExecute = false` process
    launcher resolving Windows `.exe`/`.cmd`/`.bat` shims in `PATH` directory
    order with custom `PATH` overrides and POSIX `/bin/sh` `ENOEXEC` fallback),
    `GitRevisionScopeService` (centralized staged, branch `--base`, push
    `--push-before`, and `git ls-files -z` tracked-file diff scoping),
    `GitIndexReader` and `StagedBlobWorkspace` (lock-free staged Git index blob
    reader via `git cat-file --batch` and disposable workspace materializer for
    `--pre-commit` gates), `CliLinterGateService` (fail-closed `actionlint` and
    `hadolint` gates plus `shellcheck` resolution from `.cadence/bin` or
    `PATH`), `WorkflowSecurityValidatorService` (GitHub Actions workflow
    security, 40-char commit SHA pinning, `sha256:` container digests,
    `persist-credentials: false`, and least-privilege permissions),
    `YamlSyntaxValidatorService` (Native AOT YAML 1.2 validator),
    `FileHygieneValidatorService` (`trailing-whitespace` and `end-of-file`
    newline checks), `FrontendQualityGateService` (fail-closed Prettier
    check/format and ESLint gates streaming staged blobs over `stdin` under
    `--pre-commit`), `DetectSecretsGateService` (fail-closed bounded-parallel
    `detect-secrets-hook` gate with isolated per-batch temporary baselines
    resolving `.cadence/venv`, `.venv`, `PATH` or `uvx`),
    `SecretsBaselineCleanerService` (canonical `.secrets.baseline` cleaner,
    non-mutating `--check` verifier, and file-exclusion regex),
    `ReadmeValidatorService`, `MarkdownValidatorService`, `AdrValidatorService`,
    `ArchitectureDriftValidatorService`, `ImportValidatorService`,
    `DependencyValidatorService` (fail-closed JSON validation across workspace
    `package.json` and `.config/dotnet-tools.json` manifests plus forbidden
    client-side crypto checks), `VulnerabilityAuditorService` (fail-closed
    `.NET`, `pnpm` and `pip-audit` CVE auditing against
    `docs/SDLC/vulnerability_exclusions_ledger.json`),
    `DemoManifestGeneratorService` (exports and verifies
    `packages/demo-fixtures` from `packages/demo-fixtures/demo-seed.json`,
    `ExecutionDbSeeder.cs`, and `docker/cadence-realm.json` via source-generated
    `System.Text.Json`), `SyncGxPService`, `ScaffoldServiceService`,
    `SelfHealService` (subdirectory-safe `cadence fix` pipeline with diff-based
    schema, OpenAPI, and web client type regeneration, plus canonical demo
    manifest sync), `OpenApiSyncService`, `PersistenceModuleCatalog` and
    `MigrationToolingService` (canonical 18-module persistence catalog and
    `cadence migrations add|script|list|verify` workflow writing SQL scripts to
    `TestResults/migrations/`), `SchemaDocGeneratorService`,
    `PublicBundleValidatorService` (the zero-exemption `public-bundle` gate over
    compiled static portal and demo bundles), and `DeidComplianceService` (the
    `deid` gate: the full `DeidDetector` over changed or tracked candidate
    files, ratcheted by `.deid-baseline.txt`).
  - `Cadence.ToolingCore.Markdown`: `MarkdownParser` (workspace link and
    `#anchor` heading fragment checking, and paths into .NET test project
    folders that no longer exist; `docs/migration/` and the generated RTM and
    IQ/OQ/PQ report are skipped for test-path checks).
  - `Cadence.ToolingCore.Security`: `SecretScanner`.
  - `Cadence.ToolingCore.OpenApi`: `OpenApiToolingService`, and
    `OpenApiSpecExporter` (currently writes placeholder documents with empty
    `paths`).
  - `Cadence.ToolingCore`: `ToolingHandlers` and request/response records
    (`Contracts.cs`) intended for MCP tools; only the unit tests call them
    today.
- `packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj`:
  console app that loads the execution, designer and gateway services in-process
  and writes their OpenAPI documents (default `docs/openapi`, override with
  `--output-dir` / `-o`).
- `packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj`:
  console app that creates each of the 18 persistence modules' DbContexts
  (`PersistenceModuleCatalog.All`: `kernel`, `intelligence`, `executionmodule`,
  and 15 standalone apps) through its design-time factory, verifies
  `HasPendingModelChanges()`, `[DbContext]`, `[Migration]`, `.Designer.cs`
  companions, and `CreateTable`/`AddColumn` uniqueness
  (`SchemaMigrationValidator`), renders count-free Markdown with
  `SchemaDocumentation` from [packages/database](../database/README.md), and
  writes `docs/schema/<module>.md` plus `README.md` (default `docs/schema`,
  override with `--output-dir` / `-o`, or `--verify-only [--module <name>]`). It
  never opens a database connection. `SchemaDocGeneratorService` and
  `MigrationToolingService` run it for `generate-schema-doc`,
  `check schema-doc`, and `migrations verify`.
- Time (K-TIME-3): all three projects ban `DateTime.Now/UtcNow`.
  `CreateAdrService.CreateAdr` takes the date.

## Key dependencies

- `Cadence.ToolingCore`: project references `Cadence.Compliance` and
  `Cadence.Deid`; framework reference `Microsoft.AspNetCore.App`; packages
  `Microsoft.EntityFrameworkCore`, `Spectre.Console`.
- `Cadence.OpenApiExporter`: `Cadence.ToolingCore`, the execution, designer and
  gateway service projects, `Microsoft.AspNetCore.Mvc.Testing`.
- `Cadence.SchemaDocExporter`: `Cadence.Database`, `Cadence.ToolingCore`,
  `Cadence.Kernel.Persistence`, `Cadence.Modules.Intelligence`,
  `Cadence.Modules.Execution`, and the 15 app projects that own a DbContext.

## Referenced by

[packages/cli](../cli/README.md), `Cadence.OpenApiExporter`, `apps/gateway`
(project reference only; no gateway code uses it),
`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`,
`tests/Cadence.E2ETests/Cadence.E2ETests.csproj`.

## Tests

Covered by `tests/Cadence.UnitTests/Packages/PortedPackagesUnitTests.cs`,
`GitRevisionScopeServiceTests.cs`, `AdrValidatorServiceTests.cs`,
`PreCommitGuardrailsTests.cs`, `VulnerabilityAuditorServiceTests.cs`,
`DemoManifestGeneratorTests.cs`, `DeidEngineUnitTests.cs` and
`DeidComplianceServiceTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests|FullyQualifiedName~GitRevisionScopeServiceTests|FullyQualifiedName~AdrValidatorServiceTests|FullyQualifiedName~Cadence.UnitTests.Packages.PreCommitGuardrailsTests|FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests|FullyQualifiedName~DeidComplianceServiceTests"
```
