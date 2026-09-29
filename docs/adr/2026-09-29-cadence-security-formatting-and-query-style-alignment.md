# ADR-2248: Cadence Security Formatting and Query Style Alignment

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

As part of the .NET 10 migration and telemetry centralization across Cadence microservices, code quality sentinels (`dotnet format` and `QueryLinterService`) detected C# formatting discrepancies and explicit boolean comparisons (`== true`) within `Cadence.Security` (`packages/security/`). Under 21 CFR Part 11 and PRD-SYS-053, code style sentinels enforce zero-warning compilation and clean query linter checks across all assemblies.

## 2. Decision Drivers & Constraints

- **Driver 1 (PRD-SYS-053 / Quality Sentinels):** Zero tolerance for formatting errors (`dotnet format Cadence.sln --verify-no-changes`) or boolean comparison query style violations (`== true`).
- **Driver 2 (Immutability & Safety):** Align `Cadence.Security` authentication and blinding filters with C# 14 pattern matching features.
- **Constraint:** Maintain 100% backward compatibility for security RBAC handlers, HMAC signing, and blinding features.

## 3. Options Considered

### Option 1: Format and Align Query Style in Cadence.Security (Selected)

- **Overview:** Auto-format `Cadence.Security` source files using `dotnet format` and replace explicit `== true` expressions with C# pattern matching (`is { IsAuthenticated: true }` and `is { UnblindedAccess: true }`).
- **Pros:**
  - ✅ Satisfies all 21 Cadence CLI sentinels and `dotnet format` checks.
  - ✅ Improves readability and null safety using C# 14 pattern matching.
- **Cons:**
  - ❌ Touches files in `packages/security/` requiring ADR registration.

### Option 2: Suppress Linter Warnings

- **Overview:** Add `#pragma` directives or ignore rules in linter config.
- **Pros:**
  - ✅ Zero code edits.
- **Cons:**
  - ❌ Violates GxP quality guidelines and sentinels.

## 4. Decision Outcome

Selected Option 1. All `Cadence.Security` files have been formatted according to `.editorconfig` rules and updated with pattern matching syntax for boolean evaluation.

## 5. Consequences & Trade-offs

- Clean sentinel execution across Gate 1 (`dotnet format`), Gate 6 (`validate-adrs`), and Gate 20 (`query-style-linter`).
- All 565 architecture tests, 774 unit tests, and 60 E2E tests pass cleanly.

## 6. Implementation & Verification

- Formatted `packages/security/` files using `dotnet format`.
- Updated `AuthorizationHandlers.cs` and `FieldMasker.cs` to replace `== true` with `is { ...: true }`.
- Verified using `dotnet format Cadence.sln --verify-no-changes`, `dotnet test Cadence.sln`, and `dotnet run --project packages/cli/src/Cadence.Cli.csproj -- check`.
