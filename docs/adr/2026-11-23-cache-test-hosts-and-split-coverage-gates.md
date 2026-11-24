# ADR-2670: Cache Test Hosts and Split Coverage Gates

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Amends:** [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md), [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md), [ADR-2537](2026-11-22-split-ui-vitest-matcher-and-enforce-demo-bundle-budgets.md)
- **Requirements:** PRD-SYS-001 (deterministic fast developer verification while preserving full GxP, WCAG, and whole-app coverage gates in CI)

---

## 1. Context & Problem Statement

Inner-loop developer feedback across both the .NET and frontend workspaces suffered from avoidable cold-start and whole-workspace coupling:

1. Several HTTP contract, security, and routing suites in `tests/Cadence.UnitTests` booted fresh `WebApplicationFactory<TEntryPoint>` hosts or Aspire `DistributedApplication` models per `[Fact]`/`[Theory]` case—including 74 separate `Cadence.Kernel.Host` instances in `KernelApiHostTests`, repeated 15-service boots in `RouteAuthorizationTests` and `SigTokenUseStoreWiringTests`, per-test factories in `ContextualHelpEndpointsTests`, `SubjectEndpointsRoutingTests`, and `PaymentsCallerAuthorizationTests`, uncached Aspire models in `AppHostModelTests`, and a 10,000-record unit dataset in `RuleBenchmarkTests` duplicating `Cadence.Rules.Benchmarks`.
2. `pnpm cadence test --fast` passed `Category!=Integration` across the entire `Cadence.sln`, forcing MSBuild to compile `Cadence.IntegrationTests` and execute `Cadence.E2ETests` during unit-only checks, and `cadence test` lacked `--project`, `--filter`, and `--no-build` flags for targeted runs.
3. Workspace `"test"` scripts ran `vitest run --coverage` unconditionally, causing single-file Vitest runs (`pnpm --filter web test tests/notifications.test.js`) to fail against [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) whole-app coverage floors, while `apps/web/vitest.config.js` booted `jsdom` and eagerly preloaded `axe-core` in `packages/ui/accessibility-matcher.js` even for pure Node contract tests.
4. `scripts/playwright-chromium-launcher.mjs` unconditionally wrapped Linux Chromium launches in `taskset -c 0`, throttling multi-core developer workstations outside CI.

## 2. Decision Drivers & Constraints

- **Zero cross-test state leakage:** Shared `IClassFixture` test hosts must isolate mutable state per test via resettable fakes and cloned in-memory SQLite databases (`BackupDatabase`), removing both `DbContextOptions<TContext>` and `IDbContextOptionsConfiguration<TContext>` descriptors.
- **Uncompromised CI coverage & GxP evidence:** Every whole-app and 80% per-file coverage ratchet from [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md), every `[GxPRequirement]` and `[KernelRule]` test tag, and every fail-closed gate in [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md) must remain enforced in `test:coverage`, `ci.yml`, and `pnpm cadence ci`.
- **Single test project layout:** Existing test project boundaries (`TestLayoutTests`) must remain unchanged without introducing new `.csproj` directories or wall-clock latency assertions (`WallClockAssertionTests`).

## 3. Options Considered

### Option 1: Class-Scoped Host Caching, Scoped `cadence test` Flags, Split `test` vs `test:coverage`, Dual `node`/`jsdom` Vitest Projects, and CI-Gated `taskset` (Selected)

- **Overview:**
  - Cache booted `WebApplicationFactory` and `DistributedApplication` instances per class (`ServiceHostCache`, `KernelApiHostTests.HostFixture`, `ContextualHelpHostFixture`, `SubjectEndpointsTestFactory`, `PaymentsCallerAuthHostFixture`, `ExpiredSubscriptionStandaloneServicesTests.HostFixture`, `AppHostModelTests.ModelFixture`) with per-test fake resets and SQLite `:memory:` `BackupDatabase` clones, and reduce `RuleBenchmarkTests` to 1,000 records while keeping the 1,000,000-record benchmark in `Cadence.Rules.Benchmarks`.
  - Scope `cadence test --fast` to `Cadence.UnitTests.csproj` and `Cadence.ArchitectureTests.csproj`, add `--project`/`-p`, `--filter`, and `--no-build` options to `TestCommand`, and synthesize `test-options` in `docs/cli.md` via `GateDocs`.
  - Separate `"test": "vitest run"` from `"test:coverage": "vitest run --coverage"` across `package.json`, `apps/web`, `packages/ui`, `apps/subject-portal`, and `packages/usdm-schemas`, and update `.github/workflows/ci.yml` and `CiPlan.cs` together to run `pnpm -r test:coverage`.
  - Split `apps/web/vitest.config.js` into `node` and `jsdom` projects while keeping lazy on-demand `getAxe()` loading inside `toBeAccessible` (`packages/ui/accessibility-matcher.js`).
  - Gate Linux `taskset -c 0` pinning in `scripts/playwright-chromium-launcher.mjs` behind truthy `CI` or `CADENCE_PIN_CHROMIUM_SINGLE_CORE=1`.
- _**Pros:**_ Cuts redundant host boots and `jsdom`/`axe-core` overhead, allows fast targeted `.NET` and Vitest test runs without tripping whole-app coverage floors, unthrottles local Linux Playwright launches, and preserves 100% of CI coverage and GxP guardrails.
- _**Cons:**_ Shared `IClassFixture` hosts require explicit `Reset()` hooks or per-request scoped database overrides whenever new mutable test fakes are introduced.

### Option 2: Move `WebApplicationFactory` Suites into Separate Test Projects

- **Overview:** Split HTTP host unit tests into dedicated `.csproj` assemblies so developers can skip host assemblies locally.
- _**Pros:**_ Avoids refactoring individual fixtures to `IClassFixture`.
- _**Cons:**_ Violates `TestLayoutTests` single-project ownership under `tests/`, still pays 100+ cold host startups whenever those assemblies run in CI, and fragments GxP requirement evidence.

### Option 3: Disable Vitest Coverage Thresholds Globally

- **Overview:** Remove `thresholds` from Vitest configs so `vitest run --coverage` passes on single files.
- _**Pros:**_ Avoids adding separate `test:coverage` scripts.
- _**Cons:**_ Violates [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) by dropping automated whole-app and Part 11 80% per-file coverage enforcement in CI.

## 4. Decision Outcome

- **Chosen Option:** Option 1 (Class-Scoped Host Caching, Scoped `cadence test` Flags, Split `test` vs `test:coverage`, Dual `node`/`jsdom` Vitest Projects, and CI-Gated `taskset`).
- **Justification:** Caching immutable hosts per class with isolated per-test state and separating inner-loop `test` from CI `test:coverage` removes cold-start and single-file friction while keeping all [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) and [ADR-2534](2026-10-20-quality-gates-live-in-one-registry-and-each-must-be-able-to-fail.md) quality gates fail-closed.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `Cadence.UnitTests` reuses booted service and kernel hosts across cases without cross-test state leakage, and `pnpm cadence test --fast` runs `Cadence.UnitTests` and `Cadence.ArchitectureTests` directly without compiling `Cadence.IntegrationTests` or running `Cadence.E2ETests`.
  - Targeted Vitest file runs (`pnpm --filter web test <file>`) succeed cleanly, pure Node specs in `apps/web` execute without `jsdom` or `axe-core` initialization, and `pnpm -r test:coverage` continues to enforce all [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) coverage floors in `ci.yml` and `cadence ci`.
  - Local Linux Playwright runs use all CPU cores by default while retaining deterministic single-core `taskset` pinning under `CI` or `CADENCE_PIN_CHROMIUM_SINGLE_CORE=1`.
- **Negative Impact & Mitigations:**
  - Contributors adding mutable fields to shared host fakes must reset them in `Reset()`, mitigated by explicit `Reset()` calls in fixture/test constructors and SQLite `:memory:` `BackupDatabase` cloning per request scope.

## 6. Implementation & Verification

- **Affected Files:**
  - `tests/Cadence.UnitTests/Security/ServiceHosts.cs`, `RouteAuthorizationTests.cs`, `SigTokenUseStoreWiringTests.cs`, `ExpiredSubscriptionStandaloneServicesTests.cs`, `Imaging/Services/DicomIngestionServiceTests.cs`
  - `tests/Cadence.UnitTests/Kernel/Api/KernelApiHostTests.cs`, `Kernel/Rules/RuleBenchmarkTests.cs`, `AppHost/AppHostModelTests.cs`
  - `tests/Cadence.UnitTests/Knowledge/ContextualHelpEndpointsTests.cs`, `Execution/Endpoints/SubjectEndpointsRoutingTests.cs`, `Payments/PaymentsCallerAuthorizationTests.cs`
  - `packages/cli/src/Commands/TestCommand.cs`, `Services/CiPlan.cs`, `Services/GateDocs.cs`, `docs/cli.md`
  - `.github/workflows/ci.yml`, `package.json`, `apps/web/package.json`, `apps/web/vitest.config.js`, `apps/web/src/components/AppShell.vue`, `packages/ui/package.json`, `packages/ui/accessibility-matcher.js`, `apps/subject-portal/package.json`, `packages/usdm-schemas/package.json`
  - `scripts/playwright-chromium-launcher.mjs`, `apps/subject-portal/tests/playwright-chromium-launcher.test.js`, `.env.example`, `docs/ENVIRONMENT_VARIABLES.md`, `docs/TESTING.md`, `docs/CI.md`
- **Verification:**
  - `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj` and `dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj` pass with zero state leakage, preserved requirement tags, and synced `GateDocs` / `LocalCiPlanTests`.
  - `pnpm --filter web test tests/notifications.test.js`, `pnpm -r test:coverage`, and `apps/subject-portal/tests/playwright-chromium-launcher.test.js` pass with all [ADR-2530](2026-09-30-measure-web-coverage-over-all-source-with-a-ratchet-floor.md) thresholds enforced.
