# 00a. Gate 5 Remediation — Clean `dotnet build Cadence.sln` Restored

**Document ID:** PHASE0-NET10-00A-BUILD-REMEDIATION
**Date:** 2026-09-03
**Scope:** Gate 5 (`.NET 10 Baseline Build & Tooling Gate`) from `00_validation_gate_report.md` §5 only. Gate 3 (environment-variable dictionary) and Gate 4 (Slice 01/Designer sequencing) are unaffected and out of scope for this remediation — they are tracked separately.

This is a standalone addendum rather than an appended section in `00_validation_gate_report.md` so it doesn't inherit that document's own unrelated, pre-existing markdown-lint debt (stale inline path references to Python-era files from before the .NET migration, in the Gate 3/4 sections) when committed.

---

## 1. Starting state at remediation time

`00_validation_gate_report.md` §5 recorded 13 restore errors: five packages in `Directory.Packages.props` pinned to a stale exact preview build (`10.0.0-preview.1.25120.13`) that no longer resolved on nuget.org (`NU1603`), `ArchUnitNET.xUnit 0.11.1` which did not exist on nuget.org at all (`NU1101`), and a known high-severity advisory on a transitive `Microsoft.OpenApi` package (`NU1903`).

By the time this remediation session started, `Directory.Packages.props` no longer matched that description: an intervening commit (`23d70a6`, "feat(hardening): add ArchUnitNET architecture tests, contracts library, git pre-commit hooks, and EF Core migration manager") had already replaced the five stale preview pins with `10.0.11` builds and `ArchUnitNET.xUnit 0.11.1` with `TngTech.ArchUnitNET.xUnit 0.13.4` — both real, published, resolving versions. So `NU1603`/`NU1101` as described in §5 were **already resolved** before this session began; `dotnet restore Cadence.sln` succeeded on the first attempt.

## 2. What this session found and fixed

### 2a. A second, previously-masked transitive version conflict

With restore actually succeeding for the first time, a reference-version conflict surfaced during `dotnet build`: `Microsoft.IdentityModel.Tokens 8.6.0` (pulled in via `System.IdentityModel.Tokens.Jwt` for JWT auth) depends on `Microsoft.Extensions.Logging.Abstractions 8.0.0` for `net10.0` — its NuGet dependency metadata has no `net10.0` group yet, so restore fell back to an older compatible group — which in turn pulls `Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2`. Elsewhere in the graph (ASP.NET Core / EF Core 10.0.11) the same two packages resolve to `10.0.11`.

`CentralPackageTransitivePinningEnabled` (already `true` in `Directory.Packages.props`) only pins a _transitive_ package to a specific version when that package id has its own `PackageVersion` entry in `Directory.Packages.props` — neither of these two had one, so the conflict wasn't pinned away. `dotnet build` reported it as `MSB3277` reference-version-conflict warnings in `tests/Cadence.ArchitectureTests.csproj`, fatal under this repo's `TreatWarningsAsErrors=true` policy.

**Fix** — added two explicit central pins to `Directory.Packages.props`, matching the `10.0.11` band already used by every other `Microsoft.*` package in the file:

```xml
<PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.11" />
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.11" />
```

No `.csproj` needed a direct `PackageReference` to either package — central transitive pinning applies the version repo-wide once the `PackageVersion` entry exists.

### 2b. A pre-existing project-layout defect producing a nondeterministic build

`tests/Cadence.UnitTests.csproj` and `tests/Cadence.ArchitectureTests.csproj` both live directly in `tests/`, with no `BaseIntermediateOutputPath`/`BaseOutputPath` overrides — the only place in the repo where two `.csproj` files share a directory (every other project has its own `src/` folder). Consequences:

- Both projects wrote to the same restore-generated `project.assets.json` (under each project's `obj/`) and to the same `bin/` output directory.
- The SDK's default `**/*.cs` glob made each project compile _both_ test files (`ArchitectureSentinelTests.cs` and `SecurityAndDomainTests.cs`), not just its own.

This was confirmed empirically, not just inferred: running `dotnet build Cadence.sln` clean, repeatedly, produced the same `CA1707`/`CA1860` analyzer diagnostics against the same four test methods every time — but attributed to a _different_ project on different runs (`Cadence.ArchitectureTests.csproj` on one run, `Cadence.UnitTests.csproj` on the next). That's the signature of a restore/analyzer-state race between two projects sharing one `obj/` directory, not a flaky analyzer. Building either project in isolation (with `tests/obj` cleared first) always succeeded — which is exactly what let the original stale-pin failure hide this second issue for as long as it did; restore never got far enough to expose it until fix 2a above landed.

**Fix:**

1. Gave each project its own `BaseIntermediateOutputPath`/`BaseOutputPath` (`obj\UnitTests\`/`bin\UnitTests\` and `obj\ArchitectureTests\`/`bin\ArchitectureTests\`). `BaseIntermediateOutputPath` must be set _before_ `Microsoft.Common.props` is imported or MSBuild emits `MSB3539`, so both `.csproj` files were converted from the `Project Sdk="Microsoft.NET.Sdk"` shorthand to the explicit form (separate `Sdk.props`/`Sdk.targets` imports with the path properties set ahead of the `Sdk.props` import) — the documented pattern for this exact scenario (MSB3539's own guidance link: `https://go.microsoft.com/fwlink/?linkid=869650`).
2. This does not eliminate the `**/*.cs` glob overlap — both projects still compile both test files, which is otherwise harmless (7 tests pass in each assembly) — but it does make which project's analyzer state applies deterministic, and removes the shared-restore-file corruption risk for future incremental/parallel builds.
3. Fixed the underlying analyzer violations at the source rather than suppressing them:
   - `CA1707` ("identifiers should not contain underscores") on 7 test methods across `ArchitectureSentinelTests.cs` and `SecurityAndDomainTests.cs` (e.g. `HmacSigner_GeneratesDeterministicSignature_AndVerifiesCorrectly`). Each was renamed to remove underscores (e.g. `HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly`), with the original descriptive name preserved via `Fact(DisplayName = "...")` so test output/reporting is unchanged.
   - `CA1860` ("prefer comparing Length to 0 rather than Any()") on two `GetCustomAttributes(...).Any()` calls in `ArchitectureSentinelTests.cs`, changed to `.Length > 0`.

   No `NoWarn`, `AnalysisLevel`, or `TreatWarningsAsErrors` change was used or needed for either.

### 2c. `NU1903` (`Microsoft.OpenApi` advisory `GHSA-v5pm-xwqc-g5wc`)

Already resolved by the `Microsoft.AspNetCore.OpenApi 10.0.11` pin present at the start of this session — it pulls a patched `Microsoft.OpenApi` transitively. Confirmed with:

```
$ dotnet list Cadence.sln package --vulnerable --include-transitive
...
The given project `Cadence.Apps.Gateway` has no vulnerable packages given the current sources.
...
```

Every project in the solution reports no vulnerable packages. **No risk-acceptance `NoWarn` was needed or added.**

## 3. Verification

Full clean rebuild (`find . -type d \( -name obj -o -name bin \) -exec rm -rf {} +` then build), repeated three times, identical result each time:

```
$ dotnet build Cadence.sln --configuration Release --verbosity normal
...
Restored /home/user/cadence-clinical/apps/execution/src/Cadence.Apps.Execution.csproj
Restored /home/user/cadence-clinical/apps/designer/src/Cadence.Apps.Designer.csproj
Restored /home/user/cadence-clinical/packages/testing/src/Cadence.Testing.csproj
Restored /home/user/cadence-clinical/tests/Cadence.ArchitectureTests.csproj
Restored /home/user/cadence-clinical/packages/engine_core/src/Cadence.Core.Domain.csproj
Restored /home/user/cadence-clinical/packages/security/src/Cadence.Security.csproj
Restored /home/user/cadence-clinical/packages/cli/src/Cadence.Cli.csproj
Restored /home/user/cadence-clinical/apps/gateway/src/Cadence.Apps.Gateway.csproj
Restored /home/user/cadence-clinical/packages/database/src/Cadence.Database.csproj
Restored /home/user/cadence-clinical/tests/Cadence.UnitTests.csproj
Restored /home/user/cadence-clinical/packages/contracts/src/Cadence.Contracts.csproj

Cadence.Core.Domain -> .../packages/engine_core/src/bin/Release/net10.0/Cadence.Core.Domain.dll
Cadence.Security -> .../packages/security/src/bin/Release/net10.0/Cadence.Security.dll
Cadence.Database -> .../packages/database/src/bin/Release/net10.0/Cadence.Database.dll
Cadence.Apps.Execution -> .../apps/execution/src/bin/Release/net10.0/Cadence.Apps.Execution.dll
Cadence.Testing -> .../packages/testing/src/bin/Release/net10.0/Cadence.Testing.dll
Cadence.Apps.Designer -> .../apps/designer/src/bin/Release/net10.0/Cadence.Apps.Designer.dll
Cadence.Apps.Gateway -> .../apps/gateway/src/bin/Release/net10.0/Cadence.Apps.Gateway.dll
Cadence.Contracts -> .../packages/contracts/src/bin/Release/net10.0/Cadence.Contracts.dll
Cadence.Cli -> .../packages/cli/src/bin/Release/net10.0/cadence.dll
Cadence.ArchitectureTests -> .../tests/bin/ArchitectureTests/Release/net10.0/Cadence.ArchitectureTests.dll
Cadence.UnitTests -> .../tests/bin/UnitTests/Release/net10.0/Cadence.UnitTests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:07.27
```

Also reproduced the exact sequence from `.github/workflows/dotnet-ci.yml`:

```
dotnet restore Cadence.sln
dotnet build Cadence.sln --configuration Release --no-restore
dotnet test Cadence.sln --configuration Release --no-build --logger "trx;LogFileName=test_results.trx"
```

All three steps succeeded; both test assemblies passed (7/7 each, 14/14 total).

Gate 5 standards re-check (unchanged from the original audit, still correct):

```
$ grep -E "TargetFramework|LangVersion|Nullable|TreatWarningsAsErrors|AnalysisLevel" Directory.Build.props
<TargetFramework>net10.0</TargetFramework>
<LangVersion>14</LangVersion>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<AnalysisLevel>latest-recommended</AnalysisLevel>

$ grep ManagePackageVersionsCentrally Directory.Packages.props
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>

$ grep -rn 'PackageReference.*Version=' --include="*.csproj" .
(no matches)
```

## 4. No policy loosened

`TreatWarningsAsErrors`, `Nullable`, `AnalysisLevel`, and `NoWarn` in `Directory.Build.props` are byte-for-byte unchanged from the original audit. No `NoWarn` entries were added anywhere in the solution. Every fix above is either a version pin scoped to the exact packages causing a real conflict, a build-output-path isolation fix, or a source-level correction of a genuine analyzer rule violation.

## 5. Status

This closes remediation item 1 of `00_validation_gate_report.md` §6/§7 (`Directory.Packages.props` fixes + a clean `dotnet build` with 0 warnings/0 errors). Items 2 (Gate 4 / MIG-03) and 3 (Gate 3 environment-variable dictionary) are unaffected by this addendum and remain tracked under their own remediation. `Cadence.sln` is build-verified clean as of this remediation and this baseline can be treated as such going forward.
