# ADR-2818: Clinical and Gateway Endpoints Take Time From IClock and Ban TimeProvider

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (audit entries carry a trustworthy timestamp), kernel spec K-TIME-3

---

## 1. Context & Problem Statement

Kernel rule K-TIME-3 and [ADR-2512](2026-09-30-security-and-compliance-take-time-from-iclock.md) require clinical, security, and compliance code to read timestamps from `Cadence.Kernel.Time.IClock` rather than directly from the wall clock. However, `BannedSymbols.txt` only banned `System.DateTime.Now`/`UtcNow` and `System.DateTimeOffset.Now`/`UtcNow`, leaving `System.TimeProvider.System` and `System.TimeProvider.GetUtcNow` unguarded by Roslyn analyzer rule `RS0030`.

As a result, clinical and gateway code paths read wall-clock timestamps via `TimeProvider` while audit and replay components in the same request pipeline read `IClock`:

- `apps/econsent` (`EconsentEndpoints`), `apps/knowledge` (`TrainingEndpoints`), `apps/intelligence` (`BiometricEndpoints`), and `apps/gateway` (`SignatureTokenEndpoints` and `GatewayIdentityForwarding`) took `TimeProvider` instead of `IClock`.
- `apps/knowledge/src/Program.cs` and `apps/gateway/src/Program.cs` registered `TimeProvider.System`, while `apps/econsent/src/Program.cs` relied on implicit `IClock` registration.
- `BiometricLivenessTokenService` defaulted optional `DateTimeOffset? now` parameters to `TimeProvider.System.GetUtcNow()`.
- Unit tests (`TrainingEndpointsTests`, `EconsentEndpointsTests`, `BiometricLivenessTests`) maintained ad-hoc `TestTimeProvider` subclasses or passed `TimeProvider.System` alongside `Cadence.Testing.Time.FakeClock`.

## 2. Decision Drivers & Constraints

- **Single Clock Source (PRD-SYS-001, K-TIME-3):** Endpoint handlers, identity-forwarding transforms, token helpers, and audit interceptors must derive timestamps from the same `IClock`.
- **Compile-Time Enforcement:** `BannedSymbols.txt` must ban `P:System.TimeProvider.System` and `M:System.TimeProvider.GetUtcNow` so `RS0030` (`TreatWarningsAsErrors=true`) catches regressions across all projects.
- **Framework Interop Boundaries:** ASP.NET Core `AuthenticationHandler<TOptions>.TimeProvider` (`GatewayAuthenticationHandler.IsFresh`) and the `SystemClock` implementation itself require narrow, justified `#pragma warning disable RS0030` pragmas.

## 3. Options Considered

### Option 1: Migrate Endpoints and Token Fallbacks to `IClock` and Ban `TimeProvider` Reads (Selected)

- **Overview:** Inject `IClock` in `EconsentEndpoints`, `TrainingEndpoints`, `BiometricEndpoints`, `SignatureTokenEndpoints`, and `GatewayIdentityForwarding`; register `SystemClock.Instance` as `IClock` in `econsent`, `knowledge`, and `gateway` `Program.cs`; default `BiometricLivenessTokenService` to `SystemClock.Instance.UtcNow`; ban `P:System.TimeProvider.System` and `M:System.TimeProvider.GetUtcNow` in `BannedSymbols.txt` with narrow `RS0030` suppressions at `SystemClock`, `GatewayAuthenticationHandler.IsFresh`, and `ClockTests.SystemClockReturnsCurrentUtcInstant`; and migrate unit tests to `FakeClock`.
- **Pros:**
  - ✅ Satisfies PRD-SYS-001 and K-TIME-3 with one clock abstraction across clinical endpoints, gateway signing, security tokens, and audit records.
  - ✅ Closes the `TimeProvider` loophole in `BannedSymbols.txt` and enforces `BannedClockSymbols` via `ClockTests`.
  - ✅ Eliminates bespoke `TestTimeProvider` test doubles in favor of `FakeClock`.
- **Cons:**
  - ❌ Requires explicit justified `RS0030` pragmas at the three framework/adapter boundary sites that legitimately touch `TimeProvider`.

### Option 2: Standardize on `System.TimeProvider` Instead of `IClock`

- **Overview:** Replace `IClock` across the kernel, security, compliance, and audit packages with BCL `TimeProvider`.
- **Pros:**
  - ✅ Aligns directly with ASP.NET Core authentication handler primitives.
- **Cons:**
  - ❌ Reverses K-TIME-3 and ADR-2512 across the kernel, audit interceptor, replay cache, and GxP signing packages without adding domain capability.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Completing the migration onto `Cadence.Kernel.Time.IClock` and banning `TimeProvider.System` and `TimeProvider.GetUtcNow` enforces K-TIME-3 and PRD-SYS-001 uniformly at compile time with minimal surface change.

## 5. Consequences & Trade-offs

- **Positive Impact:** All eConsent, Knowledge, Intelligence, and Gateway timestamps are deterministic under `FakeClock`, and `RS0030` fails the build on any unguarded `DateTime`, `DateTimeOffset`, or `TimeProvider` wall-clock read.
- **Negative Impact / Technical Debt:** Three boundary sites (`SystemClock`, `GatewayAuthenticationHandler`, and `ClockTests`) carry `#pragma warning disable RS0030` suppressions.
- **Mitigation Strategy:** `WarningSuppressionPolicyTests` requires every `#pragma warning disable` line to state an inline justification, and `ClockTests.BannedSymbolsBanWallClockAndKernelIsWired` verifies `BannedSymbols.txt` entries against reflection metadata.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/econsent`, `apps/knowledge`, `apps/intelligence`, `apps/gateway`, `packages/kernel`, `packages/security`, `BannedSymbols.txt`, `docs/kernel/CLOCK_MIGRATION_BACKLOG.md`, `tests/Cadence.UnitTests`.
- **Verification Plan:** Verified via `dotnet build Cadence.sln` (`RS0030` clean across all projects), `ClockTests`, `EconsentEndpointsTests`, `TrainingEndpointsTests`, `BiometricLivenessTests`, `WarningSuppressionPolicyTests`, and `cadence check`.
