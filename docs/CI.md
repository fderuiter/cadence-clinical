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

1. **Workflow Action Pinning & Actionlint Gate**: runs `rhysd/actionlint` and `python3 scripts/check-action-pinning.py` to enforce GitHub Actions supply-chain security (pinning third-party action usages to full 40-character commit SHAs) and workflow YAML syntax.
2. **Style and Lint Verification**: `dotnet format --verify-no-changes`, the Cadence quality gates, architecture tests (ArchUnitNET), a Native AOT publish of the CLI that fails on any trim or AOT warning, ADR, Markdown, OpenAPI, schema-doc and traceability checks, code duplication, Hadolint, ShellCheck, CODEOWNERS coverage, the docs build and frontend ESLint/Prettier.
3. **Security and Vulnerability Audit**: static security audit, secret baseline immutability verification (`git diff --exit-code .secrets.baseline`), secret detection, DEID scan and NuGet/npm vulnerability audit.
4. **.NET 10 C# xUnit Verification**: `dotnet build` (warnings are errors), then `dotnet test Cadence.sln`, which runs the unit, integration, architecture and E2E test projects against a PostgreSQL 18 service container pinned to `PostgresTestImage.Image` (`CADENCE_TEST_POSTGRES`), so the database tests run instead of skipping. Locally either `CADENCE_TEST_POSTGRES` or `CADENCE_TEST_DOCKER=1` works for every database fixture ([TESTING.md](TESTING.md#tests-that-need-postgresql)), and `cadence ci` fails when any integration or E2E test was skipped or did not execute, unless `--allow-skip` is given.
5. **Frontend Unit Tests**: Vitest across the pnpm workspace.
6. **GxP Compliance & Qualification Reporting**: `Generate Compliance Reports` runs `pnpm cadence gxp sync`, which writes a preview of the RTM and IQ/OQ/PQ report to `TestResults/gxp/preview/` and fails on unknown requirement IDs, failing tagged tests and skipped database tests; it writes `docs/SDLC` only with `CADENCE_GXP_SNAPSHOT=1`. `Check GxP Snapshot Policy` runs `pnpm cadence check gxp-snapshot`, which fails when the branch changes the committed RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` (ADR-2532). With `CADENCE_GXP_SNAPSHOT=1` it instead compares the regenerated documents with the snapshot (`cadence gxp diff`, which ignores the evidence block naming the tested revision, time, database and runner).

Only on pushes to `main`: accessibility E2E tests, the container build and Trivy vulnerability scan (enforcing fail-closed exit code 1 with SARIF report upload to GitHub Security tab), the production pipeline and the docs deploy. They are kept off pull requests to save Actions minutes.

> [!IMPORTANT]
> GitHub Actions does not run jobs for this repository until 2027: every check on a pull request fails within seconds
> without running. Until then, run `pnpm cadence ci` (or `make ci`) before merging. It runs the jobs above on your
> machine with the same commands and needs PostgreSQL for the .NET tests ([cli.md](cli.md#cadence-ci)). A full run
> takes about 20 minutes: `CADENCE_TEST_POSTGRES=<connection string> pnpm cadence ci`. In a shell without a terminal
> (an AI agent, a script, a pipe), add `TERM=dumb` or run it under `script -qc "…" /dev/null`, or only the banner prints.

## Quality gates

`pnpm cadence check` runs all 22 quality gates. The pre-commit hook runs a quicker variant (below), and CI runs each gate as well. Run one gate with `pnpm cadence check <gate>`; an unknown gate name fails.

| Gate (short name)     | What it checks                                                                                                                                                                                  |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet-format`       | Whitespace formatting against `.editorconfig`                                                                                                                                                   |
| `dotnet-format-style` | Roslyn code-style rules                                                                                                                                                                         |
| `clean-secrets`       | Normalizes `.secrets.baseline` and enforces baseline immutability                                                                                                                               |
| `duplication`         | Duplicated code blocks over 15 normalized lines                                                                                                                                                 |
| `cli-docs`            | `docs/cli.md` matches the CLI's commands                                                                                                                                                        |
| `adrs`                | ADR schema and `docs/adr/index.md` consistency                                                                                                                                                  |
| `arch-drift`          | Architecture diagrams match the solution                                                                                                                                                        |
| `sanitizer`           | Requirement tag prefixes in tests                                                                                                                                                               |
| `markdown`            | Markdown structure, links, and paths into .NET test project folders (`tests/Cadence.*`) that no longer exist                                                                                    |
| `doc-index`           | Completeness of `docs/DOCUMENTATION_INDEX.md` across all repository markdown documents                                                                                                          |
| `env-vars`            | Environment variables documented in `docs/ENVIRONMENT_VARIABLES.md`                                                                                                                             |
| `readmes`             | Each project has a README                                                                                                                                                                       |
| `imports`             | No cross-service persistence references                                                                                                                                                         |
| `security`            | Static security audit and secret patterns                                                                                                                                                       |
| `deid`                | PHI/PII in tracked files outside tests, docs, scripts and similar folders (all 14 detector categories), ratcheted by `.deid-baseline.txt`; a line that is not PHI takes a `deid-ignore` comment |
| `openapi`             | `docs/openapi` matches what the .NET services serve, and `docs/api` matches `docs/openapi`                                                                                                      |
| `api-clients`         | The web API clients typecheck against their generated OpenAPI types                                                                                                                             |
| `schema-doc`          | `docs/schema` matches the EF Core models (skipped by the pre-commit hook)                                                                                                                       |
| `path-patterns`       | Files live in allowed directories                                                                                                                                                               |
| `contracts`           | Hexagonal port/adapter contracts                                                                                                                                                                |
| `query-linter`        | No interpolated raw SQL (`FromSqlRaw($...)`, `ExecuteSqlRaw($...)`)                                                                                                                             |
| `vulnerabilities`     | NuGet dependency vulnerabilities (npm is not audited yet)                                                                                                                                       |
| `rtm`                 | Requirement tag integrity and traceability matrix alignment                                                                                                                                     |
| `gxp-snapshot`        | The branch (or, in the pre-commit hook, the staged files) does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` unless `CADENCE_GXP_SNAPSHOT=1` (ADR-2532)                    |

`pnpm cadence check rtm` (or `gxp-rtm-validate`) runs Gate 22 to validate the requirement tags and integrity stamps without regenerating the matrix; it requires the matrix's test mapping to be current only with `CADENCE_GXP_SNAPSHOT=1`. `pnpm cadence check gxp-snapshot` (or `gxp-snapshot-guard`) runs the snapshot guard.

## Pre-commit hook

`scripts/setup_wizard.sh` points git at `.githooks/` (`git config core.hooksPath .githooks`). The `pre-commit` hook (`pre-commit.ps1` on Windows) runs:

1. `dotnet format Cadence.sln --verify-no-changes --include <staged .cs files>`, skipped when no C# file is staged.
2. `pnpm cadence check --pre-commit`: all 22 gates, with two shortcuts. The two format gates are skipped because step 1 covers the staged files. The `openapi` gate only checks `docs/api` against the committed `docs/openapi` specs, because exporting the specs from the services takes minutes. `pnpm cadence check`, `make verify` and CI run every gate in full.

Additionally, `.pre-commit-config.yaml` configures `rhysd/actionlint` and local `action-pinning-check` (`scripts/check-action-pinning.py`) hooks to ensure all GitHub Actions references in `.github/workflows/` are syntax-checked and pinned to 40-character commit SHAs.

There is no pre-push hook. Requirement tags and test results are checked in CI by the compliance job; run `pnpm cadence gxp sync` before pushing test changes, and commit nothing it generates.

```bash
# Run the same checks without committing
dotnet format Cadence.sln --verify-no-changes
pnpm cadence check
python3 scripts/check-action-pinning.py

# Enable the hooks by hand if setup was skipped
git config core.hooksPath .githooks
```

Don't bypass the hook with `--no-verify`; fix the failing gate instead.

## CI failure runbook

CI runs in `.github/workflows/ci.yml`. `pnpm cadence ci` reproduces the whole workflow; each check can also be reproduced on its own with the command in the last column.

| Failing job or step                                          | Usual cause                                                                       | Reproduce and fix locally                                                                                                                                                |
| ------------------------------------------------------------ | --------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Workflow Action Pinning & Actionlint Gate                    | Third-party action not pinned to 40-char SHA or actionlint YAML error             | `python3 scripts/check-action-pinning.py` and `pre-commit run actionlint --all-files`                                                                                    |
| Style › Verify .NET C# code formatting                       | Code doesn't match `.editorconfig` or analyzer style                              | `dotnet format Cadence.sln`, then commit                                                                                                                                 |
| Style › Run frontend style and lint                          | ESLint or Prettier findings in `apps/web`, `apps/subject-portal` or `packages/ui` | `pnpm format` and `pnpm -r lint`                                                                                                                                         |
| Style › Run Cadence quality sentinels, or a single gate step | One of the 21 gates failed; the log names it                                      | `pnpm cadence check <gate>`                                                                                                                                              |
| Style › Run architecture tests                               | A layer dependency rule was broken                                                | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`                                                                                           |
| Style › Publish the CLI as Native AOT                        | The CLI, or code it calls, raised a trim or AOT warning (IL2xxx/IL3xxx)           | `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64`; fix the call, never add `NoWarn`                                                           |
| Style › Run Hadolint or ShellCheck                           | Dockerfile or shell script findings                                               | `hadolint <Dockerfile>`, `shellcheck <script>`                                                                                                                           |
| Style › Verify OpenAPI specification drift                   | An endpoint changed without regenerating the specs                                | `pnpm cadence sync-openapi-spec` and `pnpm cadence generate-api-docs`, then commit `docs/openapi` and `docs/api`                                                         |
| Style › Run ADR validation                                   | ADR missing, malformed or not indexed                                             | `pnpm cadence scaffold adr`, then `pnpm cadence check adrs`                                                                                                              |
| Security › dependency audit                                  | A vulnerable NuGet or npm package                                                 | `pnpm cadence check vulnerabilities`; upgrade in `Directory.Packages.props` or `package.json`                                                                            |
| Frontend Unit Tests                                          | A Vitest failure                                                                  | `pnpm -r test`                                                                                                                                                           |
| .NET 10 C# xUnit Verification                                | A build warning (warnings are errors) or an xUnit failure                         | `dotnet build Cadence.sln` and `dotnet test Cadence.sln`                                                                                                                 |
| GxP Compliance › Generate Compliance Reports                 | An unknown requirement ID, a failing tagged test or a skipped database test       | `pnpm cadence gxp sync` with PostgreSQL; fix the tag or test                                                                                                             |
| GxP Compliance › Check GxP Snapshot Policy                   | The branch changed the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/`        | `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`; never commit them on a feature branch |
| Check for Merge Conflicts                                    | The PR conflicts with `main`                                                      | `git merge origin/main` and resolve                                                                                                                                      |
