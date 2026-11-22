# Cadence CLI

Developer and CI command-line tool, published as a Native AOT binary. System.CommandLine parses the command line and Spectre.Console renders the output. It runs the repository quality gates, test wrappers, database and GxP tooling, and scaffolding.

## Project

- Project: `packages/cli/src/Cadence.Cli.csproj` (assembly `cadence`; `PackAsTool` with command name `cadence`).
- `Program.cs`: command tree, registered with `AddCommand` and `AddBranch` (the `cli-docs` gate reads these calls). `Commands/`: static classes for commands that take options or have their own logic. `Services/`: the gate registry (`QualityGates.cs`: each gate's name, aliases, check and pre-commit behaviour; `cadence check`, its help text, the gate count and the gate tables in `docs/cli.md` and `docs/CI.md` come from it, the tables through `GateDocs.cs`), the local CI runner, the hooks and the GxP guards; most checks delegate to [packages/tooling_core](../tooling_core/README.md).
- The tool is not registered in `.config/dotnet-tools.json`, so run it with `dotnet run` (or `pnpm cadence`, which does the same).
- Output without a terminal: `Services/CliConsole.cs` runs first in `Main`. When standard output is redirected (a pipe, a log
  file, a git hook, an agent shell) or the terminal has no ANSI support, it swaps `AnsiConsole.Console` for a plain
  console: no escape codes or colours, not interactive, 120 columns. Spectre's own detection measured such a console as
  -1 columns wide under `TERM=linux` and dropped every markup line, including gate failures (#5417). No `TERM=dumb` or
  `script` wrapper is needed.
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `scaffold adr` and `scaffold post` take today's date from `Cadence.Kernel.Time.IClock` (`SystemClock.Instance`).

## Usage

```bash
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command> [options]
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- --help
```

Common commands:

| Command                                                                | Purpose                                                                                                                                                                                                                                                                                                                                                                                                               |
| ---------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `check [gate] [files] [--pre-commit] [--exclude <gate>]`               | Runs every gate in `Services/QualityGates.cs` (scoped to staged files under `--pre-commit`, or skipping `--exclude` gates), or one gate such as `prettier`, `eslint`, `workflow-security`, `rulesets`, `yaml`, `detect-secrets`, `readmes`, `markdown`, `adrs`, `demo-manifest`, `arch-drift`, `imports`, `dependencies`, `gxp-snapshot`, `baseline-ratchet`, `branch-freshness`, `architecture-tests`, `unit-tests`. |
| `fix [--full] [--openapi]`                                             | Runs the self-healing pipeline from any subdirectory (`SelfHealService`): C# and Prettier formatting, `.secrets.baseline` normalization, doc/ADR/CLI/demo index sync, and diff-based EF Core schema, OpenAPI, and web API client regeneration.                                                                                                                                                                        |
| `test`                                                                 | Test runner wrapper.                                                                                                                                                                                                                                                                                                                                                                                                  |
| `ci [--job <job>] [--allow-skip] [--list]`                             | Runs the pull-request jobs of `.github/workflows/ci.yml` locally (including `actionlint`), failing on missing tools or PostgreSQL unless `--allow-skip` is passed (`Services/CiPlan.cs`, `Services/LocalCiRunner.cs`).                                                                                                                                                                                                |
| `rulesets [--dry-run] [--repo <owner/repo>]`                           | Validates `.github/rulesets/*.json` against workflow definitions and reconciles them to GitHub Repository Rulesets via `gh api` (`Services/RulesetService.cs`).                                                                                                                                                                                                                                                       |
| `doctor`                                                               | Checks the local toolchain and environment (`.NET 10 SDK`, `pnpm` pin, `detect-secrets`, PostgreSQL).                                                                                                                                                                                                                                                                                                                 |
| `db reset`, `db migrate`                                               | Database utilities.                                                                                                                                                                                                                                                                                                                                                                                                   |
| `gxp sync`, `gxp snapshot`, `gxp validate`, `gxp diff`, `generate-rtm` | Requirements traceability and GxP reports (`--results-dir <dir>` reuses verified TRX + `*.database.txt` evidence). `gxp sync` validates a branch and writes a preview; only `gxp snapshot` writes `docs/SDLC` (ADR-2532).                                                                                                                                                                                             |
| `generate-demo-manifest [--check]`                                     | Exports `packages/demo-fixtures` from `packages/demo-fixtures/demo-seed.json`, `ExecutionDbSeeder.cs`, and `docker/cadence-realm.json` (`DemoManifestGeneratorService`), or verifies parity with `--check`.                                                                                                                                                                                                           |
| `scaffold service`, `scaffold adr`, `scaffold post`                    | Scaffolding.                                                                                                                                                                                                                                                                                                                                                                                                          |
| `wizard onboarding`, `wizard migrate`, `wizard dotnet`                 | Interactive wizards.                                                                                                                                                                                                                                                                                                                                                                                                  |
| `hook pre-tool`, `hook post-write`, `hook pre-invocation`, `hook stop` | Agent lifecycle hooks run by `.agents/hooks.json` from `bin/cadence` (PRD-SYS-053); exit `2` blocks the step.                                                                                                                                                                                                                                                                                                         |

Validation commands such as `validate-readmes`, `validate-markdown`, `validate-imports`, `sync-openapi-spec` and `check-ports` are also available as top-level commands.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Database`, `Cadence.Compliance`, `Cadence.Security`, `Cadence.ToolingCore`, `Cadence.Kernel.Abstractions`.
- Packages: `System.CommandLine`, `Spectre.Console`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Sqlite`.

## Referenced by

`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` (for architecture scanning). No application references it.

## Native AOT

`PublishAot` is on and no trim or AOT warning (IL2xxx/IL3xxx) is suppressed. Warnings are errors, so code that is
not AOT-safe fails `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, which CI runs
(ADR-2249, ADR-2496). Make targets that use the CLI delegate to `pnpm cadence` and share its on-demand build.

## Tests

- `tests/Cadence.UnitTests/Packages/CliCommandLineTests.cs`: parses the documented invocations with `Program.BuildRootCommand()` and
  checks that unknown input, missing arguments and bare groups are rejected and that every command has an action and
  a description.
- `tests/Cadence.UnitTests/Packages/LocalCiPlanTests.cs`: reads `.github/workflows/ci.yml` and fails when a job or step
  is added, renamed, removed or runs a different command without `CiPlan` following. `LocalCiRunnerTests.cs` covers the
  run rules (`if: always()`, covered steps, missing tools, PostgreSQL, job needs, changed files).
- `tests/Cadence.UnitTests/Packages/CliDocsParityTests.cs`: the `cli-docs` gate's command discovery.
- `tests/Cadence.UnitTests/Packages/QualityGateRegistryTests.cs`: runs every gate in the registry against a fixture
  that must make it fail, checks that gate names are unique, and that the generated gate tables are current.
- `tests/Cadence.UnitTests/Packages/PreCommitGuardrailsTests.cs`: covers the consolidated pre-commit guardrails
  (`.githooks/pre-commit`, `WorkflowSecurityValidatorService` staged-file wiring, `YamlSyntaxValidatorService`,
  `FileHygieneValidatorService`, `FrontendQualityGateService`, `DetectSecretsGateService`,
  `SecretsBaselineCleanerService`, and `CiPlan` parity).
- `tests/Cadence.UnitTests/Packages/AgentHookGuardTests.cs`: which commands and payloads the `hook` commands block or allow
  (`Services/AgentHookGuard.cs`).
- `tests/Cadence.UnitTests/Packages/GxpSnapshotPolicyTests.cs`: the `gxp-snapshot` gate (`Services/GxpSnapshotGuardService.cs`)
  and branch validation: two branches with differently tagged tests pass and merge without a conflict on the GxP documents.
- `tests/Cadence.UnitTests/Packages/BaselineRatchetServiceTests.cs`: the `baseline-ratchet` gate
  (`Services/BaselineRatchetService.cs`): growth fails without a `Baseline-Growth` trailer, and shrinks and reorders pass.
- `tests/Cadence.UnitTests/Packages/RulesetServiceTests.cs`: the `rulesets` gate and `cadence rulesets` reconciliation
  command (`Services/RulesetService.cs`), covering strict status checks, `CI Gate` upstream job coverage, workflow
  parity, `--dry-run`, and fail-closed `gh api` sync.
- `tests/Cadence.UnitTests/Packages/ScaffoldPostAndBlogGatesTests.cs`: `cadence scaffold post` (`Commands/ScaffoldCommands.cs`),
  documentation index auto-registration, and VitePress theme `.vue` boundary validation.
- `tests/Cadence.UnitTests/Packages/CliConsoleTests.cs`: plain output when there is no terminal (`Services/CliConsole.cs`),
  including the CLI run as a process with redirected output and `TERM=linux` or no `TERM`, which must print its failure.
- Architecture rules cover the assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.
