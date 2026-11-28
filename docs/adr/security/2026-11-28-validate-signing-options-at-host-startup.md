# ADR-SECURITY-20261128-validate-signing-options-at-host-startup: Validate signing options at host startup

- **Status:** Accepted
- **Date:** 2026-11-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

`CadenceSecurityServiceCollectionExtensions.AddCadenceSecurity` previously enforced `ValidateOnStart()` on `GatewayAuthenticationOptions` (`GATEWAY_SECRET`), while registering `AuditDigestEngine` as a lazy singleton backed by `AuditSecretKey.Resolve`. Services deployed without `AUDIT_LOG_SECRET_KEY` (or with a blank or short key) booted cleanly, passed `/healthz`, and failed only when the first GxP audit event was written (`PRD-SYS-001`). Similarly, `Cadence.Kernel.Host` (`apps/kernel-api/src/Program.cs`) registered `CommercialQuoteOptions` directly without `ValidateOnStart()`, allowing blank or under-length `CommercialQuote:SigningSecret` values to reach `ActivateSubscriptionHandler`, `GrantProductEntitlementHandler`, and `RevokeProductEntitlementHandler` mid-request.

## 2. Decision Drivers & Constraints

- **Fail-Closed Host Startup (`PRD-SYS-001`, `ADR-2256`):** Every service calling `AddCadenceSecurity()` and `Cadence.Kernel.Host` must reject missing, blank, or `< 16`-character signing keys during `IStartupValidator.Validate()` / `StartAsync()` before serving `/healthz` or accepting requests.
- **Secret Hygiene & Non-Exposure:** Validation failures and `ToString()` representations must identify only configuration key names (`AUDIT_LOG_SECRET_KEY`, `CommercialQuote:SigningSecret`) and never log, serialize, or expose configured secret material.
- **Defense in Depth & Entitlement Compatibility (`ADR-2604`):** Standalone `AuditSecretKey.Resolve`, `AuditDigestCalculator.ComputeAuditDigest`, `Cadence.Compliance.AuditDigestCalculator.ComputeAuditDigest`, and `SubscriptionQuoteToken.TryVerify` retain their runtime guards, while `CommercialQuote:SigningSecret` falls back to `Security:GatewaySecret` or `GATEWAY_SECRET` only when `CommercialQuote:SigningSecret` is `null`.

## 3. Options Considered

### Option 1: Options-Bound `ValidateOnStart()` for `CadenceSecurityOptions` and `CommercialQuoteOptions` (Selected)

- **Overview:** Bind `CadenceSecurityOptions` via `AddOptions<CadenceSecurityOptions>()` in `AddCadenceSecurity` from callback configuration, `IConfiguration["AUDIT_LOG_SECRET_KEY"]`, or `AuditSecretKey`, enforce `ValidateOnStart()` requiring a trimmed `AuditLogSecretKey` of at least 16 characters (`AuditSecretKey.MinimumSecretLength`), and construct `AuditDigestEngine` from `IOptions<CadenceSecurityOptions>`. Expose `SubscriptionQuoteToken.MinSecretLength` (`16`), bind `CommercialQuoteOptions` with `AddOptions<CommercialQuoteOptions>()` and `ValidateOnStart()` in `apps/kernel-api/src/Program.cs` using null-only fallback to `Security:GatewaySecret` or `GATEWAY_SECRET`, and resolve `IOptions<CommercialQuoteOptions>` across commercial command handlers via `AddKernelCommercialHandlers`. Wire `AUDIT_LOG_SECRET_KEY` to all Aspire AppHost services (including non-database hosts such as `designer`), `Cadence.OpenApiExporter`, and `SignedClientExtensions.UseTestGatewaySecret`, and coalesce multi-option `AggregateException` startup failures in `StartupValidationFactory`.
- **Pros:**
  - ✅ Rejects missing, blank, or short signing secrets at host boot before `/healthz` reports healthy (`PRD-SYS-001`).
  - ✅ Preserves standalone `AuditSecretKey.Resolve` digest-parity behavior and `SubscriptionQuoteToken.TryVerify` defense-in-depth checks.
  - ✅ Prevents explicit blank or short `CommercialQuote:SigningSecret` overrides from silently falling back to gateway secrets.
- **Cons:**
  - ❌ Requires non-database hosts and test harnesses calling `AddCadenceSecurity()` to supply a valid 16+ character `AUDIT_LOG_SECRET_KEY`.

### Option 2: Retain Lazy Singleton Resolution and Request-Time Validation

- **Overview:** Keep `AuditDigestEngine` resolving `AUDIT_LOG_SECRET_KEY` on first audit write and `CommercialCommandHandlers` rejecting invalid `CommercialQuoteOptions` when entitlement endpoints are called.
- **Pros:**
  - ✅ Requires no updates to non-database host wiring or test security helpers.
- **Cons:**
  - ❌ Misconfigured deployments pass `/healthz` and fail mid-transaction on regulated audit or commercial entitlement mutations.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Validating `CadenceSecurityOptions` and `CommercialQuoteOptions` via `AddOptions<TOptions>().Validate(...).ValidateOnStart()` aligns audit-chain and commercial-quote signing keys with `ADR-2256` fail-closed startup semantics (`PRD-SYS-001`) while keeping runtime calculators and quote-token verifiers fail-closed as defense in depth.

## 5. Consequences & Trade-offs

- **Positive Impact:** Operators catch missing, blank, or under-length `AUDIT_LOG_SECRET_KEY` and `CommercialQuote:SigningSecret` settings during deployment startup rather than mid-request, with zero secret values leaked in exception messages or RFC 9457 responses.
- **Negative Impact / Technical Debt:** Every host invoking `AddCadenceSecurity()` (including stateless services such as `designer` and offline OpenAPI export hosts) must configure `AUDIT_LOG_SECRET_KEY` of at least 16 characters.
- **Mitigation Strategy:** Provide `AUDIT_LOG_SECRET_KEY` to all services in `apps/apphost/src/Program.cs`, supply a default test audit key in `UseTestGatewaySecret()`, configure an ephemeral export key in `Cadence.OpenApiExporter`, and document the 16-character minimum in `.env.example` and `docs/ENVIRONMENT_VARIABLES.md`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security`, `packages/kernel`, `packages/testing`, `packages/tooling_core`, `apps/kernel-api`, `apps/apphost`, `.env.example`, `docs/`.
- **Verification Plan:** Verified via `CadenceSecurityUnitTests`, `CommercialAndOnboardingUnitTests`, `AppHostModelTests`, `ExecutionAuthenticationTests`, `Cadence.ArchitectureTests`, `Cadence.Kernel.ConsumerTests`, and `cadence check` quality sentinels.
