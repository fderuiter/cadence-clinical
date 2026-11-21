# ADR-2535: Synchronize Service EF Core Migrations and Standalone Browser Accessibility Verification

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (audit integrity, schema determinism, and verified runtime boundaries), PRD-EDC-005 (case report and observation correction workflows)

---

## 1. Context & Problem Statement

Recent domain lifecycle hardening across `CtmsDbContext` and `PaymentsDbContext` introduced status check constraints and the CTMS transactional outbox (`TransactionalOutboxMessage`) in entity configurations without accompanying EF Core migrations, causing `MigrationUnitTests.ServiceDbContext_HasNoModelChangesMissingAMigration` and PostgreSQL 18 startup migration checks (`PendingModelChangesWarning`) to fail (`PRD-SYS-001`). Concurrently, browser verification uncovered three cross-cutting issues: (1) circular ES module evaluation between `apps/web/src/api/client.ts`, `authorization.js`, `stores/auth.js`, and `kernelApi.ts` evaluated `getBaseUrl()` before `import.meta.env` was initialized and froze `createOpenApiClient()`'s base URL before `VITE_API_BASE_URL` overrides applied in `BrowserCorrectionReachesTheDatabaseWithItsReason` (`PRD-EDC-005`); (2) `accessibility-e2e` in `.github/workflows/ci.yml` executed the full-stack Keycloak/Docker suite (`pnpm --filter web test:e2e`) in a runner without a Keycloak realm instead of targeting the standalone Playwright `accessibility` project (`pnpm --filter web test:e2e:a11y`); and (3) `AiVerifyLiveness` (`/api/v1/ai/biometrics/verify-liveness`), a stateless HMAC verification endpoint, was not listed in `KernelErrorContractTests.ComputationOnly`.

## 2. Decision Drivers & Constraints

- **Zero Unmigrated EF Core Model Drift (`PRD-SYS-001`):** Every `DbContext` model modification must carry an explicit, formatted EF Core migration and synchronized schema documentation so PostgreSQL 18 qualification fixtures (`DatabaseSchemaIsolationTests`) apply cleanly without `PendingModelChangesWarning`.
- **Resilient OpenAPI Client Initialization (`PRD-EDC-005`, ADR-2620):** Typed OpenAPI clients (`kernelApi.ts`, `executionApi.ts`) must tolerate circular module graph evaluation and dynamically resolve `getBaseUrl()` on every request when no explicit `baseUrl` override is passed.
- **Fail-Closed Standalone Accessibility Verification:** `accessibility-e2e` and `pnpm cadence ci --job accessibility-e2e` must execute real headless Chromium `@axe-core/playwright` WCAG 2.1 AA checks across both `apps/subject-portal` and `apps/web` plus `BrowserCorrectionReachesTheDatabaseWithItsReason` without requiring external Docker Compose containers or skipping tests.

## 3. Options Considered

### Option 1: Commit Service Migrations, Dynamic OpenAPI Base URL Dispatch, and Targeted `test:e2e:a11y` Execution (Selected)

- **Overview:** Generate and format EF Core migrations for `ctms` (`SyncCtmsOutboxAndLifecycleModel`) and `payments` (`SyncPaymentsOutboxAndPayoutModel`); guard `import.meta.env?.` and route default `createOpenApiClient()` calls through `dispatchOpenApiFetch` in `apps/web/src/api/client.ts`; add `AiVerifyLiveness` to `KernelErrorContractTests.ComputationOnly`; and wire `accessibility-e2e` in `ci.yml` and `CiPlan.cs` to `pnpm --filter web test:e2e:a11y` (`playwright test --project=accessibility`) separately from full-stack Keycloak workflows (`pnpm --filter web test:e2e`).
- **Pros:**
  - ✅ Satisfies `PRD-SYS-001` and `PRD-EDC-005` by restoring deterministic PostgreSQL 18 migrations across all services and end-to-end browser-to-database observation corrections.
  - ✅ Preserves Keycloak-only sign-in invariants (ADR-2492, ADR-2493) in `pnpm --filter web test:e2e` while running real `@axe-core/playwright` WCAG 2.2 AA audits (`ADR-2820`) in `accessibility-e2e` and local `cadence ci`.
  - ✅ Updates wallet tab coverage in `portal-accessibility.spec.js` to match authenticated portal routing.
- **Cons:**
  - ❌ Separates standalone accessibility verification (`test:e2e:a11y`) from full-stack Keycloak E2E workflows (`test:e2e`) in `apps/web`.

### Option 2: Suppress `PendingModelChangesWarning` or Inject Client-Side Mock Auth Into `apps/web`

- **Overview:** Ignore EF Core pending model warnings at startup and re-introduce client-side session injection to run `apps/web/tests/e2e` without Keycloak.
- **Pros:**
  - ✅ Avoids adding migrations or a separate Playwright config.
- **Cons:**
  - ❌ Violates `PRD-SYS-001` schema qualification rules and breaks ADR-2492's Keycloak-only authentication boundary.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Explicit EF Core migrations, dynamic request-time OpenAPI URL dispatch, and dedicated WCAG 2.1 AA Playwright verification preserve every security, GxP, and local-CI parity invariant (`LocalCiPlanTests`) without weakening runtime or test guardrails.

## 5. Consequences & Trade-offs

- **Positive Impact:** All `.NET 10` unit, architecture, PostgreSQL 18 integration/E2E, browser-to-database correction, and Playwright WCAG 2.1 AA suites execute deterministically both locally and in GitHub Actions.
- **Negative Impact / Technical Debt:** Full-stack Keycloak persona suites (`pnpm --filter web test:e2e`) remain targeted at full Docker Compose environments until remaining Python-era screens migrate onto kernel modules.
- **Mitigation Strategy:** `LocalCiPlanTests`, `MigrationUnitTests`, `DatabaseSchemaIsolationTests`, `WallClockAssertionTests`, and `KernelObservationHostPostgresTests` continuously enforce parity across CI and local runs.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/ctms`, `apps/payments`, `apps/econsent`, `apps/web`, `apps/subject-portal`, `packages/cli` (`CiPlan.cs`), `.github/workflows/ci.yml`, `docs/CI.md`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln --configuration Release`, all xUnit suites (`Cadence.UnitTests`, `Cadence.ArchitectureTests`, `Cadence.IntegrationTests`, `Cadence.E2ETests`, `Cadence.Kernel.ConsumerTests`) against PostgreSQL 18, `BrowserCorrectionReachesTheDatabaseWithItsReason`, `pnpm --filter subject-portal test:e2e`, `pnpm --filter web test:e2e:a11y`, `pnpm -r test`, `pnpm -r lint`, and `pnpm cadence check`.
