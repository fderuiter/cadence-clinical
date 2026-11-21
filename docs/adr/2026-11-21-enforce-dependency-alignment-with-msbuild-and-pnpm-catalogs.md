# ADR-2527: Enforce Dependency Alignment with MSBuild Properties and pnpm Catalogs

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Relates to:** [ADR-2233](2026-09-02-migration-to-dotnet-10-architecture-and-standards.md), [ADR-2327](2026-08-03-upgrade-environments-to-python-314.md), [ADR-2508](2026-10-16-pin-pnpm-and-node-with-packagemanager-and-engines.md), [ADR-2524](2026-10-19-messaging-stays-on-the-apache-licensed-masstransit-8-line.md), [ADR-2525](2026-10-20-dependabot-bumps-merge-as-one-locally-validated-batch.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md)

---

## 1. Context & Problem Statement

Ungrouped dependency update rules in `.github/dependabot.yml` and repeated version literals across `Directory.Packages.props`, `apps/apphost/src/Cadence.AppHost.csproj`, `.config/dotnet-tools.json`, `docker/Dockerfile.dotnet`, and five workspace `package.json` manifests allowed single-package bumps to introduce cross-manifest and transitive version skew while GitHub Actions runners remain paused until 2027 (ADR-2525, PRD-SYS-001):

1. **.NET, Aspire, IdentityModel, and CLI tool skew:** `Directory.Packages.props` repeated `.NET 10` (`10.0.14` vs `10.0.15`), `IdentityModel` (`8.22.0` vs `8.23.0`, plus transitive `8.19.2`), and `Aspire` (`13.6.5` packages vs `13.5.4` `Aspire.AppHost.Sdk` in `Cadence.AppHost.csproj`) literals. Meanwhile, `AWSSDK.S3 4.1.2` transitively pulled `System.IO.Hashing 11.0.0` into `net10.0` projects, and `.config/dotnet-tools.json` pinned `dotnet-ef` to `11.0.0` (`rollForward: false`) while `docker/Dockerfile.dotnet` installed `10.0.11`.
2. **Frontend workspace skew:** Shared cohorts (`@typescript-eslint`, `vitest`, `@playwright`, `vue`, `vite`, `typescript`, `pinia`) were duplicated across `package.json`, `packages/ui/package.json`, `packages/usdm-schemas/package.json`, `apps/web/package.json`, and `apps/subject-portal/package.json`, leaving `@typescript-eslint/eslint-plugin` at `^8.72.2` alongside `@typescript-eslint/parser` `^8.73.0`.
3. **Statistical container drift & fail-open manifest validation:** Unguarded Dependabot Docker updates drifted `docker/sce/python/Dockerfile` and `docker/sce/r/Dockerfile` away from the qualified `python:3.14-slim` and `r-base:4.4.2` GxP statistical execution baselines in ADR-2327 and `docs/openapi/interop_openapi.json`. In addition, `DependencyValidatorService.ValidateDependencies` caught JSON parse exceptions and returned `0` (failing open) and checked only `apps/web/package.json`.

## 2. Decision Drivers & Constraints

- **Single source of truth per cohort:** Lockstep `.NET`, `Aspire`, `IdentityModel`, `Microsoft.Extensions.AI`, `OpenTelemetry`, `MassTransit`, `FluentValidation`, and shared `npm` cohorts must define each version once with zero duplicate literals across `.NET` and `pnpm` manifests.
- **Compile-time transitive enforcement:** Because `.githooks/pre-commit` skips `dotnet build` when no C# files are staged and GitHub Actions is paused, `dotnet build Cadence.sln` itself must reject `.NET 11+` framework assemblies or split `IdentityModel`/`Aspire` graphs on `net10.0` without disabling Roslyn analyzers, `CentralPackageTransitivePinningEnabled`, or `TreatWarningsAsErrors`.
- **Native AOT & test isolation (`K-XS-1`, ADR-2496, ADR-2534):** `packages/cli` and `packages/tooling_core` must remain 100% Native AOT clean (zero `IL2xxx`/`IL3xxx` warnings), preserve `FloorNativeAotPacks` in `Directory.Build.targets`, fail closed on malformed JSON manifests, and still pass `QualityGateRegistryTests` against isolated temporary directories without manifests.
- **GxP statistical baseline preservation:** Platform migrations to `.NET 11`, `Python 3.15`, or `R 4.6` remain out of scope; statistical execution containers stay pinned by tag and `@sha256` digest to `python:3.14-slim` and `r-base:4.4.2`.

## 3. Options Considered

### Option 1: MSBuild cohort properties + `Directory.Build.targets` compile-time skew gate + `pnpm-workspace.yaml` `catalog:` + Dependabot groups/ignores + fail-closed `DependencyValidatorService` (selected)

- **Overview:** Centralize lockstep NuGet and SDK versions in `Directory.Build.props` consumed by `Directory.Packages.props`, `Cadence.AppHost.csproj`, and `Directory.Build.targets`; enforce `ValidateDependencyAlignment` before `CoreCompile` across all `net10.0` projects; move all multi-manifest `npm` packages into `pnpm-workspace.yaml` `catalog:`; align `dotnet-ef` (`10.0.15`) and SCE Dockerfiles (`python:3.14-slim`, `r-base:4.4.2`); configure Dependabot `groups` and runtime `ignore` rules; make `DependencyValidatorService` fail closed on malformed JSON; and lock everything with `ContainerImagePinTests`.
- **Pros:**
  - ✅ Eliminates duplicate version literals across `.NET` and `pnpm` manifests at the package-manager layer (`CentralPackageTransitivePinningEnabled` + `pnpm` `catalog:`).
  - ✅ Catches transitive major-version skew (`CADDEP001`) and split `IdentityModel`/`Aspire` closures (`CADDEP002`, `CADDEP003`) deterministically during `dotnet build`.
  - ✅ Fails closed on malformed `package.json` and `.config/dotnet-tools.json` files while keeping `Cadence.Cli` Native AOT clean.
- **Cons:**
  - ❌ Dependabot has partial support for MSBuild property indirection and `pnpm-workspace.yaml` catalogs, requiring documented manual cohort bump steps when reviewing grouped PRs.

### Option 2: Custom CLI script scanning `Directory.Packages.props` and `package.json` literals without MSBuild targets or `pnpm` catalogs

- **Overview:** Keep literal versions in every manifest and add a regex-based CLI linter to compare strings across files.
- **Pros:**
  - ✅ Dependabot can edit raw string literals directly in individual manifests.
- **Cons:**
  - ❌ Does not inspect resolved NuGet transitive graphs (`project.assets.json` / `PackageDefinitions` / `ReferencePath`), missing indirect skew such as `AWSSDK.Core 4.1.x -> System.IO.Hashing 11.0.0` or transitive `IdentityModel 8.19.2`.
  - ❌ Leaves contributors manually editing five `package.json` files and dozens of `<PackageVersion>` lines on every routine upgrade.

### Option 3: Rely solely on `.github/dependabot.yml` groups without MSBuild or `pnpm` structural enforcement

- **Overview:** Add `groups` and `ignore` rules to `.github/dependabot.yml` while leaving manifests unchanged.
- **Pros:**
  - ✅ Minimal diff in `.github/dependabot.yml`.
- **Cons:**
  - ❌ Leaves existing version drift unresolved and provides zero compile-time or unit-test protection against manual edits or transitive package upgrades while GitHub Actions is paused.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **MSBuild cohort properties (`Directory.Build.props`, `Directory.Packages.props`, `Cadence.AppHost.csproj`):** Define `DotNetServicingVersion` (`10.0.15`), `AspireHostingVersion` (`13.6.5`), `IdentityModelVersion` (`8.23.0`), `DotNetAiVersion` (`10.11.0`), `OpenTelemetryVersion` (`1.19.1`), `OpenTelemetryInstrumentationVersion` (`1.19.0`), `MassTransitVersion` (`8.5.12`), and `FluentValidationVersion` (`12.2.0`) in `Directory.Build.props`. Reference those properties across `Directory.Packages.props` (including transitive `Microsoft.IdentityModel.Protocols.OpenIdConnect` under `CentralPackageTransitivePinningEnabled=true` and `AWSSDK.S3 4.0.104.12`), `Cadence.AppHost.csproj` (`<Sdk Name="Aspire.AppHost.Sdk" Version="$(AspireHostingVersion)" />`), and `Cadence.Cli.csproj` / `FloorNativeAotPacks`.
- **Compile-time skew target (`Directory.Build.targets`):** `ValidateDependencyAlignment` runs after `ResolvePackageAssets` and `ResolveAssemblyReferences` before `CoreCompile` on `net10.0`, failing compilation with `CADDEP001` on `.NET 11+` `System.*`/`Microsoft.*` framework packages or assemblies, `CADDEP002` on split or misaligned `IdentityModel` versions, and `CADDEP003` on split or misaligned `Aspire` SDK/package versions.
- **Central `pnpm` catalog (`pnpm-workspace.yaml`):** Move all 19 shared frontend packages (including `@typescript-eslint/*` unified on `^8.73.0`, `vitest`, `@vitest/coverage-v8`, `@playwright/test`, `@axe-core/playwright`, `vue`, `vue-router`, `pinia`, `vite`, `@vitejs/plugin-vue`, `typescript`) into `catalog:` in `pnpm-workspace.yaml`, reference `"catalog:"` across all five workspace `package.json` manifests, and refresh `pnpm-lock.yaml`.
- **CLI tool, SCE Dockerfiles, and Dependabot alignment:** Pin `dotnet-ef` to `10.0.15` in `.config/dotnet-tools.json` (`rollForward: false`) and `docker/Dockerfile.dotnet`; restore `python:3.14-slim@sha256:9aaa474097d094040649f2162a627c0049b408f52138d980916788d80f04802f` and `r-base:4.4.2@sha256:fe9b29520eeb5292d814b0958783c0ddfcdab37402967a3e67307604354f98d7` in `docker/sce/{python,r}/Dockerfile`; and add lockstep `groups` and runtime `ignore` rules across `nuget`, `npm`, and `docker` in `.github/dependabot.yml`.
- **Fail-closed `DependencyValidatorService` & xUnit sentinels:** `DependencyValidatorService.ValidateDependencies` validates JSON syntax and schema across root/workspace `package.json` files and `.config/dotnet-tools.json` (returning `1` on malformed JSON or forbidden client-side crypto packages `node-forge`, `elliptic`, `jose`, and `0` when an isolated test fixture has no manifests). `ContainerImagePinTests` verifies CLI tools, statistical Dockerfiles, MSBuild properties and skew targets, `pnpm` catalog usage, Dependabot rules, and `DependencyValidatorService`.

## 5. Consequences & Trade-offs

- **Positive Impact:** Lockstep `.NET`, `Aspire`, `IdentityModel`, and `pnpm` cohorts have a single version definition per cohort; transitive `.NET 11+` or `IdentityModel` skew fails immediately during `dotnet build Cadence.sln`; statistical execution containers match ADR-2327 and `interop_openapi.json`; and `cadence check dependencies` no longer masks broken JSON manifests.
- **Negative Impact / Technical Debt:** When Dependabot cannot update an MSBuild property in `Directory.Build.props` or a `catalog:` version in `pnpm-workspace.yaml` automatically, maintainers update the central property or catalog entry once and refresh lockfiles/pins together.
- **Mitigation Strategy:** `AGENTS.md` and `docs/CI.md` document the exact manual cohort update steps and local MSBuild, `pnpm`, and xUnit verification commands, and `ContainerImagePinTests` fails immediately if `.config/dotnet-tools.json`, `docker/Dockerfile.dotnet`, `Directory.Build.props`, `pnpm-workspace.yaml`, or `.github/dependabot.yml` drift out of sync.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets`, `apps/apphost`, `packages/cli`, `packages/tooling_core`, `pnpm-workspace.yaml`, workspace `package.json` manifests, `pnpm-lock.yaml`, `.config/dotnet-tools.json`, `docker/Dockerfile.dotnet`, `docker/sce/python/Dockerfile`, `docker/sce/r/Dockerfile`, `.github/dependabot.yml`, `tests/Cadence.UnitTests`, `AGENTS.md`, `docs/CI.md`, `docs/cli.md`.
- **Verification Plan:**
  - `dotnet restore Cadence.sln && dotnet build Cadence.sln` verifies zero warnings/errors on `HEAD`, and `ContainerImagePinTests.DirectoryBuildTargets_FailsCompilationOnDotNet11OrSplitIdentityModelOrAspireSkew` verifies compile-time failure (`CADDEP001`, `CADDEP002`, `CADDEP003`) on skewed resolutions.
  - `pnpm install --frozen-lockfile`, `pnpm build`, and `pnpm -r test` verify unified catalog resolution across all workspace packages.
  - `pnpm cadence check dependencies`, `cli-docs`, `adrs`, `doc-index`, `yaml`, and `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter FullyQualifiedName~ContainerImagePinTests` verify fail-closed manifest parsing, CLI documentation sync, and sentinel coverage.
