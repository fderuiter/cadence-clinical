# ADR-0082: Native C# Security Package Formatting and Query Style Sentinels Compliance

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @google-labs-stitch[bot]
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053

---

## 1. Context & Problem Statement

The native C# `Cadence.Security` package in `packages/security/` required formatting alignment with `.editorconfig` rules and query style linter compliance (eliminating explicit `== true` boolean comparison operators in favor of null-coalescing expressions). Modifying files within `packages/security/` triggers Cadence CLI ADRSentinel check Gate 6.

## 2. Decision Drivers & Constraints

- **Driver 1:** Compliance with C# 14 / .NET 10 `.editorconfig` formatting and Roslyn analyzer sentinels (`dotnet format Cadence.sln`).
- **Driver 2:** Automated pre-commit quality gate 20 (query-style-linter) enforcement prohibiting explicit `== true` evaluations.
- **Requirement:** PRD-SYS-053 (21 CFR Part 11 Audit Trail Integrity and Security Boundaries).

## 3. Options Considered

### Option 1: Disable Query Style Sentinel for Security Package

- **Overview:** Suppress the query-style linter for `packages/security/`.
- **Pros:**
  - ✅ Quick workaround.
- **Cons:**
  - ❌ Violates Cadence architectural standards and quality sentinels.

### Option 2: Refactor Boolean Expressions to Use Null-Coalescing and Add ADR (Selected)

- **Overview:** Refactor `principal?.UnblindedAccess == true` and `context.User.Identity?.IsAuthenticated == true` to null-coalesced boolean expressions (`?? false`), auto-format `packages/security/`, and record this ADR.
- **Pros:**
  - ✅ Fully complies with all 21 Cadence CLI sentinels.
  - ✅ Preserves type-safe null safety without explicit boolean comparisons.
- **Cons:**
  - ❌ None.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Aligns `packages/security` with .NET 10 C# 14 coding standards and passes all quality gates cleanly.

## 5. Consequences & Trade-offs

- **Positive Impact:** All 21 quality sentinels and `dotnet format` checks pass with zero errors and zero warnings.
- **Negative Impact:** None.
- **Mitigation Strategy:** Automated enforcement in `dotnet cadence check`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security/`
- **Verification Plan:** Validated via `dotnet format Cadence.sln --verify-no-changes` and `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check`.
