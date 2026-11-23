# ADR-2824: Complete Dependabot Batching and Enforce Group Coverage

- **Status:** Accepted
- **Date:** 2026-11-22
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails and fail-closed dependency batching enforcement), PRD-SYS-001 (deterministic GxP build and runtime baselines)
- **Relates to:** [ADR-2525](2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)

---

## 1. Context & Problem Statement

Following the centralization of MSBuild cohort properties and `pnpm-workspace.yaml` catalog pins in [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), `.github/dependabot.yml` still left 27 of 63 NuGet pins in `Directory.Packages.props` (including `Anthropic*`, `ModelContextProtocol.AspNetCore`, Roslyn/Sonar analyzers, and xUnit/test SDK packages), 18 of 39 npm packages across `pnpm-workspace.yaml` and workspace `package.json` manifests, 6 digest-pinned local-stack service images in `docker/docker-compose.yml`, both statistical execution Dockerfiles (`docker/sce/python/Dockerfile`, `docker/sce/r/Dockerfile`), and all 16 GitHub Actions workflow dependencies ungrouped:

1. **Serial `pnpm-lock.yaml` invalidation:** Four split npm groups (`typescript-eslint`, `vitest`, `playwright`, `vue-vite`) plus 16 ungrouped npm dependencies with `open-pull-requests-limit: 10` produced concurrent branches that rewrote the shared `pnpm-lock.yaml` out of order, recreating the lockfile merge conflicts documented in [ADR-2525](2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md) while GitHub Actions runners remain paused until 2027.
2. **Monday PR burst & unguarded drift:** All 7 Dependabot ecosystem blocks ran simultaneously on Monday with a combined `open-pull-requests-limit` of 50, and `DependencyValidatorService` did not inspect `.github/dependabot.yml`, allowing newly added NuGet, npm, container, compose, or workflow dependencies to drift outside Dependabot groups undetected.
3. **CI trigger & ruleset parity gaps:** `.github/workflows/ci.yml` (`classify`) omitted `.config/dotnet-tools.json` from `container_scan`, and `RulesetService.Validate` did not verify `conflict-check.yml` (`Check for Merge Conflicts`) mergeability exit-code semantics or `classify` manifest guardrails.

## 2. Decision Drivers & Constraints

- **100% Dependabot group coverage across all ecosystems:** Every NuGet package and CLI tool, npm workspace/catalog dependency, Dockerfile base image, Docker Compose service image, and GitHub Actions workflow reference must match an explicit Dependabot group.
- **Single active `pnpm-lock.yaml` update stream:** Only one open npm Dependabot pull request may rewrite `pnpm-lock.yaml` at a time, grouping minor/patch updates together and separating major updates without splitting workspace packages across competing branches.
- **Bounded, staggered weekly cadence:** Weekly Dependabot runs must stagger across Monday through Friday with total `open-pull-requests-limit <= 9`.
- **Fail-closed Native AOT CLI enforcement:** `DependencyValidatorService` and `RulesetService` must enforce group coverage, PR-limit caps, weekday staggering, `conflict-check.yml` parity, and `classify` manifest guardrails without reflection (`PublishAot=true`) while keeping `DependencyValidatorService` compatible with empty test fixtures (`QualityGateRegistryTests`).

## 3. Options Considered

### Option 1: Complete ecosystem groups with catch-alls, consolidate npm into one workspace lockfile stream, stagger schedules (cap 9), and enforce fail-closed CLI & unit-test guards (Selected)

- **Overview:** Preserve all 7 NuGet lockstep cohort groups and 9 runtime ignores from ADR-2527 while adding `ai-providers`, `code-analyzers`, `dotnet-testing`, and trailing `nuget-dependencies` (`*`); replace the 4 split npm groups with `npm-workspace` (minor/patch `*`) and `npm-workspace-major` (major `*`) at `open-pull-requests-limit: 1` on Wednesday; add `sce-python`, `sce-r`, `local-stack`, and `workflow-actions` (`*`) groups; stagger the 7 blocks across Monday–Friday with total `open-pull-requests-limit` = 9; extend `DependencyValidatorService`, `RulesetService`, and `ci.yml` (`classify`); and lock invariants in `ContainerImagePinTests` and `RulesetServiceTests`.
- **Pros:**
  - ✅ Guarantees 100% group coverage across all 63 NuGet pins (+ `dotnet-ef`), 39 npm packages, 3 Dockerfiles, 6 compose images, and 16 workflow actions.
  - ✅ Eliminates concurrent `pnpm-lock.yaml` branches and cuts maximum open Dependabot PRs from 50 to 9 across staggered weekdays.
  - ✅ Fails closed in `pnpm cadence check dependencies` and `pnpm cadence check rulesets` while keeping `Cadence.Cli` Native AOT clean.
- **Cons:**
  - ❌ A breaking update inside `npm-workspace` or `nuget-dependencies` holds sibling updates in that batch until maintainers add a scoped `ignore:` rule or land a code fix alongside the batch.

### Option 2: Keep split per-framework npm groups and add individual package patterns without catch-all (`*`) groups

- **Overview:** Enumerate each current npm, NuGet, compose, and workflow dependency explicitly in `.github/dependabot.yml` without `*` catch-alls or consolidating npm groups.
- **Pros:**
  - ✅ Smaller per-PR changelogs for individual npm libraries.
- **Cons:**
  - ❌ Leaves concurrent npm groups rewriting `pnpm-lock.yaml` in parallel and requires manual `.github/dependabot.yml` edits on every new dependency.

### Option 3: Disable scheduled Dependabot updates entirely until GitHub Actions resumes in 2027

- **Overview:** Remove `.github/dependabot.yml` schedules and rely exclusively on ad-hoc manual dependency sweeps.
- **Pros:**
  - ✅ Zero automated PR noise while Actions is paused.
- **Cons:**
  - ❌ Loses automated digest and security version discovery across base containers, compose services, and pinned GitHub Actions SHAs.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **NuGet, Docker, Compose, and GitHub Actions batching (`.github/dependabot.yml`):** Retain all 7 ADR-2527 NuGet cohort groups and runtime `ignore:` rules (including `AWSSDK.*` `>= 4.1.0` and `OpenTelemetry*` `>= 1.21.0` while targeting `net10.0`, which otherwise pull `.NET 11` `System.IO.Hashing`/`System.Diagnostics.DiagnosticSource` `11.0.0` packages and fail `CADDEP001`, plus `SonarAnalyzer.CSharp` `>= 10.7.0`, which escalates `S5332`/`S6444`/`S8969` rules on canonical HL7 FHIR `http://` code-system URIs under `TreatWarningsAsErrors`, #6030), plus `version-update:semver-major` ignores for `Fluid.Core` (`2.41.x`; `3.x` makes `TemplateOptions`/`MemberAccessStrategy` read-only after `Build()`, #5948) and `Microsoft.CodeAnalysis.NetAnalyzers` (`9.0.0`; `11.x` targets the `.NET 11` SDK wave, #5949, ADR-2233); add `ai-providers` (`Anthropic*`, `ModelContextProtocol*`), `code-analyzers` (`Microsoft.CodeAnalysis.*`, `SonarAnalyzer*`), `dotnet-testing` (`xunit*`, `Microsoft.NET.Test.Sdk`, `FluentAssertions*`, `NSubstitute*`, `Testcontainers*`, `coverlet*`, `TngTech.ArchUnitNET*`), and trailing catch-all `nuget-dependencies` (`*`) on Monday (`open-pull-requests-limit: 3`). Schedule `/docker` (`dotnet-containers`) and `/docker` `docker-compose` (`local-stack: *`, ignoring `version-update:semver-major` on `postgres` so compose stays locked to PostgreSQL 18 `PostgresTestImage.Image`, #6033, ADR-2533) on Tuesday (`limit: 1` each), `/docker/sce/python` (`sce-python: *`) and `/docker/sce/r` (`sce-r: *`) on Thursday (`limit: 1` each), and `github-actions` (`workflow-actions: *`) on Friday (`limit: 1`).
- **Atomic npm workspace lockfile stream (`.github/dependabot.yml`):** Replace `typescript-eslint`, `vitest`, `playwright`, and `vue-vite` with `npm-workspace` (`patterns: ["*"]`, `update-types: ["minor", "patch"]`) and `npm-workspace-major` (`patterns: ["*"]`, `update-types: ["major"]`), retain the `@types/node` major ignore, and run weekly on Wednesday with `open-pull-requests-limit: 1`.
- **Fail-closed CLI & CI parity (`DependencyValidatorService`, `RulesetService`, `ci.yml`):** `DependencyValidatorService.ValidateDependencies` validates `.github/dependabot.yml` when present (preserving `0` on empty fixtures), rejecting ungrouped dependencies across NuGet, npm, Docker, compose, and workflows, split npm minor/patch groups, unstaggered weekday schedules, or PR limits above cap (`total <= 9`, `npm <= 1`). `ci.yml` (`classify`) keeps all dependency manifests out of `is_safe` automerge and triggers `container_scan=true` on `.config/dotnet-tools.json` alongside `.NET` and Docker inputs, and `RulesetService.Validate` enforces `conflict-check.yml` (`Check for Merge Conflicts`) pull-request/conflict-exit parity and `classify` manifest guardrails alongside declarative `.github/rulesets/*.json` rules.

## 5. Consequences & Trade-offs

- **Positive Impact:** Every dependency in the repository maps to a deterministic Dependabot group; `pnpm-lock.yaml` never accumulates conflicting sibling Dependabot branches; open PR volume is bounded at 9 across Monday–Friday; and CLI quality gates block ungrouped drift automatically.
- **Negative Impact / Technical Debt:** Incompatible minor/patch releases inside a catch-all batch can temporarily hold back sibling updates in the same ecosystem group, and MSBuild property / `pnpm` catalog indirection still requires maintainers to apply shared cohort bumps in one local pass (ADR-2527).
- **Mitigation Strategy:** Add targeted `ignore:` entries in `.github/dependabot.yml` with an issue/ADR reference when blocking a broken upstream release, and validate batches locally with `dotnet restore Cadence.sln && dotnet build Cadence.sln`, `pnpm install --frozen-lockfile && pnpm cadence check dependencies`, and `pnpm cadence check rulesets`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Directory.Packages.props`, `.github/dependabot.yml`, `.github/workflows/ci.yml`, `packages/tooling_core/src/Services/DependencyValidatorService.cs`, `packages/cli/src/Services/RulesetService.cs`, `packages/cli/src/Services/QualityGates.cs`, `tests/Cadence.UnitTests/Testing/ContainerImagePinTests.cs`, `tests/Cadence.UnitTests/Packages/RulesetServiceTests.cs`, `tests/Cadence.UnitTests/Packages/WorkflowSecurityValidatorServiceTests.cs`, `AGENTS.md`, `docs/CI.md`, `docs/cli.md`, `docs/DOCUMENTATION_INDEX.md`, `docs/adr/index.md`.
- **Verification Plan:**
  - `dotnet restore Cadence.sln && dotnet build Cadence.sln` and `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64` verify zero compiler, analyzer, or Native AOT warnings.
  - `pnpm cadence check dependencies`, `pnpm cadence check rulesets`, `pnpm cadence check workflow-security`, `pnpm cadence check yaml`, `pnpm cadence check adrs`, `pnpm cadence check cli-docs`, and `pnpm cadence check doc-index` verify gate and documentation parity.
  - `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` and `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` verify 100% group coverage, staggered schedules, PR limit caps, fail-closed fixture rejections, and architecture invariants.
