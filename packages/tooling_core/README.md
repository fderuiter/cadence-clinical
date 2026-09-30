# Cadence.ToolingCore

Implementation library behind the Cadence CLI: repository validators, generators and gate runners, plus two standalone exporters (OpenAPI specs and database schema docs).

## Projects

- `packages/tooling_core/src/Cadence.ToolingCore.csproj` (assembly `Cadence.ToolingCore`):
  - `Cadence.ToolingCore.Services`: one static service per tool, for example `ReadmeValidatorService`, `MarkdownValidatorService`, `AdrValidatorService`, `ArchitectureDriftValidatorService`, `ImportValidatorService`, `DependencyValidatorService`, `SyncGxPService`, `ScaffoldServiceService`, `SelfHealService`, `OpenApiSyncService`, `SchemaDocGeneratorService`.
  - `Cadence.ToolingCore.Markdown`: `MarkdownParser` (workspace link checking).
  - `Cadence.ToolingCore.Contracts`: `ContractVerifier`.
  - `Cadence.ToolingCore.Security`: `SecretScanner`.
  - `Cadence.ToolingCore.OpenApi`: `OpenApiToolingService`, and `OpenApiSpecExporter` (currently writes placeholder documents with empty `paths`).
  - `Cadence.ToolingCore`: `ToolingHandlers` and request/response records (`Contracts.cs`) intended for MCP tools; only the unit tests call them today.
- `packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj`: console app that loads the execution, designer and gateway services in-process and writes their OpenAPI documents (default `docs/openapi`, override with `--output-dir` / `-o`).
- `packages/tooling_core/src/Cadence.SchemaDocExporter/Cadence.SchemaDocExporter.csproj`: console app that creates each module's DbContext through its design-time factory (the kernel and 15 apps), renders the EF Core model with `SchemaDocumentation` from [packages/database](../database/README.md) and writes `docs/schema/<module>.md` plus `README.md` (default `docs/schema`, override with `--output-dir` / `-o`). It never opens a database connection. `SchemaDocGeneratorService` runs it for `generate-schema-doc` and the `schema-doc` gate.

## Key dependencies

- `Cadence.ToolingCore`: project reference `Cadence.Compliance`; framework reference `Microsoft.AspNetCore.App`; packages `Microsoft.EntityFrameworkCore`, `Spectre.Console`.
- `Cadence.OpenApiExporter`: `Cadence.ToolingCore`, the execution, designer and gateway service projects, `Microsoft.AspNetCore.Mvc.Testing`.
- `Cadence.SchemaDocExporter`: `Cadence.Database`, `Cadence.Kernel.Persistence` and the 15 app projects that own a DbContext.

## Referenced by

[packages/cli](../cli/README.md), `Cadence.OpenApiExporter`, `apps/gateway` (project reference only; no gateway code uses it), `tests/Cadence.UnitTests.csproj`, `tests/Cadence.E2ETests.csproj`.

## Tests

Covered by `tests/Packages/PortedPackagesUnitTests.cs` and `tests/Packages/DeidEngineUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests|FullyQualifiedName~Cadence.UnitTests.Packages.DeidEngineUnitTests"
```
