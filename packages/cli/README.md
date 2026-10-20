# Cadence CLI

Developer and CI command-line tool, published as a Native AOT binary. System.CommandLine parses the command line and Spectre.Console renders the output. It runs the repository quality gates, test wrappers, database and GxP tooling, and scaffolding.

## Project

- Project: `packages/cli/src/Cadence.Cli.csproj` (assembly `cadence`; `PackAsTool` with command name `cadence`).
- `Program.cs`: command tree, registered with `AddCommand` and `AddBranch` (the `cli-docs` gate reads these calls). `Commands/`: static classes for commands that take options or have their own logic. `Services/`: thin wrappers, most of which delegate to [packages/tooling_core](../tooling_core/README.md).
- The tool is not registered in `.config/dotnet-tools.json`, so run it with `dotnet run` (or `pnpm cadence`, which does the same).
- Time (K-TIME-3): the project bans `DateTime.Now/UtcNow`. `scaffold adr` takes today's date from `Cadence.Kernel.Time.SystemClock`.

## Usage

```bash
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command> [options]
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- --help
```

Common commands:

| Command                                                                              | Purpose                                                                                                                                                                   |
| ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `check [gate] [files]`                                                               | Runs all quality gates, or one gate such as `readmes`, `markdown`, `adrs`, `arch-drift`, `imports`, `dependencies`, `gxp-snapshot`, `baseline-ratchet`.                   |
| `fix`                                                                                | Runs the automatic fixers (`SelfHealService`).                                                                                                                            |
| `test`                                                                               | Test runner wrapper.                                                                                                                                                      |
| `ci [--job <job>] [--allow-skip] [--list]`                                           | Runs the jobs of `.github/workflows/ci.yml` locally with the same commands (`Services/CiPlan.cs`, `Services/LocalCiRunner.cs`).                                           |
| `doctor`                                                                             | Checks the local toolchain and environment.                                                                                                                               |
| `db reset`, `db migrate`, `db status`, `db seed`                                     | Database utilities.                                                                                                                                                       |
| `gxp sync`, `gxp snapshot`, `gxp validate`, `gxp diff`, `gxp verify`, `generate-rtm` | Requirements traceability and GxP reports. `gxp sync` validates a branch and writes a preview; only the integration owner's `gxp snapshot` writes `docs/SDLC` (ADR-2532). |
| `cdisc export`                                                                       | Not implemented: prints a message and exits `1`.                                                                                                                          |
| `scaffold service`, `scaffold adr`                                                   | Scaffolding.                                                                                                                                                              |
| `wizard onboarding`, `wizard migrate`, `wizard dotnet`                               | Interactive wizards.                                                                                                                                                      |
| `mcp server`                                                                         | Not implemented: prints a message and exits `1`.                                                                                                                          |
| `hook pre-tool`, `hook post-write`, `hook pre-invocation`, `hook stop`               | Agent lifecycle hooks run by `.agents/hooks.json` from `bin/cadence` (PRD-SYS-053); exit `2` blocks the step.                                                             |

Validation commands such as `validate-readmes`, `validate-markdown`, `validate-imports`, `sync-openapi-spec` and `check-ports` are also available as top-level commands.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Database`, `Cadence.Compliance`, `Cadence.Security`, `Cadence.ToolingCore`, `Cadence.Kernel.Abstractions`.
- Packages: `System.CommandLine`, `Spectre.Console`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Sqlite`.

## Referenced by

`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` (for architecture scanning). No application references it.

## Native AOT

`PublishAot` is on and no trim or AOT warning (IL2xxx/IL3xxx) is suppressed. Warnings are errors, so code that is
not AOT-safe fails `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`, which CI runs
(ADR-2249, ADR-2496). The Makefile publishes the same way to `./bin/cadence`.

## Tests

- `tests/Cadence.UnitTests/Packages/CliCommandLineTests.cs`: parses the documented invocations with `Program.BuildRootCommand()` and
  checks that unknown input, missing arguments and bare groups are rejected and that every command has an action and
  a description.
- `tests/Cadence.UnitTests/Packages/LocalCiPlanTests.cs`: reads `.github/workflows/ci.yml` and fails when a job or step
  is added, renamed, removed or runs a different command without `CiPlan` following. `LocalCiRunnerTests.cs` covers the
  run rules (`if: always()`, covered steps, missing tools, PostgreSQL, job needs, changed files).
- `tests/Cadence.UnitTests/Packages/CliDocsParityTests.cs`: the `cli-docs` gate's command discovery.
- `tests/Cadence.UnitTests/Packages/AgentHookGuardTests.cs`: which commands and payloads the `hook` commands block or allow
  (`Services/AgentHookGuard.cs`).
- `tests/Cadence.UnitTests/Packages/GxpSnapshotPolicyTests.cs`: the `gxp-snapshot` gate (`Services/GxpSnapshotGuardService.cs`)
  and branch validation: two branches with differently tagged tests pass and merge without a conflict on the GxP documents.
- `tests/Cadence.UnitTests/Packages/BaselineRatchetServiceTests.cs`: the `baseline-ratchet` gate
  (`Services/BaselineRatchetService.cs`): growth fails without a `Baseline-Growth` trailer, and shrinks and reorders pass.
- Architecture rules cover the assembly: `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`.
