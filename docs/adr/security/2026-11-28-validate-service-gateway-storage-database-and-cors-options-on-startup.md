# ADR-SECURITY-20261128-validate-service-gateway-storage-database-and-cors-options-on-startup: Validate Service, Gateway, Storage, Database, and CORS Options on Startup

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (21 CFR Part 11 fail-closed configuration and access controls)
- **Extends:** [ADR-2260](../2026-09-29-no-default-credentials-and-fail-closed-secret-resolution.md), [ADR-2451](../2026-09-29-gateway-timeouts-and-health-checked-clusters.md), [ADR-2491](../2026-09-30-http-edge-hardening-forwarded-headers-hsts-rate-limits.md), [ADR-2636](../2026-10-20-inference-is-pluggable-residency-bound-and-sandboxed.md)

---

## 1. Context & Problem Statement

Several .NET service, gateway, module, and shared infrastructure configuration bindings previously relied on unvalidated POCO bindings or imperative startup parsing that deferred validation until first request or allowed unsafe defaults:

1. `FileshareWorkerOptions` (`apps/fileshare`) bound `FileshareWorker` without `DataAnnotations` or `ValidateOnStart()`, allowing zero or negative batch sizes, concurrency limits, retry counts, and polling/backoff intervals to reach worker loops.
2. `InferenceOptions` (`apps/intelligence`) validated provider configuration inside provider constructors resolved by `InferenceStartupCheck`, surfacing misconfigurations as `InvalidOperationException` rather than `OptionsValidationException` and missing raw `Mode` enum token validation or positive `Timeout` bounds.
3. `SandboxEgressOptions` (`apps/intelligence`) accepted blank `AllowedClients` entries, and `SandboxEgressConfiguration.Configure` attached `SandboxEgressHandler` only when `string.IsNullOrEmpty(name)` was false, leaving unnamed default `IHttpClientFactory.CreateClient()` (`Options.DefaultName == ""`) calls unguarded inside `AgentSandbox` scopes.
4. `KeycloakAdminOptions` (`apps/kernel-api`) bound `KeycloakAdmin` without startup validation, deferring malformed `BaseUrl` or partial credential errors until runtime trial onboarding calls (`POST /api/v1/kernel/onboarding/trial`).
5. `SignatureCredentialOptions`, gateway cluster timeouts, and gateway rate limits (`apps/gateway`) either threw `UriFormatException` on malformed OIDC authority/token URLs during endpoint resolution or parsed raw `IConfiguration` integers instead of binding startup-validated options models (`PRD-SYS-001`).
6. `FileSystemStorageOptions`, `S3StorageOptions` (`packages/storage`), and PostgreSQL connection resolution (`packages/database`) enforced imperative registration guards without startup-validated `IOptions<T>` bindings, and `CadenceTelemetryExtensions` (`packages/telemetry`) lacked a fail-closed `Cors:AllowedOrigins` options guard rejecting blank, wildcard (`*`), or non-HTTPS origins.

## 2. Decision Drivers & Constraints

- **Fail-closed host startup (PRD-SYS-001, ADR-2260):** Misconfigured worker bounds, OIDC/Keycloak URLs, gateway timeouts/rate limits, storage settings, database connection strings, inference providers, sandbox egress allowlists, and CORS origins must fail `IHost.StartAsync()` immediately with `OptionsValidationException`.
- **Zero-bypass sandbox egress (ADR-2636):** Every `IHttpClientFactory` client—including unnamed `Options.DefaultName` (`""`) clients—must pass through `SandboxEgressHandler` while an `AgentSandbox` run scope is active, and blank or whitespace `AllowedClients` entries must be rejected at startup.
- **Strict HTTPS CORS posture (ADR-2491):** Cross-origin resource sharing must emit zero CORS headers when `Cors:AllowedOrigins` is empty and reject blank, wildcard (`*`), or non-HTTPS origins on host startup.
- **Backward compatibility for synchronous callers:** Existing callers and unit tests that invoke `AddCadenceStorage` or `ResolvePostgresConnectionString` directly on `ServiceCollection` without starting a host must continue to observe immediate synchronous guard exceptions alongside `ValidateOnStart()` registrations.

## 3. Options Considered

### Option 1: Standardized `DataAnnotations` + `IValidateOptions<T>` + `ValidateOnStart()` across hosts and shared packages (Selected)

- **Overview:** Annotate option models with `System.ComponentModel.DataAnnotations` attributes, pair cross-property invariants with `.Validate(...)` or `IValidateOptions<T>`, register `.ValidateOnStart()` so `IStartupValidator` throws `OptionsValidationException` during `IHost.StartAsync()`, close the `Options.DefaultName` bypass in `SandboxEgressConfiguration`, and bind `CadenceCorsOptions` (`Cors:AllowedOrigins`) in `CadenceTelemetryExtensions`.
- **Pros:**
  - Enforces uniform `OptionsValidationException` failure semantics across `fileshare`, `intelligence`, `kernel-api`, `gateway`, `storage`, `database`, and `telemetry` (`PRD-SYS-001`).
  - Eliminates unnamed `HttpClient` egress bypass inside `AgentSandbox` and blocks wildcard or non-HTTPS CORS origins before Kestrel accepts traffic.
  - Preserves synchronous registration guards in `AddCadenceStorage` and `ResolvePostgresConnectionString`.
- **Cons:**
  - Requires `InferenceStartupCheck` to resolve `IEnumerable<IInferenceProvider>` inside `StartAsync` rather than its constructor so `IStartupValidator` runs before hosted service execution.

### Option 2: Ad-hoc imperative `IConfiguration` checks in `Program.cs`

- **Overview:** Keep manual `IConfiguration` lookups and throw `InvalidOperationException` in composition roots or constructors.
- **Pros:**
  - Avoids introducing option classes for gateway timeouts, rate limits, database connection strings, and CORS origins.
- **Cons:**
  - Produces inconsistent exception types, bypasses DI `IOptions<T>` inspection, and leaves nested options or unnamed `HttpClient` pipelines unvalidated until first use.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- **Justification:** Standardizing on `OptionsBuilder<T>.ValidateDataAnnotations()`, cross-field `Validate(...)` / `IValidateOptions<T>` rules, and `.ValidateOnStart()` guarantees fail-closed startup validation (`OptionsValidationException`) across every affected host and shared library while satisfying `PRD-SYS-001`, `ADR-2260`, `ADR-2491`, and `ADR-2636`.

Key architectural changes:

1. **Fileshare workers (`apps/fileshare`):** Annotate `FileshareWorkerOptions` (`BatchSize`, `MaxConcurrentJobs`, `MaxRetryCount` with `[Range(1, int.MaxValue)]`) and validate positive `TranscodePollInterval`, `ConversionPollInterval`, and `InitialBackoff` values with `ValidateOnStart()`.
2. **Intelligence inference & sandbox egress (`apps/intelligence`):** Annotate `InferenceOptions` and nested provider options, register `InferenceOptionsValidator : IValidateOptions<InferenceOptions>` with `ValidateOnStart()` (checking raw `Mode` enum tokens, positive `Timeout`, `ResidencyCodes`, global `Vertex` residency prohibition, and `InDeploymentEndpoint`), defer `InferenceStartupCheck` provider resolution to `StartAsync`, validate non-whitespace `SandboxEgressOptions.AllowedClients` on startup, and block `Options.DefaultName` (`""`) and whitespace client names in `SandboxEgressConfiguration`.
3. **Keycloak onboarding (`apps/kernel-api`):** Bind `KeycloakAdminOptions` with `ValidateDataAnnotations()`, `Validate(...)`, and `ValidateOnStart()`, allowing completely unconfigured `KeycloakAdmin` sections while requiring valid HTTP/HTTPS `BaseUrl` and non-empty `AdminPassword`, `Realm`, `AdminRealm`, `ClientId`, and `AdminUsername` whenever any `KeycloakAdmin` property is set.
4. **Gateway credentials, clusters, and rate limiting (`apps/gateway`):** Replace `Uri` throwing in `SignatureCredentialOptions` with `TryResolveTokenEndpoint` (`Uri.TryCreate`) + startup validation, and replace imperative `IConfiguration` parsing in `GatewayClusters` and `GatewayRateLimiting` with `[Range]`-annotated `GatewayClusterOptions` and `GatewayRateLimitOptions` bound via `ValidateDataAnnotations().ValidateOnStart()`.
5. **Shared storage, database, and CORS packages (`packages/storage`, `packages/database`, `packages/telemetry`):** Bind `[Required]`-annotated `FileSystemStorageOptions`, `S3StorageOptions`, and `CadencePostgresOptions` with `ValidateOnStart()` (preserving synchronous guards), and register startup-validated `CadenceCorsOptions` (`Cors:AllowedOrigins`) in `AddCadenceWebDefaults` / `UseCadenceWebDefaults` rejecting blank, `*`, or non-HTTPS origins and emitting no CORS headers when `Cors:AllowedOrigins` is empty.

## 5. Consequences & Trade-offs

- **Positive Impact:** Invalid service, gateway, storage, database, inference, sandbox, or CORS configuration is caught deterministically during host startup with `OptionsValidationException`, unnamed `HttpClient` egress inside `AgentSandbox` is refused, and wildcard/cleartext CORS origins cannot be enabled.
- **Negative Impact / Technical Debt:** Empty array placeholders such as `Intelligence__Sandbox__AllowedClients__0=` bind as empty strings in `IConfiguration` and now fail startup if uncommented; `.env.example` therefore keeps indexed list examples commented until populated.
- **Mitigation Strategy:** Document indexed array keys (`Intelligence__Sandbox__AllowedClients__<index>`, `Cors__AllowedOrigins__<index>`) in `docs/ENVIRONMENT_VARIABLES.md` and `.env.example`, and cover all startup validation paths in `ServiceOptionsStartupValidationTests`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/fileshare`, `apps/intelligence`, `apps/kernel-api`, `apps/gateway`, `packages/storage`, `packages/database`, `packages/telemetry`, `.env.example`, and `docs/ENVIRONMENT_VARIABLES.md`.
- **Verification Plan:** Verified via `tests/Cadence.UnitTests/Packages/ServiceOptionsStartupValidationTests.cs` (covering `WebApplicationFactory` and `IHost.StartAsync()` `OptionsValidationException` assertions across all option types, `AgentSandbox` default `HttpClient` refusal, and strict HTTPS `Cors:AllowedOrigins` header behavior) and `tests/Cadence.ArchitectureTests`.
