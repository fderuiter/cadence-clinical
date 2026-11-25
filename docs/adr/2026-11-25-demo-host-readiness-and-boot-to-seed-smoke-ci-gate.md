# ADR-2499: Demo Host Readiness Gate and Pre-OCI Boot-to-Seed Smoke CI Gate

- **Status:** Accepted
- **Date:** 2026-11-25
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-013 (tenant data isolation), PRD-SYS-001 (startup readiness and CI verification gates)
- **Extends:** [ADR-2533](2026-10-19-tests-ci-and-compose-run-one-pinned-postgresql-major.md), [ADR-2542](2026-10-19-the-kernel-host-provisions-the-development-tenant.md), [ADR-2543](2026-10-19-the-kernel-host-seeds-the-local-demo-study.md)

---

## 1. Context & Problem Statement

ADR-2542 and ADR-2543 added `DevelopmentTenantBootstrap` and `DevelopmentDemoData` to `Cadence.Kernel.Host` so a local stack can automatically provision the `cadence-local` tenant and seed the canonical `CADENCE-101` study (`SITE-101`, `SITE-102`, participants, roles, and realm persona grants) through the governed kernel command pipeline. Three operational and CI gaps remained:

1. **`Demo` environment startup guard and configuration:** `DevelopmentTenantBootstrap` permitted `Kernel:DevelopmentTenant:Code` only when `IHostEnvironment.IsDevelopment()` was true and had no `appsettings.Demo.json` profile, preventing dedicated `ASPNETCORE_ENVIRONMENT=Demo` deployments and smoke harnesses from booting with automatic migrations and idempotent `CADENCE-101` seeding while still failing closed in `Production` and `Staging`.
2. **Readiness before bootstrap completion and probe path compatibility:** `KernelDatabaseHealthCheck` checked PostgreSQL connectivity (`CanConnectAsync`) without waiting for `DevelopmentTenantBootstrap` to finish provisioning and seeding `CADENCE-101`, and `CadenceTelemetryExtensions.MapCadenceProbeEndpoints` exposed `/healthz/ready` without `/health/ready` for orchestrators and load balancers expecting the `/health/ready` convention.
3. **OCI container matrix ran before end-to-end boot-to-seed verification:** In `.github/workflows/ci.yml`, `container-security` built and scanned the full multi-service OCI container matrix immediately after `classify`, before any automated test verified that `Cadence.Kernel.Host` and `Cadence.Apps.Gateway` could actually boot from an empty PostgreSQL 18 database, run migrations and demo seeding, pass readiness checks, authenticate a seeded OIDC persona, and execute a read/write/read-back flow.

## 2. Decision Drivers & Constraints

- **Fail-closed production posture:** `Production` and `Staging` hosts must continue to throw `InvalidOperationException` at startup if `Kernel:DevelopmentTenant:Code` is set.
- **True readiness semantics:** `/healthz/ready` and `/health/ready` must report `200 Healthy` only after database migrations, tenant provisioning, and `CADENCE-101` demo seeding complete when `DevelopmentTenantBootstrap` is registered.
- **Non-skipping, real-stack smoke verification:** The smoke test must never silently skip when invoked via `--filter Category=Smoke`, must run against pinned PostgreSQL 18 (`PostgresTestImage.Image`), and must authenticate through `Cadence.Apps.Gateway` OIDC validation and HMAC-signed gateway header forwarding rather than bypassing the gateway with pre-signed test clients.
- **Fast CI feedback before expensive OCI builds:** Container image builds (`container-security`), `ci-gate`, `release-and-archive`, and `deploy-docs` must not proceed unless the smoke gate succeeds.

## 3. Options Considered

### Option 1: Rely on Existing `dotnet-tests` Shards Without Gating `container-security`

- **Overview:** Keep startup seeding tested only in `Development` inside the full `dotnet-tests` job and let `container-security` run in parallel.
- **Pros:**
  - ✅ No workflow DAG changes.
- **Cons:**
  - ❌ Wastes runner minutes building and scanning all OCI images when startup migrations, `Demo` configuration, readiness probes, or gateway-to-kernel wiring are broken.
  - ❌ Does not exercise `ASPNETCORE_ENVIRONMENT=Demo` or real OIDC bearer login through `Cadence.Apps.Gateway`.

### Option 2: Full Docker Compose Stack in CI Before `container-security`

- **Overview:** Build all Docker images and boot `docker/docker-compose.yml` with Keycloak before running `container-security`.
- **Pros:**
  - ✅ Exercises container entrypoints directly.
- **Cons:**
  - ❌ Defeats the pre-OCI fast-fail goal by building container images before the smoke gate itself can run.

### Option 3: `Demo` Environment Bootstrap + Readiness Gate + Testcontainers `boot-to-seed-smoke` Pre-OCI Gate (Selected)

- **Overview:** Permit `Development` and `Demo` in `DevelopmentTenantBootstrap` with `appsettings.Demo.json` defaults (`Database:MigrateOnStartup=true`, `Kernel:DevelopmentTenant:Code=cadence-local`, `DemoData=true`), gate `KernelDatabaseHealthCheck` on `DevelopmentTenantBootstrapReadiness`, expose `/health/ready` alongside `/healthz/ready`, add non-skipping `DemoBootToSeedSmokeTests` (`[Trait("Category", "Smoke")]`) over `WebApplicationFactory` + `Testcontainers.PostgreSql`, and gate `container-security` on a dedicated `boot-to-seed-smoke` CI job.
- **Pros:**
  - ✅ Fails closed in `Production` and `Staging` while making `ASPNETCORE_ENVIRONMENT=Demo` turnkey and idempotent across restarts.
  - ✅ Guarantees `/healthz/ready` and `/health/ready` report `200 Healthy` only once migrations and `CADENCE-101` seeding complete.
  - ✅ Completes in ~30 seconds against real PostgreSQL 18, verifying OIDC authentication (`crc.site101`), gateway entitlement posture lookup, signed identity header forwarding, eCRF form opening, participant screening, read-back persistence, and restart idempotency before any OCI image is built.
- **Cons:**
  - ❌ Adds a short upstream dependency (~1–2 minutes including restore/build) before `container-security` starts when container-triggering paths change.

## 4. Decision Outcome

- **Chosen Option:** Option 3 — `Demo` Environment Bootstrap + Readiness Gate + Testcontainers `boot-to-seed-smoke` Pre-OCI Gate.
- **Justification:** Combining explicit `Demo` environment bootstrap support, bootstrap-aware readiness probes (`/healthz/ready` and `/health/ready`), and a fast Testcontainers smoke test that exercises `Cadence.Apps.Gateway` -> `Cadence.Kernel.Host` -> PostgreSQL 18 catches broken migrations, seeders, DI registrations, and gateway auth contracts in seconds before spending CI compute on multi-image OCI builds.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - `DevelopmentTenantBootstrap.IsAllowedEnvironment` explicitly allows `Development` and `Demo` while continuing to throw `InvalidOperationException` in `Production`, `Staging`, and any other environment.
  - `DevelopmentTenantBootstrapReadiness` and `KernelDatabaseHealthCheck` hold `/healthz/ready` and `/health/ready` at `503 Unhealthy` until startup tenant provisioning and `CADENCE-101` seeding complete.
  - `CadenceTelemetryExtensions.MapCadenceProbeEndpoints` maps `/health/ready` anonymously alongside `/healthz/live` and `/healthz/ready` across all Cadence hosts and suppresses probe trace noise in OpenTelemetry.
  - `.github/workflows/ci.yml`, `CiPlan`, and `RulesetService` enforce `boot-to-seed-smoke` as a required CI gate and a direct prerequisite of `container-security` with zero skipped smoke tests allowed.
- **Negative Impact / Technical Debt:**
  - `RulesetService` merge-group/PR parity evaluation must evaluate `needs.<job>.result == 'success'` clauses on conditional jobs (`container-security`).
- **Mitigation Strategy:**
  - `RulesetService.EvaluateAtomicIfClause` explicitly models `needs.<job>.result` equality/inequality checks and is covered by unit tests in `RulesetServiceTests`, `LocalCiPlanTests`, and `LocalCiRunnerTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/kernel-api`, `packages/telemetry`, `packages/cli`, `tests/Cadence.IntegrationTests`, `tests/Cadence.UnitTests`, `.github/workflows/ci.yml`, `docs/CI.md`, `AGENTS.md`.
- **Verification Plan:**
  - `DemoBootToSeedSmokeTests` (`dotnet test tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj --filter Category=Smoke`) against PostgreSQL 18 (`Testcontainers.PostgreSql` and `CADENCE_TEST_POSTGRES`).
  - Unit and security suites in `KernelApiHostTests`, `TelemetryUnitTests`, `RouteAuthorizationTests`, `RulesetServiceTests`, `LocalCiPlanTests`, and `LocalCiRunnerTests`.
  - Repository quality gates via `cadence check` (`adrs`, `doc-index`, `workflow-security`, `rulesets`, `actionlint`, `markdown`, `openapi`, `schema-doc`) and `Cadence.ArchitectureTests`.
