# Contributing to Cadence

This guide covers setup, the development workflow, local checks and pull requests. Coding rules live
in [AGENTS.md](AGENTS.md) and apply to everyone, human or AI agent.

## Quick reference

| Task | Command |
| --- | --- |
| Set up a new machine | `make wizard` |
| Build | `dotnet build Cadence.sln` |
| Unit tests | `dotnet test tests/Cadence.UnitTests.csproj` |
| Architecture tests | `dotnet test tests/Cadence.ArchitectureTests.csproj` |
| Format check | `dotnet format Cadence.sln --verify-no-changes` |
| All quality gates | `pnpm cadence check` |
| Auto-fix | `pnpm cadence fix` |
| Traceability matrix | `pnpm cadence gxp sync` |
| New ADR | `pnpm cadence scaffold adr "<title>"` |
| Docs site locally | `pnpm docs:dev` |
| All Make targets | `make help` |

`pnpm cadence <command>` runs the Cadence CLI from source
(`dotnet run --project packages/cli/src/Cadence.Cli.csproj -- <command>`). There is no installed
`dotnet cadence` tool. The generated command reference is [docs/cli.md](docs/cli.md).

---

## 1. Setup

```bash
git clone https://github.com/fderuiter/cadence-clinical.git
cd cadence-clinical
make wizard            # or: bash scripts/setup_wizard.sh   (Windows: scripts/setup_wizard.ps1)
```

The wizard detects the environment (workstation, Codespaces, devcontainer, Claude Code remote session
or CI), installs or validates the .NET 10 SDK and dev certificate, installs the pnpm workspace,
points Git at the repository hooks (`git config core.hooksPath .githooks`), creates `.env` from
`.env.example`, and runs `cadence doctor`. Use `--dev` (or `-y`) to skip prompts and `--minimal` to
skip the Playwright browser download.

Already have the SDK? `dotnet restore Cadence.sln && pnpm install` is enough.

NuGet restores from the v3 feed configured in [`NuGet.Config`](NuGet.Config).

To run the services locally, see [README: Running the services](README.md#running-the-services) and
[docs/LOCAL_DEV_ENVIRONMENT.md](docs/LOCAL_DEV_ENVIRONMENT.md).

## 2. Workflow

1. Pick or open an issue. The backlog is organised in epics: kernel
   ([#4719](https://github.com/fderuiter/cadence-clinical/issues/4719)), security and GxP defects
   ([#4759](https://github.com/fderuiter/cadence-clinical/issues/4759)) and hygiene
   ([#4760](https://github.com/fderuiter/cadence-clinical/issues/4760)). Kernel work follows the
   [spec roadmap](docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md#15-roadmap).
2. Branch from `main` (`feature/<issue>-<slug>`, `fix/<issue>-<slug>`, `docs/<slug>`, `chore/<slug>`).
3. Make the change, with tests and the documentation it affects (see section 5).
4. Run the local checks (section 3). Commit with a
   [Conventional Commits](https://www.conventionalcommits.org/) message, for example
   `fix(execution): …` or `docs(adr): …`.
5. Open a pull request against `main` using the template. Open it as a draft until it is complete.

## 3. Local checks

The pre-commit hook (`.githooks/pre-commit`) runs `dotnet format --verify-no-changes` and
`pnpm cadence check`. Before opening a PR also run the tests:

```bash
dotnet build Cadence.sln
dotnet test tests/Cadence.UnitTests.csproj
dotnet test tests/Cadence.ArchitectureTests.csproj
dotnet format Cadence.sln --verify-no-changes
pnpm cadence check
```

Useful single gates (`pnpm cadence check <gate>`): `adrs`, `markdown`, `readmes`, `arch-drift`,
`env-vars`, `contracts`, `openapi`, `rtm`, `duplication`, `security`, `vulnerabilities`.

**PostgreSQL integration tests** are skipped unless you provide a database:
`CADENCE_TEST_POSTGRES=<Npgsql connection string>` or `CADENCE_TEST_DOCKER=1` (Testcontainers).

**CI** runs the same gates in `.github/workflows/ci.yml`; [docs/CI.md](docs/CI.md) lists every job and
gate and how to reproduce each failure locally. If CI cannot run, validate locally and say so in the PR
description.

## 4. Tests and requirement traceability

- Tests live in `tests/`, organised by area (`tests/Execution`, `tests/Kernel`, `tests/Gateway`, …).
  Use xUnit v3 and FluentAssertions, and the helpers in `packages/testing`
  ([AGENTS.md pattern 7](AGENTS.md#7-test-infrastructure-packagestesting)).
- A test that verifies a requirement carries both an `@req:<ID>` tag in its XML doc comment and a
  `[GxPRequirement("<ID>")]` attribute ([AGENTS.md pattern 6](AGENTS.md#6-requirement-traceability-in-tests-21-cfr-part-11)).
  Tags on a class do not count, the ID must exist in the PRD or SRS, and a requirement is qualified only
  when a test declaring it ran and passed ([docs/TESTING.md](docs/TESTING.md#linking-a-test-to-a-requirement)).
- After adding or changing such tests, run `pnpm cadence gxp sync`. It runs the tests and regenerates
  `docs/SDLC/Requirements_Traceability_Matrix.md` and `docs/SDLC/IQ_OQ_PQ_Execution_Report.md`.
  `pnpm cadence gxp validate` checks without regenerating. Never edit those two files by hand. Set
  `CADENCE_TEST_POSTGRES` first so database tests produce results instead of skips.
- [docs/TESTING.md](docs/TESTING.md) covers running the suites, the PostgreSQL opt-ins and each
  `Cadence.Testing` helper.
- Never skip, disable or fake a test to get green.

## 5. Documentation is part of the change

Every PR updates what its change makes stale, in the same PR:

| If you change… | Update |
| --- | --- |
| A service or package's behaviour, endpoints, settings or tests | Its `README.md` |
| A setting (environment variable or configuration key) | [docs/ENVIRONMENT_VARIABLES.md](docs/ENVIRONMENT_VARIABLES.md) and `.env.example` |
| Architecture, service map, security model | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Domain vocabulary | [CONTEXT.md](CONTEXT.md) |
| A requirement | [PRD](docs/SDLC/01_Product_Requirements_Document_PRD.md) or [SRS](docs/SRS.md) with its ID |
| A significant decision (new dependency, datastore, contract or schema rule) | A new ADR (`pnpm cadence scaffold adr "<title>"`), linked from [docs/adr/index.md](docs/adr/index.md) |
| Anything that supersedes an ADR | The old ADR's status and the index ([AGENTS.md](AGENTS.md#architecture-decision-records)) |
| Kernel concepts or invariants | Raise on the kernel epic; the spec changes only through its owners |
| A new document | [docs/DOCUMENTATION_INDEX.md](docs/DOCUMENTATION_INDEX.md) |

## 6. Repository hygiene

- Never commit `.env`, secrets, `.docx` files, test reports (`*.trx`, `report.xml`) or build output.
  `.secrets.baseline` tracks accepted detect-secrets findings.
- Do not re-add Python code or tooling; Python reference sources are being removed as ports are
  verified ([#4789](https://github.com/fderuiter/cadence-clinical/issues/4789)).
- `docs/index.md`, `docs/AGENTS.md`, `docs/ARCHITECTURE.md` and `docs/LICENSE.md` are generated by
  `pnpm docs:build` from the root files and are not committed. Edit the root files.
- The docs site build fails on a dead link, so run `pnpm docs:build` (or `node scripts/build-docs.js
  --prepare-only && npx vitepress build docs`) after moving or renaming a document.

## 7. Getting help

- Security issues: follow [SECURITY.md](SECURITY.md); do not open a public issue.
- Everything else: [SUPPORT.md](SUPPORT.md).
