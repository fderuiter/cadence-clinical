# Cadence Unified Developer CLI Reference

`cadence` is the developer CLI for this repository. It is a .NET 10 console app built with Spectre.Console. The source is in `packages/cli/src`. Commands are registered in `packages/cli/src/Program.cs`.

## Running the CLI

Run it through the root `package.json` script. Run it from the repository root.

```bash
pnpm cadence <command> [arguments] [options]
```

The script runs `dotnet run --project packages/cli/src/Cadence.Cli.csproj --`, so it builds the CLI first when needed. Examples:

```bash
pnpm cadence doctor
pnpm cadence check duplication
pnpm cadence gxp sync --dry-run
```

Some commands also have a `make` target. The Makefile publishes the CLI to `./bin/cadence` the first time and reuses that binary afterwards. Delete `./bin` to pick up CLI changes.

| `make` target                            | Runs                                      |
| ---------------------------------------- | ----------------------------------------- |
| `make doctor`                            | `cadence doctor --auto-fix`               |
| `make ports`                             | `cadence check-ports`                     |
| `make dev`                               | `cadence dev`                             |
| `make fix`                               | `cadence fix`                             |
| `make check`                             | `cadence check`                           |
| `make test`                              | `cadence test`                            |
| `make verify`                            | `cadence check`, then `cadence test`      |
| `make sync-gxp`, `make gxp-sync`         | `cadence gxp sync`                        |
| `make rtm`                               | `cadence gxp validate`                    |
| `make adr TITLE="..."`                   | `cadence scaffold adr "<title>"`          |
| `make db-reset`, `make db-reset-offline` | `cadence db reset`                        |
| `make db-seed`                           | `cadence db seed` (not implemented yet)   |
| `make db-status`                         | `cadence db status` (not implemented yet) |

`make wizard` runs `scripts/setup_wizard.sh` directly. That is the same script as `cadence wizard onboarding`.

There are no global options. Spectre.Console adds `-h|--help` to every command, for example `pnpm cadence check --help`. Every run prints a "Cadence" banner first.

Exit code `0` means success. Any other exit code means failure. Some commands always exit `0`; the sections below say which.

## Commands

### `cadence doctor`

Checks the local environment. It reports:

- the .NET SDK version (from `dotnet --version`)
- whether `Directory.Packages.props` exists in the current directory
- whether PostgreSQL answers on `127.0.0.1:5432`

It always exits `0`.

| Option           | Description                                                                          |
| ---------------- | ------------------------------------------------------------------------------------ |
| `-f\|--auto-fix` | Declared as "remediate missing databases and recoverable issues". Currently ignored. |

### `cadence check`

Runs the quality gates.

```bash
pnpm cadence check                 # all 21 gates
pnpm cadence check <gate>          # one gate
pnpm cadence check <gate> <files>  # one gate, limited to these files
pnpm cadence check --pre-commit    # what the pre-commit hook runs
```

| Argument       | Description                                                                                                                                                                                                                             |
| -------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[gate]`       | One gate to run. Full or short name, for example `duplication` or `detect-code-duplication`. Underscores are treated as hyphens.                                                                                                        |
| `[files]`      | Files to check. Only some gates use them (duplication, sanitizer, markdown, security, deid, path-patterns, query-linter).                                                                                                               |
| `--pre-commit` | Quick variants for the pre-commit hook: the two format gates are skipped (the hook formats the staged files itself), and the `openapi` gate checks `docs/api` against the committed specs instead of exporting specs from the services. |

With no gate, all 21 gates run in order. A failing gate does not stop the run. The command exits `1` if any gate failed.

An unknown gate name prints an error and exits `1`. Two extra names are accepted:

- `rtm` (or `gxp-rtm-validate`) validates the traceability matrix without writing it. It is not one of the 21.
- `prettier-check` always passes. It runs nothing.

See [CI.md](CI.md#quality-gates) for the list of gates and what each one checks.

### `cadence test`

Runs `dotnet test Cadence.sln` from the repository root.

| Option              | Description                              |
| ------------------- | ---------------------------------------- |
| `-f\|--fast`        | Adds `--filter "Category!=Integration"`. |
| `-i\|--integration` | Adds `--filter "Category=Integration"`.  |

With no option, all tests run. If both options are given, `--fast` wins.

### `cadence dev`

Starts the .NET Aspire AppHost: `dotnet run --project apps/apphost/src/Cadence.AppHost.csproj`. The AppHost starts the services, the datastores and the Aspire dashboard. The command waits until the AppHost exits and returns its exit code. It has no options.

### `cadence fix`

Runs these steps in order:

1. `dotnet format Cadence.sln` in the current directory.
2. ADR validation (same check as the `adrs` gate, without the git diff check). It does not change ADR files.
3. `sync-openapi-spec` and `generate-api-docs` (see the tool table below).

It exits `1` if any step fails. It has no options.

### Tool commands

Single-purpose commands. Most are also run as gates by `cadence check`. None of them take arguments. Options are listed in the table.

Paths are relative to the repository root. "Calls" is the method the command runs, in `packages/tooling_core/src/Services` unless noted.

| Command                       | What it does                                                                                                                                                                                                                                                                                                                                          | Calls                                                         |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `detect-duplication`          | Finds duplicated blocks (over 15 normalized lines) in `.cs`, `.py`, `.js`, `.vue` and `.css` files under `apps/` and `packages/`. Writes `duplication_summary.json`. Fails only when a duplicate involves a `.cs` file.                                                                                                                               | `DuplicationDetectorService.DetectDuplication`                |
| `validate-imports`            | Fails when a `.cs` file in one `apps/<service>` has `using Cadence.Apps.<other service>`.                                                                                                                                                                                                                                                             | `ImportValidatorService.ValidateImports`                      |
| `validate-dependencies`       | Fails when `apps/web/package.json` lists `node-forge`, `elliptic` or `jose`. Passes if the file is missing.                                                                                                                                                                                                                                           | `DependencyValidatorService.ValidateDependencies`             |
| `validate-path-patterns`      | Fails on directories named `tmp`, `temp`, `scratch` or `old_src`, `.vue` files outside `apps/` and `packages/`, and `.sh` files outside `scripts/`.                                                                                                                                                                                                   | `PathPatternValidatorService.ValidatePathPatterns`            |
| `validate-readmes`            | Fails when a direct subdirectory of `apps/` or `packages/` has no `README.md`, or its README has no `# ` title.                                                                                                                                                                                                                                       | `ReadmeValidatorService.ValidateReadmes`                      |
| `validate-codeowners`         | Fails when `.github/CODEOWNERS` is missing or empty.                                                                                                                                                                                                                                                                                                  | `CodeownersValidatorService.ValidateCodeowners`               |
| `validate-markdown`           | Checks workspace links and `#` heading spacing in `docs/**/*.md`.                                                                                                                                                                                                                                                                                     | `MarkdownValidatorService.ValidateMarkdown`                   |
| `query-linter`                | Scans `.cs` files outside `tests/`. Fails on interpolated `FromSqlRaw($...)` or `ExecuteSqlRaw($...)`, and on `== true` or `== false`.                                                                                                                                                                                                                | `QueryLinterService.LintQueries`                              |
| `extract-env-vars`            | Finds environment variable reads in `.cs` and `.py` files. Fails only if `.env.example` is missing. With `--check`, warns about variables missing from `.env.example` or `docs/ENVIRONMENT_VARIABLES.md`. It never writes files.                                                                                                                      | `EnvVarExtractorService.ExtractAndValidateEnvVars`            |
| `verify-contracts`            | Checks hexagonal port and contract definitions. The rules are in `packages/tooling_core/src/Contracts/ContractVerifier.cs`.                                                                                                                                                                                                                           | `ContractVerifierService.VerifyContracts`                     |
| `audit-security`              | Scans every file in the repository for secret patterns.                                                                                                                                                                                                                                                                                               | `SecurityAuditorService.AuditSecurity`                        |
| `validate-vulnerabilities`    | Runs `dotnet list package --vulnerable --no-restore`. Fails if a vulnerable NuGet package is listed. It does not check npm packages.                                                                                                                                                                                                                  | `VulnerabilityAuditorService.AuditVulnerabilities`            |
| `clean-secrets-baseline`      | Rewrites the file exclusion filter in `.secrets.baseline`. Skips if the file is missing.                                                                                                                                                                                                                                                              | `SecretsBaselineCleanerService.CleanSecretsBaseline`          |
| `validate-architecture-drift` | Lists services in `docker/docker-compose.yml`. Warns when one is not mentioned in `ARCHITECTURE.md` or `docs/SDLC/02_Technical_Design_Document_TDD.md`. Fails only if the compose file is missing or has no services.                                                                                                                                 | `ArchitectureDriftValidatorService.ValidateArchitectureDrift` |
| `sync-openapi-spec`           | Runs `Cadence.OpenApiExporter`, which boots every .NET service in memory and exports its OpenAPI document, then writes the specs and the `cdisc_openapi.json` aggregate to `docs/openapi/` and deletes top-level specs no service serves. `docs/openapi/legacy/` is left alone. Option: `--validate` (compare only; exits `1` on drift).              | `OpenApiSyncService.SyncOpenApiSpec`                          |
| `validate-openapi-drift`      | Runs `sync-openapi-spec --validate` and `generate-api-docs --validate`. Exits `1` if either finds drift.                                                                                                                                                                                                                                              | `OpenApiDriftValidatorService.ValidateDrift`                  |
| `generate-api-docs`           | Writes `docs/api/<service>.md` (one table of operations per service) and `docs/api/index.md` from `docs/openapi/*_openapi.json`, and deletes pages with no spec. Option: `--validate` (compare only; exits `1` on drift).                                                                                                                             | `ApiDocsGeneratorService.GenerateApiDocs`                     |
| `generate-schema-doc`         | Runs `Cadence.SchemaDocExporter`, which builds every module's EF Core design-time model (no database needed) and writes one Markdown page per module plus `README.md` to `docs/schema/`, deleting files no module produces. Option: `--validate` (compare only; exits `1` on drift). This is the `schema-doc` gate.                                   | `SchemaDocGeneratorService.GenerateSchemaDocumentation`       |
| `pre-commit-openapi`          | Same as `validate-openapi-drift`. This is the `openapi` gate. It does not stage anything.                                                                                                                                                                                                                                                             | `PreCommitOpenApiService.ExecutePreCommitOpenApi`             |
| `validate-api-clients`        | Checks that `apps/web/src/api/generated/` matches `docs/openapi/` (`node scripts/generate-api-types.js --check`), then runs `pnpm exec tsc --noEmit` in `apps/web`, which typechecks the `apps/web/src/api` clients against their generated OpenAPI types. Exits `1` on stale generated types or a type error. This is the `api-clients` gate.        | `ApiClientValidatorService.ValidateApiClients`                |
| `generate-cli-docs`           | Without options, creates a stub `docs/cli.md` only if it is missing. With `--check`, fails when this file lacks the required headings or a command registered in `Program.cs`.                                                                                                                                                                        | `CliDocsGeneratorService.GenerateCliDocs`                     |
| `generate-rtm`                | Builds the traceability matrix from requirements in `docs/`, `@req` tags in tests and `report_dotnet.trx`. Writes `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`. Options: `--validate` (write nothing; fail if a mapped test failed), `--draft` (mark tests as unverified when the report is missing). | `RtmGeneratorService.GenerateRtm` (`packages/compliance/src`) |
| `compliance-utility`          | With `--sanitize`, upper-cases `@req:PRD-...` tags in `docs/**/*.md`, skipping `docs/SDLC/runs/`. Without it, does nothing.                                                                                                                                                                                                                           | `ComplianceUtilityService.ExecuteComplianceUtility`           |
| `verify-gxp-signatures`       | Not implemented yet. Exits `1` without checking any signature.                                                                                                                                                                                                                                                                                        | `VerifyGxPSignaturesService.VerifySignatures`                 |
| `clean-test-dbs`              | Deletes every `.db` and `.sqlite` file in the repository whose path does not contain `node_modules`, `bin` or `obj`.                                                                                                                                                                                                                                  | `CleanTestDbsService.CleanTestDatabases`                      |
| `check-ports`                 | Reports whether ports 8000-8002, 8004-8019, 5432, 7687 and 8080 are in use on `127.0.0.1`. Always exits `0`.                                                                                                                                                                                                                                          | `PortCheckerService.CheckPorts`                               |
| `run-gating-pipeline`         | Runs `detect-duplication`, `validate-imports` and `validate-openapi-drift`. Stops at the first failure.                                                                                                                                                                                                                                               | `GatingPipelineService.RunGatingPipeline`                     |

These commands always exit `0`: `compliance-utility`, `clean-test-dbs` and `check-ports`.

### `cadence gxp`

GxP compliance documents. `docs/SDLC/` must only be changed through these commands.

#### `cadence gxp sync`

1. Runs `dotnet test Cadence.sln --no-restore --configuration Release` and writes `report_dotnet.trx`. Stops if tests fail.
2. Regenerates `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` (see `generate-rtm`).
3. Stages those files and `docs/SDLC/runs` with `git add`.

| Option      | Description                                                                                                            |
| ----------- | ---------------------------------------------------------------------------------------------------------------------- |
| `--dry-run` | Skips the tests, writes nothing and stages nothing. Fails if a mapped test failed in the existing `report_dotnet.trx`. |
| `--commit`  | After staging, runs `git commit -m "docs(rtm): sync GxP compliance docs with current test state"`.                     |

#### `cadence gxp validate`

Same as `cadence gxp sync --dry-run`. It does not compare the checked-in documents with freshly generated ones.

#### `cadence gxp verify`

Not implemented yet. Same as the `verify-gxp-signatures` tool command: it exits `1` without checking any signature.

### `cadence db`

Local database commands.

#### `cadence db reset`

Resets the local datastores:

1. PostgreSQL: drops and re-creates the schemas. The connection string comes from `POSTGRES_CONNECTION_STRING`, then `DATABASE_URL`, then a localhost default.
2. SQLite: deletes every `.db` and `.sqlite` file in the repository whose path does not contain `node_modules`, `bin` or `obj`.

It refuses to run when `CADENCE_ENV`, `ASPNETCORE_ENVIRONMENT` or `ENVIRONMENT` is `Production` or `Prod`. It also refuses connection strings that look remote (for example containing `prod`, `aws` or `azure`, or a non-local host). If a datastore cannot be reached, it prints a warning and continues. It exits `0` even when a reset step fails.

#### `cadence db migrate`

Runs `dotnet ef database update --project apps/execution/src/Cadence.Apps.Execution.csproj` in the current directory. Only the execution service is migrated. Use `scripts/manage_migrations.ps1` to add migrations.

#### `cadence db status`

Not implemented yet. Use `cadence doctor` to check datastore connectivity.

#### `cadence db seed`

Not implemented yet.

### `cadence settings`

#### `cadence settings export`

Same as the `extract-env-vars` tool command, including the `--check` option. It does not write `.env.example` or any docs.

### `cadence scaffold`

#### `cadence scaffold service <name>`

Runs `bash scripts/migrate_service_wizard.sh -ServiceName <name>` in the current directory. The script reads its first argument as the service name, so it receives `-ServiceName` instead of `<name>`. Use `cadence wizard migrate` instead. Needs `bash`, so it does not work on plain Windows.

#### `cadence scaffold adr <title>`

Creates `docs/adr/<yyyy-mm-dd>-<slug>.md` from a built-in template. The ADR number is one more than the highest `ADR-<n>:` found on the first line of existing ADRs, or 100 if there are none. Fails if the file already exists. Appends a link to `docs/adr/index.md` if that file exists.

The title is required. With make, pass it as `make adr TITLE="Use X for Y"`.

### `cadence setup`

#### `cadence setup credentials`

Not implemented yet. Copy `.env.example` to `.env` and fill in the values described in [ENVIRONMENT_VARIABLES.md](ENVIRONMENT_VARIABLES.md).

### `cadence record`

#### `cadence record setup-gdrive`

Not implemented yet.

#### `cadence record list`

Not implemented yet.

#### `cadence record run`

Not implemented yet.

### `cadence cdisc`

#### `cadence cdisc export`

Not implemented yet. The USDM JSON schemas in `packages/usdm-schemas/src` are maintained by hand.

### `cadence mcp`

#### `cadence mcp server`

Not implemented yet.

### `cadence wizard`

Interactive wizards. Each one runs a script in `scripts/` from the repository root: the `.sh` script with `bash`, or the `.ps1` script with PowerShell on Windows. The command returns the script's exit code, or `1` if the script is missing.

| Command                     | Script                   | Arguments                                 |
| --------------------------- | ------------------------ | ----------------------------------------- |
| `cadence wizard onboarding` | `setup_wizard`           | None                                      |
| `cadence wizard dotnet`     | `setup_dotnet_wizard`    | None                                      |
| `cadence wizard migrate`    | `migrate_service_wizard` | None. The script prompts for the service. |

What each wizard does is defined by its script, not by the CLI.
