# Contributing to Cadence

This guide covers the maintainer workflow for setup, development, local checks, and pull requests.
For feedback and feature ideas, see the [README’s contribution guidance](README.md#contributing).
Coding rules live in [AGENTS.md](AGENTS.md) and apply to everyone, human or AI agent.
Use the [project plan](docs/PROJECT_PLAN.md) to find the module owner and the [goals and priorities](docs/GOALS_AND_PRIORITIES.md) to find the active work and its verification outcome.

## Quick reference

| Task                    | Command                                                                        |
| ----------------------- | ------------------------------------------------------------------------------ |
| Set up a new machine    | `make wizard` (headless: `make setup`)                                         |
| Build                   | `dotnet build Cadence.sln`                                                     |
| Unit tests (.NET)       | `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`                 |
| Architecture tests      | `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` |
| Frontend & docs tests   | `pnpm test`                                                                    |
| Format check            | `dotnet format Cadence.sln --verify-no-changes`                                |
| All quality gates       | `pnpm cadence check`                                                           |
| Full local verification | `make verify`                                                                  |
| Auto-fix                | `pnpm cadence fix`                                                             |
| Traceability validation | `pnpm cadence gxp sync`                                                        |
| New ADR                 | `pnpm cadence scaffold adr --domain <domain> "<title>"`                        |
| Docs site locally       | `pnpm docs:dev`                                                                |
| All Make targets        | `make help`                                                                    |

`pnpm cadence <command>` invokes `scripts/run-cli.js`, which builds `./bin/cadence` on demand as a
local framework-dependent `Debug` binary and runs it (you can also run
`dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command>` directly). There is no installed
`dotnet cadence` tool. The generated command reference is [docs/cli.md](docs/cli.md).

---

## 1. Setup

```bash
git clone https://github.com/fderuiter/cadence-clinical.git
cd cadence-clinical
make wizard            # interactive: bash scripts/setup_wizard.sh (Windows: pwsh scripts/setup_wizard.ps1)
make setup             # non-interactive: bash scripts/setup_wizard.sh --dev --minimal
```

`scripts/setup_wizard.sh` (and `scripts/setup_wizard.ps1` on Windows) is a fail-closed bootstrap wrapper:
it detects the environment (workstation, Codespaces, devcontainer, Claude Code remote session, or CI),
installs or validates the .NET 10 SDK and HTTPS dev certificate via `scripts/setup_dotnet_env.{sh,ps1}`,
verifies `dotnet`, `node`, and `pnpm` are active, and delegates workspace provisioning to
`node scripts/run-cli.js doctor --auto-fix`. `cadence doctor --auto-fix` runs `dotnet tool restore`
(`.config/dotnet-tools.json`) and `dotnet restore Cadence.sln`, provisions `detect-secrets==1.5.0` in
`.cadence/venv` and SHA-256-verified `actionlint` (`1.7.12`), `hadolint` (`2.15.1`), and `shellcheck`
(`0.11.0`) in `.cadence/bin`, installs the `pnpm` workspace (`pnpm install --frozen-lockfile`), creates
`.env` from `.env.example` if absent, and enables `.githooks/pre-commit`
(`git config core.hooksPath .githooks`) only after all blocking checks pass. `make setup` passes
`--dev --minimal` to skip prompts, HTTPS cert trust prompts, and Playwright browser downloads; pass
`--linters-only` / `-LintersOnly` to provision only `.cadence/bin` linters.

With the [prerequisites installed](docs/LOCAL_DEV_ENVIRONMENT.md#1-prerequisites) and developer tools
already provisioned, restore the workspaces directly with `dotnet restore Cadence.sln && pnpm install --frozen-lockfile`,
or self-heal the local toolchain at any time with `pnpm cadence doctor --auto-fix --minimal`.

NuGet restores from the v3 feed configured in [`NuGet.Config`](NuGet.Config).

To run the services locally, see [README: Running the services](README.md#running-the-services) and
[docs/LOCAL_DEV_ENVIRONMENT.md](docs/LOCAL_DEV_ENVIRONMENT.md).

## 2. Workflow

1. Pick agreed work using the [issue workflow](docs/ISSUE_MANAGEMENT.md). It defines the vision,
   canonical labels, horizon caps, native relationships, and Definition of Ready. Promote a bounded
   outcome from an existing owner before creating another ticket. Feedback and uncommitted ideas
   start in [Discussions](https://github.com/fderuiter/cadence-clinical/discussions).
2. Branch from `main` (`feature/<issue>-<slug>`, `fix/<issue>-<slug>`, `docs/<slug>`, `chore/<slug>`).
3. Make the change, with tests and the documentation it affects (see section 5).
4. Run the local checks (section 3). Commit with a
   [Conventional Commits](https://www.conventionalcommits.org/) message, for example
   `fix(execution): …` or `docs(adr): …`.
5. Open a pull request against `main` using the template. Open it as a draft until it is complete.

## 3. Local checks

The pre-commit hook (`.githooks/pre-commit`) delegates directly to `node scripts/run-cli.js check --pre-commit` on Linux, macOS, and Windows, reading staged Git index blobs via `git cat-file --batch` (without mutating working-tree files or scanning the full repository on deletion/clean-merge commits) and running Prettier, ESLint, `actionlint`, `hadolint`, `shellcheck`, `dotnet format` (`whitespace` and `style`), `detect-secrets`, GitHub Actions workflow security and SHA pinning (`workflow-security`), YAML syntax, trailing-whitespace, EOF newline, and all Cadence policy gates fail-closed. Before opening a PR also run the full checks and tests:

```bash
dotnet build Cadence.sln
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj
dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj
dotnet format Cadence.sln --verify-no-changes
pnpm test
pnpm cadence check
```

`make verify` runs `pnpm cadence check`, `pnpm test`, and `pnpm cadence test` in sequence.

Useful single gates (`pnpm cadence check <gate>`): `prettier`, `eslint`, `typecheck`, `actionlint`, `hadolint`, `shellcheck`, `workflow-security`, `rulesets`,
`yaml`, `detect-secrets`, `adrs`, `markdown`, `readmes`, `arch-drift`, `env-vars`, `openapi`, `rtm`,
`duplication`, `security`, `deid`, `vulnerabilities`.

**Before merging, run `pnpm cadence ci`** (or `make ci`). GitHub Actions runs `.github/workflows/ci.yml` on pull
requests and pushes, and `pnpm cadence ci` runs the same pull-request validation jobs locally before pushing
(including `actionlint`) with the same commands, failing when a required binary (`actionlint`, `hadolint`, or `shellcheck` in
`.cadence/bin` or `PATH`, `detect-secrets-hook` in `.cadence/venv`, `.venv` or `PATH`, `bash`) is missing
or the .NET tests have no PostgreSQL unless `--allow-missing-tools` or `--allow-db-skip` (or `--allow-skip` for both) is passed ([docs/cli.md](docs/cli.md#cadence-ci)). A full
run takes about 20 minutes:

```bash
CADENCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres;Database=postgres" pnpm cadence ci
```

Without a terminal (an AI agent, a script, a pipe, a git hook) the CLI prints plain text without colours, so its
output can be redirected to a file as it is.

For **PostgreSQL integration tests**, see the [database setup and test commands](docs/TESTING.md#tests-that-need-postgresql).

**Warnings are errors.** Fix a warning rather than suppressing it. When a suppression is the right call
(a generated EF migration, an established acronym, an analyzer false positive), scope it to the member
and give the reason: `[SuppressMessage(..., Justification = "...")]`, or a comment on the
`#pragma warning disable` or `<NoWarn>` line. Two project-wide rules:

- CS1591 (missing XML comment on a public member) is off for services, tools and tests. Projects that
  are an API for other code (`packages/contracts`, `Cadence.Kernel.Api`, `Cadence.Standards`, `Cadence.Rules`) set
  `<CadenceRequireXmlDocs>true</CadenceRequireXmlDocs>` (see `Directory.Build.targets`), so their
  public members must be documented. The other kernel projects opt in as their comments are written.
- The CLI (`packages/cli`) suppresses no trim or AOT warning; CI publishes it as Native AOT.

**CI** runs the same gates in `.github/workflows/ci.yml`; [docs/CI.md](docs/CI.md) lists every job and
gate and how to reproduce each failure locally. If CI cannot run, validate locally and say so in the PR
description.

## 4. Tests and requirement traceability

- Tests live in `tests/`, one folder per project: `Cadence.UnitTests` (no database), `Cadence.IntegrationTests`
  (real PostgreSQL), `Cadence.ArchitectureTests` and `Cadence.E2ETests`, each organised by area
  (`tests/Cadence.UnitTests/Execution`, `tests/Cadence.UnitTests/Kernel`, …). Shared fakes go in
  `tests/Cadence.TestSupport` ([docs/TESTING.md](docs/TESTING.md#test-projects)).
  Use xUnit v3 and FluentAssertions, and the helpers in `packages/testing`
  ([AGENTS.md pattern 7](AGENTS.md#7-test-infrastructure-packagestesting)).
- A test that verifies a requirement carries both an `@req:<ID>` tag in its XML doc comment and a
  `[GxPRequirement("<ID>")]` attribute ([AGENTS.md pattern 6](AGENTS.md#6-requirement-traceability-in-tests-21-cfr-part-11)).
  Tags on a class do not count, the ID must exist in the PRD or SRS, and a requirement is qualified only
  when a test declaring it ran and passed ([docs/TESTING.md](docs/TESTING.md#linking-a-test-to-a-requirement)).
- After adding or changing such tests, run `pnpm cadence gxp sync`. It runs the tests, validates the tags
  and results, and writes a preview to `TestResults/gxp/preview/`; it does not touch `docs/SDLC`.
  `pnpm cadence gxp validate` checks the tags without running tests. Set `CADENCE_TEST_POSTGRES` first so
  database tests produce results instead of skips.
- `docs/SDLC/Requirements_Traceability_Matrix.md`, `docs/SDLC/IQ_OQ_PQ_Execution_Report.md` and
  `docs/SDLC/runs/` are a generated snapshot that only the integration owner refreshes
  (`CADENCE_GXP_SNAPSHOT=1 pnpm cadence gxp snapshot`, ADR-2532). Never edit them, commit them on a feature
  branch or resolve a conflict in them by hand; take `main`'s copy
  (`git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`)
  and do not regenerate them on the branch. The `gxp-snapshot` gate refuses them.
- [docs/TESTING.md](docs/TESTING.md) covers running the suites, the PostgreSQL opt-ins and each
  `Cadence.Testing` helper.
- Never skip, disable or fake a test to get green.

## 5. Documentation is part of the change

Every PR updates what its change makes stale, in the same PR:

| If you change…                                                              | Update                                                                                                                    |
| --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| A service or package's behaviour, endpoints, settings or tests              | Its `README.md`                                                                                                           |
| A setting (environment variable or configuration key)                       | [docs/ENVIRONMENT_VARIABLES.md](docs/ENVIRONMENT_VARIABLES.md) and `.env.example`                                         |
| Architecture, service map, security model                                   | [ARCHITECTURE.md](ARCHITECTURE.md)                                                                                        |
| Domain vocabulary                                                           | [CONTEXT.md](CONTEXT.md)                                                                                                  |
| A requirement                                                               | [PRD](docs/SDLC/01_Product_Requirements_Document_PRD.md) or [SRS](docs/SRS.md) with its ID                                |
| A significant decision (new dependency, datastore, contract or schema rule) | A new ADR (`pnpm cadence scaffold adr --domain <domain> "<title>"`), indexed in `docs/adr/<domain>/index.md`              |
| Anything that supersedes an ADR                                             | The old ADR's status and its shard index ([AGENTS.md](AGENTS.md#architecture-decision-records))                           |
| Kernel concepts or invariants                                               | Raise on the kernel epic; the spec changes only through its owners                                                        |
| A new document                                                              | Its owning category shard (`pnpm cadence generate-doc-index`, [docs/DOCUMENTATION_INDEX.md](docs/DOCUMENTATION_INDEX.md)) |

## 6. Repository hygiene

- Never commit `.env`, secrets, `.docx` files, test reports (`*.trx`, `report.xml`) or build output.
  `baselines/secrets/` (`config.json` and `<unit>.secrets.baseline.json` shards) tracks accepted detect-secrets findings.
- Never commit PHI or PII. The `deid` gate runs the full de-identification detector (14 categories) over
  application code, authored `docs/`, root portal documents, `.vue` templates, and client `stores/` — excluding
  `tests/`, `baselines/`, `scripts/`, `docker/`, vendored standards (`docs/CDISC/`, `docs/NCI/`), generated `docs/openapi/` and
  `docs/schema/`, and the GxP snapshot outputs (`docs/SDLC/runs/`, `Requirements_Traceability_Matrix.md`,
  `IQ_OQ_PQ_Execution_Report.md`) — and fails on any finding not in `baselines/deid/<unit>.baseline.txt`
  (`DeidComplianceService.cs`). Those shards may only shrink. When a flagged line is not PHI (demo data, a code-system
  URL), put a `deid-ignore` comment on that line rather than adding to the baseline. When the gate reports stale
  baseline entries, drop them with `CADENCE_UPDATE_BASELINE=1 pnpm cadence check deid [files]`.
- Ratchet baselines (`tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries/<Assembly>.baseline.txt`,
  `tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage/<AREA>.baseline.txt`,
  `tests/Cadence.ArchitectureTests/ArchitectureBaselines/{direct-state-writes,permission-matrix,cross-service-isolation}.baseline.txt`,
  `baselines/deid/<unit>.baseline.txt`, `baselines/secrets/<unit>.secrets.baseline.json`, and any other
  `*.baseline.txt`) may only shrink, aggregated per gate across all shards. The `baseline-ratchet` gate fails on uncommitted
  working-tree growth and when any commit on the branch or push range adds an entry without a matching file-and-count-scoped
  `Baseline-Growth: <path> +<count> - <reason>` trailer on that commit covering the gate (`git commit --trailer "Baseline-Growth: <path> +<count> - <reason>"`).
  For the commit that stages growth against `HEAD`, set `CADENCE_BASELINE_GROWTH="<path> +<count> - <reason>"` so the pre-commit hook admits it
  ([docs/TESTING.md](docs/TESTING.md#ratchet-baselines)).
- Do not re-add Python code or tooling (`check path-patterns` rejects `.py` files under `apps/` and `packages/`);
  historical Python dispositions are archived in [docs/PYTHON_REFERENCE_INVENTORY.md](docs/PYTHON_REFERENCE_INVENTORY.md)
  ([#4789](https://github.com/fderuiter/cadence-clinical/issues/4789)).
- `docs/index.md`, `docs/AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/CONTEXT.md`, `docs/CONTRIBUTING.md`,
  `docs/SECURITY.md`, `docs/SUPPORT.md` and `docs/LICENSE.md` are generated by `pnpm docs:build` from the root files
  and are not committed. Edit the root files.
- The docs site build fails on a dead link, so run `pnpm docs:build` (or `node scripts/build-docs.js
--prepare-only && npx vitepress build docs`) after moving or renaming a document.

## 7. Getting help

- Security issues: follow [SECURITY.md](SECURITY.md); do not open a public issue.
- Everything else: [SUPPORT.md](SUPPORT.md).
