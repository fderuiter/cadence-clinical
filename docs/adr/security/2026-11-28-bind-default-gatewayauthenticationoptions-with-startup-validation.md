# ADR-SECURITY-20261128-bind-default-gatewayauthenticationoptions-with-startup-validation: Bind Default GatewayAuthenticationOptions With Startup Validation

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

`AddCadenceSecurity` previously bound `GATEWAY_SECRET` only onto the named authentication scheme options (`GatewayAuthenticationHandler.SchemeName = "CadenceGateway"`) inside `AddScheme<GatewayAuthenticationOptions, GatewayAuthenticationHandler>` without registering the default unnamed `GatewayAuthenticationOptions` or `ValidateOnStart()`. Consequently, services and callers resolving `IOptions<GatewayAuthenticationOptions>` or `IOptionsMonitor<GatewayAuthenticationOptions>.CurrentValue` observed an unbound `GatewaySecret` unless tests or hosts configured the unnamed options explicitly, and hosts with an unset or short (< 32 character) `GATEWAY_SECRET` booted without failing startup validation.

Downstream services worked around this gap with inconsistent raw configuration fallbacks:

1. **CTMS (`StartupGreenlightApplicationService`):** Read `IConfiguration["GATEWAY_SECRET"]` directly during `CertifyAsync` and `BatchCertifyAsync` after `ElectronicSignatureEndpointFilter` had already verified and consumed the single-use `X-Sig-Token` against `GatewayAuthenticationOptions` (ADR-2515, PRD-SSU-003, SRS-SYS-005).
2. **eTMF (`HttpEtmfCompletenessWebhookPublisher`):** Read `IOptionsMonitor<GatewayAuthenticationOptions>.CurrentValue` (unbound default name) and fell back to `IConfiguration["GATEWAY_SECRET"]`, bypassing the 32-character minimum check.
3. **Payments (`PaymentHmacTokenHelper` and `PaymentEndpoints`):** Made `IOptions<GatewayAuthenticationOptions>` optional and fell back to `Environment.GetEnvironmentVariable("GATEWAY_SECRET")` without enforcing minimum secret length (PRD-PAT-002).

## 2. Decision Drivers & Constraints

- **Fail-Closed Startup Validation (ADR-2256, ADR-2260):** Services calling `AddCadenceSecurity` must refuse to start when `GATEWAY_SECRET` is unset, whitespace, or shorter than `MinimumSecretLength` (32 characters), matching `docs/ENVIRONMENT_VARIABLES.md` and `apps/gateway/src/Program.cs`.
- **Single-Use E-Signature Atomicity (ADR-2515, SRS-SYS-005):** Configuration errors must fail at host startup or before consuming a single-use `X-Sig-Token`, never mid-request after token redemption.
- **Unified Options Source of Truth:** Default (`Options.DefaultName`) and named (`"CadenceGateway"`) `GatewayAuthenticationOptions` must resolve the same validated secret across `GatewayAuthenticationHandler`, `TrainingDelegationService`, `HttpKernelLockGuard`, CTMS, eTMF, and Payments, including `WebApplicationFactory` tests that override either option name.
- **Native AOT Compatibility (ADR-2496):** `Cadence.Security` is referenced by `Cadence.Cli` (`PublishAot=true`), so options registration and synchronization must avoid reflection-based `ConfigurationBinder` calls.
- **Token Wire Compatibility (ADR-2642, ADR-2817):** Existing HMAC-SHA256 token formats under `GATEWAY_SECRET` for CTMS greenlight gates and Payments receipt OCR / diary sync verification tokens must remain unchanged.

## 3. Options Considered

### Option 1: Bind and Synchronize Default and Named `GatewayAuthenticationOptions` with `ValidateOnStart()` and Remove Raw Fallbacks (Selected)

- **Overview:** Register `AddOptions<GatewayAuthenticationOptions>()` (default) with `IConfiguration["GATEWAY_SECRET"]` binding, `MinimumSecretLength = 32` validation, and `.ValidateOnStart()`, alongside named validation on `GatewayAuthenticationHandler.SchemeName` and an explicit `IPostConfigureOptions<GatewayAuthenticationOptions>` synchronizer. Refactor CTMS, eTMF, and Payments to inject `IOptions<GatewayAuthenticationOptions>` exclusively without `IConfiguration` or `Environment.GetEnvironmentVariable` fallbacks.
- **Pros:**
  - Enforces fail-closed startup validation across every backend service calling `AddCadenceSecurity`.
  - Eliminates configuration split-brain between `ElectronicSignatureEndpointFilter` and downstream application handlers.
  - Preserves Native AOT compilation (`0` trim/AOT warnings) and existing `WebApplicationFactory` test overrides on either default or named options.
- **Cons:**
  - Requires updating unit tests that constructed CTMS, eTMF, or Payments services with raw `IConfiguration` or ambient `GATEWAY_SECRET` environment variables to pass `IOptions<GatewayAuthenticationOptions>`.

### Option 2: Introduce Separate Signing Key Environment Variables per Domain Service

- **Overview:** Keep `GATEWAY_SECRET` scoped to `GatewayAuthenticationHandler` and add separate `CTMS_GREENLIGHT_SECRET` and `PAYMENTS_TOKEN_SECRET` environment variables.
- **Pros:**
  - Separates intra-service token keys from gateway header HMAC keys.
- **Cons:**
  - Breaks existing token contracts documented in ADR-2642 and ADR-2817, expands operator secret management surface, and still leaves eTMF outbound gateway signing and `IOptionsMonitor.CurrentValue` callers inconsistent.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Centralizing binding, 32-character length validation, and `ValidateOnStart()` in `AddCadenceSecurity` makes `IOptions<GatewayAuthenticationOptions>` the single authoritative source of `GATEWAY_SECRET` across every service while preserving existing HMAC token formats and Native AOT compatibility.

## 5. Consequences & Trade-offs

- **Positive Impact:** All backend services fail fast at startup with `OptionsValidationException` if `GATEWAY_SECRET` is missing or shorter than 32 characters; CTMS greenlight certification, eTMF completeness webhooks, and Payments OCR/diary tokens use the exact validated secret verified by `GatewayAuthenticationHandler`.
- **Negative Impact / Technical Debt:** Standalone unit tests instantiating application services or invoking endpoint delegates directly must supply `IOptions<GatewayAuthenticationOptions>` with a >= 32-character test secret marker.
- **Mitigation Strategy:** Enforced via constructor injection (non-nullable `IOptions<GatewayAuthenticationOptions>`), defense-in-depth length checks in token helpers, and dedicated host startup + divergent raw configuration tests.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` (`Cadence.Security`), `apps/ctms` (`Cadence.Apps.Ctms`), `apps/etmf` (`Cadence.Apps.Etmf`), `apps/payments` (`Cadence.Apps.Payments`), `tests/Cadence.UnitTests`, and `tests/Cadence.IntegrationTests`.
- **Verification Plan:** Validated by `GatewayAuthenticationOptionsBindingTests`, `StartupGreenlightEndpointsTests`, `EtmfCompletenessWebhookTests`, Payments unit suites, `Cadence.ArchitectureTests`, and `cadence check` quality sentinels.
