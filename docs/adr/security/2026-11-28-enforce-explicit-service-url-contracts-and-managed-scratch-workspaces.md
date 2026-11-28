# ADR-SECURITY-20261128-enforce-explicit-service-url-contracts-and-managed-scratch-workspaces: Enforce explicit service URL contracts and managed scratch workspaces

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Service-to-service HTTP clients, YARP reverse-proxy clusters, Vite development/build configurations, and temporary filesystem staging previously relied on implicit `http://localhost:<port>` fallbacks and ambient `System.IO.Path.GetTempPath()` directories (`PRD-SYS-190`, `PRD-IMG-002`). Specifically:

1. `apps/etmf` allowed `EXECUTION_SERVICE_URL` to remain unset outside `Development` and silently dropped `SITE_ACTIVATION` completeness webhooks when no base address was configured, while `apps/gateway` bound `service.LocalUrl` automatically in `Development` and `apps/apphost` pointed omitted `core` profile services at unused host loopback ports.
2. Frontend Vite configs and runtime API base URL resolvers fell back to `http://localhost:8000` outside explicit `development` `serve` or test sessions, and Vite proxy targets accepted `secure: false` indiscriminately.
3. Backend services (`apps/etmf`, `apps/imaging`, `apps/interop`) and `apps/apphost` used ambient OS temporary directories via `Path.GetTempPath()`, while `apps/imaging` spooled unauthenticated-scope multipart sections before validating `studyId` and `siteId` against `ClinicalScopeGuard`.

## 2. Decision Drivers & Constraints

- **Fail-closed service topology (`PRD-SYS-190`):** Non-development hosts must never fall back to `localhost`, omitted services must fail deterministically (`503 Service Unavailable`) rather than probing arbitrary loopback ports, and `Development` loopback fallbacks must require explicit opt-in (`Cadence:AllowLocalhostFallbacks=true`).
- **Regulated scratch isolation & pre-spool authorization (`PRD-IMG-002`):** Ephemeral files containing clinical exports, DICOM slices/archives, or SCE container workspaces must reside inside a dedicated, owner-only (`0700` directory / `0600` file) root (`Scratch:RootPath`) rather than world-shared `/tmp`, and multipart uploads must authorize `studyId` and `siteId` before spooling any file bytes to disk.
- **Compile-time enforcement:** Ambient `System.IO.Path.GetTempPath` calls must be banned across `apps/` at build time while remaining permitted in developer CLI and test harness packages (`packages/cli`, `packages/tooling_core`, `packages/testing`).

## 3. Options Considered

### Option 1: Shared topology URL validators, managed `IScratchWorkspaceProvider`, and Roslyn `BannedSymbols.txt` (Selected)

- **Overview:** Centralize `ServiceTopology.ResolveServiceUrl` and `ServiceTopology.ResolveServiceUrlOrDisabled` in `packages/topology`, add `IScratchWorkspaceProvider` (`FileSystemScratchWorkspaceProvider` + `AddCadenceScratchWorkspace`) in `packages/storage`, enforce `ClinicalScopeGuard.RequireStudyAndSite` prior to multipart spooling in `apps/imaging`, gate Vite/frontend loopback defaults to `development` `serve` and Vitest environments, and ban `Path.GetTempPath` across `apps/` via `Microsoft.CodeAnalysis.BannedApiAnalyzers`.
- **Pros:**
  - Eliminates silent webhook drops and accidental loopback connections across backend and frontend runtimes.
  - Confines transient clinical payloads to owner-only directories inside `Scratch:RootPath` with deterministic disposal and blocks unauthorized disk spooling.
  - Prevents regressions at compile time through `apps/BannedSymbols.txt`.
- **Cons:**
  - Requires explicit `Cadence:AllowLocalhostFallbacks=true` and `Scratch:RootPath` settings in local orchestration (`docker-compose.yml`, `AppHost`, `TestHostSettings`, `Cadence.OpenApiExporter`).

### Option 2: Per-service ad-hoc validation without compile-time bans

- **Overview:** Patch individual environment variable checks and temp paths inside each service without shared contracts or Roslyn banned-symbol enforcement.
- **Pros:**
  - Smaller touch surface in shared packages.
- **Cons:**
  - Leaves URL validation semantics inconsistent across `gateway`, `apphost`, and `etmf`, and allows `Path.GetTempPath()` to re-enter `apps/` undetected.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing URL and scratch workspace contracts in `packages/topology` and `packages/storage` paired with compile-time `BannedApiAnalyzers` enforcement guarantees uniform fail-closed startup behavior, eliminates silent webhook drops, and enforces owner-only scratch permissions across all application services.

## 5. Consequences & Trade-offs

- **Positive Impact:** Deterministic startup validation for service URLs and storage/scratch paths, HTTP `503` responses for explicitly `"disabled"` topology services, TLS verification (`secure: true`) on non-development Vite proxies, pre-spool study/site RBAC checks on DICOM uploads, and zero `Path.GetTempPath()` usage in `apps/`.
- **Negative Impact / Technical Debt:** Standalone local runs of `imaging` and `interop` must supply `Scratch__RootPath`, and standalone `gateway`/`etmf` runs without explicit `*_SERVICE_URL` values must opt into `Cadence__AllowLocalhostFallbacks=true`.
- **Mitigation Strategy:** Pre-configure `Cadence__AllowLocalhostFallbacks` and isolated volumes in `docker/docker-compose.yml`, `apps/apphost`, `.env.example`, `TestHostSettings`, and `Cadence.OpenApiExporter`, and document required variables in `docs/ENVIRONMENT_VARIABLES.md` and service READMEs.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/topology`, `packages/storage`, `packages/testing`, `packages/tooling_core`, `apps/gateway`, `apps/apphost`, `apps/etmf`, `apps/imaging`, `apps/interop`, `apps/web`, `apps/subject-portal`, `docker/`, and `scripts/vite-hoist-csp.mjs`.
- **Verification Plan:** Verified via `ScratchWorkspaceProviderTests`, `GatewayServiceUrlValidationTests`, `AppHostModelTests`, `EtmfCompletenessWebhookTests`, `EtmfExportTests`, `DicomWebEndpointsTests`, `DicomIngestionServiceTests`, `StatisticalComputingEnvironmentTests`, frontend `vite_config_contract` and `demo_isolation` suites, `BannedApiAnalyzers` build gates, and `./bin/cadence check` sentinels.
