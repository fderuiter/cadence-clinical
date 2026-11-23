# ADR-2824: Use Incremental Managed Builds for Local CLI and Keep Native AOT in CI

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @cadence-team
- **Requirements:** PRD-SYS-053
- **Supersedes:** [ADR-2249](2026-09-29-native-aot-cli-compilation-and-makefile-binding.md) (local Native AOT compilation)

---

## 1. Context & Problem Statement

Under [ADR-2249](2026-09-29-native-aot-cli-compilation-and-makefile-binding.md) and [ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md), `packages/cli/src/Cadence.Cli.csproj` enabled `<PublishAot>true</PublishAot>` unconditionally while also referencing unused domain, persistence, and EF Core projects (`Cadence.Core.Domain`, `Cadence.Database`, `Cadence.Security`, and `Npgsql.EntityFrameworkCore.PostgreSQL`). In addition, `scripts/run-cli.js` compared file modification timestamps (`mtimeMs`) across 9 package directories and 6 MSBuild configuration files and invoked `dotnet publish -c Release --self-contained` whenever any watched timestamp advanced.

In day-to-day engineering workflows—especially `.githooks/pre-commit`, `pnpm cadence`, `Makefile` targets, branch switches, and setup scripts—timestamp changes triggered full Native AOT `ilc` compilation locally, stalling developer commands and pre-commit checks for 55 to 113 seconds even when watched file contents had not changed or only a single line of C# had been edited. Meanwhile, `.github/workflows/ci.yml` (`native-aot-build`) and `packages/cli/src/Services/CiPlan.cs` already run a dedicated Native AOT publish check for CI and release artifacts.

## 2. Decision Drivers & Constraints

- **Fast Local Feedback Loops:** Eliminate 55–113s local Native AOT `ilc` stalls during `.githooks/pre-commit`, `pnpm cadence`, and agent hooks (`@req:PRD-SYS-053`).
- **Deterministic Cache Invalidation:** Avoid false-positive rebuilds from `git checkout`, rebase, or timestamp-only updates (`utimes`).
- **Zero Regression in Trim / AOT Safety:** Every local build must still fail closed on `IL2xxx` and `IL3xxx` trim and AOT warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), with zero suppressions ([ADR-2496](2026-09-30-parse-the-cli-with-system-commandline-and-publish-aot-without-suppressions.md)).
- **Production & CI Parity:** CI (`native-aot-build`) and release artifacts must continue publishing and verifying the self-contained Native AOT binary via `ilc`.
- **Minimal Dependency Graph:** `Cadence.Cli` should reference only the projects and packages it directly consumes (`Cadence.Compliance`, `Cadence.ToolingCore`, `Cadence.Kernel.Abstractions`, `System.CommandLine`, `Spectre.Console`, and `Npgsql`).

## 3. Options Considered

### Option 1: Keep Unconditional `<PublishAot>true</PublishAot>` Locally with SHA-256 Hashing

- **Overview:** Replace `mtimeMs` checks in `scripts/run-cli.js` with SHA-256 content hashing while keeping `dotnet publish` with Native AOT locally.
- **Pros:**
  - Eliminates false rebuilds when timestamps change without content edits.
- **Cons:**
  - Any legitimate edit to `packages/cli`, `packages/tooling_core`, `packages/compliance`, `packages/security`, `packages/deid`, `packages/topology`, `Cadence.Kernel.Abstractions`, or root MSBuild props still incurs a 55–113s `ilc` native compilation penalty during local development and pre-commit checks.

### Option 2: Incremental Managed `dotnet build` Locally with `IsAotCompatible` + `EnableTrimAnalyzer`, and `-p:PublishAot=true` in CI (Selected)

- **Overview:** Replace unconditional `<PublishAot>true</PublishAot>` in `Cadence.Cli.csproj` with `<IsAotCompatible>true</IsAotCompatible>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`, prune unused project/package references, build `./bin/cadence` locally via incremental `dotnet build` gated by a SHA-256 input digest (`./bin/.cli-input-hash`), and pass `-p:PublishAot=true` explicitly in `.github/workflows/ci.yml` and `CiPlan.cs`.
- **Pros:**
  - Reduces local cold builds and one-line incremental rebuilds to a few seconds while warm runs execute the compiled `./bin/cadence` apphost directly without `dotnet run` MSBuild evaluation overhead.
  - Preserves `IL2xxx` and `IL3xxx` trim/AOT static analysis as build-breaking errors on every local build.
  - Preserves full `ilc` Native AOT compilation and single-file artifact distribution in CI (`native-aot-build` and `cadence ci`).
  - Prunes `engine_core` and `database` from the CLI dependency graph, watch list, and CI cache keys.
- **Cons:**
  - Local `./bin` contains managed framework-dependent build outputs (`cadence`, `cadence.dll`, `cadence.runtimeconfig.json`, and dependency assemblies) rather than a single static binary, requiring `scripts/run-cli.js` and workflow caches to verify managed runtime companion files.

### Option 3: Delegate `pnpm cadence` Directly to `dotnet run`

- **Overview:** Remove `./bin/cadence` caching from `scripts/run-cli.js` and invoke `dotnet run --project packages/cli/src/Cadence.Cli.csproj` on every command.
- **Pros:**
  - Minimal wrapper code.
- **Cons:**
  - Re-introduces >1.2s MSBuild graph evaluation latency on every `pnpm cadence` and agent hook invocation, violating the sub-100ms warm startup goal from [ADR-2249](2026-09-29-native-aot-cli-compilation-and-makefile-binding.md).

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Option 2 retains fast warm execution via `./bin/cadence`, cuts local rebuild latency from ~1–2 minutes to a few seconds via incremental `dotnet build` and pruned references, eliminates timestamp-only rebuilds via SHA-256 content hashing, and preserves both local `IL2xxx`/`IL3xxx` analyzer enforcement and CI Native AOT `ilc` verification.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Local `pnpm cadence`, `Makefile`, `.githooks/pre-commit`, and `.agents/hooks.json` invocations skip rebuilds whenever watched file contents are unchanged and complete incremental rebuilds in seconds.
  - Edits to `packages/engine_core` and `packages/database` no longer invalidate the CLI binary locally or in CI caches.
  - `Cadence.Cli` depends directly on `Npgsql` (`10.0.4` in `Directory.Packages.props`) instead of pulling `Npgsql.EntityFrameworkCore.PostgreSQL`, `Cadence.Database`, and `Cadence.Core.Domain`.
- **Negative Impact / Technical Debt:**
  - `scripts/run-cli.js` must verify that `./bin/cadence.dll` and `./bin/cadence.runtimeconfig.json` accompany `./bin/cadence` for managed builds (including when `CADENCE_CLI_CACHE_HIT=true`), while allowing prebuilt Native AOT artifacts in downstream `ci.yml` jobs.
- **Mitigation Strategy:**
  - Separate the Native AOT cache key prefix (`cadence-cli-aot-`) in `ci.yml` from managed `./bin` caches (`cadence-cli-`) in `deploy-docs.yml` and `production-pipeline.yml`, keep `packages/security/src` watched for transitive `Cadence.Compliance` / `Cadence.Deid` changes, and enforce `IsAotCompatible`, `EnableTrimAnalyzer`, `-p:PublishAot=true`, and `run-cli.js` cache invalidation in automated tests.

## 6. Implementation & Verification

- Updated `packages/cli/src/Cadence.Cli.csproj` (`IsAotCompatible`, `EnableTrimAnalyzer`, pruned references) and pinned `Npgsql` in `Directory.Packages.props`.
- Updated `scripts/run-cli.js` to write `./bin/.cli-input-hash` (SHA-256 over watched source trees and MSBuild config files), verify `cadence.dll` and `cadence.runtimeconfig.json`, and run incremental `dotnet build` into `./bin`.
- Updated `.github/workflows/ci.yml`, `.github/workflows/deploy-docs.yml`, `.github/workflows/production-pipeline.yml`, and `packages/cli/src/Services/CiPlan.cs` to pass `-p:PublishAot=true` for Native AOT publishing and cache `./bin` with pruned dependency globs.
- Enforced via `WarningSuppressionPolicyTests`, `LocalCiPlanTests`, and `PreCommitGuardrailsTests`, plus `dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64 -p:PublishAot=true` and `pnpm cadence check`.
