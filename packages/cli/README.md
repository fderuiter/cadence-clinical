# Cadence CLI

Developer and CI command-line tool built with Spectre.Console. It runs the repository quality gates, test wrappers, database and GxP tooling, and scaffolding.

## Project

- Project: `packages/cli/src/Cadence.Cli.csproj` (assembly `cadence`; `PackAsTool` with command name `cadence`).
- `Program.cs`: command tree. `Commands/`: command classes. `Services/`: thin wrappers, most of which delegate to [packages/tooling_core](../tooling_core/README.md).
- The tool is not registered in `.config/dotnet-tools.json`, so run it with `dotnet run` (or `pnpm cadence`, which does the same).

## Usage

```bash
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command> [options]
dotnet run --project packages/cli/src/Cadence.Cli.csproj -- --help
```

Common commands:

| Command                                                                       | Purpose                                                                                                             |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `check [gate] [files]`                                                        | Runs all quality gates, or one gate such as `readmes`, `markdown`, `adrs`, `arch-drift`, `imports`, `dependencies`. |
| `fix`                                                                         | Runs the automatic fixers (`SelfHealService`).                                                                      |
| `test`                                                                        | Test runner wrapper.                                                                                                |
| `doctor`                                                                      | Checks the local toolchain and environment.                                                                         |
| `db reset`, `db migrate`, `db status`, `db seed`                              | Database utilities.                                                                                                 |
| `gxp sync`, `gxp validate`, `gxp verify`, `generate-rtm`                      | Requirements traceability and GxP reports.                                                                          |
| `cdisc export`                                                                | Placeholder: prints a message and exits (the `usdm-schemas` build script calls it).                                 |
| `scaffold service`, `scaffold adr`                                            | Scaffolding.                                                                                                        |
| `wizard onboarding`, `wizard migrate`, `wizard decommission`, `wizard dotnet` | Interactive wizards.                                                                                                |
| `mcp server`                                                                  | Placeholder: prints a start message and exits; no MCP server is implemented.                                        |

Validation commands such as `validate-readmes`, `validate-markdown`, `validate-imports`, `sync-openapi-spec` and `check-ports` are also available as top-level commands.

## Key dependencies

- Project references: `Cadence.Core.Domain`, `Cadence.Database`, `Cadence.Compliance`, `Cadence.Security`, `Cadence.ToolingCore`.
- Packages: `Spectre.Console`, `Spectre.Console.Cli`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Sqlite`.

## Referenced by

`tests/Cadence.ArchitectureTests.csproj` (for architecture scanning). No application references it.

## Tests

No dedicated CLI tests. Architecture rules cover the assembly: `dotnet test tests/Cadence.ArchitectureTests.csproj`.
