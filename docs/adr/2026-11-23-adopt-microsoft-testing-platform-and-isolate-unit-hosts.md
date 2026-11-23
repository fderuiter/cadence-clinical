# ADR-2832: Adopt Microsoft.Testing.Platform and Isolate Unit WebApplicationFactory Hosts

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (GxP verification evidence integrity and fail-closed test qualification)

---

## 1. Context & Problem Statement

Cadence Clinical's five `.NET 10` test projects (`Cadence.UnitTests`, `Cadence.IntegrationTests`, `Cadence.ArchitectureTests`, `Cadence.E2ETests`, and `Cadence.Kernel.ConsumerTests`) run on `xunit.v3` (`4.0.3`), while `global.json` still relied on the legacy VSTest `dotnet test` runner with a temporary `<IsTestingPlatformApplication Condition="'$(IsTestingPlatformApplication)' == ''">false</IsTestingPlatformApplication>` workaround in `Directory.Build.props`. Moving to `.NET 10` native `Microsoft.Testing.Platform` (MTP) surfaced four coupling and compliance hazards:

1. **CLI & CI Runner Flags:** `.NET 10` MTP `dotnet test` rejects positional `.csproj`/`.sln` arguments, `-nologo`/`--nologo`, and VSTest `--logger "trx;..."` flags, requiring `--project` or `--solution` and `--report-xunit-trx`.
2. **Single-Line `xunit.v3` TRX Database Skip Verification:** `xunit.v3`'s `--report-xunit-trx` emits the entire `<TestRun>` XML document on one line and writes `Assert.Skip` reasons inside `<Output><StdOut>` rather than `<ErrorInfo><Message>`. Line-oriented `grep -v` opt-in skip filtering in `scripts/check-database-test-results.sh` could mask mandatory skipped database tests whenever an opt-in skip appeared in the same TRX file, and `DatabaseTestEvidence.FindSkipped` reported empty `Reason` strings.
3. **Workspace `node_modules` Coupling in Unit Guardrail Tests:** `PreCommitGuardrailsTests.FrontendEslintGate_ValidatesDuplicateIdentifiersInStagedClinicalEcrfSlice` symlinked the repository root's `node_modules`, failing whenever unit tests ran in a `.NET`-only checkout before `pnpm install`.
4. **Background PostgreSQL Poller Retries in Unit `WebApplicationFactory` Hosts:** Several unit tests booted real service `Program` entry points (`Cadence.Apps.*`, `Cadence.Kernel.Host`) on in-memory or SQLite providers without removing MassTransit's `BusOutboxDeliveryService<TDbContext>`, EF Core startup migrators, or kernel bootstrap workers, causing background `NpgsqlException` connection retries against `127.0.0.1:5432` when PostgreSQL was stopped.

## 2. Decision Drivers & Constraints

- **Native `.NET 10` Test Execution:** Eliminate the legacy VSTest shim and `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` override so all five `xunit.v3` projects execute as first-class `Microsoft.Testing.Platform` applications.
- **Fail-Closed GxP Database Evidence (`PRD-SYS-053`, ADR-2531):** `scripts/check-database-test-results.sh` and `DatabaseTestEvidence.FindSkipped` must reject any `NotExecuted` database test outside `DatabaseTestEvidence.OptInTests` and preserve skip reasons across both multi-line VSTest TRX and single-line `xunit.v3` MTP TRX formats.
- **Hermetic, Zero-Dependency Unit & Architecture Suites:** `Cadence.ArchitectureTests` and `Cadence.UnitTests` must pass in clean `.NET` checkouts without `node_modules` or a local PostgreSQL server on `127.0.0.1:5432`, while keeping `FrontendQualityGateService.EnsureNodeModules` fail-closed and keeping `SigTokenUseStoreStartupCheck` active in unit hosts (ADR-2515).
- **CI / Local Runner Parity:** `.github/workflows/ci.yml`, `CiPlan.cs` (`LocalCiPlanTests`), `QualityGates.cs` (all 43 gates), `TestCommand.cs`, and `SyncGxPService.cs` must stay strictly synchronized.

## 3. Options Considered

### Option 1: Adopt `Microsoft.Testing.Platform` Globally, Harden Single-Line TRX Parsing, Self-Contain `@babel/parser` Test Fixtures, and Isolate Unit Host Background Services via `TestHostSettings` (Selected)

- **Overview:** Configure `"test": { "runner": "Microsoft.Testing.Platform" }` in `global.json` and remove the `IsTestingPlatformApplication` override in `Directory.Build.props`. Migrate CI workflows, `CiPlan.cs`, `QualityGates.cs`, `TestCommand.cs`, `SyncGxPService.cs`, and documentation to `--project`/`--solution` and `--report-xunit-trx`. Split `<UnitTestResult>` elements before filtering in `scripts/check-database-test-results.sh` and read `<StdOut>` fallback skip reasons in `DatabaseTestEvidence.FindSkipped`. Provision a minimal `@babel/parser` package inside `_tempDir/node_modules/@babel/parser` in `PreCommitGuardrailsTests`. Add `DisableBackgroundHostedServices` and `RemoveBackgroundHostedServices` to `Cadence.Testing.Hosting.TestHostSettings` (preserving `SigTokenUseStoreStartupCheck` and any caller-specified in-memory workers) and apply them across unit `WebApplicationFactory` hosts.
- **Pros:**
  - ✅ Removes the VSTest compatibility workaround and standardizes MTP execution across CI, `cadence check`, `cadence test`, `cadence ci`, and `cadence gxp sync`.
  - ✅ Guarantees fail-closed database test verification on single-line `xunit.v3` TRX reports even when opt-in skips co-occur with mandatory database tests.
  - ✅ Makes `Cadence.UnitTests` and `Cadence.ArchitectureTests` completely hermetic without `pnpm install` or PostgreSQL, eliminating background `127.0.0.1:5432` retries while keeping `EnsureNodeModules` and `SigTokenUseStoreStartupCheck` active.
- **Cons:**
  - ❌ Contributors and scripts must pass `--project` or `--solution` to `dotnet test` instead of positional targets, and filtered solution runs where an entire assembly has zero matching tests require `--ignore-exit-code 8`.

### Option 2: Retain the VSTest Runner Shim in `Directory.Build.props`

- **Overview:** Keep `<IsTestingPlatformApplication>false</IsTestingPlatformApplication>` and positional `dotnet test` VSTest invocations while isolating unit `WebApplicationFactory` hosts separately.
- **Pros:**
  - ✅ Preserves legacy positional `dotnet test <path>` muscle memory.
- **Cons:**
  - ❌ Perpetuates a deprecated `.NET` test host bridge on top of `xunit.v3`, leaves `scripts/check-database-test-results.sh` vulnerable to single-line TRX formatting, and diverges from `.NET 10` SDK defaults.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Adopting `Microsoft.Testing.Platform` in `global.json` aligns all five `xunit.v3` suites with `.NET 10`'s native runner, while hardening TRX skip inspection and isolating unit `WebApplicationFactory` hosts and `@babel/parser` test fixtures makes local and CI verification faster, deterministic, and fail-closed.

## 5. Consequences & Trade-offs

- **Positive Impact:** Unit and architecture test suites run cleanly with zero external Node or PostgreSQL state and zero background `BusOutboxDeliveryService` connection retries; GxP TRX parsers handle both legacy multi-line VSTest and single-line `xunit.v3` MTP reports fail-closed.
- **Negative Impact / Technical Debt:** Bare positional `dotnet test <path>` invocations fail under `.NET 10` MTP when `global.json` selects `Microsoft.Testing.Platform`; unit tests that intentionally exercise a specific `IHostedService` worker on an in-memory store must pass that worker type to `DisableBackgroundHostedServices(typeof(TWorker))`.
- **Mitigation Strategy:** All repository scripts, CLI commands, `.agents` skills, and contributor guides document `--project` and `--solution`, `pnpm cadence test` wraps the flags transparently, and `TestHostSettings.DisableBackgroundHostedServices(params Type[] keepHostedServices)` supports explicit in-memory worker opt-ins while keeping `SigTokenUseStoreStartupCheck` enabled unconditionally.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `global.json`, `Directory.Build.props`, `.github/workflows/ci.yml`, `packages/cli`, `packages/tooling_core`, `packages/compliance`, `packages/testing`, `scripts/check-database-test-results.sh`, `scripts/test-check-database-test-results.sh`, `tests/Cadence.UnitTests`, `docs/`, `AGENTS.md`, `CONTRIBUTING.md`, `README.md`.
- **Verification Plan:** Verified via `dotnet test --project tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` and `dotnet test --project tests/Cadence.UnitTests/Cadence.UnitTests.csproj` without `node_modules` or PostgreSQL (`0` `127.0.0.1:5432` retries), `dotnet test --solution Cadence.sln`, `bash scripts/test-check-database-test-results.sh`, `pnpm cadence test --fast`, and `pnpm cadence check` (`architecture-tests`, `unit-tests`, `cli-docs`, `adrs`, `doc-index`, `markdown`, `readmes`, `workflow-security`, `rulesets`, `gxp-snapshot`).
