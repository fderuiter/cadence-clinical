# Continuous Integration

How CI is set up, which checks run where, and how to reproduce and fix each failure locally. The Cadence CLI runs as `pnpm cadence <command>` (a wrapper for `dotnet run --project packages/cli/src/Cadence.Cli.csproj --`).

## Workflows

| Workflow                  | Runs on                                       | What it does                                                                                                          |
| ------------------------- | --------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `ci.yml`                  | Pull requests and pushes to `main`            | Style and lint, security audit, .NET tests, frontend tests, GxP compliance report check                               |
| `conflict-check.yml`      | Pull requests (opened, synchronize, reopened) | Fails when the PR conflicts with `main`                                                                               |
| `production-pipeline.yml` | Called by `ci.yml` on pushes to `main`        | Vulnerability audit, regenerates the RTM and IQ/OQ/PQ report, uploads them with SHA-256 checksums as release evidence |
| `deploy-docs.yml`         | Called by `ci.yml` on pushes to `main`        | Builds and publishes the VitePress docs site                                                                          |

### `ci.yml` jobs

1. **Style and Lint Verification**: `dotnet format --verify-no-changes`, the Cadence quality gates, architecture tests (ArchUnitNET), a Native AOT publish of the CLI that fails on any trim or AOT warning, ADR, Markdown, OpenAPI, schema-doc and traceability checks, code duplication, Hadolint, ShellCheck, CODEOWNERS coverage, the docs build and frontend ESLint/Prettier.
2. **Security and Vulnerability Audit**: static security audit, secret detection, DEID scan and NuGet/npm vulnerability audit.
3. **.NET 10 C# xUnit Verification**: `dotnet build` (warnings are errors), then `dotnet test Cadence.sln`, which runs the unit, integration, architecture and E2E test projects against a `postgres:16` service container (`CADENCE_TEST_POSTGRES`), so the database tests run instead of skipping.
4. **Frontend Unit Tests**: Vitest across the pnpm workspace.
5. **GxP Compliance & Qualification Reporting**: regenerates the RTM and IQ/OQ/PQ report with `pnpm cadence gxp sync` and fails if they differ from the committed files.

Only on pushes to `main`: accessibility E2E tests, the container build and Trivy scan, the production pipeline and the docs deploy. They are kept off pull requests to save Actions minutes.

> [!IMPORTANT]
> GitHub Actions does not run jobs for this repository until 2027: every check on a pull request fails within seconds
> without running. Until then, run `pnpm cadence ci` (or `make ci`) before merging. It runs the jobs above on your
> machine with the same commands and needs PostgreSQL for the .NET tests ([cli.md](cli.md#cadence-ci)). A full run
> takes about 20 minutes: `CADENCE_TEST_POSTGRES=<connection string> pnpm cadence ci`. In a shell without a terminal
> (an AI agent, a script, a pipe), add `TERM=dumb` or run it under `script -qc "…" /dev/null`, or only the banner prints.

## Quality gates

`pnpm cadence check` runs all 21 quality gates. The pre-commit hook runs a quicker variant (below), and CI runs each gate as well. Run one gate with `pnpm cadence check <gate>`; an unknown gate name fails.

| Gate (short name)     | What it checks                                                                             |
| --------------------- | ------------------------------------------------------------------------------------------ |
| `dotnet-format`       | Whitespace formatting against `.editorconfig`                                              |
| `dotnet-format-style` | Roslyn code-style rules                                                                    |
| `clean-secrets`       | Normalizes `.secrets.baseline`                                                             |
| `duplication`         | Duplicated code blocks over 15 normalized lines                                            |
| `cli-docs`            | `docs/cli.md` matches the CLI's commands                                                   |
| `adrs`                | ADR schema and `docs/adr/index.md` consistency                                             |
| `arch-drift`          | Architecture diagrams match the solution                                                   |
| `sanitizer`           | Requirement tag prefixes in tests                                                          |
| `markdown`            | Markdown structure, links, and paths into .NET test project folders (`tests/Cadence.*`) that no longer exist |
| `env-vars`            | Environment variables documented in `docs/ENVIRONMENT_VARIABLES.md`                        |
| `readmes`             | Each project has a README                                                                  |
| `imports`             | No cross-service persistence references                                                    |
| `security`            | Static security audit and secret patterns                                                  |
| `deid`                | PHI/PII in tracked files outside tests, docs, scripts and similar folders (all 14 detector categories), ratcheted by `.deid-baseline.txt`; a line that is not PHI takes a `deid-ignore` comment |
| `openapi`             | `docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi` |
| `api-clients`         | The web API clients typecheck against their generated OpenAPI types                        |
| `schema-doc`          | `docs/schema` matches the EF Core models (skipped by the pre-commit hook)                   |
| `path-patterns`       | Files live in allowed directories                                                          |
| `contracts`           | Hexagonal port/adapter contracts                                                           |
| `query-linter`        | No interpolated raw SQL (`FromSqlRaw($...)`, `ExecuteSqlRaw($...)`)                        |
| `vulnerabilities`     | NuGet dependency vulnerabilities (npm is not audited yet)                                  |

`pnpm cadence check rtm` (not part of the 21) validates the traceability matrix without regenerating it.

## Pre-commit hook

`scripts/setup_wizard.sh` points git at `.githooks/` (`git config core.hooksPath .githooks`). The `pre-commit` hook (`pre-commit.ps1` on Windows) runs:

1. `dotnet format Cadence.sln --verify-no-changes --include <staged .cs files>`, skipped when no C# file is staged.
2. `pnpm cadence check --pre-commit`: all 21 gates, with two shortcuts. The two format gates are skipped because step 1 covers the staged files. The `openapi` gate only checks `docs/api` against the committed `docs/openapi` specs, because exporting the specs from the services takes minutes. `pnpm cadence check`, `make verify` and CI run every gate in full.

There is no pre-push hook. The traceability matrix is checked in CI by the compliance job; run `pnpm cadence gxp sync` before pushing test changes.

```bash
# Run the same checks without committing
dotnet format Cadence.sln --verify-no-changes
pnpm cadence check

# Enable the hooks by hand if setup was skipped
git config core.hooksPath .githooks
```

Don't bypass the hook with `--no-verify`; fix the failing gate instead.

## CI failure runbook

CI runs in `.github/workflows/ci.yml`. `pnpm cadence ci` reproduces the whole workflow; each check can also be reproduced on its own with the command in the last column.

| Failing job or step                                          | Usual cause                                                                       | Reproduce and fix locally                                                                                        |
| ------------------------------------------------------------ | --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| Style › Verify .NET C# code formatting                       | Code doesn't match `.editorconfig` or analyzer style                              | `dotnet format Cadence.sln`, then commit                                                                         |
| Style › Run frontend style and lint                          | ESLint or Prettier findings in `apps/web`, `apps/subject-portal` or `packages/ui` | `pnpm format` and `pnpm -r lint`                                                                                 |
| Style › Run Cadence quality sentinels, or a single gate step | One of the 21 gates failed; the log names it                                      | `pnpm cadence check <gate>`                                                                                      |
| Style › Run architecture tests                               | A layer dependency rule was broken                                                | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`                                                             |
| Style › Publish the CLI as Native AOT                        | The CLI, or code it calls, raised a trim or AOT warning (IL2xxx/IL3xxx)           | `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`; fix the call, never add `NoWarn`   |
| Style › Run Hadolint or ShellCheck                           | Dockerfile or shell script findings                                               | `hadolint <Dockerfile>`, `shellcheck <script>`                                                                   |
| Style › Verify OpenAPI specification drift                   | An endpoint changed without regenerating the specs                                | `pnpm cadence sync-openapi-spec` and `pnpm cadence generate-api-docs`, then commit `docs/openapi` and `docs/api` |
| Style › Run ADR validation                                   | ADR missing, malformed or not indexed                                             | `pnpm cadence scaffold adr`, then `pnpm cadence check adrs`                                                      |
| Security › dependency audit                                  | A vulnerable NuGet or npm package                                                 | `pnpm cadence check vulnerabilities`; upgrade in `Directory.Packages.props` or `package.json`                    |
| Frontend Unit Tests                                          | A Vitest failure                                                                  | `pnpm -r test`                                                                                                   |
| .NET 10 C# xUnit Verification                                | A build warning (warnings are errors) or an xUnit failure                         | `dotnet build Cadence.sln` and `dotnet test Cadence.sln`                                                         |
| GxP Compliance › Assert No Documentation Git Diff            | RTM docs not regenerated after test changes                                       | `pnpm cadence gxp sync`, then commit `docs/SDLC/`                                                                |
| Check for Merge Conflicts                                    | The PR conflicts with `main`                                                      | `git merge origin/main` and resolve                                                                              |
