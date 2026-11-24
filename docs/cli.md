# Cadence Unified Developer CLI Reference

`cadence` is the developer CLI for this repository. It is a .NET 10 Native AOT console app: System.CommandLine parses the command line and Spectre.Console renders the output. The source is in `packages/cli/src`. Commands are registered in `packages/cli/src/Program.cs` with `AddCommand` and `AddBranch`; the `cli-docs` gate reads those calls to check that this page documents every command.

## Running the CLI

Run it through the root `package.json` script. Run it from the repository root.

```bash
pnpm cadence <command> [arguments] [options]
```

The script runs `node scripts/run-cli.js`, which executes the compiled Native AOT binary (`./bin/cadence`) and automatically compiles it on demand when needed. Examples:

```bash
pnpm cadence doctor
pnpm cadence check duplication
pnpm cadence gxp sync --dry-run
pnpm cadence scaffold post "Ship Kernel Phase 3"
```

Some commands also have a `make` target. Those targets use `pnpm cadence` and share its on-demand CLI build.

| `make` target                            | Runs                                          |
| ---------------------------------------- | --------------------------------------------- |
| `make doctor`                            | `cadence doctor --auto-fix`                   |
| `make ports`                             | `cadence check-ports`                         |
| `make dev`                               | `cadence dev`                                 |
| `make fix`                               | `cadence fix`                                 |
| `make check`                             | `cadence check`                               |
| `make action-pinning`                    | `cadence check workflow-security`             |
| `make test`                              | `cadence test`                                |
| `make verify`                            | `cadence check`, then `cadence test`          |
| `make ci`                                | `cadence ci`                                  |
| `make sync-gxp`, `make gxp-sync`         | `cadence gxp sync`                            |
| `make rtm`                               | `cadence gxp validate`                        |
| `make gxp-snapshot`                      | `CADENCE_GXP_SNAPSHOT=1 cadence gxp snapshot` |
| `make adr TITLE="..."`                   | `cadence scaffold adr "<title>"`              |
| `make post TITLE="..."`                  | `cadence scaffold post "<title>"`             |
| `make db-reset`, `make db-reset-offline` | `cadence db reset`                            |

`make wizard` runs `scripts/setup_wizard.sh` directly. That is the same script as `cadence wizard onboarding`.

System.CommandLine adds `-h|--help|-?` to every command, for example `pnpm cadence check --help`, and `--version` to the root command. There are no other global options. Every run prints a "Cadence" banner first.

An unknown command or option, a missing required argument, or a group such as `cadence gxp` without a subcommand prints the error and the command's help, and exits `1`. `cadence check` also rejects option-like tokens where it expects a gate or file name, so a typo such as `--pre-comit` fails instead of running as a gate name.

Exit code `0` means success. Any other exit code means failure. Some commands always exit `0`; the sections below say which.

## Commands

### `cadence doctor`

Checks the local environment. It reports:

- the .NET SDK version (from `dotnet --version`)
- whether `Directory.Packages.props` exists in the current directory
- whether the pnpm on the `PATH` runs and matches the `packageManager` pin in `package.json` (ADR-2508)
- whether `detect-secrets-hook` runs from `.cadence/venv`, `.venv`, `PATH`, or `uvx`
- whether `actionlint`, `hadolint`, and `shellcheck` run from `.cadence/bin` or `PATH`
- whether PostgreSQL answers on `127.0.0.1:5432`

It exits `1` when the .NET SDK is missing, pnpm is missing/crashes/differs from the pin, or `detect-secrets`, `actionlint`, `hadolint`, or `shellcheck` is unavailable, and `0` otherwise.

| Option           | Description                                                                          |
| ---------------- | ------------------------------------------------------------------------------------ |
| `-f\|--auto-fix` | Declared as "remediate missing databases and recoverable issues". Currently ignored. |

### `cadence check`

Runs the quality gates.

```bash
pnpm cadence check                       # every gate
pnpm cadence check <gate>                # one gate
pnpm cadence check <gate> <files>        # one gate, limited to these files
pnpm cadence check --exclude <gate>      # every gate except the named gate (repeatable)
pnpm cadence check --fail-fast           # stop at the first failing gate
pnpm cadence check --base <ref>          # scope branch diffs against <ref> (default: origin/main)
pnpm cadence check --push-before <sha>   # scope push diffs across <sha>..HEAD (falls back to git ls-files -z)
pnpm cadence check --pre-commit          # what the pre-commit hook runs
```

| Argument              | Description                                                                                                                                                                                                                              |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[gate]`              | One gate to run. Full or short name, for example `duplication` or `detect-code-duplication`. Underscores are treated as hyphens.                                                                                                         |
| `[files]`             | Files to check. Only the gates marked in the table below use them.                                                                                                                                                                       |
| `--exclude <gate>`    | Skip a gate by name or alias when running all gates (repeatable). Fails on an unknown gate name or when combined with `[gate]`.                                                                                                          |
| `--fail-fast`         | Stop at the first failing quality gate and exit `1`. Enabled by default with `--pre-commit` (pass `--fail-fast false` to run every remaining pre-commit gate).                                                                           |
| `--base <ref>`        | Base Git ref for revision-scoped gates (`deid`, `adrs`, `gxp-snapshot`, `baseline-ratchet`, `merge-readiness`, `branch-freshness`, and secret scanning; defaults to `origin/main`). Scans all tracked files when `HEAD` matches `<ref>`. |
| `--push-before <sha>` | Pre-push SHA used in CI push runs (`<sha>..HEAD`). Falls back to every tracked file (`git ls-files -z`) when `<sha>` is all zeros, a tag, or not an ancestor of `HEAD`. Mutually exclusive with `--base`.                                |
| `--pre-commit`        | What the pre-commit hook runs: the staged files are the `[files]`, fail-fast is enabled by default, and each gate runs, runs a quicker variant or is skipped as the last column of the table below says.                                 |

With no gate, every gate runs in the order below. Unscoped `cadence check` runs every gate and exits `1` if any gate
failed; `--fail-fast` (enabled by default with `--pre-commit`) stops at the first failing gate. An unknown gate name
prints the gate names and exits `1`.

The gates are defined once, in `packages/cli/src/Services/QualityGates.cs`. This table and the one in
[CI.md](CI.md#quality-gates) are generated from it by `pnpm cadence generate-cli-docs`; the `cli-docs` gate fails when
either is stale. `tests/Cadence.UnitTests/Packages/QualityGateRegistryTests.cs` runs every gate against a fixture that
must make it fail.

<!-- quality-gates:begin (generated from packages/cli/src/Services/QualityGates.cs by `pnpm cadence generate-cli-docs`; do not edit) -->

| Gate                  | Also accepted                                                          | Takes `[files]` | Pre-commit hook (`--pre-commit`)                                                                                                                                                      |
| --------------------- | ---------------------------------------------------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet-format`       |                                                                        | yes             | Quick: checks staged C# blobs; fails closed when `dotnet` is missing                                                                                                                  |
| `dotnet-format-style` |                                                                        | yes             | Quick: checks staged C# files and fails closed when `dotnet` is missing or unstaged edits overlap MSBuild inputs                                                                      |
| `prettier`            |                                                                        | yes             | Quick: streams staged JS, TS, Vue, CSS, JSON, Markdown and YAML blobs over stdin; fails closed when `pnpm` or `node_modules` is missing                                               |
| `eslint`              |                                                                        | yes             | Quick: lints staged JS, TS and Vue blobs over stdin; fails closed when `pnpm` or `node_modules` is missing                                                                            |
| `typecheck`           | `validate-typecheck`                                                   | yes             | Quick: runs `pnpm -r typecheck` when staged JS, TS, Vue, JSON or YAML files are present; fails closed when `pnpm` or `node_modules` is missing                                        |
| `actionlint`          | `validate-workflows`                                                   | yes             | Quick: validates staged `.github/workflows` and `.github/actions` blobs; fails closed when `actionlint` is missing                                                                    |
| `hadolint`            | `hadolint-docker`, `validate-dockerfiles`                              | yes             | Quick: lints staged `Dockerfile*` blobs; fails closed when `hadolint` is missing                                                                                                      |
| `yaml`                | `check-yaml`, `validate-yaml`                                          | yes             | Runs on the staged files                                                                                                                                                              |
| `trailing-whitespace` | `check-trailing-whitespace`                                            | yes             | Runs on the staged files                                                                                                                                                              |
| `end-of-file`         | `end-of-file-fixer`, `eof-newline`                                     | yes             | Runs on the staged files                                                                                                                                                              |
| `clean-secrets`       | `clean-secrets-baseline`                                               |                 | Quick: validates staged `baselines/secrets/` JSON shards without mutating the working tree                                                                                            |
| `detect-secrets`      | `secrets-scan`                                                         | yes             | Quick: scans materialized staged blobs instead of the working tree                                                                                                                    |
| `duplication`         | `detect-code-duplication`                                              | yes             | Runs on the staged files                                                                                                                                                              |
| `cli-docs`            | `validate-cli-docs`                                                    |                 | Runs                                                                                                                                                                                  |
| `adrs`                | `validate-adrs`                                                        |                 | Quick: checks staged files for architectural changes, accepting ADRs already added on the branch                                                                                      |
| `arch-drift`          | `validate-architecture-drift`                                          |                 | Runs                                                                                                                                                                                  |
| `sanitizer`           | `path-requirement-sanitizer`                                           | yes             | Quick: reports lowercase `@req:prd-…` tags in staged Markdown blobs without rewriting working-tree files                                                                              |
| `markdown`            | `validate-markdown`                                                    | yes             | Runs on the staged files                                                                                                                                                              |
| `env-vars`            | `validate-environment-variables`                                       |                 | Runs                                                                                                                                                                                  |
| `dev-env`             | `validate-dev-environment`, `dev-environment`                          |                 | Runs                                                                                                                                                                                  |
| `readmes`             | `validate-readmes`                                                     |                 | Runs                                                                                                                                                                                  |
| `codeowners`          | `validate-codeowners`                                                  |                 | Runs                                                                                                                                                                                  |
| `workflow-security`   | `validate-workflow-security`, `action-pinning`, `check-action-pinning` | yes             | Runs on the staged files                                                                                                                                                              |
| `rulesets`            | `validate-rulesets`                                                    |                 | Quick: validates staged `.github/rulesets` and `.github/workflows` blobs                                                                                                              |
| `imports`             | `validate-imports`                                                     |                 | Quick: validates staged `apps/<service>` C# blobs and `cross-service-isolation.baseline.txt` against cross-service imports and reflection lookups                                     |
| `permission-matrix`   | `validate-permission-matrix`, `rbac-matrix`                            |                 | Quick: checks staged RBAC sources and `permission-matrix.baseline.txt` against the shrink-only grant inventory                                                                        |
| `security`            | `audit-security`                                                       | yes             | Runs on the staged files                                                                                                                                                              |
| `deid`                | `deid-compliance-scan`                                                 | yes             | Runs on the staged files                                                                                                                                                              |
| `openapi`             | `auto-stage-openapi`                                                   | yes             | Quick: exports specs only for the services the staged C# files touch, otherwise checks staged `docs/api` against staged `docs/openapi`                                                |
| `api-clients`         | `validate-api-clients`                                                 | yes             | Quick: runs `generate:api --check` and `pnpm -r typecheck` when staged files touch `docs/openapi` or frontend inputs; fails closed when `pnpm` or `apps/web/tsconfig.json` is missing |
| `schema-doc`          | `auto-stage-schema-doc`                                                | yes             | Quick: runs the drift check when staged files include Entity Framework entities, migrations, configurations, or `docs/schema`                                                         |
| `path-patterns`       | `validate-path-patterns`                                               | yes             | Runs on the staged files                                                                                                                                                              |
| `query-linter`        | `query-style-linter`                                                   | yes             | Runs on the staged files                                                                                                                                                              |
| `vulnerabilities`     | `validate-vulnerabilities`                                             | yes             | Quick: validates `docs/SDLC/vulnerability_exclusions_ledger.json` offline and scans only ecosystems with staged manifests, lockfiles or ledger edits                                  |
| `dependencies`        | `validate-dependencies`                                                |                 | Runs                                                                                                                                                                                  |
| `doc-index`           | `validate-doc-index`                                                   |                 | Runs                                                                                                                                                                                  |
| `demo-manifest`       | `validate-demo-manifest`                                               | yes             | Quick: runs when staged files touch `packages/demo-fixtures`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` or `docker/cadence-realm.json`, otherwise skips it                     |
| `rtm`                 | `gxp-rtm-validate`                                                     |                 | Runs                                                                                                                                                                                  |
| `gxp-snapshot`        | `gxp-snapshot-guard`                                                   |                 | Quick: checks the staged files instead of the branch                                                                                                                                  |
| `baseline-ratchet`    | `baselines`                                                            |                 | Quick: checks staged files against `HEAD`; `CADENCE_BASELINE_GROWTH="<path> +<count> - <reason>"` admits growth                                                                       |
| `merge-readiness`     | `check-merge-readiness`                                                |                 | Skipped: skipped so pre-commit stays offline and fast; runs in `cadence check`, `cadence ci`, pull requests, and `merge_group`                                                        |
| `public-bundle`       | `validate-public-bundle`                                               |                 | Skipped: post-build gate; skipped in unscoped `check` only when all three `dist/` folders are absent, and enforced fail-closed in `docs-build` and `deploy-docs`                      |
| `branch-freshness`    | `check-branch-freshness`, `validate-branch-freshness`                  |                 | Skipped: skipped on commit so local work-in-progress commits stay offline; run by `cadence check` before push                                                                         |
| `architecture-tests`  | `arch-tests`                                                           | yes             | Quick: runs when staged files touch backend C#/MSBuild, architecture baselines, or test files; skips docs-only and non-test frontend asset commits                                    |
| `unit-tests`          | `dotnet-unit-tests`                                                    |                 | Skipped: full xUnit unit test suite; run by `cadence check` and CI's `dotnet-unit-tests` job                                                                                          |

<!-- quality-gates:end -->

The `gxp-snapshot` gate (also `gxp-snapshot-guard`, ADR-2532) fails when the generated RTM, the IQ/OQ/PQ report or
anything under `docs/SDLC/runs/` is changed: the staged files with `--pre-commit`, the `--push-before <sha>..HEAD` range
on pushes to `main` or `release/*`, and otherwise everything the branch changed since its merge base with `--base <ref>`
(default `origin/main`), uncommitted changes included. With `CADENCE_GXP_SNAPSHOT=1` it admits them, and outside the
pre-commit hook it then runs `cadence gxp diff` instead. A branch that changed them restores `main`'s copy with
`git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.

The `baseline-ratchet` gate (also `baselines`, #5318) fails when a ratchet baseline gate gained an entry: every tracked
`*.baseline.txt` or `*-baseline.txt` shard (comment lines ignored) and the `results` of `baselines/secrets/*.secrets.baseline.json`
shards, aggregated per gate as multisets, so a reorder, shard split, or shrink passes. With `--pre-commit` it compares the staged
index copies against `HEAD` and admits staged growth only when `CADENCE_BASELINE_GROWTH="<path> +<count> - <reason>"` is set. Outside `--pre-commit`
it rejects any uncommitted working-tree growth and inspects every commit in `compareBase..HEAD` (`--push-before <sha>` or the merge
base with `--base <ref>`, falling back from default `origin/main` to `refs/heads/main` when `origin/main` is absent), requiring each
commit that grows a baseline gate to carry a matching file-and-count-scoped `Baseline-Growth: <path> +<count> - <reason>` trailer covering the gate. When neither
`origin/main` nor `refs/heads/main` (or an explicit `--base <ref>`) exists, it fails closed both locally and in CI.

The `merge-readiness` gate (also `check-merge-readiness`) verifies that `--base <ref>` (`origin/main` by default) exists
locally and merges cleanly into `HEAD` in-memory via `git merge-tree --write-tree` without modifying the working tree or
index; on `--push-before` push runs it skips cleanly, and `--pre-commit` skips it so local commits are not blocked while
offline. When conflicts exist, it prints every conflicted file path and exits `1`.

The `deid` gate scans the given files, or the candidate files resolved from `--pre-commit`, `--push-before <sha>`, or
`--base <ref>` (default `origin/main`, falling back to every file git tracks via `git ls-files -z` when `HEAD` equals
the base ref, the base ref is missing, or `--push-before` is all zeros, a tag, or a non-ancestor), with the full
de-identification detector (`packages/deid`). Candidate files cover all 22 text extensions (including `.vue`, `.sql`,
and `.svg`), `AlwaysScannedNames` (`Dockerfile`, `Makefile`, `LICENSE`, `Pipfile`, `pyproject.toml`, `package.json`,
`pnpm-workspace.yaml`, `pnpm-lock.yaml`, `uv.lock`) and extensionless UTF-8 text files (such as `.gitignore` and
`.editorconfig`), spanning application code, authored `docs/`, root portal docs (`README.md`, `ARCHITECTURE.md`,
`AGENTS.md`, `CONTEXT.md`, `CONTRIBUTING.md`, `SECURITY.md`, `SUPPORT.md`, `LICENSE`), `.vue` templates, and `stores/`.
Tests, `baselines/`, `scripts/`, vendored standards (`docs/CDISC/`, `docs/NCI/`), generated OpenAPI/schema folders, and GxP
qualification snapshots (`docs/SDLC/runs/`, `Requirements_Traceability_Matrix.md`, `IQ_OQ_PQ_Execution_Report.md`) are
skipped. A source line containing `deid-ignore` is skipped by `deid` (never by `public-bundle`). Findings print as
`path:line:column` and category, never the matched value. Findings that predate the full detector are listed in
`baselines/deid/<unit>.baseline.txt` shards (`path|category|matched-token hash`), which may only shrink (empty shards are deleted):
a new finding or a stale entry fails the gate. Run `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid [files]` to subtract stale
entries from touched shards without adding new findings.

The `public-bundle` gate (also `validate-public-bundle`, ADR-2497) scans every generated text file in
`docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` after compilation with zero comment or pragma
exemptions, rejecting national identifiers, unapproved email domains, secrets, external assets, executable local backend
URLs, and missing or permissive `Content-Security-Policy` `<meta>` directives. It is skipped by `--pre-commit`, is
skipped by unscoped `cadence check` only when all three `dist/` folders are absent (partial `dist/` output fails
closed), and always fails closed on direct `cadence check public-bundle` calls in `docs-build` and `deploy-docs.yml`.

The `branch-freshness` and `unit-tests` gates are skipped by `--pre-commit` so local commits
stay fast and offline, while `architecture-tests` runs in `--pre-commit` whenever staged files touch backend C#/MSBuild,
architecture baselines, or test files (skipping docs-only and non-test frontend asset commits), and all three run in full
during `pnpm cadence check`: `branch-freshness` fails when `origin/main` (or
`--base <ref>`) is missing or not an ancestor of `HEAD` (skipping post-push `--push-before` runs), `architecture-tests`
runs `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, and `unit-tests` runs
`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`. The `demo-manifest` gate runs in `--pre-commit` only when staged
files touch `packages/demo-fixtures`, `DevelopmentDemoData.cs`, `ExecutionDbSeeder.cs` or `docker/cadence-realm.json`.

See [CI.md](CI.md#quality-gates) for the list of gates and what each one checks.

### `cadence test`

Runs `dotnet test Cadence.sln` from the repository root.

| Option              | Description                                                                                     |
| ------------------- | ----------------------------------------------------------------------------------------------- |
| `-f\|--fast`        | Adds `--filter "Category!=Integration"`: every test project except the integration tests.       |
| `-i\|--integration` | Runs `tests/Cadence.IntegrationTests` only. It needs PostgreSQL (see [TESTING.md](TESTING.md)). |

With no option, all tests run. If both options are given, `--fast` wins.

### `cadence ci`

Runs the pull-request validation jobs of `.github/workflows/ci.yml` locally (#5017). Before running any job,
`cadence ci` inspects working-tree hygiene (`GitRevisionScopeService.GetWorkingTreeChanges`) and fails closed on
`merge-readiness` if staged, unstaged, or untracked edits exist unless `--include-uncommitted` is passed (which copies
`.git/index` to a temporary index file and synthesizes an ephemeral snapshot commit on `HEAD` without mutating your
checkout). It then fetches `--base <ref>` (`origin/main` by default, resolving remote branches into
`refs/remotes/<remote>/<branch>`, resolving commit SHAs, `HEAD~1`, and local tags without `git fetch`, or skipping fetch
with `--offline`), reports how many commits the head revision is behind the resolved base ref, synthesizes a merged
commit via `git merge-tree --write-tree` + `git commit-tree` (failing closed immediately with conflicted file paths if
`git fetch` or `git merge-tree` fails), pins synthesized commits under `refs/cadence-ci/<runId>`, checks out the merged
commit in a disposable detached worktree under `Path.GetTempPath()`, reuses read-only `.cadence/bin`, `.cadence/venv`,
and `.venv` toolchains from your repository checkout, runs all selected jobs inside the temporary worktree in CI order
(forwarding the resolved base ref to scoped steps), and removes the temporary ref and worktree afterward without
mutating your checkout.

```bash
pnpm cadence ci                          # fetch origin/main, synthesize merged worktree, and run all 11 PR jobs
pnpm cadence ci --include-uncommitted    # synthesize an ephemeral snapshot commit of uncommitted edits without mutating checkout
pnpm cadence ci --job container-security # validate Compose config, build and Trivy-scan all 18 .NET and 2 SCE images
pnpm cadence ci --job dotnet-tests       # one job (repeat --job for more)
pnpm cadence ci --job compliance         # a job and the jobs it needs (native-aot-build, unit-tests, dotnet-tests)
pnpm cadence ci --job accessibility-e2e  # the Playwright E2E and browser-to-database job alone
pnpm cadence ci --offline                # air-gapped debugging: skip git fetch and merge against local origin/main
pnpm cadence ci --list                   # what each step runs locally, without running it
```

| Option                  | Description                                                                                                                                                                                                                                                                                   |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `-j\|--job <job>`       | Runs this job and the jobs it needs. Jobs: `actionlint`, `native-aot-build`, `lint-and-static`, `dotnet-quality`, `docs-build`, `security`, `container-security`, `unit-tests`, `dotnet-tests`, `accessibility-e2e`, `compliance`.                                                            |
| `--allow-missing-tools` | Skips steps whose required binary (`actionlint`, `hadolint`, `shellcheck`, `detect-secrets-hook`, `docker`, `trivy`, `bash`) or Docker daemon is unavailable with setup guidance, while still requiring PostgreSQL and `*.database.txt` database test evidence.                               |
| `--allow-db-skip`       | Accepts a run whose PostgreSQL tests or `*.database.txt` fixture metadata did not execute (without PostgreSQL or with incomplete TRX/metadata), while still requiring all external tool binaries and the Docker daemon.                                                                       |
| `--allow-skip`          | Shorthand for `--allow-missing-tools --allow-db-skip`: skips steps whose required binary or Docker daemon is unavailable with setup guidance, and accepts a run without PostgreSQL or complete database test evidence.                                                                        |
| `--base <ref>`          | The ref fetched into `refs/remotes/<remote>/<branch>` (or resolved locally without `git fetch` for commit SHAs, `HEAD~1`, and tags; skipped with `--offline`), merged for the temporary worktree, used to scope `container-security`, and forwarded to scoped checks. Default: `origin/main`. |
| `--offline`             | Skips `git fetch` for explicit air-gapped debugging while still synthesizing a detached merged worktree against the local `--base <ref>`.                                                                                                                                                     |
| `--include-uncommitted` | Copies `.git/index` to a temporary index, stages staged, unstaged, and untracked edits, and synthesizes an ephemeral snapshot commit for the merged worktree without mutating your checkout.                                                                                                  |
| `--list`                | Prints each step and what it runs locally, then exits `0`.                                                                                                                                                                                                                                    |

It follows the workflow's rules: inside a job, a failed step stops the later steps unless CI gives them `if: always()`,
and the compliance job runs only when the frontend and .NET test steps passed. The run itself never stops early. It
ends with one line per step (passed, failed, covered, skipped or not run, with its time) and exits `1` if any step
failed.

Where it differs from CI:

- **PostgreSQL and fixture metadata are required.** CI gives the .NET tests a database, so without `CADENCE_TEST_POSTGRES` or
  `CADENCE_TEST_DOCKER=1` ([TESTING.md](TESTING.md#tests-that-need-postgresql)) the test step fails before it starts
  instead of passing with every database test skipped. After `dotnet-tests` passes, both `LocalCiRunner` and `Fail the run when a PostgreSQL test did not execute` (`SyncGxPService.VerifyDatabaseEvidence`) verify every mandatory database test across `tests/` (`Cadence.IntegrationTests` and `Cadence.E2ETests`) against the TRX files in `TestResults/gxp` and require `*.database.txt` fixture metadata for every database assembly (#5325),
  except the opt-in lineage benchmark and browser correction check listed in `DatabaseTestEvidence.OptInTests` (the
  browser correction check runs with `CADENCE_TEST_BROWSER=1` in `accessibility-e2e`, which scopes out full-repository TRX/`*.database.txt` discovery so `--job accessibility-e2e` runs standalone). `--allow-db-skip` (or `--allow-skip`) accepts incomplete database evidence outside snapshot mode.
- **Container builds and Trivy scans follow branch diffs.** `container-security` validates `docker compose -f docker/docker-compose.yml config -q`, builds all 18 `.NET` services plus
  `sce-python` and `sce-r`, and runs `trivy image --exit-code 1 --ignore-unfixed --pkg-types os,library --severity CRITICAL,HIGH`
  against each image. On a default `pnpm cadence ci` run it resolves changed files via `GitRevisionScopeService.ResolveFileScanScope`,
  skipping when a branch diff touches no Dockerfile, `docker/*`, `.dockerignore`, `.github/dependabot.yml`, `.github/workflows/ci.yml`,
  `.NET` source (`apps/**/*.cs`, `packages/**/*.cs`), or MSBuild/NuGet/analyzer trigger, and running unconditionally when the scope falls back to all tracked
  files or when `--job container-security` is requested.
- **Checks already run once are not repeated.** `actionlint` runs `actionlint`, `lint-and-static` runs Prettier, ESLint, Hadolint, ShellCheck, `markdown` and `api-clients`,
  `dotnet-quality` runs `cadence check --exclude prettier --exclude eslint --exclude actionlint --exclude hadolint --exclude detect-secrets --exclude api-clients --exclude vulnerabilities --exclude public-bundle --exclude architecture-tests --exclude unit-tests`
  (followed by its dedicated `Cadence.ArchitectureTests` step, while `Cadence.UnitTests` runs in `dotnet-tests`),
  `docs-build` builds the VitePress portal and co-hosted interactive demos and runs `public-bundle`,
  and `security` runs `detect-secrets` and `vulnerabilities`. Steps in `security` whose gate already ran inside
  `Run Cadence quality sentinels` (the static security audit, secrets and DEID) are reported as _covered_ when
  `Run Cadence quality sentinels` passed; if that step failed, each runs on its own so the summary shows which gate
  failed. The Node install and UI build in later jobs are covered by earlier jobs.
  The documentation build (`pnpm docs:build`) runs the read-only `openapi`, `schema-doc`, `markdown` and `rtm`
  checks unconditionally on every build without touching `docs/SDLC` or `docs/schema`.
- **The tests run once.** The .NET test step writes its TRX files and `*.database.txt` fixture evidence to `TestResults/gxp`, and the compliance job's
  `Generate Compliance Reports` step builds the RTM and IQ/OQ/PQ report from them instead of running `gxp sync`, which
  would run every test again. It writes them to `TestResults/gxp/preview/` (to `docs/SDLC` only with
  `CADENCE_GXP_SNAPSHOT=1`), stages nothing, and fails on unknown requirement IDs, failing tagged tests, skipped database tests,
  and missing `*.database.txt` files (`SyncGxPService.VerifyDatabaseEvidence`, skipped with `--allow-db-skip` or `--allow-skip` outside snapshot mode).
  `Check GxP Snapshot Policy` (`cadence check gxp-snapshot`) then fails if the branch changed the committed reports. With
  `CADENCE_GXP_SNAPSHOT=1` it runs `cadence gxp diff` instead, which fails if the regenerated reports differ from the
  committed ones outside their evidence block, and puts the committed copies back when they match.
- **Required binaries and daemons must be available.** `actionlint` (`rhysd/actionlint` in CI), Hadolint, and ShellCheck (resolved
  from `.cadence/bin` or `PATH`, provisioned by `./scripts/setup_wizard.sh --minimal` or `pwsh scripts/setup_wizard.ps1 -Minimal`),
  `detect-secrets-hook` (resolved from `.cadence/venv`, `.venv`, or `PATH`), `docker` (with a reachable Docker daemon), `trivy`,
  and `bash` are not installed automatically by `cadence ci`: when any required binary or daemon is unavailable, the step fails with setup guidance
  so an incomplete environment cannot report green. Pass `--allow-missing-tools` (or `--allow-skip`) to record unavailable tool or Docker steps as skipped,
  with the setup hint, instead of failing.
- **Not run:** the Playwright browser install, `Upload Trivy scan results to GitHub Security tab` (SARIF upload to GitHub Code Scanning), and the jobs that act on GitHub itself
  (`ci-gate`, `classify`, `automerge`, `release-and-archive`, `deploy-docs`).
- **Clean checkouts required unless `--include-uncommitted` is passed.** `cadence ci` checks `GitRevisionScopeService.GetWorkingTreeChanges` before synthesizing the merged worktree and fails `merge-readiness` with exit `1` (listing dirty paths and instructing you to commit, stash, or pass `--include-uncommitted`) when staged, unstaged, or untracked edits exist. Passing `--include-uncommitted` copies `.git/index` to a temporary index file, stages all edits, and synthesizes an ephemeral snapshot commit pinned under `refs/cadence-ci/<runId>` (deleted when the worktree is disposed without running `git prune` or `git gc`), leaving your checkout untouched.
- **Single Native AOT publish reused across CLI jobs.** `native-aot-build` targets this machine's runtime (`linux-x64` in CI), since AOT does not cross-compile, publishes into `./bin` (`<worktreeRoot>/bin/cadence`), and sets `CADENCE_CLI_CACHE_HIT=true` for downstream `node scripts/run-cli.js` steps in `lint-and-static`, `docs-build`, and `security` so the compiled binary is reused without rebuilding.

`tests/Cadence.UnitTests/Packages/LocalCiPlanTests.cs` reads `ci.yml` and fails when a job or step is added, renamed or
removed, changes its command or `ForwardsBaseRef` flag, leaves `InProcess` null while declaring `InProcessCiText` or `InProcessDisplay`, or breaks `CoveredBy` gate coverage, until `packages/cli/src/Services/CiPlan.cs` follows.

### `cadence rulesets`

Validates the declarative GitHub Repository Rulesets in `.github/rulesets/*.json` (`main.json`, `release_branches.json`,
`version_tags.json`) against `.github/workflows/*.yml` and reconciles them to GitHub via `gh api`.

```bash
pnpm cadence rulesets --dry-run                         # validate locally and preview planned gh api operations (paused-actions)
pnpm cadence rulesets                                   # reconcile .github/rulesets/*.json using paused-actions profile
pnpm cadence rulesets --profile active-actions          # reconcile full rulesets including status checks and merge queue
pnpm cadence rulesets --repo fderuiter/cadence-clinical # target an explicit owner/repo
```

| Option                                       | Description                                                                                                                                                                                                                   |
| -------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--dry-run`                                  | Validates `CI Gate` upstream job coverage in `.github/workflows/ci.yml` (and `.github/rulesets/*.json` plus planned `gh api` operations for the selected profile when `.github/rulesets` is present) without mutating GitHub. |
| `--repo <owner/repo>`                        | Target GitHub repository (`owner/repo`). Defaults to `GITHUB_REPOSITORY`, then `git remote get-url origin`.                                                                                                                   |
| `--profile <paused-actions\|active-actions>` | Reconciliation profile. `paused-actions` (default while Actions runners are paused) omits `required_status_checks` and `merge_queue` from synced payloads; `active-actions` applies the full declarative ruleset payload.     |

Before making any API request, `cadence rulesets` runs the `rulesets` quality gate (`RulesetService.Validate`, which also validates staged index blobs under `cadence check --pre-commit`): `.github/rulesets/` must contain `main.json`, `release_branches.json`, and `version_tags.json`; every branch ruleset must restrict any `bypass_actors` entries to repository admins (`RepositoryRole` `5`) with `bypass_mode: "pull_request"` (`"always"` bypasses are rejected on branches), while `version_tags.json` requires `creation`, `required_signatures`, `deletion`, and `non_fast_forward` on `refs/tags/v*` with `bypass_actors` pinned to release admins (`RepositoryRole` `5`, `bypass_mode: "always"`); `main.json` must configure a `merge_queue` rule (`SQUASH`, `ALLGREEN`); every branch ruleset must set `strict_required_status_checks_policy: true`, pin required status checks to GitHub Actions (`integration_id: 15368`), require `CI Gate` and `Check for Merge Conflicts`, and match active `pull_request` and `merge_group` workflow jobs (`CI Gate` in `.github/workflows/ci.yml` must use `if: always()` and evaluate every pull-request validation job). When `.github/rulesets` is absent, `cadence rulesets --dry-run` still validates `CI Gate` upstream job coverage and exits `0` when valid (or `1` if `CI Gate` omits an upstream job), whereas `cadence check rulesets` and non-dry-run reconciliation both fail closed (`1`) without `.github/rulesets/*.json`.

Without `--dry-run`, it fetches `gh api --paginate repos/<owner>/<repo>/rulesets?per_page=100`, updates existing
rulesets by `name` (`PUT repos/<owner>/<repo>/rulesets/<id> --input <file>`), and creates missing ones
(`POST repos/<owner>/<repo>/rulesets --input <file>`). Requires `gh` authenticated with `Administration: write` on the
target repository, and exits `1` whenever validation fails or any `gh api` call returns a non-zero exit code or invalid
JSON.

### `cadence dev`

Starts the .NET Aspire AppHost: `dotnet run --project apps/apphost/src/Cadence.AppHost.csproj`. The AppHost starts the services, the datastores and the Aspire dashboard. The command waits until the AppHost exits and returns its exit code. It has no options.

### `cadence fix`

Resolves the repository root from any working directory (`Cadence.sln`) and runs the self-healing pipeline in order:

1. `dotnet format whitespace` and `dotnet format style` in the repository root (scoped to changed C# files by default; full `Cadence.sln` with `--full`).
2. Synthesized documentation marker regions (`GateDocs.Write`) across `docs/cli.md`, `docs/CI.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, `ARCHITECTURE.md`, `docs/OPERATIONAL_DATABASE.md`, and `docs/SDLC/Signature_Token_Cryptographic_Contract.md`, followed by root-wide Prettier auto-formatting across tracked JS, TS, Vue, CSS, JSON, Markdown and YAML files respecting `.prettierignore`.
3. `baselines/secrets/` shard normalization (`SecretsBaselineCleanerService.CleanSecretsBaseline`) and upper-case `@req:PRD-...` tag sanitization in `docs/**/*.md` (excluding `docs/SDLC/runs/`).
4. ADR index synchronization (`docs/adr/index.md`) and schema validation (`AdrValidatorService.ValidateAdrs` without git diff), documentation index sync (`docs/DOCUMENTATION_INDEX.md`), and demo study manifest sync (`packages/demo-fixtures`).
5. Diff-based EF Core schema documentation (`docs/schema/`): runs `generate-schema-doc` when EF Core entities, configurations, or migrations changed (or with `--full`), skipping otherwise.
6. Diff-based OpenAPI specifications, API Markdown pages, and web TypeScript client types: exports `docs/openapi/` and regenerates `docs/api/` when backend C# contracts changed (or with `--openapi` / `--full`), and runs `pnpm --filter web run generate:api` whenever OpenAPI specs are exported or `apps/web/src/api/generated/` is stale so `check api-clients` passes.

Options:

| Option      | What it does                                                                                                     |
| ----------- | ---------------------------------------------------------------------------------------------------------------- |
| `--openapi` | Force full OpenAPI specification export, `docs/api/` regeneration, and `pnpm --filter web run generate:api`.     |
| `--full`    | Force full regeneration of EF Core `docs/schema/`, all OpenAPI specs, `docs/api/`, and web OpenAPI client types. |

It exits `1` if any step fails.

### Tool commands

Single-purpose commands. Most are also run as gates by `cadence check`. None of them take arguments. Options are listed in the table.

Paths are relative to the repository root. "Calls" is the method the command runs, in `packages/tooling_core/src/Services` unless noted.

| Command                       | What it does                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Calls                                                                                     |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `detect-duplication`          | Finds duplicated blocks (over 15 normalized lines) across `.cs`, `.js`, `.ts`, `.vue` and `.css` files under `apps/` and `packages/` (excluding `/generated/`, `.d.ts`, `apps/web/src/api/types.ts`, and `packages/ui/src/types/cdash.ts`). Writes `duplication_summary.json` and exits `1` when any duplicate is found.                                                                                                                                                                      | `DuplicationDetectorService.DetectDuplication`                                            |
| `validate-imports`            | Fails when a `.cs` file in one `apps/<service>` has `using Cadence.Apps.<other service>`.                                                                                                                                                                                                                                                                                                                                                                                                     | `ImportValidatorService.ValidateImports`                                                  |
| `validate-dependencies`       | Validates JSON syntax and structure across workspace `package.json` and `.config/dotnet-tools.json` manifests (failing closed on malformed JSON) and fails when any `package.json` lists `node-forge`, `elliptic` or `jose`, or places test harness packages (`axe-core`, `@axe-core/playwright`, `vitest`, `@vue/test-utils`, `jsdom`, `fake-indexeddb`, `msw`) in runtime `dependencies`. Passes if no manifests exist.                                                                     | `DependencyValidatorService.ValidateDependencies`                                         |
| `validate-path-patterns`      | Fails on directories named `tmp`, `temp`, `scratch` or `old_src`, `.vue` files outside `apps/`, `packages/` and `docs/.vitepress/`, and `.sh` files outside `scripts/`.                                                                                                                                                                                                                                                                                                                       | `PathPatternValidatorService.ValidatePathPatterns`                                        |
| `validate-readmes`            | Fails when a direct subdirectory of `apps/` or `packages/` has no `README.md`, or its README has no `# ` title.                                                                                                                                                                                                                                                                                                                                                                               | `ReadmeValidatorService.ValidateReadmes`                                                  |
| `validate-codeowners`         | Fails when `.github/CODEOWNERS` is missing or empty.                                                                                                                                                                                                                                                                                                                                                                                                                                          | `CodeownersValidatorService.ValidateCodeowners`                                           |
| `validate-workflow-security`  | Validates `.github/workflows/*.{yml,yaml}` and `.github/actions/**/action.{yml,yaml}` for 40-hex commit SHAs with version comments, `sha256:` digests on `docker://` refs, `persist-credentials: false` on `actions/checkout`, typed non-interpolated `workflow_dispatch` inputs, ref-guarded dispatch triggers, and least-privilege permissions.                                                                                                                                             | `WorkflowSecurityValidatorService.ValidateWorkflowSecurity`                               |
| `validate-markdown`           | Checks workspace links and `#` heading spacing in `docs/**/*.md`.                                                                                                                                                                                                                                                                                                                                                                                                                             | `MarkdownValidatorService.ValidateMarkdown`                                               |
| `query-linter`                | Scans `.cs` files outside `tests/`. Fails on interpolated `FromSqlRaw($...)` or `ExecuteSqlRaw($...)`, and on `== true` or `== false`.                                                                                                                                                                                                                                                                                                                                                        | `QueryLinterService.LintQueries`                                                          |
| `extract-env-vars`            | Extracts environment variable and screaming-snake-case `IConfiguration` reads in `.cs`, `.py`, and frontend `import.meta.env.VITE_*` files, verifies bidirectional parity between `.env.example` and concrete keys in `docs/ENVIRONMENT_VARIABLES.md`, and checks that every repository file path cited in `docs/ENVIRONMENT_VARIABLES.md` exists on disk. Exits `1` on drift when `--check` is passed. Never writes files.                                                                   | `EnvVarExtractorService.ExtractAndValidateEnvVars`                                        |
| `audit-security`              | Scans source files, `.vue` templates and `scripts/` for secret patterns (private keys, cloud credentials, API tokens, hardcoded compliance secrets).                                                                                                                                                                                                                                                                                                                                          | `SecurityAuditorService.AuditSecurity`                                                    |
| `validate-public-bundle`      | Verifies built `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` text artifacts with zero comment or pragma exemptions, rejecting national IDs, unapproved emails, secrets, external assets, executable localhost URLs, and missing/permissive CSP headers. This is the `public-bundle` gate.                                                                                                                                                                           | `PublicBundleValidatorService.ValidatePublicBundles`                                      |
| `validate-vulnerabilities`    | Audits direct and transitive NuGet and pnpm workspace dependencies against active exemptions in `docs/SDLC/vulnerability_exclusions_ledger.json`. Fails closed when any scanner is missing, unrestored, times out or emits invalid JSON, or when unapproved vulnerabilities exist.                                                                                                                                                                                                            | `VulnerabilityAuditorService.AuditVulnerabilities`                                        |
| `clean-secrets-baseline`      | Splits/normalizes `baselines/secrets/config.json` and `baselines/secrets/<unit>.secrets.baseline.json` shards (deleting empty shards and legacy `.secrets.baseline`). Skips if absent. Option: `--check` (verify shard normalization without modifying files; exits `1` when unnormalized).                                                                                                                                                                                                   | `SecretsBaselineCleanerService.CleanSecretsBaseline`                                      |
| `validate-architecture-drift` | Validates fail-closed parity of `docker/docker-compose.yml`, `ServiceTopology`, Vite ports, `core` hosts, EF Core module schemas, `RequireElectronicSignature` endpoints, durable signature token stores, and `GxPAuditInterceptor` hosts across `ARCHITECTURE.md`, `docs/OPERATIONAL_DATABASE.md`, and `docs/SDLC/Signature_Token_Cryptographic_Contract.md`. Exits `1` on drift.                                                                                                            | `ArchitectureDriftValidatorService.ValidateArchitectureDrift`                             |
| `sync-openapi-spec`           | Runs `Cadence.OpenApiExporter` (reusing a fresh prebuilt binary when available), which boots the .NET services concurrently in memory and exports their OpenAPI documents, then writes the specs and the `cdisc_openapi.json` aggregate to `docs/openapi/` and deletes top-level specs no service serves. `docs/openapi/legacy/` is left alone. Option: `--validate` (compare only; exits `1` on drift).                                                                                      | `OpenApiSyncService.SyncOpenApiSpec`                                                      |
| `validate-openapi-drift`      | Runs `sync-openapi-spec --validate` and `generate-api-docs --validate`. Exits `1` if either finds drift.                                                                                                                                                                                                                                                                                                                                                                                      | `OpenApiDriftValidatorService.ValidateDrift`                                              |
| `generate-api-docs`           | Writes `docs/api/<service>.md` (one table of operations per service) and `docs/api/index.md` from `docs/openapi/*_openapi.json`, and deletes pages with no spec. Option: `--validate` (compare only; exits `1` on drift).                                                                                                                                                                                                                                                                     | `ApiDocsGeneratorService.GenerateApiDocs`                                                 |
| `generate-schema-doc`         | Runs `Cadence.SchemaDocExporter` (reusing a fresh prebuilt binary when available), which verifies `HasPendingModelChanges()`, `[DbContext]`, `[Migration]`, `.Designer.cs` companions, and `CreateTable`/`AddColumn` uniqueness across all 18 persistence modules (no database needed) and writes one Markdown page per module plus `README.md` to `docs/schema/`, deleting files no module produces. Option: `--validate` (compare only; exits `1` on drift). This is the `schema-doc` gate. | `SchemaDocGeneratorService.GenerateSchemaDocumentation`                                   |
| `pre-commit-openapi`          | Runs the quick staged-file `openapi` check: exports specs only for dependent services affected by staged C# files, or verifies staged `docs/api` against staged `docs/openapi` without booting hosts when no C# backend contracts are staged. It does not stage anything.                                                                                                                                                                                                                     | `PreCommitOpenApiService.ExecutePreCommitOpenApi`                                         |
| `validate-api-clients`        | Checks that `apps/web/src/api/generated/` matches every auto-discovered `docs/openapi/*_openapi.json` service and module contract (excluding `cdisc_openapi.json`) plus Canvas contracts (`apps/web/scripts/generate-api-types.js --check`), then runs `pnpm --filter web exec tsc --noEmit`, which typechecks the `apps/web/src/api` clients against their generated OpenAPI types. Exits `1` on stale generated types or a type error. This is the `api-clients` gate.                      | `ApiClientValidatorService.ValidateApiClients`                                            |
| `generate-cli-docs`           | Without options, synthesizes the generated documentation marker regions in `docs/cli.md`, `docs/CI.md`, `docs/LOCAL_DEV_ENVIRONMENT.md`, `ARCHITECTURE.md`, `docs/OPERATIONAL_DATABASE.md`, and `docs/SDLC/Signature_Token_Cryptographic_Contract.md` from live code metadata. With `--check`, runs the `cli-docs` gate: fails when any marker region is missing, malformed, or stale, or when `docs/cli.md` lacks the required headings or a command registered in `Program.cs`.             | `GateDocs.Write` (`packages/cli/src/Services`), `CliDocsGeneratorService.GenerateCliDocs` |
| `generate-doc-index`          | Scans markdown files in `docs/` and updates `docs/DOCUMENTATION_INDEX.md` organized by category. Option: `--check` (verify index completeness without editing; exits `1` on unindexed files). This is the `validate-doc-index` gate.                                                                                                                                                                                                                                                          | `DocIndexSyncService.SyncDocIndex`                                                        |
| `generate-demo-manifest`      | Exports the canonical synthetic demo study manifest (`CADENCE-101`, sites, participants, RFC 9562 UUIDv8 identifiers, and eight realm personas) from `packages/demo-fixtures/demo-seed.json`, `ExecutionDbSeeder.cs`, and `docker/cadence-realm.json` into `packages/demo-fixtures`. Option: `--check` (verify parity without modifying files). This is the `demo-manifest` gate.                                                                                                             | `DemoManifestGeneratorService.GenerateDemoManifest`                                       |
| `generate-rtm`                | Builds the traceability matrix from requirements in `docs/`, `@req` tags in tests and `report_dotnet.trx`. Writes `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`. Options: `--validate` (write nothing; fail if a mapped test failed), `--draft` (mark tests as unverified when the report is missing).                                                                                                                                         | `RtmGeneratorService.GenerateRtm` (`packages/compliance/src`)                             |
| `compliance-utility`          | With `--sanitize`, upper-cases `@req:PRD-...` tags in `docs/**/*.md`, skipping `docs/SDLC/runs/`. Without it, does nothing.                                                                                                                                                                                                                                                                                                                                                                   | `ComplianceUtilityService.ExecuteComplianceUtility`                                       |
| `check-ports`                 | Reports whether ports 3000, 5174, 5432, 8000-8016, 8018 and 8080 are in use on `127.0.0.1` (`PortCheckerService.DefaultPorts`). Always exits `0`.                                                                                                                                                                                                                                                                                                                                             | `PortCheckerService.CheckPorts`                                                           |
| `run-gating-pipeline`         | Runs `detect-duplication`, `validate-imports` and `validate-openapi-drift`. Stops at the first failure.                                                                                                                                                                                                                                                                                                                                                                                       | `GatingPipelineService.RunGatingPipeline`                                                 |

These commands always exit `0`: `compliance-utility` and `check-ports`.

<!-- check-ports:begin (generated from packages/tooling_core/src/Services/PortCheckerService.cs by `pnpm cadence generate-cli-docs`; do not edit) -->

`check-ports` probes these 22 default ports on `127.0.0.1` (`PortCheckerService.DefaultPorts`): 3000, 5174, 5432, 8000-8016, 8018 and 8080.

<!-- check-ports:end -->

### `cadence gxp`

GxP compliance documents. The RTM, the IQ/OQ/PQ report and `docs/SDLC/runs/` are a snapshot that only
`cadence gxp snapshot` writes, run by the integration owner on the integrated candidate (ADR-2532). A feature branch
validates with `cadence gxp sync` and never commits the snapshot; the `gxp-snapshot` gate of `cadence check` enforces
this.

#### `cadence gxp sync`

Validates a feature branch. It writes and stages nothing under `docs/SDLC`.

1. Runs `dotnet test Cadence.sln --no-restore --configuration Release`, writing one TRX file per test project and the
   database fixtures' environment records (`CADENCE_TEST_EVIDENCE_DIR`) into `TestResults/gxp/`.
2. Generates a preview of the RTM and IQ/OQ/PQ report (see `generate-rtm`) in `TestResults/gxp/preview/` (gitignored),
   with an evidence block naming the tested revision, the generation time, the database and the runner.
3. Fails if a test failed, a test declares an unknown requirement ID, a document's integrity stamp is broken, or a
   database test in the integration or E2E results did not execute.

| Option                | Description                                                                                                                                                                                       |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--dry-run`           | Runs no tests and writes nothing. Validates the requirement tags and integrity stamps, and with `CADENCE_GXP_SNAPSHOT=1` also that the committed RTM's test mapping matches the tags in `tests/`. |
| `--results-dir <dir>` | Reuses existing TRX reports and `*.database.txt` fixture evidence under `<dir>` instead of running `dotnet test`, while enforcing all requirement tag, stamp and skipped-database-test gates.     |

#### `cadence gxp snapshot`

The integration owner's only command for writing the snapshot. Run it on the integrated candidate against PostgreSQL:
`CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot` (or pass `--results-dir <dir>` to reuse verified CI PostgreSQL test
evidence without re-running `dotnet test`).

1. Runs the tests as `cadence gxp sync` does, or reuses existing TRX reports and `*.database.txt` fixture evidence
   under `<dir>` when `--results-dir <dir>` is given.
2. Regenerates `docs/SDLC/Requirements_Traceability_Matrix.md`, `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` and
   `docs/SDLC/runs/`.
3. Fails, without staging, on the same errors as `cadence gxp sync`, when `*.database.txt` fixture metadata is missing
   under `--results-dir`, and when the RTM's test mapping does not match the tags in `tests/`.
4. Otherwise stages those files with `git add` when `.git` is present. Commit them with `CADENCE_GXP_SNAPSHOT=1` set,
   so the pre-commit `gxp-snapshot` gate admits them.

| Option                | Description                                                                                                                                                                                    |
| --------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--commit`            | After staging, runs `git commit -m "docs(rtm): take the GxP snapshot from the integrated candidate"` with `CADENCE_GXP_SNAPSHOT=1` set.                                                        |
| `--results-dir <dir>` | Reuses verified TRX reports and `*.database.txt` fixture evidence under `<dir>` instead of running `dotnet test`; fails if `*.database.txt` is missing or any mandatory database test skipped. |

#### `cadence gxp validate`

Same as `cadence gxp sync --dry-run`: no tests run and nothing is written. Unknown requirement IDs and broken integrity
stamps fail it. It requires the committed RTM's test mapping to be current only with `CADENCE_GXP_SNAPSHOT=1`, and it
does not compare the checked-in documents with freshly generated ones.

#### `cadence gxp diff`

Compares the RTM and IQ/OQ/PQ report in the working tree with the copies in the git index, leaving out the evidence
block and the integrity stamp, which change on every run. Exits `1` and prints the differing lines when the results or
the test mapping differ. The `gxp-snapshot` gate runs it in snapshot mode (`CADENCE_GXP_SNAPSHOT=1`) outside the
pre-commit hook, as CI's `Check GxP Snapshot Policy` step does after regenerating the reports.

### `cadence db`

Local PostgreSQL database commands.

#### `cadence db reset`

Resets the local PostgreSQL database and provisions all 19 module and service schemas from EF Core migrations:

1. Resolves the connection string from `ConnectionStrings__cadence`, then `POSTGRES_CONNECTION_STRING` (accepting Npgsql key-value strings and `postgres://` or `postgresql://` URIs; fails when neither is set).
2. Refuses to run when `CADENCE_ENV`, `ASPNETCORE_ENVIRONMENT` or `ENVIRONMENT` is `Production` or `Prod`, or when `NpgsqlConnectionStringBuilder` resolves a missing or non-local host or a production/cloud keyword.
3. Queries `pg_catalog.pg_namespace` to drop all non-system schemas (`CASCADE`), recreates an empty `public` schema, and runs `Cadence.DatabaseMigrator` once across all 18 design-time `DbContext` assemblies (`kernel`, `identity_vault`, `execution`, `intelligence`, and the 15 `SearchPath` service schemas).

Exits `1` when validation, schema reset, or migration fails.

#### `cadence db migrate`

Resolves the PostgreSQL connection string from `ConnectionStrings__cadence`, then `POSTGRES_CONNECTION_STRING`, and runs `Cadence.DatabaseMigrator` once across all 18 design-time `DbContext` assemblies (`kernel`, `identity_vault`, `execution`, `intelligence`, and the 15 `SearchPath` service schemas), pre-creating any missing `SearchPath` service schemas before applying pending EF Core migrations. Use `pnpm cadence migrations add <module> <Name>` (or `scripts/manage_migrations.sh` / `scripts/manage_migrations.ps1`) to add or script migrations across any of the 18 persistence modules.

### `cadence migrations`

Unified EF Core migration tooling across all 18 persistence modules in `PersistenceModuleCatalog` (`ctms`, `econsent`, `eisf`, `etmf`, `execution`, `executionmodule`, `fileshare`, `imaging`, `intelligence`, `interop`, `kernel`, `knowledge`, `notifications`, `org`, `payments`, `quality`, `safety`, `tickets`; [ADR-2536](adr/2026-11-22-unify-18-module-migrations-and-strip-schema-doc-counts.md)). `scripts/manage_migrations.sh` and `scripts/manage_migrations.ps1` delegate `add`, `script`, `list`, and `verify` to these subcommands.

#### `cadence migrations add <module> <name>`

Scaffolds a new EF Core migration in the module's configured `Migrations` directory (`MigrationToolingService.Add`). When `POSTGRES_CONNECTION_STRING` is unset, passes a credential-free design-time placeholder after `--` so fail-closed design-time factories build the model offline. Rejects `all` or unknown module names with exit code `1`.

#### `cadence migrations script [module|all]`

Generates idempotent SQL review scripts (`dotnet ef migrations script --idempotent`) for one module or all 18 modules (`MigrationToolingService.Script`), writing `<module>.sql` into untracked `TestResults/migrations/` by default instead of committing SQL dumps under `Migrations/`.

| Option                   | Description                                                                            |
| ------------------------ | -------------------------------------------------------------------------------------- |
| `-o\|--output-dir <dir>` | Destination directory for `<module>.sql` artifacts. Default: `TestResults/migrations`. |

#### `cadence migrations list [module|all]`

Lists migrations for one module or all 18 modules (`MigrationToolingService.List`). Uses live PostgreSQL state when `POSTGRES_CONNECTION_STRING` is set, or offline `--no-connect` / `.Designer.cs` inspection when unset.

#### `cadence migrations verify [module|all]`

Runs `Cadence.SchemaDocExporter --verify-only` (`MigrationToolingService.Verify`) to verify `HasPendingModelChanges()`, `[DbContext]`, `[Migration]`, `.Designer.cs` companions, and unique `CreateTable`/`AddColumn` DDL targets across one module or all 18 modules without opening a database connection or rewriting `docs/schema`.

### `cadence settings`

#### `cadence settings export`

Same as the `extract-env-vars` tool command, including the `--check` option. It does not write `.env.example` or any docs.

### `cadence scaffold`

#### `cadence scaffold service <name>`

Runs `bash scripts/migrate_service_wizard.sh <name>` in the current directory, passing `<name>` as the first positional argument (`$1`) to the wizard script. Needs `bash`, so it does not work on plain Windows.

#### `cadence scaffold adr <title>`

Creates `docs/adr/<yyyy-mm-dd>-<slug>.md` from a built-in template. The ADR number is one more than the highest `ADR-<n>:` found on the first line of existing ADRs, or 100 if there are none. Fails if the file already exists. Appends a link to `docs/adr/index.md` if that file exists.

The title is required. With make, pass it as `make adr TITLE="Use X for Y"`.

#### `cadence scaffold post <title>`

Creates `docs/blog/<yyyy-mm-dd>-<slug>.md` with YAML frontmatter (`title`, `date`, `authors`, `summary`, `tags`, `draft`, `related`) and structured Markdown sections, then updates `docs/DOCUMENTATION_INDEX.md` under `## Engineering blog` when that file exists. Posts start with `draft: true` by default so drafts stay excluded from public index pages, site search, and `/feed.xml` until flipped to `draft: false` (or created with `--publish`). Fails with exit code `1` if the title produces an empty slug or the dated file already exists.

The title is required. With make, pass it as `make post TITLE="Ship X"`.

| Option                 | Description                                                                                                                            |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `-a\|--author <value>` | Author GitHub handle or display name (comma-separated for multiple, e.g. `fderuiter` or `--author=@fderuiter`). Default: `@fderuiter`. |
| `-t\|--tag <slug>`     | Topic tag slug; repeat or comma-separate for multiple tags. Default: `engineering`.                                                    |
| `-s\|--summary <text>` | One-sentence summary excerpt for the blog index and RSS feed. Defaults to a sentence derived from `<title>`.                           |
| `--publish`            | Writes `draft: false` so the post is published immediately instead of starting as a draft.                                             |

### `cadence hook`

Agent lifecycle hooks (PRD-SYS-053). `.agents/hooks.json` runs them from the published binary (`make build` writes `bin/cadence`). Each hook reads the runtime's JSON payload on standard input, exits `0` to allow the step, and exits `2` with the reason on standard error to block it. The banner is not printed.

| Command                       | Phase           | What it does                                                                                                                                                                                                                                                             |
| ----------------------------- | --------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `cadence hook pre-tool`       | `PreToolUse`    | Blocks `--no-verify`, force-pushing or deleting `main`/`master`, `git filter-branch`/`filter-repo`, `rm -rf` of `/`, `~` or the repository, `git clean -x`, and any write to the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/`. An unreadable payload is blocked.  |
| `cadence hook post-write`     | `PostToolUse`   | Runs the query-style lint on the edited `.cs` files and blocks on `== true`/`== false` comparisons.                                                                                                                                                                      |
| `cadence hook pre-invocation` | `PreInvocation` | Prints the GxP working rules from `AGENTS.md`.                                                                                                                                                                                                                           |
| `cadence hook stop`           | `Stop`          | Blocks finishing while a test declares an unknown requirement ID or a generated document's integrity stamp is broken; with `CADENCE_GXP_SNAPSHOT=1` also while the RTM's test mapping is out of date with the requirement tags. Fix the tags and run `cadence gxp sync`. |

### `cadence wizard`

Interactive wizards. Each one runs a script in `scripts/` from the repository root: the `.sh` script with `bash`, or the `.ps1` script with PowerShell on Windows. The command returns the script's exit code, or `1` if the script is missing.

| Command                     | Script                   | Arguments                                 |
| --------------------------- | ------------------------ | ----------------------------------------- |
| `cadence wizard onboarding` | `setup_wizard`           | None                                      |
| `cadence wizard dotnet`     | `setup_dotnet_wizard`    | None                                      |
| `cadence wizard migrate`    | `migrate_service_wizard` | None. The script prompts for the service. |

What each wizard does is defined by its script, not by the CLI.
