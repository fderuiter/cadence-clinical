# ADR-2539: Extend Cohort, xUnit v3, and PostgreSQL Digest Guardrails

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Relates to:** [ADR-2233](2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md), [ADR-2525](2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md), [ADR-2527](2026-11-21-enforce-dependency-alignment-with-msbuild-and-pnpm-catalogs.md), [ADR-2533](2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)

---

## 1. Context & Problem Statement

ADR-2527 centralized `.NET`, `Aspire`, `IdentityModel`, `Microsoft.Extensions.AI`, `OpenTelemetry`, `MassTransit`, `FluentValidation`, and `pnpm` `catalog:` cohorts and added `CADDEP001`–`CADDEP003` compile-time skew checks in `Directory.Build.targets`, while ADR-2533 standardized a single digest-pinned PostgreSQL 18 image across `packages/testing/src/Persistence/PostgresTestImage.cs`, CI workflows, `apps/apphost/src/Program.cs`, and `docker/docker-compose.yml` (PRD-SYS-001). Four dependency-governance gaps remained open:

1. **Dual xUnit v2 + v3 assertion graphs in `Cadence.ArchitectureTests`:** While all other test projects in `Cadence.sln` ran exclusively on `xunit.v3` (`4.0.3`), `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` referenced `TngTech.ArchUnitNET.xUnit` (`0.13.5`), pulling legacy `xunit.assert` `2.4.1` alongside `xunit.v3.assert` `4.0.3` and requiring disambiguated assertion aliases across architecture boundary suites.
2. **Ungrouped test, analyzer, and AI provider cohorts in `.github/dependabot.yml`:** The `nuget` block left `xunit*`, `Microsoft.NET.Test.Sdk`, `TngTech.ArchUnitNET*`, `Microsoft.CodeAnalysis.*`, `SonarAnalyzer.CSharp`, and `Anthropic*` (`Anthropic`, `Anthropic.Aws`, `Anthropic.Bedrock`, `Anthropic.Foundry`, `Anthropic.Vertex`) ungrouped and without `version-update:semver-major` ignores, allowing fragmented PRs and breaking major bumps under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
3. **Standalone Compose PostgreSQL digest bumps:** Scoping the `docker-compose` `/docker` `postgres` ignore in `.github/dependabot.yml` to `update-types: ["version-update:semver-major"]` still allowed Dependabot to open standalone `@sha256` digest bump PRs for `docker/docker-compose.yml` that drift out of sync with `PostgresTestImage.cs`, `.github/workflows/ci.yml`, `.github/workflows/production-pipeline.yml`, and `apps/apphost/src/Program.cs`.
4. **C#-free pre-commit blind spot for manifest and digest edits:** Because `.githooks/pre-commit` skips `dotnet build` and unit tests when no `.cs` files are staged, manifest-only commits editing `Directory.Build.props`, `Directory.Packages.props`, `.github/dependabot.yml`, `docker/docker-compose.yml`, or CI workflows were not checked by `DependencyValidatorService` before commit.

## 2. Decision Drivers & Constraints

- **Zero legacy `xunit` v2 packages on `net10.0`:** Every test project in `Cadence.sln` must resolve only `xunit.v3*` and `TngTech.ArchUnitNET.xUnitV3` without pulling `xunit`, `xunit.assert`, `xunit.core`, or `TngTech.ArchUnitNET.xUnit`.
- **Compile-time and pre-commit defense in depth:** `ValidateDependencyAlignment` in `Directory.Build.targets` must fail compilation on legacy `xunit` v2 or split `xunit.v3`/`TngTech.ArchUnitNET` closures (`CADDEP004`), and `pnpm cadence check dependencies` (including staged Git index blobs via `GitIndexReader` under `--pre-commit`) must validate MSBuild cohorts, Dependabot rules, and PostgreSQL `@sha256` digest parity even when no C# files are staged.
- **Native AOT & isolated fixture safety (ADR-2496, ADR-2534):** `packages/cli` and `packages/tooling_core` must compile cleanly under `PublishAot=true` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (zero `IL2xxx`/`IL3xxx` trim warnings), preserve `FloorNativeAotPacks`, and return `0` when `DependencyValidatorService.ValidateDependencies` runs against isolated temporary directories with zero manifests or partial fixtures.
- **Preserve `.NET 10` and analyzer baselines:** `.NET 10` (`net10.0`), `CentralPackageTransitivePinningEnabled`, `Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`, and `Microsoft.CodeAnalysis.BannedApiAnalyzers` remain intact with zero `NoWarn` or `#pragma warning disable` suppressions.

## 3. Options Considered

### Option 1: Migrate to `TngTech.ArchUnitNET.xUnitV3`, add `CADDEP004`, expand `.github/dependabot.yml` groups/ignores, and enforce MSBuild, Dependabot, and PostgreSQL digest parity in `DependencyValidatorService` (selected)

- **Overview:** Replace `TngTech.ArchUnitNET.xUnit` with `TngTech.ArchUnitNET.xUnitV3` (`0.13.5`) in `Directory.Packages.props`, `Cadence.ArchitectureTests.csproj`, and the three boundary suites; add `dotnet-testing`, `analyzers`, and `anthropic` lockstep groups plus `semver-major` ignores and an unconditional `docker-compose` `postgres` ignore in `.github/dependabot.yml`; extend `ValidateDependencyAlignment` in `Directory.Build.targets` with `CADDEP004`; and extend `DependencyValidatorService` (with `GitIndexReader` `--pre-commit` support) to verify MSBuild XML/cohorts, `pnpm-workspace.yaml` catalog alignment, `.github/dependabot.yml` rules, and `PostgresTestImage.Image` `@sha256` parity.
- **Pros:**
  - ✅ Eliminates `xunit.assert` `2.x` from the entire `Cadence.sln` dependency graph and locks that invariant at both MSBuild compile time (`CADDEP004`) and CLI/pre-commit time (`dependencies`).
  - ✅ Prevents fragmented Dependabot updates across test runners, analyzers, `Anthropic*` provider packages, and standalone Compose PostgreSQL digests.
  - ✅ Catches manifest, Dependabot, and PostgreSQL digest drift during `cadence check --pre-commit` via lock-free `GitIndexReader` blobs without requiring a full solution build on commit.
- **Cons:**
  - ❌ PostgreSQL image upgrades require updating `PostgresTestImage.cs`, `docker/docker-compose.yml`, `.github/workflows/ci.yml`, `.github/workflows/production-pipeline.yml`, and `apps/apphost/src/Program.cs` together in one PR rather than relying on automated Compose-only PRs.

### Option 2: Keep `TngTech.ArchUnitNET.xUnit` with alias workarounds and rely on unit tests only

- **Overview:** Retain `TngTech.ArchUnitNET.xUnit` in `Cadence.ArchitectureTests` and check Dependabot and PostgreSQL pins only inside `Cadence.UnitTests`.
- **Pros:**
  - ✅ Avoids touching `Cadence.ArchitectureTests` or `DependencyValidatorService`.
- **Cons:**
  - ❌ Keeps `xunit.assert` `2.4.1` loaded alongside `xunit.v3.assert` `4.0.3` and allows manifest-only or Compose-only commits to bypass `.githooks/pre-commit` whenever no `.cs` files are staged.

### Option 3: Invoke `dotnet build` and `Cadence.UnitTests` inside `.githooks/pre-commit` on every manifest edit

- **Overview:** Run a full solution restore, build, and unit test pass inside `cadence check --pre-commit` whenever YAML, JSON, or MSBuild files are staged.
- **Pros:**
  - ✅ Reuses existing xUnit tests directly at commit time.
- **Cons:**
  - ❌ Adds multi-minute latency to routine manifest or workflow commits and inspects working-tree state rather than staged Git index blobs when unstaged changes are present.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **`TngTech.ArchUnitNET.xUnitV3` migration:** Replace `TngTech.ArchUnitNET.xUnit` with `TngTech.ArchUnitNET.xUnitV3` (`0.13.5`) in `Directory.Packages.props` and `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`, and update `KernelBoundaryTests.cs`, `IntelligenceToolBoundaryTests.cs`, and `SimulationBoundaryTests.cs` to bind `ArchUnitNET.xUnitV3.ArchRuleExtensions` so zero projects in `Cadence.sln` load `xunit.assert` `2.x`.
- **Dependabot cohort groups & ignores (`.github/dependabot.yml`):** Add `dotnet-testing` (`xunit*`, `Microsoft.NET.Test.Sdk`, `TngTech.ArchUnitNET*`), `analyzers` (`Microsoft.CodeAnalysis.*`, `SonarAnalyzer.CSharp`), and `anthropic` (`Anthropic*`) groups and `version-update:semver-major` ignores to the `nuget` block (keeping the separate `SonarAnalyzer.CSharp >= 10.7.0` ignore entry so Dependabot evaluates both rules independently), and make the `docker-compose` `/docker` `postgres` ignore unconditional so standalone Compose tag or `@sha256` digest bumps are suppressed unless synchronized with `PostgresTestImage.cs`.
- **Compile-time xUnit v3 & ArchUnitNET cohort enforcement (`Directory.Build.targets`):** Extend `ValidateDependencyAlignment` with `CADDEP004` to fail compilation if any `net10.0` project resolves legacy `xunit` v2 packages (`xunit`, `xunit.abstractions`, `xunit.assert`, `xunit.core`, `xunit.execution.*`, `xunit.extensibility.*`, `TngTech.ArchUnitNET.xUnit`) or multiple distinct `xunit.v3*` or `TngTech.ArchUnitNET*` versions.
- **Fail-closed CLI & pre-commit validation (`DependencyValidatorService`, `QualityGates`):** Extend `DependencyValidatorService.ValidateDependencies` (and wire `PreCommit = PreCommitMode.Quick` with `GitIndexReader` in `QualityGates.cs`) to validate XML syntax and cohort property alignment across `Directory.Build.props`, `Directory.Packages.props`, and `Cadence.AppHost.csproj`, `pnpm-workspace.yaml` `catalog:` usage, `.github/dependabot.yml` cohort groups and ignores, and `PostgresTestImage.Image` tag + `@sha256` parity across `docker/docker-compose.yml`, `.github/workflows/ci.yml`, `.github/workflows/production-pipeline.yml`, and `apps/apphost/src/Program.cs`.

## 5. Consequences & Trade-offs

- **Positive Impact:** `Cadence.sln` resolves a single `xunit.v3` assertion stack across all test projects; `CADDEP004` blocks accidental reintroduction of `xunit` v2 or split `xunit.v3`/`ArchUnitNET` packages during `dotnet build`; Dependabot batches test, analyzer, and `Anthropic*` updates cleanly; and `pnpm cadence check dependencies` catches MSBuild, `pnpm`, Dependabot, and PostgreSQL `@sha256` drift both in full runs and on staged index blobs during `--pre-commit`.
- **Negative Impact / Technical Debt:** PostgreSQL container digest upgrades and major analyzer/test/AI SDK migrations must be applied manually as coordinated lockstep commits rather than via single-file Dependabot PRs.
- **Mitigation Strategy:** `AGENTS.md` and `docs/CI.md` document the exact lockstep update steps for MSBuild cohorts, `Anthropic*`/test/analyzer majors, and PostgreSQL `@sha256` digests, and `ContainerImagePinTests` plus `PostgresVersionPinTests` verify every failure path.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Directory.Packages.props`, `Directory.Build.targets`, `tests/Cadence.ArchitectureTests`, `.github/dependabot.yml`, `packages/tooling_core`, `packages/cli`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/CI.md`, `docs/cli.md`, `docs/adr/index.md`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:**
  - `dotnet restore Cadence.sln && dotnet build Cadence.sln` and `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64` verify zero compiler, analyzer, or Native AOT trim warnings and zero `xunit.assert` `2.x` assets.
  - `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` and `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~ContainerImagePinTests|FullyQualifiedName~PostgresVersionPinTests|FullyQualifiedName~QualityGateRegistryTests"` verify all architecture rules, `CADDEP004` skew probes, PostgreSQL digest parity, and pre-commit staged-blob validation.
  - `pnpm cadence check dependencies`, `cli-docs`, `adrs`, `doc-index`, `yaml`, `markdown`, `duplication`, `query-linter`, `deid`, and `prettier` verify end-to-end gate and documentation alignment.
