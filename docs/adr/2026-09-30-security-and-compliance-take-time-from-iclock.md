# ADR-2512: Security and Compliance Take Time From IClock

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (audit entries carry a trustworthy timestamp), kernel spec K-TIME-3

---

## 1. Context & Problem Statement

K-TIME-3 says clinical code takes time from an injected clock, never from `DateTime.UtcNow`. After the audit
interceptor moved to `IClock` (#4784), `packages/security` and `packages/compliance` were the last packages that read
the wall clock:

- `ReplayCache` checked request timestamps against `DateTimeOffset.UtcNow`.
- `AuditDigestEngine` stamped digest records with `DateTime.UtcNow`, and `AuditLogRecord.Timestamp` defaulted to it.
- `GxPSigner`, `Pkcs7Signer` and `RtmGeneratorService` dated signatures, certificates and the RTM from the wall clock,
  and `ComplianceAuditLogRecord.Timestamp` defaulted to it.

A default of "now" on a record also hides a writer that forgot to supply the time.

## 2. Decision Drivers & Constraints

- **One clock:** audit, replay and signing times come from the same `IClock` the rest of the service uses.
- **Enforced, not reviewed:** the packages opt into the `BannedSymbols.txt` ban so a regression fails the build.
- **No breaking change for callers:** hosts and the CLI keep working without new wiring.

## 3. Options Considered

### Option 1: Required `IClock` on every entry point

- **Cons:**
  - ❌ Every caller of the signing helpers and the replay cache changes, including CLI commands with no container.

### Option 2: Optional `IClock` defaulting to `SystemClock.Instance`, and the ban (Selected)

- **Overview:**
  - `ReplayCache`, `AuditDigestEngine`, `GxPSigner.SignGxpMarkdown`, `Pkcs7Signer.GenerateSigningCredentials` and
    `RtmGeneratorService.GenerateRtm` take an optional `IClock`. `AddCadenceSecurity` passes the registered clock.
  - `AuditLogRecord.Timestamp` is `required`; `ComplianceAuditLogRecord.Timestamp` has no default.
  - Both projects reference `Cadence.Kernel.Abstractions` and set `CadenceBanWallClock`.
- **Pros:**
  - ✅ Tests can fix the time with `FakeClock`; hosts use their registered clock.
  - ✅ The analyzer keeps the wall clock out of both packages.
- **Cons:**
  - ❌ A caller that passes no clock still gets the system clock, through `SystemClock` rather than a direct read.

## 4. Decision Outcome

- **Chosen Option:** Option 2.
- **Justification:** It removes the last wall-clock reads from `packages/` and lets the ban guard them, without
  touching callers that have no container.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Replay windows, audit digests, signatures and the RTM date are testable with a fixed clock.
  - `grep` for wall-clock reads in `apps/` and `packages/` finds none.
- **Negative Impact / Technical Debt:**
  - Code that builds an `AuditLogRecord` must now set `Timestamp`.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/security/src/Authentication/ReplayCache.cs`
  - `packages/security/src/Audit/AuditDigestEngine.cs`, `AuditLogRecord.cs`
  - `packages/compliance/src/GxPSigner.cs`, `Pkcs7Signer.cs`, `RtmGeneratorService.cs`, `ComplianceAuditLogRecord.cs`
- **Verification:** `tests/Cadence.UnitTests/Packages/SecurityAndComplianceClockTests.cs` checks each entry point
  against a `FakeClock`; the build fails on a wall-clock read in either package.
