# ADR-2535: Forward Verified CI GxP Evidence to Releases and Synchronize Service Migration Snapshots

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter, Stitch Agent
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails and deterministic GxP verification evidence), PRD-EVT-001

---

## 1. Context & Problem Statement

On pushes to `main`, `release/*`, and `v*` tags, `.github/workflows/ci.yml` executes the full `.NET 10` xUnit verification suite against a pinned PostgreSQL 18 container (`dotnet-tests`), validates GxP traceability (`compliance`), and then invokes `.github/workflows/production-pipeline.yml`. Previously, `production-pipeline.yml` had no access to the `TestResults/gxp` TRX and `*.database.txt` artifacts produced by `dotnet-tests` and defaulted `RTM_DRAFT=true`, while `deploy-docs.yml` triggered as an unguarded push workflow in parallel with validation. In addition, recent domain state-machine and transactional outbox updates in `ctms`, `tickets`, and `payments` modified EF Core entity configurations without recording matching EF Core migrations, causing `PendingModelChangesWarning` and `MigrationUnitTests` failures during PostgreSQL qualification.

## 2. Decision Drivers & Constraints

- Release archives published by `production-pipeline.yml` must contain non-draft, integrity-stamped GxP RTM and IQ/OQ/PQ execution reports backed by verified PostgreSQL 18 test evidence (`ADR-2531`, `ADR-2532`, `ADR-2533`).
- `dotnet-tests` must fail closed if either `tests/Cadence.IntegrationTests` or `tests/Cadence.E2ETests` omits TRX output, omits `*.database.txt` fixture evidence, or skips non-opt-in database tests (`DatabaseTestEvidence.OptInTests`).
- Every service `DbContext` model must match its EF Core migration snapshot (`MigrationUnitTests`) so `MigrateAsync()` executes cleanly against PostgreSQL 18.
- Artifact uploads across `ci.yml` and `production-pipeline.yml` must be scoped by `<run_id>-<run_attempt>` so re-runs never collide on immutable artifact names.

## 3. Options Considered

### Option 1: Gate reusable release/docs workflows in `ci.yml`, forward `gxp-test-evidence` to `gxp snapshot --results-dir`, and add missing service migrations (Selected)

- **Overview:** Upload verified `TestResults/gxp` (`*.trx` and `*.database.txt`) from `dotnet-tests` only after `scripts/check-database-test-results.sh` passes; consume that artifact in both `compliance` (`gxp sync --results-dir`) and `production-pipeline.yml` (`gxp snapshot --results-dir`, falling back to running `gxp snapshot` against pinned PostgreSQL 18 on `workflow_dispatch`); gate `deploy-docs.yml` via `workflow_call` from `ci.yml` on `main` pushes; and synchronize the EF Core migrations for `ctms` (`#5900`), `tickets` (`#5899`), and `payments` (`#5900`).
- **Pros:**
  - ✅ Eliminates duplicate `.NET` test execution during push release archiving while guaranteeing non-draft, PostgreSQL-backed GxP qualification bundles.
  - ✅ Prevents unvalidated documentation deployments and artifact name collisions on workflow re-attempts.
  - ✅ Restores 1:1 parity between EF Core runtime models and migration snapshots across all 15 platform services.
- **Cons:**
  - ❌ `production-pipeline.yml` must support both `workflow_call` (forwarded artifact) and standalone `workflow_dispatch` (self-hosted PostgreSQL 18 service container).

### Option 2: Re-run the full `.NET` PostgreSQL suite unconditionally inside `production-pipeline.yml`

- **Cons:**
  - ❌ Doubles CI runtime on every `main` and release push and can qualify a different transient execution than the one `compliance` verified.

## 4. Decision Outcome

**Chosen option: Option 1.**

1. `gxp snapshot` accepts `--results-dir <dir>` and requires recorded `*.database.txt` PostgreSQL fixture metadata (`DatabaseTestEvidence.HasRecordedEnvironment`) alongside zero non-opt-in skipped database tests.
2. `dotnet-tests` enforces assembly, `*.database.txt`, and `NotExecuted` checks via `scripts/check-database-test-results.sh` before uploading `gxp-test-evidence-${{ github.run_id }}-${{ github.run_attempt }}`.
3. `ci.yml` calls `production-pipeline.yml` and `deploy-docs.yml` only after all push validation jobs succeed, and disables `cancel-in-progress` on `push` events so release evidence pipelines are not aborted mid-run.
4. `ctms` (`AddTransactionalOutbox`, `#5900`), `tickets` (`EnforceTicketStatusCheckConstraint`, `#5899`), and `payments` (`AddPaymentStatusCheckConstraints`, `#5900`) include EF Core migrations matching their domain state-machine and transactional outbox configurations without duplicate migrations.
5. `apps/web` (`client.ts`, `demoMode.js`, `router/index.js`) and `apps/subject-portal` (`src/index.js`) guard circular ESM `import.meta.env` access, dynamically resolve `VITE_API_BASE_URL` across circular OpenAPI client imports, normalize path-based demo deep links into hash routes prior to `createWebHashHistory` initialization, and run `initializeApp()` immediately when `document.readyState !== "loading"` so all `accessibility-e2e` browser suites (`subject-portal`, `web`, and `BrowserCorrectionReachesTheDatabaseWithItsReason`) execute deterministically in CI.

## 5. Consequences & Trade-offs

- Release archives (`gxp-release-evidence-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}`) carry stamped, non-draft RTM and IQ/OQ/PQ reports without committing generated snapshots on feature branches (`ADR-2532`).
- Manual `workflow_dispatch` executions of `production-pipeline.yml` still qualify against a live pinned PostgreSQL 18 service container when no upstream `evidence_artifact` is supplied.
- Standalone CI runs of `apps/web` Playwright E2E execute against `--mode demo` with stateful MSW handlers while preserving `E2E_KEYCLOAK=true` for live Docker Keycloak runs and keeping non-demo runtime authentication strictly Keycloak-only (`ADR-2493`).

## 6. Implementation & Verification

- `packages/cli/src/Commands/GxPCommands.cs`, `packages/cli/src/Services/GxPComplianceService.cs`, `packages/tooling_core/src/Services/SyncGxPService.cs`, and `packages/compliance/src/DatabaseTestEvidence.cs`.
- `scripts/check-database-test-results.sh` and `scripts/test-check-database-test-results.sh`.
- `.github/workflows/ci.yml`, `.github/workflows/production-pipeline.yml`, and `.github/workflows/deploy-docs.yml`.
- EF Core migrations under `apps/ctms/src/Infrastructure/Persistence/Migrations/`, `apps/tickets/src/Infrastructure/Persistence/Migrations/`, and `apps/payments/src/Infrastructure/Persistence/Migrations/`.
- Browser & E2E verification across `apps/web/src/api/client.ts`, `apps/web/src/router/index.js`, `apps/web/src/mocks/demoHandlers.js`, `apps/subject-portal/src/index.js`, `apps/subject-portal/tests/e2e/`, and `tests/Cadence.IntegrationTests/Kernel/KernelObservationHostPostgresTests.cs`.
