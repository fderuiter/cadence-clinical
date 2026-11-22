# Fix Prompt A — Restore a Clean .NET 10 Build (Gate 5 Remediation)

## Role & Execution Protocol

You are a Principal .NET Build Engineer with direct workspace access. Your job is narrow and mechanical: make `dotnet build Cadence.sln --configuration Release` succeed with **0 Errors and 0 Warnings** under this repository's existing `TreatWarningsAsErrors=true` policy, without weakening that policy or any other rule in `Directory.Build.props`. You are not authorized to loosen `TreatWarningsAsErrors`, `Nullable`, `AnalysisLevel`, or `NoWarn` beyond what already exists — if a real fix requires touching one of those, stop and flag it instead of silently disabling it.

**Source of truth for this task:** `docs/migration/phase0/00_validation_gate_report.md`, §5 ("Gate 5 — .NET 10 Baseline Build & Tooling Gate"). Read it first. Do not re-derive the failure from scratch — the exact errors, root causes, and file locations are already recorded there. Your job is to fix what it found, then prove the fix with a real build log, not to re-audit.

---

## Step 0 — Get a working .NET 10 SDK on PATH

This repo pins SDK `10.0.100` (`rollForward: latestFeature`) in `global.json`. If `dotnet` is not already on PATH in your environment, install it before doing anything else (e.g. `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --version 10.0.100 --install-dir <dir>` and add `<dir>` to `PATH`/`DOTNET_ROOT`). Confirm with `dotnet --info`. Do not proceed on the assumption the SDK is unavailable — a prior Phase 0 pass wrongly certified this repo without ever getting the SDK installed; do not repeat that mistake.

## Step 1 — Reproduce the failure exactly as reported

```bash
dotnet build Cadence.sln --configuration Release --verbosity normal
```

Confirm you see the same three error classes the report documents:

- `NU1603` (Warning As Error) on `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.Extensions.Http.Polly` — all pinned in `Directory.Packages.props` to an exact preview build (`10.0.0-preview.1.25120.13`) that no longer resolves on nuget.org.
- `NU1101` on `ArchUnitNET.xUnit` version `0.11.1` in `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` — this package/version does not exist on nuget.org at all.
- `NU1903` (Warning As Error) — `Microsoft.OpenApi 2.0.0-preview7` has a known high-severity advisory (`GHSA-v5pm-xwqc-g5wc`), pulled in transitively by `Microsoft.AspNetCore.OpenApi`.

If your build produces a _different_ error set than this, the repo has changed since the audit — re-read the current `Directory.Packages.props` and adjust your plan accordingly; do not force-fit the fixes below onto a different failure.

## Step 2 — Fix the stale preview pins

For each of the 5 packages pinned to `10.0.0-preview.1.25120.13` (or `10.0.0-preview.1` for `Npgsql.EntityFrameworkCore.PostgreSQL`) in `Directory.Packages.props`:

1. Query nuget.org for the actual set of currently-published versions (e.g. `dotnet package search <PackageId> --prerelease --exact-match`, or the NuGet v3 flat-container index). Do not guess a version number — confirm it resolves.
2. Prefer the _*latest published preview build in the `10.0.0-preview.*` band that is consistent across all Microsoft.* packages_* (EF Core, AspNetCore.OpenApi, Extensions.Http.Polly should generally move together — check for a coherent "SDK band" rather than picking the newest patch of each independently, which can itself produce cross-package version conflicts).
3. Update the pin in `Directory.Packages.props` only — do not add per-project `Version=` overrides in any `.csproj` (Central Package Management must stay clean; the report confirms it is clean today, keep it that way).

## Step 3 — Fix the nonexistent ArchUnitNET package

`ArchUnitNET.xUnit 0.11.1` does not exist on nuget.org. Investigate what package id/version this project actually needs:

- Check what xUnit major version `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` targets (xUnit v2 vs xUnit v3 — the `ArchUnitNET` ecosystem has split adapter packages, e.g. `ArchUnitNET.xUnitV3` for xUnit v3 projects, plain `ArchUnitNET` + a separate test-runner-agnostic API otherwise).
- Confirm the correct package id and a real, published version against nuget.org before pinning it.
- Update `Directory.Packages.props` and, if the API surface differs from what `tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`'s test files (`ArchitectureSentinelTests.cs` et al.) currently call, fix the call sites too — but keep changes to the minimum needed to compile; this is a build-fix task, not a test-rewrite task.

## Step 4 — Resolve the Microsoft.OpenApi vulnerability advisory

Check first whether re-pinning `Microsoft.AspNetCore.OpenApi` to a current build (Step 2) also pulls a patched `Microsoft.OpenApi` transitively — this is the most likely fix and requires no further action beyond Step 2. If the advisory still fires after that:

- Check if a newer `Microsoft.OpenApi` version resolves the advisory and is compatible with the AspNetCore.OpenApi version in use; add an explicit `PackageVersion` pin for `Microsoft.OpenApi` in `Directory.Packages.props` if needed to force the patched version.
- Only if no patched version exists yet upstream: do not silently suppress `NU1903` — document the accepted risk explicitly (a `NoWarn` scoped to `NU1903` only, with a comment naming the advisory, the affected package, and why it's accepted) and flag it prominently in your final report so a human can make the call. Do not choose this path if Step 2/the paragraph above resolves it.

## Step 5 — Full clean rebuild and verification

```bash
dotnet clean Cadence.sln
rm -rf **/obj **/bin   # or equivalent — ensure no stale restore artifacts
dotnet build Cadence.sln --configuration Release --verbosity normal
```

Required result: **0 Errors, 0 Warnings**, all projects build. Capture the full build output.

Also re-run the Gate 5 standards checks from the validation report to confirm nothing regressed:

```bash
grep -E "TargetFramework|LangVersion|Nullable|TreatWarningsAsErrors|AnalysisLevel" Directory.Build.props
grep ManagePackageVersionsCentrally Directory.Packages.props
grep -rn 'PackageReference.*Version=' --include="*.csproj" .   # must return nothing
```

## Deliverable

- Updated `Directory.Packages.props` (and any minimal call-site fixes required by the ArchUnitNET package swap).
- A short writeup (append to `docs/migration/phase0/00_validation_gate_report.md` as a new dated addendum, or a new `docs/migration/phase0/00a_build_remediation.md` — pick whichever this repo's conventions favor at the time) recording: the exact versions you pinned and why, the full passing `dotnet build` output, and — if you had to accept the `NU1903` risk rather than eliminate it — an explicit, unmissable note to that effect.
- Do not touch anything related to Gate 3 (env var dictionary) or Gate 4 (Slice 01 sequencing) — those are separate prompts. Stay scoped to the build.
